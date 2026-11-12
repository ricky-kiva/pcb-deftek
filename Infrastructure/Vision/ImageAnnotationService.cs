using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Infrastructure.Vision;

/// <summary>
/// OpenCV-powered service that draws defect bounding boxes, class abbreviations, and confidence percentages
/// onto PCB board images using defect-specific colors and produces frozen WPF <see cref="BitmapSource"/> instances.
/// </summary>
public class ImageAnnotationService : IImageAnnotationService
{
    /// <inheritdoc />
    public BitmapSource? AnnotateImage(string imagePath, IReadOnlyList<DetectedDefect> defects, int maxDimension = 0)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return null;
        }

        try
        {
            using var mat = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (mat.Empty())
            {
                return null;
            }

            AnnotateMatInternal(mat, defects, maxDimension);
            return MatToBitmapSource(mat);
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public BitmapSource? AnnotateMat(Mat sourceMat, IReadOnlyList<DetectedDefect> defects, int maxDimension = 0)
    {
        if (sourceMat == null || sourceMat.Empty())
        {
            return null;
        }

        try
        {
            using var mat = sourceMat.Clone();
            AnnotateMatInternal(mat, defects, maxDimension);
            return MatToBitmapSource(mat);
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public BitmapSource? MatToBitmapSource(Mat sourceMat)
    {
        if (sourceMat == null || sourceMat.IsDisposed || sourceMat.Empty())
        {
            return null;
        }

        try
        {
            PixelFormat pixelFormat = sourceMat.Channels() switch
            {
                1 => PixelFormats.Gray8,
                4 => PixelFormats.Bgra32,
                _ => PixelFormats.Bgr24
            };

            int stride = (int)sourceMat.Step();
            int bufferSize = stride * sourceMat.Height;

            var bitmap = BitmapSource.Create(
                sourceMat.Width,
                sourceMat.Height,
                96.0,
                96.0,
                pixelFormat,
                null,
                sourceMat.Data,
                bufferSize,
                stride);

            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static void AnnotateMatInternal(Mat mat, IReadOnlyList<DetectedDefect> defects, int maxDimension)
    {
        int w = mat.Width;
        int h = mat.Height;

        if (defects != null && defects.Count > 0)
        {
            int minDim = Math.Min(w, h);
            int thickness = Math.Clamp((int)Math.Round(minDim / 200.0), 1, 4);
            double fontScale = Math.Clamp(minDim / 600.0, 0.35, 1.2);

            foreach (var defect in defects)
            {
                int x1 = Math.Clamp((int)Math.Round(defect.BoundingBox.X * w), 0, w - 1);
                int y1 = Math.Clamp((int)Math.Round(defect.BoundingBox.Y * h), 0, h - 1);
                int bw = Math.Clamp((int)Math.Round(defect.BoundingBox.Width * w), 1, w - x1);
                int bh = Math.Clamp((int)Math.Round(defect.BoundingBox.Height * h), 1, h - y1);
                int x2 = Math.Min(w - 1, x1 + bw);
                int y2 = Math.Min(h - 1, y1 + bh);

                var boxColor = GetDefectColor(defect.Class);
                var labelTextColor = GetContrastingTextColor(defect.Class);

                // Draw bounding box
                Cv2.Rectangle(mat, new Point(x1, y1), new Point(x2, y2), boxColor, thickness);

                // Compute label text
                string label = $"{defect.ClassInfo.Abbreviation} {defect.Confidence:P0}";
                var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, fontScale, 1, out int baseline);

                int labelY1 = Math.Max(0, y1 - textSize.Height - 4);
                int labelY2 = y1;
                int labelX2 = Math.Min(w - 1, x1 + textSize.Width + 6);

                // Draw label badge background
                Cv2.Rectangle(mat, new Point(x1, labelY1), new Point(labelX2, labelY2), boxColor, -1);

                // Draw label text
                Cv2.PutText(mat, label, new Point(x1 + 3, labelY2 - 3), HersheyFonts.HersheySimplex, fontScale, labelTextColor, 1, LineTypes.AntiAlias);
            }
        }

        // Downscale if maximum dimension is specified (for thumbnails)
        if (maxDimension > 0 && Math.Max(w, h) > maxDimension)
        {
            double scale = (double)maxDimension / Math.Max(w, h);
            int targetW = Math.Max(1, (int)Math.Round(w * scale));
            int targetH = Math.Max(1, (int)Math.Round(h * scale));
            Cv2.Resize(mat, mat, new Size(targetW, targetH), interpolation: InterpolationFlags.Area);
        }
    }

    /// <summary>
    /// Gets the distinct BGR display color for the specified defect class.
    /// </summary>
    public static Scalar GetDefectColor(DefectClass defectClass) => defectClass switch
    {
        DefectClass.Short => new Scalar(255, 102, 0),                      // Blue (SH)
        DefectClass.Spur => new Scalar(220, 220, 0),                       // Cyan (SP)
        DefectClass.SpuriousCopper => new Scalar(255, 255, 255),           // White (SC)
        DefectClass.Open => new Scalar(80, 160, 79),                       // Fern Green (OP)
        DefectClass.MouseBite => new Scalar(140, 48, 0),                   // Dark Blue / Navy (MB)
        DefectClass.HoleBreakout => new Scalar(180, 105, 255),             // Pink (HB)
        DefectClass.ConductorScratch => new Scalar(35, 17, 232),            // Red (CS)
        DefectClass.ConductorForeignObject => new Scalar(0, 215, 255),      // Yellow (CFO)
        DefectClass.BaseMaterialForeignObject => new Scalar(113, 204, 46),  // Green (BMFO)
        _ => new Scalar(28, 43, 196)                                       // Default Crimson Red
    };

    /// <summary>
    /// Gets the contrasting label text color (dark text for bright backgrounds, white text for dark/vivid backgrounds).
    /// </summary>
    public static Scalar GetContrastingTextColor(DefectClass defectClass) => defectClass switch
    {
        DefectClass.SpuriousCopper or DefectClass.Spur or DefectClass.ConductorForeignObject => new Scalar(20, 20, 20),
        _ => new Scalar(255, 255, 255)
    };
}
