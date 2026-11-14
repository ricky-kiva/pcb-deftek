using System;
using System.Collections.Generic;
using FluentAssertions;
using pcb_deftek.Common.Exceptions;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using Xunit;

namespace pcb_deftek.Tests;

public class DomainModelsAndContractsTests
{
    [Fact]
    public void BoundingBox_WithValidNormalizedCoordinates_InstantiatesCorrectlyAndComputesProperties()
    {
        // Arrange & Act
        var box = new BoundingBox(0.1f, 0.2f, 0.3f, 0.4f);

        // Assert
        box.X.Should().Be(0.1f);
        box.Y.Should().Be(0.2f);
        box.Width.Should().Be(0.3f);
        box.Height.Should().Be(0.4f);
        box.Right.Should().BeApproximately(0.4f, 0.0001f);
        box.Bottom.Should().BeApproximately(0.6f, 0.0001f);
        box.CenterX.Should().BeApproximately(0.25f, 0.0001f);
        box.CenterY.Should().BeApproximately(0.40f, 0.0001f);
        box.Area.Should().BeApproximately(0.12f, 0.0001f);
    }

    [Fact]
    public void BoundingBox_Deconstruct_ReturnsIndividualComponents()
    {
        // Arrange
        var box = new BoundingBox(0.15f, 0.25f, 0.35f, 0.45f);

        // Act
        var (x, y, w, h) = box;

        // Assert
        x.Should().Be(0.15f);
        y.Should().Be(0.25f);
        w.Should().Be(0.35f);
        h.Should().Be(0.45f);
    }

    [Theory]
    [InlineData(0.2f, 0.3f, true)]   // inside
    [InlineData(0.1f, 0.2f, true)]   // top-left edge
    [InlineData(0.4f, 0.6f, true)]   // bottom-right edge
    [InlineData(0.05f, 0.3f, false)] // outside left
    [InlineData(0.5f, 0.3f, false)]  // outside right
    [InlineData(0.2f, 0.1f, false)]  // outside top
    [InlineData(0.2f, 0.7f, false)]  // outside bottom
    public void BoundingBox_Contains_TestsNormalizedPointContainment(float px, float py, bool expectedContains)
    {
        // Arrange
        var box = new BoundingBox(0.1f, 0.2f, 0.3f, 0.4f);

        // Act
        bool contains = box.Contains(px, py);

        // Assert
        contains.Should().Be(expectedContains);
    }

    [Fact]
    public void BoundingBox_ToPixels_ScalesBackToRasterCoordinates()
    {
        // Arrange
        var box = new BoundingBox(0.1f, 0.2f, 0.3f, 0.4f);

        // Act
        box.ToPixels(1000, 2000, out int px, out int py, out int pw, out int ph);

        // Assert
        px.Should().Be(100);
        py.Should().Be(400);
        pw.Should().Be(300);
        ph.Should().Be(800);
    }

    [Fact]
    public void BoundingBox_ToPixels_WithNonPositiveDimensions_ThrowsValidationException()
    {
        // Arrange
        var box = new BoundingBox(0.1f, 0.2f, 0.3f, 0.4f);

        // Act & Assert
        Assert.Throws<ValidationException>(() =>
            box.ToPixels(0, 100, out _, out _, out _, out _));
    }

    [Theory]
    [InlineData(float.NaN, 0.1f, 0.2f, 0.2f)]
    [InlineData(0.1f, float.NaN, 0.2f, 0.2f)]
    [InlineData(float.PositiveInfinity, 0.1f, 0.2f, 0.2f)]
    [InlineData(-0.1f, 0.1f, 0.2f, 0.2f)]
    [InlineData(0.1f, -0.1f, 0.2f, 0.2f)]
    [InlineData(0.1f, 0.1f, 0.0f, 0.2f)]
    [InlineData(0.1f, 0.1f, 0.2f, -0.2f)]
    [InlineData(0.9f, 0.1f, 0.2f, 0.2f)] // x + w > 1.0
    [InlineData(0.1f, 0.9f, 0.2f, 0.2f)] // y + h > 1.0
    public void BoundingBox_WithInvalidBounds_ThrowsValidationException(float x, float y, float w, float h)
    {
        // Act & Assert
        Assert.Throws<ValidationException>(() => new BoundingBox(x, y, w, h));
    }

