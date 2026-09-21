using System;

namespace System.Windows.Media.ProGPU.Platform;

public sealed class DispatcherWpfRenderScheduler : IWpfDelayedRenderScheduler, IDisposable
{
    public static readonly TimeSpan DefaultRenderInterval = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan MinimumRenderDelay = TimeSpan.FromMilliseconds(1);

    private readonly object _gate = new();
    private readonly IWpfDispatcherService _dispatcher;
    private readonly IWpfTimerService _timers;
    private IWpfTimer? _renderTimer;
    private IWpfDispatcherOperation? _renderOperation;
    private DateTime _scheduledRenderDueUtc;
    private bool _renderTimerIsFollowUp;
    private bool _hasPendingRenderRequest;
    private bool _isRaisingRenderRequested;
    private bool _isDisposed;

    public DispatcherWpfRenderScheduler(
        IWpfDispatcherService dispatcher,
        IWpfTimerService timers,
        TimeSpan? renderInterval = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(timers);

        RenderInterval = renderInterval ?? DefaultRenderInterval;
        if (RenderInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(renderInterval), renderInterval, "Render interval must be greater than zero.");
        }

        _dispatcher = dispatcher;
        _timers = timers;
    }

    public event EventHandler? RenderRequested;

    public TimeSpan RenderInterval { get; }

    public bool HasPendingRenderRequest
    {
        get
        {
            lock (_gate)
            {
                return _hasPendingRenderRequest;
            }
        }
    }

    public void RequestRender()
    {
        RequestRender(RenderInterval);
    }

    public void RequestRender(TimeSpan delay)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            TimeSpan normalizedDelay = NormalizeDelay(delay);
            DateTime dueUtc = DateTime.UtcNow + normalizedDelay;

            if (_hasPendingRenderRequest)
            {
                if (_renderOperation != null)
                {
                    return;
                }

                bool scheduleFollowUp = _isRaisingRenderRequested || _renderTimerIsFollowUp;
                if (!scheduleFollowUp && _renderTimer == null)
                {
                    return;
                }

                if (_renderTimer != null && dueUtc >= _scheduledRenderDueUtc)
                {
                    return;
                }

                ScheduleTimerLocked(normalizedDelay, dueUtc, scheduleFollowUp);
                return;
            }

            _hasPendingRenderRequest = true;
            ScheduleTimerLocked(normalizedDelay, dueUtc, isFollowUp: false);
        }
    }

    public bool ConsumeRenderRequest()
    {
        lock (_gate)
        {
            var hadPendingRequest = _hasPendingRenderRequest;
            if (_renderTimerIsFollowUp)
            {
                _hasPendingRenderRequest = false;
                _renderOperation?.Cancel();
                _renderOperation = null;
                return hadPendingRequest;
            }

            _hasPendingRenderRequest = false;
            CancelRenderDispatchLocked();
            return hadPendingRequest;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _hasPendingRenderRequest = false;
            CancelRenderDispatchLocked();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _hasPendingRenderRequest = false;
            CancelRenderDispatchLocked();
            _isDisposed = true;
        }
    }

    private void OnRenderTimerTick()
    {
        lock (_gate)
        {
            bool isFollowUp = _renderTimerIsFollowUp;
            _renderTimer?.Dispose();
            _renderTimer = null;
            _scheduledRenderDueUtc = default;
            _renderTimerIsFollowUp = false;

            if (isFollowUp)
            {
                _hasPendingRenderRequest = true;
            }

            if (_isDisposed || !_hasPendingRenderRequest || _renderOperation != null)
            {
                return;
            }

            _renderOperation = _dispatcher.Post(RaiseRenderRequested, WpfDispatcherPriority.Render);
        }
    }

    private void RaiseRenderRequested()
    {
        var shouldRaise = false;

        lock (_gate)
        {
            _renderOperation = null;
            shouldRaise = !_isDisposed && _hasPendingRenderRequest;
        }

        if (shouldRaise)
        {
            lock (_gate)
            {
                if (_isDisposed)
                {
                    return;
                }

                _isRaisingRenderRequested = true;
            }

            try
            {
                RenderRequested?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                lock (_gate)
                {
                    _isRaisingRenderRequested = false;
                }
            }
        }
    }

    private void CancelRenderDispatchLocked()
    {
        if (_renderTimer != null)
        {
            if (_renderTimer.IsEnabled)
            {
                _renderTimer.Stop();
            }

            _renderTimer.Dispose();
            _renderTimer = null;
        }

        _scheduledRenderDueUtc = default;
        _renderTimerIsFollowUp = false;
        _renderOperation?.Cancel();
        _renderOperation = null;
    }

    private void ScheduleTimerLocked(TimeSpan delay, DateTime dueUtc, bool isFollowUp)
    {
        if (_renderTimer != null)
        {
            if (_renderTimer.IsEnabled)
            {
                _renderTimer.Stop();
            }

            _renderTimer.Dispose();
        }

        _scheduledRenderDueUtc = dueUtc;
        _renderTimerIsFollowUp = isFollowUp;
        _renderTimer = _timers.CreateTimer(delay, OnRenderTimerTick, isRepeating: false);
        _renderTimer.Start();
    }

    private static TimeSpan NormalizeDelay(TimeSpan delay)
    {
        return delay <= TimeSpan.Zero ? MinimumRenderDelay : delay;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
