using System;
using System.Threading;

namespace System.Windows.Media.ProGPU.Platform;

public sealed class ThreadPoolWpfTimerService : IWpfTimerService
{
    public IWpfTimer CreateTimer(TimeSpan interval, Action callback, bool isRepeating = true)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return new ThreadPoolWpfTimer(interval, callback, isRepeating);
    }

    private sealed class ThreadPoolWpfTimer : IWpfTimer
    {
        private readonly object _gate = new();
        private readonly Action _callback;
        private readonly bool _isRepeating;
        private readonly Timer _timer;
        private bool _isDisposed;
        private bool _isEnabled;

        public ThreadPoolWpfTimer(TimeSpan interval, Action callback, bool isRepeating)
        {
            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(interval), interval, "Timer interval must be greater than zero.");
            }

            Interval = interval;
            _callback = callback;
            _isRepeating = isRepeating;
            _timer = new Timer(OnTick, state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public TimeSpan Interval { get; }

        public bool IsEnabled
        {
            get
            {
                lock (_gate)
                {
                    return _isEnabled;
                }
            }
        }

        public void Start()
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                _isEnabled = true;
                _timer.Change(Interval, _isRepeating ? Interval : Timeout.InfiniteTimeSpan);
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                StopCore();
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

                StopCore();
                _timer.Dispose();
                _isDisposed = true;
            }
        }

        private void OnTick(object? state)
        {
            lock (_gate)
            {
                if (_isDisposed || !_isEnabled)
                {
                    return;
                }

                if (!_isRepeating)
                {
                    StopCore();
                }
            }

            _callback();
        }

        private void StopCore()
        {
            _isEnabled = false;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
        }
    }
}
