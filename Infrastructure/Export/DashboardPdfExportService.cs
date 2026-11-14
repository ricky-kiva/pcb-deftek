using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pcb_deftek.Common.Exceptions;
using pcb_deftek.Common.Storage;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Notifications;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Infrastructure.Services;
using pcb_deftek.Infrastructure.Vision;

namespace pcb_deftek.Infrastructure.Export;

/// <summary>
/// Orchestrates an audit PDF export: resolves the destination, queries the filtered evidence,
/// stages annotated PNGs one at a time, renders the PDF off the UI thread, moves it into place
/// atomically, and cleans every temporary artifact on cancellation or failure.
/// </summary>
public sealed class DashboardPdfExportService : IDashboardExportService, IDisposable
{
    internal const int MaxAnnotatedDimension = 1600;
    private const double StagingStartPercent = 15;
    private const double StagingEndPercent = 90;
    private const double CompositionPercent = 95;
    private const string ReportFilePrefix = "PCB-Deftek-Report";

    private readonly IDashboardRepository _repository;
    private readonly IUserSettingsService _settingsService;
    private readonly IImageAnnotationService _annotationService;
    private readonly IInspectionDeletionService _deletionService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<DashboardPdfExportService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly string _stagingRoot;
    private readonly string _defaultReportDirectory;
    private readonly SemaphoreSlim _exportGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly object _artifactGate = new();
    private string? _activeStagingDirectory;
    private string? _activeTempPdfPath;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="DashboardPdfExportService"/> class.</summary>
    public DashboardPdfExportService(
        IDashboardRepository repository,
        IUserSettingsService settingsService,
        IImageAnnotationService annotationService,
        IInspectionDeletionService deletionService,
        INotificationService notificationService,
        ILogger<DashboardPdfExportService>? logger = null,
        TimeProvider? timeProvider = null)
        : this(
            repository,
            settingsService,
            annotationService,
            deletionService,
            notificationService,
            logger,
            timeProvider,
            stagingDirectoryRoot: null,
            defaultReportDirectory: null)
    {
    }

