using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace System.Resources
{
    public sealed class ResXFileRef
    {
        public ResXFileRef(string fileName, string typeName)
            : this(fileName, typeName, null)
        {
        }

        public ResXFileRef(string fileName, string typeName, Encoding? textFileEncoding)
        {
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
            TextFileEncoding = textFileEncoding;
        }

        public string FileName { get; }

        public Encoding? TextFileEncoding { get; }

        public string TypeName { get; }

        public override string ToString()
        {
            return TextFileEncoding is null
                ? FileName + ";" + TypeName
                : FileName + ";" + TypeName + ";" + TextFileEncoding.WebName;
        }
    }

    public sealed class ResXDataNode
    {
        private readonly string? _basePath;

        public ResXDataNode(string name, object? value)
            : this(name, value, null)
        {
        }

        public ResXDataNode(string name, object? value, Func<Type, string>? typeNameConverter)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Value = value;
            TypeNameConverter = typeNameConverter;
        }

        public ResXDataNode(string name, ResXFileRef fileRef)
            : this(name, fileRef, null)
        {
        }

        internal ResXDataNode(string name, ResXFileRef fileRef, string? basePath)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            FileRef = fileRef ?? throw new ArgumentNullException(nameof(fileRef));
            _basePath = basePath;
        }

        public string? Comment { get; set; }

        public ResXFileRef? FileRef { get; }

        public string Name { get; }

        internal object? Value { get; }

        internal Func<Type, string>? TypeNameConverter { get; }

        public ResXFileRef? GetFileRef()
        {
            return FileRef;
        }

        public object? GetValue(ITypeResolutionService? typeResolver)
        {
            return FileRef is null ? Value : ResXResourceValueCodec.ReadFileRef(FileRef, _basePath);
        }

        public object? GetValue(ITypeResolutionService? typeResolver, string? basePath)
        {
            return FileRef is null ? Value : ResXResourceValueCodec.ReadFileRef(FileRef, basePath ?? _basePath);
        }
    }

    public sealed class ResXResourceReader : IResourceReader
    {
        private readonly string? _fileName;
        private readonly Stream? _stream;
        private readonly TextReader? _reader;
        private List<DictionaryEntry>? _data;
        private List<DictionaryEntry>? _metadata;

        public ResXResourceReader(string fileName)
        {
            _fileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        }

        public ResXResourceReader(Stream stream)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        }

        public ResXResourceReader(TextReader reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        public string? BasePath { get; set; }

        public bool UseResXDataNodes { get; set; }

        public void Close()
        {
            Dispose();
        }

        public void Dispose()
        {
        }

        public IDictionaryEnumerator GetEnumerator()
        {
            EnsureLoaded();
            return new ResXDictionaryEnumerator(_data!);
        }

        public IDictionaryEnumerator GetMetadataEnumerator()
        {
            EnsureLoaded();
            return new ResXDictionaryEnumerator(_metadata!);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private void EnsureLoaded()
        {
            if (_data is not null)
                return;

            _data = new List<DictionaryEntry>();
            _metadata = new List<DictionaryEntry>();

            XDocument document = LoadDocument();
            XElement? root = document.Root;
            if (root is null)
                return;

            foreach (XElement element in root.Elements())
            {
                if (element.Name.LocalName is not ("data" or "metadata"))
                    continue;

                string? name = (string?)element.Attribute("name");
                if (string.IsNullOrEmpty(name))
                    continue;

                object? value = ReadElementValue(element, name);
                if (element.Name.LocalName == "metadata")
                    _metadata.Add(new DictionaryEntry(name, value));
                else
                    _data.Add(new DictionaryEntry(name, value));
            }
        }

        private XDocument LoadDocument()
        {
            if (_fileName is not null)
            {
                using FileStream file = File.OpenRead(_fileName);
                return XDocument.Load(file, LoadOptions.PreserveWhitespace);
            }

            if (_stream is not null)
                return XDocument.Load(_stream, LoadOptions.PreserveWhitespace);

            return XDocument.Load(_reader!, LoadOptions.PreserveWhitespace);
        }

        private object? ReadElementValue(XElement element, string name)
        {
            string valueText = element.Element("value")?.Value ?? string.Empty;
            string? typeName = (string?)element.Attribute("type");
            string? mimeType = (string?)element.Attribute("mimetype");
            string? comment = element.Element("comment")?.Value;

            if (UseResXDataNodes)
            {
                ResXFileRef? fileRef = ResXResourceValueCodec.TryReadFileRef(valueText, typeName);
                ResXDataNode node = fileRef is not null
                    ? new ResXDataNode(name, fileRef, BasePath)
                    : new ResXDataNode(name, ResXResourceValueCodec.ReadValue(valueText, typeName, mimeType, BasePath));
                node.Comment = comment;
                return node;
            }

            return ResXResourceValueCodec.ReadValue(valueText, typeName, mimeType, BasePath);
        }
    }

    public sealed class ResXResourceWriter : IResourceWriter
    {
        private readonly string? _fileName;
        private readonly Stream? _stream;
        private readonly TextWriter? _writer;
        private readonly Func<Type, string>? _typeNameConverter;
        private readonly List<ResXResourceEntry> _data = new();
        private readonly List<ResXResourceEntry> _metadata = new();
        private bool _generated;

        public ResXResourceWriter(string fileName)
        {
            _fileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        }

        public ResXResourceWriter(Stream stream)
            : this(stream, null)
        {
        }

        public ResXResourceWriter(Stream stream, Func<Type, string>? typeNameConverter)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _typeNameConverter = typeNameConverter;
        }

        public ResXResourceWriter(TextWriter writer)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        public string? BasePath { get; set; }

        public void AddMetadata(string name, object? value)
        {
            AddEntry(_metadata, name, value);
        }

        public void AddMetadata(string name, string? value)
        {
            AddEntry(_metadata, name, value);
        }

        public void AddMetadata(string name, byte[]? value)
        {
            AddEntry(_metadata, name, value);
        }

        public void AddResource(string name, string? value)
        {
            AddEntry(_data, name, value);
        }

        public void AddResource(string name, object? value)
        {
            AddEntry(_data, name, value);
        }

        public void AddResource(string name, byte[]? value)
        {
            AddEntry(_data, name, value);
        }

        public void AddResource(ResXDataNode node)
        {
            ArgumentNullException.ThrowIfNull(node);
            AddEntry(_data, node.Name, node);
        }

        public void Close()
        {
            Generate();
        }

        public void Dispose()
        {
            Generate();
        }

        public void Generate()
        {
            if (_generated)
                return;

            _generated = true;
            XDocument document = CreateDocument();

            if (_fileName is not null)
            {
                using FileStream file = File.Create(_fileName);
                document.Save(file);
                return;
            }

            if (_stream is not null)
            {
                document.Save(_stream);
                return;
            }

            document.Save(_writer!);
        }

        private void AddEntry(List<ResXResourceEntry> entries, string name, object? value)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Resource name must not be empty.", nameof(name));

            if (value is ResXDataNode node)
            {
                entries.Add(new ResXResourceEntry(name, node.FileRef ?? node.Value, node.Comment, node.TypeNameConverter));
                return;
            }

            entries.Add(new ResXResourceEntry(name, value, null, null));
        }

        private XDocument CreateDocument()
        {
            XElement root = new("root");
            AddResHeader(root, "resmimetype", "text/microsoft-resx");
            AddResHeader(root, "version", "2.0");
            AddResHeader(root, "reader", typeof(ResXResourceReader).FullName + ", System.Windows.Forms");
            AddResHeader(root, "writer", typeof(ResXResourceWriter).FullName + ", System.Windows.Forms");

            foreach (ResXResourceEntry entry in _metadata)
                root.Add(CreateValueElement("metadata", entry));
            foreach (ResXResourceEntry entry in _data)
                root.Add(CreateValueElement("data", entry));

            return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        }

        private static void AddResHeader(XElement root, string name, string value)
        {
            root.Add(new XElement("resheader", new XAttribute("name", name), new XElement("value", value)));
        }

        private XElement CreateValueElement(string elementName, ResXResourceEntry entry)
        {
            XElement element = new(elementName, new XAttribute("name", entry.Name));
            object? value = entry.Value;

            if (value is ResXFileRef fileRef)
            {
                element.SetAttributeValue("type", typeof(ResXFileRef).FullName + ", System.Windows.Forms");
                element.Add(new XElement("value", fileRef.ToString()));
            }
            else if (value is byte[] bytes)
            {
                element.SetAttributeValue("type", GetConvertedTypeName(typeof(byte[]), entry.TypeNameConverter));
                element.Add(new XElement("value", Convert.ToBase64String(bytes, Base64FormattingOptions.InsertLineBreaks)));
            }
            else
            {
                string? stringValue = ResXResourceValueCodec.WriteValue(value, out Type? valueType);
                if (valueType is not null && valueType != typeof(string))
                    element.SetAttributeValue("type", GetConvertedTypeName(valueType, entry.TypeNameConverter));
                element.SetAttributeValue(XNamespace.Xml + "space", "preserve");
                element.Add(new XElement("value", stringValue ?? string.Empty));
            }

            if (!string.IsNullOrEmpty(entry.Comment))
                element.Add(new XElement("comment", entry.Comment));

            return element;
        }

        private string GetConvertedTypeName(Type type, Func<Type, string>? entryTypeNameConverter)
        {
            return entryTypeNameConverter?.Invoke(type)
                ?? _typeNameConverter?.Invoke(type)
                ?? type.AssemblyQualifiedName
                ?? type.FullName
                ?? type.Name;
        }
    }

    internal static class ResXResourceValueCodec
    {
        public static object? ReadValue(string valueText, string? typeName, string? mimeType, string? basePath)
        {
            if (IsResXFileRef(typeName))
                return ReadFileRef(ParseFileRef(valueText), basePath);

            if (IsByteArray(typeName) || IsBinaryMimeType(mimeType))
                return Convert.FromBase64String(RemoveWhitespace(valueText));

            if (string.IsNullOrEmpty(typeName) || IsString(typeName))
                return valueText;

            if (TryReadPrimitive(valueText, typeName, out object? primitive))
                return primitive;

            return valueText;
        }

        public static ResXFileRef? TryReadFileRef(string valueText, string? typeName)
        {
            return IsResXFileRef(typeName) ? ParseFileRef(valueText) : null;
        }

        public static object? ReadFileRef(ResXFileRef fileRef, string? basePath)
        {
            string fileName = ResolveFileName(fileRef.FileName, basePath);
            if (IsString(fileRef.TypeName))
            {
                Encoding encoding = fileRef.TextFileEncoding ?? Encoding.UTF8;
                return File.Exists(fileName) ? File.ReadAllText(fileName, encoding) : string.Empty;
            }

            if (IsByteArray(fileRef.TypeName))
                return File.Exists(fileName) ? File.ReadAllBytes(fileName) : Array.Empty<byte>();

            if (IsBitmap(fileRef.TypeName) || IsImage(fileRef.TypeName))
                return File.Exists(fileName) ? new global::System.Drawing.Bitmap(fileName) : null;

            if (IsIcon(fileRef.TypeName))
                return File.Exists(fileName) ? new global::System.Drawing.Icon(fileName) : null;

            return fileRef;
        }

        public static string? WriteValue(object? value, out Type? valueType)
        {
            valueType = value?.GetType();
            if (value is null)
            {
                valueType = typeof(string);
                return string.Empty;
            }

            if (value is string text)
                return text;

            if (value is IFormattable formattable)
                return formattable.ToString(null, CultureInfo.InvariantCulture);

            TypeConverter converter = TypeDescriptor.GetConverter(value);
            if (converter.CanConvertTo(typeof(string)))
                return converter.ConvertToInvariantString(value);

            valueType = typeof(string);
            return value.ToString();
        }

        private static ResXFileRef ParseFileRef(string valueText)
        {
            string[] parts = valueText.Split(';');
            string fileName = parts.Length > 0 ? parts[0] : string.Empty;
            string typeName = parts.Length > 1 ? parts[1] : typeof(string).AssemblyQualifiedName!;
            Encoding? encoding = null;
            if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]))
            {
                try
                {
                    encoding = Encoding.GetEncoding(parts[2]);
                }
                catch
                {
                    encoding = Encoding.UTF8;
                }
            }

            return new ResXFileRef(fileName, typeName, encoding);
        }

        private static bool TryReadPrimitive(string valueText, string typeName, out object? value)
        {
            Type? primitive = ResolvePrimitiveType(typeName);
            if (primitive is null)
            {
                value = null;
                return false;
            }

            if (primitive == typeof(int))
                value = int.Parse(valueText, CultureInfo.InvariantCulture);
            else if (primitive == typeof(long))
                value = long.Parse(valueText, CultureInfo.InvariantCulture);
            else if (primitive == typeof(short))
                value = short.Parse(valueText, CultureInfo.InvariantCulture);
            else if (primitive == typeof(byte))
                value = byte.Parse(valueText, CultureInfo.InvariantCulture);
            else if (primitive == typeof(bool))
                value = bool.Parse(valueText);
            else if (primitive == typeof(float))
                value = float.Parse(valueText, CultureInfo.InvariantCulture);
            else if (primitive == typeof(double))
                value = double.Parse(valueText, CultureInfo.InvariantCulture);
            else if (primitive == typeof(decimal))
                value = decimal.Parse(valueText, CultureInfo.InvariantCulture);
            else
                value = valueText;

            return true;
        }

        private static Type? ResolvePrimitiveType(string typeName)
        {
            string normalized = NormalizeTypeName(typeName);
            return normalized switch
            {
                "System.Int32" => typeof(int),
                "System.Int64" => typeof(long),
                "System.Int16" => typeof(short),
                "System.Byte" => typeof(byte),
                "System.Boolean" => typeof(bool),
                "System.Single" => typeof(float),
                "System.Double" => typeof(double),
                "System.Decimal" => typeof(decimal),
                _ => null
            };
        }

        private static string ResolveFileName(string fileName, string? basePath)
        {
            if (Path.IsPathRooted(fileName) || string.IsNullOrEmpty(basePath))
                return fileName;

            return Path.Combine(basePath, fileName);
        }

        private static bool IsBinaryMimeType(string? mimeType)
        {
            return string.Equals(mimeType, "application/x-microsoft.net.object.binary.base64", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mimeType, "application/x-microsoft.net.object.bytearray.base64", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsResXFileRef(string? typeName)
        {
            return NormalizeTypeName(typeName) == typeof(ResXFileRef).FullName;
        }

        private static bool IsString(string? typeName)
        {
            return NormalizeTypeName(typeName) == typeof(string).FullName;
        }

        private static bool IsByteArray(string? typeName)
        {
            return NormalizeTypeName(typeName) == typeof(byte[]).FullName;
        }

        private static bool IsBitmap(string? typeName)
        {
            return NormalizeTypeName(typeName) == "System.Drawing.Bitmap";
        }

        private static bool IsImage(string? typeName)
        {
            return NormalizeTypeName(typeName) == "System.Drawing.Image";
        }

        private static bool IsIcon(string? typeName)
        {
            return NormalizeTypeName(typeName) == "System.Drawing.Icon";
        }

        private static string NormalizeTypeName(string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return string.Empty;

            int separator = typeName.IndexOf(',');
            return separator > 0 ? typeName[..separator].Trim() : typeName.Trim();
        }

        private static string RemoveWhitespace(string text)
        {
            StringBuilder builder = new(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (!char.IsWhiteSpace(c))
                    builder.Append(c);
            }

            return builder.ToString();
        }
    }

    internal readonly struct ResXResourceEntry
    {
        public ResXResourceEntry(string name, object? value, string? comment, Func<Type, string>? typeNameConverter)
        {
            Name = name;
            Value = value;
            Comment = comment;
            TypeNameConverter = typeNameConverter;
        }

        public string? Comment { get; }

        public string Name { get; }

        public object? Value { get; }

        public Func<Type, string>? TypeNameConverter { get; }
    }

    internal sealed class ResXDictionaryEnumerator : IDictionaryEnumerator
    {
        private readonly List<DictionaryEntry> _entries;
        private int _index = -1;

        public ResXDictionaryEnumerator(List<DictionaryEntry> entries)
        {
            _entries = entries;
        }

        public DictionaryEntry Entry => _entries[_index];

        public object Key => Entry.Key;

        public object? Value => Entry.Value;

        public object Current => Entry;

        public bool MoveNext()
        {
            if (_index + 1 >= _entries.Count)
                return false;

            _index++;
            return true;
        }

        public void Reset()
        {
            _index = -1;
        }
    }
}
