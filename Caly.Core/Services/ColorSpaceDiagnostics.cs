// Copyright (c) 2025 BobLd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation on the rights
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
using UglyToad.PdfPig.Graphics.Colors;

namespace Caly.Core.Services;

/// <summary>
/// Fine-grained classification of a PDF color space, independent of any specific renderer.
/// This is the "identify the color space type" step of the color pipeline: once a page's color
/// spaces are known, each can be routed through the correct conversion path and given a fallback.
/// </summary>
public enum PdfColorSpaceFamily
{
    /// <summary>Could not be determined.</summary>
    Unknown,

    /// <summary>DeviceGray - single intensity channel.</summary>
    DeviceGray,

    /// <summary>DeviceRGB - device red/green/blue.</summary>
    DeviceRGB,

    /// <summary>DeviceCMYK - device cyan/magenta/yellow/black.</summary>
    DeviceCMYK,

    /// <summary>CalGray - CIE calibrated single channel.</summary>
    CalGray,

    /// <summary>CalRGB - CIE calibrated ABC (red/green/blue).</summary>
    CalRGB,

    /// <summary>Lab - CIE L*a*b*.</summary>
    Lab,

    /// <summary>ICCBased - an embedded ICC profile defines the space.</summary>
    ICCBased,

    /// <summary>Separation - a single spot color with a tint transform over an alternate space.</summary>
    Separation,

    /// <summary>DeviceN - multiple components with a tint transform over an alternate space.</summary>
    DeviceN,

    /// <summary>Indexed - a palette over a base color space.</summary>
    Indexed,

    /// <summary>Pattern - procedural/tiling/stuffing patterns.</summary>
    Pattern,

    /// <summary>The color space is not understood by the current renderer.</summary>
    Unsupported,
}

/// <summary>
/// A single color space used by a page (an image XObject, a page resource, or the content stream),
/// classified for rendering risk. Value-object; safe to share across threads once constructed.
/// </summary>
public sealed class PageColorSpaceInfo
{
    public PageColorSpaceInfo(string source, PdfColorSpaceFamily family, bool isAtRisk, PdfColorSpaceFamily? underlyingFamily, string note)
    {
        Source = source;
        Family = family;
        IsAtRisk = isAtRisk;
        UnderlyingFamily = underlyingFamily;
        Note = note;
    }

    /// <summary>Where this color space came from, e.g. <c>"Image 1200x800"</c>, <c>"Resource /CS1"</c>, <c>"ContentStream (non-stroking)"</c>.</summary>
    public string Source { get; }

    /// <summary>The color space family.</summary>
    public PdfColorSpaceFamily Family { get; }

    /// <summary>
    /// True when the current renderer cannot reproduce this color space faithfully and instead falls
    /// back to an approximation. The canonical case is <see cref="PdfColorSpaceFamily.ICCBased" />: the embedded ICC
    /// profile is not applied, so the color values are re-interpreted as the (usually sRGB/CMYK)
    /// alternate device space. Such pages may render with incorrect color in this app while looking
    /// correct in a color-managed viewer such as macOS Preview.
    /// </summary>
    public bool IsAtRisk { get; }

    /// <summary>
    /// For composite spaces (Indexed / Separation / DeviceN / ICCBased), the family of the space that
    /// is actually used to convert the components (the base or alternate space). Null for simple spaces.
    /// </summary>
    public PdfColorSpaceFamily? UnderlyingFamily { get; }

    /// <summary>Human-readable explanation of how (and how reliably) this color space is rendered.</summary>
    public string Note { get; }

    public override string ToString()
    {
        string risk = IsAtRisk ? " [AT-RISK]" : string.Empty;
        return $"{Source}: {Family}{risk} -> {Note}";
    }
}

/// <summary>
/// Pure, renderer-agnostic classification of PDF color spaces. The core rules
/// (<see cref="IsAtRisk"/>, <see cref="Describe"/>) operate only on <see cref="PdfColorSpaceFamily"/> values so they can be
/// unit-tested without constructing any renderer-specific color space objects.
/// <para>
/// The single <see cref="MapFamily"/> method bridges to a specific renderer's color-space enum; the thin
/// <c>ClassifyDetails</c> adapter (see <c>PdfPigDocumentService</c>) is the only piece that touches
/// renderer objects.
/// </para>
/// </summary>
public static class ColorSpaceClassifier
{
    /// <summary>Map a renderer color-space enum value to our fine-grained family.</summary>
    public static PdfColorSpaceFamily MapFamily(ColorSpace cs) => cs switch
    {
        ColorSpace.DeviceGray => PdfColorSpaceFamily.DeviceGray,
        ColorSpace.DeviceRGB => PdfColorSpaceFamily.DeviceRGB,
        ColorSpace.DeviceCMYK => PdfColorSpaceFamily.DeviceCMYK,
        ColorSpace.CalGray => PdfColorSpaceFamily.CalGray,
        ColorSpace.CalRGB => PdfColorSpaceFamily.CalRGB,
        ColorSpace.Lab => PdfColorSpaceFamily.Lab,
        ColorSpace.ICCBased => PdfColorSpaceFamily.ICCBased,
        ColorSpace.Separation => PdfColorSpaceFamily.Separation,
        ColorSpace.DeviceN => PdfColorSpaceFamily.DeviceN,
        ColorSpace.Indexed => PdfColorSpaceFamily.Indexed,
        ColorSpace.Pattern => PdfColorSpaceFamily.Pattern,
        _ => PdfColorSpaceFamily.Unknown,
    };

