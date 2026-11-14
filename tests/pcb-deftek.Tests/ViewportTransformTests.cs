using System;
using FluentAssertions;
using pcb_deftek.Domain.Models;
using pcb_deftek.Presentation.Models;
using Xunit;

namespace pcb_deftek.Tests;

public class ViewportTransformTests
{
    [Fact]
    public void Constructor_FitsAndCentersLetterboxedImage()
    {
        var sut = new ViewportTransform(800, 600, 1000, 1000);

        sut.FitScale.Should().BeApproximately(1.25, 0.0001);
        sut.Scale.Should().BeApproximately(1.25, 0.0001);
        sut.OffsetX.Should().BeApproximately(0, 0.0001);
        sut.OffsetY.Should().BeApproximately(125, 0.0001);
        sut.ZoomFactor.Should().BeApproximately(1.0, 0.0001);
    }

    [Fact]
    public void ZoomAt_KeepsImagePointUnderAnchorStationary()
    {
        var sut = new ViewportTransform(800, 600, 1000, 800);
        const double anchorX = 300;
        const double anchorY = 200;
        double imageXBefore = (anchorX - sut.OffsetX) / sut.Scale;
        double imageYBefore = (anchorY - sut.OffsetY) / sut.Scale;

        sut.ZoomAt(anchorX, anchorY, 2.0);

        double imageXAfter = (anchorX - sut.OffsetX) / sut.Scale;
        double imageYAfter = (anchorY - sut.OffsetY) / sut.Scale;
        imageXAfter.Should().BeApproximately(imageXBefore, 0.0001);
        imageYAfter.Should().BeApproximately(imageYBefore, 0.0001);
    }

    [Fact]
    public void ZoomAt_ClampsToFiveTenthsAndTenTimesFit()
    {
        var sut = new ViewportTransform(800, 600, 1000, 800);

        sut.ZoomAt(500, 400, 1000);
        sut.ZoomFactor.Should().BeApproximately(ViewportTransform.MaxZoom, 0.0001);

        sut.ZoomAt(500, 400, 0.000001);
        sut.ZoomFactor.Should().BeApproximately(ViewportTransform.MinZoom, 0.0001);
    }

    [Fact]
    public void Pan_AtFit_DoesNotMoveImage()
    {
        var sut = new ViewportTransform(800, 600, 1000, 1000);

        sut.Pan(300, -200);

        sut.OffsetX.Should().BeApproximately(0, 0.0001);
        sut.OffsetY.Should().BeApproximately(125, 0.0001);
    }

    [Fact]
    public void Pan_ClampsToImageEdgesWithoutExposingEmptySpace()
    {
        var sut = new ViewportTransform(800, 600, 1000, 800);
        sut.ZoomAt(500, 400, 2.0);

        sut.Pan(10000, 10000);
        sut.OffsetX.Should().BeApproximately(0, 0.0001);
        sut.OffsetY.Should().BeApproximately(0, 0.0001);

        sut.Pan(-10000, -10000);
        sut.OffsetX.Should().BeApproximately(1000 - 800 * sut.Scale, 0.0001);
        sut.OffsetY.Should().BeApproximately(800 - 600 * sut.Scale, 0.0001);
    }

    [Fact]
    public void FocusOn_CentersDefectAndZoomsWithinBounds()
    {
        var sut = new ViewportTransform(1000, 500, 800, 600);
        var defect = new NormalizedRect(0.4, 0.4, 0.2, 0.2);

        sut.FocusOn(defect, padding: 0.1);

        double defectCenterX = (defect.X + (defect.Width / 2)) * sut.ImageWidth;
        double defectCenterY = (defect.Y + (defect.Height / 2)) * sut.ImageHeight;
        (defectCenterX * sut.Scale + sut.OffsetX).Should().BeApproximately(400, 0.0001);
        (defectCenterY * sut.Scale + sut.OffsetY).Should().BeApproximately(300, 0.0001);
        sut.ZoomFactor.Should().BeInRange(ViewportTransform.MinZoom, ViewportTransform.MaxZoom);
        sut.ZoomFactor.Should().BeGreaterThan(1);
    }

