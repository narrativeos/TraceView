using Caly.Core.Services;
using Caly.Core.Utilities;
using System.IO;

namespace Caly.Tests;

public class MinerUJsonServiceTests
{
    [Fact]
    public void TryParseMinerUMiddleJson_ParsesPdfInfoBlocks()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "caly-mineru-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var jsonPath = Path.Combine(tempDir, "sample_middle.json");
        File.WriteAllText(jsonPath, """
        {
          "pdf_info": [
            {
              "page_idx": 1,
              "page_size": [1920, 2560],
              "preproc_blocks": [
                {
                  "type": "text",
                  "bbox": [10, 20, 100, 40],
                  "lines": [
                    {
                      "bbox": [10, 20, 100, 40],
                      "spans": [
                        { "bbox": [10, 20, 100, 40], "type": "text", "content": "preproc" }
                      ]
                    }
                  ]
                }
              ],
              "para_blocks": [
                {
                  "type": "text",
                  "bbox": [110, 120, 200, 140],
                  "lines": [
                    {
                      "bbox": [110, 120, 200, 140],
                      "spans": [
                        { "bbox": [110, 120, 200, 140], "type": "text", "content": "hello" }
                      ]
                    }
                  ]
                }
              ],
              "discarded_blocks": [
                {
                  "type": "text",
                  "bbox": [200, 220, 300, 240],
                  "lines": [
                    {
                      "bbox": [200, 220, 300, 240],
                      "spans": [
                        { "bbox": [200, 220, 300, 240], "type": "text", "content": "discarded" }
                      ]
                    }
                  ]
                }
              ]
            }
          ]
        }
        """);

