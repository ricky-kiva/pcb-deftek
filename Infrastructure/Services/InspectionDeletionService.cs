using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pcb_deftek.Common.Storage;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Persistence.Repositories;

namespace pcb_deftek.Infrastructure.Services;

/// <summary>
/// Default <see cref="IInspectionDeletionService"/> implementation. Deletes database rows first
/// (transactionally), then removes only capture files that resolve under the configured captures root.
/// </summary>
public sealed class InspectionDeletionService : IInspectionDeletionService
{
    private const string RawImageSuffix = "_raw.png";
    private const string AnnotatedImageSuffix = "_annotated.png";

    private readonly IInspectionRepository _repository;
    private readonly ILogger<InspectionDeletionService> _logger;
    private readonly IUserSettingsService? _settingsService;
    private readonly string _capturesRoot;
    private readonly string? _imageOutputRootOverride;

    /// <summary>
    /// Initializes a new instance of the <see cref="InspectionDeletionService"/> class.
    /// </summary>
    /// <param name="repository">Repository used for record lookup and transactional deletion.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <param name="capturesDirectoryPath">
    /// Optional legacy captures root override (used by tests). Defaults to the application's legacy capture directory.
    /// </param>
    /// <param name="imageOutputDirectory">
    /// Optional image output root override (used by tests). Defaults to the configured image output directory.
    /// </param>
    /// <param name="settingsService">Optional settings service supplying the image output directory.</param>
    public InspectionDeletionService(
        IInspectionRepository repository,
        ILogger<InspectionDeletionService>? logger = null,
        string? capturesDirectoryPath = null,
        string? imageOutputDirectory = null,
        IUserSettingsService? settingsService = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? NullLogger<InspectionDeletionService>.Instance;
        _settingsService = settingsService;
        _imageOutputRootOverride = string.IsNullOrWhiteSpace(imageOutputDirectory)
            ? null
            : NormalizeDirectory(imageOutputDirectory);
        _capturesRoot = NormalizeDirectory(
            string.IsNullOrWhiteSpace(capturesDirectoryPath)
                ? GetLegacyCapturesDirectoryPath()
                : capturesDirectoryPath);
    }

    /// <inheritdoc />
    public Task<InspectionDeletionResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return DeleteManyAsync(new[] { id }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<InspectionDeletionResult> DeleteManyAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        cancellationToken.ThrowIfCancellationRequested();

        var distinctIds = ids.Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return new InspectionDeletionResult(0, 0, Array.Empty<string>());
        }

        var summaries = await _repository
            .GetSummariesByIdsAsync(distinctIds, cancellationToken)
            .ConfigureAwait(false);

        int deletedRecords = await _repository
            .DeleteManyAsync(distinctIds, cancellationToken)
            .ConfigureAwait(false);

        int deletedFiles = 0;
        var failedFiles = new List<string>();
        string imageOutputRoot = await ResolveImageOutputRootAsync(cancellationToken).ConfigureAwait(false);

        foreach (var summary in summaries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (summary.Origin == InspectionOrigin.Crop)
            {
                if (!IsWithinRoot(summary.ImagePath, imageOutputRoot))
                {
                    _logger.LogWarning(
                        "Skipped deleting cropped image outside the image output root for inspection {InspectionId}: {ImagePath}",
                        summary.Id,
                        summary.ImagePath);
                    continue;
                }

                if (TryDeleteFile(summary.ImagePath, failedFiles))
                {
                    deletedFiles++;
                }

                continue;
            }

            bool isLiveCapture =
                summary.Origin == InspectionOrigin.LiveCapture ||
                summary.Mode == DetectionMode.LiveCamera;

            if (!isLiveCapture)
            {
                continue;
            }

            bool inLegacyCaptures = IsWithinRoot(summary.ImagePath, _capturesRoot);
            bool inImageOutput = IsWithinRoot(summary.ImagePath, imageOutputRoot);

            if (!inLegacyCaptures && !inImageOutput)
            {
                _logger.LogWarning(
                    "Skipped deleting capture file outside the owned roots for inspection {InspectionId}: {ImagePath}",
                    summary.Id,
                    summary.ImagePath);
                continue;
            }

            if (TryDeleteFile(summary.ImagePath, failedFiles))
            {
                deletedFiles++;
            }

            string? annotatedPath = GetAnnotatedSiblingPath(summary.ImagePath);
            if (annotatedPath != null &&
                (IsWithinRoot(annotatedPath, _capturesRoot) || IsWithinRoot(annotatedPath, imageOutputRoot)) &&
                TryDeleteFile(annotatedPath, failedFiles))
            {
                deletedFiles++;
            }
        }

        _logger.LogInformation(
            "Deleted {RecordCount} inspection record(s) and {FileCount} capture file(s); {FailureCount} file failure(s).",
            deletedRecords,
            deletedFiles,
            failedFiles.Count);

        return new InspectionDeletionResult(deletedRecords, deletedFiles, failedFiles.ToArray());
    }

    private bool TryDeleteFile(string path, List<string> failedFiles)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            _logger.LogWarning(ex, "Failed to delete capture file '{FilePath}'.", path);
            failedFiles.Add(path);
            return false;
        }
    }

    private bool IsWithinRoot(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            _logger.LogWarning(ex, "Rejected invalid image path '{FilePath}'.", path);
            return false;
        }

        return fullPath.Length > root.Length &&
               fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetAnnotatedSiblingPath(string rawImagePath)
    {
        return rawImagePath.EndsWith(RawImageSuffix, StringComparison.OrdinalIgnoreCase)
            ? string.Concat(rawImagePath.AsSpan(0, rawImagePath.Length - RawImageSuffix.Length), AnnotatedImageSuffix)
            : null;
    }

    private async Task<string> ResolveImageOutputRootAsync(CancellationToken cancellationToken)
    {
        if (_imageOutputRootOverride is not null)
        {
            return _imageOutputRootOverride;
        }

        if (_settingsService is not null)
        {
            try
            {
                var settings = await _settingsService.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(settings.ImageOutputDirectory))
                {
                    return NormalizeDirectory(settings.ImageOutputDirectory);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read the configured image output directory; using the default.");
            }
        }

        return NormalizeDirectory(KnownFolderPaths.GetCropImagesDirectory());
    }

    private static string GetLegacyCapturesDirectoryPath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "PcbDeftek", "pcb-deftek", "Captures");
    }

    private static string NormalizeDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string separator = Path.DirectorySeparatorChar.ToString();
        return fullPath.EndsWith(separator, StringComparison.Ordinal)
            ? fullPath
            : fullPath + separator;
    }
}
