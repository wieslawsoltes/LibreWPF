using System.Windows.Controls;
using System.Windows.Navigation;

namespace ProGPU.Wpf.SdkSwitchSmoke;

public sealed class SmokePageFunction : PageFunction<string>
{
    public const string DefaultResult = "SDK PageFunction return";

    public SmokePageFunction()
    {
        Title = "Compiled Smoke PageFunction";
        Content = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Name = "PageFunctionTitle",
                    Text = "Compiled page function content"
                },
                new TextBlock
                {
                    Name = "PageFunctionSubtitle",
                    Text = "Frame navigated to managed PageFunction"
                }
            }
        };
    }

    public void Complete(string? result = null)
    {
        OnReturn(new ReturnEventArgs<string>(result ?? DefaultResult));
    }
}
