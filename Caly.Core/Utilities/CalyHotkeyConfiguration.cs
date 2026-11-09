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
using System.Linq;
using Avalonia;
using Avalonia.Input;

namespace Caly.Core.Utilities;

internal static class CalyHotkeyConfiguration
{
    private static readonly KeyModifiers CommandModifiers;

    static CalyHotkeyConfiguration()
    {
        if (Application.Current?.PlatformSettings is null)
        {
            throw new NullReferenceException("PlatformSettings is null.");
        }

        CommandModifiers = Application.Current.PlatformSettings.HotkeyConfiguration.CommandModifiers;
    }

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Copy' action.
    /// </summary>
    public static KeyGesture? CopyGesture => Application.Current?.PlatformSettings?.HotkeyConfiguration.Copy.FirstOrDefault();

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Open File' action.
    /// </summary>
    public static KeyGesture OpenFileGesture => new KeyGesture(Key.O, CommandModifiers);

    /// <summary>
    /// Gets the platform-specific <see cref="KeyGesture"/> for the 'Settings' action (Cmd+, on macOS).
    /// </summary>
    public static KeyGesture SettingsGesture => new KeyGesture(Key.OemComma, CommandModifiers);

    /// <summary>
    /// Gets the platform-specific <see cref="KeyGesture"/> for the 'Undo' action.
    /// </summary>
    public static KeyGesture? UndoGesture => Application.Current?.PlatformSettings?.HotkeyConfiguration.Undo.FirstOrDefault();

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Find Next' action.
    /// </summary>
    public static KeyGesture FindNextGesture => new KeyGesture(Key.G, CommandModifiers);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Find Previous' action.
    /// </summary>
    public static KeyGesture FindPreviousGesture => new KeyGesture(Key.G, CommandModifiers | KeyModifiers.Shift);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for toggling the document sidebar.
    /// </summary>
    public static KeyGesture ToggleSidebarGesture => new KeyGesture(Key.S, CommandModifiers | KeyModifiers.Control);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Zoom To Page Width' action.
    /// </summary>
    public static KeyGesture DocumentZoomToFitGesture => new KeyGesture(Key.D0, CommandModifiers);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Print Document' action.
    /// </summary>
    public static KeyGesture DocumentPrintGesture => new KeyGesture(Key.P, CommandModifiers);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Go to Specific Page' action.
    /// </summary>
    /// <remarks>
    /// Uses <c>Cmd+J</c> (<c>Ctrl+J</c> elsewhere): <c>Cmd+G</c> is reserved by the platform
    /// for 'Find Next' (see <see cref="FindNextGesture"/>).
    /// </remarks>
    public static KeyGesture DocumentGoToGesture => new KeyGesture(Key.J, CommandModifiers);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Zoom In' action.
    /// </summary>
    public static KeyGesture DocumentZoomInGesture => new KeyGesture(Key.OemPlus, CommandModifiers);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Zoom Out' action.
    /// </summary>
    public static KeyGesture DocumentZoomOutGesture => new KeyGesture(Key.OemMinus, CommandModifiers);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Search in Document' action.
    /// </summary>
    public static KeyGesture DocumentSearchGesture => new KeyGesture(Key.F, CommandModifiers);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Close Document' action.
    /// </summary>
    /// <remarks>
    /// <c>Cmd+W</c> is the platform standard for closing a document / tab / window.
    /// </remarks>
    public static KeyGesture DocumentCloseGesture => new KeyGesture(Key.W, CommandModifiers);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Go To Next Page' action.
    /// </summary>
    public static KeyGesture DocumentNextGesture => new KeyGesture(Key.PageDown, CommandModifiers);

    /// <summary>
    /// Gets a platform-specific <see cref="KeyGesture"/> for the 'Go To Previous Page' action.
    /// </summary>
    public static KeyGesture DocumentPreviousGesture => new KeyGesture(Key.PageUp, CommandModifiers);
}
