using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace ScreenTranslator.Ocr;

/// <summary>
/// Given every recognized line in a screenshot, finds the block of text a point (the cursor) is
/// on: the nearest line, plus the neighbouring lines that visibly belong to the same block
/// (similar height, tightly stacked or side by side, horizontally overlapping). This is what
/// lets instant translate translate "the dialogue under the cursor" instead of a fixed box.
/// </summary>
public static class TextBlockLocator
{
    /// <summary>Returns the lines of the block in reading order, or an empty list if no text is
    /// close enough to <paramref name="point"/> (coordinates are in the lines' own pixel space).</summary>
    public static List<OcrTextLine> FindBlockAt(IReadOnlyList<OcrTextLine> lines, Point point)
    {
        var candidates = lines
            .Where(l => !string.IsNullOrWhiteSpace(l.Text) && l.Bounds.Width > 0 && l.Bounds.Height > 0)
            .ToList();
        if (candidates.Count == 0) return new List<OcrTextLine>();

        OcrTextLine? seed = null;
        var best = double.MaxValue;
        foreach (var line in candidates)
        {
            var d = DistanceToRect(line.Bounds, point);
            if (d < best) { best = d; seed = line; }
        }
        if (seed is null) return new List<OcrTextLine>();

        // The cursor doesn't have to sit exactly on a glyph (bubble padding, gaps between
        // lines), but text far from it isn't what was meant.
        var tolerance = Math.Max(60, seed.Bounds.Height * 2.5);
        if (best > tolerance) return new List<OcrTextLine>();

        var block = new List<OcrTextLine> { seed };
        var grew = true;
        while (grew)
        {
            grew = false;
            foreach (var line in candidates)
            {
                if (block.Contains(line)) continue;
                if (block.Any(b => AreSameBlock(b.Bounds, line.Bounds)))
                {
                    block.Add(line);
                    grew = true;
                }
            }
        }

        return InReadingOrder(block);
    }

    private static double DistanceToRect(Rectangle r, Point p)
    {
        double dx = Math.Max(Math.Max(r.Left - p.X, 0), p.X - r.Right);
        double dy = Math.Max(Math.Max(r.Top - p.Y, 0), p.Y - r.Bottom);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool AreSameBlock(Rectangle a, Rectangle b)
    {
        // A big title next to small body text isn't one block.
        var minH = Math.Min(a.Height, b.Height);
        var maxH = Math.Max(a.Height, b.Height);
        if ((double)maxH / minH > 1.7) return false;

        var avgH = (a.Height + b.Height) / 2.0;
        var verticalGap = Math.Max(a.Top, b.Top) - Math.Min(a.Bottom, b.Bottom); // negative = they overlap vertically

        // Same row: OCR sometimes splits one wide line into separate segments.
        if (-verticalGap >= minH * 0.5)
        {
            var horizontalGap = Math.Max(a.Left, b.Left) - Math.Min(a.Right, b.Right);
            return horizontalGap <= avgH * 2;
        }

        // Stacked lines: close together vertically and lined up horizontally.
        if (verticalGap > avgH * 0.8) return false;
        var overlap = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        return overlap >= Math.Min(a.Width, b.Width) * 0.3;
    }

    /// <summary>Top-to-bottom, and left-to-right within a row.</summary>
    private static List<OcrTextLine> InReadingOrder(List<OcrTextLine> block)
    {
        var rows = new List<List<OcrTextLine>>();
        foreach (var line in block.OrderBy(l => l.Bounds.Top + l.Bounds.Height / 2.0))
        {
            var centerY = line.Bounds.Top + line.Bounds.Height / 2.0;
            var row = rows.FirstOrDefault(r =>
            {
                var first = r[0].Bounds;
                return Math.Abs(centerY - (first.Top + first.Height / 2.0)) < Math.Min(first.Height, line.Bounds.Height) * 0.5;
            });
            if (row is null) rows.Add(new List<OcrTextLine> { line });
            else row.Add(line);
        }
        return rows.SelectMany(r => r.OrderBy(l => l.Bounds.Left)).ToList();
    }
}
