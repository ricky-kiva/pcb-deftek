using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Export;
using Xunit;

namespace pcb_deftek.Tests;

public class DashboardReportPdfSmokeTests : IDisposable
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Zl9sAAAAASUVORK5CYII=");

    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        $"pcb-deftek-report-smoke-{Guid.NewGuid():N}");

    public DashboardReportPdfSmokeTests()
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
    public void Render_WritesAValidMultiPagePdfAndCleansUp()
    {
        string imagePath = Path.Combine(_tempRoot, "board.png");
        File.WriteAllBytes(imagePath, OnePixelPng);
        DashboardReportDocument report = CreateMixedReport(imagePath, 12);
        string pdfPath = Path.Combine(_tempRoot, "report.pdf");

        using (var stream = File.Create(pdfPath))
        {
            DashboardReportPdfDocument.Render(report, stream);
        }

        var file = new FileInfo(pdfPath);
        file.Exists.Should().BeTrue();
        file.Length.Should().BeGreaterThan(2_000);
        byte[] header = File.ReadAllBytes(pdfPath).Take(5).ToArray();
        Encoding.ASCII.GetString(header).Should().Be("%PDF-");

        File.Delete(pdfPath);
        Directory.GetFiles(_tempRoot, "*.pdf").Should().BeEmpty();
    }

    private static DashboardReportDocument CreateMixedReport(string imagePath, int inspectionCount)
    {
        DateTime createdUtc = new DateTime(2026, 3, 2, 10, 0, 0, DateTimeKind.Local).ToUniversalTime();
        var inspections = new List<InspectionRecord>();
        var staged = new Dictionary<Guid, string>();
        for (int i = 0; i < inspectionCount; i++)
        {
            Guid id = Guid.NewGuid();
            var defects = i % 2 == 0
                ? new[]
                {
                    new DetectedDefect(
                        Guid.NewGuid(),
                        id,
                        DefectKnowledgeBase.GetInfo(DefectClass.Open).Code,
                        0.92f,
                        new BoundingBox(0.1f, 0.1f, 0.2f, 0.2f))
                }
                : Array.Empty<DetectedDefect>();
            inspections.Add(new InspectionRecord(
                id,
                $"Board {i + 1}",
                createdUtc.AddMinutes(-i),
                createdUtc.AddMinutes(-i),
                i % 2 == 0 ? DetectionMode.LiveCamera : DetectionMode.BatchFolder,
                $"images/board-{i}.png",
                defects,
                i % 2 == 0 ? InspectionOrigin.LiveCapture : InspectionOrigin.External));
            staged[id] = imagePath;
        }

        var classes = DefectKnowledgeBase.GetAll()
            .OrderBy(info => info.ClassId)
            .Select((info, index) => new DashboardClassCount(info.Class, info.Abbreviation, index))
            .ToArray();
        var buckets = new[]
        {
            new DashboardBucket(
                new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Local),
                new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Local),
                "1 Mar",
                inspectionCount,
                inspectionCount,
                inspectionCount / 2,
                inspectionCount / 2),
            new DashboardBucket(
                new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Local),
                new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Local),
                "2 Mar",
                0,
                0,
                0,
                0)
        };
        var statistics = new DashboardStatistics(DashboardBucketGranularity.Day, buckets, classes);

        return DashboardReportContentFactory.Create(
            statistics,
            inspections,
            staged,
            DashboardFilter.AllTime,
            new DateTime(2026, 3, 3, 9, 30, 0, DateTimeKind.Local),
            CultureInfo.GetCultureInfo("en-US"));
    }
}
