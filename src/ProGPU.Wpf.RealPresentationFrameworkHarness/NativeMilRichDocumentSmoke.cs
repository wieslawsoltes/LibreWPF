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
        Set(box, "Width", 240.0); Set(box, "Height", 640.0);
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
        object table = New(framework, "System.Windows.Documents.Table");
        Set(table, "CellSpacing", 2.0);
        object[] tableColumns = new object[2];
        for (int i = 0; i < tableColumns.Length; ++i)
        {
            tableColumns[i] = New(framework, "System.Windows.Documents.TableColumn");
            var width = tableColumns[i].GetType().GetProperty("Width")!;
            width.SetValue(tableColumns[i], Activator.CreateInstance(width.PropertyType, [i == 0 ? 80.0 : 120.0]));
            Add(table, "Columns", tableColumns[i]);
        }
        object group = New(framework, "System.Windows.Documents.TableRowGroup");
        object[] sourceCells = new object[4];
        for (int row = 0; row < 2; ++row)
        {
            object tableRow = New(framework, "System.Windows.Documents.TableRow");
            for (int column = 0; column < 2; ++column)
            {
                object cell = New(framework, "System.Windows.Documents.TableCell");
                Set(cell, "Padding", New(framework, "System.Windows.Thickness", 2.0));
                Set(cell, "Background", core.GetType("System.Windows.Media.Brushes", true)!.GetProperty("LightYellow")!.GetValue(null)!);
                Add(cell, "Blocks", Paragraph(row == 0 ? column == 0 ? "alpha" : "right first" : column == 0 ? "left second" : "right second"));
                if (row == 0 && column == 0) Add(cell, "Blocks", Paragraph("beta"));
                sourceCells[row * 2 + column] = cell;
                Add(tableRow, "Cells", cell);
            }
            Add(group, "Rows", tableRow);
        }
        Add(table, "RowGroups", group);
        Add(document, "Blocks", table);
        object container = Get(document, "TextContainer");
        object selection = Get(box, "Selection");
        Call(box, "ApplyTemplate");
        object scope = Get(box, "RenderScope");
        if (scope.GetType().FullName != "MS.Internal.Documents.FlowDocumentView" ||
            !ReferenceEquals(Get(scope, "Document"), document))
            throw new InvalidOperationException("Rich editor did not select the shared source document view.");
        object Measure()
        {
            Call(box, "Measure", New(windowsBase, "System.Windows.Size", 240.0, 640.0));
            Call(box, "Arrange", New(windowsBase, "System.Windows.Rect", 0.0, 0.0, 240.0, 640.0));
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
        int previousSource = -1;
        foreach (object record in lines)
        {
            int start = (int)Get(record, "Start");
            if (start <= previousSource) throw new InvalidOperationException("Document lines lost source symbol order.");
            previousSource = start;
        }
        for (int i = 1; i < lines.Count; ++i)
        {
            if (ReferenceEquals(Get(lines[i - 1]!, "Paragraph"), Get(lines[i]!, "Paragraph")) &&
                (double)Get(positions[i]!, "Y") <= (double)Get(positions[i - 1]!, "Y"))
                throw new InvalidOperationException("Native paragraph lines were flattened onto the same line.");
        }
        object firstPosition = positions[0]!;
        object point = New(windowsBase, "System.Windows.Point",
            (double)Get(firstPosition, "X") + 2, (double)Get(firstPosition, "Y") + 2);
        object pointer = Call(textView, "GetTextPositionFromPoint", point, true)!;
        if (!ReferenceEquals(Get(pointer, "TextContainer"), container))
            throw new InvalidOperationException("Native document hit returned a copied document position.");
        if (new WpfNativeMilSceneCompiler().BuildBatch(box, 240, 640).GlyphRunFonts is not { Count: > 0 })
            throw new InvalidOperationException("Rich document source drawing did not reach native MIL.");
        using (var session = new WpfNativeMilCompilationSession())
        {
            session.Update(box, 240, 640);
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
        object CellBox(object generation, int cellIndex)
        {
            object record = ((IList)Get(generation, "Cells"))[cellIndex]!;
            return ((IList)Get(generation, "Boxes"))[(int)(uint)Get(record, "BlockIndex")]!;
        }
        if (((IList)Get(layout, "Cells")).Count != 4 || ((IList)Get(layout, "Rows")).Count != 2)
            throw new InvalidOperationException("Native table export lost actual source rows/cells.");
        var cellLines = new List<int>[4];
        for (int cell = 0; cell < 4; ++cell)
        {
            cellLines[cell] = [];
            for (int i = 0; i < lines.Count; ++i)
                if (ReferenceEquals(Get(Get(lines[i]!, "Paragraph"), "Parent"), sourceCells[cell])) cellLines[cell].Add(i);
            if (cellLines[cell].Count == 0)
                throw new InvalidOperationException("Table cell text was replaced by a copied or empty paragraph.");
            object nativeCell = CellBox(layout, cell);
            if ((double)Get(nativeCell, "Width") != (cell % 2 == 0 ? 76.0 : 116.0))
                throw new InvalidOperationException("Cell text did not consume its native column constraint.");
            object origin = positions[cellLines[cell][0]]!;
            object cellHit = Call(textView, "GetTextPositionFromPoint",
                New(windowsBase, "System.Windows.Point", (double)Get(origin, "X") + 1, (double)Get(origin, "Y") + 1), true)!;
            if (!ReferenceEquals(Get(cellHit, "TextContainer"), container) ||
                (int)Get(cellHit, "Offset") < (int)Get(Get(sourceCells[cell], "ContentStart"), "Offset") ||
                (int)Get(cellHit, "Offset") >= (int)Get(Get(sourceCells[cell], "ContentEnd"), "Offset"))
                throw new InvalidOperationException("Table point lookup selected a different source cell.");
        }
        if (cellLines[0].Count != 2 ||
            (double)Get(positions[cellLines[1][0]]!, "Y") >= (double)Get(positions[cellLines[0][1]]!, "Y") ||
            (double)Get(CellBox(layout, 0), "Height") != (double)Get(CellBox(layout, 1), "Height"))
            throw new InvalidOperationException("Row height or nonmonotonic source line order was lost.");
        object CellPosition(int cell) => Call(textView, "GetTextPositionFromPoint",
            New(windowsBase, "System.Windows.Point", (double)Get(positions[cellLines[cell][0]]!, "X") + 1,
                (double)Get(positions[cellLines[cell][0]]!, "Y") + 1), true)!;
        void MoveToCell(int from, int count, int to)
        {
            object?[] args = [CellPosition(from), double.NaN, count, null, null];
            object moved = lineMove.Invoke(textView, args)!;
            if ((int)args[4]! != count || (int)Get(moved, "Offset") < (int)Get(Get(sourceCells[to], "ContentStart"), "Offset") ||
                (int)Get(moved, "Offset") >= (int)Get(Get(sourceCells[to], "ContentEnd"), "Offset"))
                throw new InvalidOperationException("Vertical table navigation entered an adjacent horizontal cell.");
        }
        MoveToCell(0, 2, 2); MoveToCell(1, 1, 3); MoveToCell(3, -1, 1); MoveToCell(2, -1, 0);
        object backward = Enum.Parse(directionType, "Backward");
        object backwardCellStart = Call(CellPosition(1), "GetPositionAtOffset", 0, backward)!;
        object backwardCaret = Call(textView, "GetRectangleFromTextPosition", backwardCellStart)!;
        if ((double)Get(backwardCaret, "Y") != (double)Get(positions[cellLines[1][0]]!, "Y"))
            throw new InvalidOperationException("Backward affinity at a cell start selected the preceding cell's last line.");
        var tableSelection = (IList)Call(textView, "GetDocumentRectangles",
            Get(sourceCells[0], "ContentStart"), Get(sourceCells[1], "ContentEnd"), false)!;
        if (tableSelection.Count < 3)
            throw new InvalidOperationException("Table selection lost source cell text rectangles.");
        Set(box, "Height", 120.0);
        object narrowViewport = Measure();
        Type scrollInfo = scope.GetType().GetInterfaces().Single(type => type.Name == "IScrollInfo");
        if (scrollInfo.GetProperty("ScrollOwner")!.GetValue(scope) == null)
            throw new InvalidOperationException("Rich document has no source scroll owner.");
        scrollInfo.GetMethod("SetVerticalOffset")!.Invoke(scope, [(double)Get(CellBox(narrowViewport, 1), "Y") - 8.0]);
        object scrolled = Measure();
        double scrollY = (double)Get(Get(scope, "PortableScrollOffset"), "Y");
        object scrolledCell = CellBox(scrolled, 1);
        object scrolledHit = Call(textView, "GetTextPositionFromPoint",
            New(windowsBase, "System.Windows.Point", (double)Get(scrolledCell, "X") + 1,
                (double)Get(scrolledCell, "Y") + 1 - scrollY), true)!;
        if (scrollY <= 0 || (int)Get(scrolledHit, "Offset") < (int)Get(Get(sourceCells[1], "ContentStart"), "Offset") ||
            (int)Get(scrolledHit, "Offset") >= (int)Get(Get(sourceCells[1], "ContentEnd"), "Offset"))
            throw new InvalidOperationException("Scrolled table point input lost source cell coordinates.");
        object scrolledCaret = Call(textView, "GetRectangleFromTextPosition", scrolledHit)!;
        if (Math.Abs((double)Get(scrolledCaret, "Y") + scrollY - (double)Get(scrolledCell, "Y")) > 1e-6)
            throw new InvalidOperationException("Table caret applied its viewport translation more than once.");
        Set(box, "Height", 640.0);
        Measure();
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
        var firstColumnWidth = tableColumns[0].GetType().GetProperty("Width")!;
        firstColumnWidth.SetValue(tableColumns[0], Activator.CreateInstance(firstColumnWidth.PropertyType, [90.0]));
        if ((bool)Get(textView, "IsValid"))
            throw new InvalidOperationException("Table column change left stale cell interaction valid.");
        double previousRightCellX = (double)Get(CellBox(resized, 1), "X");
        object widened = Measure();
        if ((double)Get(CellBox(widened, 0), "Width") != 86.0 ||
            (double)Get(CellBox(widened, 1), "X") - previousRightCellX != 10.0)
            throw new InvalidOperationException("Native table reflow did not move the actual source cells.");
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
            restoredSession.Update(box, 240, 640);
            restoredSession.CompileFrame(8213, 1, 0, 1,
                flags: ProGPU.Backend.Native.NativeMilSceneBuildRequestFlags.HitTestIndex);
        }
        Call(box, "SelectAll");
        if (!((string)Get(selection, "Text")).Contains("second numbered source item", StringComparison.Ordinal))
            throw new InvalidOperationException("Native document view lost source undo.");
        if (((IList)Get(restoredLayout, "Cells")).Count != 4 ||
            (double)Get(CellBox(restoredLayout, 0), "Width") != 86.0 ||
            !((string)Get(selection, "Text")).Contains("right second", StringComparison.Ordinal))
            throw new InvalidOperationException("Source undo lost the actual table and its text.");
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
        Call(Get(document, "Blocks"), "Remove", restoredContainer);
        try
        {
            Call(paginator, "GetPage", 0);
            throw new InvalidOperationException("Pagination silently treated table rows as vertical paragraphs.");
        }
        catch (TargetInvocationException error) when (error.InnerException is PlatformNotSupportedException unsupported &&
            unsupported.Message.Contains("native row/cell fragmentation", StringComparison.Ordinal))
        {
        }
        Console.WriteLine("Source rich document passed: native paragraph/list/object/table layout, original source ownership, cell hits/navigation, scrolling, MIL export, selection, resize, edit/undo, detachment and explicit pagination rejection.");
    }
}