    /// <summary>
    /// Core at-risk rule. A color space is "at risk" (rendered approximately rather than faithfully) when
    /// the renderer must fall back to a device/alternate space it cannot fully honor:
    /// <list type="bullet">
    /// <item><description><see cref="PdfColorSpaceFamily.ICCBased"/> - the embedded ICC profile is not applied; the renderer
    /// uses the alternate device space, so colors differ from a color-managed viewer.</description></item>
    /// <item><description><see cref="PdfColorSpaceFamily.Unsupported"/> - the space is not understood at all.</description></item>
    /// <item><description><see cref="PdfColorSpaceFamily.Indexed"/> - at risk only if its base space is itself at risk.</description></item>
    /// <item><description><see cref="PdfColorSpaceFamily.Separation"/> / <see cref="PdfColorSpaceFamily.DeviceN"/> - at risk only if their
    /// alternate space is itself at risk (a device alternate is fine).</description></item>
    /// </list>
    /// DeviceGray/DeviceRGB/DeviceCMYK, CalGray/CalRGB/Lab and Pattern are rendered faithfully.
    /// </summary>
    /// <param name="family">The color space family being classified.</param>
    /// <param name="underlyingFamily">For composite spaces, the family of the base/alternate space; ignored otherwise.</param>
    public static bool IsAtRisk(PdfColorSpaceFamily family, PdfColorSpaceFamily? underlyingFamily = null)
    {
        switch (family)
        {
            case PdfColorSpaceFamily.ICCBased:
            case PdfColorSpaceFamily.Unsupported:
                return true;
            case PdfColorSpaceFamily.Indexed:
            case PdfColorSpaceFamily.Separation:
            case PdfColorSpaceFamily.DeviceN:
                return IsAtRisk(underlyingFamily ?? PdfColorSpaceFamily.Unknown);
            default:
                return false;
        }
    }

    /// <summary>Produce a human-readable note explaining how the color space is (or isn't) rendered faithfully.</summary>
    public static string Describe(PdfColorSpaceFamily family, PdfColorSpaceFamily? underlyingFamily = null)
    {
        switch (family)
        {
            case PdfColorSpaceFamily.ICCBased:
                return "Embedded ICC profile is NOT applied by this renderer; the color values are re-interpreted as the "
                       + FamilyName(underlyingFamily) + " alternate space. Colors may differ from color-managed viewers (e.g. Preview).";
            case PdfColorSpaceFamily.Indexed:
                return "Palette (Indexed) color space; palette entries are interpreted as " + FamilyName(underlyingFamily) + ".";
            case PdfColorSpaceFamily.Separation:
                return "Separation (spot color) space; tint values are mapped through the " + FamilyName(underlyingFamily) + " alternate space.";
            case PdfColorSpaceFamily.DeviceN:
                return "DeviceN (multi-channel) space; components are mapped through the " + FamilyName(underlyingFamily) + " alternate space.";
            case PdfColorSpaceFamily.DeviceCMYK:
                return "CMYK is converted to sRGB with an approximate polynomial (not a color-managed ICC transform).";
            case PdfColorSpaceFamily.CalGray:
            case PdfColorSpaceFamily.CalRGB:
            case PdfColorSpaceFamily.Lab:
                return "CIE-based space; converted to sRGB via the CIE transformer (white point + gamma), then clamped to [0,1].";
            case PdfColorSpaceFamily.Unsupported:
                return "Unsupported color space; rendered with a fallback.";
            default:
                return "Rendered directly.";
        }
    }

    private static string FamilyName(PdfColorSpaceFamily? f) => f switch
    {
        PdfColorSpaceFamily.DeviceCMYK => "CMYK",
        PdfColorSpaceFamily.DeviceGray => "grayscale",
        PdfColorSpaceFamily.DeviceRGB => "sRGB",
        PdfColorSpaceFamily.ICCBased => "ICC",
        null => "device",
        _ => f?.ToString()?.ToLowerInvariant() ?? "device",
    };
}