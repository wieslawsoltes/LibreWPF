// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;

namespace Microsoft.Win32;

public enum PowerModes
{
    Resume = 1,
    StatusChange = 2,
    Suspend = 3,
}

public enum SessionEndReasons
{
    Logoff = 1,
    SystemShutdown = 2,
}

public enum SessionSwitchReason
{
    ConsoleConnect = 1,
    ConsoleDisconnect = 2,
    RemoteConnect = 3,
    RemoteDisconnect = 4,
    SessionLogon = 5,
    SessionLogoff = 6,
    SessionLock = 7,
    SessionUnlock = 8,
    SessionRemoteControl = 9,
}

public enum UserPreferenceCategory
{
    Accessibility = 1,
    Color = 2,
    Desktop = 3,
    General = 4,
    Icon = 5,
    Keyboard = 6,
    Menu = 7,
    Mouse = 8,
    Policy = 9,
    Power = 10,
    Screensaver = 11,
    Window = 12,
    Locale = 13,
    VisualStyle = 14,
}

public class PowerModeChangedEventArgs(PowerModes mode) : EventArgs
{
    public PowerModes Mode { get; } = mode;
}

public delegate void PowerModeChangedEventHandler(object sender, PowerModeChangedEventArgs e);

public class SessionEndedEventArgs(SessionEndReasons reason) : EventArgs
{
    public SessionEndReasons Reason { get; } = reason;
}

public delegate void SessionEndedEventHandler(object sender, SessionEndedEventArgs e);

public class SessionEndingEventArgs(SessionEndReasons reason) : EventArgs
{
    public bool Cancel { get; set; }

    public SessionEndReasons Reason { get; } = reason;
}

public delegate void SessionEndingEventHandler(object sender, SessionEndingEventArgs e);

public class SessionSwitchEventArgs(SessionSwitchReason reason) : EventArgs
{
    public SessionSwitchReason Reason { get; } = reason;
}

public delegate void SessionSwitchEventHandler(object sender, SessionSwitchEventArgs e);

public class TimerElapsedEventArgs(nint timerId) : EventArgs
{
    public nint TimerId { get; } = timerId;
}

public delegate void TimerElapsedEventHandler(object sender, TimerElapsedEventArgs e);

public class UserPreferenceChangedEventArgs(UserPreferenceCategory category) : EventArgs
{
    public UserPreferenceCategory Category { get; } = category;
}

public delegate void UserPreferenceChangedEventHandler(object sender, UserPreferenceChangedEventArgs e);

public class UserPreferenceChangingEventArgs(UserPreferenceCategory category) : EventArgs
{
    public UserPreferenceCategory Category { get; } = category;
}

public delegate void UserPreferenceChangingEventHandler(object sender, UserPreferenceChangingEventArgs e);

/// <summary>
/// Provides the portable implementation of the standard system-event surface.
/// Event registration is always available; platform hosts can publish typed changes through
/// <see cref="PortableSystemEvents"/> without a hidden Win32 window or reflected callbacks.
/// </summary>
public sealed class SystemEvents
{
    private static readonly SystemEvents s_sender = new();
    private static readonly EventRegistry<EventHandler> s_displaySettingsChanged = new();
    private static readonly EventRegistry<EventHandler> s_displaySettingsChanging = new();
    private static readonly EventRegistry<EventHandler> s_eventsThreadShutdown = new();
    private static readonly EventRegistry<EventHandler> s_installedFontsChanged = new();
    private static readonly EventRegistry<EventHandler> s_lowMemory = new();
    private static readonly EventRegistry<EventHandler> s_paletteChanged = new();
    private static readonly EventRegistry<PowerModeChangedEventHandler> s_powerModeChanged = new();
    private static readonly EventRegistry<SessionEndedEventHandler> s_sessionEnded = new();
    private static readonly EventRegistry<SessionEndingEventHandler> s_sessionEnding = new();
    private static readonly EventRegistry<SessionSwitchEventHandler> s_sessionSwitch = new();
    private static readonly EventRegistry<EventHandler> s_timeChanged = new();
    private static readonly EventRegistry<TimerElapsedEventHandler> s_timerElapsed = new();
    private static readonly EventRegistry<UserPreferenceChangedEventHandler> s_userPreferenceChanged = new();
    private static readonly EventRegistry<UserPreferenceChangingEventHandler> s_userPreferenceChanging = new();
    private static readonly ConcurrentDictionary<nint, Timer> s_timers = new();
    private static int s_nextTimerId;

