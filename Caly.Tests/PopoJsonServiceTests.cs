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
using System.IO;

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
    public void SaveMinerUDocumentToProject_And_LoadBack()
    {
        var projectPath = Path.Combine(Path.GetTempPath(), "caly-popo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectPath);

        try
        {
            var doc = new Caly.Core.Models.MinerUDocument
            {
                ModelName = "test_model",
                DocId = "test_doc"
            };

            PopoJsonService.SaveMinerUDocumentToProject(doc, projectPath);

            var popoDir = Path.Combine(projectPath, "popo");
            Assert.True(Directory.Exists(popoDir));

            var popoJsonPath = Path.Combine(popoDir, "popo.json");
            Assert.True(File.Exists(popoJsonPath));

            var loaded = PopoJsonService.LoadMinerUDocumentFromProject(projectPath);
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
    public void LoadMinerUDocumentFromProject_ReturnsNullForNonExistentProject()
    {
        var result = PopoJsonService.LoadMinerUDocumentFromProject("/nonexistent/path");
        Assert.Null(result);
    }

    [Fact]
    public void LoadMinerUDocumentFromProject_ReturnsNullForNullPath()
    {
        var result = PopoJsonService.LoadMinerUDocumentFromProject(null);
        Assert.Null(result);
    }

    #endregion
}