    internal DashboardPdfExportService(
        IDashboardRepository repository,
        IUserSettingsService settingsService,
        IImageAnnotationService annotationService,
        IInspectionDeletionService deletionService,
        INotificationService notificationService,
        ILogger<DashboardPdfExportService>? logger,
        TimeProvider? timeProvider,
        string? stagingDirectoryRoot,
        string? defaultReportDirectory)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _annotationService = annotationService ?? throw new ArgumentNullException(nameof(annotationService));
        _deletionService = deletionService ?? throw new ArgumentNullException(nameof(deletionService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _logger = logger ?? NullLogger<DashboardPdfExportService>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _stagingRoot = string.IsNullOrWhiteSpace(stagingDirectoryRoot)
            ? Path.Combine(Path.GetTempPath(), "pcb-deftek-export")
            : Path.GetFullPath(stagingDirectoryRoot);
        _defaultReportDirectory = NormalizeDirectory(
            string.IsNullOrWhiteSpace(defaultReportDirectory)
                ? KnownFolderPaths.GetReportExportDirectory()
                : defaultReportDirectory);
    }

    /// <inheritdoc />
    public async Task<int> CountMatchingInspectionsAsync(
        DashboardFilter filter,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(filter);

        return await _repository
            .GetMatchingInspectionCountAsync(filter, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DashboardExportResult> ExportAsync(
        DashboardFilter filter,
        IProgress<DashboardExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(filter);

        if (!await _exportGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new ExportException("Another report export is already in progress.");
        }

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCts.Token);
            CancellationToken token = linkedCts.Token;
            token.ThrowIfCancellationRequested();

            string? destinationDirectory = null;
            string? stagingDirectory = null;
            string? tempPdfPath = null;

            try
            {
                IReadOnlyList<InspectionRecord> inspections = await _repository
                    .GetMatchingInspectionsAsync(filter, token)
                    .ConfigureAwait(false);

                if (inspections.Count == 0)
                {
                    return DashboardExportResult.NoMatches();
                }

                DateTime localNow = _timeProvider.GetLocalNow().DateTime;
                ReportProgress(progress, 0, "Preparing report");

                UserSettings settings = await _settingsService
                    .LoadSettingsAsync(token)
                    .ConfigureAwait(false);
                destinationDirectory = ResolveDestinationDirectory(settings);
                Directory.CreateDirectory(destinationDirectory);

                DashboardStatistics statistics = await _repository
                    .GetStatisticsAsync(filter, token)
                    .ConfigureAwait(false);

                ReportProgress(
                    progress,
                    10,
                    $"Loaded {inspections.Count.ToString("N0", CultureInfo.CurrentCulture)} inspection(s)");

                stagingDirectory = Path.Combine(_stagingRoot, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stagingDirectory);
                SetActiveStagingDirectory(stagingDirectory);

                var included = new List<InspectionRecord>(inspections.Count);
                var stagedPaths = new Dictionary<Guid, string>(inspections.Count);

                for (int index = 0; index < inspections.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    InspectionRecord record = inspections[index];

                    if (index == 0)
                    {
                        ReportProgress(
                            progress,
                            StagingStartPercent,
                            $"Staging evidence 1 of {inspections.Count}");
                    }

                    string? stagedPath = await StageAnnotatedImageAsync(record, stagingDirectory, token)
                        .ConfigureAwait(false);

                    if (stagedPath is null)
                    {
                        await DeleteUnreadableInspectionAsync(record, token).ConfigureAwait(false);
                    }
                    else
                    {
                        included.Add(record);
                        stagedPaths[record.Id] = stagedPath;
                    }

                    double percentage = StagingStartPercent +
                        ((StagingEndPercent - StagingStartPercent) * (index + 1) / inspections.Count);
                    ReportProgress(
                        progress,
                        percentage,
                        $"Staged evidence {index + 1} of {inspections.Count}");
                }

                token.ThrowIfCancellationRequested();

                if (included.Count == 0)
                {
                    throw new ExportException(
                        "No inspections with readable images matched the selected filter.",
                        destinationDirectory);
                }

                ReportProgress(progress, CompositionPercent, "Composing report");

                DashboardReportDocument report = DashboardReportContentFactory.Create(
                    statistics,
                    included,
                    stagedPaths,
                    filter,
                    localNow);

                tempPdfPath = Path.Combine(
                    destinationDirectory,
                    $".pcb-deftek-{Guid.NewGuid():N}.tmp");
                SetActiveTempPdfPath(tempPdfPath);

                string tempPath = tempPdfPath;
                await Task.Run(
                    () =>
                    {
                        using var stream = new FileStream(
                            tempPath,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None);
                        DashboardReportPdfDocument.Render(report, stream);
                    },
                    token).ConfigureAwait(false);

                token.ThrowIfCancellationRequested();
                string finalPath = MoveIntoPlace(tempPath, destinationDirectory, localNow);

                tempPdfPath = null;
                SetActiveTempPdfPath(null);

                ReportProgress(progress, 100, "Report saved");
                await TryNotifyAsync(
                    "Report exported",
                    $"Saved to {finalPath}").ConfigureAwait(false);

                return DashboardExportResult.Exported(finalPath, included.Count);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Report export failed. Destination: {Destination}",
                    destinationDirectory ?? "(unresolved)");

                var exportException = ex as ExportException
                    ?? new ExportException(
                        "The report could not be exported. See the application log for details.",
                        destinationDirectory,
                        ex);

                await TryNotifyAsync("Report export failed", exportException.Message)
                    .ConfigureAwait(false);
                throw exportException;
            }
            finally
            {
                CleanupArtifacts(stagingDirectory, tempPdfPath);
            }
        }
        finally
        {
            _exportGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _lifetimeCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already cancelled/disposed.
        }

        string? stagingDirectory;
        string? tempPdfPath;
        lock (_artifactGate)
        {
            stagingDirectory = _activeStagingDirectory;
            tempPdfPath = _activeTempPdfPath;
        }

        TryDeleteFile(tempPdfPath);
        TryDeleteDirectory(stagingDirectory);

        _lifetimeCts.Dispose();
    }

    /// <summary>
    /// Builds the report filename for a local timestamp and collision suffix.
    /// </summary>
    internal static string BuildFileName(DateTime localTime, int collisionIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(collisionIndex);

        string suffix = collisionIndex == 0 ? string.Empty : $" ({collisionIndex})";
        return $"{ReportFilePrefix}-{localTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}{suffix}.pdf";
    }

