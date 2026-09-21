using System.Collections;
using System.Resources;
using System.Text;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class PortableWinFormsResXResourceTests
{
    [Fact]
    public void ResXWriterAndReaderRoundTripValuesAndMetadata()
    {
        using MemoryStream stream = new();
        using (ResXResourceWriter writer = new(stream))
        {
            writer.AddResource("Title", "LibreWPF");
            writer.AddResource("Bytes", new byte[] { 1, 2, 3, 4 });
            writer.AddMetadata("Generator", "Portable");
            writer.Generate();
        }

        stream.Position = 0;
        using ResXResourceReader reader = new(stream);
        Dictionary<string, object?> values = ReadEntries(reader.GetEnumerator());
        Dictionary<string, object?> metadata = ReadEntries(reader.GetMetadataEnumerator());

        Assert.Equal("LibreWPF", values["Title"]);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, Assert.IsType<byte[]>(values["Bytes"]));
        Assert.Equal("Portable", metadata["Generator"]);
    }

    [Fact]
    public void ResXReaderCanReturnDataNodesAndWriterCanPersistThem()
    {
        using MemoryStream first = new();
        using (ResXResourceWriter writer = new(first))
        {
            ResXDataNode node = new("Greeting", "Hello")
            {
                Comment = "Shown on startup"
            };
            writer.AddResource("Greeting", node);
            writer.Generate();
        }

        first.Position = 0;
        ResXDataNode readNode;
        using (ResXResourceReader reader = new(first) { UseResXDataNodes = true })
        {
            IDictionaryEnumerator enumerator = reader.GetEnumerator();
            Assert.True(enumerator.MoveNext());
            readNode = Assert.IsType<ResXDataNode>(enumerator.Value);
        }

        Assert.Equal("Greeting", readNode.Name);
        Assert.Equal("Hello", readNode.GetValue(null));
        Assert.Equal("Shown on startup", readNode.Comment);

        using MemoryStream second = new();
        using (ResXResourceWriter writer = new(second))
        {
            writer.AddResource(readNode.Name, readNode);
            writer.Generate();
        }

        second.Position = 0;
        using ResXResourceReader roundTripReader = new(second);
        Dictionary<string, object?> values = ReadEntries(roundTripReader.GetEnumerator());
        Assert.Equal("Hello", values["Greeting"]);
    }

    [Fact]
    public void ResXFileRefReadsRelativeTextFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "librewpf-resx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string textPath = Path.Combine(directory, "message.txt");
            File.WriteAllText(textPath, "From file", Encoding.UTF8);

            using MemoryStream stream = new();
            using (ResXResourceWriter writer = new(stream))
            {
                writer.AddResource("Message", new ResXFileRef("message.txt", typeof(string).AssemblyQualifiedName!, Encoding.UTF8));
                writer.Generate();
            }

            stream.Position = 0;
            using ResXResourceReader reader = new(stream)
            {
                BasePath = directory
            };
            Dictionary<string, object?> values = ReadEntries(reader.GetEnumerator());

            Assert.Equal("From file", values["Message"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ResXDataNodeFromReaderUsesReaderBasePathForFileRefs()
    {
        string directory = Path.Combine(Path.GetTempPath(), "librewpf-resx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string textPath = Path.Combine(directory, "resource.txt");
            File.WriteAllText(textPath, "Node file value", Encoding.UTF8);

            using MemoryStream stream = new();
            using (ResXResourceWriter writer = new(stream))
            {
                writer.AddResource("FromFile", new ResXFileRef("resource.txt", typeof(string).AssemblyQualifiedName!, Encoding.UTF8));
                writer.Generate();
            }

            stream.Position = 0;
            using ResXResourceReader reader = new(stream)
            {
                BasePath = directory,
                UseResXDataNodes = true
            };

            IDictionaryEnumerator enumerator = reader.GetEnumerator();
            Assert.True(enumerator.MoveNext());
            ResXDataNode node = Assert.IsType<ResXDataNode>(enumerator.Value);

            Assert.Equal("Node file value", node.GetValue(null));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Dictionary<string, object?> ReadEntries(IDictionaryEnumerator enumerator)
    {
        Dictionary<string, object?> values = new(StringComparer.Ordinal);
        while (enumerator.MoveNext())
        {
            values.Add((string)enumerator.Key, enumerator.Value);
        }

        return values;
    }
}
