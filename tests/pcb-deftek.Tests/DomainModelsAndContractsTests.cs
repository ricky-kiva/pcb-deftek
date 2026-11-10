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
    public void BoundingBox_WithValidNormalizedCoordinates_InstantiatesCorrectly()
    {
        // Arrange & Act
        var box = new BoundingBox(0.1f, 0.2f, 0.3f, 0.4f);

        // Assert
        box.X.Should().Be(0.1f);
        box.Y.Should().Be(0.2f);
        box.Width.Should().Be(0.3f);
        box.Height.Should().Be(0.4f);
    }

    [Theory]
    [InlineData(float.NaN, 0.1f, 0.2f, 0.2f)]
    [InlineData(0.1f, float.NaN, 0.2f, 0.2f)]
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
        defect.ClassName.Should().Be("Short");
        defect.Severity.Should().Be(DefectSeverity.Critical);
        defect.Explanation.Should().Contain("unintended connection");
        defect.RootCause.Should().NotBeNullOrWhiteSpace();
        defect.Impact.Should().NotBeNullOrWhiteSpace();
        defect.Action.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.05f)]
    [InlineData(float.NaN)]
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
    public void InspectionRecord_WithoutDefects_DerivesStatusAsPass()
    {
        // Arrange & Act
        var inspection = new InspectionRecord(
            id: Guid.NewGuid(),
            name: "20261110-001",
            createdAtUtc: DateTime.UtcNow,
            updatedAtUtc: DateTime.UtcNow,
            mode: DetectionMode.SingleFile,
            imagePath: "images/board1.png",
            defects: Array.Empty<DetectedDefect>());

        // Assert
        inspection.DefectCount.Should().Be(0);
        inspection.Status.Should().Be(InspectionStatus.Pass);
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
    }

    [Fact]
    public void InspectionRecord_WithUpdatedName_ReturnsNewInstanceWithUpdatedNameAndTimestamp()
    {
        // Arrange
        var original = new InspectionRecord(
            id: Guid.NewGuid(),
            name: "OriginalName",
            createdAtUtc: DateTime.UtcNow.AddMinutes(-5),
            updatedAtUtc: DateTime.UtcNow.AddMinutes(-5),
            mode: DetectionMode.BatchFolder,
            imagePath: "images/board3.png");

        // Act
        var updated = original.WithUpdatedName("RenamedBoard");

        // Assert
        updated.Id.Should().Be(original.Id);
        updated.Name.Should().Be("RenamedBoard");
        updated.CreatedAtUtc.Should().Be(original.CreatedAtUtc);
        updated.UpdatedAtUtc.Should().BeAfter(original.UpdatedAtUtc);
        updated.Mode.Should().Be(original.Mode);
        updated.ImagePath.Should().Be(original.ImagePath);
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
}
