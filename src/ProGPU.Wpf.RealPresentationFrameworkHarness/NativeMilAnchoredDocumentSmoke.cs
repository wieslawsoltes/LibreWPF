using System.Collections;
using System.Reflection;

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
        }
        Console.WriteLine("native-mil: original Figure/Floater subtree measurement and source ownership passed");
    }
}