    /// <summary>
    /// Finds the first available report path at or above <paramref name="startingIndex"/>.
    /// </summary>
    internal static string FindAvailablePath(
        string directory,
        DateTime localTime,
        Func<string, bool> exists,
        int startingIndex = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(exists);
        ArgumentOutOfRangeException.ThrowIfNegative(startingIndex);

        for (int index = startingIndex; index < 10_000; index++)
        {
            string candidate = Path.Combine(directory, BuildFileName(localTime, index));
            if (!exists(candidate))
            {
                return candidate;
            }
        }

        throw new ExportException("A unique report filename could not be chosen.", directory);
    }

    private async Task<string?> StageAnnotatedImageAsync(
        InspectionRecord record,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        return await Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                BitmapSource? bitmap = null;
                for (int attempt = 0; attempt < 2 && bitmap is null; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (attempt > 0)
                    {
                        Thread.Sleep(50);
                    }

                    bitmap = _annotationService.AnnotateImage(
                        record.ImagePath,
                        record.Defects,
                        MaxAnnotatedDimension);
                }

                if (bitmap is null)
                {
                    return null;
                }

                if (!bitmap.IsFrozen)
                {
                    bitmap.Freeze();
                }

                string stagedPath = Path.Combine(stagingDirectory, $"{record.Id:N}.png");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = new FileStream(
                    stagedPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                encoder.Save(stream);
                return stagedPath;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task DeleteUnreadableInspectionAsync(
        InspectionRecord record,
        CancellationToken cancellationToken)
    {
        if (!InvalidInspectionImageGuard.IsParentDirectoryAccessible(record.ImagePath, _logger))
        {
            return;
        }

        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                InspectionDeletionResult result = await _deletionService
                    .DeleteAsync(record.Id, cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogInformation(
                    "Removed unreadable inspection {InspectionId}: {RecordCount} record(s), {FileCount} file(s).",
                    record.Id,
                    result.DeletedRecordCount,
                    result.DeletedFileCount);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt == 0)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to remove unreadable inspection {InspectionId}; retrying once.",
                        record.Id);
                }
                else
                {
                    _logger.LogError(
                        ex,
                        "Failed to remove unreadable inspection {InspectionId}; export continues.",
                        record.Id);
                }
            }
        }
    }

    private string ResolveDestinationDirectory(UserSettings settings)
    {
        string configured = settings.ReportExportPath;
        string directory = string.IsNullOrWhiteSpace(configured)
            ? _defaultReportDirectory
            : NormalizeDirectory(configured);

        return directory;
    }

    private static string MoveIntoPlace(string tempPath, string destinationDirectory, DateTime localTime)
    {
        for (int attempt = 0; attempt < 10_000; attempt++)
        {
            string candidate = FindAvailablePath(destinationDirectory, localTime, File.Exists);
            try
            {
                File.Move(tempPath, candidate);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // Another export claimed this name; try the next suffix.
            }
        }

        throw new ExportException(
            "A unique report filename could not be chosen.",
            destinationDirectory);
    }

    private void ReportProgress(
        IProgress<DashboardExportProgress>? progress,
        double percentage,
        string? message)
    {
        try
        {
            progress?.Report(new DashboardExportProgress(percentage, message));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "An export progress observer failed at {Percentage}%.", percentage);
        }
    }

    private async Task TryNotifyAsync(string title, string message)
    {
        try
        {
            await _notificationService
                .ShowToastAsync(title, message, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch report export notification '{Title}'.", title);
        }
    }

    private void CleanupArtifacts(string? stagingDirectory, string? tempPdfPath)
    {
        lock (_artifactGate)
        {
            _activeStagingDirectory = null;
            _activeTempPdfPath = null;
        }

        TryDeleteFile(tempPdfPath);
        TryDeleteDirectory(stagingDirectory);
    }

    private void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or System.Security.SecurityException)
        {
            _logger.LogWarning(ex, "Failed to delete export artifact '{Path}'.", path);
        }
    }

    private void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or System.Security.SecurityException)
        {
            _logger.LogWarning(ex, "Failed to delete export staging directory '{Path}'.", path);
        }
    }

    private void SetActiveStagingDirectory(string path)
    {
        lock (_artifactGate)
        {
            _activeStagingDirectory = path;
        }
    }

    private void SetActiveTempPdfPath(string? path)
    {
        lock (_artifactGate)
        {
            _activeTempPdfPath = path;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DashboardPdfExportService));
        }
    }

    private static string NormalizeDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path.Trim());
        string separator = Path.DirectorySeparatorChar.ToString();
        return fullPath.EndsWith(separator, StringComparison.Ordinal)
            ? fullPath
            : fullPath + separator;
    }
}
