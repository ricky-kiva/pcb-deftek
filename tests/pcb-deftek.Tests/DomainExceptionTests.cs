using System;
using System.Linq;
using FluentAssertions;
using pcb_deftek.Common.Exceptions;
using Xunit;

namespace pcb_deftek.Tests;

public class DomainExceptionTests
{
    [Fact]
    public void AllDomainExceptions_ShouldDeriveFromPcbDeftekException()
    {
        // Arrange
        var assembly = typeof(PcbDeftekException).Assembly;
        var exceptionTypes = assembly.GetTypes()
            .Where(t => t.Namespace == "pcb_deftek.Common.Exceptions" && typeof(Exception).IsAssignableFrom(t))
            .ToList();

        // Assert
        exceptionTypes.Should().NotBeEmpty();
        foreach (var type in exceptionTypes)
        {
            typeof(PcbDeftekException).IsAssignableFrom(type).Should().BeTrue(
                $"{type.Name} must inherit directly or indirectly from PcbDeftekException");
        }
    }

    [Fact]
    public void PcbDeftekException_WhenCreatedWithInnerException_PreservesInnerException()
    {
        // Arrange
        var inner = new FormatException("Corrupted format");

        // Act
        var ex = new InferenceException("Failed inference", inner);

        // Assert
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void ModelLoadingException_WhenInstantiated_PreservesMessageAndModelPath()
    {
        // Arrange & Act
        var ex = new ModelLoadingException("ONNX model file not found", "Assets/Models/best.onnx");

        // Assert
        ex.Message.Should().Be("ONNX model file not found");
        ex.ModelPath.Should().Be("Assets/Models/best.onnx");
    }

    [Fact]
    public void ModelExecutionException_WhenInstantiated_PreservesMessageAndExecutionStage()
    {
        // Arrange & Act
        var ex = new ModelExecutionException("Tensor shape mismatch", "PreProcessing");

        // Assert
        ex.Message.Should().Be("Tensor shape mismatch");
        ex.ExecutionStage.Should().Be("PreProcessing");
    }

    [Fact]
    public void DeviceUnavailableException_WhenInstantiated_PreservesMessageAndProviderName()
    {
        // Arrange & Act
        var ex = new DeviceUnavailableException("DirectML execution provider unavailable", "DirectML");

        // Assert
        ex.Message.Should().Be("DirectML execution provider unavailable");
        ex.ProviderName.Should().Be("DirectML");
    }

    [Fact]
    public void CameraCaptureException_WhenInstantiated_PreservesMessageAndCameraIndex()
    {
        // Arrange & Act
        var ex = new CameraCaptureException("Webcam frame acquisition timeout", 0);

        // Assert
        ex.Message.Should().Be("Webcam frame acquisition timeout");
        ex.CameraIndex.Should().Be(0);
    }

    [Fact]
    public void FileInspectionException_WhenInstantiated_PreservesMessageAndTargetPathAndFormat()
    {
        // Arrange & Act
        var ex = new FileInspectionException("Unrecognized image encoding", "board-sample.png", ".png");

        // Assert
        ex.Message.Should().Be("Unrecognized image encoding");
        ex.TargetPath.Should().Be("board-sample.png");
        ex.Format.Should().Be(".png");
    }

    [Fact]
    public void ImageProcessingException_WhenInstantiated_PreservesMessageAndOperation()
    {
        // Arrange & Act
        var ex = new ImageProcessingException("Failed to crop defect bounding box", "Crop");

        // Assert
        ex.Message.Should().Be("Failed to crop defect bounding box");
        ex.Operation.Should().Be("Crop");
    }

    [Fact]
    public void PersistenceException_WhenInstantiated_PreservesMessageAndDatabasePath()
    {
        // Arrange & Act
        var ex = new PersistenceException("SQLite database connection failed", "app.db");

        // Assert
        ex.Message.Should().Be("SQLite database connection failed");
        ex.DatabasePath.Should().Be("app.db");
    }

    [Fact]
    public void DatabaseMigrationException_WhenInstantiated_PreservesMessageAndTargetVersion()
    {
        // Arrange & Act
        var ex = new DatabaseMigrationException("Migration step 2 failed", 2, "app.db");

        // Assert
        ex.Message.Should().Be("Migration step 2 failed");
        ex.TargetVersion.Should().Be(2);
        ex.DatabasePath.Should().Be("app.db");
    }

    [Fact]
    public void DatabaseBackupException_WhenInstantiated_PreservesMessageAndBackupPath()
    {
        // Arrange & Act
        var ex = new DatabaseBackupException("Failed to copy database to backup destination", "backup-20261109.db", "app.db");

        // Assert
        ex.Message.Should().Be("Failed to copy database to backup destination");
        ex.BackupPath.Should().Be("backup-20261109.db");
        ex.DatabasePath.Should().Be("app.db");
    }

    [Fact]
    public void ExportException_WhenInstantiated_PreservesMessageAndDestinationPath()
    {
        // Arrange & Act
        var ex = new ExportException("Failed to render PDF export document", "report.pdf");

        // Assert
        ex.Message.Should().Be("Failed to render PDF export document");
        ex.DestinationPath.Should().Be("report.pdf");
    }

    [Fact]
    public void ValidationException_WhenInstantiated_PreservesPropertyNameAndAttemptedValue()
    {
        // Arrange & Act
        var ex = new ValidationException("Confidence threshold must be between 0.0 and 1.0", "ConfidenceThreshold", 1.85);

        // Assert
        ex.Message.Should().Be("Confidence threshold must be between 0.0 and 1.0");
        ex.PropertyName.Should().Be("ConfidenceThreshold");
        ex.AttemptedValue.Should().Be(1.85);
    }

    [Fact]
    public void ConfigurationException_WhenInstantiated_PreservesMessageAndSettingKey()
    {
        // Arrange & Act
        var ex = new ConfigurationException("Setting key not found in storage", "DefaultStartupPage");

        // Assert
        ex.Message.Should().Be("Setting key not found in storage");
        ex.SettingKey.Should().Be("DefaultStartupPage");
    }
}
