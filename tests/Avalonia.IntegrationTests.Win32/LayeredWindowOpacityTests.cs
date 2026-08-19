using System;
using Avalonia.Controls;
using Avalonia.Media;
using Xunit;
using static Avalonia.IntegrationTests.Win32.UnmanagedMethods;

namespace Avalonia.IntegrationTests.Win32;

public sealed class LayeredWindowOpacityTests : IDisposable
{
    private readonly Window _window = new()
    {
        Width = 200,
        Height = 200,
        Background = Brushes.Red,
        WindowStartupLocation = WindowStartupLocation.CenterScreen,
    };

    public LayeredWindowOpacityTests()
    {
        _window.Show();
    }

    [Fact]
    public void AppliesNativeAlphaAndSupportsLiveUpdates()
    {
        Assert.False(IsLayered(_window));

        Win32Properties.SetLayeredWindowOpacity(_window, 0.5);
        Assert.True(IsLayered(_window));
        Assert.Equal(128, GetAlpha(_window));

        Win32Properties.SetLayeredWindowOpacity(_window, 0.25);
        Assert.Equal(64, GetAlpha(_window));
    }

    [Fact]
    public void DisablingResetsAndRemovesAvaloniaOwnedStyle()
    {
        Win32Properties.SetLayeredWindowOpacity(_window, 0.5);

        Win32Properties.SetLayeredWindowOpacity(_window, null);

        Assert.Null(Win32Properties.GetLayeredWindowOpacity(_window));
        Assert.False(IsLayered(_window));
    }

    [Fact]
    public void DisablingKeepsPreexistingLayeredStyleAndResetsAlpha()
    {
        var externallyLayeredWindow = new Window
        {
            Width = 200,
            Height = 200,
            Background = Brushes.Red,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };

        try
        {
            Win32Properties.AddWindowStylesCallback(externallyLayeredWindow,
                (style, exStyle) => (style, exStyle | (uint)WS_EX_LAYERED));
            externallyLayeredWindow.Show();

            Assert.True(IsLayered(externallyLayeredWindow));

            Win32Properties.SetLayeredWindowOpacity(externallyLayeredWindow, 0.5);
            Win32Properties.SetLayeredWindowOpacity(externallyLayeredWindow, null);

            Assert.True(IsLayered(externallyLayeredWindow));
            Assert.Equal(255, GetAlpha(externallyLayeredWindow));
        }
        finally
        {
            externallyLayeredWindow.Close();
        }
    }

    [Fact]
    public void RejectsInvalidValues()
    {
        foreach (var value in new[] { -0.1, 1.1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Assert.Throws<ArgumentException>(() => Win32Properties.SetLayeredWindowOpacity(_window, value));
    }

    public void Dispose()
        => _window.Close();

    private static bool IsLayered(Window window)
        => (GetWindowLongPtr(window.TryGetPlatformHandle()!.Handle, GWL_EXSTYLE).ToInt64() & WS_EX_LAYERED) != 0;

    private static byte GetAlpha(Window window)
    {
        Assert.True(GetLayeredWindowAttributes(
            window.TryGetPlatformHandle()!.Handle,
            out _,
            out var alpha,
            out var flags));
        Assert.Equal(LWA_ALPHA, flags);
        return alpha;
    }
}