    [Fact]
    public void BoundingBox_FromPixels_ScalesAndNormalizesCoordinates()
    {
        // Arrange & Act
        var box = BoundingBox.FromPixels(100, 200, 300, 400, 1000, 1000);

        // Assert
        box.X.Should().BeApproximately(0.1f, 0.001f);
        box.Y.Should().BeApproximately(0.2f, 0.001f);
        box.Width.Should().BeApproximately(0.3f, 0.001f);
        box.Height.Should().BeApproximately(0.4f, 0.001f);
    }

    [Fact]
    public void BoundingBox_FromPixels_WithNonPositiveDimensions_ThrowsValidationException()
    {
        // Act & Assert
        Assert.Throws<ValidationException>(() => BoundingBox.FromPixels(10, 10, 50, 50, 0, 100));
        Assert.Throws<ValidationException>(() => BoundingBox.FromPixels(10, 10, 50, 50, 100, -5));
    }

    [Fact]
    public void BoundingBox_Clamp_ConstrainsOverflowCoordinatesSafely()
    {
        // Arrange & Act
        var box = BoundingBox.Clamp(-0.5f, 1.5f, 2.0f, 2.0f);

        // Assert
        box.X.Should().Be(0.0f);
        box.Y.Should().Be(1.0f);
        box.Width.Should().BeLessThanOrEqualTo(1.0f);
        box.Height.Should().BeLessThanOrEqualTo(1.0f);
        (box.X + box.Width).Should().BeLessThanOrEqualTo(1.0001f);
        (box.Y + box.Height).Should().BeLessThanOrEqualTo(1.0001f);
    }

