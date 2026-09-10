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
        object blockObject = New(framework, "System.Windows.Documents.BlockUIContainer");
        object button = New(framework, "System.Windows.Controls.Button");
        Set(button, "Width", 100.0); Set(button, "Height", 32.0);
        Set(button, "Content", "embedded source button");
        Set(button, "OverridesDefaultStyle", true);
        object buttonTemplate = New(framework, "System.Windows.Controls.ControlTemplate", button.GetType());
        object buttonBorder = New(framework, "System.Windows.FrameworkElementFactory",
            framework.GetType("System.Windows.Controls.Border", true)!);
        object background = framework.GetType("System.Windows.Controls.Border", true)!.GetField("BackgroundProperty")!.GetValue(null)!;
        object blue = core.GetType("System.Windows.Media.Brushes", true)!.GetProperty("Blue")!.GetValue(null)!;
        Call(buttonBorder, "SetValue", background, blue);
        Set(buttonTemplate, "VisualTree", buttonBorder);
        Set(button, "Template", buttonTemplate);
        Set(blockObject, "Child", button);
        Add(document, "Blocks", blockObject);
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
        object? Parent(object visual) => core.GetType("System.Windows.Media.VisualTreeHelper", true)!
            .GetMethod("GetParent", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [visual]);
        object objectVisual = Parent(button) ?? throw new InvalidOperationException("Source block control is not attached.");
        var embeddedRecords = (IList)Get(layout, "Objects");
        if (embeddedRecords.Count != 1 || !ReferenceEquals(Get(embeddedRecords[0]!, "Child"), button) ||
            !ReferenceEquals(Get(blockObject, "Child"), button))
            throw new InvalidOperationException("Native block layout copied or lost the source control.");
        object embeddedRecord = embeddedRecords[0]!;
        object nativeBox = ((IList)Get(layout, "Boxes"))[(int)Get(embeddedRecord, "BlockIndex")]!;
        double objectX = (double)Get(nativeBox, "X"), objectY = (double)Get(nativeBox, "Y");
        if ((double)Get(nativeBox, "Height") != 32.0)
            throw new InvalidOperationException("Native block placement lost measured control height.");
        object contentStart = Get(blockObject, "ContentStart"), contentEnd = Get(blockObject, "ContentEnd");
        object objectPoint = New(windowsBase, "System.Windows.Point", objectX + 1, objectY + 1);
        object objectHit = Call(textView, "GetTextPositionFromPoint", objectPoint, true)!;
        if ((int)Get(objectHit, "Offset") != (int)Get(contentStart, "Offset"))
            throw new InvalidOperationException("Object hit did not retain the actual source symbol.");
        var caretMethod = textView.GetType().GetMethod("GetNextCaretUnitPosition", flags)!;
        Type directionType = caretMethod.GetParameters()[1].ParameterType;
        object forward = Enum.Parse(directionType, "Forward");
        if (!(bool)Call(textView, "IsAtCaretUnitBoundary", contentStart)! ||
            !(bool)Call(textView, "IsAtCaretUnitBoundary", contentEnd)! ||
            (int)Get(Call(textView, "GetNextCaretUnitPosition", contentStart, forward)!, "Offset") != (int)Get(contentEnd, "Offset") ||
            (int)Get(Call(textView, "GetBackspaceCaretUnitPosition", contentEnd)!, "Offset") != (int)Get(contentStart, "Offset"))
            throw new InvalidOperationException("Block control caret movement lost its one source object symbol.");
        var rectangles = (IList)Call(textView, "GetDocumentRectangles", contentStart, contentEnd, false)!;
        if (rectangles.Count != 1 || (double)Get(rectangles[0]!, "Height") != 32.0)
            throw new InvalidOperationException("Block object selection did not consume its native content box.");
        var lineMove = textView.GetType().GetMethod("GetPositionAtNextLine", flags)!;
        object?[] moveArgs = [contentStart, objectX, 1, null, null];
        object nextLine = lineMove.Invoke(textView, moveArgs)!;
        if ((int)moveArgs[4]! != 1 || (int)Get(nextLine, "Offset") <= (int)Get(contentEnd, "Offset"))
            throw new InvalidOperationException("Vertical navigation did not leave the real block object.");
        Set(button, "Height", 48.0);
        Call(button, "Measure", New(windowsBase, "System.Windows.Size", (double)Get(nativeBox, "Width"), double.PositiveInfinity));
        if ((bool)Get(textView, "IsValid"))
            throw new InvalidOperationException("Control desired-size change left stale native interaction valid.");
        object resized = Measure();
        object resizedBox = ((IList)Get(resized, "Boxes"))[(int)Get(embeddedRecord, "BlockIndex")]!;
        if ((double)Get(resizedBox, "Height") != 48.0 || !ReferenceEquals(Parent(button), objectVisual))
            throw new InvalidOperationException("Reflow lost native control size or detached its stable visual parent.");
        Call(box, "SelectAll");
        if (!((string)Get(selection, "Text")).Contains("second numbered source item", StringComparison.Ordinal))
            throw new InvalidOperationException("SelectAll did not span the original document blocks.");
        Set(selection, "Text", "source replacement");
        if ((bool)Get(textView, "IsValid"))
            throw new InvalidOperationException("Document edit left stale native interaction valid.");
        object replacement = Measure();
        if (Parent(button) != null) throw new InvalidOperationException("Deleted block control retained a drawing attachment.");
        if (ReferenceEquals(layout, replacement) || !ReferenceEquals(Get(box, "Selection"), selection) ||
            !ReferenceEquals(Get(document, "TextContainer"), container))
            throw new InvalidOperationException("Editing replaced source ownership or retained a stale layout.");
        Call(box, "SelectAll");
        if (!((string)Get(selection, "Text")).Contains("source replacement", StringComparison.Ordinal))
            throw new InvalidOperationException("Source replacement was not retained.");
        Call(box, "Undo");
        object restoredLayout = Measure();
        var restoredObjects = (IList)Get(restoredLayout, "Objects");
        if (restoredObjects.Count != 1)
            throw new InvalidOperationException("Undo did not restore the source block object.");
        // Source undo deserializes its saved UIElement; renderer ownership must
        // follow that live source child, not reattach the deleted instance.
        object restoredRecord = restoredObjects[0]!;
        object restoredButton = Get(restoredRecord, "Child");
        object restoredContainer = Get(restoredRecord, "Container");
        if (restoredButton.GetType() != button.GetType() ||
            !Equals(Get(restoredButton, "Content"), "embedded source button") ||
            (double)Get(restoredButton, "Height") != 48.0 ||
            !ReferenceEquals(Get(restoredContainer, "Child"), restoredButton) ||
            !ReferenceEquals(Get(Get(restoredContainer, "ContentStart"), "TextContainer"), container) ||
            Parent(restoredButton) == null || Parent(button) != null)
            throw new InvalidOperationException("Undo lost the restored source control or retained a deleted drawing attachment.");
        using (var restoredSession = new WpfNativeMilCompilationSession())
        {
            restoredSession.Update(box, 240, 120);
            restoredSession.CompileFrame(8213, 1, 0, 1,
                flags: ProGPU.Backend.Native.NativeMilSceneBuildRequestFlags.HitTestIndex);
        }
        Call(box, "SelectAll");
        if (!((string)Get(selection, "Text")).Contains("second numbered source item", StringComparison.Ordinal))
            throw new InvalidOperationException("Native document view lost source undo.");
        object nextDocument = New(framework, "System.Windows.Documents.FlowDocument");
        Add(nextDocument, "Blocks", Paragraph("new document"));
        Set(box, "Document", nextDocument);
        if ((bool)Get(textView, "IsValid"))
            throw new InvalidOperationException("Detached document view retained valid source interaction.");
        if (Parent(button) != null || Parent(restoredButton) != null)
            throw new InvalidOperationException("Detached document retained its borrowed block control visual.");
        // Bottomless object layout must not silently enter the paginator's
        // line-only fragmentation path after releasing the editor view.
        Type paginatorSource = document.GetType().GetInterfaces().Single(type => type.Name == "IDocumentPaginatorSource");
        object paginator = paginatorSource.GetProperty("DocumentPaginator")!.GetValue(document)!;
        try
        {
            Call(paginator, "GetPage", 0);
            throw new InvalidOperationException("Pagination silently omitted the source block control.");
        }
        catch (TargetInvocationException error) when (error.InnerException is PlatformNotSupportedException unsupported &&
            unsupported.Message.Contains("native object fragmentation", StringComparison.Ordinal))
        {
        }
        if (Parent(restoredButton) != null)
            throw new InvalidOperationException("Rejected pagination attached the source control.");
        Console.WriteLine("Source rich document passed: native paragraph/section/list/object layout, original control and text view, hits, MIL export, selection, caret, resize, edit, undo and detachment.");
    }
}
