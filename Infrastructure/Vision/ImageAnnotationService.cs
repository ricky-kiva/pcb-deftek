using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Infrastructure.Vision;

/// <summary>
/// OpenCV-powered service that draws defect bounding boxes, class abbreviations, and confidence percentages
/// onto PCB board images and produces frozen WPF <see cref="BitmapSource"/> instances.
/// </summary>
public class ImageAnnotationService : IImageAnnotationService
{
    private static readonly Scalar DefectBoxColor = new(28, 43, 196); // #C42B1C in BGR format
    private static readonly Scalar TextColor = new(255, 255, 255);    // White

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

                    // Draw bounding box
                    Cv2.Rectangle(mat, new Point(x1, y1), new Point(x2, y2), DefectBoxColor, thickness);

                    // Compute label text
                    string label = $"{defect.ClassInfo.Abbreviation} {defect.Confidence:P0}";
                    var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, fontScale, 1, out int baseline);

                    int labelY1 = Math.Max(0, y1 - textSize.Height - 4);
                    int labelY2 = y1;
                    int labelX2 = Math.Min(w - 1, x1 + textSize.Width + 6);

                    // Draw label badge background
                    Cv2.Rectangle(mat, new Point(x1, labelY1), new Point(labelX2, labelY2), DefectBoxColor, -1);

                    // Draw label text
                    Cv2.PutText(mat, label, new Point(x1 + 3, labelY2 - 3), HersheyFonts.HersheySimplex, fontScale, TextColor, 1, LineTypes.AntiAlias);
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

            byte[] pngBytes = mat.ToBytes(".png");
            var bitmap = new BitmapImage();
            using var stream = new MemoryStream(pngBytes);
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
