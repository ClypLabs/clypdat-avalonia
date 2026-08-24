using System;
using Avalonia.Controls.Media;
using Xunit;

namespace Avalonia.Controls.Media.UnitTests;

public class MediaClockTests
{
    [Fact]
    public void Play_Pause_And_Seek_Should_Use_The_Injected_TimeProvider()
    {
        var time = new TestTimeProvider();
        using var target = new MediaClock(time);

        target.Play();
        time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(TimeSpan.FromSeconds(3), target.Position);

        target.Pause();
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(3), target.Position);

        target.Seek(TimeSpan.FromMilliseconds(750));
        target.Play();
        time.Advance(TimeSpan.FromMilliseconds(250));
        Assert.Equal(TimeSpan.FromSeconds(1), target.Position);
    }

    [Fact]
    public void Stop_Should_Reset_Position_And_Cancel_Previous_Generation()
    {
        var time = new TestTimeProvider();
        using var target = new MediaClock(time);
        target.Play();
        var generation = target.CurrentGeneration;

        target.Stop();

        Assert.Equal(MediaPlaybackState.Stopped, target.State);
        Assert.Equal(TimeSpan.Zero, target.Position);
        Assert.True(generation.CancellationToken.IsCancellationRequested);
        Assert.False(target.IsCurrent(generation));
    }

    [Fact]
    public void AdvanceGeneration_Should_Cancel_Only_Stale_Work()
    {
        using var target = new MediaClock(new TestTimeProvider());
        var first = target.CurrentGeneration;

        var second = target.AdvanceGeneration();

        Assert.True(first.CancellationToken.IsCancellationRequested);
        Assert.False(second.CancellationToken.IsCancellationRequested);
        Assert.False(target.IsCurrent(first));
        Assert.True(target.IsCurrent(second));
    }

    [Fact]
    public void Position_Should_Not_Move_Backward_When_The_Provider_Does()
    {
        var time = new TestTimeProvider();
        using var target = new MediaClock(time);
        target.Play();
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(10), target.Position);

        time.Rewind(TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(10), target.Position);
    }

    [Fact]
    public void Long_Playback_Should_Not_Accumulate_Drift()
    {
        var time = new TestTimeProvider();
        using var target = new MediaClock(time);
        target.Play();

        time.Advance(TimeSpan.FromMinutes(30));

        Assert.Equal(TimeSpan.FromMinutes(30), target.Position);
    }

    [Fact]
    public void Seek_Should_Clamp_Negative_Positions_And_Raise_A_Transition()
    {
        using var target = new MediaClock(new TestTimeProvider());
        MediaClockChangedEventArgs? changed = null;
        target.Changed += (_, args) => changed = args;

        target.Seek(TimeSpan.FromMilliseconds(-1));
        Assert.Null(changed);

        target.Seek(TimeSpan.FromSeconds(1));

        Assert.NotNull(changed);
        Assert.Equal(TimeSpan.Zero, changed!.Previous.Position);
        Assert.Equal(TimeSpan.FromSeconds(1), changed.Current.Position);
        Assert.Equal(changed.Current, target.Snapshot);
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan elapsed) => _timestamp += elapsed.Ticks;

        public void Rewind(TimeSpan elapsed) => _timestamp -= elapsed.Ticks;
    }
}
