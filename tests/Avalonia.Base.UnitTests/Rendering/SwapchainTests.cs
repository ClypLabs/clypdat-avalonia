using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Rendering;
using Xunit;

namespace Avalonia.Base.UnitTests.Rendering;

public class SwapchainTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_Or_Canceled_Presents_Retire_Their_Image(bool canceled)
    {
        var result = canceled ? Task.FromCanceled(new CancellationToken(true)) :
            Task.FromException(new InvalidOperationException("Present failed"));
        await using var swapchain = new TestSwapchain(result);
        using (swapchain.Draw()) { }
        var first = Assert.Single(swapchain.Images);

        using (swapchain.Draw()) { }

        Assert.True(first.Disposed);
        Assert.Equal(2, swapchain.Images.Count);
        Assert.False(swapchain.Images[1].Disposed);
    }

    private sealed class TestSwapchain(Task result) : SwapchainBase<TestImage>(null!, null!)
    {
        public List<TestImage> Images { get; } = new();
        public IDisposable Draw() => BeginDrawCore(new PixelSize(800, 600), out _);
        protected override TestImage CreateImage(PixelSize size)
        {
            var image = new TestImage(size, result);
            Images.Add(image);
            return image;
        }
    }

    private sealed class TestImage(PixelSize size, Task result) : ISwapchainImage
    {
        public PixelSize Size => size;
        public Task? LastPresent { get; private set; }
        public bool Disposed { get; private set; }
        public void BeginDraw() => Assert.False(Disposed);
        public void Present() => LastPresent = result;
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return default;
        }
    }
}
