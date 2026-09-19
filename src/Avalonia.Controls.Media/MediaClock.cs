using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Avalonia.Controls.Media;

/// <summary>
/// Describes the transport state of a <see cref="MediaClock"/>.
/// </summary>
public enum MediaPlaybackState
{
    /// <summary>The transport is reset to the beginning.</summary>
    Stopped,

    /// <summary>The transport has a position but is not advancing.</summary>
    Paused,

    /// <summary>The transport is advancing.</summary>
    Playing
}

/// <summary>
/// An immutable observation of a media transport.
/// </summary>
public readonly record struct MediaClockSnapshot(MediaPlaybackState State, TimeSpan Position, long Generation);

/// <summary>
/// Identifies cancellable work started for a particular media transport generation.
/// </summary>
public readonly record struct MediaOperationGeneration(long Value, CancellationToken CancellationToken);

/// <summary>
/// Contains the state before and after a media clock transition.
/// </summary>
public sealed class MediaClockChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MediaClockChangedEventArgs"/> class.
    /// </summary>
    public MediaClockChangedEventArgs(MediaClockSnapshot previous, MediaClockSnapshot current)
    {
        Previous = previous;
        Current = current;
    }

    /// <summary>Gets the clock state before the transition.</summary>
    public MediaClockSnapshot Previous { get; }

    /// <summary>Gets the clock state after the transition.</summary>
    public MediaClockSnapshot Current { get; }
}

/// <summary>
/// A monotonic, seekable playback clock independent of a decoder or audio output.
/// </summary>
/// <remarks>
/// The clock uses <see cref="TimeProvider"/> rather than wall-clock time, making
/// playback scheduling deterministic in tests. Each observable transport transition
/// creates a new operation generation and cancels work associated with the previous one.
/// <see cref="Changed"/> is raised outside the clock's lock, one event at a time and in
/// transition order. A transition made from another thread while an event is being raised
/// is delivered by the thread that is already raising events, after the current handler returns.
/// </remarks>
public sealed class MediaClock : IDisposable
{
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private CancellationTokenSource _generationCancellation = new();
    private MediaPlaybackState _state;
    private TimeSpan _position;
    private TimeSpan _lastObservedPosition;
    private long _anchorTimestamp;
    private long _generation;
    private bool _disposed;
    private readonly Queue<MediaClockChangedEventArgs> _pendingChanges = new();
    private bool _raisingChanges;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaClock"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider used to advance the clock.</param>
    public MediaClock(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _anchorTimestamp = _timeProvider.GetTimestamp();
    }

    /// <summary>Raised after an observable transport transition.</summary>
    public event EventHandler<MediaClockChangedEventArgs>? Changed;

    /// <summary>Gets the provider used by this clock.</summary>
    public TimeProvider TimeProvider => _timeProvider;

