using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Portable monitor information used by legacy WPF controls for popup placement.
/// </summary>
public sealed class Screen
{
    private static readonly Rectangle s_defaultWorkingArea = GetDefaultWorkingArea();
    private static readonly Screen s_primaryScreen = new(s_defaultWorkingArea);

    private Screen(Rectangle workingArea)
    {
        WorkingArea = workingArea;
        Bounds = workingArea;
    }

    public static Screen PrimaryScreen => s_primaryScreen;

    public static Screen[] AllScreens => new[] { s_primaryScreen };

    public static Screen FromHandle(IntPtr hwnd)
    {
        return s_primaryScreen;
    }

    public Rectangle Bounds { get; }

    public Rectangle WorkingArea { get; }

    public static Screen FromPoint(Point point)
    {
        return s_primaryScreen;
    }

    public static Rectangle GetWorkingArea(Point pt)
    {
        return s_primaryScreen.WorkingArea;
    }

    private static Rectangle GetDefaultWorkingArea()
    {
        int width = ReadPositiveInt("LIBREWPF_WINFORMS_SCREEN_WIDTH", 4096);
        int height = ReadPositiveInt("LIBREWPF_WINFORMS_SCREEN_HEIGHT", 2160);
        return new Rectangle(0, 0, width, height);
    }

    private static int ReadPositiveInt(string name, int fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out int parsed) && parsed > 0 ? parsed : fallback;
    }
}
