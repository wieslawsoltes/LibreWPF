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
            Add(parent, "Inlines", anchor);
            Add(document, "Blocks", parent);
            object Create(double width) => method.Invoke(null, [document, anchor, width, 1.0, mode])!;
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
