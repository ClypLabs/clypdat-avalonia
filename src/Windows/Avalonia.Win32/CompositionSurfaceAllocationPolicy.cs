using System;

namespace Avalonia.Win32;

/// <summary>
/// Chooses a reusable composition surface capacity for a window render target.
/// </summary>
internal static class CompositionSurfaceAllocationPolicy
{
    private const int Alignment = 256;

    public static PixelSize GetCapacity(PixelSize requestedSize, PixelSize? currentCapacity)
    {
        if (currentCapacity is { } capacity && Fits(requestedSize, capacity))
            return capacity;

        return new PixelSize(Expand(requestedSize.Width), Expand(requestedSize.Height));
    }

    public static bool Fits(PixelSize requestedSize, PixelSize capacity) =>
        requestedSize.Width <= capacity.Width && requestedSize.Height <= capacity.Height;

    private static int Expand(int value)
    {
        var withHeadroom = Math.Max(1L, ((long)value * 5 + 3) / 4);
        var aligned = ((withHeadroom + Alignment - 1) / Alignment) * Alignment;
        return (int)Math.Min(aligned, int.MaxValue);
    }
}
