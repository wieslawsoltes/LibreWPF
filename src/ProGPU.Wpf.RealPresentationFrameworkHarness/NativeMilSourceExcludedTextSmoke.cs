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
        object Get(object value, string name) => value.GetType().GetProperty(name, flags)!.GetValue(value)!;
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
                var bounds = (IList)line.GetType().GetMethod("GetTextBounds", flags)!.Invoke(line, [position, (int)Get(line, "Length")])!;
                if (bounds.Count == 0 || (double)Get(Get(bounds[0]!, "Rectangle"), "X") < (index == 0 ? 0 : 70))
                    throw new InvalidOperationException("Source fragment selection lost its native X frame.");
                position += (int)Get(line, "Length");
                var next = line.GetType().GetMethod("GetTextLineBreak", flags)!.Invoke(line, null);
                (continuation as IDisposable)?.Dispose(); continuation = next;
            }
        }
        finally { (continuation as IDisposable)?.Dispose(); foreach (var line in retained) line.Dispose(); }
        Console.WriteLine("native-mil: source excluded TextLines preserve same-row frames, caret and selection");
    }
}
