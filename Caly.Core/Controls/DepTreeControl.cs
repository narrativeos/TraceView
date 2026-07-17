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
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Caly.Core.Models;

namespace Caly.Core.Controls;

/// <summary>
/// Custom control that renders a dependency parsing tree visualization.
/// Bottom row: word cards with text/POS, Top: curved arcs showing dependency relations.
/// </summary>
public class DepTreeControl : Control
{
    public static readonly StyledProperty<IList<SemanticDepToken>?> TokensProperty =
        AvaloniaProperty.Register<DepTreeControl, IList<SemanticDepToken>?>(nameof(Tokens));

    public static readonly StyledProperty<IList<SemanticDepEdge>?> EdgesProperty =
        AvaloniaProperty.Register<DepTreeControl, IList<SemanticDepEdge>?>(nameof(Edges));

    public IList<SemanticDepToken>? Tokens
    {
        get => GetValue(TokensProperty);
        set => SetValue(TokensProperty, value);
    }

    public IList<SemanticDepEdge>? Edges
    {
        get => GetValue(EdgesProperty);
        set => SetValue(EdgesProperty, value);
    }

    static DepTreeControl()
    {
        AffectsRender<DepTreeControl>(TokensProperty, EdgesProperty);
    }

    public DepTreeControl()
    {
        MinHeight = 120;
    }


    public override void Render(DrawingContext context)
    {
        if (Tokens == null || Tokens.Count == 0 || Edges == null || Edges.Count == 0)
            return;

        var bounds = Bounds;
        var padding = 16.0;
        var cardWidth = 60.0;
        var cardHeight = 32.0;
        var cardBottomY = bounds.Height - padding;
        var totalCardsWidth = Tokens.Count * (cardWidth + 8) - 8;
        var startX = (bounds.Width - totalCardsWidth) / 2;

        // Map token id to x center position
        var tokenPositions = new Dictionary<int, double>();
        foreach (var token in Tokens)
        {
            var idx = token.Id;
            if (idx >= 0 && idx < Tokens.Count)
            {
                var x = startX + idx * (cardWidth + 8) + cardWidth / 2;
                tokenPositions[token.Id] = x;
            }
        }

        // Relation type colors
        var relColors = new Dictionary<string, ImmutableSolidColorBrush>
        {
            ["dobj"] = new ImmutableSolidColorBrush(Color.Parse("#4FACC4"), 0.9),
            ["nsubj"] = new ImmutableSolidColorBrush(Color.Parse("#9B59B6"), 0.9),
            ["nn"] = new ImmutableSolidColorBrush(Color.Parse("#4CAF50"), 0.9),
            ["amod"] = new ImmutableSolidColorBrush(Color.Parse("#E67E22"), 0.9),
            ["advmod"] = new ImmutableSolidColorBrush(Color.Parse("#3F51B5"), 0.9),
            ["punct"] = new ImmutableSolidColorBrush(Color.Parse("#969696"), 0.7),
            ["cc"] = new ImmutableSolidColorBrush(Color.Parse("#636E72"), 0.7),
            ["conj"] = new ImmutableSolidColorBrush(Color.Parse("#0097A7"), 0.9),
            ["rcmod"] = new ImmutableSolidColorBrush(Color.Parse("#C9302C"), 0.9),
            ["cpm"] = new ImmutableSolidColorBrush(Color.Parse("#F57C00"), 0.9),
            ["loc"] = new ImmutableSolidColorBrush(Color.Parse("#00897B"), 0.9),
            ["lobj"] = new ImmutableSolidColorBrush(Color.Parse("#009688"), 0.9),
            ["assn"] = new ImmutableSolidColorBrush(Color.Parse("#7C4DDF"), 0.9),
            ["attr"] = new ImmutableSolidColorBrush(Color.Parse("#E53935"), 0.9),
            ["case"] = new ImmutableSolidColorBrush(Color.Parse("#8D6E63"), 0.9),
            ["root"] = new ImmutableSolidColorBrush(Color.Parse("#FF1744"), 0.9),
        };

        var defaultBrush = new ImmutableSolidColorBrush(Color.Parse("#757575"), 0.7);
        var cardBg = new ImmutableSolidColorBrush(Color.Parse("#2E2E2E"));
        var cardBorderBrush = new ImmutableSolidColorBrush(Color.Parse("#454545"));
        var whiteBrush = new ImmutableSolidColorBrush(Colors.White);
        var grayBrush = new ImmutableSolidColorBrush(Color.Parse("#B0B0B0"), 0.8);
        var labelBgBrush = new ImmutableSolidColorBrush(Color.Parse("#1E1E1E"), 0.5);

        var cardBorderPen = new ImmutablePen(cardBorderBrush, 1.0);
        var cardCornerRadius = new CornerRadius(6.0);
        var pillCornerRadius = new CornerRadius(3.0);

        // Draw arcs first (behind cards)
        foreach (var edge in Edges)
        {
            if (!tokenPositions.TryGetValue(edge.Child, out var childX) ||
                !tokenPositions.TryGetValue(edge.Head, out var headX))
                continue;

            var brush = relColors.TryGetValue(edge.Rel, out var b) ? b : defaultBrush;
            var pen = new ImmutablePen(brush, 1.5);

            var midX = (childX + headX) / 2.0;
            var distance = Math.Abs(headX - childX);
            var arcHeight = Math.Min(80 + distance * 0.3, 180);
            var controlY = cardBottomY - arcHeight;

            // Draw arc using cubic bezier
            var sg = new StreamGeometry();
            using (var ctx = sg.Open())
            {
                ctx.BeginFigure(new Point(childX, cardBottomY - cardHeight), false);
                var cp1 = new Point((childX + midX) / 2, (cardBottomY - cardHeight + controlY) / 2);
                var cp2 = new Point((midX + headX) / 2, (controlY + cardBottomY - cardHeight) / 2);
                ctx.CubicBezierTo(cp1, cp2, new Point(headX, cardBottomY - cardHeight));
                ctx.EndFigure(false);
            }
            context.DrawGeometry(null, pen, sg);

            // Draw relation label at arc peak
            var formattedText = new FormattedText(
                edge.Rel,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                10.0,
                brush);
            var textWidth = formattedText.Width;
            var pillWidth = textWidth + 6.0;
            var pillHeight = 14.0;

            // Background pill
            var pillLeft = midX - pillWidth / 2;
            var pillTop = controlY - pillHeight / 2;
            context.DrawRectangle(labelBgBrush, null, new Rect(pillLeft, pillTop, pillWidth, pillHeight));

            // Text
            context.DrawText(formattedText, new Point(midX - textWidth / 2, controlY - 4));
        }

        // Draw word cards
        foreach (var token in Tokens)
        {
            var idx = token.Id;
            if (idx < 0 || idx >= Tokens.Count)
                continue;

            var x = startX + idx * (cardWidth + 8);
            var cardRect = new Rect(x, cardBottomY - cardHeight, cardWidth, cardHeight);

            // Card background
            context.DrawRectangle(cardBg, cardBorderPen, new Rect(x, cardBottomY - cardHeight, cardWidth, cardHeight));

            // Word text (centered)
            var centerX = x + cardWidth / 2;
            var wt = new FormattedText(
                token.Text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                11.0,
                whiteBrush);
            context.DrawText(wt, new Point(centerX - wt.Width / 2, cardBottomY - cardHeight / 2 - 5));

            // POS tag (centered)
            var pt = new FormattedText(
                token.Pos,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                8.0,
                grayBrush);
            context.DrawText(pt, new Point(centerX - pt.Width / 2, cardBottomY - cardHeight / 2 + 8));
        }
    }
}