using Avalonia.Win32;
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

        Assert.Equal(capacity, CompositionSurfaceAllocationPolicy.GetCapacity(new PixelSize(800, 600), capacity));
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
