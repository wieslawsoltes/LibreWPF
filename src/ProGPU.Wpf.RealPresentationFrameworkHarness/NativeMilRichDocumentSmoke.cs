using System.Collections;
using System.Reflection;
using System.Windows.Media.ProGPU.Composition.Mil;

internal static class NativeMilRichDocumentSmoke
{
    internal static void Run(Assembly framework, Assembly core, Assembly windowsBase)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object New(Assembly assembly, string name, params object[] args) =>
            Activator.CreateInstance(assembly.GetType(name, true)!, flags, null, args, null)!;
        object Get(object value, string name) =>
            value.GetType().GetProperty(name, flags)?.GetValue(value) ??
            value.GetType().GetField(name, flags)?.GetValue(value) ??
            throw new InvalidOperationException("Missing source member: " + name);
        void Set(object value, string name, object data) =>
            value.GetType().GetProperty(name, flags)!.SetValue(value, data);
        object? Call(object value, string name, params object[] args) =>
            value.GetType().GetMethods(flags).Single(m => m.Name == name && m.GetParameters().Length == args.Length &&
                m.GetParameters().Select((p, i) => p.ParameterType.IsInstanceOfType(args[i])).All(v => v)).Invoke(value, args);
        void Add(object owner, string name, object value) => Call(Get(owner, name), "Add", value);
        object Paragraph(string text)
        {
            object paragraph = New(framework, "System.Windows.Documents.Paragraph");
            Add(paragraph, "Inlines", New(framework, "System.Windows.Documents.Run", text));
            return paragraph;
        }
        object box = New(framework, "System.Windows.Controls.RichTextBox");
        Set(box, "OverridesDefaultStyle", true);
        Set(box, "Width", 240.0); Set(box, "Height", 120.0);
        object template = New(framework, "System.Windows.Controls.ControlTemplate", box.GetType());
        object factory = New(framework, "System.Windows.FrameworkElementFactory",
            framework.GetType("System.Windows.Controls.ScrollViewer", true)!);
        Set(factory, "Name", "PART_ContentHost");
        Set(template, "VisualTree", factory);
        Set(box, "Template", template);
        object document = Get(box, "Document");
        Call(Get(document, "Blocks"), "Clear");
        Add(document, "Blocks", Paragraph("first source paragraph"));
        object section = New(framework, "System.Windows.Documents.Section");
        Add(section, "Blocks", Paragraph("nested section source paragraph"));
        object list = New(framework, "System.Windows.Documents.List");
        var markerProperty = list.GetType().GetProperty("MarkerStyle")!;
        markerProperty.SetValue(list, Enum.Parse(markerProperty.PropertyType, "Decimal"));
        foreach (string text in new[] { "first numbered source item", "second numbered source item" })
        {
            object item = New(framework, "System.Windows.Documents.ListItem");
            Add(item, "Blocks", Paragraph(text));
            Add(list, "ListItems", item);
        }
        Add(section, "Blocks", list);
        Add(document, "Blocks", section);
        object container = Get(document, "TextContainer");
        object selection = Get(box, "Selection");
        Call(box, "ApplyTemplate");
        object scope = Get(box, "RenderScope");
        if (scope.GetType().FullName != "MS.Internal.Documents.FlowDocumentView" ||
            !ReferenceEquals(Get(scope, "Document"), document))
            throw new InvalidOperationException("Rich editor did not select the shared source document view.");
        object Measure()
        {
            Call(box, "Measure", New(windowsBase, "System.Windows.Size", 240.0, 120.0));
            Call(box, "Arrange", New(windowsBase, "System.Windows.Rect", 0.0, 0.0, 240.0, 120.0));
            Call(box, "UpdateLayout");
            return Get(scope, "PortableLayout");
        }
        object layout = Measure();
        object textView = Get(scope, "PortableTextView");
        if (!(bool)Get(textView, "IsValid") || !ReferenceEquals(Get(textView, "TextContainer"), container) ||
            !ReferenceEquals(Get(container, "TextView"), textView))
            throw new InvalidOperationException("Editor text service lost original source container ownership.");
        var lines = (IList)Get(layout, "Lines");
        var positions = (IList)Get(layout, "Positions");
        var markers = (IList)Get(layout, "Markers");
        if (lines.Count < 4 || markers.Count != 2)
            throw new InvalidOperationException("Native document layout lost source paragraphs or list markers.");
        double previousY = double.NegativeInfinity;
        int previousSource = -1;
        foreach (object record in lines)
        {
            int start = (int)Get(record, "Start");
            if (start <= previousSource) throw new InvalidOperationException("Document lines lost source symbol order.");
            previousSource = start;
        }
        foreach (object position in positions)
        {
            double y = (double)Get(position, "Y");
            if (y <= previousY) throw new InvalidOperationException("Native blocks were flattened onto the same line.");
            previousY = y;
        }
        object firstPosition = positions[0]!;
        object point = New(windowsBase, "System.Windows.Point",
            (double)Get(firstPosition, "X") + 2, (double)Get(firstPosition, "Y") + 2);
        object pointer = Call(textView, "GetTextPositionFromPoint", point, true)!;
        if (!ReferenceEquals(Get(pointer, "TextContainer"), container))
            throw new InvalidOperationException("Native document hit returned a copied document position.");
        if (new WpfNativeMilSceneCompiler().BuildBatch(box, 240, 120).GlyphRunFonts is not { Count: > 0 })
            throw new InvalidOperationException("Rich document source drawing did not reach native MIL.");
        using (var session = new WpfNativeMilCompilationSession())
        {
            session.Update(box, 240, 120);
            session.CompileFrame(8212, 1, 0, 1,
                flags: ProGPU.Backend.Native.NativeMilSceneBuildRequestFlags.HitTestIndex);
        }
        Call(box, "SelectAll");
        if (!((string)Get(selection, "Text")).Contains("second numbered source item", StringComparison.Ordinal))
            throw new InvalidOperationException("SelectAll did not span the original document blocks.");
        Set(selection, "Text", "source replacement");
        if ((bool)Get(textView, "IsValid"))
            throw new InvalidOperationException("Document edit left stale native interaction valid.");
        object replacement = Measure();
        if (ReferenceEquals(layout, replacement) || !ReferenceEquals(Get(box, "Selection"), selection) ||
            !ReferenceEquals(Get(document, "TextContainer"), container))
            throw new InvalidOperationException("Editing replaced source ownership or retained a stale layout.");
        Call(box, "SelectAll");
        if (!((string)Get(selection, "Text")).Contains("source replacement", StringComparison.Ordinal))
            throw new InvalidOperationException("Source replacement was not retained.");
        Call(box, "Undo");
        Measure();
        Call(box, "SelectAll");
        if (!((string)Get(selection, "Text")).Contains("second numbered source item", StringComparison.Ordinal))
            throw new InvalidOperationException("Native document view lost source undo.");
        object nextDocument = New(framework, "System.Windows.Documents.FlowDocument");
        Add(nextDocument, "Blocks", Paragraph("new document"));
        Set(box, "Document", nextDocument);
        if ((bool)Get(textView, "IsValid"))
            throw new InvalidOperationException("Detached document view retained valid source interaction.");
        Console.WriteLine("Source rich document passed: native paragraph/section/list layout, original text view, hits, MIL export, selection, edit, undo and replacement invalidation.");
    }
}