        try
        {
            var result = MinerUJsonService.TryParseMinerUMiddleJson(jsonPath);

            Assert.NotNull(result);
            Assert.NotEmpty(result!.GetAllBlocks());
            // Only para_blocks and discarded_blocks are parsed (preproc_blocks is skipped)
            Assert.Equal(2, result.GetAllBlocks().Count);
            Assert.Contains(result.GetAllBlocks(), b => b.Content.Contains("hello"));
            Assert.Contains(result.GetAllBlocks(), b => b.Content.Contains("discarded"));
            // "preproc" from preproc_blocks should NOT be parsed
            Assert.All(result.GetAllBlocks(), b => Assert.DoesNotContain("preproc", b.Content));

            // Verify BlockSource is correctly set
            var paraBlock = result.GetAllBlocks().First(b => b.Content.Contains("hello"));
            Assert.Equal(MinerUConstants.SourcePara, paraBlock.BlockSource);

            var discardedBlock = result.GetAllBlocks().First(b => b.Content.Contains("discarded"));
            Assert.Equal(MinerUConstants.SourceDiscarded, discardedBlock.BlockSource);

            // Verify that blocks with no overlap have empty RelatedBlockIds
            // (preproc block at [10,20,100,40] does not overlap with para at [110,120,200,140]
            //  or discarded at [200,220,300,240], so no cross-references are created)
            Assert.Empty(paraBlock.RelatedBlockIds);
            Assert.Empty(discardedBlock.RelatedBlockIds);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryParseMinerUMiddleJson_BuildsRelatedBlockIds_WhenOverlap()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "caly-mineru-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        // This test uses overlapping bboxes: preproc [10,20,100,40] overlaps with para [10,20,100,40]
        var jsonPath = Path.Combine(tempDir, "overlap_middle.json");
        File.WriteAllText(jsonPath, """
        {
          "pdf_info": [
            {
              "page_idx": 1,
              "page_size": [1920, 2560],
              "preproc_blocks": [
                {
                  "type": "text",
                  "bbox": [10, 20, 100, 40],
                  "lines": [
                    {
                      "bbox": [10, 20, 100, 40],
                      "spans": [
                        { "bbox": [10, 20, 100, 40], "type": "text", "content": "preproc-overlap" }
                      ]
                    }
                  ]
                }
              ],
              "para_blocks": [
                {
                  "type": "text",
                  "bbox": [10, 20, 100, 40],
                  "lines": [
                    {
                      "bbox": [10, 20, 100, 40],
                      "spans": [
                        { "bbox": [10, 20, 100, 40], "type": "text", "content": "para-overlap" }
                      ]
                    }
                  ]
                }
              ]
            }
          ]
        }
        """);

        try
        {
            var result = MinerUJsonService.TryParseMinerUMiddleJson(jsonPath);

            Assert.NotNull(result);
            // Only para_blocks is in the output (preproc_blocks is not added)
            var paraBlock = Assert.Single(result!.GetAllBlocks());
            Assert.Equal(MinerUConstants.SourcePara, paraBlock.BlockSource);
            Assert.Equal("para-overlap", paraBlock.Content);

            // The preproc block should have been matched to this para block by overlap.
            // Although preproc blocks are not in GetAllBlocks(), the para block should
            // have the preproc block's ID in its RelatedBlockIds.
            Assert.NotEmpty(paraBlock.RelatedBlockIds);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryParseMinerUMiddleJson_PreprocPrefersParaOverDiscarded()
    {
        // When a preproc block overlaps with both a para and a discarded block,
        // it should prefer the para block (higher priority).
        var tempDir = Path.Combine(Path.GetTempPath(), "caly-mineru-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var jsonPath = Path.Combine(tempDir, "prefer_para_middle.json");
        File.WriteAllText(jsonPath, """
        {
          "pdf_info": [
            {
              "page_idx": 1,
              "page_size": [1920, 2560],
              "preproc_blocks": [
                {
                  "type": "text",
                  "bbox": [10, 20, 100, 40],
                  "lines": [
                    {
                      "bbox": [10, 20, 100, 40],
                      "spans": [
                        { "bbox": [10, 20, 100, 40], "type": "text", "content": "preproc-both" }
                      ]
                    }
                  ]
                }
              ],
              "para_blocks": [
                {
                  "type": "text",
                  "bbox": [10, 20, 100, 40],
                  "lines": [
                    {
                      "bbox": [10, 20, 100, 40],
                      "spans": [
                        { "bbox": [10, 20, 100, 40], "type": "text", "content": "para-adopted" }
                      ]
                    }
                  ]
                }
              ],
              "discarded_blocks": [
                {
                  "type": "text",
                  "bbox": [10, 20, 100, 40],
                  "lines": [
                    {
                      "bbox": [10, 20, 100, 40],
                      "spans": [
                        { "bbox": [10, 20, 100, 40], "type": "text", "content": "discarded-rejected" }
                      ]
                    }
                  ]
                }
              ]
            }
          ]
        }
        """);

        try
        {
            var result = MinerUJsonService.TryParseMinerUMiddleJson(jsonPath);

            Assert.NotNull(result);
            var blocks = result!.GetAllBlocks();
            // Both para and discarded should be in the output
            Assert.Equal(2, blocks.Count);

            var paraBlock = blocks.First(b => b.BlockSource == MinerUConstants.SourcePara);
            var discardedBlock = blocks.First(b => b.BlockSource == MinerUConstants.SourceDiscarded);

            // The preproc block should be linked to para (higher priority), not discarded.
            Assert.NotEmpty(paraBlock.RelatedBlockIds);
            Assert.Empty(discardedBlock.RelatedBlockIds);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryParseMinerUMiddleJson_PreprocMatchesDiscarded_WhenNoPara()
    {
        // When a preproc block overlaps only with a discarded block (no para overlap),
        // it should correctly match the discarded block.
        var tempDir = Path.Combine(Path.GetTempPath(), "caly-mineru-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var jsonPath = Path.Combine(tempDir, "match_discarded_middle.json");
        File.WriteAllText(jsonPath, """
        {
          "pdf_info": [
            {
              "page_idx": 1,
              "page_size": [1920, 2560],
              "preproc_blocks": [
                {
                  "type": "text",
                  "bbox": [10, 20, 100, 40],
                  "lines": [
                    {
                      "bbox": [10, 20, 100, 40],
                      "spans": [
                        { "bbox": [10, 20, 100, 40], "type": "text", "content": "preproc-disc" }
                      ]
                    }
                  ]
                }
              ],
              "discarded_blocks": [
                {
                  "type": "text",
                  "bbox": [10, 20, 100, 40],
                  "lines": [
                    {
                      "bbox": [10, 20, 100, 40],
                      "spans": [
                        { "bbox": [10, 20, 100, 40], "type": "text", "content": "discarded-only" }
                      ]
                    }
                  ]
                }
              ]
            }
          ]
        }
        """);

        try
        {
            var result = MinerUJsonService.TryParseMinerUMiddleJson(jsonPath);

            Assert.NotNull(result);
            var discardedBlock = Assert.Single(result!.GetAllBlocks());
            Assert.Equal(MinerUConstants.SourceDiscarded, discardedBlock.BlockSource);
            Assert.NotEmpty(discardedBlock.RelatedBlockIds);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryParseMinerUMiddleJson_PartialOverlap_BelowThreshold()
    {
        // Preproc block with small overlap (<30%) should NOT be matched.
        // preproc: [0,0,100,100] area=10000
        // para:    [70,70,100,100] area=900, overlap=[70,70,100,100]=900
        // overlap ratio = 900/10000 = 0.09 (< 0.3 threshold)
        var tempDir = Path.Combine(Path.GetTempPath(), "caly-mineru-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var jsonPath = Path.Combine(tempDir, "below_threshold_middle.json");
        File.WriteAllText(jsonPath, """
        {
          "pdf_info": [
            {
              "page_idx": 1,
              "page_size": [1920, 2560],
              "preproc_blocks": [
                {
                  "type": "text",
                  "bbox": [0, 0, 100, 100],
                  "lines": [
                    {
                      "bbox": [0, 0, 100, 100],
                      "spans": [
                        { "bbox": [0, 0, 100, 100], "type": "text", "content": "preproc-small" }
                      ]
                    }
                  ]
                }
              ],
              "para_blocks": [
                {
                  "type": "text",
                  "bbox": [70, 70, 100, 100],
                  "lines": [
                    {
                      "bbox": [70, 70, 100, 100],
                      "spans": [
                        { "bbox": [70, 70, 100, 100], "type": "text", "content": "para-partial" }
                      ]
                    }
                  ]
                }
              ]
            }
          ]
        }
        """);

        try
        {
            var result = MinerUJsonService.TryParseMinerUMiddleJson(jsonPath);

            Assert.NotNull(result);
            var paraBlock = Assert.Single(result!.GetAllBlocks());
            Assert.Equal(MinerUConstants.SourcePara, paraBlock.BlockSource);
            // Overlap ratio ~9%, below threshold, so no cross-reference
            Assert.Empty(paraBlock.RelatedBlockIds);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryParseMinerUMiddleJson_AboveThresholdOverlap_Matches()
    {
        // Preproc block with overlap slightly above threshold (>30%) should be matched.
        // preproc: [0,0,100,100] area=10000
        // para:    [0,65,100,100] area=3500, overlap=[0,65,100,100]=3500
        // overlap ratio = 3500/10000 = 0.35 (> 0.3 threshold, should match)
        var tempDir = Path.Combine(Path.GetTempPath(), "caly-mineru-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var jsonPath = Path.Combine(tempDir, "above_threshold_middle.json");
        File.WriteAllText(jsonPath, """
        {
          "pdf_info": [
            {
              "page_idx": 1,
              "page_size": [1920, 2560],
              "preproc_blocks": [
                {
                  "type": "text",
                  "bbox": [0, 0, 100, 100],
                  "lines": [
                    {
                      "bbox": [0, 0, 100, 100],
                      "spans": [
                        { "bbox": [0, 0, 100, 100], "type": "text", "content": "preproc-above" }
                      ]
                    }
                  ]
                }
              ],
              "para_blocks": [
                {
                  "type": "text",
                  "bbox": [0, 65, 100, 100],
                  "lines": [
                    {
                      "bbox": [0, 65, 100, 100],
                      "spans": [
                        { "bbox": [0, 65, 100, 100], "type": "text", "content": "para-above" }
                      ]
                    }
                  ]
                }
              ]
            }
          ]
        }
        """);

        try
        {
            var result = MinerUJsonService.TryParseMinerUMiddleJson(jsonPath);

            Assert.NotNull(result);
            var paraBlock = Assert.Single(result!.GetAllBlocks());
            Assert.Equal(MinerUConstants.SourcePara, paraBlock.BlockSource);
            // 35% overlap, above threshold, should match
            Assert.NotEmpty(paraBlock.RelatedBlockIds);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
