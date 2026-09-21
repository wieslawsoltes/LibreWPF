namespace ProGPU.Wpf.SdkMixedDesktopSmoke;

internal static class Program
{
    [global::System.STAThread]
    private static void Main()
    {
        global::System.Type wpfApplication = typeof(global::System.Windows.Application);
        global::System.Type winFormsApplication = typeof(global::System.Windows.Forms.Application);
        Form? implicitWinFormsType = null;

        if (wpfApplication.Assembly == winFormsApplication.Assembly || implicitWinFormsType is not null)
        {
            throw new global::System.InvalidOperationException(
                "The mixed desktop SDK smoke did not resolve distinct WPF and WinForms surfaces.");
        }
    }
}
