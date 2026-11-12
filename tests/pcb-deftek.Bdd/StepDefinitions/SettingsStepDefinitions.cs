using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Notifications;
using pcb_deftek.Infrastructure.Persistence;
using pcb_deftek.Infrastructure.Persistence.DatabaseBackup;
using pcb_deftek.Infrastructure.Persistence.DatabaseExport;
using pcb_deftek.Infrastructure.Persistence.Migrations;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Presentation.ViewModels;
using Reqnroll;

namespace pcb_deftek.Bdd.StepDefinitions;

[Binding]
public class SettingsStepDefinitions : IDisposable
{
    private readonly string _testRoot;
    private readonly string _dbPath;
    private string? _customExportDir;
    private ServiceProvider? _serviceProvider;
    private IUserSettingsService? _userSettingsService;
    private SettingsViewModel? _settingsViewModel;
    private string? _capturedToastTitle;
    private string? _capturedToastMessage;
    private string? _exportedFilePath;

    public SettingsStepDefinitions()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "pcb-deftek-bdd-settings-" + Guid.NewGuid().ToString("N"));
        _dbPath = Path.Combine(_testRoot, "app.db");
        Directory.CreateDirectory(_testRoot);
    }

    [Given(@"a configured settings service connected to a test SQLite database")]
    public async Task GivenAConfiguredSettingsServiceConnectedToATestSqliteDatabase()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);

        // Override factory to point to test SQLite database
        services.AddSingleton<ISqliteConnectionFactory>(_ => new TestConnectionFactory(_dbPath));

        // Intercept notification toasts for BDD assertions
        services.AddSingleton<INotificationService>(_ => new WindowsNotificationService((title, msg) =>
        {
            _capturedToastTitle = title;
            _capturedToastMessage = msg;
        }));

        _serviceProvider = services.BuildServiceProvider();

        // Run migrations
        var migrator = _serviceProvider.GetRequiredService<IDatabaseMigrator>();
        await migrator.MigrateToLatestAsync();

        _userSettingsService = _serviceProvider.GetRequiredService<IUserSettingsService>();
        _settingsViewModel = _serviceProvider.GetRequiredService<SettingsViewModel>();
        await _settingsViewModel.LoadAsync();
    }

    [When(@"the operator adjusts the confidence threshold to (.*)")]
    public void WhenTheOperatorAdjustsTheConfidenceThresholdTo(double threshold)
    {
        _settingsViewModel.Should().NotBeNull();
        _settingsViewModel!.ConfidenceThreshold = threshold;
    }

    [When(@"the operator saves the updated settings")]
    public async Task WhenTheOperatorSavesTheUpdatedSettings()
    {
        _settingsViewModel.Should().NotBeNull();
        await _settingsViewModel!.SaveAsync();
    }

    [Then(@"the persisted confidence threshold in the database should be (.*)")]
    public async Task ThenThePersistedConfidenceThresholdInTheDatabaseShouldBe(double expectedThreshold)
    {
        _userSettingsService.Should().NotBeNull();
        var reloaded = await _userSettingsService!.LoadSettingsAsync();
        reloaded.ConfidenceThreshold.Should().BeApproximately(expectedThreshold, 0.001);
    }

    [Given(@"a custom destination export folder")]
    public void GivenACustomDestinationExportFolder()
    {
        _customExportDir = Path.Combine(_testRoot, "Exports");
        Directory.CreateDirectory(_customExportDir);
        _settingsViewModel.Should().NotBeNull();
        _settingsViewModel!.DatabaseExportPath = _customExportDir;
    }

    [When(@"the operator triggers an immediate database export")]
    public async Task WhenTheOperatorTriggersAnImmediateDatabaseExport()
    {
        _settingsViewModel.Should().NotBeNull();
        await _settingsViewModel!.ExportDatabaseCommand.ExecuteAsync(null);
    }

    [Then(@"a valid SQLite export database file should be created in the destination folder")]
    public void ThenAValidSqliteExportDatabaseFileShouldBeCreatedInTheDestinationFolder()
    {
        _customExportDir.Should().NotBeNull();
        var files = Directory.GetFiles(_customExportDir!, "pcb-deftek-export-*.db");
        files.Should().NotBeEmpty("An export database file should have been written to the destination folder");

        _exportedFilePath = files[0];
        File.Exists(_exportedFilePath).Should().BeTrue();

        // Verify valid SQLite structure
        using var conn = new SqliteConnection($"Data Source={_exportedFilePath};Mode=ReadOnly");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table';";
        var tableCount = Convert.ToInt32(cmd.ExecuteScalar());
        tableCount.Should().BeGreaterThan(0, "Exported database must contain SQLite schema tables");
    }

    [Then(@"a desktop toast notification should be dispatched informing the user")]
    public void ThenADesktopToastNotificationShouldBeDispatchedInformingTheUser()
    {
        _capturedToastTitle.Should().NotBeNullOrWhiteSpace();
        _capturedToastMessage.Should().NotBeNullOrWhiteSpace();
        _capturedToastTitle.Should().ContainEquivalentOf("Database Export");
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
        try
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private class TestConnectionFactory : ISqliteConnectionFactory
    {
        public string DatabasePath { get; }
        public TestConnectionFactory(string path) => DatabasePath = path;
        public SqliteConnection CreateConnection() => new($"Data Source={DatabasePath}");
        public async ValueTask<SqliteConnection> CreateOpenConnectionAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);
            return conn;
        }
    }
}
