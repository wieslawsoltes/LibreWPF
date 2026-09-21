namespace System.Windows.Forms;

/// <summary>
/// Portable compatibility surface for WPF-era code that toggles the WinForms cursor.
/// </summary>
public sealed class Cursor
{
    private static int s_visibilityDepth;

    public static Cursor Current { get; set; } = Cursors.Default;

    public Cursor()
    {
    }

    public static void Hide()
    {
        s_visibilityDepth--;
    }

    public static void Show()
    {
        if (s_visibilityDepth < 0)
        {
            s_visibilityDepth++;
        }
    }
}
