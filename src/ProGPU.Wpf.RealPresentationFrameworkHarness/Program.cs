using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Collections;
using System.Globalization;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGpuContainerVisual = global::ProGPU.Scene.ContainerVisual;
using ProGpuDrawingContext = global::ProGPU.Scene.DrawingContext;
using ProGpuGradientSpreadMethod = global::ProGPU.Vector.GradientSpreadMethod;
using ProGpuLinearGradientBrush = global::ProGPU.Vector.LinearGradientBrush;
using ProGpuRadialGradientBrush = global::ProGPU.Vector.RadialGradientBrush;
using ProGpuRenderCommand = global::ProGPU.Scene.RenderCommand;
using ProGpuRenderCommandType = global::ProGPU.Scene.RenderCommandType;
using ProGpuVisual = global::ProGPU.Scene.Visual;

public static class Program
{
    private const string PortableWindowActivationServiceTypeName = "System.Windows.PortableWindowActivationService";
    private const string PortableRenderDataProviderTypeName = "System.Windows.Media.PortableRenderDataDrawingContextSinkProvider";
    private const string PortableRenderDataSinkInterfaceTypeName = "System.Windows.Media.IPortableRenderDataDrawingContextSink";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string repoRoot = FindRepoRoot();
            string presentationFrameworkPath = FindRealAssembly(repoRoot, "PresentationFramework");
            string presentationCorePath = FindRealAssembly(repoRoot, "PresentationCore");

            if (args.Contains("--text-wrapping-only", StringComparer.Ordinal))
            {
                RunTextWrappingHarness(repoRoot, presentationFrameworkPath, presentationCorePath);
                Console.WriteLine("Real PresentationFramework portable text wrapping smoke succeeded.");
                return 0;
            }