    [Fact]
    public void DetectedDefect_WithValidData_DelegatesMetadataToDefectKnowledgeBase()
    {
        // Arrange
        var defectId = Guid.NewGuid();
        var inspectionId = Guid.NewGuid();
        var box = new BoundingBox(0.1f, 0.1f, 0.2f, 0.2f);

        // Act
        var defect = new DetectedDefect(defectId, inspectionId, "short", 0.95f, box);

        // Assert
        defect.Id.Should().Be(defectId);
        defect.InspectionId.Should().Be(inspectionId);
        defect.ClassCode.Should().Be("short");
        defect.Confidence.Should().Be(0.95f);
        defect.BoundingBox.Should().Be(box);
        defect.Class.Should().Be(DefectClass.Short);
        defect.ClassName.Should().Be("Short");
        defect.Severity.Should().Be(DefectSeverity.Critical);
        defect.Explanation.Should().Contain("unintended connection");
        defect.RootCause.Should().NotBeNullOrWhiteSpace();
        defect.Impact.Should().NotBeNullOrWhiteSpace();
        defect.Action.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void DetectedDefect_WithUnknownClassCode_ThrowsValidationException()
    {
        // Arrange
        var box = new BoundingBox(0.1f, 0.1f, 0.2f, 0.2f);

        // Act & Assert
        Assert.Throws<ValidationException>(() =>
            new DetectedDefect(Guid.NewGuid(), Guid.NewGuid(), "invalid_defect_code", 0.9f, box));
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.05f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void DetectedDefect_WithInvalidConfidence_ThrowsValidationException(float invalidConfidence)
    {
        // Arrange
        var box = new BoundingBox(0.1f, 0.1f, 0.2f, 0.2f);

        // Act & Assert
        Assert.Throws<ValidationException>(() =>
            new DetectedDefect(Guid.NewGuid(), Guid.NewGuid(), "short", invalidConfidence, box));
    }

    [Fact]
    public void DetectedDefect_WithEmptyGuids_ThrowsValidationException()
    {
        // Arrange
        var box = new BoundingBox(0.1f, 0.1f, 0.2f, 0.2f);

        // Act & Assert
        Assert.Throws<ValidationException>(() =>
            new DetectedDefect(Guid.Empty, Guid.NewGuid(), "short", 0.9f, box));

        Assert.Throws<ValidationException>(() =>
            new DetectedDefect(Guid.NewGuid(), Guid.Empty, "short", 0.9f, box));
    }

    [Fact]
    public void InspectionRecord_CreateFactory_GeneratesDefaultsAndCalculatesPassStatusWhenZeroDefects()
    {
        // Act
        var inspection = InspectionRecord.Create(
            name: "20261110-001",
            mode: DetectionMode.SingleFile,
            imagePath: "images/board1.png");

        // Assert
        inspection.Id.Should().NotBeEmpty();
        inspection.Name.Should().Be("20261110-001");
        inspection.DefectCount.Should().Be(0);
        inspection.Status.Should().Be(InspectionStatus.Pass);
        inspection.IsPass.Should().BeTrue();
        inspection.HasDefects.Should().BeFalse();
        inspection.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        inspection.UpdatedAtUtc.Should().Be(inspection.CreatedAtUtc);
    }

    [Fact]
    public void InspectionRecord_WithDefects_DerivesStatusAsFail()
    {
        // Arrange
        var inspectionId = Guid.NewGuid();
        var box = new BoundingBox(0.1f, 0.1f, 0.2f, 0.2f);
        var defect = new DetectedDefect(Guid.NewGuid(), inspectionId, "open", 0.88f, box);

        // Act
        var inspection = new InspectionRecord(
            id: inspectionId,
            name: "20261110-002",
            createdAtUtc: DateTime.UtcNow,
            updatedAtUtc: DateTime.UtcNow,
            mode: DetectionMode.LiveCamera,
            imagePath: "images/board2.png",
            defects: new[] { defect });

        // Assert
        inspection.DefectCount.Should().Be(1);
        inspection.Status.Should().Be(InspectionStatus.Fail);
        inspection.IsPass.Should().BeFalse();
        inspection.HasDefects.Should().BeTrue();
    }

    [Fact]
    public void InspectionRecord_WithUpdatedName_ReturnsNewInstanceWithUpdatedNameAndTimestamp()
    {
        // Arrange
        var original = InspectionRecord.Create(
            name: "OriginalName",
            mode: DetectionMode.BatchFolder,
            imagePath: "images/board3.png",
            origin: InspectionOrigin.Crop);

        // Act
        var updated = original.WithUpdatedName("RenamedBoard");

        // Assert
        updated.Id.Should().Be(original.Id);
        updated.Name.Should().Be("RenamedBoard");
        updated.CreatedAtUtc.Should().Be(original.CreatedAtUtc);
        updated.UpdatedAtUtc.Should().BeAfter(original.UpdatedAtUtc);
        updated.Mode.Should().Be(original.Mode);
        updated.ImagePath.Should().Be(original.ImagePath);
        updated.Origin.Should().Be(InspectionOrigin.Crop);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void InspectionRecord_WithInvalidNameOrPath_ThrowsValidationException(string? invalidString)
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() =>
            new InspectionRecord(Guid.NewGuid(), invalidString!, DateTime.UtcNow, DateTime.UtcNow, DetectionMode.SingleFile, "path.png"));

        Assert.ThrowsAny<ArgumentException>(() =>
            new InspectionRecord(Guid.NewGuid(), "ValidName", DateTime.UtcNow, DateTime.UtcNow, DetectionMode.SingleFile, invalidString!));
    }

    [Fact]
    public void InspectionQueryFilter_PageSize_BoundsBetweenOneAndOneHundred()
    {
        // Arrange & Act
        var filterLow = new InspectionQueryFilter { PageSize = -5 };
        var filterHigh = new InspectionQueryFilter { PageSize = 500 };
        var filterNormal = new InspectionQueryFilter { PageSize = 25 };

        // Assert
        filterLow.PageSize.Should().Be(1);
        filterHigh.PageSize.Should().Be(100);
        filterNormal.PageSize.Should().Be(25);
    }

    [Fact]
    public void PagedResult_ComputesPaginationPropertiesCorrectly()
    {
        // Arrange & Act
        var paged = new PagedResult<string>(
            Items: new[] { "item1", "item2" },
            PageNumber: 2,
            PageSize: 10,
            TotalItems: 25);

        // Assert
        paged.TotalPages.Should().Be(3);
        paged.HasNextPage.Should().BeTrue();
        paged.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public void PagedResult_EmptyFactory_CreatesEmptyContainerWithZeroTotalItems()
    {
        // Act
        var empty = PagedResult<int>.Empty(15);

        // Assert
        empty.Items.Should().BeEmpty();
        empty.PageNumber.Should().Be(1);
        empty.PageSize.Should().Be(15);
        empty.TotalItems.Should().Be(0);
        empty.TotalPages.Should().Be(0);
        empty.HasNextPage.Should().BeFalse();
        empty.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public void UpdateInspectionNameCommand_InitializesCorrectly()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var cmd = new UpdateInspectionNameCommand(id, "NewCustomName");

        // Assert
        cmd.InspectionId.Should().Be(id);
        cmd.NewName.Should().Be("NewCustomName");
    }
}
