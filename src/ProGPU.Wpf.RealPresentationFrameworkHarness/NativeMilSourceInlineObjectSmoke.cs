using System.Reflection;
using System.Windows.Media.ProGPU.Composition.Mil;

internal static class NativeMilSourceInlineObjectSmoke
{
    // Diagnostic binding for side-by-side source and facade assemblies only.
    internal static void Run(Assembly framework, Assembly core, Assembly windowsBase)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object New(string name, params object[] args) => Activator.CreateInstance(framework.GetType(name, true)!, args)!;
        object Get(object value, string name) => value.GetType().GetProperty(name, flags)!.GetValue(value)!;
        void Set(object value, string name, object item) => value.GetType().GetProperty(name, flags)!.SetValue(value, item);
        void Add(object collection, object item) => collection.GetType().GetMethods().First(m => m.Name == "Add" &&
            m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsInstanceOfType(item)).Invoke(collection, [item]);
        object text = New("System.Windows.Controls.TextBlock");
        Set(text, "FontSize", 16.0);
        var wrap = text.GetType().GetProperty("TextWrapping")!;
        wrap.SetValue(text, Enum.Parse(wrap.PropertyType, "Wrap"));
        object child = New("System.Windows.Controls.Button");
        Set(child, "Width", 30.0); Set(child, "Height", 42.0);
        object container = New("System.Windows.Documents.InlineUIContainer", child);
        object inlines = Get(text, "Inlines");
        Add(inlines, New("System.Windows.Documents.Run", "A"));
        Add(inlines, container);
        Add(inlines, New("System.Windows.Documents.Run", "B"));
        var size = windowsBase.GetType("System.Windows.Size", true)!;
        var rect = windowsBase.GetType("System.Windows.Rect", true)!;
        var visualHelper = core.GetType("System.Windows.Media.VisualTreeHelper", true)!;
        object Layout(double width, bool requireChild = true)
        {
            text.GetType().GetMethod("Measure", [size])!.Invoke(text, [Activator.CreateInstance(size, width, 180.0)]);
            text.GetType().GetMethod("Arrange", [rect])!.Invoke(text, [Activator.CreateInstance(rect, 0.0, 0.0, width, 180.0)]);
            text.GetType().GetMethod("UpdateLayout", Type.EmptyTypes)!.Invoke(text, null);
            if (!requireChild) return new object();
            if (!(bool)Get(child, "IsArrangeValid") || !ReferenceEquals(Get(container, "Child"), child))
                throw new InvalidOperationException("Inline source child was replaced or not arranged.");
            object proxy = visualHelper.GetMethod("GetParent")!.Invoke(null, [child]) ??
                throw new InvalidOperationException("Inline source child is missing its retained visual parent.");
            object offset = visualHelper.GetMethod("GetOffset")!.Invoke(null, [proxy])!;
            var batch = new WpfNativeMilSceneCompiler().BuildBatch(text, checked((uint)width), 180);
            if (batch.GlyphRunFonts is not { Count: > 0 })
                throw new InvalidOperationException("Source text surrounding the inline object lost native glyph export.");
            return offset;
        }
        object narrow = Layout(31);
        double narrowY = (double)Get(narrow, "Y");
        if (narrowY <= 0 || Math.Abs((double)Get(Get(child, "RenderSize"), "Height") - 42) > .001)
            throw new InvalidOperationException("Tall inline child did not wrap with its real source height.");
        object wide = Layout(180);
        if ((double)Get(wide, "Y") >= narrowY || (double)Get(wide, "X") <= 0)
            throw new InvalidOperationException("Inline child did not rejoin its source text after widening.");
        object narrowAgain = Layout(31);
        if (Math.Abs((double)Get(narrowAgain, "Y") - narrowY) > .001)
            throw new InvalidOperationException("Repeated inline reflow changed its retained source placement.");
        Set(child, "Height", 60.0);
        Layout(31);
        if (Math.Abs((double)Get(Get(child, "RenderSize"), "Height") - 60) > .001)
            throw new InvalidOperationException("Inline source size mutation did not invalidate measurement.");
        inlines.GetType().GetMethod("Clear")!.Invoke(inlines, null);
        Layout(31, false);
        // TextBlock clears its proxy collection, not each disconnected proxy's
        // children. The actual source ownership invariant is no live root path.
        object? detachedProxy = visualHelper.GetMethod("GetParent")!.Invoke(null, [child]);
        if ((int)visualHelper.GetMethod("GetChildrenCount")!.Invoke(null, [text])! != 0 ||
            (detachedProxy != null && visualHelper.GetMethod("GetParent")!.Invoke(null, [detachedProxy]) != null))
            throw new InvalidOperationException("Cleared inline child retained a live source visual path.");
        Add(inlines, New("System.Windows.Documents.Run", "A"));
        Add(inlines, container);
        Add(inlines, New("System.Windows.Documents.Run", "B"));
        var direction = text.GetType().GetProperty("FlowDirection")!;
        direction.SetValue(text, Enum.Parse(direction.PropertyType, "RightToLeft"));
        object rtlNarrow = Layout(31);
        object rtlWide = Layout(180);
        if ((double)Get(rtlNarrow, "Y") <= (double)Get(rtlWide, "Y"))
            throw new InvalidOperationException("Reattached RTL inline child did not reflow.");
        Set(child, "Width", 0.0);
        Layout(31);
        if ((double)Get(Get(child, "RenderSize"), "Width") != 0)
            throw new InvalidOperationException("Zero-width source inline object acquired invented width.");
        Console.WriteLine("Source inline Button passed: real child ownership, LTR/RTL reflow, size/zero-width invalidation, detachment and reattachment.");
    }
}
