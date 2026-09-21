using System.Collections;
using System.Reflection;

internal static class NativeMilRichTextDecorationSmoke
{
    // Diagnostic binding only: the harness deliberately loads real WPF beside
    // the neutral facade assemblies. Product source uses TextSpanModifier directly.
    internal static void Run(Assembly framework)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object New(string name, params object[] args) => Activator.CreateInstance(
            framework.GetType(name, true)!, flags, null, args, null)!;
        object Get(object value, string name) => value.GetType().GetProperty(name, flags)!.GetValue(value)!;
        void Add(object owner, string property, object value)
        {
            object collection = Get(owner, property);
            var method = collection.GetType().GetMethods().First(m => m.Name == "Add" &&
                m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsInstanceOfType(value));
            method.Invoke(collection, [value]);
        }
        object box = New("System.Windows.Controls.RichTextBox");
        object document = Get(box, "Document");
        object blocks = Get(document, "Blocks");
        blocks.GetType().GetMethod("Clear")!.Invoke(blocks, null);
        object paragraph = New("System.Windows.Documents.Paragraph");
        object outer = New("System.Windows.Documents.Underline");
        object inner = New("System.Windows.Documents.Underline");
        Add(outer, "Inlines", New("System.Windows.Documents.Run", "before "));
        Add(inner, "Inlines", New("System.Windows.Documents.Run", "nested"));
        Add(outer, "Inlines", inner);
        Add(outer, "Inlines", New("System.Windows.Documents.LineBreak"));
        Add(outer, "Inlines", New("System.Windows.Documents.Run", "after"));
        Add(paragraph, "Inlines", outer);
        Add(paragraph, "Inlines", New("System.Windows.Documents.Run", " plain"));
        Add(document, "Blocks", paragraph);
        object view = New("System.Windows.Controls.TextBoxView", box);
        using var line = (IDisposable)New("System.Windows.Controls.TextBoxLine", view);
        var fetch = line.GetType().GetMethod("GetTextRun", flags)!;
        var modifiers = new Stack<object>();
        var starts = new List<(int Position, bool Decorated)>();
        int position = 0, opened = 0, closed = 0, breaks = 0, decorated = 0, plain = 0;
        bool finished = false;
        for (int iteration = 0; iteration < 128; ++iteration)
        {
            object run = fetch.Invoke(line, [position])!;
            int length = (int)Get(run, "Length");
            if (length <= 0) throw new InvalidOperationException("Rich source run did not advance.");
            switch (run.GetType().Name)
            {
                case "TextEndOfParagraph":
                    finished = true;
                    break;
                case "TextSpanModifier":
                    modifiers.Push(run); ++opened;
                    break;
                case "TextEndOfSegment":
                    if (!modifiers.TryPop(out _)) throw new InvalidOperationException("Unmatched rich decoration end.");
                    ++closed;
                    break;
                case "TextEndOfLine":
                    if (modifiers.Count != 1) throw new InvalidOperationException("Explicit line break lost its outer decoration scope.");
                    ++breaks;
                    break;
                case "TextCharacters":
                    starts.Add((position, modifiers.Count > 0));
                    object properties = Get(run, "Properties");
                    foreach (object modifier in modifiers)
                        properties = modifier.GetType().GetMethod("ModifyProperties")!.Invoke(modifier, [properties])!;
                    var decorations = (ICollection?)properties.GetType().GetProperty("TextDecorations", flags)!.GetValue(properties);
                    if (modifiers.Count == 0)
                    {
                        if (decorations is { Count: > 0 }) throw new InvalidOperationException("Decoration leaked beyond its source scope.");
                        plain += length;
                    }
                    else
                    {
                        if (decorations?.Count != modifiers.Count) throw new InvalidOperationException("Nested source decorations were lost.");
                        decorated += length;
                    }
                    break;
            }
            if (finished) break;
            position += length;
        }
        if (!finished || modifiers.Count != 0 || opened != 2 || closed != 2 || breaks != 1 ||
            decorated != "before nestedafter".Length || plain != " plain".Length)
            throw new InvalidOperationException("Rich decoration scope/source-length contract failed.");
        Assembly core = line.GetType().BaseType!.Assembly;
        object propertiesForLine = view.GetType().GetMethod("GetLineProperties", flags)!.Invoke(view, null)!;
        using var formatter = (IDisposable)core.GetType("System.Windows.Media.TextFormatting.TextFormatter", true)!
            .GetMethod("Create", Type.EmptyTypes)!.Invoke(null, null)!;
        foreach (var start in starts)
        {
            line.Dispose();
            object cache = Activator.CreateInstance(core.GetType("System.Windows.Media.TextFormatting.TextRunCache", true)!)!;
            line.GetType().GetMethod("Format", flags)!.Invoke(line,
                [start.Position, 160.0, 160.0, propertiesForLine, cache, formatter]);
            object formatted = line.GetType().GetField("_line", flags)!.GetValue(line)!;
            if (formatted.GetType().Name != "PortableTextLine" || (double)Get(formatted, "Width") <= 0)
                throw new InvalidOperationException("Rich editor source did not reach native text formatting.");
            object? underlineGeometry = formatted.GetType().GetField("_underlines", flags)!.GetValue(formatted);
            bool hasUnderline = underlineGeometry is ICollection geometry && geometry.Count > 0;
            if (hasUnderline != start.Decorated)
                throw new InvalidOperationException("Independent rich line formatting lost or leaked its source decoration scopes.");
        }
        foreach (string unsupported in new[] { "Figure", "Floater", "InlineUIContainer" })
        {
            object inlines = Get(paragraph, "Inlines");
            inlines.GetType().GetMethod("Clear")!.Invoke(inlines, null);
            Add(paragraph, "Inlines", New("System.Windows.Documents." + unsupported));
            bool rejected = false;
            position = 0;
            try
            {
                for (int iteration = 0; iteration < 16; ++iteration)
                {
                    object run = fetch.Invoke(line, [position])!;
                    if (run.GetType().Name == "TextEndOfParagraph") break;
                    position += (int)Get(run, "Length");
                }
            }
            catch (TargetInvocationException ex) when (ex.InnerException is PlatformNotSupportedException failure &&
                failure.Message.Contains("Source element: " + unsupported, StringComparison.Ordinal))
            {
                rejected = true;
            }
            if (!rejected) throw new InvalidOperationException("Unsupported source object was flattened as text: " + unsupported);
        }
        Console.WriteLine("Source rich-text decoration scopes passed: nested modifiers, independent native line formatting, line-break continuation, closing edges, plain suffix and explicit object rejection.");
    }
}
