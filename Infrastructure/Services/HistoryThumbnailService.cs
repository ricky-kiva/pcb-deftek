using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace pcb_deftek.Infrastructure.Services;

/// <summary>
/// WPF thumbnail decoder for history cards. Decodes at a bounded width with a concurrency cap,
/// then freezes the bitmap so it can be handed to the UI thread safely.
/// </summary>
public sealed class HistoryThumbnailService : IHistoryThumbnailService
{
    private readonly ILogger<HistoryThumbnailService> _logger;
    private readonly SemaphoreSlim _throttle;
    private readonly int _maxDimension;

    /// <summary>
    /// Initializes a new instance of the <see cref="HistoryThumbnailService"/> class.
    /// </summary>
    /// <param name="logger">Optional logger instance.</param>
    /// <param name="maxDimension">Maximum decoded width in pixels.</param>
    /// <param name="maxConcurrency">Maximum simultaneous decode operations.</param>
    public HistoryThumbnailService(
        ILogger<HistoryThumbnailService>? logger = null,
        int maxDimension = 240,
        int maxConcurrency = 4)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDimension);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);

        _logger = logger ?? NullLogger<HistoryThumbnailService>.Instance;
        _maxDimension = maxDimension;
        _throttle = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    /// <inheritdoc />
    public async Task<BitmapSource?> GetThumbnailAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        cancellationToken.ThrowIfCancellationRequested();

        await _throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            return await Task.Run(() => DecodeThumbnail(imagePath), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _throttle.Release();
        }
    }

    private BitmapSource? DecodeThumbnail(string imagePath)
    {
        try
        {
            if (!File.Exists(imagePath))
            {
                return null;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.DecodePixelWidth = _maxDimension;
            bitmap.UriSource = new Uri(Path.GetFullPath(imagePath), UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException
            or NotSupportedException
            or UnauthorizedAccessException
            or ArgumentException
            or System.Runtime.InteropServices.COMException)
        {
            _logger.LogWarning(ex, "Failed to decode history thumbnail for '{ImagePath}'.", imagePath);
            return null;
        }
    }
}