    /// <summary>Gets the current transport state.</summary>
    public MediaPlaybackState State
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                return _state;
            }
        }
    }

    /// <summary>Gets the current position.</summary>
    public TimeSpan Position
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                return GetPosition(_timeProvider.GetTimestamp());
            }
        }
    }

    /// <summary>Gets an atomic view of the state, position and generation.</summary>
    public MediaClockSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                return GetSnapshot(_timeProvider.GetTimestamp());
            }
        }
    }

    /// <summary>Gets the current cancellable operation generation.</summary>
    public MediaOperationGeneration CurrentGeneration
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                return new MediaOperationGeneration(_generation, _generationCancellation.Token);
            }
        }
    }

    /// <summary>Starts the clock from its current position.</summary>
    public void Play() => Transition(MediaPlaybackState.Playing, position: null);

    /// <summary>Pauses the clock at its current position.</summary>
    public void Pause() => Transition(MediaPlaybackState.Paused, position: null);

    /// <summary>Stops the clock and resets its position to zero.</summary>
    public void Stop() => Transition(MediaPlaybackState.Stopped, TimeSpan.Zero);

    /// <summary>Moves the clock to a non-negative position without changing its transport state.</summary>
    /// <param name="position">The requested position.</param>
    public void Seek(TimeSpan position) => Transition(state: null, MediaTiming.ClampToZero(position));

    /// <summary>
    /// Cancels all work associated with the current generation without changing transport state.
    /// </summary>
    /// <returns>The newly current generation.</returns>
    public MediaOperationGeneration AdvanceGeneration()
    {
        CancellationTokenSource cancellation;
        MediaOperationGeneration generation;
        lock (_gate)
        {
            ThrowIfDisposed();
            cancellation = _generationCancellation;
            generation = AdvanceGenerationCore();
        }

        cancellation.Cancel();
        cancellation.Dispose();
        return generation;
    }

    /// <summary>Returns whether a generation still belongs to this clock.</summary>
    public bool IsCurrent(MediaOperationGeneration generation)
    {
        lock (_gate)
        {
            return !_disposed && generation.Value == _generation &&
                generation.CancellationToken == _generationCancellation.Token &&
                !generation.CancellationToken.IsCancellationRequested;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            cancellation = _generationCancellation;
        }

        cancellation.Cancel();
        cancellation.Dispose();
    }

    private void Transition(MediaPlaybackState? state, TimeSpan? position)
    {
        CancellationTokenSource? cancellation = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            var now = _timeProvider.GetTimestamp();
            var previous = GetSnapshot(now);
            var nextPosition = position ?? GetPosition(now);
            var nextState = state ?? _state;

            if (previous.State == nextState && previous.Position == nextPosition)
                return;

            _position = nextPosition;
            _lastObservedPosition = nextPosition;
            _state = nextState;
            _anchorTimestamp = now;
            cancellation = _generationCancellation;
            var generation = AdvanceGenerationCore();
            _pendingChanges.Enqueue(new MediaClockChangedEventArgs(previous,
                new MediaClockSnapshot(_state, _position, generation.Value)));
        }

        cancellation!.Cancel();
        cancellation.Dispose();
        RaisePendingChanges();
    }

    private void RaisePendingChanges()
    {
        lock (_gate)
        {
            // Another call is already delivering events; it will pick up the one just queued.
            if (_raisingChanges)
                return;
            _raisingChanges = true;
        }

        ExceptionDispatchInfo? failure = null;
        while (true)
        {
            MediaClockChangedEventArgs? changed;
            lock (_gate)
            {
                if (!_pendingChanges.TryDequeue(out changed))
                {
                    _raisingChanges = false;
                    break;
                }
            }

            try
            {
                Changed?.Invoke(this, changed);
            }
            catch (Exception e)
            {
                // A failing subscriber must not strand transitions queued by
                // another thread or a reentrant handler.
                failure ??= ExceptionDispatchInfo.Capture(e);
            }
        }
        failure?.Throw();
    }

    private MediaOperationGeneration AdvanceGenerationCore()
    {
        _generationCancellation = new CancellationTokenSource();
        _generation++;
        return new MediaOperationGeneration(_generation, _generationCancellation.Token);
    }

    private MediaClockSnapshot GetSnapshot(long timestamp) => new(_state, GetPosition(timestamp), _generation);

    private TimeSpan GetPosition(long timestamp)
    {
        var position = _state != MediaPlaybackState.Playing || timestamp <= _anchorTimestamp
            ? _position
            : MediaTiming.AddSaturating(_position, _timeProvider.GetElapsedTime(_anchorTimestamp, timestamp));

        if (position < _lastObservedPosition)
            return _lastObservedPosition;

        _lastObservedPosition = position;
        return position;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

/// <summary>
/// Provides common media timeline conversions that preserve integer tick precision.
/// </summary>
public static class MediaTiming
{
    /// <summary>Clamps a media position to zero or later.</summary>
    public static TimeSpan ClampToZero(TimeSpan position) => position < TimeSpan.Zero ? TimeSpan.Zero : position;

    /// <summary>Converts a timeline position to whole milliseconds without using floating point arithmetic.</summary>
    public static long ToMilliseconds(TimeSpan position) => ClampToZero(position).Ticks / TimeSpan.TicksPerMillisecond;

    private const long MaxMilliseconds = long.MaxValue / TimeSpan.TicksPerMillisecond;

    /// <summary>
    /// Creates a timeline position from non-negative whole milliseconds, saturating at
    /// <see cref="TimeSpan.MaxValue"/>.
    /// </summary>
    public static TimeSpan FromMilliseconds(long milliseconds) =>
        milliseconds <= 0 ? TimeSpan.Zero :
        milliseconds > MaxMilliseconds ? TimeSpan.MaxValue :
        TimeSpan.FromTicks(milliseconds * TimeSpan.TicksPerMillisecond);

    internal static TimeSpan AddSaturating(TimeSpan left, TimeSpan right)
    {
        if (right <= TimeSpan.Zero)
            return left;

        return left > TimeSpan.MaxValue - right ? TimeSpan.MaxValue : left + right;
    }
}
