using System;
using System.IO;
using FluentAssertions;
using pcb_deftek.Infrastructure.Logging;
using Xunit;

namespace pcb_deftek.Tests;

public class LoggingBootstrapperTests
{
    [Fact]
    public void GetDefaultLogDirectory_ShouldReturnPathUnderLocalAppData()
    {
        // Act
        string logDir = LoggingBootstrapper.GetDefaultLogDirectory();

        // Assert
        logDir.Should().NotBeNullOrWhiteSpace();
        logDir.Should().Contain("PcbDeftek");
        logDir.Should().Contain("logs");
    }

    [Fact]
    public void GetDefaultLogFilePath_ShouldEndWithPattern()
    {
        // Act
        string logFilePath = LoggingBootstrapper.GetDefaultLogFilePath();

        // Assert
        logFilePath.Should().EndWith("app-.log");
    }

    [Fact]
    public void Initialize_WithCustomDirectory_ShouldCreateDirectory()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), "pcb-deftek-test-logs-" + Guid.NewGuid().ToString("N"));

        try
        {
            // Act
            LoggingBootstrapper.Initialize(tempDir);

            // Assert
            Directory.Exists(tempDir).Should().BeTrue();
        }
        finally
        {
            LoggingBootstrapper.Shutdown();
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore cleanup errors */ }
            }
        }
    }
}
