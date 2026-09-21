using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Windows.Media.ProGPU.Composition;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WpfRenderDataGeneratorRedirectionTests
{
    [Fact]
    public void RedirectionCatalogMatchesWpfRenderDataInstructionMetadata()
    {
        var resourceInstructions = ReadRenderDataInstructions();
        var catalog = WpfRenderDataInstructionRedirectionCatalog.Instructions;

        Assert.Equal(resourceInstructions.Select(instruction => instruction.Name), catalog.Select(instruction => instruction.Name));

        foreach (var resourceInstruction in resourceInstructions)
        {
            var catalogEntry = catalog.Single(instruction => instruction.Name == resourceInstruction.Name);

            Assert.Equal(resourceInstruction.HasAdvancedOverload, catalogEntry.HasAdvancedOverload);
            Assert.Equal(resourceInstruction.HasGeneratedNoOpCheck, catalogEntry.HasGeneratedNoOpCheck);
            Assert.Equal(resourceInstruction.IsScope, catalogEntry.IsScope);
            Assert.Equal(resourceInstruction.IsGeneratedInternal, catalogEntry.IsGeneratedInternal);
        }
    }

    [Fact]
    public void GeneratedRenderDataAdapterContractCoversEveryInstruction()
    {
        var catalog = WpfRenderDataInstructionRedirectionCatalog.Instructions;
        var methods = typeof(IWpfGeneratedRenderDataDrawingContext)
            .GetMethods()
            .GroupBy(method => method.Name)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        foreach (var instruction in catalog)
        {
            Assert.True(methods.ContainsKey(instruction.Name), $"{instruction.Name} is missing from the generated render-data adapter contract.");

            if (instruction.HasAdvancedOverload)
            {
                Assert.True(methods[instruction.Name].Length >= 2, $"{instruction.Name} is missing its generated advanced overload.");
            }
            else
            {
                Assert.Single(methods[instruction.Name]);
            }
        }
    }

    [Fact]
    public void CompositionDrawingContextImplementsGeneratedRenderDataAdapterContract()
    {
        var contract = typeof(IWpfGeneratedRenderDataDrawingContext);
        var implementation = typeof(WpfCompositionDrawingContext);

        Assert.True(contract.IsAssignableFrom(implementation));

        var interfaceMap = implementation.GetInterfaceMap(contract);

        Assert.Equal(contract.GetMethods().Length, interfaceMap.TargetMethods.Length);
        Assert.DoesNotContain(interfaceMap.TargetMethods, method => method.IsAbstract);
    }

    [Fact]
    public void RedirectionCatalogDocumentsNullResourceScopeBridgeDecision()
    {
        var nullResourceScopes = WpfRenderDataInstructionRedirectionCatalog.Instructions
            .Where(instruction => instruction.PreservesNullResourceScope)
            .Select(instruction => instruction.Name);

        Assert.Equal(new[] { "PushClip", "PushOpacityMask", "PushTransform" }, nullResourceScopes);
    }

    [Fact]
    public void WpfInternalSinkContractCoversEveryRenderDataInstruction()
    {
        var source = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "IRenderDataDrawingContextSink.cs"));

        foreach (var instruction in WpfRenderDataInstructionRedirectionCatalog.Instructions)
        {
            var overloadCount = Regex.Matches(
                source,
                @"void\s+" + Regex.Escape(instruction.Name) + @"\s*\(",
                RegexOptions.CultureInvariant).Count;
            var expectedCount = instruction.HasAdvancedOverload ? 2 : 1;

            Assert.Equal(expectedCount, overloadCount);
        }

        Assert.Contains("void Close();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfPresentationCoreIncludesInternalRenderDataSinkContract()
    {
        var project = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "PresentationCore.csproj"));

        Assert.Contains(
            @"<Compile Include=""System\Windows\Media\IRenderDataDrawingContextSink.cs"" />",
            project,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WpfRenderDataDrawingContextOwnsSinkLifetimeAndAutoPop()
    {
        var source = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "RenderDataDrawingContext.cs"));

        Assert.Contains("RenderDataDrawingContext(IRenderDataDrawingContextSink renderDataSink)", source, StringComparison.Ordinal);
        Assert.Contains("_renderDataSink = renderDataSink ?? throw new ArgumentNullException(nameof(renderDataSink));", source, StringComparison.Ordinal);
        Assert.Contains("_renderDataSink?.Close();", source, StringComparison.Ordinal);
        Assert.Contains("if (_stackDepth > 0)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (_renderData != null && _stackDepth > 0)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfVisualDrawingContextFactorySelectsSinkBackedContextWhenAvailable()
    {
        var source = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "DrawingVisualDrawingContext.cs"));

        Assert.Contains("internal static VisualDrawingContext Create(Visual ownerVisual)", source, StringComparison.Ordinal);
        Assert.Contains("RenderDataDrawingContextSinkProvider.CreateSink(ownerVisual)", source, StringComparison.Ordinal);
        Assert.Contains("? new VisualDrawingContext(ownerVisual, renderDataSink)", source, StringComparison.Ordinal);
        Assert.Contains(": new VisualDrawingContext(ownerVisual);", source, StringComparison.Ordinal);
        Assert.Contains(") : base(renderDataSink)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfVisualRenderOpenPathsUseSinkAwareFactory()
    {
        var drawingVisual = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "DrawingVisual.cs"));
        var uiElement = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "UIElement.cs"));

        Assert.Contains("return VisualDrawingContext.Create(this);", drawingVisual, StringComparison.Ordinal);
        Assert.Contains("return VisualDrawingContext.Create(this);", uiElement, StringComparison.Ordinal);
        Assert.DoesNotContain("return new VisualDrawingContext(this);", drawingVisual, StringComparison.Ordinal);
        Assert.DoesNotContain("return new VisualDrawingContext(this);", uiElement, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfRenderDataSinkProviderIsProjectIncludedAndSupportsScopedRegistration()
    {
        var provider = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "RenderDataDrawingContextSinkProvider.cs"));
        var project = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "PresentationCore.csproj"));

        Assert.Contains("internal static Func<Visual, IRenderDataDrawingContextSink> SinkFactory { get; private set; }", provider, StringComparison.Ordinal);
        Assert.Contains("internal static IDisposable PushSinkFactory(Func<Visual, IRenderDataDrawingContextSink> sinkFactory)", provider, StringComparison.Ordinal);
        Assert.Contains("internal static IDisposable PushDrawingContextFactory(Func<Visual, DrawingContext> drawingContextFactory)", provider, StringComparison.Ordinal);
        Assert.Contains("internal static IDisposable PushObjectSinkFactory(Func<Visual, object> sinkFactory)", provider, StringComparison.Ordinal);
        Assert.Contains("throw new ArgumentNullException(nameof(sinkFactory));", provider, StringComparison.Ordinal);
        Assert.Contains("throw new ArgumentNullException(nameof(drawingContextFactory));", provider, StringComparison.Ordinal);
        Assert.Contains("new DrawingContextRenderDataSink(drawingContext)", provider, StringComparison.Ordinal);
        Assert.Contains("sink is IPortableRenderDataDrawingContextSink portableSink", provider, StringComparison.Ordinal);
        Assert.Contains("new ObjectRenderDataDrawingContextSink(portableSink)", provider, StringComparison.Ordinal);
        Assert.Contains("private static SinkFactoryScope s_currentScope;", provider, StringComparison.Ordinal);
        Assert.Contains("new SinkFactoryScope(s_currentScope, sinkFactory);", provider, StringComparison.Ordinal);
        Assert.Contains("RestoreSinkFactory(this);", provider, StringComparison.Ordinal);
        Assert.Contains("lock (s_syncRoot)", provider, StringComparison.Ordinal);
        Assert.Contains("return sinkFactory?.Invoke(ownerVisual);", provider, StringComparison.Ordinal);
        Assert.Contains(@"<Compile Include=""System\Windows\Media\DrawingContextRenderDataSink.cs"" />", project, StringComparison.Ordinal);
        Assert.Contains(@"<Compile Include=""System\Windows\Media\ObjectRenderDataDrawingContextSink.cs"" />", project, StringComparison.Ordinal);
        Assert.Contains(@"<Compile Include=""System\Windows\Media\IPortableRenderDataDrawingContextSink.cs"" />", project, StringComparison.Ordinal);
        Assert.Contains(@"<Compile Include=""System\Windows\Media\PortableRenderDataDrawingContextSinkProvider.cs"" />", project, StringComparison.Ordinal);
        Assert.Contains(@"<Compile Include=""System\Windows\Media\RenderDataDrawingContextSinkProvider.cs"" />", project, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfRenderDataSinkProviderScopeRestoresNestedRegistrationsSafely()
    {
        var provider = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "RenderDataDrawingContextSinkProvider.cs"));

        Assert.Contains("if (!ReferenceEquals(s_currentScope, scope))", provider, StringComparison.Ordinal);
        Assert.Contains("s_currentScope = s_currentScope._previousScope;", provider, StringComparison.Ordinal);
        Assert.Contains("while (s_currentScope != null && s_currentScope._disposed);", provider, StringComparison.Ordinal);
        Assert.Contains("SinkFactory = s_currentScope?._sinkFactory;", provider, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfDrawingContextRenderDataSinkForwardsEveryRenderDataInstructionToDrawingContext()
    {
        var source = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "DrawingContextRenderDataSink.cs"));

        Assert.Contains("internal sealed class DrawingContextRenderDataSink :", source, StringComparison.Ordinal);
        Assert.Contains(
            "IRenderDataDrawingContextSink,\n        IPortableNativeDrawingContextSource,\n        IPortableNativeDrawingContextStateSource",
            source,
            StringComparison.Ordinal);
        Assert.Contains("_drawingContext = drawingContext ?? throw new ArgumentNullException(nameof(drawingContext));", source, StringComparison.Ordinal);
        Assert.Contains("return ((IPortableNativeDrawingContextSource)_drawingContext)", source, StringComparison.Ordinal);
        Assert.Contains("return ((IPortableNativeDrawingContextStateSource)_drawingContext)", source, StringComparison.Ordinal);
        Assert.Contains(".TryGetPortableNativeDrawingContext(out nativeDrawingContext);", source, StringComparison.Ordinal);
        Assert.Contains(".TryGetPortableNativeDrawingContextState(out state);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BindingFlags", source, StringComparison.Ordinal);

        foreach (var instruction in WpfRenderDataInstructionRedirectionCatalog.Instructions)
        {
            var forwardCount = Regex.Matches(
                source,
                @"_drawingContext\." + Regex.Escape(instruction.Name) + @"\s*\(",
                RegexOptions.CultureInvariant).Count;
            var expectedCount = instruction.HasAdvancedOverload ? 2 : 1;

            Assert.Equal(expectedCount, forwardCount);
        }

        Assert.Contains("_drawingContext.Close();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfObjectRenderDataDrawingContextSinkForwardsEveryRenderDataInstructionWithoutReflection()
    {
        var source = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "ObjectRenderDataDrawingContextSink.cs"));

        Assert.Contains("internal sealed class ObjectRenderDataDrawingContextSink :", source, StringComparison.Ordinal);
        Assert.Contains(
            "IRenderDataDrawingContextSink,\n        IPortableNativeDrawingContextSource,\n        IPortableNativeDrawingContextStateSource",
            source,
            StringComparison.Ordinal);
        Assert.Contains("private readonly IPortableRenderDataDrawingContextSink _sink;", source, StringComparison.Ordinal);
        Assert.Contains("_sink = sink ?? throw new ArgumentNullException(nameof(sink));", source, StringComparison.Ordinal);
        Assert.Contains("_sink is IPortableNativeDrawingContextSource nativeDrawingContextSource", source, StringComparison.Ordinal);
        Assert.Contains("_sink is IPortableNativeDrawingContextStateSource nativeDrawingContextStateSource", source, StringComparison.Ordinal);
        Assert.Contains("return nativeDrawingContextStateSource.TryGetPortableNativeDrawingContextState(out state);", source, StringComparison.Ordinal);
        Assert.Contains("state = new PortableNativeDrawingContextState(", source, StringComparison.Ordinal);
        Assert.Contains("System.Numerics.Matrix4x4.Identity", source, StringComparison.Ordinal);
        Assert.Contains("state = default;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BindingFlags", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MethodInfo", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Invoke(", source, StringComparison.Ordinal);
        Assert.Contains("ToPortablePoint(point0)", source, StringComparison.Ordinal);
        Assert.Contains("ToPortablePoint(point1)", source, StringComparison.Ordinal);
        Assert.Contains("ToPortablePoint(center)", source, StringComparison.Ordinal);
        Assert.Contains("ToPortableRect(rectangle)", source, StringComparison.Ordinal);
        Assert.Contains("return new PortablePoint(point.X, point.Y);", source, StringComparison.Ordinal);
        Assert.Contains("return rect.IsEmpty", source, StringComparison.Ordinal);

        foreach (var instruction in WpfRenderDataInstructionRedirectionCatalog.Instructions)
        {
            var forwardCount = Regex.Matches(
                source,
                @"_sink\." + Regex.Escape(instruction.Name) + @"\s*\(",
                RegexOptions.CultureInvariant).Count;
            var expectedCount = instruction.HasAdvancedOverload ? 2 : 1;

            Assert.Equal(expectedCount, forwardCount);
        }
    }

    [Fact]
    public void WpfRenderDataGeneratorEmitsSinkRedirectBeforeMilSerialization()
    {
        var generator = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "WpfGfx",
            "codegen",
            "mcg",
            "generators",
            "renderdata.cs"));

        Assert.Contains("private static string WriteRenderDataSinkRedirect", generator, StringComparison.Ordinal);
        Assert.Contains("[[WriteRenderDataSinkRedirect(renderdataInstruction, true /* skip advanced params */)]]", generator, StringComparison.Ordinal);
        Assert.Contains("[[WriteRenderDataSinkRedirect(renderdataInstruction, false /* don't skip advanced params */)]]", generator, StringComparison.Ordinal);
        Assert.Contains("if (_renderDataSink != null)", generator, StringComparison.Ordinal);
        Assert.Contains("_renderDataSink.[[renderdataInstruction.Name]](", generator, StringComparison.Ordinal);
        Assert.Contains("[[WriteStackOperation(renderdataInstruction, true)]]", generator, StringComparison.Ordinal);
        Assert.Contains("return;", generator, StringComparison.Ordinal);
        Assert.True(
            generator.IndexOf("[[WriteRenderDataSinkRedirect(renderdataInstruction, true /* skip advanced params */)]]", StringComparison.Ordinal)
            < generator.IndexOf("_renderData.WriteDataRecord(MILCMD.Mil[[renderdataInstruction.Name]]", StringComparison.Ordinal));
    }

    [Fact]
    public void WpfCheckedInGeneratedRenderDataDrawingContextRedirectsEveryGeneratedMethodToSink()
    {
        var generated = ReadCheckedInGeneratedRenderDataDrawingContext();

        foreach (var instruction in WpfRenderDataInstructionRedirectionCatalog.Instructions)
        {
            AssertGeneratedSinkRedirectBeforeMilPath(generated, instruction.Name, "const");

            if (instruction.HasAdvancedOverload)
            {
                AssertGeneratedSinkRedirectBeforeMilPath(generated, instruction.Name, "animate");
            }
        }
    }

    [Fact]
    public void WpfCheckedInGeneratedRenderDataDrawingContextPreservesSinkStackAccounting()
    {
        var generated = ReadCheckedInGeneratedRenderDataDrawingContext();

        foreach (var instruction in WpfRenderDataInstructionRedirectionCatalog.Instructions.Where(instruction => instruction.Name.StartsWith("Push", StringComparison.Ordinal)))
        {
            AssertSinkBranchContains(generated, instruction.Name, "const", "_stackDepth++;");
        }

        AssertSinkBranchContains(generated, "PushOpacity", "animate", "_stackDepth++;");
        AssertSinkBranchContains(generated, "Pop", "const", "_stackDepth--;");
    }

    [Fact]
    public void WpfMcgProjectUsesPortableCspPathOnNonWindows()
    {
        var project = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "WpfGfx",
            "codegen",
            "mcg",
            "mcg.proj"));

        Assert.Contains("<CspTargetFramework Condition=\"'$(CspTargetFramework)' == '' and '$(OS)' == 'Windows_NT'\">net48</CspTargetFramework>", project, StringComparison.Ordinal);
        Assert.Contains("<CspTargetFramework Condition=\"'$(CspTargetFramework)' == ''\">net10.0</CspTargetFramework>", project, StringComparison.Ordinal);
        Assert.Contains("Properties=\"TargetFramework=$(CspTargetFramework)\"", project, StringComparison.Ordinal);
        Assert.Contains("Condition=\"'$(OS)' == 'Windows_NT'\" Command=\"tools\\GenerateFiles.cmd\"", project, StringComparison.Ordinal);
        Assert.Contains("Condition=\"'$(OS)' != 'Windows_NT'\"", project, StringComparison.Ordinal);
        Assert.Contains("dotnet &quot;$(CspExe)&quot; -rsp:main/Resources.rsp", project, StringComparison.Ordinal);
        Assert.Contains("dotnet &quot;$(CspExe)&quot; -rsp:main/Elements.rsp", project, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfCspToolKeepsNetCoreRoslynBuildPath()
    {
        var project = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "WpfGfx",
            "tools",
            "csp",
            "csp.csproj"));
        var source = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "WpfGfx",
            "tools",
            "csp",
            "Project.cs"));

        Assert.Contains("<TargetFrameworks>net48;net10.0</TargetFrameworks>", project, StringComparison.Ordinal);
        Assert.Contains("<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>", project, StringComparison.Ordinal);
        Assert.Contains("Microsoft.CodeAnalysis.CSharp", project, StringComparison.Ordinal);
        Assert.Contains("#if NETCOREAPP", source, StringComparison.Ordinal);
        Assert.Contains("return BuildWithRoslyn(", source, StringComparison.Ordinal);
        Assert.Contains("CSharpCompilation.Create(", source, StringComparison.Ordinal);
        Assert.Contains("LanguageVersion.Preview", source, StringComparison.Ordinal);
        Assert.Contains("TRUSTED_PLATFORM_ASSEMBLIES", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfMcgFileCodeSinkNormalizesGeneratedPaths()
    {
        var source = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "WpfGfx",
            "codegen",
            "mcg",
            "Runtime",
            "FileCodeSink.cs"));

        Assert.Contains("_filePath = NormalizePath(Path.Combine(dir, filename));", source, StringComparison.Ordinal);
        Assert.Contains("string directory = Path.GetDirectoryName(_filePath);", source, StringComparison.Ordinal);
        Assert.Contains("Directory.CreateDirectory(directory);", source, StringComparison.Ordinal);
        Assert.Contains("path.Replace('\\\\', '/')", source, StringComparison.Ordinal);
        Assert.Contains("path.Replace('/', '\\\\')", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfMcgElementsGenerationNoLongerRequiresXsdExe()
    {
        var generateElements = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "WpfGfx",
            "codegen",
            "mcg",
            "tools",
            "GenerateElements.cmd"));
        var sourceModel = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "WpfGfx",
            "codegen",
            "mcg",
            "ResourceModel",
            "Generated",
            "Elements.cs"));

        Assert.DoesNotContain("xsd.exe", generateElements, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-rsp:main\\Elements.rsp", generateElements, StringComparison.Ordinal);
        Assert.Contains("XML serialization model for xml/Elements.xsd.", sourceModel, StringComparison.Ordinal);
        Assert.DoesNotContain("auto-generated", sourceModel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GeneratedCodeAttribute(\"xsd\"", sourceModel, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfMcgElementsSourceModelMatchesElementsXsdAttributes()
    {
        var sourceModel = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "WpfGfx",
            "codegen",
            "mcg",
            "ResourceModel",
            "Generated",
            "Elements.cs"));
        var schema = XDocument.Load(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "WpfGfx",
            "codegen",
            "mcg",
            "xml",
            "Elements.xsd"));

        Assert.Contains("public CGElement[] Elements", sourceModel, StringComparison.Ordinal);
        Assert.Contains("public CGEvent[] Events", sourceModel, StringComparison.Ordinal);
        Assert.Contains("public CGProperty[] Properties", sourceModel, StringComparison.Ordinal);

        AssertSchemaAttributesMatchSourceModel(schema, sourceModel, "Element", "CGElement");
        AssertSchemaAttributesMatchSourceModel(schema, sourceModel, "Event", "CGEvent");
        AssertSchemaAttributesMatchSourceModel(schema, sourceModel, "Property", "CGProperty");
    }

    private static IReadOnlyList<ResourceInstruction> ReadRenderDataInstructions()
    {
        var document = XDocument.Load(FindResourceXmlPath());
        var instructions = document.Root!
            .Element("RenderDataInstructions")!
            .Elements("RenderDataInstruction")
            .Select(ReadInstruction)
            .ToArray();

        return instructions;
    }

    private static ResourceInstruction ReadInstruction(XElement instruction)
    {
        var name = (string)instruction.Attribute("Name")!;
        var modifier = (string?)instruction.Attribute("Modifier") ?? string.Empty;
        var hasAnimatedField = instruction
            .Element("Fields")?
            .Elements("Field")
            .Any(field => string.Equals((string?)field.Attribute("Animate"), "true", StringComparison.OrdinalIgnoreCase)) == true;
        var hasNoOpGroup = instruction.Element("NoOpGroups")?.Elements("NoOpGroup").Any() == true;
        var isScope = name.StartsWith("Push", StringComparison.Ordinal) || string.Equals(name, "Pop", StringComparison.Ordinal);
        var isGeneratedInternal = modifier
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Contains("internal", StringComparer.Ordinal);

        return new ResourceInstruction(name, hasAnimatedField, hasNoOpGroup, isScope, isGeneratedInternal);
    }

    private static string FindResourceXmlPath()
    {
        return FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "WpfGfx",
            "codegen",
            "mcg",
            "xml",
            "Resource.xml");
    }

    private static string ReadCheckedInGeneratedRenderDataDrawingContext()
    {
        return File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "Generated",
            "RenderDataDrawingContext.cs"));
    }

    private static void AssertGeneratedSinkRedirectBeforeMilPath(string generated, string instructionName, string traceKind)
    {
        var trace = $"""MediaTrace.DrawingContextOp.Trace("{instructionName}({traceKind})");""";
        var traceIndex = generated.IndexOf(trace, StringComparison.Ordinal);

        Assert.True(traceIndex >= 0, $"Missing generated trace marker {instructionName}({traceKind}).");

        var branchIndex = generated.IndexOf($"_renderDataSink.{instructionName}(", traceIndex, StringComparison.Ordinal);
        var ensureIndex = generated.IndexOf("EnsureRenderData();", traceIndex, StringComparison.Ordinal);

        Assert.True(branchIndex >= 0, $"Missing generated sink branch for {instructionName}({traceKind}).");
        Assert.True(ensureIndex >= 0, $"Missing generated MIL path for {instructionName}({traceKind}).");
        Assert.True(branchIndex < ensureIndex, $"Generated sink branch for {instructionName}({traceKind}) must run before EnsureRenderData().");
    }

    private static void AssertSinkBranchContains(string generated, string instructionName, string traceKind, string expected)
    {
        var trace = $"""MediaTrace.DrawingContextOp.Trace("{instructionName}({traceKind})");""";
        var traceIndex = generated.IndexOf(trace, StringComparison.Ordinal);
        var branchIndex = generated.IndexOf($"_renderDataSink.{instructionName}(", traceIndex, StringComparison.Ordinal);
        var returnIndex = generated.IndexOf("return;", branchIndex, StringComparison.Ordinal);

        Assert.True(traceIndex >= 0, $"Missing generated trace marker {instructionName}({traceKind}).");
        Assert.True(branchIndex >= 0, $"Missing generated sink branch for {instructionName}({traceKind}).");
        Assert.True(returnIndex >= 0, $"Missing generated sink branch return for {instructionName}({traceKind}).");
        Assert.Contains(expected, generated[branchIndex..returnIndex], StringComparison.Ordinal);
    }

    private static void AssertSchemaAttributesMatchSourceModel(
        XDocument schema,
        string sourceModel,
        string schemaElementName,
        string sourceTypeName)
    {
        XNamespace xs = "http://www.w3.org/2001/XMLSchema";
        XElement schemaElement = schema
            .Descendants(xs + "element")
            .Single(element => (string?)element.Attribute("name") == schemaElementName);
        var attributes = schemaElement
            .Descendants(xs + "attribute")
            .Select(attribute => new
            {
                Name = (string)attribute.Attribute("name")!,
                Type = (string)attribute.Attribute("type")!
            })
            .ToArray();

        Assert.Contains($"public partial class {sourceTypeName}", sourceModel, StringComparison.Ordinal);

        foreach (var attribute in attributes)
        {
            string expectedType = attribute.Type == "xs:boolean" ? "bool" : "string";

            Assert.Contains($"public {expectedType} {attribute.Name}", sourceModel, StringComparison.Ordinal);

            if (attribute.Type == "xs:boolean")
            {
                Assert.Contains($"public bool {attribute.Name}Specified", sourceModel, StringComparison.Ordinal);
            }
        }
    }

    private static string FindRepoPath(params string[] pathSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathSegments).ToArray());

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repo file '{Path.Combine(pathSegments)}' from the test output directory.");
    }

    private sealed record ResourceInstruction(
        string Name,
        bool HasAdvancedOverload,
        bool HasGeneratedNoOpCheck,
        bool IsScope,
        bool IsGeneratedInternal);
}
