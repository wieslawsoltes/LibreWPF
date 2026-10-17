using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ProGPU.Backend;
using System.Windows.Media.ProGPU.Platform;

// Actual owned Win32 API control. This is not source/package/application parity.
// No input injection or forced activation; only windows created here are mutated.
internal static class Program
{
    private static readonly List<object> Records = [];
    private static int Main()
    {
        var clock = Stopwatch.StartNew();
        nint child = 0, first = 0, second = 0, hidden = 0;
        WpfWin32TaskbarPolicy? policy = null;
        Exception? failure = null;
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            first = Create("Taskbar API owner A"); second = Create("Taskbar API owner B"); child = Create("Taskbar API subject");
            var native = new Win32WpfTaskbarOperations(new(NativeWindowKind.Win32, child, 0, "Owned API control"));
            policy = new(native);
            nint large = LoadIconW(0, 32512), small = LoadIconW(0, 32513);
            Require(large != 0 && small != 0, "Original shared icon resources unavailable.");
            SendMessageW(child, 0x80, 1, large); SendMessageW(child, 0x80, 0, small);
            long originalStyle = GetWindowLongPtrW(child, -16);
            long originalEx = native.Read().ExtendedStyle;
            native.SetVisibleWithoutActivation(true);
            Check(true, 0, "unowned-default");
            Check(false, 0, "unowned-false");
            hidden = native.Read().Owner;
            Require(hidden != 0 && IsWindow(hidden) && !IsWindowVisible(hidden), "Hidden owner not retained.");
            nint oldLarge = SendMessageW(hidden, 0x7f, 1, 0), oldSmall = SendMessageW(hidden, 0x7f, 0, 0);
            Require(oldLarge != 0 && oldSmall != 0 && oldLarge != large && oldSmall != small, "Hidden owner must own icon copies.");
            Require(ValidIcon(oldLarge) && ValidIcon(oldSmall), "Copied icons are not live.");
            Check(true, hidden, "unowned-true-again");
            Check(false, 0, "unowned-false-again");
            Check(false, first, "owned-A-false");
            Check(true, first, "owned-A-true");
            Check(false, second, "owned-B-false");
            Check(false, 0, "owner-cleared-false");
            Check(true, hidden, "owner-cleared-true");
            Require(IsWindow(hidden), "True must retain its hidden dependency.");
            oldLarge = SendMessageW(hidden, 0x7f, 1, 0); oldSmall = SendMessageW(hidden, 0x7f, 0, 0);
            SendMessageW(child, 0x80, 1, small); SendMessageW(child, 0x80, 0, large);
            policy.SynchronizeIcons();
            Require(!ValidIcon(oldLarge) && !ValidIcon(oldSmall), "Replaced icon copies leaked.");
            Require(ValidIcon(large) && ValidIcon(small), "Provider icon handles were destroyed.");
            nint retainedLarge = SendMessageW(hidden, 0x7f, 1, 0), retainedSmall = SendMessageW(hidden, 0x7f, 0, 0);
            Require(ValidIcon(retainedLarge) && ValidIcon(retainedSmall), "Replacement copies are invalid.");
            Destroy(child); child = 0;
            policy.ReleaseAfterNativeWindow();
            policy.ReleaseAfterNativeWindow();
            Require(!IsWindow(hidden), "Hidden dependency survived retirement.");
            Require(!ValidIcon(retainedLarge) && !ValidIcon(retainedSmall), "Icon copies survived native retirement.");
            Require(ValidIcon(large) && ValidIcon(small), "Retirement destroyed borrowed provider icons.");
            Records.Add(new { phase = "retired", iconCopiesRetired = true, providerIconsPreserved = true });

            void Check(bool show, nint sourceOwner, string phase)
            {
                if (clock.Elapsed.TotalSeconds >= 20) throw new TimeoutException("Original 20-second API bound exceeded.");
                native.SetOwner(sourceOwner);
                policy.Apply(show);
                var actual = native.Read();
                nint expectedOwner = show && sourceOwner == hidden ? 0 : sourceOwner;
                if (!show && sourceOwner == 0) expectedOwner = hidden == 0 ? actual.Owner : hidden;
                Require(actual.Owner == expectedOwner && (show || sourceOwner != 0 || actual.Owner != 0), "Native owner mismatch: " + phase);
                Require(actual.ExtendedStyle == (show ? originalEx | 0x40000 : originalEx & ~0x40000), "Unrelated extended styles changed: " + phase);
                Require(actual.Visible, "Visibility lost: " + phase);
                // WS_VISIBLE is the only ordinary style bit changed by this control.
                Require(((long)GetWindowLongPtrW(child, -16) & ~0x10000000L) == (originalStyle & ~0x10000000L), "Ordinary chrome changed: " + phase);
                Records.Add(new { phase, show, owner = (long)actual.Owner, extendedStyle = actual.ExtendedStyle, visible = actual.Visible });
            }
        }
        catch (Exception error) { failure = error; }
        finally
        {
            try { if (child != 0 && IsWindow(child)) Destroy(child); policy?.ReleaseAfterNativeWindow(); }
            catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
            foreach (nint owner in new[] { second, first })
            {
                try { if (owner != 0 && IsWindow(owner)) Destroy(owner); }
                catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { diagnosticOnly = true, qualified = false,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(), records = Records,
            elapsedMs = clock.Elapsed.TotalMilliseconds, failure = failure?.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
        return failure == null && Records.Count == 10 && clock.Elapsed.TotalSeconds < 20 ? 0 : 1;
    }
    private static nint Create(string title)
    {
        nint result = CreateWindowExW(0x40000, "STATIC", title, 0xcf0000, 100, 100, 240, 150, 0, 0, 0, 0);
        if (result == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        return result;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Destroy(nint window) { if (!DestroyWindow(window)) throw new Win32Exception(Marshal.GetLastPInvokeError()); }
    private static bool ValidIcon(nint icon)
    {
        if (icon == 0 || !GetIconInfo(icon, out var info)) return false;
        try { return true; }
        finally
        {
            if (info.Mask != 0 && !DeleteObject(info.Mask)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (info.Color != 0 && !DeleteObject(info.Color)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct IconInfo { public int IsIcon; public uint X, Y; public nint Mask, Color; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint window, int index);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint window, uint message, nint parameter, nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadIconW(nint instance, nint name);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetIconInfo(nint icon, out IconInfo info);
    [DllImport("gdi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint value);
}
