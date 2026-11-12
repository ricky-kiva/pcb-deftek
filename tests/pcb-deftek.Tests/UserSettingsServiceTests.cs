using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using pcb_deftek.Domain.Models;
using pcb_deftek.Domain.Models.Messages;
using pcb_deftek.Infrastructure.Persistence.DatabaseExport;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using Xunit;

namespace pcb_deftek.Tests;

public class UserSettingsServiceTests : IDisposable
{
    private readonly InMemorySettingsRepository _settingsRepository;
    private readonly StubDatabaseExportService _exportService;
    private readonly UserSettingsService _settingsService;
    private readonly string _defaultDownloadsPath;

    public UserSettingsServiceTests()
    {
        _defaultDownloadsPath = Path.Combine(Path.GetTempPath(), "test-downloads-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_defaultDownloadsPath);

        _settingsRepository = new InMemorySettingsRepository();
        _exportService = new StubDatabaseExportService(_defaultDownloadsPath);
        _settingsService = new UserSettingsService(_settingsRepository, _exportService);
    }

    public void Dispose()
    {
        WeakReferenceMessenger.Default.Reset();
        try
        {
            if (Directory.Exists(_defaultDownloadsPath))
            {
                Directory.Delete(_defaultDownloadsPath, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public async Task LoadSettingsAsync_WhenNoSettingsStored_ReturnsDefaultsWithConfiguredDownloadsDirectory()
    {
        // Act
        var settings = await _settingsService.LoadSettingsAsync();

        // Assert
        settings.Should().NotBeNull();
        settings.ConfidenceThreshold.Should().Be(0.25);
        settings.PreferredDevice.Should().Be("Cpu");
        settings.DefaultStartupPage.Should().Be("Dashboard");
        settings.SkipDashboardOnStartup.Should().BeFalse();
        settings.ReportExportPath.Should().Be(_defaultDownloadsPath);
        settings.DatabaseExportPath.Should().Be(_defaultDownloadsPath);
    }

    [Fact]
    public async Task SaveSettingsAsync_PersistsSettingsAndSubsequentLoadReflectsUpdates()
    {
        // Arrange
        var customReportPath = Path.Combine(_defaultDownloadsPath, "Reports");
        var customDatabasePath = Path.Combine(_defaultDownloadsPath, "Backups");

        var updated = new UserSettings
        {
            ConfidenceThreshold = 0.45,
            PreferredDevice = "GpuDirectML",
            DefaultStartupPage = "Live Detection",
            SkipDashboardOnStartup = true,
            ReportExportPath = customReportPath,
            DatabaseExportPath = customDatabasePath
        };

        // Act
        await _settingsService.SaveSettingsAsync(updated);
        var loaded = await _settingsService.LoadSettingsAsync();

        // Assert
        loaded.Should().NotBeNull();
        loaded.ConfidenceThreshold.Should().Be(0.45);
        loaded.PreferredDevice.Should().Be("GpuDirectML");
        loaded.DefaultStartupPage.Should().Be("Live Detection");
        loaded.SkipDashboardOnStartup.Should().BeTrue();
        loaded.ReportExportPath.Should().Be(customReportPath);
        loaded.DatabaseExportPath.Should().Be(customDatabasePath);
    }

    [Fact]
    public async Task SaveSettingsAsync_BroadcastsSettingsChangedMessageViaMessenger()
    {
        // Arrange
        UserSettings? receivedSettings = null;
        double? receivedThreshold = null;
        string? receivedDevice = null;

        WeakReferenceMessenger.Default.Register<UserSettingsChangedMessage>(this, (_, m) =>
        {
            receivedSettings = m.Value;
        });

        WeakReferenceMessenger.Default.Register<ConfidenceThresholdChangedMessage>(this, (_, m) =>
        {
            receivedThreshold = m.Value;
        });

        WeakReferenceMessenger.Default.Register<DevicePreferenceChangedMessage>(this, (_, m) =>
        {
            receivedDevice = m.Value;
        });

        var updated = new UserSettings
        {
            ConfidenceThreshold = 0.50,
            PreferredDevice = "GpuDirectML",
            DefaultStartupPage = "History",
            SkipDashboardOnStartup = false,
            ReportExportPath = _defaultDownloadsPath,
            DatabaseExportPath = _defaultDownloadsPath
        };

        // Act
        await _settingsService.SaveSettingsAsync(updated);

        // Assert
        receivedSettings.Should().NotBeNull();
        receivedSettings!.ConfidenceThreshold.Should().Be(0.50);
        receivedThreshold.Should().Be(0.50);
        receivedDevice.Should().Be("GpuDirectML");
    }

    private class InMemorySettingsRepository : ISettingsRepository
    {
        private readonly Dictionary<string, string> _store = new(StringComparer.OrdinalIgnoreCase);

        public Task<T?> GetSettingAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (_store.TryGetValue(key, out var val))
            {
                if (typeof(T) == typeof(bool) && bool.TryParse(val, out var b))
                {
                    return Task.FromResult<T?>((T)(object)b);
                }
                if (typeof(T) == typeof(double) && double.TryParse(val, out var d))
                {
                    return Task.FromResult<T?>((T)(object)d);
                }
                if (typeof(T) == typeof(string))
                {
                    return Task.FromResult<T?>((T)(object)val);
                }
            }

            return Task.FromResult<T?>(default);
        }

        public Task SetSettingAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            _store[key] = value?.ToString() ?? string.Empty;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string>> GetAllSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(_store);

        public Task<bool> DeleteSettingAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.Remove(key));
    }

    private class StubDatabaseExportService : IDatabaseExportService
    {
        public string DefaultExportDirectory { get; }

        public StubDatabaseExportService(string defaultDir)
        {
            DefaultExportDirectory = defaultDir;
        }

        public Task<string> ExportDatabaseAsync(string? destinationDirectory = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Path.Combine(destinationDirectory ?? DefaultExportDirectory, "stub.db"));
    }
}
