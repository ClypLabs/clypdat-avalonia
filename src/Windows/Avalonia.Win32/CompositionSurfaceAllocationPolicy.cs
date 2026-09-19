using System;
using Avalonia.Win32.DirectX;
using MicroCom.Runtime;

namespace Avalonia.Win32;

/// <summary>
/// Chooses a reusable composition surface capacity for a window render target.
/// </summary>
internal static class CompositionSurfaceAllocationPolicy
{
    private const int Alignment = 256;

    /// <summary>
    /// D3D11_REQ_TEXTURE2D_U_OR_V_DIMENSION, the limit for feature level 11_0 and later.
    /// </summary>
    public const int DefaultMaxTextureDimension = 16384;

    public static PixelSize GetCapacity(PixelSize requestedSize, PixelSize? currentCapacity,
        int maxTextureDimension = DefaultMaxTextureDimension)
    {
        if (currentCapacity is { } capacity && Fits(requestedSize, capacity) && !IsOversized(requestedSize, capacity))
            return capacity;

        return new PixelSize(Expand(requestedSize.Width, maxTextureDimension),
            Expand(requestedSize.Height, maxTextureDimension));
    }

    public static bool Fits(PixelSize requestedSize, PixelSize capacity) =>
        requestedSize.Width <= capacity.Width && requestedSize.Height <= capacity.Height;

    /// <summary>
    /// Returns whether a surface is so much larger than the requested size that it should
    /// be replaced by a smaller one rather than reused. Empty sizes (e.g. a minimized window)
    /// keep the existing surface.
    /// </summary>
    public static bool IsOversized(PixelSize requestedSize, PixelSize capacity) =>
        requestedSize.Width > 0 && requestedSize.Height > 0 &&
        (long)requestedSize.Width * 2 <= capacity.Width && (long)requestedSize.Height * 2 <= capacity.Height;

    /// <summary>
    /// Returns the largest texture dimension the D3D11 device supports, or
    /// <see cref="DefaultMaxTextureDimension"/> if its feature level cannot be read.
    /// </summary>
    public static int GetMaxTextureDimension(IUnknown d3dDevice)
    {
        try
        {
            using var device = d3dDevice.QueryInterface<ID3D11Device>();
            return GetMaxTextureDimension(device.FeatureLevel);
        }
        catch
        {
            return DefaultMaxTextureDimension;
        }
    }

    public static int GetMaxTextureDimension(D3D_FEATURE_LEVEL featureLevel) => featureLevel switch
    {
        >= D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0 => DefaultMaxTextureDimension,
        >= D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_0 => 8192,
        >= D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_3 => 4096,
        _ => 2048
    };

    private static int Expand(int value, int maxTextureDimension)
    {
        // A size that is already over the limit is requested as-is, so the failure is
        // the same one an exact allocation would have produced.
        if (value >= maxTextureDimension)
            return value;

        var withHeadroom = Math.Max(1L, ((long)value * 5 + 3) / 4);
        var aligned = ((withHeadroom + Alignment - 1) / Alignment) * Alignment;
        return (int)Math.Min(aligned, maxTextureDimension);
    }
}
