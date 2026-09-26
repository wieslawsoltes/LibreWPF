using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ProGPU.Wpf.ShowcaseApp;

public partial class MainWindow
{
    // Only the final failed attempt reads these values. Diagnostics neither
    // repair layout nor inject input, and cannot turn a failed gate into a pass.
    private string DescribeLiveThumbHitFailure(Thumb target)
    {
        Point center = target.TranslatePoint(
            new Point(Math.Max(1.0, target.ActualWidth) / 2.0, Math.Max(1.0, target.ActualHeight) / 2.0),
            this);
        object? hit = InputHitTest(center);
        PresentationSource? targetSource = PresentationSource.FromVisual(target);
        PresentationSource? windowSource = PresentationSource.FromVisual(this);
        var tabs = FindName("ShowcaseTabControl") as TabControl;
        return $", SelectedTab={tabs?.SelectedIndex}, " +
            $"TargetSourcePresent={targetSource is not null}, WindowSourcePresent={windowSource is not null}, " +
            $"SameSource={targetSource is not null && ReferenceEquals(targetSource, windowSource)}, " +
            $"TargetPath=[{DescribeLiveInputVisualPath(target)}], " +
            $"HitPath=[{DescribeLiveInputVisualPath(hit as DependencyObject)}]";
    }

    private string DescribeLiveInputVisualPath(DependencyObject? element)
    {
        var result = new StringBuilder();
        DependencyObject? current = element;
        for (int depth = 0; current is not null && depth < 64; depth++)
        {
            if (depth != 0)
                result.Append(" <- ");

            string kind = current switch
            {
                Window => "Window",
                Thumb => "Thumb",
                ScrollViewer => "ScrollViewer",
                ContentPresenter => "ContentPresenter",
                Border => "Border",
                Panel => "Panel",
                FrameworkElement => "FrameworkElement",
                UIElement => "UIElement",
                _ => "DependencyObject"
            };
            result.Append(kind).Append('(').Append(DescribeInputElement(current));
            if (current is FrameworkElement frameworkElement)
            {
                result.Append($", Size={frameworkElement.RenderSize}, " +
                    $"Visible={frameworkElement.IsVisible}, HitVisible={frameworkElement.IsHitTestVisible}, " +
                    $"MeasureValid={frameworkElement.IsMeasureValid}, ArrangeValid={frameworkElement.IsArrangeValid}, " +
                    $"TemplateOwner={DescribeInputElement(frameworkElement.TemplatedParent)}");
            }

            if (current is Visual visual)
            {
                Geometry? clip = VisualTreeHelper.GetClip(visual);
                result.Append($", Offset={VisualTreeHelper.GetOffset(visual)}, " +
                    $"Transform={VisualTreeHelper.GetTransform(visual)?.Value}, " +
                    $"Clip={(clip is null ? "none" : clip is RectangleGeometry rectangle ? rectangle.Rect.ToString() : "nonrectangular")}");
            }

            result.Append(')');
            if (ReferenceEquals(current, this))
            {
                result.Append(" ROOT");
                return result.ToString();
            }

            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : current is FrameworkContentElement contentElement ? contentElement.Parent : null;
        }

        result.Append(current is null ? " DISCONNECTED" : " TRUNCATED");
        return result.ToString();
    }
}
