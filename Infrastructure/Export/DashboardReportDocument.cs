using System;
using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace pcb_deftek.Infrastructure.Export;

/// <summary>
/// Composes the audit report PDF from the immutable report content. A4 portrait, four
/// single-column metric blocks, an evidence section, and page-numbered footer.
/// </summary>
public static class DashboardReportPdfDocument
{
    private const float EvidenceImageRelativeWidth = 0.4f;
    private const float ChartHeight = 140;

    /// <summary>
    /// Renders the report to a PDF byte array.
    /// </summary>
    /// <param name="report">Report content.</param>
    /// <returns>The generated PDF bytes.</returns>
    public static byte[] Render(DashboardReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        QuestPdfConfiguration.EnsureConfigured();
        return CreateDocument(report).GeneratePdf();
    }

    /// <summary>
    /// Renders the report into a writable stream. The stream is left open.
    /// </summary>
    /// <param name="report">Report content.</param>
    /// <param name="output">Destination stream.</param>
    public static void Render(DashboardReportDocument report, Stream output)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite)
        {
            throw new ArgumentException("The output stream must be writable.", nameof(output));
        }

        QuestPdfConfiguration.EnsureConfigured();
        CreateDocument(report).GeneratePdf(output);
    }

    /// <summary>
    /// Creates the QuestPDF document for layout tests. Kept internal so QuestPDF types do not
    /// leak into the public export surface.
    /// </summary>
    internal static IDocument CreateDocument(DashboardReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return Document.Create(container => Compose(container, report));
    }

    private static void Compose(IDocumentContainer container, DashboardReportDocument report)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(18, Unit.Millimetre);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(text => text.FontSize(9).FontColor(Colors.Grey.Darken3));

            page.Header().PaddingBottom(10).Column(header => ComposeHeader(header, report));
            page.Content().Column(column => ComposeContent(column, report));
            page.Footer().PaddingTop(4).Row(row => ComposeFooter(row, report));
        });
    }

    private static void ComposeHeader(ColumnDescriptor header, DashboardReportDocument report)
    {
        header.Item().Text(report.Title).FontSize(20).SemiBold().FontColor(Colors.Black);
        header.Item().Text(report.Subtitle).FontSize(11).FontColor(Colors.Grey.Darken1);
        header.Item().Text($"Generated {report.GeneratedAtLocal:yyyy-MM-dd HH:mm}")
            .FontSize(8)
            .FontColor(Colors.Grey.Medium);
        header.Item().PaddingTop(8).Background(Colors.Grey.Lighten4).Padding(8).Column(filter =>
        {
            filter.Item().Text($"Date range: {report.FilterSummary.DateRange}").FontSize(8);
            filter.Item().Text($"Defect classes: {report.FilterSummary.DefectClasses}").FontSize(8);
            filter.Item().Text($"Severities: {report.FilterSummary.Severities}").FontSize(8);
        });
    }

    private static void ComposeContent(ColumnDescriptor column, DashboardReportDocument report)
    {
        column.Spacing(10);

        foreach (DashboardReportMetric metric in report.Metrics)
        {
            ComposeMetric(column, metric);
        }

        if (report.Inspections.Count == 0)
        {
            return;
        }

        column.Item().PageBreak();
        column.Item().Text("Annotated Inspections").FontSize(14).SemiBold().FontColor(Colors.Black);

        foreach (DashboardReportInspection inspection in report.Inspections)
        {
            ComposeInspection(column, inspection);
        }
    }

    private static void ComposeMetric(ColumnDescriptor column, DashboardReportMetric metric)
    {
        column.Item()
            .Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(10)
            .Column(block =>
            {
                block.Item().Row(row =>
                {
                    row.RelativeItem().Text(metric.Title).FontSize(10).SemiBold();
                    row.RelativeItem().AlignRight().Text(metric.DisplayValue).FontSize(16).SemiBold();
                });
                block.Item().Text(metric.Caption).FontSize(8).FontColor(Colors.Grey.Medium);
                block.Item()
                    .PaddingTop(4)
                    .Height(90, Unit.Point)
                    .Svg(size =>
                    {
                        float width = size.Width;
                        double safeWidth = double.IsFinite(width) && width > 0 ? width : 600;
                        return DashboardReportChartSvgRenderer.Render(metric, safeWidth, ChartHeight);
                    });
            });
    }

    private static void ComposeInspection(ColumnDescriptor column, DashboardReportInspection inspection)
    {
        column.Item()
            .EnsureSpace(160)
            .PaddingBottom(12)
            .Column(block =>
            {
                block.Item().Text(inspection.Name).FontSize(11).SemiBold();
                block.Item().PaddingTop(1).Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(8).FontColor(Colors.Grey.Darken1));
                    text.Span($"{inspection.CreatedAtLocal:yyyy-MM-dd HH:mm} · {inspection.ModeLabel} · ");
                    text.Span(inspection.StatusLabel)
                        .SemiBold()
                        .FontColor(inspection.Status == Domain.Models.InspectionStatus.Pass
                            ? Colors.Green.Medium
                            : Colors.Red.Medium);
                    text.Span($" · {inspection.DefectCount} defect(s)");
                });

                block.Item().PaddingTop(6).Row(row =>
                {
                    row.RelativeItem(EvidenceImageRelativeWidth)
                        .Image(inspection.StagedImagePath)
                        .FitWidth();
                    row.ConstantItem(10);
                    row.RelativeItem(1 - EvidenceImageRelativeWidth)
                        .Column(details => ComposeEvidenceDetails(details, inspection));
                });
            });
    }

    private static void ComposeEvidenceDetails(
        ColumnDescriptor details,
        DashboardReportInspection inspection)
    {
        if (inspection.Defects.Count == 0)
        {
            details.Item().Text("No defects detected.")
                .FontSize(8)
                .FontColor(Colors.Grey.Medium);
            return;
        }

        details.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2.4f);
                columns.RelativeColumn(1.5f);
                columns.RelativeColumn(1.5f);
            });

            table.Header(header =>
            {
                header.Cell().Background(Colors.Grey.Lighten3).Padding(3)
                    .Text("Class").SemiBold().FontSize(8);
                header.Cell().Background(Colors.Grey.Lighten3).Padding(3)
                    .Text("Confidence").SemiBold().FontSize(8);
                header.Cell().Background(Colors.Grey.Lighten3).Padding(3)
                    .Text("Severity").SemiBold().FontSize(8);
            });

            foreach (DashboardReportDefect defect in inspection.Defects)
            {
                table.Cell().Padding(3).Text(defect.ClassName).FontSize(8);
                table.Cell().Padding(3).Text($"{defect.ConfidencePercent}%").FontSize(8);
                table.Cell().Padding(3).Text(defect.SeverityLabel).FontSize(8);
            }
        });
    }

    private static void ComposeFooter(RowDescriptor row, DashboardReportDocument report)
    {
        row.RelativeItem()
            .Text($"PCB Deftek · Generated {report.GeneratedAtLocal:yyyy-MM-dd HH:mm}")
            .FontSize(7)
            .FontColor(Colors.Grey.Medium);
        row.RelativeItem().AlignRight().Text(text =>
        {
            text.DefaultTextStyle(style => style.FontSize(7).FontColor(Colors.Grey.Medium));
            text.Span("Page ");
            text.CurrentPageNumber();
            text.Span(" of ");
            text.TotalPages();
        });
    }
}