            RunHarness(repoRoot, presentationFrameworkPath, presentationCorePath);
            Console.WriteLine("Real PresentationFramework code-only smoke succeeded.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void RunTextWrappingHarness(
        string repoRoot,
        string presentationFrameworkPath,
        string presentationCorePath)
    {
        var loadContext = new WpfAssemblyLoadContext(repoRoot, presentationFrameworkPath, presentationCorePath);
        try
        {
            Assembly presentationFramework = loadContext.LoadFromAssemblyPath(presentationFrameworkPath);
            Assembly windowsBase = loadContext.LoadFromAssemblyName(new AssemblyName("WindowsBase"));
            VerifyPortableTextWrapping(presentationFramework, windowsBase);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static void RunHarness(
        string repoRoot,
        string presentationFrameworkPath,
        string presentationCorePath)
    {
        var loadContext = new WpfAssemblyLoadContext(repoRoot, presentationFrameworkPath, presentationCorePath);
        Assembly presentationFramework = loadContext.LoadFromAssemblyPath(presentationFrameworkPath);
        Assembly presentationCore = loadContext.LoadFromAssemblyPath(presentationCorePath);
        Assembly windowsBase = loadContext.LoadFromAssemblyName(new AssemblyName("WindowsBase"));

        object? application = null;
        object? activation = null;
        Type? activationServiceType = null;

        try
        {
            application = Create(presentationFramework, "System.Windows.Application");
            object window = Create(presentationFramework, "System.Windows.Window");
            SetProperty(window, "Title", "ProGPU WPF smoke");
            SetProperty(window, "Width", 320.0);
            SetProperty(window, "Height", 200.0);

            object stackPanel = Create(presentationFramework, "System.Windows.Controls.StackPanel");
            object textBox = Create(presentationFramework, "System.Windows.Controls.TextBox");
            SetProperty(textBox, "Text", "text input smoke");
            object richTextBox = Create(presentationFramework, "System.Windows.Controls.RichTextBox");
            object flowDocument = CreateFlowDocument(presentationFramework);
            SetProperty(richTextBox, "Document", flowDocument);

            AddToCollection(GetProperty(stackPanel, "Children"), textBox);
            AddToCollection(GetProperty(stackPanel, "Children"), richTextBox);
            SetProperty(window, "Content", stackPanel);

            object resources = CreateResourceDictionary(presentationFramework);
            SetProperty(application, "Resources", resources);
            SetProperty(window, "Resources", resources);

            AssertEqual("ProGPU WPF smoke", GetProperty(window, "Title"), "window title");
            AssertEqual(320.0, GetProperty(window, "Width"), "window width");
            AssertEqual(200.0, GetProperty(window, "Height"), "window height");
            AssertEqual(stackPanel, GetProperty(window, "Content"), "window content");
            AssertCollectionCount(GetProperty(stackPanel, "Children"), expected: 2, "stack panel children");
            AssertCollectionCount(GetProperty(resources, "Keys"), expected: 2, "resource dictionary keys");
            AssertCollectionCount(GetProperty(flowDocument, "Blocks"), expected: 1, "flow document blocks");

            VerifyPortableSpellerFallback(presentationFramework);
            VerifyPortableTextWrapping(presentationFramework, windowsBase);
            RegisterPortableActivation(presentationFramework, window, out activationServiceType, out activation);

            using var target = ProGpuWpfCompositionTarget.CreateHeadless();
            var frame = target.BeginDrawingFrame(96, 64);
            IDisposable registration = RegisterRealPortableObjectSinkProvider(
                presentationCore,
                frame,
                new WpfBitmapSourceImageAdapter());

            using (registration)
            {
                DrawRealDrawingVisual(presentationCore, windowsBase);
            }

            VerifyRetainedDrawingVisualBranch(target);
        }
        finally
        {
            if (activation != null)
            {
                Invoke(activation, "Dispose");
            }

            activationServiceType?.GetMethod(
                "Clear",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);

            if (application != null)
            {
                Invoke(application, "Shutdown");
            }

            loadContext.Unload();
        }
    }

    private static object CreateFlowDocument(Assembly presentationFramework)
    {
        object run = Create(presentationFramework, "System.Windows.Documents.Run", "rich text smoke");
        object paragraph = Create(presentationFramework, "System.Windows.Documents.Paragraph", run);
        object flowDocument = Create(presentationFramework, "System.Windows.Documents.FlowDocument");
        AddToCollection(GetProperty(flowDocument, "Blocks"), paragraph);
        return flowDocument;
    }

    private static void VerifyPortableTextWrapping(Assembly presentationFramework, Assembly windowsBase)
    {
        object textBlock = Create(presentationFramework, "System.Windows.Controls.TextBlock");
        SetProperty(
            textBlock,
            "Text",
            "Portable text wrapping keeps all gallery description text visible across multiple constrained lines.");
        SetEnumProperty(textBlock, "TextWrapping", "Wrap");

        Type sizeType = GetRequiredType(windowsBase, "System.Windows.Size");
        object constraint = Activator.CreateInstance(sizeType, 120.0, double.PositiveInfinity)
            ?? throw new InvalidOperationException("Failed to create the portable text wrapping measure constraint.");
        MethodInfo measure = textBlock.GetType().GetMethod(
            "Measure",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { sizeType },
            modifiers: null)
            ?? throw new MissingMethodException(textBlock.GetType().FullName, "Measure(Size)");
        measure.Invoke(textBlock, new[] { constraint });

        int lineCount = (int)GetProperty(textBlock, "LineCount");
        if (lineCount < 2)
        {
            throw new InvalidOperationException(
                $"Expected portable wrapped TextBlock to produce multiple lines, got {lineCount}.");
        }

        MethodInfo getLine = textBlock.GetType().GetMethod(
            "GetLine",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(int) },
            modifiers: null)
            ?? throw new MissingMethodException(textBlock.GetType().FullName, "GetLine(Int32)");
        int coveredCharacters = 0;
        for (int lineIndex = 0; lineIndex < lineCount; ++lineIndex)
        {
            object line = getLine.Invoke(textBlock, new object[] { lineIndex })
                ?? throw new InvalidOperationException($"Expected wrapped TextBlock line {lineIndex} metrics.");
            coveredCharacters += (int)GetProperty(line, "Length");
        }

        string text = (string)GetProperty(textBlock, "Text");
        AssertEqual(text.Length, coveredCharacters, "portable wrapped TextBlock covered character count");

        object desiredSize = GetProperty(textBlock, "DesiredSize");
        double desiredWidth = (double)GetProperty(desiredSize, "Width");
        if (desiredWidth > 120.01)
        {
            throw new InvalidOperationException(
                $"Expected portable wrapped TextBlock width to stay within 120 DIPs, got {desiredWidth}.");
        }
    }

    private static object CreateResourceDictionary(Assembly presentationFramework)
    {
        Type textBoxType = GetRequiredType(presentationFramework, "System.Windows.Controls.TextBox");
        Type buttonType = GetRequiredType(presentationFramework, "System.Windows.Controls.Button");

        object resources = Create(presentationFramework, "System.Windows.ResourceDictionary");
        object textBoxStyle = Create(presentationFramework, "System.Windows.Style", textBoxType);
        object buttonTemplate = Create(presentationFramework, "System.Windows.Controls.ControlTemplate", buttonType);

        AddToDictionary(resources, textBoxType, textBoxStyle);
        AddToDictionary(resources, buttonType, buttonTemplate);
        return resources;
    }

    private static void RegisterPortableActivation(
        Assembly presentationFramework,
        object window,
        out Type activationServiceType,
        out object activation)
    {
        if (!WpfPortableWindowActivation.TryRegisterPresentationFrameworkActivation(
                hostFactory: w => new ProGpuWpfWindowHost(WpfPortableWindowActivation.CreateHostOptions(w))))
        {
            throw new InvalidOperationException("Failed to register ProGPU portable activation with real PresentationFramework.");
        }

        activationServiceType = GetRequiredType(presentationFramework, PortableWindowActivationServiceTypeName);
        AssertEqual(true, GetStaticProperty(activationServiceType, "IsEnabled"), "portable activation enabled");

        MethodInfo tryActivate = activationServiceType.GetMethod(
            "TryActivate",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(activationServiceType.FullName, "TryActivate");
        object?[] parameters = { window, null };
        if (!Equals(true, tryActivate.Invoke(null, parameters)) || parameters[1] == null)
        {
            throw new InvalidOperationException("Real PresentationFramework did not create a portable ProGPU activation.");
        }

        activation = parameters[1]!;
        if (activation is not WpfPortableWindowActivation portableActivation)
        {
            throw new InvalidOperationException($"Expected a ProGPU activation, got {activation.GetType().FullName}.");
        }

        AssertEqual(window, portableActivation.Window, "activation window");
        AssertEqual(window, portableActivation.RootVisual, "activation root visual");
        AssertEqual("ProGPU WPF smoke", portableActivation.Host.Title, "host title");
        AssertEqual(320, portableActivation.Host.Width, "host width");
        AssertEqual(200, portableActivation.Host.Height, "host height");

        activationServiceType.GetMethod(
            "SetTitle",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?.Invoke(null, new[] { activation, "ProGPU WPF smoke updated" });
        activationServiceType.GetMethod(
            "SetClientSize",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?.Invoke(null, new object[] { activation, 480.0, 240.0 });

        AssertEqual("ProGPU WPF smoke updated", portableActivation.Host.Title, "updated host title");
        AssertEqual(480, portableActivation.Host.Width, "updated host width");
        AssertEqual(240, portableActivation.Host.Height, "updated host height");
    }

    private static void VerifyPortableSpellerFallback(Assembly presentationFramework)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Type spellerInteropType = GetRequiredType(
            presentationFramework,
            "System.Windows.Documents.SpellerInteropBase");
        MethodInfo createInstance = spellerInteropType.GetMethod(
            "CreateInstance",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(spellerInteropType.FullName, "CreateInstance");
        object speller = createInstance.Invoke(null, null)
            ?? throw new InvalidOperationException("Expected non-Windows SpellerInteropBase.CreateInstance() to return a portable fallback.");

        try
        {
            AssertEqual("NullSpellerInterop", speller.GetType().Name, "portable speller fallback type");

            MethodInfo canSpellCheck = speller.GetType().GetMethod(
                "CanSpellCheck",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(speller.GetType().FullName, "CanSpellCheck");
            AssertEqual(false, canSpellCheck.Invoke(speller, new object[] { CultureInfo.GetCultureInfo("en-US") }), "portable speller spell-check availability");

            Type sentenceCallbackType = GetRequiredNestedType(spellerInteropType, "EnumSentencesCallback");
            Type segmentCallbackType = GetRequiredNestedType(spellerInteropType, "EnumTextSegmentsCallback");
            Delegate sentenceCallback = Delegate.CreateDelegate(
                sentenceCallbackType,
                typeof(Program).GetMethod(nameof(RecordPortableSpellerSentence), BindingFlags.Static | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(nameof(Program), nameof(RecordPortableSpellerSentence)));
            Delegate segmentCallback = Delegate.CreateDelegate(
                segmentCallbackType,
                typeof(Program).GetMethod(nameof(RecordPortableSpellerSegment), BindingFlags.Static | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(nameof(Program), nameof(RecordPortableSpellerSegment)));

            MethodInfo enumTextSegments = speller.GetType().GetMethod(
                "EnumTextSegments",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(speller.GetType().FullName, "EnumTextSegments");
            var probe = new PortableSpellerProbe();
            object? segmentCount = enumTextSegments.Invoke(
                speller,
                new object[] { "alpha beta, gamma!".ToCharArray(), 18, sentenceCallback, segmentCallback, probe });

            AssertEqual(3, segmentCount, "portable speller segment count");
            AssertEqual(3, probe.Segments.Count, "portable speller callback segment count");
            AssertEqual("alpha:0:5:True", probe.Segments[0], "portable speller first segment");
            AssertEqual("beta:6:4:True", probe.Segments[1], "portable speller second segment");
            AssertEqual("gamma:12:5:True", probe.Segments[2], "portable speller third segment");
            AssertEqual(18, probe.SentenceEndOffset, "portable speller sentence end offset");
        }
        finally
        {
            Invoke(speller, "Dispose");
        }
    }

    private static bool RecordPortableSpellerSentence(object sentence, object data)
    {
        ((PortableSpellerProbe)data).SentenceEndOffset = (int)GetProperty(sentence, "EndOffset");
        return true;
    }

    private static bool RecordPortableSpellerSegment(object segment, object data)
    {
        object textRange = GetProperty(segment, "TextRange");
        ((PortableSpellerProbe)data).Segments.Add(
            $"{GetProperty(segment, "Text")}:{GetProperty(textRange, "Start")}:{GetProperty(textRange, "Length")}:{GetProperty(segment, "IsClean")}");
        return true;
    }

    private static void DrawRealDrawingVisual(Assembly presentationCore, Assembly windowsBase)
    {
        object drawingVisual = Create(presentationCore, "System.Windows.Media.DrawingVisual");
        object drawingContext = Invoke(drawingVisual, "RenderOpen");

        Type brushType = GetRequiredType(presentationCore, "System.Windows.Media.Brush");
        Type penType = GetRequiredType(presentationCore, "System.Windows.Media.Pen");
        Type drawingBrushType = GetRequiredType(presentationCore, "System.Windows.Media.DrawingBrush");
        Type drawingType = GetRequiredType(presentationCore, "System.Windows.Media.Drawing");
        Type formattedTextType = GetRequiredType(presentationCore, "System.Windows.Media.FormattedText");
        Type glyphRunType = GetRequiredType(presentationCore, "System.Windows.Media.GlyphRun");
        Type geometryType = GetRequiredType(presentationCore, "System.Windows.Media.Geometry");
        Type imageBrushType = GetRequiredType(presentationCore, "System.Windows.Media.ImageBrush");
        Type imageSourceType = GetRequiredType(presentationCore, "System.Windows.Media.ImageSource");
        Type transformType = GetRequiredType(presentationCore, "System.Windows.Media.Transform");
        Type pointType = GetRequiredType(windowsBase, "System.Windows.Point");
        Type rectType = GetRequiredType(windowsBase, "System.Windows.Rect");

        Type colorsType = GetRequiredType(presentationCore, "System.Windows.Media.Colors");
        object redBrush = Create(presentationCore, "System.Windows.Media.SolidColorBrush", GetStaticProperty(colorsType, "Red"));
        object greenBrush = Create(presentationCore, "System.Windows.Media.SolidColorBrush", GetStaticProperty(colorsType, "Green"));
        object blueBrush = Create(presentationCore, "System.Windows.Media.SolidColorBrush", GetStaticProperty(colorsType, "Blue"));
        object purpleBrush = Create(presentationCore, "System.Windows.Media.SolidColorBrush", GetStaticProperty(colorsType, "Purple"));
        object bluePen = Create(presentationCore, "System.Windows.Media.Pen", blueBrush, 2.0);

        object rect = Activator.CreateInstance(rectType, 4.0, 5.0, 24.0, 12.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Rect.");
        object guidelineRect = Activator.CreateInstance(rectType, 2.25, 3.25, 40.0, 50.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Rect.");
        object lineStart = Activator.CreateInstance(pointType, 2.0, 3.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Point.");
        object lineEnd = Activator.CreateInstance(pointType, 40.0, 20.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Point.");
        object ellipseCenter = Activator.CreateInstance(pointType, 28.0, 24.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Point.");
        object geometryRect = Activator.CreateInstance(rectType, 10.0, 28.0, 18.0, 11.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Rect.");
        object rectangleGeometry = Create(presentationCore, "System.Windows.Media.RectangleGeometry", geometryRect);
        object clipRect = Activator.CreateInstance(rectType, 1.0, 1.0, 42.0, 34.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Rect.");
        object clipGeometry = Create(presentationCore, "System.Windows.Media.RectangleGeometry", clipRect);
        object drawingGeometryRect = Activator.CreateInstance(rectType, 46.0, 8.0, 14.0, 9.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Rect.");
        object drawingGeometry = Create(presentationCore, "System.Windows.Media.RectangleGeometry", drawingGeometryRect);
        object geometryDrawing = Create(
            presentationCore,
            "System.Windows.Media.GeometryDrawing",
            purpleBrush,
            null,
            drawingGeometry);
        object transform = Create(presentationCore, "System.Windows.Media.TranslateTransform", 6.0, 7.0);
        object guidelineSet = CreateDynamicGuidelineSet(
            presentationCore,
            new[] { 2.25, 42.25 },
            new[] { 3.25, 53.25 });
        object glyphRun = CreateRealGlyphRun(presentationCore, windowsBase, pointType);
        object formattedText = CreateRealFormattedText(presentationCore, greenBrush);
        object textOrigin = Activator.CreateInstance(pointType, 18.0, 82.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Point.");
        object imageSource = CreateRealManagedBitmapSource(presentationCore, windowsBase);
        object imageRect = Activator.CreateInstance(rectType, 62.0, 8.0, 16.0, 12.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Rect.");
        object imageBrush = Activator.CreateInstance(imageBrushType, imageSource)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Media.ImageBrush.");
        object imageBrushRect = Activator.CreateInstance(rectType, 82.0, 8.0, 16.0, 12.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Rect.");
        object drawingBrush = Activator.CreateInstance(drawingBrushType, geometryDrawing)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Media.DrawingBrush.");
        object drawingBrushRect = Activator.CreateInstance(rectType, 102.0, 8.0, 16.0, 12.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Rect.");
        object linearGradientBrush = CreateRealLinearGradientBrush(presentationCore, windowsBase, colorsType);
        object linearGradientRect = Activator.CreateInstance(rectType, 122.0, 8.0, 16.0, 12.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Rect.");
        object radialGradientBrush = CreateRealRadialGradientBrush(presentationCore, windowsBase, colorsType);
        object radialGradientCenter = Activator.CreateInstance(pointType, 150.0, 18.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Point.");

        InvokeDrawing(
            drawingContext,
            "DrawRectangle",
            new[] { brushType, penType, rectType },
            redBrush,
            null,
            rect);
        InvokeDrawing(
            drawingContext,
            "DrawLine",
            new[] { penType, pointType, pointType },
            bluePen,
            lineStart,
            lineEnd);
        InvokeDrawing(
            drawingContext,
            "DrawEllipse",
            new[] { brushType, penType, pointType, typeof(double), typeof(double) },
            greenBrush,
            null,
            ellipseCenter,
            9.0,
            5.0);
        InvokeDrawing(
            drawingContext,
            "DrawGeometry",
            new[] { brushType, penType, geometryType },
            purpleBrush,
            null,
            rectangleGeometry);
        InvokeDrawing(
            drawingContext,
            "PushOpacity",
            new[] { typeof(double) },
            0.5);
        InvokeDrawing(
            drawingContext,
            "DrawRectangle",
            new[] { brushType, penType, rectType },
            greenBrush,
            null,
            rect);
        InvokeDrawing(
            drawingContext,
            "Pop",
            Type.EmptyTypes);
        InvokeDrawing(
            drawingContext,
            "PushClip",
            new[] { geometryType },
            clipGeometry);
        InvokeDrawing(
            drawingContext,
            "DrawRectangle",
            new[] { brushType, penType, rectType },
            blueBrush,
            null,
            rect);
        InvokeDrawing(
            drawingContext,
            "Pop",
            Type.EmptyTypes);
        InvokeDrawing(
            drawingContext,
            "PushTransform",
            new[] { transformType },
            transform);
        InvokeDrawing(
            drawingContext,
            "DrawLine",
            new[] { penType, pointType, pointType },
            bluePen,
            lineStart,
            lineEnd);
        InvokeDrawing(
            drawingContext,
            "Pop",
            Type.EmptyTypes);
        InvokeDrawing(
            drawingContext,
            "PushOpacityMask",
            new[] { brushType },
            redBrush);
        InvokeDrawing(
            drawingContext,
            "DrawRectangle",
            new[] { brushType, penType, rectType },
            purpleBrush,
            null,
            rect);
        InvokeDrawing(
            drawingContext,
            "Pop",
            Type.EmptyTypes);
        InvokeDrawing(
            drawingContext,
            "DrawRoundedRectangle",
            new[] { brushType, penType, rectType, typeof(double), typeof(double) },
            purpleBrush,
            null,
            rect,
            4.0,
            6.0);
        InvokeDrawing(
            drawingContext,
            "PushGuidelineSet",
            new[] { guidelineSet.GetType() },
            guidelineSet);
        InvokeDrawing(
            drawingContext,
            "DrawRectangle",
            new[] { brushType, penType, rectType },
            redBrush,
            null,
            guidelineRect);
        InvokeDrawing(
            drawingContext,
            "Pop",
            Type.EmptyTypes);
        InvokeDrawing(
            drawingContext,
            "DrawDrawing",
            new[] { drawingType },
            geometryDrawing);
        InvokeDrawing(
            drawingContext,
            "DrawGlyphRun",
            new[] { brushType, glyphRunType },
            blueBrush,
            glyphRun);
        InvokeDrawing(
            drawingContext,
            "DrawText",
            new[] { formattedTextType, pointType },
            formattedText,
            textOrigin);
        InvokeDrawing(
            drawingContext,
            "DrawImage",
            new[] { imageSourceType, rectType },
            imageSource,
            imageRect);
        InvokeDrawing(
            drawingContext,
            "DrawRectangle",
            new[] { brushType, penType, rectType },
            imageBrush,
            null,
            imageBrushRect);
        InvokeDrawing(
            drawingContext,
            "DrawRectangle",
            new[] { brushType, penType, rectType },
            drawingBrush,
            null,
            drawingBrushRect);
        InvokeDrawing(
            drawingContext,
            "DrawRectangle",
            new[] { brushType, penType, rectType },
            linearGradientBrush,
            null,
            linearGradientRect);
        InvokeDrawing(
            drawingContext,
            "DrawEllipse",
            new[] { brushType, penType, pointType, typeof(double), typeof(double) },
            radialGradientBrush,
            null,
            radialGradientCenter,
            8.0,
            6.0);
        Invoke(drawingContext, "Close");
    }

    private static void VerifyRetainedDrawingVisualBranch(ProGpuWpfCompositionTarget target)
    {
        if (target.RootVisual.Context.Commands.Count != 0)
        {
            throw new InvalidOperationException(
                $"Expected real DrawingVisual RenderOpen output to use the retained WPF owner branch, but the flat root received {target.RootVisual.Context.Commands.Count} commands.");
        }

        ProGpuContainerVisual retainedFrameRoot = GetSingleContainerChild(
            target.RetainedWpfVisualRoot,
            "retained WPF frame root");
        ProGpuVisual ownerBranch = GetSingleChild(
            retainedFrameRoot,
            "real framework drawing visual owner branch");
        IReadOnlyList<ProGpuRenderCommand> commands = GetRetainedCommands(ownerBranch);
        ProGpuRenderCommandType[] expectedCommandTypes =
        {
            ProGpuRenderCommandType.DrawRect,
            ProGpuRenderCommandType.DrawLine,
            ProGpuRenderCommandType.DrawEllipse,
            ProGpuRenderCommandType.DrawPath,
            ProGpuRenderCommandType.PushOpacity,
            ProGpuRenderCommandType.DrawRect,
            ProGpuRenderCommandType.PopOpacity,
            ProGpuRenderCommandType.PushGeometryClip,
            ProGpuRenderCommandType.DrawRect,
            ProGpuRenderCommandType.PopGeometryClip,
            ProGpuRenderCommandType.DrawLine,
            ProGpuRenderCommandType.PushOpacityMask,
            ProGpuRenderCommandType.DrawRect,
            ProGpuRenderCommandType.PopOpacityMask,
            ProGpuRenderCommandType.DrawRoundedRect,
            ProGpuRenderCommandType.DrawRect,
            ProGpuRenderCommandType.DrawPath,
            ProGpuRenderCommandType.DrawGlyphRun,
            ProGpuRenderCommandType.DrawGlyphRun,
            ProGpuRenderCommandType.DrawTexture,
            ProGpuRenderCommandType.PushGeometryClip,
            ProGpuRenderCommandType.DrawTexture,
            ProGpuRenderCommandType.PopGeometryClip,
            ProGpuRenderCommandType.PushGeometryClip,
            ProGpuRenderCommandType.DrawPath,
            ProGpuRenderCommandType.PopGeometryClip,
            ProGpuRenderCommandType.DrawRect,
            ProGpuRenderCommandType.DrawEllipse
        };
        if (commands.Count != expectedCommandTypes.Length)
        {
            throw new InvalidOperationException(
                $"Expected {expectedCommandTypes.Length} retained drawing commands after real DrawingVisual dispatch, got {commands.Count} commands: {string.Join(", ", commands.Select(command => command.Type))}.");
        }

        for (var i = 0; i < expectedCommandTypes.Length; i++)
        {
            if (commands[i].Type != expectedCommandTypes[i])
            {
                throw new InvalidOperationException(
                    $"Expected retained DrawingVisual command {i} to be {expectedCommandTypes[i]}, got {commands[i].Type}.");
            }
        }

        AssertEqual(0.5f, commands[4].FontSize, "real DrawingVisual retained opacity value");
        AssertEqual(6f, commands[10].Transform.M41, "real DrawingVisual transformed line X offset");
        AssertEqual(7f, commands[10].Transform.M42, "real DrawingVisual transformed line Y offset");
        if (commands[11].Brush == null)
        {
            throw new InvalidOperationException("Expected real DrawingVisual retained opacity mask to carry a native brush.");
        }

        AssertEqual(4f, commands[14].RadiusX, "real DrawingVisual retained rounded rectangle radius X");
        AssertEqual(6f, commands[14].RadiusY, "real DrawingVisual retained rounded rectangle radius Y");
        AssertEqual(2f, commands[15].Rect.X, "real DrawingVisual retained guideline snapped rect X");
        AssertEqual(3f, commands[15].Rect.Y, "real DrawingVisual retained guideline snapped rect Y");
        AssertEqual(40f, commands[15].Rect.Width, "real DrawingVisual retained guideline snapped rect width");
        AssertEqual(50f, commands[15].Rect.Height, "real DrawingVisual retained guideline snapped rect height");
        if (commands[16].Brush == null || commands[16].Path == null)
        {
            throw new InvalidOperationException("Expected real DrawingVisual retained drawing resource path to carry a native brush and path.");
        }

        ushort[]? glyphIndices = commands[17].GlyphIndices;
        if (glyphIndices == null || glyphIndices.Length != 2 || commands[17].Brush == null)
        {
            throw new InvalidOperationException("Expected real DrawingVisual retained glyph run to carry native glyph indices and brush.");
        }

        AssertEqual(12f, commands[17].FontSize, "real DrawingVisual retained glyph run font size");
        AssertEqual(12f, commands[17].Position.X, "real DrawingVisual retained glyph run position X");
        AssertEqual(64f, commands[17].Position.Y, "real DrawingVisual retained glyph run position Y");

        ushort[]? formattedGlyphIndices = commands[18].GlyphIndices;
        if (formattedGlyphIndices == null || formattedGlyphIndices.Length == 0 || commands[18].Brush == null)
        {
            throw new InvalidOperationException("Expected real DrawingVisual retained formatted text to carry WPF-generated glyph indices and brush.");
        }

        AssertEqual(13f, commands[18].FontSize, "real DrawingVisual retained formatted text font size");
        AssertEqual(18f, commands[18].Position.X, "real DrawingVisual retained formatted text position X");

        if (commands[19].Texture == null)
        {
            throw new InvalidOperationException("Expected real DrawingVisual retained bitmap source to upload a native ProGPU texture.");
        }

        AssertEqual(62f, commands[19].Rect.X, "real DrawingVisual retained bitmap image rect X");
        AssertEqual(8f, commands[19].Rect.Y, "real DrawingVisual retained bitmap image rect Y");
        AssertEqual(16f, commands[19].Rect.Width, "real DrawingVisual retained bitmap image rect width");
        AssertEqual(12f, commands[19].Rect.Height, "real DrawingVisual retained bitmap image rect height");
        if (commands[20].Path == null)
        {
            throw new InvalidOperationException("Expected real DrawingVisual retained image brush fill to push a native geometry clip.");
        }

        if (commands[21].Texture == null)
        {
            throw new InvalidOperationException("Expected real DrawingVisual retained image brush fill to upload a native ProGPU texture.");
        }

        AssertEqual(82f, commands[21].Rect.X, "real DrawingVisual retained image brush texture rect X");
        AssertEqual(8f, commands[21].Rect.Y, "real DrawingVisual retained image brush texture rect Y");
        AssertEqual(16f, commands[21].Rect.Width, "real DrawingVisual retained image brush texture rect width");
        AssertEqual(12f, commands[21].Rect.Height, "real DrawingVisual retained image brush texture rect height");
        if (commands[23].Path == null)
        {
            throw new InvalidOperationException("Expected real DrawingVisual retained drawing brush fill to push a native geometry clip.");
        }

        if (commands[24].Brush == null || commands[24].Path == null)
        {
            throw new InvalidOperationException("Expected real DrawingVisual retained drawing brush fill to replay its nested drawing as a native path.");
        }

        AssertLinearGradientCommand(commands[26]);
        AssertRadialGradientCommand(commands[27]);
    }

    private static void AssertLinearGradientCommand(ProGpuRenderCommand command)
    {
        var brush = command.Brush as ProGpuLinearGradientBrush
            ?? throw new InvalidOperationException($"Expected real DrawingVisual retained linear gradient brush, got {command.Brush?.GetType().FullName ?? "null"}.");

        AssertEqual(122f, command.Rect.X, "real DrawingVisual retained linear gradient rect X");
        AssertEqual(8f, command.Rect.Y, "real DrawingVisual retained linear gradient rect Y");
        AssertEqual(16f, command.Rect.Width, "real DrawingVisual retained linear gradient rect width");
        AssertEqual(12f, command.Rect.Height, "real DrawingVisual retained linear gradient rect height");
        AssertEqual(122f, brush.StartPoint.X, "real DrawingVisual retained linear gradient start X");
        AssertEqual(8f, brush.StartPoint.Y, "real DrawingVisual retained linear gradient start Y");
        AssertEqual(138f, brush.EndPoint.X, "real DrawingVisual retained linear gradient end X");
        AssertEqual(20f, brush.EndPoint.Y, "real DrawingVisual retained linear gradient end Y");
        AssertEqual(0.8f, brush.Opacity, "real DrawingVisual retained linear gradient opacity");
        AssertEqual(ProGpuGradientSpreadMethod.Reflect, brush.SpreadMethod, "real DrawingVisual retained linear gradient spread method");
        AssertEqual(2, brush.Stops.Length, "real DrawingVisual retained linear gradient stop count");
        AssertEqual(0f, brush.Stops[0].Offset, "real DrawingVisual retained linear gradient first stop offset");
        AssertEqual(1f, brush.Stops[0].Color.X, "real DrawingVisual retained linear gradient first stop red");
        AssertEqual(1f, brush.Stops[1].Offset, "real DrawingVisual retained linear gradient second stop offset");
        AssertEqual(1f, brush.Stops[1].Color.Z, "real DrawingVisual retained linear gradient second stop blue");
    }

    private static void AssertRadialGradientCommand(ProGpuRenderCommand command)
    {
        var brush = command.Brush as ProGpuRadialGradientBrush
            ?? throw new InvalidOperationException($"Expected real DrawingVisual retained radial gradient brush, got {command.Brush?.GetType().FullName ?? "null"}.");

        AssertEqual(150f, command.Position2.X, "real DrawingVisual retained radial gradient ellipse center X");
        AssertEqual(18f, command.Position2.Y, "real DrawingVisual retained radial gradient ellipse center Y");
        AssertEqual(8f, command.RadiusX, "real DrawingVisual retained radial gradient ellipse radius X");
        AssertEqual(6f, command.RadiusY, "real DrawingVisual retained radial gradient ellipse radius Y");
        AssertEqual(150f, brush.Center.X, "real DrawingVisual retained radial gradient center X");
        AssertEqual(18f, brush.Center.Y, "real DrawingVisual retained radial gradient center Y");
        AssertEqual(146f, brush.GradientOrigin.X, "real DrawingVisual retained radial gradient origin X");
        AssertEqual(21f, brush.GradientOrigin.Y, "real DrawingVisual retained radial gradient origin Y");
        AssertEqual(8f, brush.RadiusX, "real DrawingVisual retained radial gradient radius X");
        AssertEqual(9f, brush.RadiusY, "real DrawingVisual retained radial gradient radius Y");
        AssertEqual(0.6f, brush.Opacity, "real DrawingVisual retained radial gradient opacity");
        AssertEqual(ProGpuGradientSpreadMethod.Repeat, brush.SpreadMethod, "real DrawingVisual retained radial gradient spread method");
        AssertEqual(2, brush.Stops.Length, "real DrawingVisual retained radial gradient stop count");
        AssertEqual(0f, brush.Stops[0].Offset, "real DrawingVisual retained radial gradient first stop offset");
        AssertEqual(128f / 255f, brush.Stops[0].Color.Y, "real DrawingVisual retained radial gradient first stop green");
        AssertEqual(1f, brush.Stops[1].Offset, "real DrawingVisual retained radial gradient second stop offset");
        AssertEqual(0f, brush.Stops[1].Color.W, "real DrawingVisual retained radial gradient second stop alpha");
    }

    private static ProGpuContainerVisual GetSingleContainerChild(ProGpuContainerVisual parent, string description)
    {
        ProGpuVisual visual = GetSingleChild(parent, description);
        return visual as ProGpuContainerVisual
            ?? throw new InvalidOperationException($"Expected {description} to be a container visual, got {visual.GetType().FullName}.");
    }

    private static ProGpuVisual GetSingleChild(ProGpuContainerVisual parent, string description)
    {
        if (parent.Children.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one {description}, got {parent.Children.Count} children.");
        }

        return parent.Children[0];
    }

    private static IReadOnlyList<ProGpuRenderCommand> GetRetainedCommands(ProGpuVisual visual)
    {
        PropertyInfo contextProperty = visual.GetType().GetProperty(
            "Context",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"Retained owner branch type '{visual.GetType().FullName}' does not expose a drawing context.");

        ProGpuDrawingContext context = contextProperty.GetValue(visual) as ProGpuDrawingContext
            ?? throw new InvalidOperationException(
                $"Retained owner branch type '{visual.GetType().FullName}' exposed an unexpected context value.");

        return context.Commands;
    }

    private static object Create(Assembly assembly, string typeName, params object?[] parameters)
    {
        Type type = GetRequiredType(assembly, typeName);
        return Activator.CreateInstance(type, parameters)
            ?? throw new InvalidOperationException($"Failed to create '{typeName}'.");
    }

    private static object CreateDynamicGuidelineSet(Assembly presentationCore, double[] guidelinesX, double[] guidelinesY)
    {
        Type guidelineSetType = GetRequiredType(presentationCore, "System.Windows.Media.GuidelineSet");
        ConstructorInfo constructor = guidelineSetType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(double[]), typeof(double[]), typeof(bool) },
            modifiers: null)
            ?? throw new MissingMethodException(guidelineSetType.FullName, ".ctor(double[], double[], bool)");

        object guidelineSet = constructor.Invoke(new object[] { guidelinesX, guidelinesY, true });
        Invoke(guidelineSet, "Freeze");
        return guidelineSet;
    }

    private static object CreateRealLinearGradientBrush(Assembly presentationCore, Assembly windowsBase, Type colorsType)
    {
        object brush = Create(presentationCore, "System.Windows.Media.LinearGradientBrush");
        SetProperty(brush, "StartPoint", CreatePoint(windowsBase, 0.0, 0.0));
        SetProperty(brush, "EndPoint", CreatePoint(windowsBase, 1.0, 1.0));
        SetProperty(brush, "Opacity", 0.8);
        SetEnumProperty(brush, "SpreadMethod", "Reflect");
        AddToCollection(
            GetProperty(brush, "GradientStops"),
            CreateGradientStop(presentationCore, GetStaticProperty(colorsType, "Red"), 0.0));
        AddToCollection(
            GetProperty(brush, "GradientStops"),
            CreateGradientStop(presentationCore, GetStaticProperty(colorsType, "Blue"), 1.0));
        return brush;
    }

    private static object CreateRealRadialGradientBrush(Assembly presentationCore, Assembly windowsBase, Type colorsType)
    {
        object brush = Create(presentationCore, "System.Windows.Media.RadialGradientBrush");
        SetProperty(brush, "Center", CreatePoint(windowsBase, 0.5, 0.5));
        SetProperty(brush, "GradientOrigin", CreatePoint(windowsBase, 0.25, 0.75));
        SetProperty(brush, "RadiusX", 0.5);
        SetProperty(brush, "RadiusY", 0.75);
        SetProperty(brush, "Opacity", 0.6);
        SetEnumProperty(brush, "SpreadMethod", "Repeat");
        AddToCollection(
            GetProperty(brush, "GradientStops"),
            CreateGradientStop(presentationCore, GetStaticProperty(colorsType, "Green"), 0.0));
        AddToCollection(
            GetProperty(brush, "GradientStops"),
            CreateGradientStop(presentationCore, GetStaticProperty(colorsType, "Transparent"), 1.0));
        return brush;
    }

    private static object CreateGradientStop(Assembly presentationCore, object color, double offset)
    {
        return Create(presentationCore, "System.Windows.Media.GradientStop", color, offset);
    }

    private static object CreatePoint(Assembly windowsBase, double x, double y)
    {
        Type pointType = GetRequiredType(windowsBase, "System.Windows.Point");
        return Activator.CreateInstance(pointType, x, y)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Point.");
    }

    private static object CreateRealGlyphRun(Assembly presentationCore, Assembly windowsBase, Type pointType)
    {
        Type glyphTypefaceType = GetRequiredType(presentationCore, "System.Windows.Media.GlyphTypeface");
        Type glyphRunType = GetRequiredType(presentationCore, "System.Windows.Media.GlyphRun");
        Type xmlLanguageType = GetRequiredType(presentationCore, "System.Windows.Markup.XmlLanguage");
        object glyphTypeface = CreateRealGlyphTypeface(glyphTypefaceType);
        object baselineOrigin = Activator.CreateInstance(pointType, 12.0, 64.0)
            ?? throw new InvalidOperationException("Failed to create System.Windows.Point.");

        ConstructorInfo constructor = glyphRunType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[]
            {
                glyphTypefaceType,
                typeof(int),
                typeof(bool),
                typeof(double),
                typeof(float),
                typeof(System.Collections.Generic.IList<ushort>),
                pointType,
                typeof(System.Collections.Generic.IList<double>),
                typeof(System.Collections.Generic.IList<>).MakeGenericType(pointType),
                typeof(System.Collections.Generic.IList<char>),
                typeof(string),
                typeof(System.Collections.Generic.IList<ushort>),
                typeof(System.Collections.Generic.IList<bool>),
                xmlLanguageType
            },
            modifiers: null)
            ?? throw new MissingMethodException(glyphRunType.FullName, ".ctor(GlyphTypeface, int, bool, double, float, ...)");

        return constructor.Invoke(new object?[]
        {
            glyphTypeface,
            0,
            false,
            12.0,
            1.0f,
            new ushort[] { 0, 0 },
            baselineOrigin,
            new[] { 7.0, 8.0 },
            null,
            new[] { 'A', 'B' },
            null,
            null,
            null,
            null
        });
    }

    private static object CreateRealFormattedText(Assembly presentationCore, object foregroundBrush)
    {
        Type brushType = GetRequiredType(presentationCore, "System.Windows.Media.Brush");
        Type fontFamilyType = GetRequiredType(presentationCore, "System.Windows.Media.FontFamily");
        Type formattedTextType = GetRequiredType(presentationCore, "System.Windows.Media.FormattedText");
        Type flowDirectionType = GetRequiredType(presentationCore, "System.Windows.FlowDirection");
        Type fontStretchType = GetRequiredType(presentationCore, "System.Windows.FontStretch");
        Type fontStretchesType = GetRequiredType(presentationCore, "System.Windows.FontStretches");
        Type fontStyleType = GetRequiredType(presentationCore, "System.Windows.FontStyle");
        Type fontStylesType = GetRequiredType(presentationCore, "System.Windows.FontStyles");
        Type fontWeightType = GetRequiredType(presentationCore, "System.Windows.FontWeight");
        Type fontWeightsType = GetRequiredType(presentationCore, "System.Windows.FontWeights");
        Type typefaceType = GetRequiredType(presentationCore, "System.Windows.Media.Typeface");
        object fontFamily = Activator.CreateInstance(fontFamilyType, "Arial")
            ?? throw new InvalidOperationException("Failed to create System.Windows.Media.FontFamily.");
        object typeface = typefaceType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { fontFamilyType, fontStyleType, fontWeightType, fontStretchType },
            modifiers: null)?.Invoke(new[]
            {
                fontFamily,
                GetStaticProperty(fontStylesType, "Normal"),
                GetStaticProperty(fontWeightsType, "Normal"),
                GetStaticProperty(fontStretchesType, "Normal")
            })
            ?? throw new InvalidOperationException("Failed to create System.Windows.Media.Typeface.");
        object flowDirection = Enum.Parse(flowDirectionType, "LeftToRight");

        ConstructorInfo constructor = formattedTextType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[]
            {
                typeof(string),
                typeof(System.Globalization.CultureInfo),
                flowDirectionType,
                typefaceType,
                typeof(double),
                brushType,
                typeof(double)
            },
            modifiers: null)
            ?? throw new MissingMethodException(formattedTextType.FullName, ".ctor(string, CultureInfo, FlowDirection, Typeface, double, Brush, double)");

        return constructor.Invoke(new object[]
        {
            "Text",
            System.Globalization.CultureInfo.InvariantCulture,
            flowDirection,
            typeface,
            13.0,
            foregroundBrush,
            1.0
        });
    }

    private static object CreateRealManagedBitmapSource(Assembly presentationCore, Assembly windowsBase)
    {
        Type bitmapSourceType = GetRequiredType(presentationCore, "System.Windows.Media.Imaging.BitmapSource");
        Type bitmapPaletteType = GetRequiredType(presentationCore, "System.Windows.Media.Imaging.BitmapPalette");
        Type pixelFormatType = GetRequiredType(presentationCore, "System.Windows.Media.PixelFormat");
        Type pixelFormatsType = GetRequiredType(presentationCore, "System.Windows.Media.PixelFormats");
        Type int32RectType = GetRequiredType(windowsBase, "System.Windows.Int32Rect");
        Type freezableType = GetRequiredType(windowsBase, "System.Windows.Freezable");
        var assemblyName = new AssemblyName("ProGpuWpfRealManagedBitmapSourceSmoke");
        AssemblyBuilder assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndCollect);
        ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);
        TypeBuilder typeBuilder = moduleBuilder.DefineType(
            "ProGpuWpfRealManagedBitmapSource",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            bitmapSourceType);
        FieldBuilder pixelsField = typeBuilder.DefineField(
            "_pixels",
            typeof(byte[]),
            FieldAttributes.Private | FieldAttributes.InitOnly);
        ConstructorInfo baseConstructor = bitmapSourceType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null)
            ?? throw new MissingMethodException(bitmapSourceType.FullName, ".ctor()");
        ConstructorBuilder constructor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            new[] { typeof(byte[]) });
        ILGenerator constructorIl = constructor.GetILGenerator();
        constructorIl.Emit(OpCodes.Ldarg_0);
        constructorIl.Emit(OpCodes.Call, baseConstructor);
        constructorIl.Emit(OpCodes.Ldarg_0);
        constructorIl.Emit(OpCodes.Ldarg_1);
        constructorIl.Emit(OpCodes.Stfld, pixelsField);
        constructorIl.Emit(OpCodes.Ret);

        DefineIntPropertyOverride(typeBuilder, bitmapSourceType, "PixelWidth", 2);
        DefineIntPropertyOverride(typeBuilder, bitmapSourceType, "PixelHeight", 2);
        DefineDoublePropertyOverride(typeBuilder, bitmapSourceType, "DpiX", 96.0);
        DefineDoublePropertyOverride(typeBuilder, bitmapSourceType, "DpiY", 96.0);
        DefineNullPropertyOverride(typeBuilder, bitmapSourceType, "Palette", bitmapPaletteType);
        DefinePixelFormatOverride(typeBuilder, bitmapSourceType, pixelFormatsType, pixelFormatType);
        DefineCopyPixelsOverride(typeBuilder, bitmapSourceType, pixelsField, new[] { typeof(Array), typeof(int), typeof(int) });
        DefineCopyPixelsOverride(typeBuilder, bitmapSourceType, pixelsField, new[] { int32RectType, typeof(Array), typeof(int), typeof(int) });
        DefineCreateInstanceCoreOverride(typeBuilder, freezableType, pixelsField, constructor);

        Type bitmapType = typeBuilder.CreateType()
            ?? throw new InvalidOperationException("Failed to create real managed BitmapSource smoke type.");
        byte[] pixels =
        {
            0, 0, 255, 255,
            0, 255, 0, 255,
            255, 0, 0, 255,
            255, 255, 255, 128
        };
        return Activator.CreateInstance(bitmapType, pixels)
            ?? throw new InvalidOperationException("Failed to create real managed BitmapSource smoke instance.");
    }

    public static void CopyManagedBitmapPixels(
        byte[] source,
        Array pixels,
        int stride,
        int offset,
        int pixelWidth,
        int pixelHeight)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels is not byte[] destination)
        {
            throw new ArgumentException("The managed bitmap smoke source only supports byte[] pixel copies.", nameof(pixels));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        int sourceStride = checked(pixelWidth * 4);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, sourceStride);
        int requiredLength = checked(offset + ((pixelHeight - 1) * stride) + sourceStride);
        if (requiredLength > destination.Length)
        {
            throw new ArgumentException("The destination pixel buffer is too small for the requested copy.", nameof(pixels));
        }

        for (var row = 0; row < pixelHeight; row++)
        {
            Buffer.BlockCopy(source, row * sourceStride, destination, offset + (row * stride), sourceStride);
        }
    }

    private static void DefineIntPropertyOverride(
        TypeBuilder typeBuilder,
        Type baseType,
        string propertyName,
        int value)
    {
        MethodBuilder getter = DefineGetter(typeBuilder, propertyName, typeof(int));
        ILGenerator il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4, value);
        il.Emit(OpCodes.Ret);
        DefinePropertyOverride(typeBuilder, baseType, propertyName, typeof(int), getter);
    }

    private static void DefineDoublePropertyOverride(
        TypeBuilder typeBuilder,
        Type baseType,
        string propertyName,
        double value)
    {
        MethodBuilder getter = DefineGetter(typeBuilder, propertyName, typeof(double));
        ILGenerator il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldc_R8, value);
        il.Emit(OpCodes.Ret);
        DefinePropertyOverride(typeBuilder, baseType, propertyName, typeof(double), getter);
    }

    private static void DefineNullPropertyOverride(
        TypeBuilder typeBuilder,
        Type baseType,
        string propertyName,
        Type returnType)
    {
        MethodBuilder getter = DefineGetter(typeBuilder, propertyName, returnType);
        ILGenerator il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        DefinePropertyOverride(typeBuilder, baseType, propertyName, returnType, getter);
    }

    private static void DefinePixelFormatOverride(
        TypeBuilder typeBuilder,
        Type baseType,
        Type pixelFormatsType,
        Type pixelFormatType)
    {
        MethodInfo getterMethod = pixelFormatsType.GetProperty(
            "Pbgra32",
            BindingFlags.Static | BindingFlags.Public)?.GetMethod
            ?? throw new MissingMemberException(pixelFormatsType.FullName, "Pbgra32");
        MethodBuilder getter = DefineGetter(typeBuilder, "Format", pixelFormatType);
        ILGenerator il = getter.GetILGenerator();
        il.Emit(OpCodes.Call, getterMethod);
        il.Emit(OpCodes.Ret);
        DefinePropertyOverride(typeBuilder, baseType, "Format", pixelFormatType, getter);
    }

    private static void DefinePropertyOverride(
        TypeBuilder typeBuilder,
        Type baseType,
        string propertyName,
        Type returnType,
        MethodBuilder getter)
    {
        PropertyBuilder property = typeBuilder.DefineProperty(
            propertyName,
            PropertyAttributes.None,
            returnType,
            Type.EmptyTypes);
        property.SetGetMethod(getter);
        MethodInfo baseGetter = baseType.GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetMethod
            ?? throw new MissingMemberException(baseType.FullName, propertyName);
        typeBuilder.DefineMethodOverride(getter, baseGetter);
    }

    private static MethodBuilder DefineGetter(TypeBuilder typeBuilder, string propertyName, Type returnType)
    {
        return typeBuilder.DefineMethod(
            $"get_{propertyName}",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
            returnType,
            Type.EmptyTypes);
    }

    private static void DefineCopyPixelsOverride(
        TypeBuilder typeBuilder,
        Type bitmapSourceType,
        FieldInfo pixelsField,
        Type[] parameterTypes)
    {
        MethodInfo baseMethod = bitmapSourceType.GetMethod(
            "CopyPixels",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: parameterTypes,
            modifiers: null)
            ?? throw new MissingMethodException(bitmapSourceType.FullName, "CopyPixels");
        MethodBuilder method = typeBuilder.DefineMethod(
            "CopyPixels",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            typeof(void),
            parameterTypes);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, pixelsField);
        il.Emit(OpCodes.Ldarg, parameterTypes.Length == 3 ? 1 : 2);
        il.Emit(OpCodes.Ldarg, parameterTypes.Length == 3 ? 2 : 3);
        il.Emit(OpCodes.Ldarg, parameterTypes.Length == 3 ? 3 : 4);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(
            OpCodes.Call,
            typeof(Program).GetMethod(
                nameof(CopyManagedBitmapPixels),
                BindingFlags.Static | BindingFlags.Public)
                ?? throw new MissingMethodException(typeof(Program).FullName, nameof(CopyManagedBitmapPixels)));
        il.Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(method, baseMethod);
    }

    private static void DefineCreateInstanceCoreOverride(
        TypeBuilder typeBuilder,
        Type freezableType,
        FieldInfo pixelsField,
        ConstructorInfo constructor)
    {
        MethodInfo baseMethod = freezableType.GetMethod(
            "CreateInstanceCore",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(freezableType.FullName, "CreateInstanceCore");
        MethodBuilder method = typeBuilder.DefineMethod(
            "CreateInstanceCore",
            MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            freezableType,
            Type.EmptyTypes);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, pixelsField);
        il.Emit(
            OpCodes.Callvirt,
            typeof(Array).GetMethod(nameof(Array.Clone), Type.EmptyTypes)
                ?? throw new MissingMethodException(typeof(Array).FullName, nameof(Array.Clone)));
        il.Emit(OpCodes.Castclass, typeof(byte[]));
        il.Emit(OpCodes.Newobj, constructor);
        il.Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(method, baseMethod);
    }

    private static object CreateRealGlyphTypeface(Type glyphTypefaceType)
    {
        Exception? lastFailure = null;
        foreach (string fontPath in EnumerateSystemFontFiles())
        {
            try
            {
                return Activator.CreateInstance(glyphTypefaceType, new Uri(fontPath, UriKind.Absolute))
                    ?? throw new InvalidOperationException($"Failed to create real GlyphTypeface for '{fontPath}'.");
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or TargetInvocationException)
            {
                lastFailure = ex;
            }
        }

        throw new FileNotFoundException(
            "Could not locate a local TrueType/OpenType font file loadable by the real GlyphTypeface smoke.",
            lastFailure);
    }

    private static IEnumerable<string> EnumerateSystemFontFiles()
    {
        foreach (string directory in EnumerateFontDirectories())
        {
            foreach (string extension in new[] { "*.ttf", "*.otf" })
            {
                foreach (string file in SafeEnumerateFiles(directory, extension))
                {
                    yield return file;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateFontDirectories()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return "/System/Library/Fonts/Supplemental";
        yield return "/System/Library/Fonts";
        yield return "/Library/Fonts";
        yield return "/usr/share/fonts";
        yield return "/usr/local/share/fonts";
        if (!string.IsNullOrWhiteSpace(home))
        {
            yield return Path.Combine(home, ".local", "share", "fonts");
            yield return Path.Combine(home, "Library", "Fonts");
        }

        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows))
        {
            yield return Path.Combine(windows, "Fonts");
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string directory, string pattern)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            yield break;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(directory, pattern, SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            yield break;
        }

        foreach (string file in files)
        {
            yield return file;
        }
    }

    private static Type GetRequiredType(Assembly assembly, string typeName)
    {
        return assembly.GetType(typeName, throwOnError: true)
            ?? throw new TypeLoadException($"Could not load '{typeName}' from '{assembly.FullName}'.");
    }

    private static Type GetRequiredNestedType(Type type, string nestedTypeName)
    {
        return type.GetNestedType(nestedTypeName, BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new TypeLoadException($"Could not load nested type '{type.FullName}+{nestedTypeName}'.");
    }

    private static object GetProperty(object instance, string propertyName)
    {
        return instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance)
            ?? throw new InvalidOperationException($"Expected '{instance.GetType().FullName}.{propertyName}' to have a value.");
    }

    private static object GetStaticProperty(Type type, string propertyName)
    {
        return type.GetProperty(
            propertyName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
            ?? throw new InvalidOperationException($"Expected '{type.FullName}.{propertyName}' to have a value.");
    }

    private static void SetProperty(object instance, string propertyName, object value)
    {
        PropertyInfo property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(instance.GetType().FullName, propertyName);
        property.SetValue(instance, value);
    }

    private static void SetEnumProperty(object instance, string propertyName, string value)
    {
        PropertyInfo property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(instance.GetType().FullName, propertyName);
        property.SetValue(instance, Enum.Parse(property.PropertyType, value));
    }

    private static object Invoke(object instance, string methodName)
    {
        MethodInfo method = instance.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null)
            ?? throw new MissingMethodException(instance.GetType().FullName, methodName);
        return method.Invoke(instance, null) ?? new object();
    }

    private static void InvokeDrawing(object drawingContext, string methodName, Type[] parameterTypes, params object?[] parameters)
    {
        MethodInfo method = drawingContext.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: parameterTypes,
            modifiers: null)
            ?? throw new MissingMethodException(drawingContext.GetType().FullName, methodName);
        method.Invoke(drawingContext, parameters);
    }

    private static void AddToCollection(object collection, object item)
    {
        MethodInfo add = collection.GetType().GetMethod(
            "Add",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { item.GetType() },
            modifiers: null)
            ?? collection.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(method =>
                    method.Name == "Add" &&
                    method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType.IsAssignableFrom(item.GetType()))
            ?? throw new MissingMethodException(collection.GetType().FullName, "Add");
        add.Invoke(collection, new[] { item });
    }

    private static void AddToDictionary(object dictionary, object key, object value)
    {
        MethodInfo add = dictionary.GetType().GetMethod(
            "Add",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { typeof(object), typeof(object) },
            modifiers: null)
            ?? throw new MissingMethodException(dictionary.GetType().FullName, "Add");
        add.Invoke(dictionary, new[] { key, value });
    }

    private static void AssertCollectionCount(object collection, int expected, string description)
    {
        object count =
            collection is Array array ? array.Length :
            collection is ICollection nonGenericCollection ? nonGenericCollection.Count :
            GetProperty(collection, "Count");
        AssertEqual(expected, count, description);
    }

    private static void AssertEqual(object? expected, object? actual, string description)
    {
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {description} to be '{expected}', got '{actual}'.");
        }
    }

    private static string FindRealAssembly(string repoRoot, string assemblyName)
    {
        string artifactsRoot = Path.Combine(repoRoot, "artifacts", "bin", assemblyName);
        if (!Directory.Exists(artifactsRoot))
        {
            throw new DirectoryNotFoundException($"Real {assemblyName} artifacts directory was not found: {artifactsRoot}");
        }

        string[] candidates = Directory.GetFiles(
            artifactsRoot,
            $"{assemblyName}.dll",
            SearchOption.AllDirectories);

        string? selected = candidates
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}net10.0{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        return selected
            ?? throw new FileNotFoundException($"Could not locate a net10.0 real {assemblyName}.dll artifact.", artifactsRoot);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string marker = Path.Combine(
                directory.FullName,
                "src",
                "Microsoft.DotNet.Wpf",
                "src",
                "PresentationFramework",
                "PresentationFramework.csproj");

            if (File.Exists(marker))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the WPF repository root.");
    }

    private static IDisposable RegisterRealPortableObjectSinkProvider(
        Assembly presentationCore,
        ProGpuWpfDrawingFrame frame,
        IWpfImageSourceAdapter? imageSourceAdapter)
    {
        Type providerType = GetRequiredType(presentationCore, PortableRenderDataProviderTypeName);
        Type portableSinkInterfaceType = GetRequiredType(presentationCore, PortableRenderDataSinkInterfaceTypeName);
        Type proxyType = BuildPortableSinkProxyType(portableSinkInterfaceType);
        Type factoryType = BuildPortableSinkFactoryType(portableSinkInterfaceType, proxyType);

        object factory = Activator.CreateInstance(factoryType, frame, imageSourceAdapter)
            ?? throw new InvalidOperationException("Failed to create the portable sink factory proxy.");
        Type delegateType = typeof(Func<,>).MakeGenericType(typeof(object), portableSinkInterfaceType);
        MethodInfo createMethod = factoryType.GetMethod(
            "Create",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMethodException(factoryType.FullName, "Create");
        Delegate sinkFactory = Delegate.CreateDelegate(delegateType, factory, createMethod);

        MethodInfo pushMethod = providerType.GetMethod(
            "PushObjectSinkFactory",
            BindingFlags.Static | BindingFlags.Public,
            binder: null,
            types: new[] { delegateType },
            modifiers: null)
            ?? throw new MissingMethodException(providerType.FullName, "PushObjectSinkFactory");

        return (IDisposable)(pushMethod.Invoke(null, new object[] { sinkFactory })
            ?? throw new InvalidOperationException("Real PresentationCore portable provider returned null registration."));
    }

    private static Type BuildPortableSinkFactoryType(Type portableSinkInterfaceType, Type proxyType)
    {
        AssemblyName assemblyName = new("ProGpuWpfFrameworkPortableSinkFactoryProxy");
        AssemblyBuilder assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndCollect);
        ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);
        TypeBuilder typeBuilder = moduleBuilder.DefineType(
            "PortableSinkFactoryProxy",
            TypeAttributes.Public | TypeAttributes.Sealed);
        FieldBuilder frameField = typeBuilder.DefineField(
            "_frame",
            typeof(ProGpuWpfDrawingFrame),
            FieldAttributes.Private | FieldAttributes.InitOnly);
        FieldBuilder imageSourceAdapterField = typeBuilder.DefineField(
            "_imageSourceAdapter",
            typeof(IWpfImageSourceAdapter),
            FieldAttributes.Private | FieldAttributes.InitOnly);

        ConstructorBuilder constructor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            new[] { typeof(ProGpuWpfDrawingFrame), typeof(IWpfImageSourceAdapter) });
        ILGenerator ctorIl = constructor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, frameField);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_2);
        ctorIl.Emit(OpCodes.Stfld, imageSourceAdapterField);
        ctorIl.Emit(OpCodes.Ret);

        MethodInfo openSinkContext = typeof(ProGpuWpfDrawingFrame).GetMethod(
            nameof(ProGpuWpfDrawingFrame.OpenObjectRenderDataSinkContext),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { typeof(object), typeof(IWpfImageSourceAdapter) },
            modifiers: null)
            ?? throw new MissingMethodException(typeof(ProGpuWpfDrawingFrame).FullName, nameof(ProGpuWpfDrawingFrame.OpenObjectRenderDataSinkContext));
        ConstructorInfo proxyConstructor = proxyType.GetConstructor(new[] { typeof(object) })
            ?? throw new MissingMethodException(proxyType.FullName, ".ctor(object)");
        MethodBuilder createMethod = typeBuilder.DefineMethod(
            "Create",
            MethodAttributes.Public,
            portableSinkInterfaceType,
            new[] { typeof(object) });
        ILGenerator createIl = createMethod.GetILGenerator();
        createIl.Emit(OpCodes.Ldarg_0);
        createIl.Emit(OpCodes.Ldfld, frameField);
        createIl.Emit(OpCodes.Ldarg_1);
        createIl.Emit(OpCodes.Ldarg_0);
        createIl.Emit(OpCodes.Ldfld, imageSourceAdapterField);
        createIl.Emit(OpCodes.Callvirt, openSinkContext);
        createIl.Emit(OpCodes.Newobj, proxyConstructor);
        createIl.Emit(OpCodes.Castclass, portableSinkInterfaceType);
        createIl.Emit(OpCodes.Ret);

        return typeBuilder.CreateTypeInfo()!.AsType();
    }

    private static Type BuildPortableSinkProxyType(Type portableSinkInterfaceType)
    {
        AssemblyName assemblyName = new("ProGpuWpfFrameworkPortableSinkProxy");
        AssemblyBuilder assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndCollect);
        ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);
        TypeBuilder typeBuilder = moduleBuilder.DefineType(
            "PortableSinkProxy",
            TypeAttributes.Public | TypeAttributes.Sealed);
        typeBuilder.AddInterfaceImplementation(portableSinkInterfaceType);
        FieldBuilder innerField = typeBuilder.DefineField(
            "_inner",
            typeof(object),
            FieldAttributes.Private | FieldAttributes.InitOnly);

        ConstructorBuilder constructor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            new[] { typeof(object) });
        ILGenerator ctorIl = constructor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, innerField);
        ctorIl.Emit(OpCodes.Ret);

