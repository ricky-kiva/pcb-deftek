using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Inference;
using pcb_deftek.Infrastructure.Notifications;
using pcb_deftek.Infrastructure.Persistence.DatabaseExport;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Presentation.ViewModels;
using Xunit;

namespace pcb_deftek.Tests;

public class SettingsViewModelTests
{
    private readonly StubUserSettingsService _settingsService;
    private readonly StubDatabaseExportService _exportService;
    private readonly StubNotificationService _notificationService;
    private readonly StubModelRunner _modelRunner;

    public SettingsViewModelTests()
    {
        _settingsService = new StubUserSettingsService(new UserSettings
        {
            ConfidenceThreshold = 0.35,
            PreferredDevice = "Cpu",
            DefaultStartupPage = "Live Detection",
            SkipDashboardOnStartup = true,
            ReportExportPath = @"C:\Exports\Reports",
            DatabaseExportPath = @"C:\Exports\Backups",
            CropOutputDirectory = @"C:\Exports\Crops"
        });

        _exportService = new StubDatabaseExportService(@"C:\Exports\Backups");
        _notificationService = new StubNotificationService();
        _modelRunner = new StubModelRunner();
    }

    [Fact]
    public async Task LoadAsync_PopulatesAllPropertiesFromSettingsService()
    {
        // Arrange
        var vm = new SettingsViewModel(_settingsService, _exportService, _notificationService, _modelRunner);

        // Act
        await vm.LoadAsync();

        // Assert
        vm.ConfidenceThreshold.Should().Be(0.35);
        vm.SelectedDevice.Should().Be("Cpu");
        vm.SelectedStartupPage.Should().Be("Live Detection");
        vm.SkipDashboardOnStartup.Should().BeTrue();
        vm.ReportExportPath.Should().Be(@"C:\Exports\Reports");
        vm.DatabaseExportPath.Should().Be(@"C:\Exports\Backups");
        vm.CropOutputDirectory.Should().Be(@"C:\Exports\Crops");
    }

    [Fact]
    public async Task SaveAsync_PersistsMutatedSettingsToSettingsService()
    {
        // Arrange
        var vm = new SettingsViewModel(_settingsService, _exportService, _notificationService, _modelRunner);
        await vm.LoadAsync();

        vm.ConfidenceThreshold = 0.55;
        vm.SelectedDevice = "GpuDirectML";
        vm.SelectedStartupPage = "History";
        vm.SkipDashboardOnStartup = false;
        vm.ReportExportPath = @"D:\CustomReportPath";
        vm.DatabaseExportPath = @"D:\CustomDbPath";
        vm.CropOutputDirectory = @"D:\CustomCropPath";

        // Act
        await vm.SaveAsync();

        // Assert
        var saved = _settingsService.CurrentSettings;
        saved.ConfidenceThreshold.Should().Be(0.55);
        saved.PreferredDevice.Should().Be("GpuDirectML");
        saved.DefaultStartupPage.Should().Be("History");
        saved.SkipDashboardOnStartup.Should().BeFalse();
        saved.ReportExportPath.Should().Be(@"D:\CustomReportPath");
        saved.DatabaseExportPath.Should().Be(@"D:\CustomDbPath");
        saved.CropOutputDirectory.Should().Be(@"D:\CustomCropPath");
    }

    [Fact]
    public async Task ExportDatabaseCommand_ExecutesExportAndSendsToastNotification()
    {
        // Arrange
        var vm = new SettingsViewModel(_settingsService, _exportService, _notificationService, _modelRunner);
        await vm.LoadAsync();

        // Act
        await vm.ExportDatabaseCommand.ExecuteAsync(null);

        // Assert
        _exportService.ExportCalled.Should().BeTrue();
        _notificationService.LastToastTitle.Should().NotBeNullOrEmpty();
        _notificationService.LastToastMessage.Should().NotBeNullOrEmpty();
        vm.ExportStatusMessage.Should().ContainEquivalentOf("succeeded");
    }

    [Fact]
    public void CloseCommand_RaisesRequestCloseEvent()
    {
        // Arrange
        var vm = new SettingsViewModel(_settingsService, _exportService, _notificationService, _modelRunner);
        bool closeRaised = false;
        vm.RequestClose += (_, _) => closeRaised = true;

        // Act
        vm.CloseCommand.Execute(null);

        // Assert
        closeRaised.Should().BeTrue();
    }

    [Fact]
    public void OpenUrlCommand_WithValidUrl_InvokesLauncher()
    {
        // Arrange
        string? launched = null;
        var vm = new SettingsViewModel(_settingsService, _exportService, _notificationService, _modelRunner, url => launched = url);

        // Act
        vm.OpenUrlCommand.Execute("https://rickyslash.my.id");

        // Assert
        launched.Should().Be("https://rickyslash.my.id");
    }

    private class StubUserSettingsService : IUserSettingsService
    {
        public UserSettings CurrentSettings { get; private set; }

        public StubUserSettingsService(UserSettings initial)
        {
            CurrentSettings = initial;
        }

        public Task<UserSettings> LoadSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentSettings);

        public Task SaveSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default)
        {
            CurrentSettings = settings;
            return Task.CompletedTask;
        }
    }

    private class StubDatabaseExportService : IDatabaseExportService
    {
        public string DefaultExportDirectory { get; }
        public bool ExportCalled { get; private set; }

        public StubDatabaseExportService(string defaultDir)
        {
            DefaultExportDirectory = defaultDir;
        }

        public Task<string> ExportDatabaseAsync(string? destinationDirectory = null, CancellationToken cancellationToken = default)
        {
            ExportCalled = true;
            return Task.FromResult(Path.Combine(destinationDirectory ?? DefaultExportDirectory, "pcb-deftek-export-test.db"));
        }
    }

    private class StubNotificationService : INotificationService
    {
        public string? LastToastTitle { get; private set; }
        public string? LastToastMessage { get; private set; }

        public Task ShowToastAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            LastToastTitle = title;
            LastToastMessage = message;
            return Task.CompletedTask;
        }
    }

    private class StubModelRunner : IOnnxModelRunner
    {
        public ExecutionDevice ActiveDevice => ExecutionDevice.Cpu;
        public ModelMetadataInfo Metadata => new("model.onnx", ExecutionDevice.Cpu, "images", new[] { 1, 3, 640, 640 }, "output0", new[] { 1, 13, 8400 }, true);

        public void Dispose() { }
        public Microsoft.ML.OnnxRuntime.IDisposableReadOnlyCollection<Microsoft.ML.OnnxRuntime.DisposableNamedOnnxValue> Run(IReadOnlyCollection<Microsoft.ML.OnnxRuntime.NamedOnnxValue> inputs) => throw new NotImplementedException();
        public Task<Microsoft.ML.OnnxRuntime.IDisposableReadOnlyCollection<Microsoft.ML.OnnxRuntime.DisposableNamedOnnxValue>> RunAsync(IReadOnlyCollection<Microsoft.ML.OnnxRuntime.NamedOnnxValue> inputs, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public void WarmUp() { }
    }
}
