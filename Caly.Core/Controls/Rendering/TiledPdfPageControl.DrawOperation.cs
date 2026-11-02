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

using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Caly.Core.Services.Rendering;
using SkiaSharp;
using System;
using System.Buffers;

namespace Caly.Core.Controls.Rendering;

public partial class TiledPdfPageControl
{
    private sealed class TiledDrawOperation : ICustomDrawOperation
    {
        private TileDrawEntry[]? _tiles;
        private readonly int _tileCount;
        private readonly SKSamplingOptions _samplingOptions;
        private readonly SKRect _cullRect;
        private readonly bool _hasWhiteBalance;
        private readonly float _whiteBalanceGainsR;
        private readonly float _whiteBalanceGainsG;
        private readonly float _whiteBalanceGainsB;

        public TiledDrawOperation(Rect bounds, SKRect cullRect, TileDrawEntry[] tiles, int tileCount, SKSamplingOptions samplingOptions,
            bool hasWhiteBalance, float whiteBalanceGainsR, float whiteBalanceGainsG, float whiteBalanceGainsB)
        {
            Bounds = bounds;
            _cullRect = cullRect;
            _tiles = tiles;
            _tileCount = tileCount;
            _samplingOptions = samplingOptions;
            _hasWhiteBalance = hasWhiteBalance;
            _whiteBalanceGainsR = whiteBalanceGainsR;
            _whiteBalanceGainsG = whiteBalanceGainsG;
            _whiteBalanceGainsB = whiteBalanceGainsB;
        }

        public Rect Bounds { get; }

        public bool HitTest(Point p) => Bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

        public override bool Equals(object? obj) => obj is ICustomDrawOperation cdo && Equals(cdo);

        public override int GetHashCode() => HashCode.Combine(Bounds, _tileCount);

        /// <summary>
        /// Executed on the render thread. Blits pre-rendered tile images.
        /// </summary>
        public void Render(ImmediateDrawingContext context)
        {
            Debug.ThrowOnUiThread();

            var tiles = _tiles;
            if (tiles is null)
            {
                return;
            }

            if (
#pragma warning disable CS8600
                !context.TryGetFeature(out ISkiaSharpApiLeaseFeature leaseFeature))
#pragma warning restore CS8600
            {
                return;
            }

            using ISkiaSharpApiLease lease = leaseFeature.Lease();

            var canvas = lease?.SkCanvas;
            if (canvas is null)
            {
                return;
            }

#if DEBUG
            if (PageInteractiveLayerControl.ShowLayoutAnalysisDebug)
            {
                using var backgroundPaint = new SKPaint();
                backgroundPaint.Style = SKPaintStyle.Fill;
                backgroundPaint.Color = SKColors.Aqua;
                canvas.DrawPaint(backgroundPaint);
            }
#endif

            canvas.Save();
            canvas.ClipRect(_cullRect);

            // When white balance is active, every tile is drawn through a per-channel gain colour
            // filter. The paint/filter are created here — on the render thread, inside the SkiaSharp
            // lease — so the SKColorFilter is never shared across threads. IsAntialias stays false
            // (the default for a fresh paint) to preserve the seamless, seam-free tile blitting that
            // a null paint gives.
            SKPaint? tilePaint = null;
            SKColorFilter? colorFilter = null;
            if (_hasWhiteBalance)
            {
                var matrix = TileRenderService.BuildGainMatrix(
                    _whiteBalanceGainsR, _whiteBalanceGainsG, _whiteBalanceGainsB);
                colorFilter = SKColorFilter.CreateColorMatrix(matrix);
                tilePaint = new SKPaint { ColorFilter = colorFilter, IsAntialias = false };
            }

            try
            {
                for (int i = 0; i < _tileCount; ++i)
                {
                    ref readonly var tile = ref tiles[i];
                    if (tile is { CanRender: true, ImageRef.IsAlive: true } && !canvas.QuickReject(tile.DestRect))
                    {
                        canvas.DrawImage(tile.ImageRef.Item, tile.SrcRect, tile.DestRect, _samplingOptions, tilePaint);
                    }
                }
            }
            finally
            {
                tilePaint?.Dispose();
                colorFilter?.Dispose();
            }

            canvas.Restore();

#if DEBUG
            if (PageInteractiveLayerControl.ShowLayoutAnalysisDebug)
            {
                using var borderPaint = new SKPaint();
                borderPaint.Style = SKPaintStyle.Stroke;
                borderPaint.Color = SKColors.Red.WithAlpha(120);
                borderPaint.StrokeWidth = 5f;

                if (canvas.TotalMatrix.TryInvert(out var invert))
                {
                    // Scale width to get consistency across scales
                    borderPaint.StrokeWidth *= invert.ScaleX;
                }

                for (int i = 0; i < _tileCount; ++i)
                {
                    ref readonly var tile = ref tiles[i];
                    canvas.DrawRect(tile.DestRect, borderPaint);
                }

                borderPaint.Color = SKColors.DarkMagenta.WithAlpha(120);
                canvas.DrawRect(_cullRect, borderPaint);
            }
#endif
        }

        public void Dispose()
        {
            var tiles = _tiles;
            if (tiles is null)
            {
                return;
            }

            _tiles = null;

            for (int i = 0; i < _tileCount; ++i)
            {
                ref var tile = ref tiles[i];
                tile.Dispose();
                tile = default;
            }

            ArrayPool<TileDrawEntry>.Shared.Return(tiles, clearArray: false);
        }
    }
}