        MethodInfo forwardMethod = typeof(Program).GetMethod(
            nameof(ForwardPortableSinkCall),
            BindingFlags.Static | BindingFlags.Public)
            ?? throw new MissingMethodException(typeof(Program).FullName, nameof(ForwardPortableSinkCall));

        foreach (MethodInfo interfaceMethod in portableSinkInterfaceType.GetMethods())
        {
            if (interfaceMethod.ReturnType != typeof(void))
            {
                throw new NotSupportedException($"Portable sink method '{interfaceMethod.Name}' must return void.");
            }

            ParameterInfo[] parameters = interfaceMethod.GetParameters();
            Type[] parameterTypes = parameters.Select(parameter => parameter.ParameterType).ToArray();
            MethodBuilder methodBuilder = typeBuilder.DefineMethod(
                interfaceMethod.Name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                typeof(void),
                parameterTypes);
            ILGenerator il = methodBuilder.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, innerField);
            il.Emit(OpCodes.Ldstr, interfaceMethod.Name);
            il.Emit(OpCodes.Ldc_I4, parameterTypes.Length);
            il.Emit(OpCodes.Ldc_I4, parameterTypes.Length);
            il.Emit(OpCodes.Newarr, typeof(object));
            for (int i = 0; i < parameterTypes.Length; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldarg, i + 1);
                if (parameterTypes[i].IsValueType)
                {
                    il.Emit(OpCodes.Box, parameterTypes[i]);
                }

