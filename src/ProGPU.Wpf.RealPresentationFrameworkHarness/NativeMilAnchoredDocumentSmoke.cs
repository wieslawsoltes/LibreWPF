using System.Collections;
using System.Reflection;
using ProGPU.Wpf.Interop;

internal static class NativeMilAnchoredDocumentSmoke
{
    internal static void Run(Assembly framework, Assembly core)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object New(string name, params object[] args) => Activator.CreateInstance(
            framework.GetType(name, true)!, instance, null, args, null)!;
        object Get(object value, string name) => value.GetType().GetProperty(name, instance)!.GetValue(value)!;
        void Add(object owner, string collection, object item)
        {
            object list = Get(owner, collection);
            list.GetType().GetMethods(instance).Single(method => method.Name == "Add" &&
                method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType.IsInstanceOfType(item))
                .Invoke(list, [item]);
        }
        var method = framework.GetType("MS.Internal.Documents.PortableFlowDocumentLayout", true)!
            .GetMethod("CreateAnchored", BindingFlags.Static | BindingFlags.NonPublic)!;
        object mode = Enum.Parse(core.GetType("System.Windows.Media.TextFormattingMode", true)!, "Ideal");
        foreach (string kind in new[] { "Figure", "Floater" })
        {
            object document = New("System.Windows.Documents.FlowDocument");
            object parent = New("System.Windows.Documents.Paragraph");
            Add(parent, "Inlines", New("System.Windows.Documents.Run", "parent prefix"));
            object anchor = New("System.Windows.Documents." + kind);
            object child = New("System.Windows.Documents.Paragraph");
            Add(child, "Inlines", New("System.Windows.Documents.Run", "original anchored paragraph wraps across multiple native lines"));
            Add(anchor, "Blocks", child);
            object span = New("System.Windows.Documents.Span");
            Add(span, "Inlines", anchor);
            Add(parent, "Inlines", span);
            Add(document, "Blocks", parent);
            var collect = framework.GetType("MS.Internal.Documents.PortableDocumentAnchorSource", true)!
                .GetMethod("Collect", BindingFlags.Static | BindingFlags.NonPublic)!;
            var anchors = (IList)collect.Invoke(null, [parent])!;
            if (anchors.Count != 1 || !ReferenceEquals(Get(anchors[0]!, "Paragraph"), parent) ||
                !ReferenceEquals(Get(anchors[0]!, "Anchor"), anchor) ||
                (int)Get(anchors[0]!, "Start") != (int)Get(Get(anchor, "ElementStart"), "Offset") ||
                (int)Get(anchors[0]!, "End") != (int)Get(Get(anchor, "ElementEnd"), "Offset") ||
                ((IList)collect.Invoke(null, [child])!).Count != 0)
                throw new InvalidOperationException(kind + " anchor inventory lost source identity or entered its child text.");
            object Create(double width) => method.Invoke(null, [document, anchor, width, 1.0, mode])!;
            var auto = framework.GetType("MS.Internal.Documents.PortableDocumentAnchorLayout", true)!
                .GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!;
            object sibling = New("System.Windows.Documents." + (kind == "Figure" ? "Floater" : "Figure"));
            object siblingParagraph = New("System.Windows.Documents.Paragraph");
            Add(siblingParagraph, "Inlines", New("System.Windows.Documents.Run", "second original anchor"));
            Add(sibling, "Blocks", siblingParagraph);
            Add(span, "Inlines", sibling);
            void CheckAuto(bool fill)
            {
                using var batch = (IDisposable)auto.Invoke(null, [document, parent, 4096.0, 1.0, mode])!;
                var entries = (IList)Get(batch, "Entries");
                if (entries.Count != 2 || !ReferenceEquals(Get(Get(entries[0]!, "Source"), "Anchor"), anchor) ||
                    !ReferenceEquals(Get(Get(entries[1]!, "Source"), "Anchor"), sibling))
                    throw new InvalidOperationException("Batched anchor sizing lost source order or ownership.");
                PortableDocumentAnchorRectangle[] frames = [new() { Right = 4096, Bottom = 1000 },
                    new() { Top = 20, Right = 4096, Bottom = 1000 }];
                var place = batch.GetType().GetMethod("Place", instance)!;
                object placed = place.Invoke(batch, [frames, 256U])!;
                var children = (IList)Get(placed, "Children");
                if (children.Count != 2 || !ReferenceEquals(Get(children[0]!, "Child"), entries[0]) ||
                    !ReferenceEquals(Get(children[1]!, "Child"), entries[1]))
                    throw new InvalidOperationException("Native anchor placement lost its measured child generation.");
                object firstBounds = Get(children[0]!, "OuterBounds"), secondBounds = Get(children[1]!, "OuterBounds");
                if ((double)Get(secondBounds, "Top") < 20 ||
                    ((double)Get(firstBounds, "Left") < (double)Get(secondBounds, "Right") &&
                     (double)Get(secondBounds, "Left") < (double)Get(firstBounds, "Right") &&
                     (double)Get(firstBounds, "Top") < (double)Get(secondBounds, "Bottom") &&
                     (double)Get(secondBounds, "Top") < (double)Get(firstBounds, "Bottom")))
                    throw new InvalidOperationException("Native source anchor placement lost reference tops or collision clearance.");
                foreach (object item in children)
                {
                    object bounds = Get(item, "OuterBounds"), origin = Get(item, "ContentOrigin");
                    if ((double)Get(origin, "X") <= (double)Get(bounds, "Left") ||
                        (double)Get(origin, "Y") <= (double)Get(bounds, "Top"))
                        throw new InvalidOperationException("Anchor content origin lost source margin/border/padding.");
                }
                object sized = Get(entries[0]!, "Layout"), outer = Get(entries[0]!, "OuterSize");
                double outerWidth = (double)Get(outer, "Width");
                double contentWidth = (double)Get(Get(sized, "Size"), "Width");
                if (!(contentWidth > 0 && outerWidth > contentWidth) ||
                    (fill ? outerWidth != 4096 : outerWidth >= 4096) ||
                    (double)Get(outer, "Height") <= (double)Get(Get(sized, "Size"), "Height"))
                    throw new InvalidOperationException($"{kind} lost automatic source sizing or anchor insets: fill={fill}, outer={outerWidth}, content={contentWidth}.");
                foreach (object entry in (IList)Get(sized, "Lines"))
                    if (!ReferenceEquals(Get(entry, "Paragraph"), child))
                        throw new InvalidOperationException(kind + " automatic remeasurement replaced the source paragraph.");
                foreach (object entry in (IList)Get(Get(entries[1]!, "Layout"), "Lines"))
                    if (!ReferenceEquals(Get(entry, "Paragraph"), siblingParagraph))
                        throw new InvalidOperationException("Batched sizing mixed child source paragraphs.");
                object source = New("MS.Internal.Documents.PortableDocumentParagraphSource", parent, 1.0, 4096.0, Get(placed, "Exclusions"), batch);
                int anchorStart = (int)Get(Get(anchor, "ElementStart"), "Offset");
                int anchorEnd = (int)Get(Get(anchor, "ElementEnd"), "Offset");
                object unowned = New("MS.Internal.Documents.PortableDocumentParagraphSource", parent, 1.0, 4096.0);
                bool unownedRejected = false;
                try { unowned.GetType().GetMethod("GetTextRun")!.Invoke(unowned, [anchorStart]); }
                catch (TargetInvocationException error) when (error.InnerException is PlatformNotSupportedException)
                { unownedRejected = true; }
                if (!unownedRejected) throw new InvalidOperationException("Parent accepted an anchor without child ownership.");
                object hidden = source.GetType().GetMethod("GetTextRun")!.Invoke(source, [anchorStart])!;
                if (hidden.GetType().Name != "TextHidden" || (int)Get(hidden, "Length") != anchorEnd - anchorStart)
                    throw new InvalidOperationException("Parent shaping did not preserve the owned anchor symbol range.");
                object preceding = source.GetType().GetMethod("GetPrecedingText")!.Invoke(source,
                    [(int)Get(Get(sibling, "ElementEnd"), "Offset")])!;
                if ((int)Get(Get(Get(preceding, "Value"), "CharacterBufferRange"), "Length") != "parent prefix".Length)
                    throw new InvalidOperationException("Parent preceding text entered an anchored child paragraph.");
                object properties = New("MS.Internal.Text.TextProperties", parent, Get(parent, "StaticElementStart"), false, false, 1.0);
                object lineProperties = New("MS.Internal.Text.LineProperties", parent, document, properties, null!);
                object formatter = core.GetType("System.Windows.Media.TextFormatting.TextFormatter", true)!
                    .GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Single(m => m.Name == "FromCurrentDispatcher" &&
                        m.GetParameters().Length == 1).Invoke(null, [mode])!;
                object cache = Activator.CreateInstance(core.GetType("System.Windows.Media.TextFormatting.TextRunCache", true)!)!;
                var format = formatter.GetType().GetMethods(instance).Single(m => m.Name == "FormatLine" && m.GetParameters().Length == 6);
                using var parentLine = (IDisposable)format.Invoke(formatter,
                    [source, (int)Get(source, "Start"), 4096.0, lineProperties, null, cache])!;
                if ((string)parentLine.GetType().GetField("_text", instance)!.GetValue(parentLine)! != "parent prefix" ||
                    (int)Get(parentLine, "Length") != (int)Get(source, "End") - (int)Get(source, "Start"))
                    throw new InvalidOperationException("Native parent shaping duplicated child text or lost source symbols.");
            }
            CheckAuto(kind == "Floater" && Get(anchor, "HorizontalAlignment").ToString() == "Stretch");
            if (kind == "Floater")
            {
                var alignment = anchor.GetType().GetProperty("HorizontalAlignment")!;
                alignment.SetValue(anchor, Enum.Parse(alignment.PropertyType, "Stretch"));
                CheckAuto(true);
                alignment.SetValue(anchor, Enum.Parse(alignment.PropertyType, "Left"));
                CheckAuto(false);
            }
            using (var measured = (IDisposable)Create(4096))
            {
                double contentWidth = (double)Get(measured, "MeasuredContentWidth");
                if (!(contentWidth > 0 && contentWidth < 4096) ||
                    (double)Get(Get(measured, "Size"), "Width") != 4096)
                    throw new InvalidOperationException(kind + " confused measured content with the allocated constraint.");
            }
            object wide = Create(220);
            object narrow = Create(45);
            try
            {
                var wideLines = (IList)Get(wide, "Lines");
                var narrowLines = (IList)Get(narrow, "Lines");
                if (wideLines.Count == 0 || narrowLines.Count <= wideLines.Count)
                    throw new InvalidOperationException(kind + " did not reformat its actual child at the new native width.");
                int start = (int)Get(Get(child, "ElementStart"), "Offset");
                int end = (int)Get(Get(child, "ElementEnd"), "Offset");
                foreach (object entry in narrowLines)
                {
                    int position = (int)Get(entry, "Start");
                    object line = Get(entry, "Line");
                    if (!ReferenceEquals(Get(entry, "Paragraph"), child) || position < start ||
                        position + (int)Get(line, "Length") > end || (double)Get(line, "Height") <= 0)
                        throw new InvalidOperationException(kind + " lost original paragraph identity or source offsets.");
                }
                ((IDisposable)wide).Dispose();
                if ((double)Get(Get(narrowLines[0]!, "Line"), "WidthIncludingTrailingWhitespace") <= 0)
                    throw new InvalidOperationException(kind + " borrowed a disposed generation or manufactured empty content.");
                bool rejected = false;
                try { method.Invoke(null, [New("System.Windows.Documents.FlowDocument"), anchor, 100.0, 1.0, mode]); }
                catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { rejected = true; }
                if (!rejected) throw new InvalidOperationException("Anchored layout accepted another source document.");
            }
            finally { ((IDisposable)wide).Dispose(); ((IDisposable)narrow).Dispose(); }
            object embedded = New("System.Windows.Controls.Button");
            embedded.GetType().GetProperty("Content")!.SetValue(embedded, "anchor child");
            embedded.GetType().GetProperty("Width")!.SetValue(embedded, 80.0);
            embedded.GetType().GetProperty("Height")!.SetValue(embedded, 25.0);
            object container = New("System.Windows.Documents.InlineUIContainer", embedded);
            Add(child, "Inlines", container);
            object following = New("System.Windows.Documents.Paragraph");
            Add(following, "Inlines", New("System.Windows.Documents.Run", "following source block"));
            Add(document, "Blocks", following);
            foreach (var block in new[] { parent, following })
            {
                var margin = block.GetType().GetProperty("Margin")!;
                margin.SetValue(block, Activator.CreateInstance(margin.PropertyType, [0.0]));
            }
            var padding = document.GetType().GetProperty("PagePadding")!;
            padding.SetValue(document, Activator.CreateInstance(padding.PropertyType, [0.0]));
            var references = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(
                parent.GetType(), typeof(PortableDocumentAnchorRectangle[])))!;
            references.Add(parent, new PortableDocumentAnchorRectangle[] {
                new() { Right = 4096, Bottom = 1000 }, new() { Top = 20, Right = 4096, Bottom = 1000 } });
            using var documentLayout = (IDisposable)method.DeclaringType!.GetMethod("CreateWithAnchorFrames",
                BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [document, 4096.0, 1.0, mode, references])!;
            var owned = (IList)Get(documentLayout, "Anchors");
            if (owned.Count != 1) throw new InvalidOperationException("Document layout lost its anchor generation.");
            object ownedBatch = Get(owned[0]!, "Owner");
            var placedChildren = (IList)Get(Get(owned[0]!, "Placement"), "Children");
            double occupiedBottom = 0;
            foreach (object placement in placedChildren)
                occupiedBottom = Math.Max(occupiedBottom, (double)Get(Get(placement, "OuterBounds"), "Bottom"));
            var documentLines = (IList)Get(documentLayout, "Lines");
            var documentPositions = (PortableDocumentLinePosition[])Get(documentLayout, "Positions");
            if (!ReferenceEquals(Get(documentLines[documentLines.Count - 1]!, "Paragraph"), following) ||
                documentPositions[^1].Y < occupiedBottom)
                throw new InvalidOperationException("Native document arrangement advanced through an occupied anchor.");
            var hosted = (IList)Get(documentLayout, "HostedChildren");
            if (hosted.Count != 1 || !ReferenceEquals(Get(hosted[0]!, "Child"), embedded) ||
                !ReferenceEquals(Get(hosted[0]!, "Owner"), container))
                throw new InvalidOperationException("Anchored embedded control lost its actual source owner.");
            object rootBounds = documentLayout.GetType().GetMethod("HostedChildBounds", instance)!.Invoke(documentLayout, [0])!;
            object childLayout = Get(Get(placedChildren[0]!, "Child"), "Layout");
            object localBounds = childLayout.GetType().GetMethod("HostedChildBounds", instance)!.Invoke(childLayout, [0])!;
            object origin = Get(placedChildren[0]!, "ContentOrigin");
            var boxes = (PortableDocumentBox[])Get(documentLayout, "Boxes");
            var parentBox = boxes[(int)Get(owned[0]!, "BlockIndex")];
            if ((double)Get(rootBounds, "X") != parentBox.X + (double)Get(origin, "X") + (double)Get(localBounds, "X") ||
                (double)Get(rootBounds, "Y") != parentBox.Y + (double)Get(origin, "Y") + (double)Get(localBounds, "Y"))
                throw new InvalidOperationException("Anchored control bounds applied its content origin more than once.");
            object visual = Activator.CreateInstance(core.GetType("System.Windows.Media.DrawingVisual", true)!)!;
            using (var drawing = (IDisposable)visual.GetType().GetMethod("RenderOpen")!.Invoke(visual, null)!)
                framework.GetType("MS.Internal.Documents.PortableFlowDocumentVisual", true)!
                    .GetMethod("DrawLayout", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [drawing, documentLayout, 0]);
            var drawingTypes = new List<string>();
            int CountGlyphs(object drawing)
            {
                drawingTypes.Add(drawing.GetType().Name);
                if (drawing.GetType().Name == "GlyphRunDrawing") return 1;
                int count = 0;
                if (drawing.GetType().GetProperty("Children")?.GetValue(drawing) is IEnumerable contents)
                    foreach (object item in contents) count += CountGlyphs(item);
                return count;
            }
            int CountLineGlyphs(IList lines)
            {
                int count = 0;
                foreach (object entry in lines)
                {
                    object line = Get(entry, "Line");
                    if (line.GetType().GetMethod("GetIndexedGlyphRuns", instance)!.Invoke(line, null) is IEnumerable runs)
                        foreach (object run in runs) ++count;
                }
                return count;
            }
            int expectedGlyphs = CountLineGlyphs(documentLines);
            foreach (object placement in placedChildren)
                expectedGlyphs += CountLineGlyphs((IList)Get(Get(Get(placement, "Child"), "Layout"), "Lines"));
            int actualGlyphs = CountGlyphs(Get(visual, "Drawing"));
            if (expectedGlyphs == 0 || actualGlyphs != expectedGlyphs)
                throw new InvalidOperationException($"Document drawing omitted retained anchored child glyphs: actual={actualGlyphs}, expected={expectedGlyphs}, types={string.Join(",", drawingTypes)}, foreground={child.GetType().GetProperty("Foreground")!.GetValue(child)}.");
            documentLayout.Dispose();
            bool ownershipReleased = false;
            try { Get(ownedBatch, "Entries"); }
            catch (TargetInvocationException error) when (error.InnerException is ObjectDisposedException) { ownershipReleased = true; }
            if (!ownershipReleased) throw new InvalidOperationException("Document disposal retained its child anchor layouts.");
        }
        Console.WriteLine("native-mil: original Figure/Floater subtree measurement and source ownership passed");
    }
}
