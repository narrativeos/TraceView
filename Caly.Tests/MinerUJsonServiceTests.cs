using Caly.Core.Services;
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
                        { "bbox": [10, 20, 100, 40], "type": "text", "content": "hello" }
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
                        { "bbox": [110, 120, 200, 140], "type": "text", "content": "world" }
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
            // Only preproc_blocks is parsed (para_blocks is skipped to avoid duplication)
            Assert.Equal(1, result.GetAllBlocks().Count);
            Assert.Contains(result.GetAllBlocks(), b => b.Content.Contains("hello"));
            // "world" from para_blocks should NOT be parsed
            Assert.All(result.GetAllBlocks(), b => Assert.DoesNotContain("world", b.Content));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}