    static SystemEvents()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            s_eventsThreadShutdown.Raise(handler => handler(s_sender, EventArgs.Empty));
            EventsThread.Shutdown();
        };
    }

    internal SystemEvents()
    {
    }

    public static event EventHandler? DisplaySettingsChanged
    {
        add => s_displaySettingsChanged.Add(value);
        remove => s_displaySettingsChanged.Remove(value);
    }

    public static event EventHandler? DisplaySettingsChanging
    {
        add => s_displaySettingsChanging.Add(value);
        remove => s_displaySettingsChanging.Remove(value);
    }

    [Obsolete("SystemEvents.EventsThreadShutdown callbacks are not run before the process exits. Use AppDomain.ProcessExit instead.", DiagnosticId = "SYSLIB0059", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
    public static event EventHandler? EventsThreadShutdown
    {
        add => s_eventsThreadShutdown.Add(value);
        remove => s_eventsThreadShutdown.Remove(value);
    }

    public static event EventHandler? InstalledFontsChanged
    {
        add => s_installedFontsChanged.Add(value);
        remove => s_installedFontsChanged.Remove(value);
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Obsolete("The LowMemory event has been deprecated and is not supported.")]
    public static event EventHandler? LowMemory
    {
        add => s_lowMemory.Add(value);
        remove => s_lowMemory.Remove(value);
    }

    public static event EventHandler? PaletteChanged
    {
        add => s_paletteChanged.Add(value);
        remove => s_paletteChanged.Remove(value);
    }

    public static event PowerModeChangedEventHandler? PowerModeChanged
    {
        add => s_powerModeChanged.Add(value);
        remove => s_powerModeChanged.Remove(value);
    }

    public static event SessionEndedEventHandler? SessionEnded
    {
        add => s_sessionEnded.Add(value);
        remove => s_sessionEnded.Remove(value);
    }

    public static event SessionEndingEventHandler? SessionEnding
    {
        add => s_sessionEnding.Add(value);
        remove => s_sessionEnding.Remove(value);
    }

    public static event SessionSwitchEventHandler? SessionSwitch
    {
        add => s_sessionSwitch.Add(value);
        remove => s_sessionSwitch.Remove(value);
    }

    public static event EventHandler? TimeChanged
    {
        add => s_timeChanged.Add(value);
        remove => s_timeChanged.Remove(value);
    }

    public static event TimerElapsedEventHandler? TimerElapsed
    {
        add => s_timerElapsed.Add(value);
        remove => s_timerElapsed.Remove(value);
    }

    public static event UserPreferenceChangedEventHandler? UserPreferenceChanged
    {
        add => s_userPreferenceChanged.Add(value);
        remove => s_userPreferenceChanged.Remove(value);
    }

    public static event UserPreferenceChangingEventHandler? UserPreferenceChanging
    {
        add => s_userPreferenceChanging.Add(value);
        remove => s_userPreferenceChanging.Remove(value);
    }

    public static nint CreateTimer(int interval)
    {
        if (interval <= 0)
        {
            throw new ArgumentException("The timer interval must be greater than zero.", nameof(interval));
        }

        nint timerId = Interlocked.Increment(ref s_nextTimerId);
        Timer? timer = null;
        timer = new Timer(
            static state =>
            {
                nint id = (nint)state!;
                EventsThread.Post(() =>
                {
                    if (s_timers.ContainsKey(id))
                    {
                        s_timerElapsed.Raise(handler => handler(s_sender, new TimerElapsedEventArgs(id)));
                    }
                });
            },
            timerId,
            interval,
            interval);

        if (!s_timers.TryAdd(timerId, timer))
        {
            timer.Dispose();
            throw new InvalidOperationException("A portable system timer identifier could not be allocated.");
        }

        return timerId;
    }

    public static void InvokeOnEventsThread(Delegate method)
    {
        ArgumentNullException.ThrowIfNull(method);
        EventsThread.Post(() => InvokeDelegate(method));
    }

    public static void KillTimer(nint timerId)
    {
        if (s_timers.TryRemove(timerId, out Timer? timer))
        {
            timer.Dispose();
        }
    }

    internal static void RaiseDisplaySettingsChanging() =>
        s_displaySettingsChanging.Raise(handler => handler(s_sender, EventArgs.Empty));

    internal static void RaiseDisplaySettingsChanged() =>
        s_displaySettingsChanged.Raise(handler => handler(s_sender, EventArgs.Empty));

    internal static void RaiseInstalledFontsChanged() =>
        s_installedFontsChanged.Raise(handler => handler(s_sender, EventArgs.Empty));

    internal static void RaisePaletteChanged() =>
        s_paletteChanged.Raise(handler => handler(s_sender, EventArgs.Empty));

    internal static void RaisePowerModeChanged(PowerModes mode) =>
        s_powerModeChanged.Raise(handler => handler(s_sender, new PowerModeChangedEventArgs(mode)));

    internal static void RaiseSessionEnded(SessionEndReasons reason) =>
        s_sessionEnded.Raise(handler => handler(s_sender, new SessionEndedEventArgs(reason)));

    internal static bool RaiseSessionEnding(SessionEndReasons reason)
    {
        var args = new SessionEndingEventArgs(reason);
        s_sessionEnding.Raise(handler => handler(s_sender, args));
        return !args.Cancel;
    }

    internal static void RaiseSessionSwitch(SessionSwitchReason reason) =>
        s_sessionSwitch.Raise(handler => handler(s_sender, new SessionSwitchEventArgs(reason)));

    internal static void RaiseTimeChanged() =>
        s_timeChanged.Raise(handler => handler(s_sender, EventArgs.Empty));

    internal static void RaiseUserPreferenceChanging(UserPreferenceCategory category) =>
        s_userPreferenceChanging.Raise(handler => handler(s_sender, new UserPreferenceChangingEventArgs(category)));

    internal static void RaiseUserPreferenceChanged(UserPreferenceCategory category) =>
        s_userPreferenceChanged.Raise(handler => handler(s_sender, new UserPreferenceChangedEventArgs(category)));

    private static void InvokeDelegate(Delegate method)
    {
        try
        {
            if (method is Action action)
            {
                action();
            }
            else if (method is EventHandler eventHandler)
            {
                eventHandler(null, EventArgs.Empty);
            }
            else
            {
                // Compatibility boundary: the desktop API accepts arbitrary parameterless
                // Delegate types, so there is no statically typed invocation for this fallback.
                // It is isolated from event delivery and every product hot path. Callers should
                // pass Action (or EventHandler where required); this fallback can be removed if a
                // future breaking API revision narrows InvokeOnEventsThread to typed delegates.
                method.DynamicInvoke();
            }
        }
        catch
        {
            // The desktop implementation isolates exceptions raised by event-thread callbacks.
        }
    }

    private sealed class EventRegistry<TDelegate>
        where TDelegate : Delegate
    {
        private readonly object _gate = new();
        private readonly List<Subscription> _subscriptions = [];

        public void Add(TDelegate? handler)
        {
            if (handler is null)
            {
                return;
            }

            lock (_gate)
            {
                _subscriptions.Add(new(handler, SynchronizationContext.Current));
            }
        }

        public void Remove(TDelegate? handler)
        {
            if (handler is null)
            {
                return;
            }

            lock (_gate)
            {
                for (int index = _subscriptions.Count - 1; index >= 0; index--)
                {
                    if (_subscriptions[index].Handler.Equals(handler))
                    {
                        _subscriptions.RemoveAt(index);
                        break;
                    }
                }
            }
        }

        public void Raise(Action<TDelegate> invoke)
        {
            Subscription[] subscriptions;
            lock (_gate)
            {
                subscriptions = [.. _subscriptions];
            }

            foreach (Subscription subscription in subscriptions)
            {
                try
                {
                    if (subscription.Context is { } context && context != SynchronizationContext.Current)
                    {
                        context.Send(static state =>
                        {
                            var dispatch = (DispatchState)state!;
                            dispatch.Invoke(dispatch.Handler);
                        }, new DispatchState(subscription.Handler, invoke));
                    }
                    else
                    {
                        invoke(subscription.Handler);
                    }
                }
                catch
                {
                    // Match desktop SystemEvents: one subscriber cannot terminate event delivery,
                    // and a failing subscription is removed before the next broadcast.
                    Remove(subscription);
                }
            }
        }

        private void Remove(Subscription subscription)
        {
            lock (_gate)
            {
                _subscriptions.Remove(subscription);
            }
        }

        private readonly record struct Subscription(TDelegate Handler, SynchronizationContext? Context);

        private readonly record struct DispatchState(TDelegate Handler, Action<TDelegate> Invoke);
    }

    private static class EventsThread
    {
        private static readonly BlockingCollection<Action> s_queue = new();
        private static readonly Lazy<Thread> s_thread = new(CreateThread, LazyThreadSafetyMode.ExecutionAndPublication);

        public static void Post(Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            _ = s_thread.Value;
            try
            {
                _ = s_queue.TryAdd(callback);
            }
            catch (InvalidOperationException) when (s_queue.IsAddingCompleted)
            {
                // Process shutdown may complete the queue between TryAdd's
                // internal completion check and its write. Late callbacks are
                // intentionally ignored once shutdown has begun.
            }
        }

        public static void Shutdown()
        {
            foreach (nint timerId in s_timers.Keys)
            {
                if (s_timers.TryRemove(timerId, out Timer? timer))
                {
                    timer.Dispose();
                }
            }

            if (s_thread.IsValueCreated)
            {
                s_queue.CompleteAdding();
            }
        }

        private static Thread CreateThread()
        {
            var thread = new Thread(static () =>
            {
                foreach (Action callback in s_queue.GetConsumingEnumerable())
                {
                    try
                    {
                        callback();
                    }
                    catch
                    {
                    }
                }
            })
            {
                IsBackground = true,
                Name = ".NET Portable System Events",
            };
            thread.Start();
            return thread;
        }
    }
}

/// <summary>
/// Typed publication seam used by portable windowing hosts to deliver operating-system changes.
/// A host must call only the notification that corresponds to a change it observes from its
/// platform API. Events are never inferred from unrelated window activity. In particular,
/// window resizing is not a display-settings change. Hosts that can observe font, palette,
/// power, session, clock, or preference changes must publish those events explicitly.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class PortableSystemEvents
{
    public static void NotifyDisplaySettingsChanging() => SystemEvents.RaiseDisplaySettingsChanging();

    public static void NotifyDisplaySettingsChanged() => SystemEvents.RaiseDisplaySettingsChanged();

    public static void NotifyInstalledFontsChanged() => SystemEvents.RaiseInstalledFontsChanged();

    public static void NotifyPaletteChanged() => SystemEvents.RaisePaletteChanged();

    public static void NotifyPowerModeChanged(PowerModes mode) => SystemEvents.RaisePowerModeChanged(mode);

    public static void NotifySessionEnded(SessionEndReasons reason) => SystemEvents.RaiseSessionEnded(reason);

    public static bool NotifySessionEnding(SessionEndReasons reason) => SystemEvents.RaiseSessionEnding(reason);

    public static void NotifySessionSwitch(SessionSwitchReason reason) => SystemEvents.RaiseSessionSwitch(reason);

    public static void NotifyTimeChanged() => SystemEvents.RaiseTimeChanged();

    public static void NotifyUserPreferenceChanging(UserPreferenceCategory category) =>
        SystemEvents.RaiseUserPreferenceChanging(category);

    public static void NotifyUserPreferenceChanged(UserPreferenceCategory category) =>
        SystemEvents.RaiseUserPreferenceChanged(category);
}
