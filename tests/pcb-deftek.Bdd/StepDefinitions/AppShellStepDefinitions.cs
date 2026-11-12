using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using pcb_deftek.Infrastructure.Persistence;
using pcb_deftek.Infrastructure.Persistence.Migrations;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Presentation.Navigation;
using pcb_deftek.Presentation.ViewModels;
using Reqnroll;

namespace pcb_deftek.Bdd.StepDefinitions;

[Binding]
public class AppShellStepDefinitions : IDisposable
{
    private readonly string _dbDir;
    private readonly string _dbPath;
    private ServiceProvider? _serviceProvider;
    private INavigationService? _navigationService;
    private IStartupRoutingService? _startupRoutingService;
    private ISettingsRepository? _settingsRepository;
    private MainViewModel? _mainViewModel;

    public AppShellStepDefinitions()
    {
        _dbDir = Path.Combine(Path.GetTempPath(), "pcb-deftek-bdd-shell-" + Guid.NewGuid().ToString("N"));
        _dbPath = Path.Combine(_dbDir, "app.db");
        Directory.CreateDirectory(_dbDir);
    }

    [Given(@"the application dependencies are configured for shell navigation")]
    public async Task GivenTheApplicationDependenciesAreConfiguredForShellNavigation()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);

        // Override connection factory with isolated temp database
        services.AddSingleton<ISqliteConnectionFactory>(_ => new TestConnectionFactory(_dbPath));

        _serviceProvider = services.BuildServiceProvider();

        // Run migrations
        var migrator = _serviceProvider.GetRequiredService<IDatabaseMigrator>();
        await migrator.MigrateToLatestAsync();

        _navigationService = _serviceProvider.GetRequiredService<INavigationService>();
        _startupRoutingService = _serviceProvider.GetRequiredService<IStartupRoutingService>();
        _settingsRepository = _serviceProvider.GetRequiredService<ISettingsRepository>();
    }

    [Given(@"the user has not enabled skip dashboard on startup")]
    public async Task GivenTheUserHasNotEnabledSkipDashboardOnStartup()
    {
        _settingsRepository.Should().NotBeNull();
        await _settingsRepository!.SetSettingAsync("Settings:App:SkipDashboardOnStartup", false);
        await _settingsRepository!.SetSettingAsync("Settings:App:DefaultStartupPage", "Dashboard");
    }

    [Given(@"the user has enabled skip dashboard on startup")]
    public async Task GivenTheUserHasEnabledSkipDashboardOnStartup()
    {
        _settingsRepository.Should().NotBeNull();
        await _settingsRepository!.SetSettingAsync("Settings:App:SkipDashboardOnStartup", true);
    }

    [When(@"the application executes startup routing")]
    public async Task WhenTheApplicationExecutesStartupRouting()
    {
        _startupRoutingService.Should().NotBeNull();
        await _startupRoutingService!.RouteStartupAsync();
    }

    [Given(@"the application shell is initialized")]
    public void GivenTheApplicationShellIsInitialized()
    {
        _serviceProvider.Should().NotBeNull();
        _mainViewModel = _serviceProvider!.GetRequiredService<MainViewModel>();
    }

    [When(@"the operator navigates to ""(.*)""")]
    public void WhenTheOperatorNavigatesTo(string routeName)
    {
        _mainViewModel.Should().NotBeNull();

        switch (routeName)
        {
            case "Dashboard":
                _mainViewModel!.NavigateToDashboardCommand.Execute(null);
                break;
            case "Live Detection":
                _mainViewModel!.NavigateToLiveDetectionCommand.Execute(null);
                break;
            case "File-Based Detection":
                _mainViewModel!.NavigateToFileDetectionCommand.Execute(null);
                break;
            case "History":
                _mainViewModel!.NavigateToHistoryCommand.Execute(null);
                break;
            default:
                throw new ArgumentException($"Unknown navigation route: {routeName}", nameof(routeName));
        }
    }

    [Then(@"the active workspace view model should be (.*)")]
    public void ThenTheActiveWorkspaceViewModelShouldBe(string expectedPageName)
    {
        _navigationService.Should().NotBeNull();
        _navigationService!.CurrentViewModel.Should().NotBeNull();

        switch (expectedPageName.Trim().ToLowerInvariant())
        {
            case "dashboard":
                _navigationService.CurrentViewModel.Should().BeOfType<DashboardViewModel>();
                break;
            case "live detection":
                _navigationService.CurrentViewModel.Should().BeOfType<LiveDetectionViewModel>();
                break;
            case "file-based detection":
                _navigationService.CurrentViewModel.Should().BeOfType<FileDetectionViewModel>();
                break;
            case "history":
                _navigationService.CurrentViewModel.Should().BeOfType<HistoryViewModel>();
                break;
            default:
                throw new ArgumentException($"Unknown expected page view model: {expectedPageName}");
        }
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_dbDir))
            {
                Directory.Delete(_dbDir, true);
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
        public Microsoft.Data.Sqlite.SqliteConnection CreateConnection() => new($"Data Source={DatabasePath}");
        public async ValueTask<Microsoft.Data.Sqlite.SqliteConnection> CreateOpenConnectionAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);
            return conn;
        }
    }
}
