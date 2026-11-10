using Caly.Core.Models;
using Caly.Core.Services;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Caly.Tests;

/// <summary>
/// Covers the Popo "output_epub" parameter: it must default to on, survive settings
/// round-trips, be sent to the server, and the EPUB retrieval must handle the server
/// answering 409 when the task was submitted without it.
/// </summary>
public class PopoEpubOutputTests
{
    #region Defaults

    [Fact]
    public void PopoOutputEpub_DefaultsToOn()
    {
        Assert.True(new CalySettings().PopoOutputEpub);
        Assert.True(CalySettings.Default.PopoOutputEpub);
    }

    [Fact]
    public void PopoOutputEpub_StaysOn_ForSettingsWrittenBeforeTheParameterExisted()
    {
        // Existing installs have no PopoOutputEpub in caly_settings; deserialization must
        // leave the property initializer (true) in place, otherwise EPUB would silently
        // turn off for everyone after the update.
        var json = """{"PopoBaseUrl":"http://localhost:8412","PopoModel":"mineru"}""";

        var settings = JsonSerializer.Deserialize(json, SourceGenerationContext.Default.CalySettings);

        Assert.NotNull(settings);
        Assert.True(settings.PopoOutputEpub);
    }

    [Fact]
    public void PopoOutputEpub_RoundTripsThroughSettingsJson()
    {
        var settings = new CalySettings { PopoOutputEpub = false };

        var json = JsonSerializer.Serialize(settings, SourceGenerationContext.Default.CalySettings);
        var reloaded = JsonSerializer.Deserialize(json, SourceGenerationContext.Default.CalySettings);

        Assert.NotNull(reloaded);
        Assert.False(reloaded.PopoOutputEpub);
    }

    #endregion

    #region SubmitTaskAsync sends output_epub

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public async Task SubmitTaskAsync_SendsOutputEpubFormField(bool outputEpub, string expectedValue)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"caly-popo-epub-{Guid.NewGuid():N}.zip");
        File.WriteAllBytes(zipPath, [0x50, 0x4b, 0x03, 0x04]);

        try
        {
            using var listener = LoopbackListener.Start(out var baseUrl);
            string? body = null;

            var serve = Task.Run(async () =>
            {
                var context = await listener.GetContextAsync();
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                body = await reader.ReadToEndAsync();

                var bytes = Encoding.UTF8.GetBytes("""{"task_id":"t1","status":"pending"}""");
                context.Response.StatusCode = (int)HttpStatusCode.Accepted;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            });

            using var service = new PopoService(baseUrl, Path.Combine(Path.GetTempPath(), "caly-popo-epub-cache"));
            var taskId = await service.SubmitTaskAsync(zipPath, "mineru", "doc1", outputEpub);
            await serve;

            Assert.Equal("t1", taskId);
            Assert.NotNull(body);
            Assert.Contains("name=\"output_epub\"", body);
            // The field value sits on its own line between CRLFs.
            Assert.Contains($"\r\n{expectedValue}\r\n", body);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    #endregion

    #region DownloadEpubAsync

    [Fact]
    public async Task DownloadEpubAsync_ReturnsNull_WhenServerNeverGeneratedAnEpub()
    {
        using var listener = LoopbackListener.Start(out var baseUrl);
        string? requestedFormat = null;

        var serve = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            requestedFormat = context.Request.QueryString["format"];
            // The server signals "this task was submitted with output_epub off" with 409.
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            context.Response.Close();
        });

        using var service = new PopoService(baseUrl, Path.Combine(Path.GetTempPath(), "caly-popo-epub-cache"));
        var epub = await service.DownloadEpubAsync("t9");
        await serve;

        Assert.Null(epub);
        Assert.Equal("epub", requestedFormat);
    }

    [Fact]
    public async Task DownloadEpubAsync_ReturnsTheEpubBytes()
    {
        using var listener = LoopbackListener.Start(out var baseUrl);
        var payload = new byte[] { 0x50, 0x4b, 0x03, 0x04, 0x6d, 0x69, 0x6d };

        var serve = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "application/epub+zip";
            await context.Response.OutputStream.WriteAsync(payload);
            context.Response.Close();
        });

        using var service = new PopoService(baseUrl, Path.Combine(Path.GetTempPath(), "caly-popo-epub-cache"));
        var epub = await service.DownloadEpubAsync("t9");
        await serve;

        Assert.Equal(payload, epub);
    }

    #endregion
}
