using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Export;
using Xunit;

namespace pcb_deftek.Tests;

public class DashboardReportChartSvgRendererTests
{
    [Fact]
    public void TimeSeries_RendersValidSelfContainedSvgWithTheSeriesColor()
    {
        var metric = DashboardReportMetric.CreateTimeSeries(
            "pcbs",
            "PCBs Inspected",
            "6",
            "Daily · 3 buckets",
            new[]
            {
                new DashboardReportTimeSeriesPoint("Mon", 1),
                new DashboardReportTimeSeriesPoint("Tue", 2),
                new DashboardReportTimeSeriesPoint("Wed", 3)
            },
            "#0066FF");

        string svg = DashboardReportChartSvgRenderer.Render(metric, 900, 220);
        XDocument document = XDocument.Parse(svg);

        document.Root!.Name.LocalName.Should().Be("svg");
        document.Root.Attribute("viewBox").Should().NotBeNull();
        svg.Should().Contain("#0066FF");
        Elements(document, "script").Should().BeEmpty();
        document.Descendants().Should().NotContain(element => element.Attribute("href") != null);
        svg.Should().NotContain("NaN");
    }

    [Fact]
    public void PassFail_UsesTheStatusColorsForBothSeries()
    {
        var metric = DashboardReportMetric.CreatePassFail(
            "rate",
            "Inspection Rate",
            "3 PASS / 1 FAIL",
            "Daily · 2 buckets",
            new[]
            {
                new DashboardReportPassFailPoint("Mon", 2, 1),
                new DashboardReportPassFailPoint("Tue", 1, 0)
            });

        string svg = DashboardReportChartSvgRenderer.Render(metric);

        XDocument.Parse(svg).Root!.Name.LocalName.Should().Be("svg");
        svg.Should().Contain("#107C41");
        svg.Should().Contain("#C42B1C");
        svg.Should().Contain("PASS");
        svg.Should().Contain("FAIL");
    }

    [Fact]
    public void Distribution_RendersNineBarsInClassOrderWithClassColors()
    {
        var counts = DefectKnowledgeBase.GetAll()
            .OrderBy(info => info.ClassId)
            .Select((info, index) => new DashboardClassCount(info.Class, info.Abbreviation, index))
            .ToArray();
        var metric = DashboardReportMetric.CreateDistribution(
            "distribution",
            "Defect Distribution",
            "36",
            "All nine defect classes",
            counts);

        string svg = DashboardReportChartSvgRenderer.Render(metric, 900, 260);
        XDocument document = XDocument.Parse(svg);
        var classColors = DefectKnowledgeBase.GetAll()
            .Select(info => info.ColorHex)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Elements(document, "rect")
            .Count(rect => rect.Attribute("fill")?.Value is { } fill && classColors.Contains(fill))
            .Should().Be(9);
        foreach (DefectClassInfo info in DefectKnowledgeBase.GetAll())
        {
            svg.Should().Contain(info.Abbreviation);
            svg.Should().Contain(info.ColorHex);
        }
        svg.Should().NotContain("NaN");
    }

    [Fact]
    public void ZeroDistribution_RendersVisibleBaselines()
    {
        var counts = DefectKnowledgeBase.GetAll()
            .OrderBy(info => info.ClassId)
            .Select(info => new DashboardClassCount(info.Class, info.Abbreviation, 0))
            .ToArray();
        var metric = DashboardReportMetric.CreateDistribution(
            "distribution",
            "Defect Distribution",
            "0",
            "All nine defect classes",
            counts);

        string svg = DashboardReportChartSvgRenderer.Render(metric, 900, 260);
        XDocument document = XDocument.Parse(svg);

        Elements(document, "rect")
            .Where(rect => rect.Attribute("height")?.Value is not null)
            .Should().HaveCount(9);
        svg.Should().NotContain("NaN");
    }

    [Fact]
    public void EmptyTimeSeries_RendersNoDataWithoutInvalidCoordinates()
    {
        var metric = DashboardReportMetric.CreateTimeSeries(
            "pcbs",
            "PCBs Inspected",
            "0",
            "Daily · 0 buckets",
            Array.Empty<DashboardReportTimeSeriesPoint>(),
            "#0066FF");

        string svg = DashboardReportChartSvgRenderer.Render(metric);

        XDocument document = XDocument.Parse(svg);
        document.Root!.Name.LocalName.Should().Be("svg");
        svg.Should().Contain("No data");
        svg.Should().NotContain("NaN");
        svg.Should().NotContain("Infinity");
    }

    [Fact]
    public void SinglePoint_RendersFiniteCoordinates()
    {
        var metric = DashboardReportMetric.CreateTimeSeries(
            "pcbs",
            "PCBs Inspected",
            "1",
            "Daily · 1 bucket",
            new[] { new DashboardReportTimeSeriesPoint("Mon", 1) },
            "#0066FF");

        string svg = DashboardReportChartSvgRenderer.Render(metric, 400, 180);

        XDocument.Parse(svg).Root!.Name.LocalName.Should().Be("svg");
        svg.Should().NotContain("NaN");
        svg.Should().NotContain("Infinity");
        svg.Should().Contain("Mon");
    }

    [Fact]
    public void Labels_AreThinnedAtNarrowWidths()
    {
        var points = Enumerable.Range(0, 100)
            .Select(index => new DashboardReportTimeSeriesPoint($"d{index}", index))
            .ToArray();
        var metric = DashboardReportMetric.CreateTimeSeries(
            "pcbs",
            "PCBs Inspected",
            "4950",
            "Hourly · 100 buckets",
            points,
            "#0066FF");

        DashboardReportChartSvgRenderer.CalculateLabelStep(100, 400).Should().BeGreaterThan(1);
        string svg = DashboardReportChartSvgRenderer.Render(metric, 400, 180);
        XDocument document = XDocument.Parse(svg);
        int axisLabels = Elements(document, "text")
            .Count(text => text.Attribute("text-anchor")?.Value == "middle" &&
                text.Value.StartsWith("d", StringComparison.Ordinal));

        axisLabels.Should().BeGreaterThan(0);
        axisLabels.Should().BeLessThan(100);
    }

    [Fact]
    public void Labels_AreXmlEscaped()
    {
        var metric = DashboardReportMetric.CreateTimeSeries(
            "pcbs",
            "PCBs Inspected",
            "1",
            "Daily · 1 bucket",
            new[] { new DashboardReportTimeSeriesPoint("A & <B>", 1) },
            "#0066FF");

        string svg = DashboardReportChartSvgRenderer.Render(metric, 400, 180);
        XDocument document = XDocument.Parse(svg);

        svg.Should().Contain("&amp;");
        svg.Should().Contain("&lt;");
        Elements(document, "text").Select(text => text.Value).Should().Contain("A & <B>");
    }

    private static IEnumerable<XElement> Elements(XDocument document, string localName) =>
        document.Descendants().Where(element => element.Name.LocalName == localName);

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(double.NaN)]
    public void Render_RejectsInvalidWidths(double width)
    {
        var metric = DashboardReportMetric.CreateTimeSeries(
            "pcbs",
            "PCBs Inspected",
            "1",
            "Daily · 1 bucket",
            new[] { new DashboardReportTimeSeriesPoint("Mon", 1) },
            "#0066FF");

        Action act = () => DashboardReportChartSvgRenderer.Render(metric, width, 180);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
