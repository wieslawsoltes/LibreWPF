using System.Collections;
using System.Reflection;
using ProGPU.Wpf.Interop;

internal static class NativeMilSourceExcludedTextSmoke
{
    internal static void Run(Assembly framework, Assembly core)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object New(Assembly assembly, string name, params object?[] args) =>
            Activator.CreateInstance(assembly.GetType(name, true)!, flags, null, args, null)!;
        object Get(object value, string name) => value.GetType().GetProperty(name, flags)?.GetValue(value) ??
            value.GetType().GetField(name, flags)?.GetValue(value) ?? throw new InvalidOperationException("Missing source member: " + name);
        void Add(object owner, string property, object value)
        {
            object collection = Get(owner, property);
            collection.GetType().GetMethods(flags).Single(m => m.Name == "Add" && m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType.IsInstanceOfType(value)).Invoke(collection, [value]);
        }
        object document = New(framework, "System.Windows.Documents.FlowDocument");
        object paragraph = New(framework, "System.Windows.Documents.Paragraph");
        Add(paragraph, "Inlines", New(framework, "System.Windows.Documents.Run", "a a a a a a a a a a a a a a a a"));
        Add(document, "Blocks", paragraph);
        PortableTextExclusion[] rectangles = [new(30, 0, 70, 1000)];
        object request = New(core, "MS.Internal.TextFormatting.PortableTextExclusionRequest",
            new PortableTextExclusionOptions(256), new ReadOnlyMemory<PortableTextExclusion>(rectangles));
        rectangles[0] = new(0, 0, 100, 1000); // Request must own its immutable snapshot.
        object source = New(framework, "MS.Internal.Documents.PortableDocumentParagraphSource", paragraph, 1.0, 100.0, request);
        object properties = New(framework, "MS.Internal.Text.TextProperties", paragraph, Get(paragraph, "StaticElementStart"), false, false, 1.0);
        object lineProperties = New(framework, "MS.Internal.Text.LineProperties", paragraph, document, properties, null);
        object mode = Enum.Parse(core.GetType("System.Windows.Media.TextFormattingMode", true)!, "Ideal");
        object formatter = core.GetType("System.Windows.Media.TextFormatting.TextFormatter", true)!
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Single(m => m.Name == "FromCurrentDispatcher" &&
                m.GetParameters().Length == 1).Invoke(null, [mode])!;
        var format = formatter.GetType().GetMethods(flags).Single(m => m.Name == "FormatLine" && m.GetParameters().Length == 6);
        object cache = New(core, "System.Windows.Media.TextFormatting.TextRunCache");
        int first = (int)Get(source, "Start");
        int position = first;
        var retained = new List<IDisposable>();
        object? continuation = null;
        try
        {
            for (int index = 0; index < 2; ++index)
            {
                object line = format.Invoke(formatter, [source, position, 100.0, lineProperties, continuation, cache])!;
                retained.Add((IDisposable)line);
                object fragment = Get(line, "Fragment");
                if ((int)Get(fragment, "RowIndex") != 0 || (double)Get(fragment, "Top") != 0 ||
                    (double)Get(line, "Start") != (index == 0 ? 0 : 70))
                    throw new InvalidOperationException("Source TextLine lost native same-row fragment placement.");
                object hit = New(core, "System.Windows.Media.TextFormatting.CharacterHit", position, 0);
                double distance = (double)line.GetType().GetMethod("GetDistanceFromCharacterHit", flags)!.Invoke(line, [hit])!;
                if (Math.Abs(distance - (index == 0 ? 0 : 70)) > 0.01)
                    throw new InvalidOperationException("Source caret applied the native fragment X twice.");
                object returned = line.GetType().GetMethod("GetCharacterHitFromDistance", flags)!.Invoke(line, [distance])!;
                double roundtrip = (double)line.GetType().GetMethod("GetDistanceFromCharacterHit", flags)!.Invoke(line, [returned])!;
                if (Math.Abs(roundtrip - distance) > 0.01) throw new InvalidOperationException("Source fragment caret round trip changed its frame.");
                object?[] movement = [position, false, true, distance, 0, false, 0];
                if (!(bool)line.GetType().GetMethod("TryMoveFragmentCaret", flags)!.Invoke(line, movement)! ||
                    (int)movement[6]! < (index == 0 ? 2 : 1) || (int)movement[4]! <= position)
                    throw new InvalidOperationException("Source native Down moved into a same-row fragment or lost source offsets.");
                var bounds = (IList)line.GetType().GetMethod("GetTextBounds", flags)!.Invoke(line, [position, (int)Get(line, "Length")])!;
                if (bounds.Count == 0 || (double)Get(Get(bounds[0]!, "Rectangle"), "X") < (index == 0 ? 0 : 70))
                    throw new InvalidOperationException("Source fragment selection lost its native X frame.");
                position += (int)Get(line, "Length");
                var next = line.GetType().GetMethod("GetTextLineBreak", flags)!.Invoke(line, null);
                (continuation as IDisposable)?.Dispose(); continuation = next;
            }
        }
        finally { (continuation as IDisposable)?.Dispose(); foreach (var line in retained) line.Dispose(); }
        // Consume these real source TextLines in the shared native block layout,
        // with clearance and a following ordinary paragraph. No source Y repair.
        object following = New(framework, "System.Windows.Documents.Paragraph");
        Add(following, "Inlines", New(framework, "System.Windows.Documents.Run", "after"));
        Add(document, "Blocks", following);
        foreach (object block in new[] { paragraph, following })
        {
            var margin = block.GetType().GetProperty("Margin")!;
            margin.SetValue(block, Activator.CreateInstance(margin.PropertyType, [0.0]));
        }
        var padding = document.GetType().GetProperty("PagePadding")!;
        padding.SetValue(document, Activator.CreateInstance(padding.PropertyType, [0.0]));
        object clearedRequest = New(core, "MS.Internal.TextFormatting.PortableTextExclusionRequest",
            new PortableTextExclusionOptions(256), new ReadOnlyMemory<PortableTextExclusion>(
                [new(0, 0, 100, 20), new(30, 20, 70, 1000)]));
        var map = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(paragraph.GetType(), clearedRequest.GetType()))!;
        map.Add(paragraph, clearedRequest);
        object layout = framework.GetType("MS.Internal.Documents.PortableFlowDocumentLayout", true)!
            .GetMethod("CreateWithExclusions", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [document, 100.0, 1.0, mode, map])!;
        try
        {
            var entries = (IList)Get(layout, "Lines");
            var positions = (Array)Get(layout, "Positions");
            if (!(bool)Get(layout, "HasPositionedParagraphs") || entries.Count < 3 ||
                (double)Get(positions.GetValue(0)!, "Y") != 20 || (double)Get(positions.GetValue(1)!, "Y") != 20 ||
                (double)Get(positions.GetValue(1)!, "X") != 0 ||
                (double)Get(Get(entries[1]!, "Line"), "Start") != 70)
                throw new InvalidOperationException("Source document stacked fragments or duplicated their X frame.");
            object last = entries[entries.Count - 1]!;
            double expectedTop = (double)Get(Get(entries[0]!, "Line"), "FragmentContentHeight");
            if (!ReferenceEquals(Get(last, "Paragraph"), following) ||
                (double)Get(positions.GetValue(entries.Count - 1)!, "Y") != expectedTop)
                throw new InvalidOperationException("Following source paragraph did not use the native fragment extent.");
        }
        finally { ((IDisposable)layout).Dispose(); }
        Console.WriteLine("native-mil: source excluded TextLines and native document placement preserve shared frames");
    }
}
