using LibreWinForms.Platform;
using LibreWinForms.ProGPU;
using System.Windows.Forms.Integration;

namespace LibreWinForms.CanonicalSdkSmoke;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        if (!LibrePlatform.IsRegistered)
        {
            throw new InvalidOperationException(
                "LibreWPF.Sdk did not register the canonical LibreWinForms ProGPU backend.");
        }

        RequireAssembly(typeof(System.Windows.Forms.Application), "System.Windows.Forms");
        RequireAssembly(typeof(WindowsFormsHost), "WindowsFormsIntegration");
        RequireAssembly(typeof(ProGpuPlatform), "LibreWinForms.ProGPU");

        using Form form = new()
        {
            Text = "Canonical LibreWPF SDK consumer"
        };
        form.Controls.Add(new Button { Text = "System.Windows.Forms" });

        Console.WriteLine(
            "Canonical LibreWPF SDK consumer passed with System.Windows.Forms, " +
            "WindowsFormsIntegration, and LibreWinForms.ProGPU.");
        LibrePlatform.Current.Dispose();
    }

    private static void RequireAssembly(Type type, string expectedName)
    {
        string? actualName = type.Assembly.GetName().Name;
        if (!string.Equals(actualName, expectedName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected {type.FullName} from {expectedName}, but resolved {actualName}.");
        }
    }
}
