using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Platform;
using ProGPU.Wpf.Interop;

// A separate process owns this headless activation registration. This exercises
// real source Slider/Track/Thumb and bridge input, not native rendering or pixels.
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 1 || !int.TryParse(args[0], out int scenario) || scenario < 0 || scenario >= 10)
            throw new ArgumentException("Specify exactly one Slider scenario index from 0 through 9.");
        PortableWpfRuntime.SelectMediaBackend(PortableWpfMediaBackend.Portable);
        RuntimeHelpers.RunModuleConstructor(typeof(Application).Module.ModuleHandle);
        if (!WpfPortableWindowActivation.TryRegisterPresentationFrameworkActivation())
            throw new InvalidOperationException("Missing typed source activation service.");

        // Each invocation owns one real Window/dispatcher lifetime and one
        // process-global headless registration. The SDK harness runs all ten.
        int kind = scenario % 5;
        Validate(scenario < 5 ? 1 : 2, vertical: kind is 2 or 3,
            reversed: kind is 1 or 3, snappedRange: kind == 4);
        Console.WriteLine($"Slider drag source contract passed: case {scenario}; no native window or renderer qualification.");
    }

    private static void Validate(double dpi, bool vertical, bool reversed, bool snappedRange)
    {
        Console.WriteLine($"Slider drag: dpi={dpi}, vertical={vertical}, reversed={reversed}, snappedRange={snappedRange}");
        using var source = PortablePresentationSourceHost.Create(dpi, dpi);
        using var host = new ProGpuWpfWindowHost { WpfRenderScheduler = new CoalescingWpfRenderScheduler() };
        var window = new Window { Width = 400, Height = 280 };
        var canvas = new Canvas { Background = Brushes.White };
        double initial = snappedRange ? 600 : 20;
        double step = snappedRange ? 50 : 5;
        var slider = new Slider
        {
            Minimum = snappedRange ? 500 : 0, Maximum = snappedRange ? 1000 : 100,
            Value = initial, Width = vertical ? 30 : 200, Height = vertical ? 200 : 30,
            Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
            IsDirectionReversed = reversed, IsSnapToTickEnabled = snappedRange,
            TickFrequency = snappedRange ? 50 : 1,
            Template = (ControlTemplate)XamlReader.Parse("""
                <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Slider">
                  <Grid Background="Transparent">
                    <Track x:Name="PART_Track" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{TemplateBinding Value}"
                           Orientation="{TemplateBinding Orientation}" IsDirectionReversed="{TemplateBinding IsDirectionReversed}">
                      <Track.Thumb><Thumb Width="20" Height="20"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="Black" /></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
                    </Track>
                  </Grid>
                </ControlTemplate>
                """)
        };
        Canvas.SetLeft(slider, 40); Canvas.SetTop(slider, 20);
        canvas.Children.Add(slider); window.Content = canvas;
        if (!WpfPortableWindowActivation.TryAttach(host, window, source, out var activation))
            throw new InvalidOperationException("Real source/bridge attachment failed.");
        using var lease = activation;
        if (!PortableWpfServiceRegistry.TryGetWindowActivationService(PortableWpfServiceKey.PresentationFramework, out var registrar))
            throw new InvalidOperationException("Source input registrar disappeared.");
        // Suppress only native Show. Source Window.Show still owns visibility and
        // activation identity; all input/layout executes in the product classes.
        registrar.Register(new PortableWindowActivationCallbacks(_ => activation, show: _ => { }, getHandle: _ => source.Handle));
        try
        {
            window.Show(); source.SetClientSize(400, 280); window.UpdateLayout();
            var track = (Track)slider.Template.FindName("PART_Track", slider);
            Thumb thumb = track.Thumb;
            Equal(200, vertical ? track.ActualHeight : track.ActualWidth, "track extent");
            Equal(20, vertical ? thumb.ActualHeight : thumb.ActualWidth, "thumb extent");
            Point start = thumb.TranslatePoint(new Point(10, 10), window);
            Send(WpfInputEventKind.MouseMove, 0);
            Send(WpfInputEventKind.MouseDown, 0);
            if (!thumb.IsDragging || !ReferenceEquals(Mouse.Captured, thumb))
                throw new InvalidOperationException("Actual Slider Thumb did not capture input.");
            // Independent geometry: 200 - 20 = 180 DIPs of travel. Nine DIPs
            // means 5/100; eighteen DIPs means 50/500 in the snapped range.
            double distance = snappedRange ? 18 : 9;
            for (int i = 1; i <= 6; ++i)
            {
                Send(WpfInputEventKind.MouseMove, i * distance);
                Equal(initial + i * step, slider.Value, $"forward event {i}");
                if (!track.IsArrangeValid) throw new InvalidOperationException($"Forward event {i}: next native move would see stale Track layout.");
            }
            for (int i = 5; i >= 0; --i)
            {
                Send(WpfInputEventKind.MouseMove, i * distance);
                Equal(initial + i * step, slider.Value, $"reverse event {i}");
                if (!track.IsArrangeValid) throw new InvalidOperationException($"Reverse event {i}: next native move would see stale Track layout.");
            }
            Send(WpfInputEventKind.MouseUp, 0);
            if (thumb.IsDragging || Mouse.Captured is not null || host.SilkWindow is not null)
                throw new InvalidOperationException("Capture cleanup or headless boundary failed.");
            Equal(initial, slider.Value, "final value");

            void Send(WpfInputEventKind kind, double offset)
            {
                double delta = offset * (vertical ? -1 : 1) * (reversed ? -1 : 1);
                if (!ProGpuWpfDiagnostics.TryRaiseInput(window, new WpfInputEventArgs(kind,
                    x: start.X + (vertical ? 0 : delta), y: start.Y + (vertical ? delta : 0), button: WpfMouseButton.Left)))
                    throw new InvalidOperationException("Typed host input rejected.");
            }
        }
        finally
        {
            Mouse.Capture(null);
            window.Close();
        }
    }

    private static void Equal(double expected, double actual, string name)
    {
        if (Math.Abs(expected - actual) > 0.000001)
            throw new InvalidOperationException($"Slider {name}: expected {expected}, actual {actual}.");
    }
}
