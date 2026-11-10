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

using Caly.Core.Services;
using System;
using System.IO;
using System.Linq;

namespace Caly.Tests;

/// <summary>
/// Tests for the overlay-only shadow blocks produced by the MinerU 4.x ("docvortex.middle")
/// parsing path: sub-elements that carry their own block_id and bbox (e.g. image_footnote
/// captions rendered below their image block's bbox) must get a box on the PDF overlay
/// without appearing as a row in the MinerU Blocks column.
/// </summary>
public class MinerUShadowBlockTests
{
    private static string WriteJson(string json)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "caly-mineru-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var jsonPath = Path.Combine(tempDir, "middle_json.json");
        File.WriteAllText(jsonPath, json);
        return jsonPath;
    }

    [Fact]
    public void V4ImageFootnoteSubElement_BecomesOverlayOnlyShadowBlock()
    {
        var path = WriteJson("""
        {
          "schema": "docvortex.middle",
          "schema_version": "2.0",
          "pages": [
            {
              "page_idx": 0,
              "blocks": [
                {
                  "type": "image",
                  "index": 2,
                  "bbox": [0.201, 0.108, 0.823, 0.368],
                  "block_id": "parent-image",
                  "content": [
                    { "type": "image_body", "index": 2, "bbox": [0.201, 0.108, 0.823, 0.368],
                      "block_id": "parent-image", "image_path": "images/p.jpg", "content": "" },
                    { "type": "image_footnote", "index": 3, "bbox": [0.204, 0.389, 0.828, 0.442],
                      "block_id": "caption-1",
                      "content": [ { "type": "text", "content": "caption text" } ] }
                  ]
                }
              ]
            }
          ]
        }
        """);

        var doc = MinerUJsonService.TryParseMinerUMiddleJson(path);
        Assert.NotNull(doc);

        // Column: only the image block (one row).
        Assert.True(doc!.PagesBlocks.TryGetValue(1, out var column));
        Assert.Single(column!);
        Assert.False(column![0].IsOverlayOnly);

        // Image block Content stays the image path (bitmap loading); the caption text
        // surfaces via ImageCaption so the row can show it below the image.
        Assert.Equal("images/p.jpg", column[0].Content);
        Assert.Equal("caption text", column[0].ImageCaption);

        // Overlay: image block + caption shadow block.
        var overlay = doc.GetPreprocBlocksForPage(1);
        Assert.Equal(2, overlay.Count);

        var shadow = overlay.Single(b => b.IsOverlayOnly);
        Assert.Equal("caption-1", shadow.BlockId);
        Assert.Equal("caption", shadow.Type);
        Assert.Equal("caption text", shadow.Content);
        Assert.True(shadow.IsBboxNormalized);
        Assert.Equal(0.204, shadow.Bbox.X, precision: 6);
        Assert.Equal(0.389, shadow.Bbox.Y, precision: 6);
        Assert.Equal(0.828 - 0.204, shadow.Bbox.Width, precision: 6);
        Assert.Equal(0.442 - 0.389, shadow.Bbox.Height, precision: 6);
        Assert.Equal("para", shadow.DestinationType);
        // Shadow links to the parent column row for connection lines / highlight.
        Assert.Equal(["parent-image"], shadow.RelatedBlockIds);

        // Parent links to itself and to the caption so selecting the row
        // highlights both boxes on the overlay.
        Assert.Contains("parent-image", column[0].RelatedBlockIds);
        Assert.Contains("caption-1", column[0].RelatedBlockIds);

        // The image_body sub-element shares the parent's block_id: no shadow for it.
        Assert.DoesNotContain(overlay, b => b.BlockId == "parent-image" && b.IsOverlayOnly);
    }

    [Fact]
    public void V4SubElementWithoutBlockId_DoesNotProduceShadow()
    {
        var path = WriteJson("""
        {
          "schema": "docvortex.middle",
          "schema_version": "2.0",
          "pages": [
            {
              "page_idx": 0,
              "blocks": [
                {
                  "type": "text",
                  "index": 0,
                  "bbox": [0.1, 0.1, 0.9, 0.2],
                  "block_id": "text-1",
                  "content": [
                    { "type": "text", "content": "hello" },
                    { "type": "text", "content": "world" }
                  ]
                }
              ]
            }
          ]
        }
        """);

        var doc = MinerUJsonService.TryParseMinerUMiddleJson(path);
        Assert.NotNull(doc);

        var overlay = doc!.GetPreprocBlocksForPage(1);
        Assert.Single(overlay);
        Assert.False(overlay[0].IsOverlayOnly);
        Assert.Equal("helloworld", overlay[0].Content);
        Assert.Equal(string.Empty, overlay[0].ImageCaption);
    }

    [Fact]
    public void V4ChartCaptionSubElement_CaptionOnImageRow()
    {
        var path = WriteJson("""
        {
          "schema": "docvortex.middle",
          "schema_version": "2.0",
          "pages": [
            {
              "page_idx": 0,
              "blocks": [
                {
                  "type": "chart",
                  "index": 2,
                  "bbox": [0.107, 0.181, 0.914, 0.492],
                  "block_id": "chart-1",
                  "content": [
                    { "type": "chart_caption", "index": 1, "bbox": [0.39, 0.133, 0.635, 0.16],
                      "block_id": "chart-cap-1",
                      "content": [ { "type": "text", "content": "常住人口和城镇化率" } ] },
                    { "type": "chart_body", "index": 2, "bbox": [0.107, 0.181, 0.914, 0.492],
                      "block_id": "chart-1", "image_path": "images/chart.jpg", "content": "" }
                  ]
                }
              ]
            }
          ]
        }
        """);

        var doc = MinerUJsonService.TryParseMinerUMiddleJson(path);
        Assert.NotNull(doc);

        var column = doc!.PagesBlocks[1];
        Assert.Single(column);
        // Chart rows render the chart image: Content stays the image path, the
        // caption surfaces via ImageCaption (same as image blocks).
        Assert.Equal("images/chart.jpg", column[0].Content);
        Assert.Equal("常住人口和城镇化率", column[0].ImageCaption);

        // Caption also gets an overlay shadow box (its bbox sits above the chart body).
        var overlay = doc.GetPreprocBlocksForPage(1);
        var shadow = Assert.Single(overlay, b => b.IsOverlayOnly);
        Assert.Equal("chart-cap-1", shadow.BlockId);
    }

    [Fact]
    public void V4NestedInlineSpansWithoutBlockId_TextNotLost()
    {
        var path = WriteJson("""
        {
          "schema": "docvortex.middle",
          "schema_version": "2.0",
          "pages": [
            {
              "page_idx": 0,
              "blocks": [
                {
                  "type": "index",
                  "index": 1,
                  "bbox": [0.1, 0.1, 0.9, 0.8],
                  "block_id": "idx-1",
                  "content": [
                    { "type": "text", "content": [ { "type": "text", "content": "城镇居民人均可支配收入39549元" } ] },
                    { "type": "text", "content": [ { "type": "text", "content": "院士工作站547个" } ] }
                  ]
                }
              ]
            }
          ]
        }
        """);

        var doc = MinerUJsonService.TryParseMinerUMiddleJson(path);
        Assert.NotNull(doc);

        var column = doc!.PagesBlocks[1];
        Assert.Single(column);
        Assert.Contains("城镇居民人均可支配收入39549元", column[0].Content);
        Assert.Contains("院士工作站547个", column[0].Content);
    }

    [Fact]
    public void V4EquationBlock_ContentFallsBackToImagePath()
    {
        var path = WriteJson("""
        {
          "schema": "docvortex.middle",
          "schema_version": "2.0",
          "pages": [
            {
              "page_idx": 0,
              "blocks": [
                {
                  "type": "equation",
                  "index": 3,
                  "bbox": [0.513, 0.881, 0.925, 0.892],
                  "block_id": "eq-1",
                  "image_path": "images/page_55_equation_3.jpg",
                  "content": ""
                }
              ]
            }
          ]
        }
        """);

        var doc = MinerUJsonService.TryParseMinerUMiddleJson(path);
        Assert.NotNull(doc);

        var column = doc!.PagesBlocks[1];
        Assert.Single(column);
        Assert.Equal("images/page_55_equation_3.jpg", column[0].Content);
    }
}