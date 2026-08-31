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

using Avalonia.Controls;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.ViewModels;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Caly.Tests;

public class PopoJsonServiceTests
{
    #region LoadNormalizationJson Tests

    [Fact]
    public void LoadNormalizationJson_ParsesPagesAndBlocks()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "caly-popo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var jsonPath = Path.Combine(tempDir, "normalization.json");
        File.WriteAllText(jsonPath, """
        {
          "model": "mineru",
          "doc_id": "test_doc",
          "pages": {
            "1": [
              {
                "type": "title",
                "content": "Hello World",
                "bbox": [10, 20, 200, 50],
                "title_level": 1,
                "source_label": "heading"
              },
              {
                "type": "text",
                "content": "This is a paragraph.",
                "bbox": [10, 60, 300, 100],
                "source_id": "doc1:5"
              }
            ],
            "2": [
              {
                "type": "text",
                "content": "Page 2 content",
                "bbox": [10, 20, 200, 50]
              }
            ]
          }
        }
        """);

        try
        {
            var result = PopoJsonService.LoadNormalizationJson(jsonPath);

            Assert.NotNull(result);
            Assert.Equal("mineru", result.ModelName);
            Assert.Equal("test_doc", result.DocId);
            Assert.Equal(2, result.PagesBlocks.Count);

            var page1Blocks = result.PagesBlocks[1];
            Assert.Equal(2, page1Blocks.Count);
            Assert.Equal("title", page1Blocks[0].Type);
            Assert.Equal("Hello World", page1Blocks[0].Content);
            Assert.Equal(1, page1Blocks[0].TitleLevel);
            Assert.Equal("heading", page1Blocks[0].SourceLabel);

            // The second block's Id is assigned from 'order' counter before source_id parsing,
            // so it starts at 1 (after first block incremented it)
            Assert.Equal(1, page1Blocks[1].Id);

            var page2Blocks = result.PagesBlocks[2];
            Assert.Single(page2Blocks);
            Assert.Equal("Page 2 content", page2Blocks[0].Content);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    #endregion

    #region LoadInferenceJson Tests

    [Fact]
    public void LoadInferenceJson_ParsesFlatBlockList()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "caly-popo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var jsonPath = Path.Combine(tempDir, "inference.json");
        File.WriteAllText(jsonPath, """
        [
          {
            "id": 1,
            "page": 1,
            "type": "title",
            "content": "Title",
            "bbox": [10, 20, 200, 50],
            "contd": 0,
            "level": 1,
            "image": 0,
            "table_merge": 0
          },
          {
            "id": 2,
            "page": 1,
            "type": "text",
            "content": "Body text",
            "bbox": [10, 60, 300, 100],
            "contd": 0,
            "level": 0,
            "image": 0,
            "table_merge": 0
          }
        ]
        """);

        try
        {
            var result = PopoJsonService.LoadInferenceJson(jsonPath);

            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Equal(1, result[0].Id);
            Assert.Equal(1, result[0].Page);
            Assert.Equal("title", result[0].Type);
            Assert.Equal("Title", result[0].Content);
            Assert.Equal(1, result[0].Level);

            Assert.Equal(2, result[1].Id);
            Assert.Equal("text", result[1].Type);
            Assert.Equal("Body text", result[1].Content);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LoadInferenceJson_ReturnsNullForNonArrayJson()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "caly-popo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var jsonPath = Path.Combine(tempDir, "invalid.json");
        File.WriteAllText(jsonPath, """
        { "not": "an array" }
        """);

        try
        {
            var result = PopoJsonService.LoadInferenceJson(jsonPath);
            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    #endregion

    #region LoadTreeJson Tests

    [Fact]
    public void LoadTreeJson_ParsesHierarchicalTree()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "caly-popo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var jsonPath = Path.Combine(tempDir, "tree.json");
        File.WriteAllText(jsonPath, """
        {
          "type": "root",
          "title": "Document Root",
          "metadata": "root metadata",
          "content": "",
          "level": 0,
          "location": [],
          "block_ids": [],
          "children": [
            {
              "type": "section",
              "title": "Section 1",
              "metadata": "",
              "content": "",
              "level": 1,
              "location": [
                {
                  "bbox": [10, 20, 200, 50],
                  "page": 1
                }
              ],
              "block_ids": [1, 2],
              "children": [
                {
                  "type": "paragraph",
                  "title": "",
                  "metadata": "",
                  "content": "Paragraph content",
                  "level": 2,
                  "location": [
                    {
                      "bbox": [10, 60, 300, 100],
                      "page": 1
                    }
                  ],
                  "block_ids": [3],
                  "children": []
                }
              ]
            }
          ]
        }
        """);

        try
        {
            var result = PopoJsonService.LoadTreeJson(jsonPath);

            Assert.NotNull(result);
            Assert.Equal("root", result.Type);
            Assert.Equal("Document Root", result.Title);
            Assert.Equal("root metadata", result.Metadata);
            Assert.Single(result.Children);

            var section = result.Children[0];
            Assert.Equal("section", section.Type);
            Assert.Equal("Section 1", section.Title);
            Assert.Equal(1, section.Level);
            Assert.Equal(2, section.BlockIds.Count);
            Assert.Contains(1, section.BlockIds);
            Assert.Contains(2, section.BlockIds);
            Assert.Single(section.Location);
            Assert.Equal(1, section.Location[0].Page);

            var paragraph = section.Children[0];
            Assert.Equal("paragraph", paragraph.Type);
            Assert.Equal("Paragraph content", paragraph.Content);
            Assert.Single(paragraph.BlockIds);
            Assert.Equal(3, paragraph.BlockIds[0]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LoadTreeJson_ParsesSourceBlockIds()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "caly-popo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var jsonPath = Path.Combine(tempDir, "tree.json");
        File.WriteAllText(jsonPath, """
        {
          "type": "root",
          "title": "",
          "metadata": "",
          "content": "",
          "level": 0,
          "location": [],
          "block_ids": [],
          "source_block_ids": [],
          "children": [
            {
              "type": "text",
              "title": "Section 1",
              "metadata": "",
              "content": "Some content",
              "level": 1,
              "location": [
                {
                  "bbox": [0.1, 0.1, 0.5, 0.2],
                  "page": 1
                }
              ],
              "block_ids": [1, 2],
              "source_block_ids": ["aaaa-1111-bbbb-2222", "cccc-3333-dddd-4444"],
              "children": [
                {
                  "type": "image",
                  "title": "",
                  "metadata": "",
                  "content": "",
                  "level": 2,
                  "location": [
                    {
                      "bbox": [0.2, 0.3, 0.6, 0.5],
                      "page": 1
                    }
                  ],
                  "block_ids": [3],
                  "source_block_ids": ["eeee-5555-ffff-6666"],
                  "children": []
                }
              ]
            }
          ]
        }
        """);

        try
        {
            var result = PopoJsonService.LoadTreeJson(jsonPath);

            Assert.NotNull(result);
            Assert.Equal("root", result.Type);
            Assert.Empty(result.SourceBlockIds);
            Assert.Single(result.Children);

            var section = result.Children[0];
            Assert.Equal("text", section.Type);
            Assert.Equal(2, section.BlockIds.Count);
            Assert.Equal(1, section.BlockIds[0]);
            Assert.Equal(2, section.BlockIds[1]);
            Assert.Equal(2, section.SourceBlockIds.Count);
            Assert.Equal("aaaa-1111-bbbb-2222", section.SourceBlockIds[0]);
            Assert.Equal("cccc-3333-dddd-4444", section.SourceBlockIds[1]);

            var image = section.Children[0];
            Assert.Equal("image", image.Type);
            Assert.Single(image.BlockIds);
            Assert.Equal(3, image.BlockIds[0]);
            Assert.Single(image.SourceBlockIds);
            Assert.Equal("eeee-5555-ffff-6666", image.SourceBlockIds[0]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void BuildStructureDocumentFromTree_PopulatesSourceBlockIdsInBlocks()
    {
        var treeRoot = new Caly.Core.Models.AnalysisTreeNode
        {
            Type = "root",
            Level = 0
        };

        var sectionNode = new Caly.Core.Models.AnalysisTreeNode
        {
            Type = "text",
            Title = "Section 1",
            Content = "Content here",
            Level = 1
        };
        sectionNode.Location.Add(new Caly.Core.Models.LocationEntry
        {
            Bbox = new Avalonia.Rect(0.1, 0.1, 0.4, 0.1),
            Page = 1
        });
        sectionNode.BlockIds.Add(1);
        sectionNode.BlockIds.Add(2);
        sectionNode.SourceBlockIds.Add("uuid-1111-2222-3333");
        sectionNode.SourceBlockIds.Add("uuid-4444-5555-6666");

        treeRoot.Children.Add(sectionNode);

        var doc = PopoJsonService.BuildStructureDocumentFromTree(treeRoot, "test_doc");

        Assert.NotNull(doc);
        Assert.Equal("test_doc", doc.DocId);
        Assert.NotNull(doc.TreeRoot);
        Assert.NotNull(doc.PagesBlocks);
        Assert.True(doc.PagesBlocks.ContainsKey(1));

        var page1Blocks = doc.PagesBlocks[1];
        Assert.Single(page1Blocks);

        var block = page1Blocks[0];
        Assert.Equal("text", block.Type);
        Assert.Equal(0, block.Id); // blockId starts at 0
        Assert.Equal(1, block.Page);
        Assert.Equal(2, block.OriginalBlockIds.Count);
        Assert.Equal(1, block.OriginalBlockIds[0]);
        Assert.Equal(2, block.OriginalBlockIds[1]);
        Assert.Equal(2, block.SourceBlockIds.Count);
        Assert.Equal("uuid-1111-2222-3333", block.SourceBlockIds[0]);
        Assert.Equal("uuid-4444-5555-6666", block.SourceBlockIds[1]);
    }

    #endregion

    #region FindPopoJsonPaths Tests

    [Fact]
    public void FindPopoJsonPaths_ReturnsNullForEmptyPath()
    {
        var result = PopoJsonService.FindPopoJsonPaths(string.Empty);
        Assert.Null(result.normalized);
        Assert.Null(result.inference);
        Assert.Null(result.tree);
    }

    [Fact]
    public void FindPopoJsonPaths_ReturnsNullForNonExistentOutputs()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "caly-popo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var pdfPath = Path.Combine(tempDir, "test.pdf");
        File.WriteAllText(pdfPath, "");

        try
        {
            var result = PopoJsonService.FindPopoJsonPaths(pdfPath);
            Assert.Null(result.normalized);
            Assert.Null(result.inference);
            Assert.Null(result.tree);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FindPopoJsonPaths_FindsFilesInOutputsDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "caly-popo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var pdfPath = Path.Combine(tempDir, "test.pdf");
        File.WriteAllText(pdfPath, "");

        // Create outputs/label_normalization/mineru/test.json
        var normDir = Path.Combine(tempDir, "outputs", "label_normalization", "mineru");
        Directory.CreateDirectory(normDir);
        File.WriteAllText(Path.Combine(normDir, "test.json"), "{}");

        // Create outputs/inference/mineru/test.json
        var infDir = Path.Combine(tempDir, "outputs", "inference", "mineru");
        Directory.CreateDirectory(infDir);
        File.WriteAllText(Path.Combine(infDir, "test.json"), "[]");

        // Create outputs/build_tree/mineru/test.json
        var treeDir = Path.Combine(tempDir, "outputs", "build_tree", "mineru");
        Directory.CreateDirectory(treeDir);
        File.WriteAllText(Path.Combine(treeDir, "test.json"), "{}");

        try
        {
            var result = PopoJsonService.FindPopoJsonPaths(pdfPath);

            Assert.NotNull(result.normalized);
            Assert.NotNull(result.inference);
            Assert.NotNull(result.tree);
            Assert.EndsWith("test.json", result.normalized);
            Assert.EndsWith("test.json", result.inference);
            Assert.EndsWith("test.json", result.tree);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    #endregion

    #region Save/Load Round-Trip Tests

    [Fact]
    public void SaveStructureDocumentToProject_And_LoadBack()
    {
        var projectPath = Path.Combine(Path.GetTempPath(), "caly-popo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectPath);

        try
        {
            var doc = new Caly.Core.Models.StructureDocument
            {
                ModelName = "test_model",
                DocId = "test_doc"
            };

            PopoJsonService.SaveStructureDocumentToProject(doc, projectPath);

            var popoDir = Path.Combine(projectPath, "popo");
            Assert.True(Directory.Exists(popoDir));

            var popoJsonPath = Path.Combine(popoDir, "popo.json");
            Assert.True(File.Exists(popoJsonPath));

            var loaded = PopoJsonService.LoadStructureDocumentFromProject(projectPath);
            Assert.NotNull(loaded);
            Assert.Equal("test_model", loaded!.ModelName);
            Assert.Equal("test_doc", loaded.DocId);
        }
        finally
        {
            if (Directory.Exists(projectPath))
                Directory.Delete(projectPath, recursive: true);
        }
    }

    [Fact]
    public void LoadStructureDocumentFromProject_ReturnsNullForNonExistentProject()
    {
        var result = PopoJsonService.LoadStructureDocumentFromProject("/nonexistent/path");
        Assert.Null(result);
    }

    [Fact]
    public void LoadStructureDocumentFromProject_ReturnsNullForNullPath()
    {
        var result = PopoJsonService.LoadStructureDocumentFromProject(null);
        Assert.Null(result);
    }

    #endregion

    #region Reference Parsing Tests

    [Fact]
    public void GenerateReferenceMarkdown_WithEntries_ReturnsFormattedMarkdown()
    {
        var entries = new System.Collections.Generic.List<Caly.Core.Models.ReferenceEntry>
        {
            new()
            {
                Id = 1,
                RawText = "[1]盛敬超.液压流体力学 [M].北京：机械工业出版社，1980.",
                Authors = "盛敬超",
                Title = "液压流体力学",
                RefType = "M",
                JournalOrPublisher = "机械工业出版社",
                Year = "1980",
                Page = 208,
                BlockIds = new System.Collections.Generic.List<int> { 1 }
            },
            new()
            {
                Id = 2,
                RawText = "[2]李成功.液压系统设计与分析 [J].液压与气动，2005(3):15-20.",
                Authors = "李成功",
                Title = "液压系统设计与分析",
                RefType = "J",
                JournalOrPublisher = "液压与气动",
                Year = "2005",
                Pages = "15-20",
                Page = 209,
                BlockIds = new System.Collections.Generic.List<int> { 2 }
            }
        };

        var markdown = PopoJsonService.GenerateReferenceMarkdown(entries);

        Assert.False(string.IsNullOrEmpty(markdown));
        Assert.Contains("# 参考文献", markdown);
        Assert.Contains("共 2 条", markdown);
        Assert.Contains("[1]盛敬超.液压流体力学 [M].北京：机械工业出版社，1980.", markdown);
        Assert.Contains("[2]李成功.液压系统设计与分析 [J].液压与气动，2005(3):15-20.", markdown);
    }

    [Fact]
    public void GenerateReferenceMarkdown_EmptyList_ReturnsEmptyMessage()
    {
        var entries = new System.Collections.Generic.List<Caly.Core.Models.ReferenceEntry>();
        var markdown = PopoJsonService.GenerateReferenceMarkdown(entries);

        Assert.False(string.IsNullOrEmpty(markdown));
        Assert.Contains("(空)", markdown);
    }

    [Fact]
    public void GenerateReferenceMarkdown_FallbackToStructuredFields()
    {
        var entries = new System.Collections.Generic.List<Caly.Core.Models.ReferenceEntry>
        {
            new()
            {
                Id = 1,
                RawText = "",  // No raw text, should fall back to structured
                Authors = "张三",
                Title = "测试文献",
                RefType = "J",
                JournalOrPublisher = "测试出版社",
                Year = "2024",
                Pages = "100-120",
                Page = 50
            }
        };

        var markdown = PopoJsonService.GenerateReferenceMarkdown(entries);

        Assert.Contains("[1]", markdown);
        Assert.Contains("张三", markdown);
        Assert.Contains("测试文献 [J]", markdown);
        Assert.Contains("测试出版社", markdown);
        Assert.Contains("2024", markdown);
        Assert.Contains("pp.100-120", markdown);
        Assert.Contains("p.50", markdown);
    }

    #endregion

    #region Structured Table (table_data / table_key_value) Tests

    [Fact]
    public void LoadTreeJson_ParsesTableDataNode_WithObjectHeadersAndRows()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "caly-popo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var jsonPath = Path.Combine(tempDir, "tree.json");
        File.WriteAllText(jsonPath, """
        {
          "type": "root",
          "title": "Root",
          "content": "",
          "level": 0,
          "location": [],
          "block_ids": [],
          "children": [
            {
              "type": "table_data",
              "title": "3.绕组连同套管的直流电阻",
              "content": "",
              "level": 3,
              "location": [
                { "bbox": [0.1, 0.2, 0.9, 0.5], "page": 2, "row_range": [2, 4] }
              ],
              "block_ids": [10],
              "node_id": "d527ca50-ecba-451a-9482-98387afcf13e",
              "children": [],
              "headers": [
                { "index": 0, "name": "绕组", "group": null, "unit": null },
                { "index": 1, "name": "相别", "group": null, "unit": null },
                { "index": 2, "name": "实测值(Ω)", "group": null, "unit": "Ω" }
              ],
              "rows": [
                {
                  "row_index": 2,
                  "cells": {
                    "绕组": { "raw": "高压侧", "value": "高压侧" },
                    "相别": { "raw": "A-0", "value": "A-0" },
                    "实测值(Ω)": { "raw": "1.234", "value": 1.234 }
                  }
                },
                {
                  "row_index": 3,
                  "cells": {
                    "绕组": { "raw": "高压侧", "value": "高压侧" },
                    "相别": { "raw": "B-0", "value": "B-0" },
                    "实测值(Ω)": { "raw": "1.235", "value": 1.235 }
                  }
                }
              ],
              "pairs": null,
              "toc": null
            }
          ]
        }
        """);

        try
        {
            var result = PopoJsonService.LoadTreeJson(jsonPath);

            Assert.NotNull(result);
            var table = result.Children[0];
            Assert.Equal("table_data", table.Type);
            Assert.Equal("d527ca50-ecba-451a-9482-98387afcf13e", table.NodeId);

            // Object-style headers: name field extracted
            Assert.NotNull(table.Headers);
            Assert.Equal(new[] { "绕组", "相别", "实测值(Ω)" }, table.Headers.Select(h => h.Name));

            // Rows with cells
            Assert.NotNull(table.Rows);
            Assert.Equal(2, table.Rows.Count);
            Assert.Equal(2, table.Rows[0].RowIndex);
            Assert.Equal(3, table.Rows[1].RowIndex);
            Assert.Equal("高压侧", table.Rows[0].Cells["绕组"].Raw);
            Assert.Equal("A-0", table.Rows[0].Cells["相别"].Raw);
            Assert.Equal("1.234", table.Rows[0].Cells["实测值(Ω)"].Raw);

            // Location row_range
            Assert.NotNull(table.Location[0].RowRange);
            Assert.Equal(new[] { 2, 4 }, table.Location[0].RowRange);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LoadTreeJson_ParsesTableDataNode_WithStringHeaders()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "caly-popo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var jsonPath = Path.Combine(tempDir, "tree.json");
        File.WriteAllText(jsonPath, """
        {
          "type": "root",
          "title": "Root",
          "content": "",
          "level": 0,
          "location": [],
          "block_ids": [],
          "children": [
            {
              "type": "table_data",
              "title": "Simple table",
              "content": "",
              "level": 3,
              "location": [],
              "block_ids": [],
              "children": [],
              "headers": ["名称", "数值"],
              "rows": [
                {
                  "row_index": 0,
                  "cells": {
                    "名称": { "raw": "容量", "value": "容量" },
                    "数值": { "raw": "50", "value": 50 }
                  }
                }
              ]
            }
          ]
        }
        """);

        try
        {
            var result = PopoJsonService.LoadTreeJson(jsonPath);

            var table = result.Children[0];
            Assert.NotNull(table.Headers);
            Assert.Equal(new[] { "名称", "数值" }, table.Headers.Select(h => h.Name));
            Assert.NotNull(table.Rows);
            Assert.Single(table.Rows);
            Assert.Equal("50", table.Rows[0].Cells["数值"].Raw);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LoadTreeJson_ParsesTableKeyValueNode_WithPairsAndRowRange()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "caly-popo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var jsonPath = Path.Combine(tempDir, "tree.json");
        File.WriteAllText(jsonPath, """
        {
          "type": "root",
          "title": "Root",
          "content": "",
          "level": 0,
          "location": [],
          "block_ids": [],
          "children": [
            {
              "type": "table_key_value",
              "title": "1.设备参数",
              "content": "",
              "level": 3,
              "location": [
                { "bbox": [0.06, 0.10, 0.93, 0.25], "page": 2, "row_range": [1, 7] }
              ],
              "block_ids": [6],
              "node_id": "d522ac87-e67b-4df9-b912-fac2d6f563b8",
              "children": [],
              "headers": null,
              "rows": null,
              "pairs": [
                {
                  "key": "设备名称",
                  "value": { "raw": "110kV,洛口变电站主变改造工程 2#主变", "value": "110kV,洛口变电站主变改造工程 2#主变" },
                  "row_index": 0,
                  "page": 2
                },
                {
                  "key": "额定容量",
                  "value": { "raw": "63000", "value": 63000, "unit": "kVA" },
                  "row_index": 1,
                  "page": 2
                }
              ],
              "toc": null
            }
          ]
        }
        """);

        try
        {
            var result = PopoJsonService.LoadTreeJson(jsonPath);

            var kv = result.Children[0];
            Assert.Equal("table_key_value", kv.Type);
            Assert.Equal("d522ac87-e67b-4df9-b912-fac2d6f563b8", kv.NodeId);

            Assert.NotNull(kv.Pairs);
            Assert.Equal(2, kv.Pairs.Count);
            Assert.Equal("设备名称", kv.Pairs[0].Key);
            Assert.Equal("110kV,洛口变电站主变改造工程 2#主变", kv.Pairs[0].Value.Raw);
            Assert.Equal(0, kv.Pairs[0].RowIndex);
            Assert.Equal(2, kv.Pairs[0].Page);
            Assert.Equal("额定容量", kv.Pairs[1].Key);
            Assert.Equal("63000", kv.Pairs[1].Value.Raw);
            Assert.Equal("kVA", kv.Pairs[1].Value.Unit);

            Assert.NotNull(kv.Location[0].RowRange);
            Assert.Equal(new[] { 1, 7 }, kv.Location[0].RowRange);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LoadTreeJson_ParsesNodeLevelToc_WithNumberingAndRowRange()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "caly-popo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var jsonPath = Path.Combine(tempDir, "tree.json");
        File.WriteAllText(jsonPath, """
        {
          "type": "root",
          "title": "Root",
          "content": "",
          "level": 0,
          "location": [],
          "block_ids": [],
          "children": [
            {
              "type": "table",
              "title": "试验报告",
              "content": "",
              "level": 2,
              "location": [],
              "block_ids": [],
              "children": [],
              "toc": [
                {
                  "title": "1.设备参数",
                  "numbering": "1.",
                  "level": 1,
                  "row_range": [1, 7],
                  "source": "table",
                  "page": 2
                },
                {
                  "title": "2.绝缘电阻",
                  "numbering": "2.",
                  "level": 1,
                  "row_range": [8, 12],
                  "source": "table",
                  "page": 3
                }
              ]
            }
          ]
        }
        """);

        try
        {
            var result = PopoJsonService.LoadTreeJson(jsonPath);

            var table = result.Children[0];
            Assert.NotNull(table.Toc);
            Assert.Equal(2, table.Toc.Count);
            Assert.Equal("1.设备参数", table.Toc[0].Title);
            Assert.Equal("1.", table.Toc[0].Numbering);
            Assert.Equal(1, table.Toc[0].Level);
            Assert.Equal(2, table.Toc[0].Page);
            Assert.Equal("table", table.Toc[0].Source);
            Assert.NotNull(table.Toc[0].RowRange);
            Assert.Equal(new[] { 1, 7 }, table.Toc[0].RowRange);
            Assert.Equal("2.绝缘电阻", table.Toc[1].Title);
            Assert.Equal(new[] { 8, 12 }, table.Toc[1].RowRange);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LoadTreeJson_OldFormatWithoutTableFields_ParsesWithoutError()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "caly-popo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var jsonPath = Path.Combine(tempDir, "tree.json");
        File.WriteAllText(jsonPath, """
        {
          "type": "root",
          "title": "Root",
          "content": "",
          "level": 0,
          "location": [],
          "block_ids": [],
          "children": [
            {
              "type": "table",
              "title": "Legacy table",
              "content": "a | b",
              "level": 2,
              "location": [
                { "bbox": [0.1, 0.2, 0.9, 0.5], "page": 1 }
              ],
              "block_ids": [1],
              "children": []
            }
          ]
        }
        """);

        try
        {
            var result = PopoJsonService.LoadTreeJson(jsonPath);

            var table = result.Children[0];
            Assert.Equal("table", table.Type);
            Assert.Empty(table.NodeId);
            Assert.Null(table.Headers);
            Assert.Null(table.Rows);
            Assert.Null(table.Pairs);
            Assert.Null(table.Toc);
            Assert.Null(table.Location[0].RowRange);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    #endregion

    #region HTML Table Parser Tests

    [Fact]
    public void HtmlTableParser_ParsesSimpleTable()
    {
        var grid = HtmlTableParser.Parse("<table><tr><td>a</td><td>b</td></tr><tr><td>c</td><td>d</td></tr></table>");

        Assert.NotNull(grid);
        Assert.Equal(2, grid.Count);
        Assert.Equal(new[] { "a", "b" }, grid[0]);
        Assert.Equal(new[] { "c", "d" }, grid[1]);
    }

    [Fact]
    public void HtmlTableParser_HandlesColspanAndRowspan()
    {
        // colspan=2: text in first cell, second empty; rowspan=2: text repeated in both rows
        var html = """
            <table>
              <tr><td rowspan="2">R</td><td colspan="2">C1 C2</td></tr>
              <tr><td>x</td><td>y</td></tr>
            </table>
            """;
        var grid = HtmlTableParser.Parse(html);

        Assert.NotNull(grid);
        Assert.Equal(2, grid.Count);
        // Row 0: [R, C1 C2, ""]
        Assert.Equal(3, grid[0].Count);
        Assert.Equal("R", grid[0][0]);
        Assert.Equal("C1 C2", grid[0][1]);
        Assert.Equal(string.Empty, grid[0][2]);
        // Row 1: [R (rowspan fill), x, y]
        Assert.Equal("R", grid[1][0]);
        Assert.Equal("x", grid[1][1]);
        Assert.Equal("y", grid[1][2]);
    }

    [Fact]
    public void HtmlTableParser_StripsTagsAndDecodesEntities()
    {
        var html = "<table><tr><th><b>名称</b></th><td>A&amp;B<br/>C&nbsp;D</td></tr></table>";
        var grid = HtmlTableParser.Parse(html);

        Assert.NotNull(grid);
        Assert.Equal("名称", grid[0][0]);
        Assert.Equal("A&B C D", grid[0][1]);
    }

    [Fact]
    public void HtmlTableParser_ReturnsNullForNonTableInput()
    {
        Assert.Null(HtmlTableParser.Parse("plain text without table"));
        Assert.Null(HtmlTableParser.Parse(string.Empty));
        Assert.Null(HtmlTableParser.Parse(null!));
    }

    [Fact]
    public void LoadTreeJson_LegacyHtmlTable_PopulatesGridRows()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "caly-popo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var jsonPath = Path.Combine(tempDir, "tree.json");
        File.WriteAllText(jsonPath, """
        {
          "type": "root",
          "title": "Root",
          "content": "",
          "level": 0,
          "location": [],
          "block_ids": [],
          "children": [
            {
              "type": "table",
              "title": "2.试验依据",
              "content": "<table><tr><td colspan=\"2\">2.试验依据</td></tr><tr><td>GB 50150</td><td>QGDW 11447</td></tr></table>",
              "level": 2,
              "location": [
                { "bbox": [0.1, 0.2, 0.9, 0.5], "page": 1 }
              ],
              "block_ids": [1],
              "children": []
            }
          ]
        }
        """);

        try
        {
            var result = PopoJsonService.LoadTreeJson(jsonPath);

            var table = result.Children[0];
            Assert.Equal("table", table.Type);
            Assert.NotNull(table.GridRows);
            Assert.Equal(2, table.GridRows.Count);
            // colspan=2: text in first cell, second empty
            Assert.Equal(new[] { "2.试验依据", string.Empty }, table.GridRows[0]);
            Assert.Equal(new[] { "GB 50150", "QGDW 11447" }, table.GridRows[1]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LoadTreeJson_NonTableNode_GridRowsIsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "caly-popo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var jsonPath = Path.Combine(tempDir, "tree.json");
        File.WriteAllText(jsonPath, """
        {
          "type": "root",
          "title": "Root",
          "content": "",
          "level": 0,
          "location": [],
          "block_ids": [],
          "children": [
            {
              "type": "text",
              "title": "",
              "content": "some <table>looking</table> text",
              "level": 1,
              "location": [],
              "block_ids": [1],
              "children": []
            }
          ]
        }
        """);

        try
        {
            var result = PopoJsonService.LoadTreeJson(jsonPath);
            Assert.Null(result.Children[0].GridRows);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LoadTreeJson_NewPopoFormat_ParsesLocationRowRange_ObjectHeaders_MixedSourceBlockIds()
    {
        // Representative snippet of the newer Popo output format (as emitted for
        // 洛口主变试验报告): a table parent with raw HTML content plus structured
        // table_data / table_key_value children. The children carry:
        //   - location entries with row_range
        //   - object-form headers { index, name, group, unit }
        //   - rows / pairs with per-row page
        //   - source_block_ids mixing UUIDs and "docname:page" strings
        var tempDir = Path.Combine(Path.GetTempPath(), "caly-popo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var jsonPath = Path.Combine(tempDir, "tree.json");
        File.WriteAllText(jsonPath, """
        {
          "type": "root",
          "title": "",
          "content": "",
          "level": 0,
          "location": [],
          "block_ids": [],
          "source_block_ids": [],
          "children": [
            {
              "type": "table",
              "title": "",
              "content": "<table><tr><td colspan=\"2\">3.绕组直流电阻</td></tr><tr><td>绕组</td><td>高压侧</td></tr></table>",
              "level": 2,
              "location": [ { "bbox": [0.06, 0.29, 0.93, 0.92], "page": 2 } ],
              "block_ids": [8, 10],
              "source_block_ids": [ "4fa1cd86-4694-49c4-9128-82e260d247d8", "洛口主变试验报告:4" ],
              "children": [
                {
                  "type": "table_data",
                  "title": "3.绕组连同套管的直流电阻",
                  "content": "",
                  "level": 3,
                  "location": [
                    { "bbox": [0.06, 0.29, 0.93, 0.92], "page": 2, "row_range": [2, 29] },
                    { "bbox": [0.06, 0.03, 0.93, 0.70], "page": 3, "row_range": [0, 27] }
                  ],
                  "block_ids": [8, 10],
                  "source_block_ids": [ "4fa1cd86-4694-49c4-9128-82e260d247d8", "洛口主变试验报告:4" ],
                  "headers": [
                    { "index": 0, "name": "绕组", "group": null, "unit": null },
                    { "index": 1, "name": "实测值(Ω)", "group": null, "unit": "Ω" }
                  ],
                  "rows": [
                    {
                      "row_index": 2,
                      "page": 2,
                      "cells": {
                        "绕组": { "raw": "高压侧", "value": "高压侧" },
                        "实测值(Ω)": { "raw": "0.3523", "value": 0.3523, "unit": "Ω" }
                      }
                    }
                  ],
                  "children": []
                },
                {
                  "type": "table_key_value",
                  "title": "3.绕组连同套管的直流电阻",
                  "content": "",
                  "level": 3,
                  "location": [ { "bbox": [0.06, 0.08, 0.93, 0.10], "page": 2, "row_range": [0, 1] } ],
                  "block_ids": [5],
                  "source_block_ids": [ "4fa1cd86-4694-49c4-9128-82e260d247d8" ],
                  "pairs": [
                    { "key": "试验温度", "value": { "raw": "18.1", "value": 18.1, "unit": "°C" }, "row_index": 0, "page": 2 }
                  ],
                  "children": []
                }
              ]
            }
          ]
        }
        """);

        try
        {
            var result = PopoJsonService.LoadTreeJson(jsonPath);
            var table = result.Children[0];

            // Parent table: HTML content parsed into a grid; block/source ids parsed.
            Assert.Equal("table", table.Type);
            Assert.NotNull(table.GridRows);
            Assert.Equal(2, table.GridRows.Count);
            Assert.Equal(new[] { 8, 10 }, table.BlockIds);
            // Mixed source_block_ids (UUID + "docname:page") are both preserved as strings.
            Assert.Equal(2, table.SourceBlockIds.Count);
            Assert.Contains("4fa1cd86-4694-49c4-9128-82e260d247d8", table.SourceBlockIds);
            Assert.Contains("洛口主变试验报告:4", table.SourceBlockIds);

            // table_data child: object-form headers flattened to names; rows + page parsed.
            var data = table.Children[0];
            Assert.Equal("table_data", data.Type);
            Assert.NotNull(data.Headers);
            Assert.Equal(new[] { "绕组", "实测值(Ω)" }, data.Headers.Select(h => h.Name));
            Assert.NotNull(data.Rows);
            Assert.Single(data.Rows);
            Assert.Equal(2, data.Rows[0].Page);
            Assert.Equal("0.3523", data.Rows[0].Cells["实测值(Ω)"].Raw);
            Assert.Equal("Ω", data.Rows[0].Cells["实测值(Ω)"].Unit);
            // location row_range parsed.
            Assert.Equal(2, data.Location.Count);
            Assert.NotNull(data.Location[0].RowRange);
            Assert.Equal(new[] { 2, 29 }, data.Location[0].RowRange);
            Assert.Equal(2, data.Location[0].Page);

            // table_key_value child: pairs parsed with unit.
            var kv = table.Children[1];
            Assert.Equal("table_key_value", kv.Type);
            Assert.NotNull(kv.Pairs);
            Assert.Single(kv.Pairs);
            Assert.Equal("试验温度", kv.Pairs[0].Key);
            Assert.Equal("18.1", kv.Pairs[0].Value.Raw);
            Assert.Equal("°C", kv.Pairs[0].Value.Unit);
            Assert.Equal(2, kv.Pairs[0].Page);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LoadTreeJson_ObjectHeaders_PreservesGroupAndUnit()
    {
        // The newer Popo format emits object-form headers carrying group (multi-tier
        // header parent) and unit, which the UI uses for display.
        var tempDir = Path.Combine(Path.GetTempPath(), "caly-popo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var jsonPath = Path.Combine(tempDir, "tree.json");
        File.WriteAllText(jsonPath, """
        {
          "type": "root",
          "title": "",
          "content": "",
          "level": 0,
          "location": [],
          "block_ids": [],
          "children": [
            {
              "type": "table_data",
              "title": "5.绝缘电阻",
              "content": "",
              "level": 3,
              "location": [],
              "block_ids": [1],
              "headers": [
                { "index": 0, "name": "项目", "group": null, "unit": null },
                { "index": 1, "name": "15s", "group": "绝缘电阻(GΩ)", "unit": null },
                { "index": 2, "name": "60s", "group": "绝缘电阻(GΩ)", "unit": null },
                { "index": 3, "name": "吸收比", "group": null, "unit": null }
              ],
              "rows": [
                { "row_index": 0, "cells": { "15s": { "raw": "100", "value": 100 } }, "page": 1 }
              ],
              "children": []
            }
          ]
        }
        """);

        try
        {
            var result = PopoJsonService.LoadTreeJson(jsonPath);
            var data = result.Children[0];

            Assert.NotNull(data.Headers);
            Assert.Equal(4, data.Headers.Count);
            Assert.Equal(0, data.Headers[0].Index);
            Assert.Equal("项目", data.Headers[0].Name);
            Assert.Null(data.Headers[0].Group);
            Assert.Null(data.Headers[0].Unit);
            Assert.Equal(1, data.Headers[1].Index);
            Assert.Equal("15s", data.Headers[1].Name);
            Assert.Equal("绝缘电阻(GΩ)", data.Headers[1].Group);
            Assert.Equal("绝缘电阻(GΩ)", data.Headers[2].Group);
            Assert.Equal("吸收比", data.Headers[3].Name);
            Assert.Null(data.Headers[3].Group);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryParsePopoResultJson_NumericCells_SurviveDocumentLifetime()
    {
        // Regression: TryParsePopoResultJson used to dispose its JsonDocument at the
        // end of the method, leaving TableCellValue.Value JsonElements dangling.
        // Building the DataGrid columns at render time (IsNumericColumn) then threw
        // ObjectDisposedException. IsNumeric is now captured at parse time and the
        // document is intentionally kept alive by the stored elements.
        var tempDir = Path.Combine(Path.GetTempPath(), "caly-popo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var jsonPath = Path.Combine(tempDir, "popo_result.json");
        File.WriteAllText(jsonPath, """
        {
          "task_id": "t1",
          "status": "completed",
          "result": {
            "doc_id": "d1",
            "status": "completed",
            "message": "",
            "tree": {
              "type": "root",
              "title": "",
              "content": "",
              "level": 0,
              "location": [],
              "block_ids": [],
              "children": [
                {
                  "type": "table_data",
                  "title": "3.直流电阻",
                  "content": "",
                  "level": 3,
                  "location": [],
                  "block_ids": [1],
                  "headers": [
                    { "index": 0, "name": "相别", "group": null, "unit": null },
                    { "index": 1, "name": "实测值", "group": null, "unit": "Ω" }
                  ],
                  "rows": [
                    {
                      "row_index": 0,
                      "cells": {
                        "相别": { "raw": "A", "value": "A" },
                        "实测值": { "raw": "0.3523", "value": 0.3523, "unit": "Ω" }
                      },
                      "page": 2
                    }
                  ],
                  "children": []
                }
              ]
            },
            "toc": null
          }
        }
        """);

        try
        {
            var doc = PopoJsonService.TryParsePopoResultJson(jsonPath);
            Assert.NotNull(doc);
            var table = doc!.TreeRoot!.Children[0];

            // IsNumeric captured at parse time.
            Assert.True(table.Rows![0].Cells["实测值"].IsNumeric);
            Assert.False(table.Rows[0].Cells["相别"].IsNumeric);

            // Render-time column building must not throw (document lifetime regression)
            // and must right-align the numeric column.
            var vm = new TreeNodeViewModel(table);
            var columns = vm.TableColumns;
            Assert.NotNull(columns);
            Assert.Equal(2, columns!.Count);
            Assert.IsType<DataGridTextColumn>(columns[0]);
            Assert.IsType<DataGridTemplateColumn>(columns[1]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TableHeader_DisplayName_FlattensGroupAndUnitWithoutDuplication()
    {
        // Plain name
        Assert.Equal("绕组", new TableHeader { Name = "绕组" }.DisplayName);

        // Group prefix when not already part of the name
        Assert.Equal(
            "绝缘电阻(GΩ)·15s",
            new TableHeader { Name = "15s", Group = "绝缘电阻(GΩ)" }.DisplayName);

        // Unit suffix when not already part of the name
        Assert.Equal(
            "实测值 (Ω)",
            new TableHeader { Name = "实测值", Unit = "Ω" }.DisplayName);

        // No duplication when name already contains group / unit
        Assert.Equal(
            "出厂值(Ω)(18.1°C)",
            new TableHeader { Name = "出厂值(Ω)(18.1°C)", Unit = "Ω" }.DisplayName);
        Assert.Equal(
            "高压/低压·额定变比",
            new TableHeader { Name = "额定变比", Group = "高压/低压" }.DisplayName);
    }

    [Fact]
    public void TableHeaderJsonConverter_AcceptsLegacyStringFormAndRoundTripsObjectForm()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new TableHeaderJsonConverter());

        // Legacy cached popo.json files store headers as plain strings.
        var legacy = JsonSerializer.Deserialize<List<TableHeader>>("[\"名称\", \"数值\"]", options);
        Assert.NotNull(legacy);
        Assert.Equal(new[] { "名称", "数值" }, legacy.Select(h => h.Name));
        Assert.All(legacy, h => Assert.Null(h.Group));
        Assert.All(legacy, h => Assert.Null(h.Unit));

        // Object form round-trips all fields.
        var header = new TableHeader { Index = 2, Name = "60s", Group = "绝缘电阻(GΩ)", Unit = "GΩ" };
        var json = JsonSerializer.Serialize(new List<TableHeader> { header }, options);
        var roundTripped = JsonSerializer.Deserialize<List<TableHeader>>(json, options);
        Assert.NotNull(roundTripped);
        var back = roundTripped[0];
        Assert.Equal(2, back.Index);
        Assert.Equal("60s", back.Name);
        Assert.Equal("绝缘电阻(GΩ)", back.Group);
        Assert.Equal("GΩ", back.Unit);
    }

    #endregion

    #region GenerateTableMarkdown Tests

    [Fact]
    public void GenerateTableMarkdown_TableData_ProducesPipeTable()
    {
        var node = new AnalysisTreeNode
        {
            Type = "table_data",
            Headers = new List<TableHeader> { new() { Name = "绕组" }, new() { Name = "实测值(Ω)" } },
            Rows = new List<TableDataRow>
            {
                new()
                {
                    RowIndex = 0,
                    Cells = new Dictionary<string, TableCellValue>
                    {
                        ["绕组"] = new() { Raw = "高压侧" },
                        ["实测值(Ω)"] = new() { Raw = "1.234" }
                    }
                }
            }
        };

        var md = PopoJsonService.GenerateTableMarkdown(node);

        Assert.Equal(
            """
            | 绕组 | 实测值(Ω) |
            | --- | --- |
            | 高压侧 | 1.234 |
            """,
            md);
    }

    [Fact]
    public void GenerateTableMarkdown_TableData_EscapesPipeCharacters()
    {
        var node = new AnalysisTreeNode
        {
            Type = "table_data",
            Headers = new List<TableHeader> { new() { Name = "A" } },
            Rows = new List<TableDataRow>
            {
                new()
                {
                    RowIndex = 0,
                    Cells = new Dictionary<string, TableCellValue>
                    {
                        ["A"] = new() { Raw = "x|y" }
                    }
                }
            }
        };

        var md = PopoJsonService.GenerateTableMarkdown(node);

        Assert.Contains("| x\\|y |", md);
    }

    [Fact]
    public void GenerateTableMarkdown_KeyValue_ProducesList()
    {
        var node = new AnalysisTreeNode
        {
            Type = "table_key_value",
            Pairs = new List<TableKeyValuePair>
            {
                new() { Key = "电压", Value = new TableCellValue { Raw = "110", Unit = "kV" } },
                new() { Key = "容量", Value = new TableCellValue { Raw = "50" } }
            }
        };

        var md = PopoJsonService.GenerateTableMarkdown(node);

        Assert.Equal("- 电压: 110 kV\n- 容量: 50", md);
    }

    [Fact]
    public void GenerateTableMarkdown_HtmlGrid_ProducesPipeTableWithFirstRowAsHeader()
    {
        var node = new AnalysisTreeNode
        {
            Type = "table",
            GridRows = new List<List<string>>
            {
                new() { "名称", "单位" },
                new() { "直流电阻", "Ω" }
            }
        };

        var md = PopoJsonService.GenerateTableMarkdown(node);

        Assert.Equal(
            """
            | 名称 | 单位 |
            | --- | --- |
            | 直流电阻 | Ω |
            """,
            md);
    }

    [Fact]
    public void GenerateTableMarkdown_NonTableNode_ReturnsEmpty()
    {
        var node = new AnalysisTreeNode { Type = "text", Content = "hello" };
        Assert.Equal(string.Empty, PopoJsonService.GenerateTableMarkdown(node));
    }

    #endregion

    #region TreeNodeViewModel Table Display Tests

    [Fact]
    public void TreeNodeViewModel_HtmlTableNode_ShowTableGridAndCanCopyTable()
    {
        var node = new AnalysisTreeNode
        {
            Type = "table",
            Content = "<table><tr><td>a</td></tr></table>",
            GridRows = new List<List<string>> { new() { "a" } }
        };

        var vm = new TreeNodeViewModel(node);

        Assert.True(vm.IsHtmlTable);
        Assert.True(vm.ShowTableGrid);
        Assert.True(vm.CanCopyTable);
        Assert.Equal(DataGridHeadersVisibility.None, vm.TableHeadersVisibility);
        // HTML content preview is hidden for grid tables
        Assert.False(vm.ShowContentPreview);
        // DataGrid rows/columns built from the grid
        Assert.NotNull(vm.TableRows);
        Assert.Single(vm.TableRows);
        Assert.NotNull(vm.TableColumns);
        Assert.Single(vm.TableColumns);
    }

    [Fact]
    public void TreeNodeViewModel_StructuredTableNode_UsesColumnHeaders()
    {
        var node = new AnalysisTreeNode
        {
            Type = "table_data",
            Headers = new List<TableHeader> { new() { Name = "A" }, new() { Name = "B" } },
            Rows = new List<TableDataRow>
            {
                new()
                {
                    RowIndex = 0,
                    Cells = new Dictionary<string, TableCellValue>
                    {
                        ["A"] = new() { Raw = "1" },
                        ["B"] = new() { Raw = "2" }
                    }
                }
            }
        };

        var vm = new TreeNodeViewModel(node);

        Assert.True(vm.IsStructuredTable);
        Assert.True(vm.ShowTableGrid);
        Assert.True(vm.CanCopyTable);
        Assert.Equal(DataGridHeadersVisibility.Column, vm.TableHeadersVisibility);
        Assert.False(vm.ShowContentPreview);
    }

    [Fact]
    public void TreeNodeViewModel_NumericColumn_RightAligned_GroupHeaderFlattened()
    {
        // Column 0: text values; column 1: typed numeric values (newer Popo format).
        var node = new AnalysisTreeNode
        {
            Type = "table_data",
            Headers = new List<TableHeader>
            {
                new() { Name = "项目" },
                new() { Name = "15s", Group = "绝缘电阻(GΩ)" }
            },
            Rows = new List<TableDataRow>
            {
                new()
                {
                    RowIndex = 0,
                    Cells = new Dictionary<string, TableCellValue>
                    {
                        ["项目"] = new() { Raw = "高压侧", Value = JsonSerializer.Deserialize<JsonElement>("\"高压侧\"") },
                        ["15s"] = new() { Raw = "100", Value = JsonSerializer.Deserialize<JsonElement>("100"), IsNumeric = true, Unit = "GΩ" }
                    }
                }
            }
        };

        var vm = new TreeNodeViewModel(node);
        var columns = vm.TableColumns;

        Assert.NotNull(columns);
        Assert.Equal(2, columns.Count);

        // Header text uses the flattened DisplayName (group prefix).
        var textColumn = Assert.IsType<DataGridTextColumn>(columns[0]);
        var numericColumn = Assert.IsType<DataGridTemplateColumn>(columns[1]);
        Assert.Equal("项目", textColumn.Header);
        Assert.Equal("绝缘电阻(GΩ)·15s", numericColumn.Header);

        // Numeric column uses the right-aligned cell template;
        // text column keeps the default (left) alignment.
        Assert.NotNull(numericColumn.CellTemplate);
        Assert.IsType<DataGridTextColumn>(textColumn);
    }

    [Fact]
    public void TreeNodeViewModel_ParentTableWithStructuredChildren_HidesRedundantPreview()
    {
        var child = new AnalysisTreeNode { Type = "table_data", Headers = new List<TableHeader> { new() { Name = "A" } } };
        var node = new AnalysisTreeNode
        {
            Type = "table",
            Content = "<table><tr><td>raw html</td></tr></table>",
            // Simulate ParseTreeNode: a table node whose content contains HTML gets
            // GridRows populated. Without this the test would not catch the real
            // double-display bug (parent HTML grid + child structured grids).
            GridRows = new List<List<string>> { new() { "raw html" } }
        };
        node.Children.Add(child);

        var vm = new TreeNodeViewModel(node);

        Assert.True(vm.HasStructuredTableChildren);
        // The parent's HTML grid IS parsed (IsHtmlTable), but must not be shown
        // because the structured children already render the DataGrids.
        Assert.True(vm.IsHtmlTable);
        // Parent's raw HTML preview is hidden (children render the DataGrids)
        Assert.False(vm.ShowContentPreview);
        Assert.False(vm.ShowFullContent);
        // Parent itself does NOT show a grid (avoids double display)
        Assert.False(vm.ShowTableGrid);
        Assert.False(vm.CanCopyTable);
    }

    [Fact]
    public void TreeNodeViewModel_PlainTextNode_ShowsPreviewAndNoCopyButton()
    {
        var node = new AnalysisTreeNode { Type = "text", Content = "hello world" };

        var vm = new TreeNodeViewModel(node);

        Assert.False(vm.ShowTableGrid);
        Assert.False(vm.CanCopyTable);
        // Content is expanded by default, so the full content is shown
        Assert.True(vm.ShowFullContent);
        vm.IsContentExpanded = false;
        Assert.True(vm.ShowContentPreview);
    }

    #endregion

    #region PopoProgressRecord Serialization Tests

    [Fact]
    public void PopoProgressRecord_Serializes_WithSourceGeneratorResolver()
    {
        // Regression test: reflection-based serialization is disabled by default in .NET 10.
        // PopoService.SaveProgressRecords must serialize via SourceGenerationContext.Default,
        // otherwise it throws InvalidOperationException ("Reflection-based serialization
        // has been disabled for this application").
        var records = new List<PopoProgressRecord>
        {
            new()
            {
                TaskId = "task-123",
                Status = "processing",
                Progress = 75,
                ProgressMessage = "image: chunk 31/31",
                StartedAt = new DateTime(2026, 8, 30, 10, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2026, 8, 30, 10, 5, 0, DateTimeKind.Utc)
            }
        };

        // Mirror the exact options used by PopoService.SaveProgressRecords
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = SourceGenerationContext.Default
        };

        var json = JsonSerializer.Serialize(records, jsonOptions);
        var compact = json.Replace(" ", string.Empty);

        Assert.Contains("\"taskId\":\"task-123\"", compact);
        Assert.Contains("\"progress\":75", compact);
        Assert.Contains("\"progressMessage\":\"image:chunk31/31\"", compact);
        Assert.Contains("\"endedAt\":null", compact);
    }

    #endregion
}