                il.Emit(OpCodes.Stelem_Ref);
            }

            il.Emit(OpCodes.Call, forwardMethod);
            il.Emit(OpCodes.Ret);
            typeBuilder.DefineMethodOverride(methodBuilder, interfaceMethod);
        }

        return typeBuilder.CreateTypeInfo()!.AsType();
    }

    public static void ForwardPortableSinkCall(
        object sink,
        string methodName,
        int parameterCount,
        object?[] arguments)
    {
        MethodInfo method = sink.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(candidate =>
                candidate.Name == methodName &&
                candidate.GetParameters().Length == parameterCount)
            ?? throw new MissingMethodException(sink.GetType().FullName, $"{methodName}({parameterCount} args)");

        method.Invoke(sink, arguments);
    }

    private sealed class PortableSpellerProbe
    {
        public List<string> Segments { get; } = new();

        public int SentenceEndOffset { get; set; } = -1;
    }

    private sealed class WpfAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly string _repoRoot;
        private readonly string _presentationFrameworkPath;
        private readonly string _presentationCorePath;
        private readonly AssemblyDependencyResolver _resolver;

        public WpfAssemblyLoadContext(
            string repoRoot,
            string presentationFrameworkPath,
            string presentationCorePath)
            : base(isCollectible: true)
        {
            _repoRoot = repoRoot;
            _presentationFrameworkPath = presentationFrameworkPath;
            _presentationCorePath = presentationCorePath;
            _resolver = new AssemblyDependencyResolver(presentationFrameworkPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.Equals(assemblyName.Name, "PresentationFramework", StringComparison.Ordinal))
            {
                return LoadFromAssemblyPath(_presentationFrameworkPath);
            }

            if (string.Equals(assemblyName.Name, "PresentationCore", StringComparison.Ordinal))
            {
                return LoadFromAssemblyPath(_presentationCorePath);
            }

            string outputAssemblyPath = Path.Combine(
                AppContext.BaseDirectory,
                $"{assemblyName.Name}.dll");

            if (File.Exists(outputAssemblyPath))
            {
                return LoadFromAssemblyPath(outputAssemblyPath);
            }

            string artifactAssemblyPath = Path.Combine(
                _repoRoot,
                "artifacts",
                "bin",
                assemblyName.Name ?? string.Empty,
                "Debug",
                "net10.0",
                $"{assemblyName.Name}.dll");

            if (File.Exists(artifactAssemblyPath))
            {
                return LoadFromAssemblyPath(artifactAssemblyPath);
            }

            string? resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
            return resolvedPath == null ? null : LoadFromAssemblyPath(resolvedPath);
        }
    }
}
