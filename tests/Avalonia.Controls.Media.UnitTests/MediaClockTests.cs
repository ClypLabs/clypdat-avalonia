using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

    [Fact]
    public void Changed_Should_Reach_Every_Subscriber_In_Transition_Order_When_A_Handler_Transitions()
    {
        using var target = new MediaClock(new TestTimeProvider());
        var observed = new List<long>();
        target.Changed += (_, args) =>
        {
            if (args.Current.State == MediaPlaybackState.Playing && args.Current.Position == TimeSpan.Zero)
                target.Seek(TimeSpan.FromSeconds(5));
        };
        target.Changed += (_, args) => observed.Add(args.Current.Generation);

        target.Play();

        Assert.Equal(new long[] { 1, 2 }, observed);
        Assert.Equal(TimeSpan.FromSeconds(5), target.Position);
    }

    [Fact]
    public void Throwing_Subscriber_Does_Not_Strand_A_Queued_Transition()
    {
        using var target = new MediaClock(new TestTimeProvider());
        var observed = new List<long>();
        target.Changed += (_, args) =>
        {
            if (args.Current.Generation == 1)
            {
                target.Seek(TimeSpan.FromSeconds(5));
                throw new InvalidOperationException("Subscriber failed");
            }
            observed.Add(args.Current.Generation);
        };

        Assert.Throws<InvalidOperationException>(target.Play);
        Assert.Equal(new long[] { 2 }, observed);
    }

    [Fact]
    public async Task Concurrent_Transitions_Are_Delivered_In_Generation_Order()
    {
        using var target = new MediaClock(new TestTimeProvider());
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var observed = new List<long>();
        target.Changed += (_, args) =>
        {
            if (args.Current.Generation == 1)
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            }
            observed.Add(args.Current.Generation);
        };
        var play = Task.Run(target.Play, TestContext.Current.CancellationToken);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            target.Seek(TimeSpan.FromSeconds(5));
        }
        finally
        {
            release.Set();
        }
        await play;
        Assert.Equal(new long[] { 1, 2 }, observed);
    }

    [Fact]
    public void FromMilliseconds_Should_Saturate_Instead_Of_Throwing()
    {
        Assert.Equal(TimeSpan.Zero, MediaTiming.FromMilliseconds(-5));
        Assert.Equal(TimeSpan.FromSeconds(1), MediaTiming.FromMilliseconds(1000));
        Assert.Equal(TimeSpan.MaxValue, MediaTiming.FromMilliseconds(long.MaxValue));
        Assert.Equal(TimeSpan.MaxValue, MediaTiming.FromMilliseconds(long.MaxValue / TimeSpan.TicksPerMillisecond + 1));
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
