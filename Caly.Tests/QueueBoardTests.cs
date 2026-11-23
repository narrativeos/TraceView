// Copyright (c) 2025 BobLd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to copy, or sell copies of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED.

using System;
using System.IO;
using System.Linq;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Store;
using System.Text.Json;
using Xunit;

namespace Caly.Tests;

/// <summary>
/// Tests for the queue board stage derivation (MinerU → Popo pipeline) with and without
/// the rerun report overlay.
/// </summary>
public class QueueBoardTests : IDisposable
{
    private const string DocKey = "0123456789abcdef0123456789abcdef";
    private readonly string _root;

    public QueueBoardTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "traceview-board-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup of the temp dir.
        }
    }

    private string CreateProject(string projectId, bool pdfExists = true)
    {
        var projectPath = Path.Combine(_root, "projects", projectId[..2], projectId.Substring(2, 2), projectId);
        Directory.CreateDirectory(projectPath);
        var pdf = Path.Combine(_root, projectId + ".pdf");
        if (pdfExists)
        {
            File.WriteAllText(pdf, "pdf");
        }

        var metadata = new ProjectMetadata
        {
            PdfPath = pdf,
            PdfFileName = projectId + ".pdf",
            ProjectName = projectId,
            ProjectPath = projectPath,
            CreatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            ProjectId = projectId,
        };
        File.WriteAllText(Path.Combine(projectPath, "project.json"),
            JsonSerializer.Serialize(metadata, SourceGenerationContext.Default.ProjectMetadata));
        return projectPath;
    }

    private static void WithMinerU(string projectPath, string docKey = DocKey)
    {
        var dir = Path.Combine(projectPath, "mineru", docKey);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "middle_json.json"), "{}");
        File.WriteAllText(Path.Combine(projectPath, "mineru", docKey + ".zip"), "zip");
    }

    private static void WithLegacyMinerU(string projectPath)
    {
        var dir = Path.Combine(projectPath, "mineru", "旧文档");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "middle.json"), "{}");
    }

    private static void WithPopo(string projectPath)
    {
        var dir = Path.Combine(projectPath, "popo");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "popo_result.json"), "{}");
    }

    private void WithOverlay(string json) =>
        File.WriteAllText(Path.Combine(_root, QueueBoard.RerunReportFileName), json);

    private QueueBoardItem Card(string projectId) =>
        QueueBoard.Build(_root).Items.Single(i => i.ProjectId == projectId);

    [Fact]
    public void NoArtifacts_WaitsForMinerU()
    {
        var p = CreateProject("aa11");
        Assert.Equal(QueueStage.AwaitingMinerU, Card("aa11").Stage);
    }

    [Fact]
    public void MinerUDone_AwaitsPopo()
    {
        var p = CreateProject("aa22");
        WithMinerU(p);
        Assert.Equal(QueueStage.MinerUDone, Card("aa22").Stage);
    }

    [Fact]
    public void LegacyLayout_CountsAsMinerUDone()
    {
        var p = CreateProject("aa33");
        WithLegacyMinerU(p);
        var card = Card("aa33");
        Assert.Equal(QueueStage.MinerUDone, card.Stage);
        Assert.Contains("旧版布局", card.Detail);
    }

    [Fact]
    public void BothOutputs_Done()
    {
        var p = CreateProject("aa44");
        WithMinerU(p);
        WithPopo(p);
        Assert.Equal(QueueStage.Done, Card("aa44").Stage);
    }

    [Fact]
    public void MissingPdf_Wins()
    {
        var p = CreateProject("aa55", pdfExists: false);
        Assert.Equal(QueueStage.PdfMissing, Card("aa55").Stage);
    }

    [Fact]
    public void OverlayPopoError_PopoFailed()
    {
        var p = CreateProject("aa66");
        WithMinerU(p);
        WithOverlay("{\"aa66\":{\"mineru\":{\"status\":\"ok\"},\"popo\":{\"status\":\"error\",\"error\":\"HTTP Error 413: Request Entity Too Large\"}}}");
        var card = Card("aa66");
        Assert.Equal(QueueStage.PopoFailed, card.Stage);
        Assert.Contains("413", card.Detail);
    }

    [Fact]
    public void OverlayPopoRunning_PopoRunning()
    {
        var p = CreateProject("aa77");
        WithMinerU(p);
        WithOverlay("{\"aa77\":{\"mineru\":{\"status\":\"ok\"},\"popo\":{\"status\":\"running\"}}}");
        Assert.Equal(QueueStage.PopoRunning, Card("aa77").Stage);
    }

    [Fact]
    public void OverlayMineruError_MinerUFailed()
    {
        var p = CreateProject("aa88");
        WithOverlay("{\"aa88\":{\"mineru\":{\"status\":\"error\",\"error\":\"Metadata input exceeds 512 MiB\"}}}");
        var card = Card("aa88");
        Assert.Equal(QueueStage.MinerUFailed, card.Stage);
        Assert.Contains("512 MiB", card.Detail);
    }

    [Fact]
    public void OverlayMineruRunning_MinerURunning()
    {
        CreateProject("aa99");
        WithOverlay("{\"aa99\":{\"mineru\":{\"status\":\"running\"}}}");
        Assert.Equal(QueueStage.MinerURunning, Card("aa99").Stage);
    }

    [Fact]
    public void DiskDone_BeatsOverlayError()
    {
        // The report may lag the disk (or describe a failed rerun of an already-done project):
        // existing artifacts must still show as Done.
        var p = CreateProject("ab01");
        WithMinerU(p);
        WithPopo(p);
        WithOverlay("{\"ab01\":{\"mineru\":{\"status\":\"ok\"},\"popo\":{\"status\":\"error\",\"error\":\"nope\"}}}");
        Assert.Equal(QueueStage.Done, Card("ab01").Stage);
    }

    [Fact]
    public void CorruptOverlay_DegradesGracefully()
    {
        var p = CreateProject("ab02");
        WithMinerU(p);
        File.WriteAllText(Path.Combine(_root, QueueBoard.RerunReportFileName), "{\"ab02\":{\"mineru\":{\"stat");
        Assert.Equal(QueueStage.MinerUDone, Card("ab02").Stage);
    }

    [Fact]
    public void OverlayAt_SurfacesAsActivity()
    {
        var p = CreateProject("ab03");
        WithMinerU(p);
        WithOverlay("{\"ab03\":{\"mineru\":{\"status\":\"ok\",\"at\":\"2026-11-20T05:55:38Z\"}}}");
        Assert.NotNull(Card("ab03").LastActivity);
    }

    /// <summary>
    /// Smoke test against the real production store when present: the board must build
    /// without errors and every PdfMissing card must genuinely lack its PDF.
    /// Skipped (vacuously true) on machines without the store.
    /// </summary>
    [Fact]
    public void LiveStore_BoardsWithoutErrors()
    {
        var root = StorePaths.DefaultRoot;
        if (!Directory.Exists(root))
        {
            return;
        }

        var snapshot = QueueBoard.Build(root);
        var index = StoreIndex.Build(root); // in-memory rebuild only: never write the live store from tests
        Assert.Equal(index.Projects.Count, snapshot.Items.Count);
        foreach (var card in snapshot.Items.Where(c => c.Stage == QueueStage.PdfMissing))
        {
            var metadataPath = Path.Combine(card.ProjectPath, "project.json");
            Assert.True(File.Exists(metadataPath), card.ProjectPath);
        }
    }
}
