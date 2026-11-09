using System;
using System.Linq;
using FluentAssertions;
using pcb_deftek.Common.Exceptions;
using Xunit;

namespace pcb_deftek.Tests;

public class DomainExceptionTests
{
    [Fact]
    public void AllDomainExceptions_ShouldInheritFromPcbDeftekException()
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
    public void InferenceExceptions_ShouldRetainPropertiesAndMessage()
    {
        var inner = new InvalidOperationException("Inner failure");
        var modelEx = new ModelLoadingException("Failed to load model", "Assets/Models/best.onnx", inner);
        var execEx = new ModelExecutionException("Execution failed", "ForwardPass", inner);

        modelEx.Message.Should().Be("Failed to load model");
        modelEx.ModelPath.Should().Be("Assets/Models/best.onnx");
        modelEx.InnerException.Should().Be(inner);

        execEx.Message.Should().Be("Execution failed");
        execEx.ExecutionStage.Should().Be("ForwardPass");
        execEx.InnerException.Should().Be(inner);
    }

    [Fact]
    public void HardwareExceptions_ShouldRetainPropertiesAndMessage()
    {
        var devEx = new DeviceUnavailableException("GPU unavailable", "DirectML");
        var camEx = new CameraCaptureException("Frame capture failed", 0);

        devEx.ProviderName.Should().Be("DirectML");
        camEx.CameraIndex.Should().Be(0);
    }

    [Fact]
    public void InspectionExceptions_ShouldRetainPropertiesAndMessage()
    {
        var fileEx = new FileInspectionException("Invalid image", "sample.png", ".png");
        var imgEx = new ImageProcessingException("Crop failed", "Crop");

        fileEx.TargetPath.Should().Be("sample.png");
        fileEx.Format.Should().Be(".png");
        imgEx.Operation.Should().Be("Crop");
    }

    [Fact]
    public void PersistenceExceptions_ShouldRetainPropertiesAndMessage()
    {
        var migEx = new DatabaseMigrationException("Migration failed", 3, "app.db");
        var bkpEx = new DatabaseBackupException("Backup failed", "backup.db", "app.db");

        migEx.TargetVersion.Should().Be(3);
        migEx.DatabasePath.Should().Be("app.db");
        bkpEx.BackupPath.Should().Be("backup.db");
        bkpEx.DatabasePath.Should().Be("app.db");
    }

    [Fact]
    public void ExportValidationAndConfigurationExceptions_ShouldRetainProperties()
    {
        var expEx = new ExportException("Export failed", "audit.pdf");
        var valEx = new ValidationException("Threshold out of range", "ConfidenceThreshold", 1.5);
        var cfgEx = new ConfigurationException("Key not found", "DefaultStartupPage");

        expEx.DestinationPath.Should().Be("audit.pdf");
        valEx.PropertyName.Should().Be("ConfidenceThreshold");
        valEx.AttemptedValue.Should().Be(1.5);
        cfgEx.SettingKey.Should().Be("DefaultStartupPage");
    }
}
