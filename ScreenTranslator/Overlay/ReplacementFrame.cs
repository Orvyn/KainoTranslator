using System;
using System.Collections.Generic;
using System.Drawing;

namespace ScreenTranslator.Overlay;

public enum BlockAlignment
{
    Left,
    Center
}

/// <summary>One block of source text found on screen plus its translation. <see cref="Bounds"/> is in
/// the pixels of the owning frame's image. <see cref="Speaker"/> is the translated name of whoever is
/// talking, when the block starts with one - kept apart from <see cref="Text"/> so it can be styled.</summary>
public sealed record ReplacementBlock(Rectangle Bounds, string? Speaker, string Text, double LineHeight, int LineCount, BlockAlignment Alignment);

/// <summary>
/// Everything the replace-text overlay needs: a screenshot taken while the source text was on
/// screen (so the text can be blurred out of it), where that screenshot sits on the screen, and the
/// translated blocks to draw over it. Owns its bitmap - dispose it once it has been rendered.
/// </summary>
public sealed class ReplacementFrame : IDisposable
{
    public ReplacementFrame(Bitmap image, Point origin, IReadOnlyList<ReplacementBlock> blocks)
    {
        Image = image;
        Origin = origin;
        Blocks = blocks;
    }

    public Bitmap Image { get; }

    /// <summary>Screen position (physical pixels) of the image's top-left corner.</summary>
    public Point Origin { get; }

    public IReadOnlyList<ReplacementBlock> Blocks { get; }

    public void Dispose() => Image.Dispose();
}
