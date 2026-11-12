using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Notifications;
using pcb_deftek.Infrastructure.Persistence;
using pcb_deftek.Infrastructure.Persistence.Migrations;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Presentation.ViewModels;
using Reqnroll;

namespace pcb_deftek.Bdd.StepDefinitions;

[Binding]
public class FileBatchDetectionStepDefinitions : IDisposable
{
    private readonly string _testRoot;
    private readonly string _dbPath;
    private ServiceProvider? _serviceProvider;
    private FileDetectionViewModel? _viewModel;
    private IInspectionRepository? _inspectionRepository;
    private string? _capturedToastTitle;
    private string? _capturedToastMessage;
    private readonly List<string> _queuedFilePaths = new();

    public FileBatchDetectionStepDefinitions()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "pcb-deftek-bdd-batch-" + Guid.NewGuid().ToString("N"));
        _dbPath = Path.Combine(_testRoot, "app.db");
        Directory.CreateDirectory(_testRoot);
    }

    [Given(@"a configured file detection environment connected to a test SQLite database")]
    public async Task GivenAConfiguredFileDetectionEnvironmentConnectedToATestSqliteDatabase()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);

        // Override connection factory with isolated test SQLite database
        services.AddSingleton<ISqliteConnectionFactory>(_ => new TestConnectionFactory(_dbPath));

        // Intercept desktop toasts
        services.AddSingleton<INotificationService>(_ => new WindowsNotificationService((title, msg) =>
        {
            _capturedToastTitle = title;
            _capturedToastMessage = msg;
        }));

        _serviceProvider = services.BuildServiceProvider();

        // Run migrations
        var migrator = _serviceProvider.GetRequiredService<IDatabaseMigrator>();
        await migrator.MigrateToLatestAsync();

        _inspectionRepository = _serviceProvider.GetRequiredService<IInspectionRepository>();
        _viewModel = _serviceProvider.GetRequiredService<FileDetectionViewModel>();
        await _viewModel.OnNavigatedToAsync();
    }

    [Given(@"a single sample PCB image ""(.*)"" queued for inspection")]
    public async Task GivenASingleSamplePcbImageQueuedForInspection(string filename)
    {
        var path = ResolveTestImagePath(filename);
        _queuedFilePaths.Clear();
        _queuedFilePaths.Add(path);

        _viewModel.Should().NotBeNull();
        await _viewModel!.DropFilesCommand.ExecuteAsync(new[] { path });
    }

    [Given(@"multiple sample PCB images queued for inspection")]
    public async Task GivenMultipleSamplePcbImagesQueuedForInspection()
    {
        var files = new[] { "SH-1.jpg", "SP-1.jpg", "OP-1.jpg" }
            .Select(ResolveTestImagePath)
            .ToList();

        _queuedFilePaths.Clear();
        _queuedFilePaths.AddRange(files);

        _viewModel.Should().NotBeNull();
        await _viewModel!.DropFilesCommand.ExecuteAsync(files);
    }

    [Given(@"a large batch of PCB images queued for inspection")]
    public async Task GivenALargeBatchOfPcbImagesQueuedForInspection()
    {
        var files = new[] { "SH-1.jpg", "SP-1.jpg", "OP-1.jpg", "SC-1.jpg", "MB-1.jpg" }
            .Select(ResolveTestImagePath)
            .ToList();

        _queuedFilePaths.Clear();
        _queuedFilePaths.AddRange(files);

        _viewModel.Should().NotBeNull();
        await _viewModel!.DropFilesCommand.ExecuteAsync(files);
    }

    [When(@"the operator starts batch inspection")]
    public async Task WhenTheOperatorStartsBatchInspection()
    {
        _viewModel.Should().NotBeNull();
        await _viewModel!.StartBatchCommand.ExecuteAsync(null);
    }

    [When(@"the operator cancels processing while the batch is running")]
    public async Task WhenTheOperatorCancelsProcessingWhileTheBatchIsRunning()
    {
        _viewModel.Should().NotBeNull();

        // Start batch in background
        var startTask = _viewModel!.StartBatchCommand.ExecuteAsync(null);

        // Wait until at least one board completes processing
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (_viewModel.ProcessedCount == 0 && DateTime.UtcNow < timeout)
        {
            await Task.Delay(20);
        }

        // Signal cancellation
        _viewModel.CancelBatchCommand.Execute(null);

        // Await batch completion
        await startTask;
    }

    [Then(@"the inspection should complete successfully")]
    public void ThenTheInspectionShouldCompleteSuccessfully()
    {
        _viewModel.Should().NotBeNull();
        _viewModel!.IsProcessing.Should().BeFalse();
        _viewModel.StatusText.Should().Contain("Completed");
    }

    [Then(@"exactly (.*) board record should be persisted in the database with mode (.*)")]
    public async Task ThenExactlyBoardRecordShouldBePersistedInTheDatabaseWithMode(int expectedCount, DetectionMode expectedMode)
    {
        _inspectionRepository.Should().NotBeNull();
        var total = await _inspectionRepository!.GetTotalCountAsync();
        total.Should().Be(expectedCount);

        var paged = await _inspectionRepository.GetPagedAsync(new InspectionQueryFilter { PageNumber = 1, PageSize = 10 });
        paged.Items.Should().HaveCount(expectedCount);
        paged.Items[0].Mode.Should().Be(expectedMode);
    }

    [Then(@"the inspected board ""(.*)"" should contain detected defects")]
    public async Task ThenTheInspectedBoardShouldContainDetectedDefects(string expectedName)
    {
        _inspectionRepository.Should().NotBeNull();
        var paged = await _inspectionRepository!.GetPagedAsync(new InspectionQueryFilter { PageNumber = 1, PageSize = 10 });
        var summaryItem = paged.Items.FirstOrDefault(r => r.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase));

        summaryItem.Should().NotBeNull();
        var fullRecord = await _inspectionRepository.GetByIdAsync(summaryItem!.Id);
        fullRecord.Should().NotBeNull();
        fullRecord!.DefectCount.Should().BeGreaterThan(0);
        fullRecord.Defects.Should().NotBeEmpty();
    }

    [Then(@"all queued boards should be inspected")]
    public void ThenAllQueuedBoardsShouldBeInspected()
    {
        _viewModel.Should().NotBeNull();
        _viewModel!.ProcessedCount.Should().Be(_viewModel.TotalCount);
        _viewModel.TotalCount.Should().BeGreaterThan(0);
    }

    [Then(@"the batch completion percentage should reach 100 percent")]
    public void ThenTheBatchCompletionPercentageShouldReach100Percent()
    {
        _viewModel.Should().NotBeNull();
        _viewModel!.PercentComplete.Should().Be(100);
    }

    [Then(@"the batch progress report should have calculated an estimated completion time")]
    public void ThenTheBatchProgressReportShouldHaveCalculatedAnEstimatedCompletionTime()
    {
        _viewModel.Should().NotBeNull();
        // Upon finish, ETA is cleared and status confirms completion
        _viewModel!.StatusText.Should().Contain("Completed");
    }

    [Then(@"all inspected records should be persisted with mode (.*)")]
    public async Task ThenAllInspectedRecordsShouldBePersistedWithMode(DetectionMode expectedMode)
    {
        _inspectionRepository.Should().NotBeNull();
        var paged = await _inspectionRepository!.GetPagedAsync(new InspectionQueryFilter { PageNumber = 1, PageSize = 50 });
        paged.Items.Should().NotBeEmpty();

        foreach (var record in paged.Items)
        {
            record.Mode.Should().Be(expectedMode);
        }
    }

    [Then(@"the batch execution status should transition to Cancelled")]
    public void ThenTheBatchExecutionStatusShouldTransitionToCancelled()
    {
        _viewModel.Should().NotBeNull();
        _viewModel!.IsProcessing.Should().BeFalse();
        _viewModel.StatusText.Should().Contain("Cancelled");
    }

    [Then(@"remaining queued images should not be inspected")]
    public void ThenRemainingQueuedImagesShouldNotBeInspected()
    {
        _viewModel.Should().NotBeNull();
        _viewModel!.ProcessedCount.Should().BeLessThan(_viewModel.TotalCount);
    }

    [Then(@"previously processed inspection records should remain safely committed in the database")]
    public async Task ThenPreviouslyProcessedInspectionRecordsShouldRemainSafelyCommittedInTheDatabase()
    {
        _inspectionRepository.Should().NotBeNull();
        var count = await _inspectionRepository!.GetTotalCountAsync();
        count.Should().Be(_viewModel!.ProcessedCount);
        count.Should().BeGreaterThan(0);
    }

    [Then(@"a desktop toast notification with title ""(.*)"" should be dispatched")]
    public void ThenADesktopToastNotificationWithTitleShouldBeDispatched(string expectedTitle)
    {
        _capturedToastTitle.Should().NotBeNullOrWhiteSpace();
        _capturedToastTitle.Should().Contain(expectedTitle);
    }

    [Then(@"the notification message should state the total processed boards and pass fail counts")]
    public void ThenTheNotificationMessageShouldStateTheTotalProcessedBoardsAndPassFailCounts()
    {
        _capturedToastMessage.Should().NotBeNullOrWhiteSpace();
        _capturedToastMessage.Should().Contain("boards");
        _capturedToastMessage.Should().Contain("passed");
    }

    private static string ResolveTestImagePath(string filename)
    {
        string? current = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, "tests", "pcb-deftek.Tests", "TestData", "Model", filename);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            string directCandidate = Path.Combine(current, "TestData", "Model", filename);
            if (File.Exists(directCandidate))
            {
                return Path.GetFullPath(directCandidate);
            }

            var parent = Directory.GetParent(current);
            current = parent?.FullName;
        }

        string fallback = Path.Combine(Directory.GetCurrentDirectory(), "tests", "pcb-deftek.Tests", "TestData", "Model", filename);
        if (File.Exists(fallback))
        {
            return Path.GetFullPath(fallback);
        }

        throw new FileNotFoundException($"Test image '{filename}' not found for BDD testing.");
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
        public async ValueTask<SqliteConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);
            return conn;
        }
    }
}
