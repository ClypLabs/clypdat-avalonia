using Avalonia.Win32;
using Avalonia.Win32.DirectX;
using Xunit;

namespace Avalonia.IntegrationTests.Win32;

public class CompositionSurfaceAllocationPolicyTests
{
    [Fact]
    public void Incremental_sizes_reuse_headroom()
    {
        PixelSize? capacity = null;

        for (var size = 800; size < 1000; size++)
        {
            var next = CompositionSurfaceAllocationPolicy.GetCapacity(new PixelSize(size, 600), capacity);
            if (capacity is null)
                capacity = next;
            else
                Assert.Equal(capacity, next);
        }
    }

    [Fact]
    public void Shrinking_keeps_existing_capacity()
    {
        var capacity = CompositionSurfaceAllocationPolicy.GetCapacity(new PixelSize(1920, 1080), null);

        Assert.Equal(capacity, CompositionSurfaceAllocationPolicy.GetCapacity(new PixelSize(1600, 900), capacity));
        Assert.Equal(capacity, CompositionSurfaceAllocationPolicy.GetCapacity(new PixelSize(800, 1080), capacity));
    }

    [Fact]
    public void Shrinking_below_half_in_both_dimensions_releases_capacity()
    {
        var capacity = CompositionSurfaceAllocationPolicy.GetCapacity(new PixelSize(3840, 2160), null);
        var shrunk = CompositionSurfaceAllocationPolicy.GetCapacity(new PixelSize(800, 600), capacity);

        Assert.True(shrunk.Width < capacity.Width);
        Assert.True(shrunk.Height < capacity.Height);
        Assert.True(CompositionSurfaceAllocationPolicy.Fits(new PixelSize(800, 600), shrunk));
    }

    [Fact]
    public void Empty_size_keeps_existing_capacity()
    {
        var capacity = CompositionSurfaceAllocationPolicy.GetCapacity(new PixelSize(1920, 1080), null);

        Assert.Equal(capacity, CompositionSurfaceAllocationPolicy.GetCapacity(new PixelSize(0, 0), capacity));
    }

    [Fact]
    public void Headroom_is_clamped_to_max_texture_dimension()
    {
        var capacity = CompositionSurfaceAllocationPolicy.GetCapacity(new PixelSize(15360, 2160), null);

        Assert.Equal(CompositionSurfaceAllocationPolicy.DefaultMaxTextureDimension, capacity.Width);
        Assert.True(capacity.Height > 2160);
    }

    [Fact]
    public void Headroom_respects_smaller_device_limit()
    {
        var capacity = CompositionSurfaceAllocationPolicy.GetCapacity(new PixelSize(7000, 600), null, 8192);

        Assert.Equal(8192, capacity.Width);
    }

    [Fact]
    public void Size_over_max_texture_dimension_is_requested_exactly()
    {
        var capacity = CompositionSurfaceAllocationPolicy.GetCapacity(new PixelSize(20000, 600), null);

        Assert.Equal(20000, capacity.Width);
    }

    [Fact]
    public void Max_texture_dimension_follows_feature_level()
    {
        Assert.Equal(16384, CompositionSurfaceAllocationPolicy.GetMaxTextureDimension(D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_1));
        Assert.Equal(16384, CompositionSurfaceAllocationPolicy.GetMaxTextureDimension(D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0));
        Assert.Equal(8192, CompositionSurfaceAllocationPolicy.GetMaxTextureDimension(D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_1));
        Assert.Equal(4096, CompositionSurfaceAllocationPolicy.GetMaxTextureDimension(D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_3));
        Assert.Equal(2048, CompositionSurfaceAllocationPolicy.GetMaxTextureDimension(D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_1));
    }

    [Fact]
    public void Overflow_grows_capacity_once()
    {
        var capacity = CompositionSurfaceAllocationPolicy.GetCapacity(new PixelSize(800, 600), null);
        var grown = CompositionSurfaceAllocationPolicy.GetCapacity(new PixelSize(capacity.Width + 1, 600), capacity);

        Assert.True(grown.Width > capacity.Width);
        Assert.Equal(grown, CompositionSurfaceAllocationPolicy.GetCapacity(new PixelSize(capacity.Width + 1, 600), grown));
    }

    [Fact]
    public void Transparency_replacement_can_keep_capacity()
    {
        var capacity = CompositionSurfaceAllocationPolicy.GetCapacity(new PixelSize(1280, 720), null);

        Assert.Equal(capacity, CompositionSurfaceAllocationPolicy.GetCapacity(new PixelSize(1024, 576), capacity));
    }
}
