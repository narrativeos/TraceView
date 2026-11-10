using Caly.Core.Models;
using Caly.Core.Services;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Caly.Tests;

/// <summary>
/// Covers the MinerU parse engine (<c>ocr_mode</c>): it must default to "auto", survive
/// settings round-trips (including settings files written before the parameter existed),
/// be sent to the jobs API, and never leave the app in a form the API would reject with
/// HTTP 400 — an unknown or empty value falls back to "auto" instead.
/// </summary>
public class MinerUOcrModeTests
{
    #region Defaults

    [Fact]
    public void MinerUOcrMode_DefaultsToAuto()
    {
        Assert.Equal("auto", new CalySettings().MinerUOcrMode);
        Assert.Equal("auto", CalySettings.Default.MinerUOcrMode);
        Assert.Equal("auto", MinerUService.DefaultOcrMode);
    }

    [Fact]
    public void MinerUOcrMode_StaysAuto_ForSettingsWrittenBeforeTheParameterExisted()
    {
        // Existing installs have no MinerUOcrMode in caly_settings; deserialization must
        // leave the property initializer ("auto") in place, otherwise every updated user
        // would silently lose the engine the server used to pick for them.
        var json = """{"MinerUBaseUrl":"http://localhost:8401","MinerUTier":"standard"}""";

        var settings = JsonSerializer.Deserialize(json, SourceGenerationContext.Default.CalySettings);

        Assert.NotNull(settings);
        Assert.Equal("auto", settings.MinerUOcrMode);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("txt")]
    [InlineData("ocr")]
    public void MinerUOcrMode_RoundTripsThroughSettingsJson(string ocrMode)
    {
        var settings = new CalySettings { MinerUOcrMode = ocrMode };

        var json = JsonSerializer.Serialize(settings, SourceGenerationContext.Default.CalySettings);
        var reloaded = JsonSerializer.Deserialize(json, SourceGenerationContext.Default.CalySettings);

        Assert.NotNull(reloaded);
        Assert.Equal(ocrMode, reloaded.MinerUOcrMode);
    }

    #endregion

    #region SubmitTaskAsync sends ocr_mode

    [Theory]
    [InlineData("auto", "\"ocr_mode\":\"auto\"")]
    [InlineData("txt", "\"ocr_mode\":\"txt\"")]
    [InlineData("ocr", "\"ocr_mode\":\"ocr\"")]
    // Whitespace and casing are normalized before the value goes on the wire.
    [InlineData(" TXT ", "\"ocr_mode\":\"txt\"")]
    // Empty or unknown engines fall back instead of being sent as-is or as null.
    [InlineData("", "\"ocr_mode\":\"auto\"")]
    [InlineData("vlm", "\"ocr_mode\":\"auto\"")]
    public async Task SubmitTaskAsync_SendsOcrMode(string ocrMode, string expected)
    {
        var pdfPath = Path.Combine(Path.GetTempPath(), $"caly-ocrmode-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(pdfPath, "%PDF-1.4 stub");

        try
        {
            using var listener = LoopbackListener.Start(out var baseUrl);
            string? jobBody = null;

            var serve = Task.Run(async () =>
            {
                // POST /v1/uploads answers with the instant-hit shape (sha256 already
                // known), which skips the byte upload so the job submit is next.
                var upload = await listener.GetContextAsync();
                await LoopbackListener.RespondAsync(upload, HttpStatusCode.Created,
                    """{"id":"u1","status":"completed","file":{"id":"f1"}}""");

                var job = await listener.GetContextAsync();
                using var reader = new StreamReader(job.Request.InputStream, Encoding.UTF8);
                jobBody = await reader.ReadToEndAsync();
                await LoopbackListener.RespondAsync(job, HttpStatusCode.Accepted,
                    """{"job_id":"j1","status":"queued"}""");
            });

            using var service = new MinerUService(baseUrl, Path.Combine(Path.GetTempPath(), "caly-ocrmode-cache"));
            var taskId = await service.SubmitTaskAsync(pdfPath, MinerUService.DefaultTier, null, ocrMode);
            await serve;

            Assert.Equal("j1", taskId);
            Assert.NotNull(jobBody);
            Assert.Contains(expected, jobBody);
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    #endregion
}
