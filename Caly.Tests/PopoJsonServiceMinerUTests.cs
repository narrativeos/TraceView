using Caly.Core.Services;
using System.IO;

namespace Caly.Tests;

public class PopoJsonServiceMinerUTests
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
            var result = PopoJsonService.TryParseMinerUMiddleJson(jsonPath);

            Assert.NotNull(result);
            Assert.NotEmpty(result!.GetAllBlocks());
            Assert.Equal(2, result.GetAllBlocks().Count);
            Assert.Contains(result.GetAllBlocks(), b => b.Content.Contains("hello"));
            Assert.Contains(result.GetAllBlocks(), b => b.Content.Contains("world"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
