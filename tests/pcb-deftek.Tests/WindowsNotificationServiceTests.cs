using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using pcb_deftek.Infrastructure.Notifications;
using Xunit;

namespace pcb_deftek.Tests;

public class WindowsNotificationServiceTests
{
    [Fact]
    public async Task ShowToastAsync_WithValidTitleAndMessage_DispatchesSuccessfully()
    {
        // Arrange
        string? capturedTitle = null;
        string? capturedMessage = null;

        var service = new WindowsNotificationService((title, message) =>
        {
            capturedTitle = title;
            capturedMessage = message;
        });

        // Act
        await service.ShowToastAsync("Export Complete", "Database exported to Downloads");

        // Assert
        capturedTitle.Should().Be("Export Complete");
        capturedMessage.Should().Be("Database exported to Downloads");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ShowToastAsync_WithNullOrWhitespaceTitle_ThrowsArgumentException(string? invalidTitle)
    {
        // Arrange
        var service = new WindowsNotificationService();

        // Act & Assert
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            service.ShowToastAsync(invalidTitle!, "Message"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ShowToastAsync_WithNullOrWhitespaceMessage_ThrowsArgumentException(string? invalidMessage)
    {
        // Arrange
        var service = new WindowsNotificationService();

        // Act & Assert
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            service.ShowToastAsync("Title", invalidMessage!));
    }

    [Fact]
    public async Task ShowToastAsync_WhenPlatformDispatcherFails_CatchesGracefullyWithoutThrowing()
    {
        // Arrange
        var service = new WindowsNotificationService((_, _) =>
        {
            throw new InvalidOperationException("Simulated COM or notification platform unavailability in headless CI");
        });

        // Act
        Func<Task> act = async () => await service.ShowToastAsync("Title", "Message");

        // Assert - should not throw
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ShowToastAsync_DefaultConstructor_DoesNotCrashInTestEnvironment()
    {
        // Arrange
        var service = new WindowsNotificationService();

        // Act
        Func<Task> act = async () => await service.ShowToastAsync("Test Title", "Test Message");

        // Assert
        await act.Should().NotThrowAsync();
    }
}
