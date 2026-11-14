using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Export;
using QuestPDF.Fluent;
using Xunit;

namespace pcb_deftek.Tests;

public class DashboardReportDocumentTests : IDisposable
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Zl9sAAAAASUVORK5CYII=");

    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        $"pcb-deftek-report-document-{Guid.NewGuid():N}");

    public DashboardReportDocumentTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public void CreateDocument_ComposesHeaderMetricsEvidenceAndFooter()
    {
        string imagePath = CreateImage("board.png");
        DashboardReportDocument report = CreateReport(inspectionCount: 1, imagePath);

        ICollection<string> pages = DashboardReportPdfDocument.CreateDocument(report).GenerateSvg();
        string svg = string.Join("\n", pages);

        pages.Count.Should().BeLessThanOrEqualTo(3);
        svg.Should().Contain("Defect Inspection Report");
        svg.Should().Contain("PCB Deftek");
        svg.Should().Contain("Date range: All time");
        svg.Should().Contain("PCBs Inspected");
        svg.Should().Contain("Defects Detected");
        svg.Should().Contain("Inspection Rate");
        svg.Should().Contain("Defect Distribution");
        svg.Should().Contain("Annotated Inspections");
        svg.Should().Contain("Board A");
        svg.Should().Contain("Class");
        svg.Should().Contain("Confidence");
        svg.Should().Contain("Severity");
        svg.Should().Contain("Page 1");
        svg.Should().Contain("Generated 2026-03-03 09:30");
    }

    [Fact]
    public void CreateDocument_RendersEmptyChartsWithoutData()
    {
        DashboardReportDocument report = DashboardReportContentFactory.Create(
            DashboardStatistics.Empty,
            Array.Empty<InspectionRecord>(),
            new Dictionary<Guid, string>(),
            DashboardFilter.AllTime,
            new DateTime(2026, 3, 3, 9, 30, 0, DateTimeKind.Local),
            CultureInfo.GetCultureInfo("en-US"));

        ICollection<string> pages = DashboardReportPdfDocument.CreateDocument(report).GenerateSvg();
        string svg = string.Join("\n", pages);

        pages.Count.Should().BeLessThanOrEqualTo(2);
        svg.Should().Contain("PCBs Inspected");
        svg.Should().NotContain("NaN");
    }

    [Fact]
    public void Render_ProducesAValidPdf()
    {
        string imagePath = CreateImage("board.png");
        DashboardReportDocument report = CreateReport(inspectionCount: 1, imagePath);

        byte[] pdf = DashboardReportPdfDocument.Render(report);

        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        pdf.Length.Should().BeGreaterThan(1_000);
    }

    [Fact]
    public void Render_ToStreamLeavesTheStreamOpen()
    {
        DashboardReportDocument report = CreateReport(inspectionCount: 0, null);
        using var stream = new MemoryStream();

        DashboardReportPdfDocument.Render(report, stream);

        stream.Length.Should().BeGreaterThan(1_000);
        stream.CanWrite.Should().BeTrue();
    }

    [Fact]
    public void Render_WithManyEvidenceBlocks_GeneratesWithoutLosingContent()
    {
        string imagePath = CreateImage("board.png");
        DashboardReportDocument single = CreateReport(inspectionCount: 1, imagePath);
        DashboardReportDocument many = CreateReport(inspectionCount: 12, imagePath);

        byte[] singlePdf = DashboardReportPdfDocument.Render(single);
        byte[] manyPdf = DashboardReportPdfDocument.Render(many);

        manyPdf.Length.Should().BeGreaterThan(singlePdf.Length);
        Encoding.ASCII.GetString(manyPdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Render_RejectsNullDocument()
    {
        Action act = () => DashboardReportPdfDocument.Render(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private DashboardReportDocument CreateReport(int inspectionCount, string? imagePath)
    {
        DateTime createdUtc = new DateTime(2026, 3, 2, 10, 0, 0, DateTimeKind.Local).ToUniversalTime();
        var inspections = new List<InspectionRecord>();
        var staged = new Dictionary<Guid, string>();
        for (int i = 0; i < inspectionCount; i++)
        {
            Guid id = Guid.NewGuid();
            var defects = i == 0
                ? new[]
                {
                    new DetectedDefect(
                        Guid.NewGuid(),
                        id,
                        DefectKnowledgeBase.GetInfo(DefectClass.Open).Code,
                        0.92f,
                        new BoundingBox(0.1f, 0.1f, 0.2f, 0.2f)),
                    new DetectedDefect(
                        Guid.NewGuid(),
                        id,
                        DefectKnowledgeBase.GetInfo(DefectClass.Short).Code,
                        0.75f,
                        new BoundingBox(0.4f, 0.4f, 0.2f, 0.2f))
                }
                : Array.Empty<DetectedDefect>();
            inspections.Add(new InspectionRecord(
                id,
                i == 0 ? "Board A" : $"Board {i + 1}",
                createdUtc.AddMinutes(-i),
                createdUtc.AddMinutes(-i),
                i % 2 == 0 ? DetectionMode.LiveCamera : DetectionMode.SingleFile,
                $"images/board-{i}.png",
                defects));
            staged[id] = imagePath ?? CreateImage($"board-{i}.png");
        }

        return DashboardReportContentFactory.Create(
            SampleStatistics(inspectionCount),
            inspections,
            staged,
            DashboardFilter.AllTime,
            new DateTime(2026, 3, 3, 9, 30, 0, DateTimeKind.Local),
            CultureInfo.GetCultureInfo("en-US"));
    }

    private static DashboardStatistics SampleStatistics(int inspectionCount)
    {
        if (inspectionCount == 0)
        {
            return DashboardStatistics.Empty;
        }

        var buckets = new[]
        {
            new DashboardBucket(
                new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Local),
                new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Local),
                "1 Mar",
                inspectionCount,
                inspectionCount,
                inspectionCount,
                0),
            new DashboardBucket(
                new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Local),
                new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Local),
                "2 Mar",
                1,
                1,
                0,
                1)
        };
        var classes = DefectKnowledgeBase.GetAll()
            .OrderBy(info => info.ClassId)
            .Select(info => new DashboardClassCount(info.Class, info.Abbreviation, info.ClassId == 0 ? 1 : 0))
            .ToArray();
        return new DashboardStatistics(DashboardBucketGranularity.Day, buckets, classes);
    }

    private string CreateImage(string fileName)
    {
        string path = Path.Combine(_tempRoot, fileName);
        File.WriteAllBytes(path, OnePixelPng);
        return path;
    }
}
