using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Caly.Tests;

/// <summary>
/// One-off LIVE smoke against the real jiulu_mcp hub: loads a real Popo project,
/// builds the push plan, searches for duplicates, then creates the book and
/// chapter DRAFTS only (never submits to audit). Inert unless CALY_JIULU_SMOKE_PROJECT
/// is set. Its main purpose is to confirm the draft-response shape (where the new
/// book id lands), which unit tests cannot observe.
/// </summary>
public class JiuluLiveSmokeTests
{
    private static string LogPath => Path.Combine(Path.DirectorySeparatorChar + "tmp", "caly-jiulu-smoke.log");

    [Fact]
    public async Task LiveSmoke_CreateDrafts_WhenEnabled()
    {
        var projectPath = Environment.GetEnvironmentVariable("CALY_JIULU_SMOKE_PROJECT");
        if (string.IsNullOrWhiteSpace(projectPath))
            return; // smoke is opt-in; keep the default suite hermetic

        var log = new StringBuilder();
        void Log(string line)
        {
            log.AppendLine(line);
            File.WriteAllText(LogPath, log.ToString());
        }

        var document = PopoJsonService.LoadStructureDocumentFromProject(projectPath)
            ?? PopoJsonService.TryParsePopoResultDir(Path.Combine(projectPath!, "popo"));
        Assert.NotNull(document);

        var docName = Path.GetFileName(projectPath!.TrimEnd('/', '\\'));
        var plan = JiuluPublisher.BuildPlan(document, docName);
        Log($"plan: {JiuluPublisher.SummarizePlan(plan)}");
        Log($"front matter: {(plan.BookDraft.ContentMarkdown?.Length ?? 0)} 字");

        if (Environment.GetEnvironmentVariable("CALY_JIULU_SMOKE_DRY") == "1")
        {
            for (var i = 0; i < plan.Chapters.Count; i++)
            {
                var c = plan.Chapters[i];
                Log($"  dry [{i + 1}] {c.Title} pp.{c.PageStart}-{c.PageEnd} md={c.ContentMarkdown?.Length ?? 0} txt={c.ContentText?.Length ?? 0}");
            }
            Log("dry run: plan only, no network calls.");
            return;
        }

        var token = Environment.GetEnvironmentVariable("JIULU_MCP_TOKEN");
        Assert.False(string.IsNullOrWhiteSpace(token), "JIULU_MCP_TOKEN must be set for the live smoke.");

        var settings = new SmokeSettingsService(new CalySettings
        {
            McpServers =
            [
                new McpServerConfig
                {
                    Name = JiuluPublisher.ServerName,
                    DisplayName = "九录文华·时空知识中枢MCP服务",
                    Url = "https://services.jiulu.ltd/mcp",
                    CredentialRef = "env:JIULU_MCP_TOKEN",
                },
            ],
        });
        await using var registry = new McpRegistryService(settings, new OsSecretStore());
        var publisher = new JiuluPublisher(registry);

        // Duplicate check (abort on hits unless forced).
        var search = await publisher.SearchBooksAsync(plan.DocName);
        var total = JiuluPublisher.SearchTotalMatches(search);
        Log($"search total: {total}");
        if (total is > 0 && Environment.GetEnvironmentVariable("CALY_JIULU_SMOKE_FORCE") != "1")
        {
            Log("aborted: duplicates found (set CALY_JIULU_SMOKE_FORCE=1 to push anyway)");
            Assert.Fail($"Found {total} existing books named {plan.DocName}; smoke aborted.");
        }

        var bookResult = await publisher.CreateBookDraftAsync(plan.BookDraft);
        Log($"book draft raw text: {JiuluPublisher.ResponseText(bookResult)}");
        if (bookResult.StructuredContent is System.Text.Json.JsonElement structured)
            Log($"book draft structured: {structured.GetRawText()}");
        var bookId = JiuluPublisher.ExtractDraftId(bookResult);
        Log($"extracted book_id: {bookId?.ToString() ?? "<none>"}");
        Assert.NotNull(bookId);

        var maxChapters = int.TryParse(Environment.GetEnvironmentVariable("CALY_JIULU_SMOKE_MAX_CHAPTERS"), out var m)
            ? Math.Min(m, plan.Chapters.Count)
            : plan.Chapters.Count;

        for (var i = 0; i < maxChapters; i++)
        {
            var chapter = plan.Chapters[i];
            var chapterResult = await publisher.CreateChapterDraftAsync(bookId!.Value, chapter);
            Log($"chapter [{i + 1}/{plan.Chapters.Count}] {chapter.Title}: {JiuluPublisher.ResponseText(chapterResult)}");
        }

        Log($"done. drafts only — NOT submitted to audit. clean up on the platform: book_id={bookId}");
    }

    private sealed class SmokeSettingsService(CalySettings settings) : ISettingsService
    {
        public CalySettings GetSettings() => settings;
        public ValueTask<CalySettings> GetSettingsAsync() => ValueTask.FromResult(settings);
        public void SetProperty(CalySettings.CalySettingsProperty property, object value) { }
        public void Load() { }
        public Task LoadAsync() => Task.CompletedTask;
        public void Save() { }
        public Task SaveAsync() => Task.CompletedTask;
    }
}
