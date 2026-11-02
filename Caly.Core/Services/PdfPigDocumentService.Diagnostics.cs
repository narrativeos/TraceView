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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Graphics.Colors;

namespace Caly.Core.Services;

/// <summary>
/// Color-space inspection for a page: identifies the color space of each image on the page and
/// flags the ones the renderer can only approximate (see <see cref="ColorSpaceClassifier"/>).
/// </summary>
internal partial class PdfPigDocumentService
{
    /// <summary>
    /// Identify the color spaces used by a page's images and classify each for rendering risk. This is the
    /// "identify the color space type" step of the color pipeline: each space is mapped to a
    /// <see cref="PdfColorSpaceFamily"/> and flagged when the renderer can only approximate it (e.g. ICCBased).
    /// Returns an empty list for a page with no images. Safe to call from any thread (uses the document lock).
    /// </summary>
    public async Task<IReadOnlyList<PageColorSpaceInfo>> GetPageColorSpacesAsync(int pageNumber, CancellationToken ct)
    {
        if (pageNumber < 1 || _document is null)
        {
            return Array.Empty<PageColorSpaceInfo>();
        }

        List<PageColorSpaceInfo>? result = await ExecuteWithLockAsync(token =>
        {
            var page = _document!.GetPage(pageNumber);
            var list = new List<PageColorSpaceInfo>();
            int index = 0;
            foreach (var image in page.GetImages())
            {
                index++;
                var info = ClassifyDetails(image.ColorSpaceDetails, $"Image {image.WidthInSamples}x{image.HeightInSamples} (#{index})");
                if (info is not null)
                {
                    list.Add(info);
                }
            }
            return list;
        }, ct).ConfigureAwait(false);

        return result ?? new List<PageColorSpaceInfo>();
    }

    /// <summary>
    /// Adapter that turns a PdfPig <see cref="ColorSpaceDetails"/> into a <see cref="PageColorSpaceInfo"/>, delegating the actual
    /// classification to the pure, testable <see cref="ColorSpaceClassifier"/>. Returns null when the image has no
    /// (resolvable) color space.
    /// </summary>
    private static PageColorSpaceInfo? ClassifyDetails(ColorSpaceDetails? details, string source)
    {
        if (details is null)
        {
            return null;
        }

        PdfColorSpaceFamily family = ColorSpaceClassifier.MapFamily(details.Type);
        PdfColorSpaceFamily? underlying = details switch
        {
            IndexedColorSpaceDetails indexed => ColorSpaceClassifier.MapFamily(indexed.BaseColorSpace.Type),
            SeparationColorSpaceDetails separation => ColorSpaceClassifier.MapFamily(separation.AlternateColorSpace.Type),
            DeviceNColorSpaceDetails deviceN => ColorSpaceClassifier.MapFamily(deviceN.AlternateColorSpace.Type),
            ICCBasedColorSpaceDetails icc => ColorSpaceClassifier.MapFamily(icc.AlternateColorSpace.Type),
            _ => null,
        };
        bool atRisk = ColorSpaceClassifier.IsAtRisk(family, underlying);
        string note = ColorSpaceClassifier.Describe(family, underlying);
        return new PageColorSpaceInfo(source, family, atRisk, underlying, note);
    }
}
