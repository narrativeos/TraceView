// Copyright (c) 2025 BobLd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace Caly.Core.Utilities;

/// <summary>
/// Bridges the AppKit application delegate callbacks that the Avalonia macOS backend implements
/// natively but never surfaces to managed code:
/// <list type="bullet">
/// <item><description><c>application:openFile:</c>, <c>application:openFiles:</c> and
/// <c>application:openURLs:</c> - opening a document from the Finder (double click, "Open With")</description></item>
/// <item><description><c>applicationShouldHandleReopen:hasVisibleWindows:</c> - clicking the Dock icon</description></item>
/// </list>
/// The methods are replaced on the live <c>NSApplication</c> delegate with managed trampolines which call
/// the original implementation first so that Avalonia keeps its own behaviour.
/// This is best effort: any failure is swallowed so that the application still starts.
/// </summary>
internal static unsafe class MacAppleEventBridge
{
    private const string LibObjC = "/usr/lib/libobjc.dylib";

    private static readonly object Sync = new();

    private static readonly IntPtr OpenFileImplementation =
        (nint)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, byte>)&OpenFile;
    private static readonly IntPtr OpenFilesImplementation =
        (nint)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, void>)&OpenFiles;
    private static readonly IntPtr OpenUrlsImplementation =
        (nint)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, void>)&OpenUrls;
    private static readonly IntPtr ReopenImplementation =
        (nint)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte, byte>)&Reopen;

    /// <summary>The implementation of 'applicationShouldHandleReopen:hasVisibleWindows:' we replaced.</summary>
    private static IntPtr _reopenPrevious;

    private static Action<string>? _openFile;
    private static Action? _reopen;

    private static bool _installed;

    /// <summary>
    /// Installs the handlers. Call once the Avalonia application has been initialised, that is after
    /// AppKit has created the application delegate.
    /// </summary>
    /// <param name="openFile">Called with a local file path for every document handed over by the Finder.</param>
    /// <param name="reopen">Called when the Dock icon is clicked.</param>
    public static void Install(Action<string> openFile, Action reopen)
    {
        ArgumentNullException.ThrowIfNull(openFile);
        ArgumentNullException.ThrowIfNull(reopen);

        lock (Sync)
        {
            if (_installed)
            {
                return;
            }

            try
            {
                var application = objc_msgSend(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
                if (application == IntPtr.Zero)
                {
                    return;
                }

                var appDelegate = objc_msgSend(application, sel_registerName("delegate"));
                if (appDelegate == IntPtr.Zero)
                {
                    return;
                }

                var cls = object_getClass(appDelegate);
                if (cls == IntPtr.Zero)
                {
                    return;
                }

                _openFile = openFile;
                _reopen = reopen;

                // The document is opened by this class alone, Avalonia has no managed equivalent.
                Replace(cls, "application:openFile:", OpenFileImplementation, "c@:@");
                Replace(cls, "application:openFiles:", OpenFilesImplementation, "v@:@");
                Replace(cls, "application:openURLs:", OpenUrlsImplementation, "v@:@");

                _reopenPrevious = Replace(cls, "applicationShouldHandleReopen:hasVisibleWindows:",
                    ReopenImplementation, "c@:@c");

                _installed = true;
            }
            catch (Exception)
            {
                // Never prevent the application from starting because of an interop problem.
            }
        }
    }

    /// <summary>
    /// Replaces (or adds) a selector on the delegate class and returns the implementation it had before.
    /// </summary>
    private static IntPtr Replace(IntPtr cls, string selectorName, IntPtr implementation, string signature)
    {
        var selector = sel_registerName(selectorName);

        IntPtr previous = IntPtr.Zero;

        var method = class_getInstanceMethod(cls, selector);
        if (method != IntPtr.Zero)
        {
            previous = method_getImplementation(method);
        }

        class_replaceMethod(cls, selector, implementation, signature);

        return previous;
    }

    /// <summary>
    /// Hands every file path held by a callback argument to <see cref="_openFile"/> on the UI thread.
    /// The argument is a single <c>NSString</c>, an <c>NSArray</c> of <c>NSString</c> or an <c>NSArray</c> of <c>NSURL</c>.
    /// </summary>
    private static void Dispatch(IntPtr argument, bool isCollection, bool isUrl)
    {
        var openFile = _openFile;
        if (openFile is null || argument == IntPtr.Zero)
        {
            return;
        }

        // Sending a selector an object does not answer would raise an Objective-C exception which
        // cannot be caught here, so every message below is guarded with 'respondsToSelector:'.
        if (isCollection && !RespondsTo(argument, "count"))
        {
            isCollection = false;
        }

        foreach (var path in EnumeratePaths(argument, isCollection, isUrl))
        {
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            var local = path;
            Dispatcher.UIThread.Post(() => openFile(local), DispatcherPriority.Normal);
        }
    }

    private static IEnumerable<string?> EnumeratePaths(IntPtr argument, bool isCollection, bool isUrl)
    {
        if (!isCollection)
        {
            yield return ReadPath(argument, isUrl);
            yield break;
        }

        var count = objc_msgSend_nuint(argument, sel_registerName("count"));
        var objectAtIndex = sel_registerName("objectAtIndexedSubscript:");

        for (nuint i = 0; i < count; i++)
        {
            yield return ReadPath(objc_msgSend_nuint_ret(argument, objectAtIndex, i), isUrl);
        }
    }

    private static string? ReadPath(IntPtr value, bool isUrl)
    {
        if (value == IntPtr.Zero)
        {
            return null;
        }

        if (isUrl && RespondsTo(value, "path"))
        {
            // NSURL -> local path.
            var urlPath = objc_msgSend(value, sel_registerName("path"));
            if (urlPath != IntPtr.Zero && ReadString(urlPath) is { } path)
            {
                return path;
            }
        }

        return ReadString(value);
    }

    private static bool RespondsTo(IntPtr value, string selectorName) =>
        value != IntPtr.Zero &&
        objc_msgSend_bool(value, sel_registerName("respondsToSelector:"),
            sel_registerName(selectorName)) != 0;

    private static string? ReadString(IntPtr value)
    {
        if (value == IntPtr.Zero ||
            objc_msgSend_bool(value, sel_registerName("isKindOfClass:"), objc_getClass("NSString")) == 0)
        {
            return null;
        }

        var utf8 = objc_msgSend(value, sel_registerName("UTF8String"));
        return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
    }

    [UnmanagedCallersOnly]
    private static byte OpenFile(IntPtr self, IntPtr command, IntPtr application, IntPtr path)
    {
        try
        {
            Dispatch(path, false, false);
        }
        catch (Exception)
        {
            // A callback must never let an exception cross back into AppKit.
        }

        // Report the document as handled - Avalonia has no managed equivalent of this callback.
        return 1;
    }

    [UnmanagedCallersOnly]
    private static void OpenFiles(IntPtr self, IntPtr command, IntPtr application, IntPtr files)
    {
        try
        {
            Dispatch(files, true, false);
        }
        catch (Exception)
        {
            // No op.
        }
    }

    [UnmanagedCallersOnly]
    private static void OpenUrls(IntPtr self, IntPtr command, IntPtr application, IntPtr urls)
    {
        try
        {
            Dispatch(urls, true, true);
        }
        catch (Exception)
        {
            // No op.
        }
    }

    [UnmanagedCallersOnly]
    private static byte Reopen(IntPtr self, IntPtr command, IntPtr application, byte hasVisibleWindows)
    {
        var result = (byte)1;

        try
        {
            if (_reopenPrevious != IntPtr.Zero)
            {
                result = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte, byte>)
                    _reopenPrevious)(self, command, application, hasVisibleWindows);
            }

            var reopen = _reopen;
            if (reopen is not null)
            {
                Dispatcher.UIThread.Post(reopen, DispatcherPriority.Normal);
            }
        }
        catch (Exception)
        {
            // No op.
        }

        return result;
    }

    [DllImport(LibObjC, EntryPoint = "objc_getClass")]
    private static extern IntPtr objc_getClass(string className);

    [DllImport(LibObjC, EntryPoint = "sel_registerName")]
    private static extern IntPtr sel_registerName(string selectorName);

    [DllImport(LibObjC, EntryPoint = "object_getClass")]
    private static extern IntPtr object_getClass(IntPtr instance);

    [DllImport(LibObjC, EntryPoint = "class_getInstanceMethod")]
    private static extern IntPtr class_getInstanceMethod(IntPtr cls, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "method_getImplementation")]
    private static extern IntPtr method_getImplementation(IntPtr method);

    [DllImport(LibObjC, EntryPoint = "class_replaceMethod")]
    private static extern IntPtr class_replaceMethod(IntPtr cls, IntPtr selector, IntPtr implementation,
        string types);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern byte objc_msgSend_bool(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern nuint objc_msgSend_nuint(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_nuint_ret(IntPtr receiver, IntPtr selector, nuint index);
}