    [Fact]
    public void FocusOn_TinyDefectStopsAtMaximumZoom()
    {
        var sut = new ViewportTransform(1000, 1000, 800, 600);

        sut.FocusOn(new NormalizedRect(0.49, 0.49, 0.01, 0.01), padding: 0.1);

        sut.ZoomFactor.Should().BeApproximately(ViewportTransform.MaxZoom, 0.0001);
    }

    [Fact]
    public void GetVisibleNormalizedRect_AtFit_ReturnsFullImage()
    {
        var sut = new ViewportTransform(800, 600, 1000, 1000);

        var visible = sut.GetVisibleNormalizedRect();

        visible.X.Should().BeApproximately(0, 0.0001);
        visible.Y.Should().BeApproximately(0, 0.0001);
        visible.Width.Should().BeApproximately(1, 0.0001);
        visible.Height.Should().BeApproximately(1, 0.0001);
    }

    [Fact]
    public void GetVisibleNormalizedRect_WhenZoomed_ReturnsClampedCenteredRegion()
    {
        var sut = new ViewportTransform(1000, 1000, 1000, 1000);
        sut.ZoomAt(500, 500, 2.0);

        var visible = sut.GetVisibleNormalizedRect();

        visible.X.Should().BeApproximately(0.25, 0.0001);
        visible.Y.Should().BeApproximately(0.25, 0.0001);
        visible.Width.Should().BeApproximately(0.5, 0.0001);
        visible.Height.Should().BeApproximately(0.5, 0.0001);
    }

    [Fact]
    public void Reset_RestoresExactFit()
    {
        var sut = new ViewportTransform(800, 600, 1000, 800);
        sut.ZoomAt(250, 250, 3);
        sut.Pan(-120, 80);

        sut.Reset();

        sut.Scale.Should().BeApproximately(sut.FitScale, 0.0001);
        sut.OffsetX.Should().BeApproximately(0, 0.0001);
        sut.OffsetY.Should().BeApproximately(25, 0.0001);
    }

    [Fact]
    public void Resize_PreservesZoomAndImageCenter()
    {
        var sut = new ViewportTransform(1000, 1000, 1000, 1000);
        sut.ZoomAt(500, 500, 2.0);

        sut.Resize(1200, 800);

        sut.ZoomFactor.Should().BeApproximately(2.0, 0.0001);
        var visible = sut.GetVisibleNormalizedRect();
        (visible.X + (visible.Width / 2)).Should().BeApproximately(0.5, 0.0001);
        (visible.Y + (visible.Height / 2)).Should().BeApproximately(0.5, 0.0001);
        visible.Width.Should().BeApproximately(0.75, 0.0001);
        visible.Height.Should().BeApproximately(0.5, 0.0001);
    }

    [Fact]
    public void Resize_AtFit_RemainsCentered()
    {
        var sut = new ViewportTransform(800, 600, 1000, 800);

        sut.Resize(900, 900);

        sut.ZoomFactor.Should().BeApproximately(1.0, 0.0001);
        sut.OffsetX.Should().BeApproximately((900 - 800 * sut.Scale) / 2, 0.0001);
        sut.OffsetY.Should().BeApproximately((900 - 600 * sut.Scale) / 2, 0.0001);
    }

    [Fact]
    public void SetView_ClampsScaleAndOffsets()
    {
        var sut = new ViewportTransform(1000, 1000, 1000, 1000);

        sut.SetView(sut.FitScale * 100, -20000, -20000);

        sut.ZoomFactor.Should().BeApproximately(ViewportTransform.MaxZoom, 0.0001);
        sut.OffsetX.Should().BeApproximately(1000 - 1000 * sut.Scale, 0.0001);
        sut.OffsetY.Should().BeApproximately(1000 - 1000 * sut.Scale, 0.0001);
    }

    [Theory]
    [InlineData(0, 600, 1000, 800)]
    [InlineData(800, 0, 1000, 800)]
    [InlineData(800, 600, 0, 800)]
    [InlineData(800, 600, 1000, 0)]
    public void Constructor_WithNonPositiveDimensions_Throws(double imageWidth, double imageHeight, double viewportWidth, double viewportHeight)
    {
        Action act = () => _ = new ViewportTransform(imageWidth, imageHeight, viewportWidth, viewportHeight);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
