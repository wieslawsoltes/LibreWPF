using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;

internal static class Program
{
    private const string CompilerHarnessAssemblyName = "ProGPU.Wpf.RealXamlCompilerHarness";
    private const string AppTypeName = "ProGPU.Wpf.RealXamlCompilerHarness.App";
    private const string MainWindowTypeName = "ProGPU.Wpf.RealXamlCompilerHarness.MainWindow";
    private const string PortableMediaContextRenderServiceTypeName = "System.Windows.Media.PortableMediaContextRenderService";
    private const string PortableClipboardServiceTypeName = "System.Windows.PortableClipboardService";
    private const string PortableFileDialogServiceTypeName = "Microsoft.Win32.PortableFileDialogService";
    private const string PortableMessageBoxServiceTypeName = "System.Windows.PortableMessageBoxService";
    private const string PortablePresentationSourceTypeName = "System.Windows.PortablePresentationSource";
    private const string PortableWindowActivationServiceTypeName = "System.Windows.PortableWindowActivationService";

    [STAThread]
    private static int Main()
    {
        try
        {
            string repoRoot = FindRepoRoot();
            string presentationFrameworkPath = FindArtifactAssembly(repoRoot, "PresentationFramework");
            string presentationCorePath = FindArtifactAssembly(repoRoot, "PresentationCore");
            string compilerHarnessPath = FindArtifactAssembly(repoRoot, CompilerHarnessAssemblyName);

            RunHarness(repoRoot, presentationFrameworkPath, presentationCorePath, compilerHarnessPath);
            Console.WriteLine("Real WPF Application.Run smoke succeeded.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void RunHarness(
        string repoRoot,
        string presentationFrameworkPath,
        string presentationCorePath,
        string compilerHarnessPath)
    {
        var loadContext = new WpfAssemblyLoadContext(
            repoRoot,
            presentationFrameworkPath,
            presentationCorePath,
            compilerHarnessPath);
        Assembly presentationCore = loadContext.LoadFromAssemblyPath(presentationCorePath);
        Assembly presentationFramework = loadContext.LoadFromAssemblyPath(presentationFrameworkPath);
        Assembly compilerHarness = loadContext.LoadFromAssemblyPath(compilerHarnessPath);
        Assembly systemXaml = loadContext.LoadFromAssemblyName(new AssemblyName("System.Xaml"));

        object? application = null;
        ActivationRecorder? recorder = null;
        Type? activationServiceType = null;

        try
        {
            application = Create(compilerHarness, AppTypeName);
            Invoke(application, "InitializeComponent");
            ValidateSystemXamlNameScopeDictionary(systemXaml);
            ValidateLooseXamlReader(presentationFramework, presentationCore);
            ValidateLooseXamlWriterRoundTrip(presentationFramework);
            ValidateLooseXamlWriterSystemResourceKeyRoundTrip(presentationFramework);
            ValidatePortableSystemParameters(presentationFramework);
            ValidateLooseXamlWriterStyleRoundTrip(presentationFramework);
            ValidateLooseXamlWriterControlTemplateRoundTrip(presentationFramework);
            ValidateLooseXamlWriterDataTemplateRoundTrip(presentationFramework);
            ValidateLooseXamlWriterHierarchicalDataTemplateRoundTrip(presentationFramework);
            ValidateLooseXamlWriterItemsPanelTemplateRoundTrip(presentationFramework);
            ValidateLooseXamlWriterGroupStyleRoundTrip(presentationFramework);
            ValidateLooseXamlWriterFrameworkElementRoundTrip(presentationFramework);
            ValidateLooseXamlWriterFlowDocumentRoundTrip(presentationFramework);
            ValidatePortableClipboard(presentationCore);
            ValidatePortableFileDialogs(presentationFramework);
            ValidateApplication(application);

            recorder = RegisterPortableActivation(
                presentationFramework,
                presentationCore,
                compilerHarness,
                application,
                out activationServiceType);

            object exitCode = Invoke(application, "Run");
            AssertEqual(0, exitCode, "Application.Run exit code");
            recorder.ValidateAfterRun();
        }
        finally
        {
            recorder?.Dispose();

            activationServiceType?.GetMethod(
                "Clear",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
            ClearPortableService(presentationFramework, PortableMessageBoxServiceTypeName);
            ClearPortableService(presentationFramework, PortableFileDialogServiceTypeName);
            ClearPortableService(presentationCore, PortableClipboardServiceTypeName);

            if (application != null)
            {
                TryInvoke(application, "Shutdown");
            }

            loadContext.Unload();
        }
    }

    private static void ValidateApplication(object application)
    {
        AssertEqual("MainWindow.xaml", GetProperty(application, "StartupUri").ToString(), "startup URI");

        object resources = GetProperty(application, "Resources");
        AssertCollectionCount(GetProperty(resources, "Keys"), expected: 17, "application resource keys");
        object mergedDictionaries = GetProperty(resources, "MergedDictionaries");
        AssertCollectionCount(mergedDictionaries, expected: 1, "application merged dictionaries");
        object smokeResources = GetCollectionItem(mergedDictionaries, 0);
        AssertType(smokeResources, "System.Windows.ResourceDictionary", "compiled merged resource dictionary");
        AssertEqual("SmokeResources.xaml", GetProperty(smokeResources, "Source").ToString(), "compiled merged resource dictionary source");

        object accentBrush = GetDictionaryValue(resources, "AccentBrush");
        AssertType(accentBrush, "System.Windows.Media.SolidColorBrush", "accent brush");
        AssertEqual("#FF356D9E", GetProperty(accentBrush, "Color").ToString(), "accent brush color");

        object replacementAccentBrush = GetDictionaryValue(resources, "ReplacementAccentBrush");
        AssertType(replacementAccentBrush, "System.Windows.Media.SolidColorBrush", "replacement accent brush");
        AssertEqual("#FF9C4A2F", GetProperty(replacementAccentBrush, "Color").ToString(), "replacement accent brush color");

        object unsharedAccentBrush = GetDictionaryValue(resources, "UnsharedAccentBrush");
        object secondUnsharedAccentBrush = GetDictionaryValue(resources, "UnsharedAccentBrush");
        AssertType(unsharedAccentBrush, "System.Windows.Media.SolidColorBrush", "unshared accent brush");
        AssertEqual("#FF4D6F8E", GetProperty(unsharedAccentBrush, "Color").ToString(), "unshared accent brush color");
        AssertNotSame(unsharedAccentBrush, secondUnsharedAccentBrush, "compiled x:Shared=false resource lookup");

        ValidateFreezableBrushResource(resources);
        ValidateFreezableGradientBrushResource(resources);

        object smokeButtonTemplate = GetDictionaryValue(resources, "SmokeButtonTemplate");
        AssertType(smokeButtonTemplate, "System.Windows.Controls.ControlTemplate", "button control template");

        object textBoxStyle = GetDictionaryValue(resources, "SmokeTextBoxStyle");
        AssertType(textBoxStyle, "System.Windows.Style", "TextBox style");
        AssertEqual("System.Windows.Controls.TextBox", GetProperty(textBoxStyle, "TargetType").ToString(), "TextBox style target");

        object basedOnTextBoxStyle = GetDictionaryValue(resources, "BasedOnTextBoxStyle");
        AssertType(basedOnTextBoxStyle, "System.Windows.Style", "BasedOn TextBox style");
        AssertEqual("System.Windows.Controls.TextBox", GetProperty(basedOnTextBoxStyle, "TargetType").ToString(), "BasedOn TextBox style target");
        AssertSame(textBoxStyle, GetProperty(basedOnTextBoxStyle, "BasedOn"), "compiled TextBox BasedOn base style");

        object triggeredButtonStyle = GetDictionaryValue(resources, "TriggeredButtonStyle");
        AssertType(triggeredButtonStyle, "System.Windows.Style", "triggered Button style");
        AssertEqual("System.Windows.Controls.Button", GetProperty(triggeredButtonStyle, "TargetType").ToString(), "triggered Button style target");

        object propertyTriggeredButtonStyle = GetDictionaryValue(resources, "PropertyTriggeredButtonStyle");
        AssertType(propertyTriggeredButtonStyle, "System.Windows.Style", "property-triggered Button style");
        AssertEqual("System.Windows.Controls.Button", GetProperty(propertyTriggeredButtonStyle, "TargetType").ToString(), "property-triggered Button style target");

        object multiPropertyTriggeredButtonStyle = GetDictionaryValue(resources, "MultiPropertyTriggeredButtonStyle");
        AssertType(multiPropertyTriggeredButtonStyle, "System.Windows.Style", "multi-property-triggered Button style");
        AssertEqual("System.Windows.Controls.Button", GetProperty(multiPropertyTriggeredButtonStyle, "TargetType").ToString(), "multi-property-triggered Button style target");

        object triggerActionButtonStyle = GetDictionaryValue(resources, "TriggerActionButtonStyle");
        AssertType(triggerActionButtonStyle, "System.Windows.Style", "trigger-action Button style");
        AssertEqual("System.Windows.Controls.Button", GetProperty(triggerActionButtonStyle, "TargetType").ToString(), "trigger-action Button style target");

        object dataTriggerActionButtonStyle = GetDictionaryValue(resources, "DataTriggerActionButtonStyle");
        AssertType(dataTriggerActionButtonStyle, "System.Windows.Style", "data-trigger-action Button style");
        AssertEqual("System.Windows.Controls.Button", GetProperty(dataTriggerActionButtonStyle, "TargetType").ToString(), "data-trigger-action Button style target");

        object multiDataTriggerActionButtonStyle = GetDictionaryValue(resources, "MultiDataTriggerActionButtonStyle");
        AssertType(multiDataTriggerActionButtonStyle, "System.Windows.Style", "multi-data-trigger-action Button style");
        AssertEqual("System.Windows.Controls.Button", GetProperty(multiDataTriggerActionButtonStyle, "TargetType").ToString(), "multi-data-trigger-action Button style target");

        object multiTriggerActionButtonStyle = GetDictionaryValue(resources, "MultiTriggerActionButtonStyle");
        AssertType(multiTriggerActionButtonStyle, "System.Windows.Style", "multi-trigger-action Button style");
        AssertEqual("System.Windows.Controls.Button", GetProperty(multiTriggerActionButtonStyle, "TargetType").ToString(), "multi-trigger-action Button style target");

        object multiTriggeredButtonStyle = GetDictionaryValue(resources, "MultiTriggeredButtonStyle");
        AssertType(multiTriggeredButtonStyle, "System.Windows.Style", "multi-triggered Button style");
        AssertEqual("System.Windows.Controls.Button", GetProperty(multiTriggeredButtonStyle, "TargetType").ToString(), "multi-triggered Button style target");

        object mergedAccentBrush = Invoke(application, "TryFindResource", "MergedAccentBrush");
        AssertType(mergedAccentBrush, "System.Windows.Media.SolidColorBrush", "merged accent brush");
        AssertEqual("#FF547A48", GetProperty(mergedAccentBrush, "Color").ToString(), "merged accent brush color");

        object mergedBlockMargin = Invoke(application, "TryFindResource", "MergedBlockMargin");
        AssertType(mergedBlockMargin, "System.Windows.Thickness", "merged block margin");
        AssertEqual(8.0, GetProperty(mergedBlockMargin, "Top"), "merged block margin top");
    }

    private static void ValidateLooseXamlReader(Assembly presentationFramework, Assembly presentationCore)
    {
        const string looseXaml = """
<StackPanel
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    x:Name="LooseRoot">
    <StackPanel.Resources>
        <SolidColorBrush x:Key="LooseAccentBrush" Color="#336699" />
        <Style x:Key="LooseButtonStyle" TargetType="{x:Type Button}">
            <Setter Property="Tag" Value="loose style tag" />
            <Setter Property="Background" Value="{StaticResource LooseAccentBrush}" />
        </Style>
    </StackPanel.Resources>
    <Button
        x:Name="LooseButton"
        Content="loose button"
        Style="{StaticResource LooseButtonStyle}" />
    <TextBox
        x:Name="LooseTextBox"
        Tag="loose binding text"
        Text="{Binding Tag, RelativeSource={RelativeSource Self}}" />
    <TextBox
        x:Name="LooseInputScopeTextBox"
        Text="input scope text">
        <InputMethod.InputScope>
            <InputScope
                RegularExpression="[a-z]+"
                SrgsMarkup="external-input-scope">
                <InputScope.Names>
                    <InputScopeName>EmailSmtpAddress</InputScopeName>
                </InputScope.Names>
                <InputScope.PhraseList>
                    <InputScopePhrase>external phrase</InputScopePhrase>
                </InputScope.PhraseList>
            </InputScope>
        </InputMethod.InputScope>
    </TextBox>
</StackPanel>
""";

        object root = ParseLooseXaml(presentationFramework, looseXaml);
        AssertType(root, "System.Windows.Controls.StackPanel", "loose XamlReader root");
        AssertEqual("LooseRoot", GetProperty(root, "Name"), "loose XamlReader root name");
        object children = GetProperty(root, "Children");
        AssertCollectionCount(children, expected: 3, "loose XamlReader children");

        object resources = GetProperty(root, "Resources");
        object accentBrush = GetDictionaryValue(resources, "LooseAccentBrush");
        AssertType(accentBrush, "System.Windows.Media.SolidColorBrush", "loose XamlReader brush resource");
        AssertEqual("#FF336699", GetProperty(accentBrush, "Color").ToString(), "loose XamlReader brush color");
        object buttonStyle = GetDictionaryValue(resources, "LooseButtonStyle");
        AssertType(buttonStyle, "System.Windows.Style", "loose XamlReader style resource");
        AssertEqual("System.Windows.Controls.Button", GetProperty(buttonStyle, "TargetType").ToString(), "loose XamlReader style target");

        object button = Invoke(root, "FindName", "LooseButton");
        AssertType(button, "System.Windows.Controls.Button", "loose XamlReader named Button");
        AssertSame(GetCollectionItem(children, 0), button, "loose XamlReader Button child");
        AssertSame(buttonStyle, GetProperty(button, "Style"), "loose XamlReader StaticResource style");
        AssertEqual("loose button", GetProperty(button, "Content"), "loose XamlReader Button content");
        AssertEqual("loose style tag", GetProperty(button, "Tag"), "loose XamlReader style setter tag");
        AssertSame(accentBrush, GetProperty(button, "Background"), "loose XamlReader style StaticResource brush");

        object textBox = Invoke(root, "FindName", "LooseTextBox");
        AssertType(textBox, "System.Windows.Controls.TextBox", "loose XamlReader named TextBox");
        AssertSame(GetCollectionItem(children, 1), textBox, "loose XamlReader TextBox child");
        AssertEqual("loose binding text", GetProperty(textBox, "Tag"), "loose XamlReader TextBox tag");
        AssertEqual("loose binding text", GetProperty(textBox, "Text"), "loose XamlReader RelativeSource binding text");
        AssertBindingPath(textBox, "TextProperty", "Tag", "loose XamlReader Binding path");

        object inputScopeTextBox = Invoke(root, "FindName", "LooseInputScopeTextBox");
        AssertType(inputScopeTextBox, "System.Windows.Controls.TextBox", "loose XamlReader named InputScope TextBox");
        AssertSame(GetCollectionItem(children, 2), inputScopeTextBox, "loose XamlReader InputScope TextBox child");
        AssertEqual("input scope text", GetProperty(inputScopeTextBox, "Text"), "loose XamlReader InputScope TextBox text");
        ValidateLooseInputScope(presentationCore, inputScopeTextBox);
    }

    private static void ValidateLooseInputScope(Assembly presentationCore, object target)
    {
        Type inputMethodType = GetRequiredType(presentationCore, "System.Windows.Input.InputMethod");
        object inputScope = InvokeStatic(inputMethodType, "GetInputScope", target);
        AssertType(inputScope, "System.Windows.Input.InputScope", "loose XamlReader InputScope attached value");
        AssertEqual("[a-z]+", GetProperty(inputScope, "RegularExpression"), "loose XamlReader InputScope regular expression");
        AssertEqual("external-input-scope", GetProperty(inputScope, "SrgsMarkup"), "loose XamlReader InputScope SRGS markup");

        object names = GetProperty(inputScope, "Names");
        AssertCollectionCount(names, expected: 1, "loose XamlReader InputScope names");
        object scopeName = GetCollectionItem(names, 0);
        AssertType(scopeName, "System.Windows.Input.InputScopeName", "loose XamlReader InputScopeName");
        AssertEqual("EmailSmtpAddress", GetProperty(scopeName, "NameValue").ToString(), "loose XamlReader InputScopeName text content");

        object phrases = GetProperty(inputScope, "PhraseList");
        AssertCollectionCount(phrases, expected: 1, "loose XamlReader InputScope phrases");
        object phrase = GetCollectionItem(phrases, 0);
        AssertType(phrase, "System.Windows.Input.InputScopePhrase", "loose XamlReader InputScopePhrase");
        AssertEqual("external phrase", GetProperty(phrase, "Name"), "loose XamlReader InputScopePhrase text content");
    }

    private static object ParseLooseXaml(Assembly presentationFramework, string xaml)
    {
        Type xamlReaderType = GetRequiredType(presentationFramework, "System.Windows.Markup.XamlReader");
        MethodInfo parse = xamlReaderType.GetMethod(
                "Parse",
                BindingFlags.Static | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(string) },
                modifiers: null)
            ?? throw new MissingMethodException(xamlReaderType.FullName, "Parse");

        return parse.Invoke(null, new object[] { xaml })
            ?? throw new InvalidOperationException("Loose XamlReader.Parse returned null.");
    }

    private static void ValidateLooseXamlWriterRoundTrip(Assembly presentationFramework)
    {
        const string writableXaml = """
<LinearGradientBrush
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    StartPoint="0,0"
    EndPoint="1,1"
    Opacity="0.75"
    SpreadMethod="Reflect">
    <GradientStop Color="#336699" Offset="0" />
    <GradientStop Color="#9C4A2F" Offset="1" />
</LinearGradientBrush>
""";

        object brush = ParseLooseXaml(presentationFramework, writableXaml);
        string serialized = SaveLooseXaml(presentationFramework, brush);
        AssertContains("LinearGradientBrush", serialized, "loose XamlWriter serialized brush");
        AssertContains("GradientStop", serialized, "loose XamlWriter serialized GradientStop");

        object roundTrippedBrush = ParseLooseXaml(presentationFramework, serialized);
        AssertType(roundTrippedBrush, "System.Windows.Media.LinearGradientBrush", "loose XamlWriter round-trip brush");
        AssertEqual(0.75, GetProperty(roundTrippedBrush, "Opacity"), "loose XamlWriter round-trip brush opacity");
        AssertEqual("Reflect", GetProperty(roundTrippedBrush, "SpreadMethod").ToString(), "loose XamlWriter round-trip brush spread method");
        AssertPoint(GetProperty(roundTrippedBrush, "StartPoint"), 0.0, 0.0, "loose XamlWriter round-trip brush start point");
        AssertPoint(GetProperty(roundTrippedBrush, "EndPoint"), 1.0, 1.0, "loose XamlWriter round-trip brush end point");

        object roundTrippedStops = GetProperty(roundTrippedBrush, "GradientStops");
        AssertCollectionCount(roundTrippedStops, expected: 2, "loose XamlWriter round-trip GradientStop count");
        ValidateLooseGradientStop(GetCollectionItem(roundTrippedStops, 0), "#FF336699", 0.0, "first");
        ValidateLooseGradientStop(GetCollectionItem(roundTrippedStops, 1), "#FF9C4A2F", 1.0, "second");
    }

    private static void ValidateLooseXamlWriterSystemResourceKeyRoundTrip(Assembly presentationFramework)
    {
        Type dictionaryType = GetRequiredType(presentationFramework, "System.Windows.ResourceDictionary");
        Type menuItemType = GetRequiredType(presentationFramework, "System.Windows.Controls.MenuItem");
        Type styleType = GetRequiredType(presentationFramework, "System.Windows.Style");

        object systemKey = GetStaticProperty(menuItemType, "SeparatorStyleKey");
        object style = CreateInternal(styleType, menuItemType);
        object dictionary = CreateInternal(dictionaryType);
        ((IDictionary)dictionary).Add(systemKey, style);

        string serialized = SaveLooseXaml(presentationFramework, dictionary);
        AssertContains("ResourceDictionary", serialized, "loose XamlWriter serialized ResourceDictionary");
        AssertContains("x:Key", serialized, "loose XamlWriter serialized system resource key directive");
        AssertContains("MenuItem", serialized, "loose XamlWriter serialized system resource key owner");
        AssertContains("SeparatorStyleKey", serialized, "loose XamlWriter serialized system resource key member");

        object roundTrippedDictionary = ParseLooseXaml(presentationFramework, serialized);
        object roundTrippedStyle = GetDictionaryValue(roundTrippedDictionary, systemKey);
        AssertType(roundTrippedStyle, "System.Windows.Style", "loose XamlWriter round-trip system-key style");
        AssertEqual(menuItemType, GetProperty(roundTrippedStyle, "TargetType"), "loose XamlWriter round-trip system-key style target");
    }

    private static void ValidatePortableSystemParameters(Assembly presentationFramework)
    {
        Type systemParametersType = GetRequiredType(presentationFramework, "System.Windows.SystemParameters");

        AssertPortableSystemParameterMetric(systemParametersType, "FocusBorderWidth");
        AssertPortableSystemParameterMetric(systemParametersType, "FocusBorderHeight");
        AssertPortableSystemParameterMetric(systemParametersType, "FocusHorizontalBorderHeight");
        AssertPortableSystemParameterMetric(systemParametersType, "FocusVerticalBorderWidth");

        if (!OperatingSystem.IsWindows())
        {
            AssertEqual(false, GetStaticProperty(systemParametersType, "HighContrast"), "portable SystemParameters.HighContrast");

            Type systemColorsType = GetRequiredType(presentationFramework, "System.Windows.SystemColors");
            AssertEqual("#FF004275", GetStaticProperty(systemColorsType, "AccentColorDark3").ToString(), "portable AccentColorDark3");
            AssertEqual("#FF005A9E", GetStaticProperty(systemColorsType, "AccentColorDark2").ToString(), "portable AccentColorDark2");
            AssertEqual("#FF0067B9", GetStaticProperty(systemColorsType, "AccentColorDark1").ToString(), "portable AccentColorDark1");
            AssertEqual("#FF0078D4", GetStaticProperty(systemColorsType, "AccentColor").ToString(), "portable AccentColor");
            AssertEqual("#FF429CE3", GetStaticProperty(systemColorsType, "AccentColorLight1").ToString(), "portable AccentColorLight1");
            AssertEqual("#FF76B9ED", GetStaticProperty(systemColorsType, "AccentColorLight2").ToString(), "portable AccentColorLight2");
            AssertEqual("#FFA6D8FF", GetStaticProperty(systemColorsType, "AccentColorLight3").ToString(), "portable AccentColorLight3");
        }
    }

    private static void AssertPortableSystemParameterMetric(Type systemParametersType, string propertyName)
    {
        double value = Convert.ToDouble(GetStaticProperty(systemParametersType, propertyName));
        if (OperatingSystem.IsWindows())
        {
            if (value < 0)
            {
                throw new InvalidOperationException($"Expected SystemParameters.{propertyName} to be non-negative, got '{value}'.");
            }

            return;
        }

        AssertClose(1.0, value, 0.0001, $"portable SystemParameters.{propertyName}");
    }

    private static void ValidateLooseXamlWriterStyleRoundTrip(Assembly presentationFramework)
    {
        const string styleDictionaryXaml = """
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Style x:Key="WriterBaseButtonStyle" TargetType="{x:Type Button}">
        <Setter Property="Tag" Value="base style tag" />
    </Style>
    <Style x:Key="WriterButtonStyle" TargetType="{x:Type Button}" BasedOn="{StaticResource WriterBaseButtonStyle}">
        <Setter Property="Content" Value="writer style content" />
        <Setter Property="MinWidth" Value="144" />
    </Style>
</ResourceDictionary>
""";

        object dictionary = ParseLooseXaml(presentationFramework, styleDictionaryXaml);
        string serialized = SaveLooseXaml(presentationFramework, dictionary);
        AssertContains("ResourceDictionary", serialized, "loose XamlWriter serialized style dictionary");
        AssertContains("WriterBaseButtonStyle", serialized, "loose XamlWriter serialized base style key");
        AssertContains("WriterButtonStyle", serialized, "loose XamlWriter serialized derived style key");
        AssertContains("BasedOn", serialized, "loose XamlWriter serialized style BasedOn");
        AssertContains("Setter", serialized, "loose XamlWriter serialized style setters");

        object roundTrippedDictionary = ParseLooseXaml(presentationFramework, serialized);
        object baseStyle = GetDictionaryValue(roundTrippedDictionary, "WriterBaseButtonStyle");
        object derivedStyle = GetDictionaryValue(roundTrippedDictionary, "WriterButtonStyle");
        AssertType(baseStyle, "System.Windows.Style", "loose XamlWriter round-trip base style");
        AssertType(derivedStyle, "System.Windows.Style", "loose XamlWriter round-trip derived style");
        AssertEqual("System.Windows.Controls.Button", GetProperty(baseStyle, "TargetType").ToString(), "loose XamlWriter round-trip base style target");
        AssertEqual("System.Windows.Controls.Button", GetProperty(derivedStyle, "TargetType").ToString(), "loose XamlWriter round-trip derived style target");

        object baseSetters = GetProperty(baseStyle, "Setters");
        AssertCollectionCount(baseSetters, expected: 1, "loose XamlWriter round-trip base style setters");
        AssertLooseStyleSetter(GetCollectionItem(baseSetters, 0), "Tag", "base style tag", "base style tag setter");

        object basedOn = GetProperty(derivedStyle, "BasedOn");
        AssertType(basedOn, "System.Windows.Style", "loose XamlWriter round-trip derived BasedOn style");
        object basedOnSetters = GetProperty(basedOn, "Setters");
        AssertCollectionCount(basedOnSetters, expected: 1, "loose XamlWriter round-trip derived BasedOn setters");
        AssertLooseStyleSetter(GetCollectionItem(basedOnSetters, 0), "Tag", "base style tag", "derived BasedOn tag setter");

        object derivedSetters = GetProperty(derivedStyle, "Setters");
        AssertCollectionCount(derivedSetters, expected: 2, "loose XamlWriter round-trip derived style setters");
        AssertLooseStyleSetter(GetCollectionItem(derivedSetters, 0), "Content", "writer style content", "derived style content setter");
        AssertLooseStyleSetter(GetCollectionItem(derivedSetters, 1), "MinWidth", 144.0, "derived style MinWidth setter");

        object styledButton = Create(presentationFramework, "System.Windows.Controls.Button");
        SetProperty(styledButton, "Style", derivedStyle);
        AssertEqual("base style tag", GetProperty(styledButton, "Tag"), "loose XamlWriter round-trip styled Button inherited Tag");
        AssertEqual("writer style content", GetProperty(styledButton, "Content"), "loose XamlWriter round-trip styled Button content");
        AssertEqual(144.0, GetProperty(styledButton, "MinWidth"), "loose XamlWriter round-trip styled Button MinWidth");
    }

    private static void ValidateLooseXamlWriterControlTemplateRoundTrip(Assembly presentationFramework)
    {
        const string templateDictionaryXaml = """
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <ControlTemplate x:Key="WriterButtonTemplate" TargetType="{x:Type Button}">
        <Border
            x:Name="TemplateBorder"
            Padding="{TemplateBinding Padding}"
            Background="{TemplateBinding Background}">
            <ContentPresenter
                x:Name="TemplateContent"
                HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                RecognizesAccessKey="True" />
        </Border>
        <ControlTemplate.Triggers>
            <Trigger Property="IsDefault" Value="True">
                <Setter TargetName="TemplateBorder" Property="Tag" Value="default template state" />
            </Trigger>
        </ControlTemplate.Triggers>
    </ControlTemplate>
</ResourceDictionary>
""";

        object dictionary = ParseLooseXaml(presentationFramework, templateDictionaryXaml);
        string serialized = SaveLooseXaml(presentationFramework, dictionary);
        AssertContains("ControlTemplate", serialized, "loose XamlWriter serialized ControlTemplate");
        AssertContains("ContentPresenter", serialized, "loose XamlWriter serialized ControlTemplate ContentPresenter");
        AssertContains("ControlTemplate.Triggers", serialized, "loose XamlWriter serialized ControlTemplate triggers");
        AssertContains("TemplateBorder", serialized, "loose XamlWriter serialized ControlTemplate target name");

        object roundTrippedDictionary = ParseLooseXaml(presentationFramework, serialized);
        object template = GetDictionaryValue(roundTrippedDictionary, "WriterButtonTemplate");
        AssertType(template, "System.Windows.Controls.ControlTemplate", "loose XamlWriter round-trip ControlTemplate");
        AssertEqual("System.Windows.Controls.Button", GetProperty(template, "TargetType").ToString(), "loose XamlWriter round-trip ControlTemplate target type");

        object triggers = GetProperty(template, "Triggers");
        AssertCollectionCount(triggers, expected: 1, "loose XamlWriter round-trip ControlTemplate triggers");
        object trigger = GetCollectionItem(triggers, 0);
        AssertType(trigger, "System.Windows.Trigger", "loose XamlWriter round-trip ControlTemplate trigger");
        AssertEqual("IsDefault", GetProperty(GetProperty(trigger, "Property"), "Name"), "loose XamlWriter round-trip ControlTemplate trigger property");
        AssertEqual(true, GetProperty(trigger, "Value"), "loose XamlWriter round-trip ControlTemplate trigger value");
        object setters = GetProperty(trigger, "Setters");
        AssertCollectionCount(setters, expected: 1, "loose XamlWriter round-trip ControlTemplate trigger setters");
        object setter = GetCollectionItem(setters, 0);
        AssertLooseStyleSetter(setter, "Tag", "default template state", "ControlTemplate trigger Tag setter");
        AssertEqual("TemplateBorder", GetProperty(setter, "TargetName"), "loose XamlWriter round-trip ControlTemplate trigger setter target");

        object button = Create(presentationFramework, "System.Windows.Controls.Button");
        SetProperty(button, "Template", template);
        SetProperty(button, "Content", "templated writer button");
        Invoke(button, "ApplyTemplate");
        object templateBorder = Invoke(template, "FindName", "TemplateBorder", button);
        object templateContent = Invoke(template, "FindName", "TemplateContent", button);
        AssertType(templateBorder, "System.Windows.Controls.Border", "loose XamlWriter round-trip applied ControlTemplate border");
        AssertType(templateContent, "System.Windows.Controls.ContentPresenter", "loose XamlWriter round-trip applied ControlTemplate content presenter");
    }

    private static void ValidateLooseXamlWriterDataTemplateRoundTrip(Assembly presentationFramework)
    {
        const string templateDictionaryXaml = """
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <DataTemplate x:Key="WriterDataTemplate">
        <StackPanel
            x:Name="TemplateRoot"
            Tag="writer data template root">
            <TextBlock
                x:Name="TemplateNameText"
                Text="{Binding Name}" />
            <TextBlock
                x:Name="TemplateCategoryText"
                Text="{Binding Category}" />
        </StackPanel>
        <DataTemplate.Triggers>
            <DataTrigger Binding="{Binding IsActive}" Value="True">
                <Setter TargetName="TemplateNameText" Property="Tag" Value="active template item" />
            </DataTrigger>
        </DataTemplate.Triggers>
    </DataTemplate>
</ResourceDictionary>
""";

        object dictionary = ParseLooseXaml(presentationFramework, templateDictionaryXaml);
        object parsedTemplate = GetDictionaryValue(dictionary, "WriterDataTemplate");
        AssertType(parsedTemplate, "System.Windows.DataTemplate", "loose XamlReader DataTemplate");
        object parsedTemplateRoot = Invoke(parsedTemplate, "LoadContent");
        AssertType(parsedTemplateRoot, "System.Windows.Controls.StackPanel", "loose XamlReader DataTemplate root");
        object parsedTemplateChildren = GetProperty(parsedTemplateRoot, "Children");
        AssertCollectionCount(parsedTemplateChildren, expected: 2, "loose XamlReader DataTemplate children");
        AssertBindingPath(GetCollectionItem(parsedTemplateChildren, 0), "TextProperty", "Name", "loose XamlReader DataTemplate name binding path");
        AssertBindingPath(GetCollectionItem(parsedTemplateChildren, 1), "TextProperty", "Category", "loose XamlReader DataTemplate category binding path");

        string serialized = SaveLooseXaml(presentationFramework, dictionary);
        AssertContains("DataTemplate", serialized, "loose XamlWriter serialized DataTemplate");
        AssertContains("WriterDataTemplate", serialized, "loose XamlWriter serialized DataTemplate key");
        AssertContains("TextBlock", serialized, "loose XamlWriter serialized DataTemplate TextBlock");
        AssertContains("DataTemplate.Triggers", serialized, "loose XamlWriter serialized DataTemplate triggers");

        object roundTrippedDictionary = ParseLooseXaml(presentationFramework, serialized);
        object template = GetDictionaryValue(roundTrippedDictionary, "WriterDataTemplate");
        AssertType(template, "System.Windows.DataTemplate", "loose XamlWriter round-trip DataTemplate");

        object triggers = GetProperty(template, "Triggers");
        AssertCollectionCount(triggers, expected: 1, "loose XamlWriter round-trip DataTemplate triggers");
        object trigger = GetCollectionItem(triggers, 0);
        AssertType(trigger, "System.Windows.DataTrigger", "loose XamlWriter round-trip DataTemplate trigger");
        AssertBindingObjectPath(GetProperty(trigger, "Binding"), "IsActive", "loose XamlWriter round-trip DataTemplate trigger binding path");
        AssertEqual("True", GetProperty(trigger, "Value").ToString(), "loose XamlWriter round-trip DataTemplate trigger value");
        object setters = GetProperty(trigger, "Setters");
        AssertCollectionCount(setters, expected: 1, "loose XamlWriter round-trip DataTemplate trigger setters");
        object setter = GetCollectionItem(setters, 0);
        AssertLooseStyleSetter(setter, "Tag", "active template item", "DataTemplate trigger Tag setter");
        AssertEqual("TemplateNameText", GetProperty(setter, "TargetName"), "loose XamlWriter round-trip DataTemplate trigger setter target");

        object templateRoot = Invoke(template, "LoadContent");
        AssertType(templateRoot, "System.Windows.Controls.StackPanel", "loose XamlWriter round-trip DataTemplate root");
        AssertEqual("TemplateRoot", GetProperty(templateRoot, "Name"), "loose XamlWriter round-trip DataTemplate root name");
        AssertEqual("writer data template root", GetProperty(templateRoot, "Tag"), "loose XamlWriter round-trip DataTemplate root tag");

        object children = GetProperty(templateRoot, "Children");
        AssertCollectionCount(children, expected: 2, "loose XamlWriter round-trip DataTemplate children");
        object nameText = GetCollectionItem(children, 0);
        object categoryText = GetCollectionItem(children, 1);
        AssertType(nameText, "System.Windows.Controls.TextBlock", "loose XamlWriter round-trip DataTemplate name TextBlock");
        AssertType(categoryText, "System.Windows.Controls.TextBlock", "loose XamlWriter round-trip DataTemplate category TextBlock");
        AssertEqual("TemplateNameText", GetProperty(nameText, "Name"), "loose XamlWriter round-trip DataTemplate name TextBlock name");
        AssertEqual("TemplateCategoryText", GetProperty(categoryText, "Name"), "loose XamlWriter round-trip DataTemplate category TextBlock name");
    }

    private static void ValidateLooseXamlWriterHierarchicalDataTemplateRoundTrip(Assembly presentationFramework)
    {
        const string templateDictionaryXaml = """
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <HierarchicalDataTemplate
        x:Key="WriterNodeTemplate"
        ItemsSource="{Binding Children}">
        <StackPanel
            x:Name="NodeTemplateRoot"
            Tag="writer hierarchical template root">
            <TextBlock
                x:Name="NodeNameText"
                Text="{Binding Name}" />
            <TextBlock
                x:Name="NodeCountText"
                Text="{Binding Children.Count}" />
        </StackPanel>
        <HierarchicalDataTemplate.Triggers>
            <DataTrigger Binding="{Binding IsExpanded}" Value="True">
                <Setter TargetName="NodeNameText" Property="Tag" Value="expanded writer node" />
            </DataTrigger>
        </HierarchicalDataTemplate.Triggers>
    </HierarchicalDataTemplate>
</ResourceDictionary>
""";

        object dictionary = ParseLooseXaml(presentationFramework, templateDictionaryXaml);
        object parsedTemplate = GetDictionaryValue(dictionary, "WriterNodeTemplate");
        AssertType(parsedTemplate, "System.Windows.HierarchicalDataTemplate", "loose XamlReader HierarchicalDataTemplate");
        AssertBindingObjectPath(GetProperty(parsedTemplate, "ItemsSource"), "Children", "loose XamlReader HierarchicalDataTemplate ItemsSource path");
        object parsedTemplateRoot = Invoke(parsedTemplate, "LoadContent");
        AssertType(parsedTemplateRoot, "System.Windows.Controls.StackPanel", "loose XamlReader HierarchicalDataTemplate root");
        object parsedTemplateChildren = GetProperty(parsedTemplateRoot, "Children");
        AssertCollectionCount(parsedTemplateChildren, expected: 2, "loose XamlReader HierarchicalDataTemplate children");
        AssertBindingPath(GetCollectionItem(parsedTemplateChildren, 0), "TextProperty", "Name", "loose XamlReader HierarchicalDataTemplate name binding path");
        AssertBindingPath(GetCollectionItem(parsedTemplateChildren, 1), "TextProperty", "Children.Count", "loose XamlReader HierarchicalDataTemplate count binding path");

        string serialized = SaveLooseXaml(presentationFramework, dictionary);
        AssertContains("HierarchicalDataTemplate", serialized, "loose XamlWriter serialized HierarchicalDataTemplate");
        AssertContains("WriterNodeTemplate", serialized, "loose XamlWriter serialized HierarchicalDataTemplate key");
        AssertContains("ItemsSource", serialized, "loose XamlWriter serialized HierarchicalDataTemplate ItemsSource");
        AssertContains("NodeTemplateRoot", serialized, "loose XamlWriter serialized HierarchicalDataTemplate root name");
        AssertContains("HierarchicalDataTemplate.Triggers", serialized, "loose XamlWriter serialized HierarchicalDataTemplate triggers");

        object roundTrippedDictionary = ParseLooseXaml(presentationFramework, serialized);
        object template = GetDictionaryValue(roundTrippedDictionary, "WriterNodeTemplate");
        AssertType(template, "System.Windows.HierarchicalDataTemplate", "loose XamlWriter round-trip HierarchicalDataTemplate");
        AssertBindingObjectPath(GetProperty(template, "ItemsSource"), "Children", "loose XamlWriter round-trip HierarchicalDataTemplate ItemsSource path");

        object triggers = GetProperty(template, "Triggers");
        AssertCollectionCount(triggers, expected: 1, "loose XamlWriter round-trip HierarchicalDataTemplate triggers");
        object trigger = GetCollectionItem(triggers, 0);
        AssertType(trigger, "System.Windows.DataTrigger", "loose XamlWriter round-trip HierarchicalDataTemplate trigger");
        AssertBindingObjectPath(GetProperty(trigger, "Binding"), "IsExpanded", "loose XamlWriter round-trip HierarchicalDataTemplate trigger binding path");
        AssertEqual("True", GetProperty(trigger, "Value").ToString(), "loose XamlWriter round-trip HierarchicalDataTemplate trigger value");
        object setters = GetProperty(trigger, "Setters");
        AssertCollectionCount(setters, expected: 1, "loose XamlWriter round-trip HierarchicalDataTemplate trigger setters");
        object setter = GetCollectionItem(setters, 0);
        AssertLooseStyleSetter(setter, "Tag", "expanded writer node", "HierarchicalDataTemplate trigger Tag setter");
        AssertEqual("NodeNameText", GetProperty(setter, "TargetName"), "loose XamlWriter round-trip HierarchicalDataTemplate trigger setter target");

        object templateRoot = Invoke(template, "LoadContent");
        AssertType(templateRoot, "System.Windows.Controls.StackPanel", "loose XamlWriter round-trip HierarchicalDataTemplate root");
        AssertEqual("NodeTemplateRoot", GetProperty(templateRoot, "Name"), "loose XamlWriter round-trip HierarchicalDataTemplate root name");
        AssertEqual("writer hierarchical template root", GetProperty(templateRoot, "Tag"), "loose XamlWriter round-trip HierarchicalDataTemplate root tag");

        object children = GetProperty(templateRoot, "Children");
        AssertCollectionCount(children, expected: 2, "loose XamlWriter round-trip HierarchicalDataTemplate children");
        object nameText = GetCollectionItem(children, 0);
        object countText = GetCollectionItem(children, 1);
        AssertType(nameText, "System.Windows.Controls.TextBlock", "loose XamlWriter round-trip HierarchicalDataTemplate name TextBlock");
        AssertType(countText, "System.Windows.Controls.TextBlock", "loose XamlWriter round-trip HierarchicalDataTemplate count TextBlock");
        AssertEqual("NodeNameText", GetProperty(nameText, "Name"), "loose XamlWriter round-trip HierarchicalDataTemplate name TextBlock name");
        AssertEqual("NodeCountText", GetProperty(countText, "Name"), "loose XamlWriter round-trip HierarchicalDataTemplate count TextBlock name");
    }

    private static void ValidateLooseXamlWriterItemsPanelTemplateRoundTrip(Assembly presentationFramework)
    {
        const string templateDictionaryXaml = """
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <ItemsPanelTemplate x:Key="WriterItemsPanelTemplate">
        <WrapPanel
            x:Name="WriterItemsHostPanel"
            Orientation="Horizontal"
            ItemWidth="48"
            ItemHeight="24"
            Tag="writer items panel" />
    </ItemsPanelTemplate>
</ResourceDictionary>
""";

        object dictionary = ParseLooseXaml(presentationFramework, templateDictionaryXaml);
        string serialized = SaveLooseXaml(presentationFramework, dictionary);
        AssertContains("ItemsPanelTemplate", serialized, "loose XamlWriter serialized ItemsPanelTemplate");
        AssertContains("WriterItemsPanelTemplate", serialized, "loose XamlWriter serialized ItemsPanelTemplate key");
        AssertContains("WrapPanel", serialized, "loose XamlWriter serialized ItemsPanelTemplate panel");
        AssertContains("WriterItemsHostPanel", serialized, "loose XamlWriter serialized ItemsPanelTemplate panel name");

        object roundTrippedDictionary = ParseLooseXaml(presentationFramework, serialized);
        object template = GetDictionaryValue(roundTrippedDictionary, "WriterItemsPanelTemplate");
        AssertType(template, "System.Windows.Controls.ItemsPanelTemplate", "loose XamlWriter round-trip ItemsPanelTemplate");

        object panel = Invoke(template, "LoadContent");
        AssertType(panel, "System.Windows.Controls.WrapPanel", "loose XamlWriter round-trip ItemsPanelTemplate panel");
        AssertEqual("WriterItemsHostPanel", GetProperty(panel, "Name"), "loose XamlWriter round-trip ItemsPanelTemplate panel name");
        AssertEqual("writer items panel", GetProperty(panel, "Tag"), "loose XamlWriter round-trip ItemsPanelTemplate panel tag");
        AssertEqual("Horizontal", GetProperty(panel, "Orientation").ToString(), "loose XamlWriter round-trip ItemsPanelTemplate orientation");
        AssertEqual(48.0, GetProperty(panel, "ItemWidth"), "loose XamlWriter round-trip ItemsPanelTemplate item width");
        AssertEqual(24.0, GetProperty(panel, "ItemHeight"), "loose XamlWriter round-trip ItemsPanelTemplate item height");
    }

    private static void ValidateLooseXamlWriterGroupStyleRoundTrip(Assembly presentationFramework)
    {
        const string groupStyleDictionaryXaml = """
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <GroupStyle
        x:Key="WriterGroupStyle"
        HidesIfEmpty="True">
        <GroupStyle.HeaderTemplate>
            <DataTemplate>
                <StackPanel
                    x:Name="WriterGroupHeaderRoot"
                    Tag="writer group header root">
                    <TextBlock
                        x:Name="WriterGroupHeaderText"
                        Text="{Binding Name}"
                        Tag="writer group header text" />
                </StackPanel>
            </DataTemplate>
        </GroupStyle.HeaderTemplate>
        <GroupStyle.Panel>
            <ItemsPanelTemplate>
                <StackPanel
                    x:Name="WriterGroupItemsPanel"
                    Orientation="Horizontal"
                    Tag="writer group panel" />
            </ItemsPanelTemplate>
        </GroupStyle.Panel>
    </GroupStyle>
</ResourceDictionary>
""";

        object dictionary = ParseLooseXaml(presentationFramework, groupStyleDictionaryXaml);
        object parsedGroupStyle = GetDictionaryValue(dictionary, "WriterGroupStyle");
        AssertType(parsedGroupStyle, "System.Windows.Controls.GroupStyle", "loose XamlReader GroupStyle");
        AssertEqual(true, GetProperty(parsedGroupStyle, "HidesIfEmpty"), "loose XamlReader GroupStyle HidesIfEmpty");
        object parsedHeaderTemplate = GetProperty(parsedGroupStyle, "HeaderTemplate");
        AssertType(parsedHeaderTemplate, "System.Windows.DataTemplate", "loose XamlReader GroupStyle HeaderTemplate");
        object parsedHeaderRoot = Invoke(parsedHeaderTemplate, "LoadContent");
        AssertType(parsedHeaderRoot, "System.Windows.Controls.StackPanel", "loose XamlReader GroupStyle header root");
        object parsedHeaderChildren = GetProperty(parsedHeaderRoot, "Children");
        AssertCollectionCount(parsedHeaderChildren, expected: 1, "loose XamlReader GroupStyle header children");
        AssertBindingPath(GetCollectionItem(parsedHeaderChildren, 0), "TextProperty", "Name", "loose XamlReader GroupStyle header binding path");
        object parsedPanelTemplate = GetProperty(parsedGroupStyle, "Panel");
        AssertType(parsedPanelTemplate, "System.Windows.Controls.ItemsPanelTemplate", "loose XamlReader GroupStyle Panel");
        object parsedPanel = Invoke(parsedPanelTemplate, "LoadContent");
        AssertType(parsedPanel, "System.Windows.Controls.StackPanel", "loose XamlReader GroupStyle panel root");
        AssertEqual("Horizontal", GetProperty(parsedPanel, "Orientation").ToString(), "loose XamlReader GroupStyle panel orientation");

        string serialized = SaveLooseXaml(presentationFramework, dictionary);
        AssertContains("GroupStyle", serialized, "loose XamlWriter serialized GroupStyle");
        AssertContains("WriterGroupStyle", serialized, "loose XamlWriter serialized GroupStyle key");
        AssertContains("GroupStyle.HeaderTemplate", serialized, "loose XamlWriter serialized GroupStyle HeaderTemplate");
        AssertContains("GroupStyle.Panel", serialized, "loose XamlWriter serialized GroupStyle Panel");
        AssertContains("WriterGroupHeaderRoot", serialized, "loose XamlWriter serialized GroupStyle header root name");
        AssertContains("WriterGroupItemsPanel", serialized, "loose XamlWriter serialized GroupStyle panel name");

        object roundTrippedDictionary = ParseLooseXaml(presentationFramework, serialized);
        object groupStyle = GetDictionaryValue(roundTrippedDictionary, "WriterGroupStyle");
        AssertType(groupStyle, "System.Windows.Controls.GroupStyle", "loose XamlWriter round-trip GroupStyle");
        AssertEqual(true, GetProperty(groupStyle, "HidesIfEmpty"), "loose XamlWriter round-trip GroupStyle HidesIfEmpty");

        object headerTemplate = GetProperty(groupStyle, "HeaderTemplate");
        AssertType(headerTemplate, "System.Windows.DataTemplate", "loose XamlWriter round-trip GroupStyle HeaderTemplate");
        object headerRoot = Invoke(headerTemplate, "LoadContent");
        AssertType(headerRoot, "System.Windows.Controls.StackPanel", "loose XamlWriter round-trip GroupStyle header root");
        AssertEqual("WriterGroupHeaderRoot", GetProperty(headerRoot, "Name"), "loose XamlWriter round-trip GroupStyle header root name");
        AssertEqual("writer group header root", GetProperty(headerRoot, "Tag"), "loose XamlWriter round-trip GroupStyle header root tag");
        object headerChildren = GetProperty(headerRoot, "Children");
        AssertCollectionCount(headerChildren, expected: 1, "loose XamlWriter round-trip GroupStyle header children");
        object headerText = GetCollectionItem(headerChildren, 0);
        AssertType(headerText, "System.Windows.Controls.TextBlock", "loose XamlWriter round-trip GroupStyle header TextBlock");
        AssertEqual("WriterGroupHeaderText", GetProperty(headerText, "Name"), "loose XamlWriter round-trip GroupStyle header TextBlock name");
        AssertEqual("writer group header text", GetProperty(headerText, "Tag"), "loose XamlWriter round-trip GroupStyle header TextBlock tag");

        object panelTemplate = GetProperty(groupStyle, "Panel");
        AssertType(panelTemplate, "System.Windows.Controls.ItemsPanelTemplate", "loose XamlWriter round-trip GroupStyle Panel");
        object panel = Invoke(panelTemplate, "LoadContent");
        AssertType(panel, "System.Windows.Controls.StackPanel", "loose XamlWriter round-trip GroupStyle panel");
        AssertEqual("WriterGroupItemsPanel", GetProperty(panel, "Name"), "loose XamlWriter round-trip GroupStyle panel name");
        AssertEqual("writer group panel", GetProperty(panel, "Tag"), "loose XamlWriter round-trip GroupStyle panel tag");
        AssertEqual("Horizontal", GetProperty(panel, "Orientation").ToString(), "loose XamlWriter round-trip GroupStyle panel orientation");
    }

    private static void ValidateLooseXamlWriterFrameworkElementRoundTrip(Assembly presentationFramework)
    {
        const string frameworkElementXaml = """
<StackPanel
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Orientation="Vertical"
    Tag="writer root">
    <Button
        Name="WriterButton"
        Content="writer button"
        Tag="writer button tag"
        Width="120"
        Height="32"
        Padding="5,6,7,8"
        Background="#224466" />
    <TextBox
        Name="WriterTextBox"
        Text="writer text"
        MinWidth="80" />
</StackPanel>
""";

        object root = ParseLooseXaml(presentationFramework, frameworkElementXaml);
        string serialized = SaveLooseXaml(presentationFramework, root);
        AssertContains("StackPanel", serialized, "loose XamlWriter serialized FrameworkElement root");
        AssertContains("Button", serialized, "loose XamlWriter serialized FrameworkElement Button");
        AssertContains("TextBox", serialized, "loose XamlWriter serialized FrameworkElement TextBox");
        AssertContains("writer button", serialized, "loose XamlWriter serialized FrameworkElement Button content");
        AssertContains("writer text", serialized, "loose XamlWriter serialized FrameworkElement TextBox text");

        object roundTrippedRoot = ParseLooseXaml(presentationFramework, serialized);
        AssertType(roundTrippedRoot, "System.Windows.Controls.StackPanel", "loose XamlWriter round-trip FrameworkElement root");
        AssertEqual("Vertical", GetProperty(roundTrippedRoot, "Orientation").ToString(), "loose XamlWriter round-trip StackPanel orientation");
        AssertEqual("writer root", GetProperty(roundTrippedRoot, "Tag"), "loose XamlWriter round-trip StackPanel tag");

        object children = GetProperty(roundTrippedRoot, "Children");
        AssertCollectionCount(children, expected: 2, "loose XamlWriter round-trip FrameworkElement children");
        object button = GetCollectionItem(children, 0);
        AssertType(button, "System.Windows.Controls.Button", "loose XamlWriter round-trip Button");
        AssertEqual("WriterButton", GetProperty(button, "Name"), "loose XamlWriter round-trip Button name");
        AssertEqual("writer button", GetProperty(button, "Content"), "loose XamlWriter round-trip Button content");
        AssertEqual("writer button tag", GetProperty(button, "Tag"), "loose XamlWriter round-trip Button tag");
        AssertEqual(120.0, GetProperty(button, "Width"), "loose XamlWriter round-trip Button width");
        AssertEqual(32.0, GetProperty(button, "Height"), "loose XamlWriter round-trip Button height");
        AssertEqual("#FF224466", GetProperty(GetProperty(button, "Background"), "Color").ToString(), "loose XamlWriter round-trip Button background");

        object textBox = GetCollectionItem(children, 1);
        AssertType(textBox, "System.Windows.Controls.TextBox", "loose XamlWriter round-trip TextBox");
        AssertEqual("WriterTextBox", GetProperty(textBox, "Name"), "loose XamlWriter round-trip TextBox name");
        AssertEqual("writer text", GetProperty(textBox, "Text"), "loose XamlWriter round-trip TextBox text");
        AssertEqual(80.0, GetProperty(textBox, "MinWidth"), "loose XamlWriter round-trip TextBox MinWidth");
    }

    private static void ValidateLooseXamlWriterFlowDocumentRoundTrip(Assembly presentationFramework)
    {
        const string flowDocumentXaml = """
<FlowDocument
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    FontSize="14"
    Tag="writer document">
    <Paragraph
        Name="WriterParagraph"
        Tag="writer paragraph">
        writer paragraph <Bold>bold text</Bold><Italic> italic text</Italic><Underline> underline text</Underline>
        <Hyperlink NavigateUri="https://example.test/progpu-wpf-writer">link text</Hyperlink>
    </Paragraph>
    <Section Name="WriterSection">
        <Paragraph>section writer text</Paragraph>
    </Section>
    <Table CellSpacing="2">
        <Table.Columns>
            <TableColumn />
            <TableColumn />
        </Table.Columns>
        <TableRowGroup>
            <TableRow>
                <TableCell>
                    <Paragraph>table writer alpha</Paragraph>
                </TableCell>
                <TableCell>
                    <Paragraph>table writer beta</Paragraph>
                </TableCell>
            </TableRow>
        </TableRowGroup>
    </Table>
    <List MarkerStyle="Decimal">
        <ListItem>
            <Paragraph>first writer item</Paragraph>
        </ListItem>
        <ListItem>
            <Paragraph>second writer item</Paragraph>
        </ListItem>
    </List>
</FlowDocument>
""";

        object document = ParseLooseXaml(presentationFramework, flowDocumentXaml);
        string serialized = SaveLooseXaml(presentationFramework, document);
        AssertContains("FlowDocument", serialized, "loose XamlWriter serialized FlowDocument root");
        AssertContains("Paragraph", serialized, "loose XamlWriter serialized FlowDocument Paragraph");
        AssertContains("WriterParagraph", serialized, "loose XamlWriter serialized FlowDocument paragraph name");
        AssertContains("Bold", serialized, "loose XamlWriter serialized FlowDocument Bold");
        AssertContains("Hyperlink", serialized, "loose XamlWriter serialized FlowDocument Hyperlink");
        AssertContains("Table", serialized, "loose XamlWriter serialized FlowDocument Table");
        AssertContains("List", serialized, "loose XamlWriter serialized FlowDocument List");

        if (serialized.Contains(" Name=\"\"", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected loose XamlWriter serialized FlowDocument not to emit empty runtime names, got '{serialized}'.");
        }

        object roundTrippedDocument = ParseLooseXaml(presentationFramework, serialized);
        AssertType(roundTrippedDocument, "System.Windows.Documents.FlowDocument", "loose XamlWriter round-trip FlowDocument");
        AssertEqual(14.0, GetProperty(roundTrippedDocument, "FontSize"), "loose XamlWriter round-trip FlowDocument font size");
        AssertEqual("writer document", GetProperty(roundTrippedDocument, "Tag"), "loose XamlWriter round-trip FlowDocument tag");

        object blocks = GetProperty(roundTrippedDocument, "Blocks");
        AssertCollectionCount(blocks, expected: 4, "loose XamlWriter round-trip FlowDocument blocks");

        object paragraph = GetCollectionItem(blocks, 0);
        AssertType(paragraph, "System.Windows.Documents.Paragraph", "loose XamlWriter round-trip FlowDocument paragraph");
        AssertEqual("WriterParagraph", GetProperty(paragraph, "Name"), "loose XamlWriter round-trip FlowDocument paragraph name");
        AssertEqual("writer paragraph", GetProperty(paragraph, "Tag"), "loose XamlWriter round-trip FlowDocument paragraph tag");

        object paragraphInlines = GetProperty(paragraph, "Inlines");
        object bold = GetFirstCollectionItemOfType(paragraphInlines, "System.Windows.Documents.Bold", "loose XamlWriter round-trip FlowDocument bold inline");
        object boldRun = GetFirstCollectionItemOfType(GetProperty(bold, "Inlines"), "System.Windows.Documents.Run", "loose XamlWriter round-trip FlowDocument bold run");
        AssertEqual("bold text", GetProperty(boldRun, "Text"), "loose XamlWriter round-trip FlowDocument bold text");
        object italic = GetFirstCollectionItemOfType(paragraphInlines, "System.Windows.Documents.Italic", "loose XamlWriter round-trip FlowDocument italic inline");
        object italicRun = GetFirstCollectionItemOfType(GetProperty(italic, "Inlines"), "System.Windows.Documents.Run", "loose XamlWriter round-trip FlowDocument italic run");
        AssertEqual("italic text", GetProperty(italicRun, "Text"), "loose XamlWriter round-trip FlowDocument italic text");
        object underline = GetFirstCollectionItemOfType(paragraphInlines, "System.Windows.Documents.Underline", "loose XamlWriter round-trip FlowDocument underline inline");
        object underlineRun = GetFirstCollectionItemOfType(GetProperty(underline, "Inlines"), "System.Windows.Documents.Run", "loose XamlWriter round-trip FlowDocument underline run");
        AssertEqual("underline text", GetProperty(underlineRun, "Text"), "loose XamlWriter round-trip FlowDocument underline text");
        object hyperlink = GetFirstCollectionItemOfType(paragraphInlines, "System.Windows.Documents.Hyperlink", "loose XamlWriter round-trip FlowDocument hyperlink");
        AssertEqual("https://example.test/progpu-wpf-writer", GetProperty(hyperlink, "NavigateUri").ToString(), "loose XamlWriter round-trip FlowDocument hyperlink URI");

        object section = GetCollectionItem(blocks, 1);
        AssertType(section, "System.Windows.Documents.Section", "loose XamlWriter round-trip FlowDocument section");
        AssertEqual("WriterSection", GetProperty(section, "Name"), "loose XamlWriter round-trip FlowDocument section name");

        object table = GetCollectionItem(blocks, 2);
        AssertType(table, "System.Windows.Documents.Table", "loose XamlWriter round-trip FlowDocument table");
        AssertCollectionCount(GetProperty(table, "Columns"), expected: 2, "loose XamlWriter round-trip FlowDocument table columns");

        object list = GetCollectionItem(blocks, 3);
        AssertType(list, "System.Windows.Documents.List", "loose XamlWriter round-trip FlowDocument list");
        AssertEqual("Decimal", GetProperty(list, "MarkerStyle").ToString(), "loose XamlWriter round-trip FlowDocument list marker style");
        AssertCollectionCount(GetProperty(list, "ListItems"), expected: 2, "loose XamlWriter round-trip FlowDocument list items");

        object textRange = Create(
            roundTrippedDocument.GetType().Assembly,
            "System.Windows.Documents.TextRange",
            GetProperty(roundTrippedDocument, "ContentStart"),
            GetProperty(roundTrippedDocument, "ContentEnd"));
        string text = GetProperty(textRange, "Text").ToString() ?? string.Empty;
        AssertContains("writer paragraph", text, "loose XamlWriter round-trip FlowDocument TextRange paragraph text");
        AssertContains("bold text", text, "loose XamlWriter round-trip FlowDocument TextRange bold text");
        AssertContains("italic text", text, "loose XamlWriter round-trip FlowDocument TextRange italic text");
        AssertContains("underline text", text, "loose XamlWriter round-trip FlowDocument TextRange underline text");
        AssertContains("link text", text, "loose XamlWriter round-trip FlowDocument TextRange hyperlink text");
        AssertContains("section writer text", text, "loose XamlWriter round-trip FlowDocument TextRange section text");
        AssertContains("table writer alpha", text, "loose XamlWriter round-trip FlowDocument TextRange first table cell");
        AssertContains("table writer beta", text, "loose XamlWriter round-trip FlowDocument TextRange second table cell");
        AssertContains("first writer item", text, "loose XamlWriter round-trip FlowDocument TextRange first list item");
        AssertContains("second writer item", text, "loose XamlWriter round-trip FlowDocument TextRange second list item");
    }

    private static void AssertLooseStyleSetter(object setter, string expectedPropertyName, object expectedValue, string description)
    {
        AssertType(setter, "System.Windows.Setter", $"loose XamlWriter round-trip {description}");
        AssertEqual(expectedPropertyName, GetProperty(GetProperty(setter, "Property"), "Name"), $"loose XamlWriter round-trip {description} property");
        AssertEqual(expectedValue, GetProperty(setter, "Value"), $"loose XamlWriter round-trip {description} value");
    }

    private static void ValidateLooseGradientStop(object stop, string expectedColor, double expectedOffset, string description)
    {
        AssertType(stop, "System.Windows.Media.GradientStop", $"loose XamlWriter round-trip {description} stop");
        AssertEqual(expectedColor, GetProperty(stop, "Color").ToString(), $"loose XamlWriter round-trip {description} stop color");
        AssertEqual(expectedOffset, GetProperty(stop, "Offset"), $"loose XamlWriter round-trip {description} stop offset");
    }

    private static string SaveLooseXaml(Assembly presentationFramework, object value)
    {
        Type xamlWriterType = GetRequiredType(presentationFramework, "System.Windows.Markup.XamlWriter");
        MethodInfo save = xamlWriterType.GetMethod(
                "Save",
                BindingFlags.Static | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(object) },
                modifiers: null)
            ?? throw new MissingMethodException(xamlWriterType.FullName, "Save");

        return save.Invoke(null, new[] { value }) as string
            ?? throw new InvalidOperationException("Loose XamlWriter.Save returned null.");
    }

    private static void ValidateSystemXamlNameScopeDictionary(Assembly systemXaml)
    {
        Type dictionaryType = GetRequiredType(systemXaml, "System.Xaml.NameScopeDictionary");
        object standaloneDictionary = CreateInternal(dictionaryType);
        ValidateNameScopeDictionaryContract(standaloneDictionary, "standalone System.Xaml NameScopeDictionary");

        Type nameScopeType = GetRequiredType(systemXaml, "System.Xaml.NameScope");
        object underlyingNameScope = CreateInternal(nameScopeType);
        object wrappedDictionary = CreateInternal(dictionaryType, underlyingNameScope);
        object externalOnlyValue = new object();
        Invoke(underlyingNameScope, "RegisterName", "ExternalOnlyName", externalOnlyValue);
        AssertSame(externalOnlyValue, Invoke(wrappedDictionary, "FindName", "ExternalOnlyName"), "wrapped System.Xaml NameScopeDictionary external FindName");
        AssertEqual(false, ((IDictionary<string, object>)wrappedDictionary).ContainsKey("ExternalOnlyName"), "wrapped System.Xaml NameScopeDictionary external key stays out of dictionary view");
        ValidateNameScopeDictionaryContract(wrappedDictionary, "wrapped System.Xaml NameScopeDictionary");
        AssertSame(externalOnlyValue, Invoke(wrappedDictionary, "FindName", "ExternalOnlyName"), "wrapped System.Xaml NameScopeDictionary clear preserves external name");
        Invoke(underlyingNameScope, "UnregisterName", "ExternalOnlyName");

        var wrapped = (IDictionary<string, object>)wrappedDictionary;
        object finalValue = new object();
        wrapped.Add("FinalName", finalValue);
        AssertSame(finalValue, Invoke(underlyingNameScope, "FindName", "FinalName"), "wrapped System.Xaml NameScopeDictionary underlying registration");
        wrapped.Clear();
        AssertEqual(null, InvokeNullable(underlyingNameScope, "FindName", "FinalName"), "wrapped System.Xaml NameScopeDictionary clear unregisters underlying name");
    }

    private static void ValidateNameScopeDictionaryContract(object nameScopeDictionary, string description)
    {
        var dictionary = (IDictionary<string, object>)nameScopeDictionary;
        var collection = (ICollection<KeyValuePair<string, object>>)nameScopeDictionary;

        AssertEqual(0, collection.Count, $"{description} initial count");
        AssertEqual(false, collection.IsReadOnly, $"{description} read/write flag");

        object first = new object();
        dictionary.Add("FirstName", first);
        AssertEqual(1, collection.Count, $"{description} add count");
        AssertEqual(true, dictionary.ContainsKey("FirstName"), $"{description} contains key");
        AssertSame(first, dictionary["FirstName"], $"{description} indexer getter");
        AssertEqual(true, dictionary.TryGetValue("FirstName", out object? foundFirst), $"{description} try-get result");
        AssertSame(first, foundFirst!, $"{description} try-get value");
        AssertCollectionCount(dictionary.Keys, expected: 1, $"{description} keys");
        AssertCollectionCount(dictionary.Values, expected: 1, $"{description} values");

        var copied = new KeyValuePair<string, object>[2];
        collection.CopyTo(copied, 1);
        AssertEqual("FirstName", copied[1].Key, $"{description} copied key");
        AssertSame(first, copied[1].Value, $"{description} copied value");
        AssertEqual(true, collection.Contains(new KeyValuePair<string, object>("FirstName", first)), $"{description} key/value contains");
        AssertEqual(false, collection.Contains(new KeyValuePair<string, object>("FirstName", new object())), $"{description} mismatched key/value contains");

        object second = new object();
        collection.Add(new KeyValuePair<string, object>("SecondName", second));
        AssertEqual(2, collection.Count, $"{description} collection add count");
        AssertEqual(true, dictionary.Remove("SecondName"), $"{description} key remove");
        AssertEqual(false, dictionary.Remove("MissingName"), $"{description} missing key remove");
        AssertEqual(false, dictionary.TryGetValue("SecondName", out object? missing), $"{description} removed try-get result");
        AssertEqual(null, missing, $"{description} removed try-get value");

        AssertEqual(false, collection.Remove(new KeyValuePair<string, object>("FirstName", new object())), $"{description} mismatched key/value remove");
        AssertEqual(true, collection.Remove(new KeyValuePair<string, object>("FirstName", first)), $"{description} key/value remove");
        AssertEqual(0, collection.Count, $"{description} empty count after remove");

        object third = new object();
        dictionary["ThirdName"] = third;
        dictionary["ThirdName"] = third;
        AssertEqual(1, collection.Count, $"{description} duplicate same-object registration count");
        AssertSame(third, dictionary["ThirdName"], $"{description} duplicate same-object registration value");

        try
        {
            dictionary["ThirdName"] = new object();
            throw new InvalidOperationException($"Expected {description} duplicate replacement to throw.");
        }
        catch (ArgumentException)
        {
        }

        collection.Clear();
        AssertEqual(0, collection.Count, $"{description} clear count");
        AssertEqual(false, dictionary.ContainsKey("ThirdName"), $"{description} clear removed name");
    }

    private static void ValidateMainWindow(Assembly presentationCore, object window, object application)
    {
        AssertType(window, MainWindowTypeName, "startup window");
        AssertEqual("ProGPU WPF XAML smoke", GetProperty(window, "Title"), "window title");
        AssertEqual(420.0, GetProperty(window, "Width"), "window width");
        AssertEqual(340.0, GetProperty(window, "Height"), "window height");

        object content = GetProperty(window, "Content");
        AssertType(content, "System.Windows.Controls.StackPanel", "window content");
        object children = GetProperty(content, "Children");
        AssertCollectionCount(children, expected: 89, "stack panel children");

        object textBlock = GetCollectionItem(children, 0);
        AssertType(textBlock, "System.Windows.Controls.TextBlock", "compiled TextBlock");
        AssertEqual("Real WPF XAML compiler smoke", GetProperty(textBlock, "Text"), "compiled TextBlock text");
        AssertEqual("#FF356D9E", GetProperty(GetProperty(textBlock, "Foreground"), "Color").ToString(), "compiled TextBlock foreground");

        object inputBox = GetField(window, "InputBox");
        AssertType(inputBox, "System.Windows.Controls.TextBox", "compiled named TextBox");
        AssertEqual("compiled TextBox", GetProperty(inputBox, "Text"), "compiled TextBox text");
        ValidateTextBoxSelection(inputBox);
        ValidatePasswordBox(window);

        object resources = GetProperty(application, "Resources");
        object expectedStyle = GetDictionaryValue(resources, "SmokeTextBoxStyle");
        object actualStyle = GetProperty(inputBox, "Style");
        AssertSame(expectedStyle, actualStyle, "compiled TextBox style");

        object basedOnTextBox = GetField(window, "BasedOnTextBox");
        AssertType(basedOnTextBox, "System.Windows.Controls.TextBox", "compiled BasedOn TextBox");
        AssertEqual("compiled BasedOn TextBox", GetProperty(basedOnTextBox, "Text"), "compiled BasedOn TextBox text");
        object basedOnStyle = GetDictionaryValue(resources, "BasedOnTextBoxStyle");
        AssertSame(basedOnStyle, GetProperty(basedOnTextBox, "Style"), "compiled TextBox BasedOn style");
        AssertSame(expectedStyle, GetProperty(basedOnStyle, "BasedOn"), "compiled TextBox BasedOn base style");
        AssertEqual("based on text box style", GetProperty(basedOnTextBox, "Tag"), "compiled TextBox BasedOn local setter");
        AssertEqual(180.0, GetProperty(basedOnTextBox, "MinWidth"), "compiled TextBox BasedOn inherited MinWidth");
        object basedOnMargin = GetProperty(basedOnTextBox, "Margin");
        AssertEqual(8.0, GetProperty(basedOnMargin, "Top"), "compiled TextBox BasedOn inherited margin top");

        object foundInputBox = Invoke(window, "FindName", "InputBox");
        AssertSame(inputBox, foundInputBox, "compiled namescope lookup");
        ValidateRuntimeNameScope(window, inputBox);

        ValidateRichFlowDocument(window);

        ValidateBindingAndCommand(window);
        ValidateAdvancedBindingFeatures(window);
        ValidateObjectDataProvider(window);
        ValidateXmlDataProvider(window);
        ValidateStoryboardEventTrigger(window);
        ValidateMarkupExtension(window);
        ValidateMergedResourceDictionary(window, application);
        ValidateScopedResourceLookup(window, application);
        ValidateUnsharedResource(window, application);
        ValidateNestedUserControl(window);
        ValidateReadOnlyGridCollectionsAndAttachedProperties(window);
        ValidateLayoutPanels(window);
        ValidateScrollingControls(window);
        ValidateDateSelectionControls(window);
        ValidateImplicitMergedStyle(window, application);
        ValidateToggleChoiceControls(window);
        ValidateXamlEventHandler(window);
        ValidateRepeatButton(window);
        ValidateThumbDragManager(window);
        ValidateStyleEventSetter(window);
        ValidateRoutedCommand(window);
        ValidateInputBinding(window);
        ValidateMouseBinding(window);
        ValidateMenuItems(window);
        ValidateContextMenuAndToolTip(window);
        ValidateToolBarAndStatusBar(window);
        ValidateRangeControls(window);
        ValidateStyleAndDataTrigger(window, application);
        ValidateTemplateAndDynamicResource(window, application);
        ValidateItemsBindingAndTemplate(window);
        ValidateComboBox(window);
        ValidateSelectorSelectionChangedEvents(window);
        ValidateListViewGridView(window);
        ValidateDataGrid(window);
        ValidateImplicitDataTemplate(window);
        ValidateContentTemplateSelector(window);
        ValidateHierarchicalDataTemplate(window);
        ValidateExplicitTreeViewItems(window);
        ValidateTabControl(window);
        ValidateSectionControls(window);
        ValidateAdornerDecorator(window);
        ValidateDependencyPropertyCore(window);
        ValidateCustomRoutedEvent(window);
        ValidateClassRoutedEvent(window);
        ValidateAccessKeyFocusScope(presentationCore, window);
        ValidateNavigationFrame(window);
    }

    private static void ValidateTextBoxSelection(object inputBox)
    {
        Invoke(inputBox, "Select", 9, 7);
        AssertEqual(9, GetProperty(inputBox, "SelectionStart"), "compiled TextBox selection start");
        AssertEqual(7, GetProperty(inputBox, "SelectionLength"), "compiled TextBox selection length");
        AssertEqual("TextBox", GetProperty(inputBox, "SelectedText"), "compiled TextBox selected text");

        SetProperty(inputBox, "SelectedText", "selection");
        AssertEqual("compiled selection", GetProperty(inputBox, "Text"), "compiled TextBox selected text replacement");
        AssertEqual(9, GetProperty(inputBox, "SelectionStart"), "compiled TextBox replacement selection start");
        AssertEqual(9, GetProperty(inputBox, "SelectionLength"), "compiled TextBox replacement selection length");
        AssertEqual("selection", GetProperty(inputBox, "SelectedText"), "compiled TextBox replacement selected text");
    }

    private static void ValidatePasswordBox(object window)
    {
        object passwordBox = GetField(window, "CredentialBox");
        AssertType(passwordBox, "System.Windows.Controls.PasswordBox", "compiled PasswordBox");
        AssertEqual(12, GetProperty(passwordBox, "MaxLength"), "compiled PasswordBox max length");
        AssertEqual('#', GetProperty(passwordBox, "PasswordChar"), "compiled PasswordBox password char");
        AssertEqual(string.Empty, GetProperty(passwordBox, "Password"), "compiled PasswordBox initial password");
        object securePassword = GetProperty(passwordBox, "SecurePassword");
        AssertEqual(0, GetProperty(securePassword, "Length"), "compiled PasswordBox initial secure password length");
        AssertEqual(0, GetProperty(window, "PasswordChangedCount"), "compiled PasswordBox initial changed count");

        SetProperty(passwordBox, "Password", "secret42");

        AssertEqual("secret42", GetProperty(passwordBox, "Password"), "compiled PasswordBox updated password");
        securePassword = GetProperty(passwordBox, "SecurePassword");
        AssertEqual(8, GetProperty(securePassword, "Length"), "compiled PasswordBox secure password length");
        AssertEqual(1, GetProperty(window, "PasswordChangedCount"), "compiled PasswordBox PasswordChanged count");
        AssertEqual("CredentialBox", GetProperty(window, "LastPasswordChangedSenderName"), "compiled PasswordBox PasswordChanged sender");
        AssertEqual("PasswordChanged", GetProperty(window, "LastPasswordChangedRoutedEventName"), "compiled PasswordBox PasswordChanged routed event");

        Invoke(passwordBox, "Clear");

        AssertEqual(string.Empty, GetProperty(passwordBox, "Password"), "compiled PasswordBox cleared password");
        securePassword = GetProperty(passwordBox, "SecurePassword");
        AssertEqual(0, GetProperty(securePassword, "Length"), "compiled PasswordBox cleared secure password length");
        AssertEqual(2, GetProperty(window, "PasswordChangedCount"), "compiled PasswordBox clear changed count");
    }

    private static void ValidateRuntimeNameScope(object window, object frameworkElement)
    {
        Assembly presentationFramework = frameworkElement.GetType().Assembly;
        object registeredButton = Create(presentationFramework, "System.Windows.Controls.Button");
        SetProperty(registeredButton, "Name", "RuntimeRegisteredButton");
        SetProperty(registeredButton, "Content", "runtime registered");

        Invoke(window, "RegisterName", "RuntimeRegisteredButton", registeredButton);
        AssertSame(registeredButton, Invoke(window, "FindName", "RuntimeRegisteredButton"), "compiled namescope runtime registered lookup");

        object duplicateButton = Create(presentationFramework, "System.Windows.Controls.Button");
        SetProperty(duplicateButton, "Name", "RuntimeRegisteredButton");
        try
        {
            Invoke(window, "RegisterName", "RuntimeRegisteredButton", duplicateButton);
            throw new InvalidOperationException("Expected duplicate runtime name registration to throw.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException)
        {
        }

        AssertSame(registeredButton, Invoke(window, "FindName", "RuntimeRegisteredButton"), "compiled namescope duplicate preserves original");
        Invoke(window, "UnregisterName", "RuntimeRegisteredButton");

        object replacementButton = Create(presentationFramework, "System.Windows.Controls.Button");
        SetProperty(replacementButton, "Name", "RuntimeRegisteredButton");
        SetProperty(replacementButton, "Content", "runtime replacement");
        Invoke(window, "RegisterName", "RuntimeRegisteredButton", replacementButton);
        AssertSame(replacementButton, Invoke(window, "FindName", "RuntimeRegisteredButton"), "compiled namescope runtime re-register after unregister");
        Invoke(window, "UnregisterName", "RuntimeRegisteredButton");
    }

    private static void ValidateRichFlowDocument(object window)
    {
        object richTextBox = GetField(window, "DocumentBox");
        AssertType(richTextBox, "System.Windows.Controls.RichTextBox", "compiled RichTextBox");

        object flowDocument = GetProperty(richTextBox, "Document");
        AssertType(flowDocument, "System.Windows.Documents.FlowDocument", "compiled FlowDocument");

        object blocks = GetProperty(flowDocument, "Blocks");
        AssertCollectionCount(blocks, expected: 5, "compiled FlowDocument blocks");

        object introParagraph = GetCollectionItem(blocks, 0);
        AssertType(introParagraph, "System.Windows.Documents.Paragraph", "compiled FlowDocument intro paragraph");

        object inlines = GetProperty(introParagraph, "Inlines");

        object bold = GetFirstCollectionItemOfType(inlines, "System.Windows.Documents.Bold", "compiled FlowDocument bold inline");
        object boldRun = GetFirstCollectionItemOfType(GetProperty(bold, "Inlines"), "System.Windows.Documents.Run", "compiled FlowDocument bold run");
        AssertEqual("rich", GetProperty(boldRun, "Text"), "compiled FlowDocument bold run text");

        object italic = GetFirstCollectionItemOfType(inlines, "System.Windows.Documents.Italic", "compiled FlowDocument italic inline");
        object italicRun = GetFirstCollectionItemOfType(GetProperty(italic, "Inlines"), "System.Windows.Documents.Run", "compiled FlowDocument italic run");
        AssertEqual(" italic", GetProperty(italicRun, "Text"), "compiled FlowDocument italic run text");

        object underline = GetFirstCollectionItemOfType(inlines, "System.Windows.Documents.Underline", "compiled FlowDocument underline inline");
        object underlineRun = GetFirstCollectionItemOfType(GetProperty(underline, "Inlines"), "System.Windows.Documents.Run", "compiled FlowDocument underline run");
        AssertEqual(" underline", GetProperty(underlineRun, "Text"), "compiled FlowDocument underline run text");

        object span = GetFirstCollectionItemOfType(inlines, "System.Windows.Documents.Span", "compiled FlowDocument span inline");
        object spanRun = GetFirstCollectionItemOfType(GetProperty(span, "Inlines"), "System.Windows.Documents.Run", "compiled FlowDocument span run");
        AssertEqual(" span", GetProperty(spanRun, "Text"), "compiled FlowDocument span run text");

        GetFirstCollectionItemOfType(inlines, "System.Windows.Documents.LineBreak", "compiled FlowDocument line break inline");

        object hyperlink = GetFirstCollectionItemOfType(inlines, "System.Windows.Documents.Hyperlink", "compiled FlowDocument hyperlink");
        AssertEqual("https://example.test/progpu-wpf", GetProperty(hyperlink, "NavigateUri").ToString(), "compiled FlowDocument hyperlink URI");
        object hyperlinkRun = GetFirstCollectionItemOfType(GetProperty(hyperlink, "Inlines"), "System.Windows.Documents.Run", "compiled FlowDocument hyperlink run");
        AssertEqual("link", GetProperty(hyperlinkRun, "Text"), "compiled FlowDocument hyperlink run text");
        AssertEqual(0, GetProperty(window, "DocumentLinkRequestNavigateCount"), "compiled Hyperlink initial RequestNavigate count");
        Invoke(hyperlink, "DoClick");
        AssertEqual(1, GetProperty(window, "DocumentLinkRequestNavigateCount"), "compiled Hyperlink RequestNavigate handler count");
        AssertEqual("DocumentLink", GetProperty(window, "LastDocumentLinkRequestNavigateSenderName"), "compiled Hyperlink RequestNavigate sender");
        AssertEqual("https://example.test/progpu-wpf", GetProperty(window, "LastDocumentLinkRequestNavigateUri"), "compiled Hyperlink RequestNavigate URI");
        AssertEqual("RequestNavigate", GetProperty(window, "LastDocumentLinkRequestNavigateRoutedEventName"), "compiled Hyperlink RequestNavigate routed event");

        object figure = GetFirstCollectionItemOfType(inlines, "System.Windows.Documents.Figure", "compiled FlowDocument figure inline");
        AssertFlowDocumentAnchoredBlockText(figure, "figure anchored text", "figure");

        object floater = GetFirstCollectionItemOfType(inlines, "System.Windows.Documents.Floater", "compiled FlowDocument floater inline");
        AssertFlowDocumentAnchoredBlockText(floater, "floater anchored text", "floater");

        object inlineContainer = GetFirstCollectionItemOfType(inlines, "System.Windows.Documents.InlineUIContainer", "compiled FlowDocument inline UI container");
        object inlineButton = GetProperty(inlineContainer, "Child");
        AssertType(inlineButton, "System.Windows.Controls.Button", "compiled FlowDocument inline Button");
        AssertEqual("inline document button", GetProperty(inlineButton, "Content"), "compiled FlowDocument inline Button content");

        object selection = GetProperty(richTextBox, "Selection");
        Invoke(selection, "Select", GetProperty(boldRun, "ContentStart"), GetProperty(boldRun, "ContentEnd"));
        AssertEqual("rich", (GetProperty(selection, "Text").ToString() ?? string.Empty).Trim(), "compiled RichTextBox selection text");

        Invoke(selection, "Select", GetProperty(spanRun, "ContentStart"), GetProperty(spanRun, "ContentEnd"));
        AssertEqual("span", (GetProperty(selection, "Text").ToString() ?? string.Empty).Trim(), "compiled RichTextBox command selection text");
        Assembly documentAssembly = flowDocument.GetType().Assembly;
        Type textEditorType = GetRequiredType(documentAssembly, "System.Windows.Documents.TextEditor");
        SetStaticProperty(textEditorType, "IsTableEditingEnabled", true);
        AssertEqual(true, GetStaticProperty(textEditorType, "IsTableEditingEnabled"), "compiled RichTextBox table editing gate");
        InvokeStatic(
            GetRequiredType(documentAssembly, "System.Windows.Documents.TextEditorTables"),
            "_RegisterClassHandlers",
            richTextBox.GetType(),
            false);
        Type editingCommandsType = GetRequiredType(documentAssembly, "System.Windows.Documents.EditingCommands");
        Type inlineType = GetRequiredType(documentAssembly, "System.Windows.Documents.Inline");
        Type textElementType = GetRequiredType(documentAssembly, "System.Windows.Documents.TextElement");
        Type typographyType = GetRequiredType(documentAssembly, "System.Windows.Documents.Typography");
        object fontWeightProperty = GetStaticField(textElementType, "FontWeightProperty");
        object fontStyleProperty = GetStaticField(textElementType, "FontStyleProperty");
        object fontSizeProperty = GetStaticField(textElementType, "FontSizeProperty");
        object fontFamilyProperty = GetStaticField(textElementType, "FontFamilyProperty");
        object foregroundProperty = GetStaticField(textElementType, "ForegroundProperty");
        object backgroundProperty = GetStaticField(textElementType, "BackgroundProperty");
        object textDecorationsProperty = GetStaticField(inlineType, "TextDecorationsProperty");
        object inlineFlowDirectionProperty = GetStaticField(inlineType, "FlowDirectionProperty");
        object variantsProperty = GetStaticField(typographyType, "VariantsProperty");
        object toggleBoldCommand = GetStaticProperty(editingCommandsType, "ToggleBold");
        AssertEqual(true, InvokeTwoArgumentCommand(toggleBoldCommand, "CanExecute", null, richTextBox), "compiled RichTextBox ToggleBold CanExecute");
        InvokeTwoArgumentCommand(toggleBoldCommand, "Execute", null, richTextBox);
        AssertEqual("Bold", Invoke(selection, "GetCurrentValue", fontWeightProperty).ToString(), "compiled RichTextBox ToggleBold applied weight");
        InvokeTwoArgumentCommand(toggleBoldCommand, "Execute", null, richTextBox);
        AssertEqual("Normal", Invoke(selection, "GetCurrentValue", fontWeightProperty).ToString(), "compiled RichTextBox ToggleBold restored weight");
        object toggleItalicCommand = GetStaticProperty(editingCommandsType, "ToggleItalic");
        AssertEqual(true, InvokeTwoArgumentCommand(toggleItalicCommand, "CanExecute", null, richTextBox), "compiled RichTextBox ToggleItalic CanExecute");
        InvokeTwoArgumentCommand(toggleItalicCommand, "Execute", null, richTextBox);
        AssertEqual("Italic", Invoke(selection, "GetCurrentValue", fontStyleProperty).ToString(), "compiled RichTextBox ToggleItalic applied style");
        InvokeTwoArgumentCommand(toggleItalicCommand, "Execute", null, richTextBox);
        AssertEqual("Normal", Invoke(selection, "GetCurrentValue", fontStyleProperty).ToString(), "compiled RichTextBox ToggleItalic restored style");
        object toggleUnderlineCommand = GetStaticProperty(editingCommandsType, "ToggleUnderline");
        AssertEqual(true, InvokeTwoArgumentCommand(toggleUnderlineCommand, "CanExecute", null, richTextBox), "compiled RichTextBox ToggleUnderline CanExecute");
        InvokeTwoArgumentCommand(toggleUnderlineCommand, "Execute", null, richTextBox);
        object appliedTextDecorations = Invoke(selection, "GetCurrentValue", textDecorationsProperty);
        AssertEqual(1, GetProperty(appliedTextDecorations, "Count"), "compiled RichTextBox ToggleUnderline applied decoration count");
        object underlineDecoration = GetCollectionItem(appliedTextDecorations, 0);
        AssertEqual("Underline", GetProperty(underlineDecoration, "Location").ToString(), "compiled RichTextBox ToggleUnderline applied decoration location");
        InvokeTwoArgumentCommand(toggleUnderlineCommand, "Execute", null, richTextBox);
        object restoredTextDecorations = Invoke(selection, "GetCurrentValue", textDecorationsProperty);
        AssertEqual(0, GetProperty(restoredTextDecorations, "Count"), "compiled RichTextBox ToggleUnderline restored decoration count");
        Type brushType = (Type)GetProperty(foregroundProperty, "PropertyType");
        Assembly mediaAssembly = brushType.Assembly;
        object applyFontSizeCommand = GetStaticProperty(editingCommandsType, "ApplyFontSize");
        AssertEqual(true, InvokeTwoArgumentCommand(applyFontSizeCommand, "CanExecute", 18.0, richTextBox), "compiled RichTextBox ApplyFontSize CanExecute");
        InvokeTwoArgumentCommand(applyFontSizeCommand, "Execute", 18.0, richTextBox);
        AssertEqual(18.0, Invoke(selection, "GetCurrentValue", fontSizeProperty), "compiled RichTextBox ApplyFontSize value");
        object increaseFontSizeCommand = GetStaticProperty(editingCommandsType, "IncreaseFontSize");
        AssertEqual(true, InvokeTwoArgumentCommand(increaseFontSizeCommand, "CanExecute", null, richTextBox), "compiled RichTextBox IncreaseFontSize CanExecute");
        InvokeTwoArgumentCommand(increaseFontSizeCommand, "Execute", null, richTextBox);
        AssertClose(18.75, Convert.ToDouble(Invoke(selection, "GetCurrentValue", fontSizeProperty)), 0.0001, "compiled RichTextBox IncreaseFontSize value");
        object decreaseFontSizeCommand = GetStaticProperty(editingCommandsType, "DecreaseFontSize");
        AssertEqual(true, InvokeTwoArgumentCommand(decreaseFontSizeCommand, "CanExecute", null, richTextBox), "compiled RichTextBox DecreaseFontSize CanExecute");
        InvokeTwoArgumentCommand(decreaseFontSizeCommand, "Execute", null, richTextBox);
        AssertEqual(18.0, Invoke(selection, "GetCurrentValue", fontSizeProperty), "compiled RichTextBox DecreaseFontSize value");
        object applyFontFamilyCommand = GetStaticProperty(editingCommandsType, "ApplyFontFamily");
        object fontFamily = Create(mediaAssembly, "System.Windows.Media.FontFamily", "Consolas");
        AssertEqual(true, InvokeTwoArgumentCommand(applyFontFamilyCommand, "CanExecute", fontFamily, richTextBox), "compiled RichTextBox ApplyFontFamily CanExecute");
        InvokeTwoArgumentCommand(applyFontFamilyCommand, "Execute", fontFamily, richTextBox);
        AssertEqual("Consolas", Invoke(selection, "GetCurrentValue", fontFamilyProperty).ToString(), "compiled RichTextBox ApplyFontFamily value");
        Type colorType = GetRequiredType(mediaAssembly, "System.Windows.Media.Color");
        object foregroundBrush = Create(mediaAssembly, "System.Windows.Media.SolidColorBrush", InvokeStatic(colorType, "FromRgb", (byte)0x12, (byte)0x34, (byte)0x56));
        object backgroundBrush = Create(mediaAssembly, "System.Windows.Media.SolidColorBrush", InvokeStatic(colorType, "FromRgb", (byte)0xAB, (byte)0xCD, (byte)0xEF));
        object applyForegroundCommand = GetStaticProperty(editingCommandsType, "ApplyForeground");
        AssertEqual(true, InvokeTwoArgumentCommand(applyForegroundCommand, "CanExecute", foregroundBrush, richTextBox), "compiled RichTextBox ApplyForeground CanExecute");
        InvokeTwoArgumentCommand(applyForegroundCommand, "Execute", foregroundBrush, richTextBox);
        object appliedForeground = Invoke(selection, "GetCurrentValue", foregroundProperty);
        AssertType(appliedForeground, "System.Windows.Media.SolidColorBrush", "compiled RichTextBox ApplyForeground brush");
        AssertEqual("#FF123456", GetProperty(appliedForeground, "Color").ToString(), "compiled RichTextBox ApplyForeground color");
        object applyBackgroundCommand = GetStaticProperty(editingCommandsType, "ApplyBackground");
        AssertEqual(true, InvokeTwoArgumentCommand(applyBackgroundCommand, "CanExecute", backgroundBrush, richTextBox), "compiled RichTextBox ApplyBackground CanExecute");
        InvokeTwoArgumentCommand(applyBackgroundCommand, "Execute", backgroundBrush, richTextBox);
        object appliedBackground = Invoke(selection, "GetCurrentValue", backgroundProperty);
        AssertType(appliedBackground, "System.Windows.Media.SolidColorBrush", "compiled RichTextBox ApplyBackground brush");
        AssertEqual("#FFABCDEF", GetProperty(appliedBackground, "Color").ToString(), "compiled RichTextBox ApplyBackground color");
        object toggleSubscriptCommand = GetStaticProperty(editingCommandsType, "ToggleSubscript");
        AssertEqual(true, InvokeTwoArgumentCommand(toggleSubscriptCommand, "CanExecute", null, richTextBox), "compiled RichTextBox ToggleSubscript CanExecute");
        InvokeTwoArgumentCommand(toggleSubscriptCommand, "Execute", null, richTextBox);
        AssertEqual("Subscript", Invoke(selection, "GetCurrentValue", variantsProperty).ToString(), "compiled RichTextBox ToggleSubscript applied variant");
        InvokeTwoArgumentCommand(toggleSubscriptCommand, "Execute", null, richTextBox);
        AssertEqual("Normal", Invoke(selection, "GetCurrentValue", variantsProperty).ToString(), "compiled RichTextBox ToggleSubscript restored variant");
        object toggleSuperscriptCommand = GetStaticProperty(editingCommandsType, "ToggleSuperscript");
        AssertEqual(true, InvokeTwoArgumentCommand(toggleSuperscriptCommand, "CanExecute", null, richTextBox), "compiled RichTextBox ToggleSuperscript CanExecute");
        InvokeTwoArgumentCommand(toggleSuperscriptCommand, "Execute", null, richTextBox);
        AssertEqual("Superscript", Invoke(selection, "GetCurrentValue", variantsProperty).ToString(), "compiled RichTextBox ToggleSuperscript applied variant");
        InvokeTwoArgumentCommand(toggleSuperscriptCommand, "Execute", null, richTextBox);
        AssertEqual("Normal", Invoke(selection, "GetCurrentValue", variantsProperty).ToString(), "compiled RichTextBox ToggleSuperscript restored variant");
        object applyInlineRtlCommand = GetStaticProperty(editingCommandsType, "ApplyInlineFlowDirectionRTL");
        AssertEqual(true, InvokeTwoArgumentCommand(applyInlineRtlCommand, "CanExecute", null, richTextBox), "compiled RichTextBox ApplyInlineFlowDirectionRTL CanExecute");
        InvokeTwoArgumentCommand(applyInlineRtlCommand, "Execute", null, richTextBox);
        AssertEqual("RightToLeft", Invoke(selection, "GetCurrentValue", inlineFlowDirectionProperty).ToString(), "compiled RichTextBox ApplyInlineFlowDirectionRTL value");
        object applyInlineLtrCommand = GetStaticProperty(editingCommandsType, "ApplyInlineFlowDirectionLTR");
        AssertEqual(true, InvokeTwoArgumentCommand(applyInlineLtrCommand, "CanExecute", null, richTextBox), "compiled RichTextBox ApplyInlineFlowDirectionLTR CanExecute");
        InvokeTwoArgumentCommand(applyInlineLtrCommand, "Execute", null, richTextBox);
        AssertEqual("LeftToRight", Invoke(selection, "GetCurrentValue", inlineFlowDirectionProperty).ToString(), "compiled RichTextBox ApplyInlineFlowDirectionLTR value");
        object alignCenterCommand = GetStaticProperty(editingCommandsType, "AlignCenter");
        AssertEqual(true, InvokeTwoArgumentCommand(alignCenterCommand, "CanExecute", null, richTextBox), "compiled RichTextBox AlignCenter CanExecute");
        InvokeTwoArgumentCommand(alignCenterCommand, "Execute", null, richTextBox);
        AssertEqual("Center", GetProperty(introParagraph, "TextAlignment").ToString(), "compiled RichTextBox AlignCenter paragraph alignment");
        object alignRightCommand = GetStaticProperty(editingCommandsType, "AlignRight");
        AssertEqual(true, InvokeTwoArgumentCommand(alignRightCommand, "CanExecute", null, richTextBox), "compiled RichTextBox AlignRight CanExecute");
        InvokeTwoArgumentCommand(alignRightCommand, "Execute", null, richTextBox);
        AssertEqual("Right", GetProperty(introParagraph, "TextAlignment").ToString(), "compiled RichTextBox AlignRight paragraph alignment");
        object alignJustifyCommand = GetStaticProperty(editingCommandsType, "AlignJustify");
        AssertEqual(true, InvokeTwoArgumentCommand(alignJustifyCommand, "CanExecute", null, richTextBox), "compiled RichTextBox AlignJustify CanExecute");
        InvokeTwoArgumentCommand(alignJustifyCommand, "Execute", null, richTextBox);
        AssertEqual("Justify", GetProperty(introParagraph, "TextAlignment").ToString(), "compiled RichTextBox AlignJustify paragraph alignment");
        object alignLeftCommand = GetStaticProperty(editingCommandsType, "AlignLeft");
        AssertEqual(true, InvokeTwoArgumentCommand(alignLeftCommand, "CanExecute", null, richTextBox), "compiled RichTextBox AlignLeft CanExecute");
        InvokeTwoArgumentCommand(alignLeftCommand, "Execute", null, richTextBox);
        AssertEqual("Left", GetProperty(introParagraph, "TextAlignment").ToString(), "compiled RichTextBox AlignLeft paragraph alignment");
        object applyParagraphRtlCommand = GetStaticProperty(editingCommandsType, "ApplyParagraphFlowDirectionRTL");
        AssertEqual(true, InvokeTwoArgumentCommand(applyParagraphRtlCommand, "CanExecute", null, richTextBox), "compiled RichTextBox ApplyParagraphFlowDirectionRTL CanExecute");
        InvokeTwoArgumentCommand(applyParagraphRtlCommand, "Execute", null, richTextBox);
        AssertEqual("RightToLeft", GetProperty(introParagraph, "FlowDirection").ToString(), "compiled RichTextBox ApplyParagraphFlowDirectionRTL value");
        object applyParagraphLtrCommand = GetStaticProperty(editingCommandsType, "ApplyParagraphFlowDirectionLTR");
        AssertEqual(true, InvokeTwoArgumentCommand(applyParagraphLtrCommand, "CanExecute", null, richTextBox), "compiled RichTextBox ApplyParagraphFlowDirectionLTR CanExecute");
        InvokeTwoArgumentCommand(applyParagraphLtrCommand, "Execute", null, richTextBox);
        AssertEqual("LeftToRight", GetProperty(introParagraph, "FlowDirection").ToString(), "compiled RichTextBox ApplyParagraphFlowDirectionLTR value");

        object section = GetCollectionItem(blocks, 1);
        AssertType(section, "System.Windows.Documents.Section", "compiled FlowDocument section");
        object sectionBlocks = GetProperty(section, "Blocks");
        AssertCollectionCount(sectionBlocks, expected: 1, "compiled FlowDocument section blocks");
        AssertFlowDocumentParagraphText(GetCollectionItem(sectionBlocks, 0), "section block text", "section");

        object blockContainer = GetCollectionItem(blocks, 2);
        AssertType(blockContainer, "System.Windows.Documents.BlockUIContainer", "compiled FlowDocument block UI container");
        object blockButton = GetProperty(blockContainer, "Child");
        AssertType(blockButton, "System.Windows.Controls.Button", "compiled FlowDocument block Button");
        AssertEqual("block document button", GetProperty(blockButton, "Content"), "compiled FlowDocument block Button content");

        object table = GetCollectionItem(blocks, 3);
        AssertType(table, "System.Windows.Documents.Table", "compiled FlowDocument table");
        AssertCollectionCount(GetProperty(table, "Columns"), expected: 2, "compiled FlowDocument table columns");
        object rowGroups = GetProperty(table, "RowGroups");
        AssertCollectionCount(rowGroups, expected: 1, "compiled FlowDocument table row groups");
        object rows = GetProperty(GetCollectionItem(rowGroups, 0), "Rows");
        AssertCollectionCount(rows, expected: 1, "compiled FlowDocument table rows");
        object originalRow = GetCollectionItem(rows, 0);
        object cells = GetProperty(GetCollectionItem(rows, 0), "Cells");
        AssertCollectionCount(cells, expected: 2, "compiled FlowDocument table cells");
        object firstTableCell = GetCollectionItem(cells, 0);
        object secondTableCell = GetCollectionItem(cells, 1);
        AssertFlowDocumentTableCellText(firstTableCell, "table alpha", "first");
        AssertFlowDocumentTableCellText(secondTableCell, "table beta", "second");
        object firstTableCellParagraph = GetCollectionItem(GetProperty(firstTableCell, "Blocks"), 0);
        Invoke(selection, "Select", GetProperty(firstTableCellParagraph, "ContentStart"), GetProperty(firstTableCellParagraph, "ContentEnd"));
        object insertRowsCommand = GetStaticProperty(editingCommandsType, "InsertRows");
        AssertEqual(true, InvokeTwoArgumentCommand(insertRowsCommand, "CanExecute", null, richTextBox), "compiled RichTextBox InsertRows CanExecute");
        InvokeTwoArgumentCommand(insertRowsCommand, "Execute", null, richTextBox);
        AssertCollectionCount(rows, expected: 2, "compiled RichTextBox InsertRows table rows");
        object insertedRow = GetCollectionItem(rows, 1);
        object insertedCells = GetProperty(insertedRow, "Cells");
        AssertCollectionCount(insertedCells, expected: 2, "compiled RichTextBox InsertRows copied cells");
        AssertType(GetCollectionItem(insertedCells, 0), "System.Windows.Documents.TableCell", "compiled RichTextBox InsertRows first inserted cell");
        AssertType(GetCollectionItem(insertedCells, 1), "System.Windows.Documents.TableCell", "compiled RichTextBox InsertRows second inserted cell");
        AssertFlowDocumentTableCellText(firstTableCell, "table alpha", "first after row insert");
        AssertFlowDocumentTableCellText(secondTableCell, "table beta", "second after row insert");
        Invoke(selection, "Select", GetProperty(firstTableCellParagraph, "ContentStart"), GetProperty(firstTableCellParagraph, "ContentEnd"));
        object insertColumnsCommand = GetStaticProperty(editingCommandsType, "InsertColumns");
        AssertEqual(true, InvokeTwoArgumentCommand(insertColumnsCommand, "CanExecute", null, richTextBox), "compiled RichTextBox InsertColumns CanExecute");
        InvokeTwoArgumentCommand(insertColumnsCommand, "Execute", null, richTextBox);
        AssertCollectionCount(cells, expected: 3, "compiled RichTextBox InsertColumns first row cells");
        AssertSame(firstTableCell, GetCollectionItem(cells, 0), "compiled RichTextBox InsertColumns preserved first cell");
        object insertedColumnCell = GetCollectionItem(cells, 1);
        AssertType(insertedColumnCell, "System.Windows.Documents.TableCell", "compiled RichTextBox InsertColumns first-row inserted cell");
        AssertSame(secondTableCell, GetCollectionItem(cells, 2), "compiled RichTextBox InsertColumns preserved second cell");
        AssertCollectionCount(insertedCells, expected: 3, "compiled RichTextBox InsertColumns copied row cells");
        AssertType(GetCollectionItem(insertedCells, 1), "System.Windows.Documents.TableCell", "compiled RichTextBox InsertColumns copied inserted-row cell");
        AssertFlowDocumentTableCellText(firstTableCell, "table alpha", "first after column insert");
        AssertFlowDocumentTableCellText(secondTableCell, "table beta", "second after column insert");
        object insertedRowCell = GetCollectionItem(insertedCells, 0);
        object insertedRowParagraph = GetCollectionItem(GetProperty(insertedRowCell, "Blocks"), 0);
        Invoke(selection, "Select", GetProperty(insertedRowParagraph, "ContentStart"), GetProperty(insertedRowParagraph, "ContentEnd"));
        object deleteRowsCommand = GetStaticProperty(editingCommandsType, "DeleteRows");
        AssertEqual(true, InvokeTwoArgumentCommand(deleteRowsCommand, "CanExecute", null, richTextBox), "compiled RichTextBox DeleteRows CanExecute");
        InvokeTwoArgumentCommand(deleteRowsCommand, "Execute", null, richTextBox);
        AssertCollectionCount(rows, expected: 1, "compiled RichTextBox DeleteRows table rows");
        AssertSame(originalRow, GetCollectionItem(rows, 0), "compiled RichTextBox DeleteRows preserved original row");
        AssertCollectionCount(cells, expected: 3, "compiled RichTextBox DeleteRows preserved inserted columns");
        AssertSame(firstTableCell, GetCollectionItem(cells, 0), "compiled RichTextBox DeleteRows preserved first cell");
        AssertSame(secondTableCell, GetCollectionItem(cells, 2), "compiled RichTextBox DeleteRows preserved second cell");
        AssertFlowDocumentTableCellText(firstTableCell, "table alpha", "first after row delete");
        AssertFlowDocumentTableCellText(secondTableCell, "table beta", "second after row delete");
        Invoke(selection, "Select", GetProperty(insertedColumnCell, "ContentStart"), GetProperty(secondTableCell, "ContentStart"));
        AssertEqual(true, GetProperty(selection, "IsTableCellRange"), "compiled RichTextBox DeleteColumns table-cell selection");
        object deleteColumnsCommand = GetStaticProperty(editingCommandsType, "DeleteColumns");
        AssertEqual(true, InvokeTwoArgumentCommand(deleteColumnsCommand, "CanExecute", null, richTextBox), "compiled RichTextBox DeleteColumns CanExecute");
        InvokeTwoArgumentCommand(deleteColumnsCommand, "Execute", null, richTextBox);
        AssertCollectionCount(cells, expected: 2, "compiled RichTextBox DeleteColumns first row cells");
        AssertSame(firstTableCell, GetCollectionItem(cells, 0), "compiled RichTextBox DeleteColumns preserved first cell");
        AssertSame(secondTableCell, GetCollectionItem(cells, 1), "compiled RichTextBox DeleteColumns preserved second cell");
        AssertFlowDocumentTableCellText(firstTableCell, "table alpha", "first after column delete");
        AssertFlowDocumentTableCellText(secondTableCell, "table beta", "second after column delete");

        object list = GetCollectionItem(blocks, 4);
        AssertType(list, "System.Windows.Documents.List", "compiled FlowDocument list");
        AssertEqual("Decimal", GetProperty(list, "MarkerStyle").ToString(), "compiled FlowDocument marker style");
        object listItems = GetProperty(list, "ListItems");
        AssertCollectionCount(listItems, expected: 2, "compiled FlowDocument list items");
        AssertFlowDocumentListItemText(GetCollectionItem(listItems, 0), "first document item", "first");
        AssertFlowDocumentListItemText(GetCollectionItem(listItems, 1), "second document item", "second");

        object textRange = Create(
            flowDocument.GetType().Assembly,
            "System.Windows.Documents.TextRange",
            GetProperty(flowDocument, "ContentStart"),
            GetProperty(flowDocument, "ContentEnd"));
        string text = GetProperty(textRange, "Text").ToString() ?? string.Empty;
        AssertContains("compiled", text, "compiled FlowDocument TextRange paragraph text");
        AssertContains("rich", text, "compiled FlowDocument TextRange bold text");
        AssertContains("italic", text, "compiled FlowDocument TextRange italic text");
        AssertContains("underline", text, "compiled FlowDocument TextRange underline text");
        AssertContains("span", text, "compiled FlowDocument TextRange span text");
        AssertContains("after line break", text, "compiled FlowDocument TextRange line-break text");
        AssertContains("FlowDocument", text, "compiled FlowDocument TextRange document text");
        AssertContains("link", text, "compiled FlowDocument TextRange hyperlink text");
        AssertContains("figure anchored text", text, "compiled FlowDocument TextRange figure text");
        AssertContains("floater anchored text", text, "compiled FlowDocument TextRange floater text");
        AssertContains("section block text", text, "compiled FlowDocument TextRange section text");
        AssertContains("table alpha", text, "compiled FlowDocument TextRange first table cell");
        AssertContains("table beta", text, "compiled FlowDocument TextRange second table cell");
        AssertContains("first document item", text, "compiled FlowDocument TextRange first list item");
        AssertContains("second document item", text, "compiled FlowDocument TextRange second list item");

        Invoke(selection, "Select", GetProperty(firstTableCell, "ContentStart"), GetProperty(secondTableCell, "ContentEnd"));
        AssertEqual(true, GetProperty(selection, "IsTableCellRange"), "compiled RichTextBox MergeCells table-cell selection");
        object mergeCellsCommand = GetStaticProperty(editingCommandsType, "MergeCells");
        AssertEqual(true, InvokeTwoArgumentCommand(mergeCellsCommand, "CanExecute", null, richTextBox), "compiled RichTextBox MergeCells CanExecute");
        InvokeTwoArgumentCommand(mergeCellsCommand, "Execute", null, richTextBox);
        AssertCollectionCount(cells, expected: 1, "compiled RichTextBox MergeCells first row cells");
        AssertSame(firstTableCell, GetCollectionItem(cells, 0), "compiled RichTextBox MergeCells preserved first cell");
        AssertEqual(2, GetProperty(firstTableCell, "ColumnSpan"), "compiled RichTextBox MergeCells column span");
        AssertFlowDocumentTableCellText(firstTableCell, "table alpha", "first after cell merge");
        Invoke(selection, "Select", GetProperty(firstTableCell, "ContentStart"), GetProperty(originalRow, "ContentEnd"));
        AssertEqual(true, GetProperty(selection, "IsTableCellRange"), "compiled RichTextBox SplitCell table-cell selection");
        object splitCellCommand = GetStaticProperty(editingCommandsType, "SplitCell");
        AssertEqual(true, InvokeTwoArgumentCommand(splitCellCommand, "CanExecute", null, richTextBox), "compiled RichTextBox SplitCell CanExecute");
        InvokeTwoArgumentCommand(splitCellCommand, "Execute", null, richTextBox);
        AssertCollectionCount(cells, expected: 2, "compiled RichTextBox SplitCell first row cells");
        AssertSame(firstTableCell, GetCollectionItem(cells, 0), "compiled RichTextBox SplitCell preserved first cell");
        AssertType(GetCollectionItem(cells, 1), "System.Windows.Documents.TableCell", "compiled RichTextBox SplitCell copied second cell");
        AssertEqual(1, GetProperty(firstTableCell, "ColumnSpan"), "compiled RichTextBox SplitCell column span");
        AssertFlowDocumentTableCellText(firstTableCell, "table alpha", "first after cell split");

        object firstListItem = GetCollectionItem(listItems, 0);
        object secondListItem = GetCollectionItem(listItems, 1);
        object firstListParagraph = GetCollectionItem(GetProperty(firstListItem, "Blocks"), 0);
        Invoke(selection, "Select", GetProperty(firstListParagraph, "ContentStart"), GetProperty(firstListParagraph, "ContentEnd"));
        AssertContains("first document item", GetProperty(selection, "Text").ToString() ?? string.Empty, "compiled RichTextBox list command selection text");
        object toggleBulletsCommand = GetStaticProperty(editingCommandsType, "ToggleBullets");
        AssertEqual(true, InvokeTwoArgumentCommand(toggleBulletsCommand, "CanExecute", null, richTextBox), "compiled RichTextBox ToggleBullets CanExecute");
        InvokeTwoArgumentCommand(toggleBulletsCommand, "Execute", null, richTextBox);
        AssertEqual("Disc", GetProperty(list, "MarkerStyle").ToString(), "compiled RichTextBox ToggleBullets marker style");
        object toggleNumberingCommand = GetStaticProperty(editingCommandsType, "ToggleNumbering");
        AssertEqual(true, InvokeTwoArgumentCommand(toggleNumberingCommand, "CanExecute", null, richTextBox), "compiled RichTextBox ToggleNumbering CanExecute");
        InvokeTwoArgumentCommand(toggleNumberingCommand, "Execute", null, richTextBox);
        AssertEqual("Decimal", GetProperty(list, "MarkerStyle").ToString(), "compiled RichTextBox ToggleNumbering marker style");

        object secondListParagraph = GetCollectionItem(GetProperty(secondListItem, "Blocks"), 0);
        Invoke(selection, "Select", GetProperty(secondListParagraph, "ContentStart"), GetProperty(secondListParagraph, "ContentEnd"));
        SetProperty(richTextBox, "AcceptsTab", true);
        object increaseIndentationCommand = GetStaticProperty(editingCommandsType, "IncreaseIndentation");
        AssertEqual(true, InvokeTwoArgumentCommand(increaseIndentationCommand, "CanExecute", null, richTextBox), "compiled RichTextBox IncreaseIndentation CanExecute");
        InvokeTwoArgumentCommand(increaseIndentationCommand, "Execute", null, richTextBox);
        AssertCollectionCount(listItems, expected: 1, "compiled RichTextBox IncreaseIndentation top-level list items");
        AssertSame(firstListItem, GetCollectionItem(listItems, 0), "compiled RichTextBox IncreaseIndentation leading list item");
        object firstListItemBlocks = GetProperty(firstListItem, "Blocks");
        AssertCollectionCount(firstListItemBlocks, expected: 2, "compiled RichTextBox IncreaseIndentation leading list item blocks");
        object nestedList = GetCollectionItem(firstListItemBlocks, 1);
        AssertType(nestedList, "System.Windows.Documents.List", "compiled RichTextBox IncreaseIndentation nested list");
        AssertSame(firstListItem, GetProperty(nestedList, "Parent"), "compiled RichTextBox IncreaseIndentation nested list parent");
        AssertEqual("Decimal", GetProperty(nestedList, "MarkerStyle").ToString(), "compiled RichTextBox IncreaseIndentation nested marker style");
        object nestedListItems = GetProperty(nestedList, "ListItems");
        AssertCollectionCount(nestedListItems, expected: 1, "compiled RichTextBox IncreaseIndentation nested list items");
        AssertSame(secondListItem, GetCollectionItem(nestedListItems, 0), "compiled RichTextBox IncreaseIndentation nested list item");
        AssertFlowDocumentListItemText(secondListItem, "second document item", "indented second");

        Invoke(selection, "Select", GetProperty(secondListParagraph, "ContentStart"), GetProperty(secondListParagraph, "ContentEnd"));
        object decreaseIndentationCommand = GetStaticProperty(editingCommandsType, "DecreaseIndentation");
        AssertEqual(true, InvokeTwoArgumentCommand(decreaseIndentationCommand, "CanExecute", null, richTextBox), "compiled RichTextBox DecreaseIndentation CanExecute");
        InvokeTwoArgumentCommand(decreaseIndentationCommand, "Execute", null, richTextBox);
        object restoredListItems = GetProperty(list, "ListItems");
        AssertCollectionCount(restoredListItems, expected: 2, "compiled RichTextBox DecreaseIndentation top-level list items");
        AssertSame(firstListItem, GetCollectionItem(restoredListItems, 0), "compiled RichTextBox DecreaseIndentation first list item");
        AssertSame(secondListItem, GetCollectionItem(restoredListItems, 1), "compiled RichTextBox DecreaseIndentation second list item");
        AssertCollectionCount(GetProperty(firstListItem, "Blocks"), expected: 1, "compiled RichTextBox DecreaseIndentation leading list item blocks");
        AssertFlowDocumentListItemText(secondListItem, "second document item", "restored second");

        Invoke(selection, "Select", GetProperty(firstListParagraph, "ContentStart"), GetProperty(secondListParagraph, "ContentEnd"));
        object removeListMarkersCommand = GetStaticProperty(editingCommandsType, "RemoveListMarkers");
        AssertEqual(true, InvokeTwoArgumentCommand(removeListMarkersCommand, "CanExecute", null, richTextBox), "compiled RichTextBox RemoveListMarkers CanExecute");
        InvokeTwoArgumentCommand(removeListMarkersCommand, "Execute", null, richTextBox);
        AssertCollectionCount(blocks, expected: 6, "compiled RichTextBox RemoveListMarkers document blocks");
        AssertFlowDocumentParagraphText(GetCollectionItem(blocks, 4), "first document item", "list marker removed first");
        AssertFlowDocumentParagraphText(GetCollectionItem(blocks, 5), "second document item", "list marker removed second");
    }

    private static void AssertFlowDocumentParagraphText(object paragraph, string expectedText, string description)
    {
        AssertType(paragraph, "System.Windows.Documents.Paragraph", $"compiled FlowDocument {description} paragraph");
        object run = GetFirstCollectionItemOfType(GetProperty(paragraph, "Inlines"), "System.Windows.Documents.Run", $"compiled FlowDocument {description} run");
        AssertEqual(expectedText, GetProperty(run, "Text"), $"compiled FlowDocument {description} text");
    }

    private static void AssertFlowDocumentAnchoredBlockText(object anchoredBlock, string expectedText, string description)
    {
        object blocks = GetProperty(anchoredBlock, "Blocks");
        AssertCollectionCount(blocks, expected: 1, $"compiled FlowDocument {description} blocks");
        AssertFlowDocumentParagraphText(GetCollectionItem(blocks, 0), expectedText, description);
    }

    private static void AssertFlowDocumentTableCellText(object tableCell, string expectedText, string description)
    {
        AssertType(tableCell, "System.Windows.Documents.TableCell", $"compiled FlowDocument {description} table cell");
        AssertFlowDocumentParagraphText(
            GetCollectionItem(GetProperty(tableCell, "Blocks"), 0),
            expectedText,
            $"{description} table cell");
    }

    private static void AssertFlowDocumentListItemText(object listItem, string expectedText, string description)
    {
        AssertType(listItem, "System.Windows.Documents.ListItem", $"compiled FlowDocument {description} list item");
        object paragraph = GetCollectionItem(GetProperty(listItem, "Blocks"), 0);
        AssertType(paragraph, "System.Windows.Documents.Paragraph", $"compiled FlowDocument {description} list paragraph");
        object run = GetFirstCollectionItemOfType(GetProperty(paragraph, "Inlines"), "System.Windows.Documents.Run", $"compiled FlowDocument {description} list run");
        AssertEqual(expectedText, GetProperty(run, "Text"), $"compiled FlowDocument {description} list text");
    }

    private static void ValidateBindingAndCommand(object window)
    {
        object dataContext = GetProperty(window, "DataContext");
        AssertType(dataContext, "ProGPU.Wpf.RealXamlCompilerHarness.MainWindow+SmokeViewModel", "compiled binding DataContext");
        AssertEqual("bound greeting from real WPF", GetProperty(dataContext, "Greeting"), "bound view-model greeting");
        AssertEqual("run bound command", GetProperty(dataContext, "ButtonText"), "bound view-model button text");
        AssertEqual("style trigger target", GetProperty(dataContext, "TriggerButtonText"), "bound view-model trigger button text");

        object bindingBlock = GetField(window, "BindingBlock");
        AssertType(bindingBlock, "System.Windows.Controls.TextBlock", "compiled binding TextBlock");
        AssertEqual("bound greeting from real WPF", GetProperty(bindingBlock, "Text"), "compiled TextBlock binding");
        SetProperty(dataContext, "Greeting", "updated greeting from property change");
        AssertEqual("updated greeting from property change", GetProperty(bindingBlock, "Text"), "compiled TextBlock property-change binding");

        object commandButton = GetField(window, "CommandButton");
        AssertType(commandButton, "System.Windows.Controls.Button", "compiled command Button");
        AssertEqual("run bound command", GetProperty(commandButton, "Content"), "compiled Button content binding");

        object viewModelCommand = GetProperty(dataContext, "SmokeCommand");
        object buttonCommand = GetProperty(commandButton, "Command");
        AssertSame(viewModelCommand, buttonCommand, "compiled Button command binding");
        AssertEqual(0, GetProperty(viewModelCommand, "ExecutionCount"), "bound command initial execution count");
        Invoke(buttonCommand, "Execute", new object?[] { null });
        AssertEqual(1, GetProperty(viewModelCommand, "ExecutionCount"), "bound command execution count");

        object canExecuteCommandButton = GetField(window, "CanExecuteCommandButton");
        AssertType(canExecuteCommandButton, "System.Windows.Controls.Button", "compiled CanExecute command Button");
        AssertEqual("can execute command", GetProperty(canExecuteCommandButton, "Content"), "compiled CanExecute command Button content");
        AssertEqual("can execute payload", GetProperty(canExecuteCommandButton, "CommandParameter"), "compiled CanExecute command Button parameter");
        object toggleCommand = GetProperty(dataContext, "ToggleCommand");
        AssertSame(toggleCommand, GetProperty(canExecuteCommandButton, "Command"), "compiled CanExecute command binding");
        AssertEqual(false, GetProperty(toggleCommand, "CanExecuteValue"), "compiled CanExecute command initial state");
        AssertEqual(false, GetProperty(canExecuteCommandButton, "IsEnabled"), "compiled CanExecute command initial button state");

        Invoke(toggleCommand, "SetCanExecute", true);
        AssertEqual(true, GetProperty(canExecuteCommandButton, "IsEnabled"), "compiled CanExecute command enabled button state");
        AssertEqual(1, GetProperty(toggleCommand, "CanExecuteChangedCount"), "compiled CanExecute command change count");
        AssertAtLeast(1, GetProperty(toggleCommand, "CanExecuteCount"), "compiled CanExecute command query count");
        Invoke(canExecuteCommandButton, "OnClick");
        AssertEqual(1, GetProperty(toggleCommand, "ExecutionCount"), "compiled CanExecute command button execution count");
        AssertEqual("can execute payload", GetProperty(toggleCommand, "LastParameter"), "compiled CanExecute command execution parameter");

        Invoke(toggleCommand, "SetCanExecute", false);
        AssertEqual(false, GetProperty(canExecuteCommandButton, "IsEnabled"), "compiled CanExecute command disabled button state");
        AssertEqual(2, GetProperty(toggleCommand, "CanExecuteChangedCount"), "compiled CanExecute command disabled change count");
    }

    private static void ValidatePostShowCommandManagerRequery(
        Assembly presentationCore,
        object window,
        Action flushDispatcherOperations)
    {
        object dataContext = GetProperty(window, "DataContext");
        object requeryCommandButton = GetField(window, "RequeryCommandButton");
        AssertType(requeryCommandButton, "System.Windows.Controls.Button", "compiled CommandManager requery Button");
        AssertEqual("requery command", GetProperty(requeryCommandButton, "Content"), "compiled CommandManager requery Button content");
        AssertEqual("requery payload", GetProperty(requeryCommandButton, "CommandParameter"), "compiled CommandManager requery Button parameter");

        object requeryCommand = GetProperty(dataContext, "RequeryCommand");
        AssertType(requeryCommand, "ProGPU.Wpf.RealXamlCompilerHarness.MainWindow+SmokeRequeryCommand", "compiled CommandManager requery command");
        AssertSame(requeryCommand, GetProperty(requeryCommandButton, "Command"), "compiled CommandManager RequerySuggested command binding");
        AssertEqual(false, GetProperty(requeryCommand, "CanExecuteValue"), "compiled CommandManager requery initial state");
        AssertEqual(false, GetProperty(requeryCommandButton, "IsEnabled"), "compiled CommandManager requery initial button state");

        Type commandManagerType = GetRequiredType(presentationCore, "System.Windows.Input.CommandManager");
        int initialQueryCount = Convert.ToInt32(GetProperty(requeryCommand, "CanExecuteCount"));
        Invoke(requeryCommand, "SetCanExecute", true);
        InvokeStatic(commandManagerType, "InvalidateRequerySuggested");
        flushDispatcherOperations();

        AssertEqual(true, GetProperty(requeryCommandButton, "IsEnabled"), "compiled CommandManager RequerySuggested enabled button state");
        AssertAtLeast(initialQueryCount + 1, GetProperty(requeryCommand, "CanExecuteCount"), "compiled CommandManager RequerySuggested enabled query count");
        Invoke(requeryCommandButton, "OnClick");
        AssertEqual(1, GetProperty(requeryCommand, "ExecutionCount"), "compiled CommandManager RequerySuggested button execution count");
        AssertEqual("requery payload", GetProperty(requeryCommand, "LastParameter"), "compiled CommandManager RequerySuggested execution parameter");

        int enabledQueryCount = Convert.ToInt32(GetProperty(requeryCommand, "CanExecuteCount"));
        Invoke(requeryCommand, "SetCanExecute", false);
        InvokeStatic(commandManagerType, "InvalidateRequerySuggested");
        flushDispatcherOperations();

        AssertEqual(false, GetProperty(requeryCommandButton, "IsEnabled"), "compiled CommandManager RequerySuggested disabled button state");
        AssertAtLeast(enabledQueryCount + 1, GetProperty(requeryCommand, "CanExecuteCount"), "compiled CommandManager RequerySuggested disabled query count");
    }

    private static void ValidateAdvancedBindingFeatures(object window)
    {
        object dataContext = GetProperty(window, "DataContext");

        object priorityBindingBlock = GetField(window, "PriorityBindingBlock");
        AssertType(priorityBindingBlock, "System.Windows.Controls.TextBlock", "compiled PriorityBinding TextBlock");
        AssertEqual(
            "updated greeting from property change",
            GetProperty(priorityBindingBlock, "Text"),
            "compiled PriorityBinding fallback value");
        object priorityBindingExpression = GetPriorityBindingExpression(priorityBindingBlock, "TextProperty");
        object parentPriorityBinding = GetProperty(priorityBindingExpression, "ParentPriorityBinding");
        object priorityBindings = GetProperty(parentPriorityBinding, "Bindings");
        AssertCollectionCount(priorityBindings, expected: 2, "compiled PriorityBinding child bindings");
        AssertBindingObjectPath(GetCollectionItem(priorityBindings, 0), "MissingPriorityText", "compiled PriorityBinding first path");
        AssertBindingObjectPath(GetCollectionItem(priorityBindings, 1), "Greeting", "compiled PriorityBinding fallback path");

        object multiBindingBlock = GetField(window, "MultiBindingBlock");
        AssertType(multiBindingBlock, "System.Windows.Controls.TextBlock", "compiled MultiBinding TextBlock");
        AssertEqual(
            "updated greeting from property change / run bound command",
            GetProperty(multiBindingBlock, "Text"),
            "compiled MultiBinding string-format value");

        object convertedBindingBlock = GetField(window, "ConvertedBindingBlock");
        AssertType(convertedBindingBlock, "System.Windows.Controls.TextBlock", "compiled converter TextBlock");
        AssertEqual(
            "converted:UPDATED GREETING FROM PROPERTY CHANGE",
            GetProperty(convertedBindingBlock, "Text"),
            "compiled converter binding value");
        object convertedBindingExpression = GetBindingExpression(convertedBindingBlock, "TextProperty");
        object convertedBinding = GetProperty(convertedBindingExpression, "ParentBinding");
        AssertBindingObjectPath(convertedBinding, "Greeting", "compiled converter binding path");
        AssertType(GetProperty(convertedBinding, "Converter"), "ProGPU.Wpf.RealXamlCompilerHarness.SmokeUpperConverter", "compiled converter binding resource");
        AssertEqual("converted", GetProperty(convertedBinding, "ConverterParameter"), "compiled converter parameter");

        object convertedMultiBindingBlock = GetField(window, "ConvertedMultiBindingBlock");
        AssertType(convertedMultiBindingBlock, "System.Windows.Controls.TextBlock", "compiled MultiBinding converter TextBlock");
        AssertEqual(
            "converted-multi:updated greeting from property change|run bound command",
            GetProperty(convertedMultiBindingBlock, "Text"),
            "compiled MultiBinding converter value");
        object convertedMultiBindingExpression = GetMultiBindingExpression(convertedMultiBindingBlock, "TextProperty");
        object convertedMultiBinding = GetProperty(convertedMultiBindingExpression, "ParentMultiBinding");
        AssertType(GetProperty(convertedMultiBinding, "Converter"), "ProGPU.Wpf.RealXamlCompilerHarness.SmokeJoinConverter", "compiled MultiBinding converter resource");
        AssertEqual("converted-multi", GetProperty(convertedMultiBinding, "ConverterParameter"), "compiled MultiBinding converter parameter");
        object convertedMultiBindings = GetProperty(convertedMultiBinding, "Bindings");
        AssertCollectionCount(convertedMultiBindings, expected: 2, "compiled MultiBinding converter child bindings");
        AssertBindingObjectPath(GetCollectionItem(convertedMultiBindings, 0), "Greeting", "compiled MultiBinding converter first path");
        AssertBindingObjectPath(GetCollectionItem(convertedMultiBindings, 1), "ButtonText", "compiled MultiBinding converter second path");

        object validatedBox = GetField(window, "ValidatedBox");
        AssertType(validatedBox, "System.Windows.Controls.TextBox", "compiled validation TextBox");
        AssertEqual("valid binding text", GetProperty(validatedBox, "Text"), "compiled validation TextBox initial text");
        AssertEqual("valid binding text", GetProperty(dataContext, "ValidatedText"), "compiled validation source initial value");
        AssertBindingPath(validatedBox, "TextProperty", "ValidatedText", "compiled validation binding path");

        Type validationType = validatedBox.GetType().Assembly.GetType("System.Windows.Controls.Validation", throwOnError: true)
            ?? throw new TypeLoadException("System.Windows.Controls.Validation");
        AssertEqual(false, GetDependencyPropertyValue(validatedBox, validationType, "HasErrorProperty"), "compiled validation initial error state");

        int initialValidatedBoxErrorCount = Convert.ToInt32(GetProperty(window, "ValidatedBoxValidationErrorCount"));
        SetProperty(validatedBox, "Text", string.Empty);
        object bindingExpression = GetBindingExpression(validatedBox, "TextProperty");
        Invoke(bindingExpression, "UpdateSource");
        AssertEqual(string.Empty, GetProperty(dataContext, "ValidatedText"), "compiled validation invalid source value");
        AssertEqual(true, GetDependencyPropertyValue(validatedBox, validationType, "HasErrorProperty"), "compiled validation error state");
        AssertEqual(initialValidatedBoxErrorCount + 1, GetProperty(window, "ValidatedBoxValidationErrorCount"), "compiled validation Error added count");
        AssertEqual("ValidatedBox", GetProperty(window, "LastValidatedBoxValidationErrorSenderName"), "compiled validation Error added sender");
        AssertEqual("ValidationError", GetProperty(window, "LastValidatedBoxValidationErrorRoutedEventName"), "compiled validation Error added routed event");
        AssertEqual("Added", GetProperty(window, "LastValidatedBoxValidationErrorAction"), "compiled validation Error added action");
        AssertEqual("ValidatedText is required", GetProperty(window, "LastValidatedBoxValidationErrorContent"), "compiled validation Error added content");
        AssertEqual("DataErrorValidationRule", GetProperty(window, "LastValidatedBoxValidationErrorRuleName"), "compiled validation Error added rule");

        SetProperty(validatedBox, "Text", "valid binding text restored");
        Invoke(bindingExpression, "UpdateSource");
        AssertEqual("valid binding text restored", GetProperty(dataContext, "ValidatedText"), "compiled validation restored source value");
        AssertEqual(false, GetDependencyPropertyValue(validatedBox, validationType, "HasErrorProperty"), "compiled validation restored error state");
        AssertEqual(initialValidatedBoxErrorCount + 2, GetProperty(window, "ValidatedBoxValidationErrorCount"), "compiled validation Error removed count");
        AssertEqual("ValidatedBox", GetProperty(window, "LastValidatedBoxValidationErrorSenderName"), "compiled validation Error removed sender");
        AssertEqual("ValidationError", GetProperty(window, "LastValidatedBoxValidationErrorRoutedEventName"), "compiled validation Error removed routed event");
        AssertEqual("Removed", GetProperty(window, "LastValidatedBoxValidationErrorAction"), "compiled validation Error removed action");
        AssertEqual("ValidatedText is required", GetProperty(window, "LastValidatedBoxValidationErrorContent"), "compiled validation Error removed content");
        AssertEqual("DataErrorValidationRule", GetProperty(window, "LastValidatedBoxValidationErrorRuleName"), "compiled validation Error removed rule");

        object ruleValidatedBox = GetField(window, "RuleValidatedBox");
        AssertType(ruleValidatedBox, "System.Windows.Controls.TextBox", "compiled ValidationRule TextBox");
        AssertEqual("rule: valid binding text", GetProperty(ruleValidatedBox, "Text"), "compiled ValidationRule TextBox initial text");
        AssertEqual("rule: valid binding text", GetProperty(dataContext, "RuleValidatedText"), "compiled ValidationRule source initial value");
        object ruleBindingExpression = GetBindingExpression(ruleValidatedBox, "TextProperty");
        object ruleBinding = GetProperty(ruleBindingExpression, "ParentBinding");
        AssertBindingObjectPath(ruleBinding, "RuleValidatedText", "compiled ValidationRule binding path");
        object validationRules = GetProperty(ruleBinding, "ValidationRules");
        AssertCollectionCount(validationRules, expected: 1, "compiled Binding ValidationRules");
        object validationRule = GetCollectionItem(validationRules, 0);
        AssertType(validationRule, "ProGPU.Wpf.RealXamlCompilerHarness.SmokePrefixValidationRule", "compiled custom ValidationRule");
        AssertEqual("rule:", GetProperty(validationRule, "RequiredPrefix"), "compiled custom ValidationRule parameter");
        AssertEqual(false, GetDependencyPropertyValue(ruleValidatedBox, validationType, "HasErrorProperty"), "compiled ValidationRule initial error state");

        int initialRuleValidatedBoxErrorCount = Convert.ToInt32(GetProperty(window, "RuleValidatedBoxValidationErrorCount"));
        SetProperty(ruleValidatedBox, "Text", "invalid rule text");
        Invoke(ruleBindingExpression, "UpdateSource");
        AssertEqual("rule: valid binding text", GetProperty(dataContext, "RuleValidatedText"), "compiled ValidationRule rejected source value");
        AssertEqual(true, GetDependencyPropertyValue(ruleValidatedBox, validationType, "HasErrorProperty"), "compiled ValidationRule error state");
        AssertEqual(initialRuleValidatedBoxErrorCount + 1, GetProperty(window, "RuleValidatedBoxValidationErrorCount"), "compiled ValidationRule Error added count");
        AssertEqual("RuleValidatedBox", GetProperty(window, "LastRuleValidatedBoxValidationErrorSenderName"), "compiled ValidationRule Error added sender");
        AssertEqual("ValidationError", GetProperty(window, "LastRuleValidatedBoxValidationErrorRoutedEventName"), "compiled ValidationRule Error added routed event");
        AssertEqual("Added", GetProperty(window, "LastRuleValidatedBoxValidationErrorAction"), "compiled ValidationRule Error added action");
        AssertEqual("Value must start with 'rule:'.", GetProperty(window, "LastRuleValidatedBoxValidationErrorContent"), "compiled ValidationRule Error added content");
        AssertEqual("SmokePrefixValidationRule", GetProperty(window, "LastRuleValidatedBoxValidationErrorRuleName"), "compiled ValidationRule Error added rule");

        SetProperty(ruleValidatedBox, "Text", "rule: restored binding text");
        Invoke(ruleBindingExpression, "UpdateSource");
        AssertEqual("rule: restored binding text", GetProperty(dataContext, "RuleValidatedText"), "compiled ValidationRule restored source value");
        AssertEqual(false, GetDependencyPropertyValue(ruleValidatedBox, validationType, "HasErrorProperty"), "compiled ValidationRule restored error state");
        AssertEqual(initialRuleValidatedBoxErrorCount + 2, GetProperty(window, "RuleValidatedBoxValidationErrorCount"), "compiled ValidationRule Error removed count");
        AssertEqual("RuleValidatedBox", GetProperty(window, "LastRuleValidatedBoxValidationErrorSenderName"), "compiled ValidationRule Error removed sender");
        AssertEqual("ValidationError", GetProperty(window, "LastRuleValidatedBoxValidationErrorRoutedEventName"), "compiled ValidationRule Error removed routed event");
        AssertEqual("Removed", GetProperty(window, "LastRuleValidatedBoxValidationErrorAction"), "compiled ValidationRule Error removed action");
        AssertEqual("Value must start with 'rule:'.", GetProperty(window, "LastRuleValidatedBoxValidationErrorContent"), "compiled ValidationRule Error removed content");
        AssertEqual("SmokePrefixValidationRule", GetProperty(window, "LastRuleValidatedBoxValidationErrorRuleName"), "compiled ValidationRule Error removed rule");

        ValidateBindingTransferEvents(window, dataContext);
        ValidateBindingGroup(window, dataContext, validationType);
    }

    private static void ValidateBindingTransferEvents(object window, object dataContext)
    {
        object transferBox = GetField(window, "BindingTransferBox");
        AssertType(transferBox, "System.Windows.Controls.TextBox", "compiled binding transfer TextBox");
        AssertEqual("binding transfer initial", GetProperty(transferBox, "Text"), "compiled binding transfer initial target");
        AssertEqual("binding transfer initial", GetProperty(dataContext, "BindingTransferText"), "compiled binding transfer initial source");

        object bindingExpression = GetBindingExpression(transferBox, "TextProperty");
        object binding = GetProperty(bindingExpression, "ParentBinding");
        AssertBindingObjectPath(binding, "BindingTransferText", "compiled binding transfer binding path");
        AssertEqual(true, GetProperty(binding, "NotifyOnSourceUpdated"), "compiled binding transfer NotifyOnSourceUpdated");
        AssertEqual(true, GetProperty(binding, "NotifyOnTargetUpdated"), "compiled binding transfer NotifyOnTargetUpdated");

        int initialSourceUpdatedCount = Convert.ToInt32(GetProperty(window, "BindingTransferSourceUpdatedCount"));
        SetProperty(transferBox, "Text", "binding transfer source update");
        Invoke(bindingExpression, "UpdateSource");
        AssertEqual("binding transfer source update", GetProperty(dataContext, "BindingTransferText"), "compiled Binding SourceUpdated source value");
        AssertEqual(initialSourceUpdatedCount + 1, GetProperty(window, "BindingTransferSourceUpdatedCount"), "compiled Binding SourceUpdated count");
        AssertEqual("BindingTransferBox", GetProperty(window, "LastBindingTransferSourceSenderName"), "compiled Binding SourceUpdated sender");
        AssertEqual("SourceUpdated", GetProperty(window, "LastBindingTransferSourceRoutedEventName"), "compiled Binding SourceUpdated routed event");
        AssertEqual("Text", GetProperty(window, "LastBindingTransferSourcePropertyName"), "compiled Binding SourceUpdated property");
        AssertEqual("BindingTransferBox", GetProperty(window, "LastBindingTransferSourceObjectName"), "compiled Binding SourceUpdated target object");

        int initialTargetUpdatedCount = Convert.ToInt32(GetProperty(window, "BindingTransferTargetUpdatedCount"));
        SetProperty(dataContext, "BindingTransferText", "binding transfer target update");
        AssertEqual("binding transfer target update", GetProperty(transferBox, "Text"), "compiled Binding TargetUpdated target value");
        AssertEqual(initialTargetUpdatedCount + 1, GetProperty(window, "BindingTransferTargetUpdatedCount"), "compiled Binding TargetUpdated count");
        AssertEqual("BindingTransferBox", GetProperty(window, "LastBindingTransferTargetSenderName"), "compiled Binding TargetUpdated sender");
        AssertEqual("TargetUpdated", GetProperty(window, "LastBindingTransferTargetRoutedEventName"), "compiled Binding TargetUpdated routed event");
        AssertEqual("Text", GetProperty(window, "LastBindingTransferTargetPropertyName"), "compiled Binding TargetUpdated property");
        AssertEqual("BindingTransferBox", GetProperty(window, "LastBindingTransferTargetObjectName"), "compiled Binding TargetUpdated target object");
    }

    private static void ValidateBindingGroup(object window, object dataContext, Type validationType)
    {
        object panel = GetField(window, "BindingGroupPanel");
        AssertType(panel, "System.Windows.Controls.StackPanel", "compiled BindingGroup panel");

        object bindingGroup = GetProperty(panel, "BindingGroup");
        AssertType(bindingGroup, "System.Windows.Data.BindingGroup", "compiled BindingGroup");
        AssertEqual("SmokeBindingGroup", GetProperty(bindingGroup, "Name"), "compiled BindingGroup name");
        AssertCollectionCount(GetProperty(bindingGroup, "Items"), expected: 1, "compiled BindingGroup items");

        object validationRules = GetProperty(bindingGroup, "ValidationRules");
        AssertCollectionCount(validationRules, expected: 1, "compiled BindingGroup ValidationRules");
        object validationRule = GetCollectionItem(validationRules, 0);
        AssertType(validationRule, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeBindingGroupValidationRule", "compiled BindingGroup custom ValidationRule");
        AssertEqual("BindingGroupFirstName", GetProperty(validationRule, "FirstProperty"), "compiled BindingGroup first property");
        AssertEqual("BindingGroupLastName", GetProperty(validationRule, "SecondProperty"), "compiled BindingGroup second property");
        AssertEqual("group:", GetProperty(validationRule, "RequiredPrefix"), "compiled BindingGroup required prefix");

        object firstBox = GetField(window, "BindingGroupFirstBox");
        object lastBox = GetField(window, "BindingGroupLastBox");
        AssertType(firstBox, "System.Windows.Controls.TextBox", "compiled BindingGroup first TextBox");
        AssertType(lastBox, "System.Windows.Controls.TextBox", "compiled BindingGroup last TextBox");
        AssertEqual("group: Ada", GetProperty(firstBox, "Text"), "compiled BindingGroup first initial text");
        AssertEqual("group: Lovelace", GetProperty(lastBox, "Text"), "compiled BindingGroup last initial text");
        AssertEqual("group: Ada", GetProperty(dataContext, "BindingGroupFirstName"), "compiled BindingGroup first initial source");
        AssertEqual("group: Lovelace", GetProperty(dataContext, "BindingGroupLastName"), "compiled BindingGroup last initial source");
        AssertBindingPath(firstBox, "TextProperty", "BindingGroupFirstName", "compiled BindingGroup first binding path");
        AssertBindingPath(lastBox, "TextProperty", "BindingGroupLastName", "compiled BindingGroup last binding path");

        AssertEqual(false, GetDependencyPropertyValue(panel, validationType, "HasErrorProperty"), "compiled BindingGroup initial error state");
        AssertEqual(true, Invoke(bindingGroup, "ValidateWithoutUpdate"), "compiled BindingGroup initial validation");

        SetProperty(firstBox, "Text", "invalid Ada");
        SetProperty(lastBox, "Text", "group: Hopper");
        AssertEqual(false, Invoke(bindingGroup, "CommitEdit"), "compiled BindingGroup rejected commit");
        AssertEqual("group: Ada", GetProperty(dataContext, "BindingGroupFirstName"), "compiled BindingGroup rejected first source");
        AssertEqual("group: Lovelace", GetProperty(dataContext, "BindingGroupLastName"), "compiled BindingGroup rejected last source");
        AssertEqual(true, GetDependencyPropertyValue(panel, validationType, "HasErrorProperty"), "compiled BindingGroup rejected error state");

        SetProperty(firstBox, "Text", "group: Grace");
        SetProperty(lastBox, "Text", "group: Hopper");
        AssertEqual(true, Invoke(bindingGroup, "CommitEdit"), "compiled BindingGroup accepted commit");
        AssertEqual("group: Grace", GetProperty(dataContext, "BindingGroupFirstName"), "compiled BindingGroup accepted first source");
        AssertEqual("group: Hopper", GetProperty(dataContext, "BindingGroupLastName"), "compiled BindingGroup accepted last source");
        AssertEqual(false, GetDependencyPropertyValue(panel, validationType, "HasErrorProperty"), "compiled BindingGroup accepted error state");
    }

    private static void ValidatePostShowBindingFeatures(object window)
    {
        object relativeSourceBlock = GetField(window, "RelativeSourceBlock");
        AssertType(relativeSourceBlock, "System.Windows.Controls.TextBlock", "compiled RelativeSource TextBlock");
        AssertEqual("ancestor binding source", GetProperty(relativeSourceBlock, "Text"), "compiled RelativeSource ancestor binding value");
        AssertBindingPath(relativeSourceBlock, "TextProperty", "Tag", "compiled RelativeSource binding path");
    }

    private static void ValidatePostShowLoadedEvent(object window)
    {
        object storyboardTargetBlock = GetField(window, "StoryboardTargetBlock");
        AssertEqual(true, GetProperty(storyboardTargetBlock, "IsLoaded"), "compiled Storyboard target loaded state");
        AssertEqual(0.37, GetProperty(storyboardTargetBlock, "Opacity"), "compiled Storyboard target post-Loaded opacity");
        ValidateLoadedEventHandlerState(window);
    }

    private static void ValidateLoadedEventHandlerState(object window)
    {
        AssertEqual(1, GetProperty(window, "StoryboardTargetLoadedCount"), "compiled Storyboard target Loaded handler count");
        AssertEqual("StoryboardTargetBlock", GetProperty(window, "LastStoryboardTargetLoadedSenderName"), "compiled Storyboard target Loaded sender name");
        AssertEqual("Loaded", GetProperty(window, "LastStoryboardTargetLoadedRoutedEventName"), "compiled Storyboard target Loaded routed event name");
    }

    private static void ValidatePostShowItemTemplateTriggerActivation(Assembly presentationCore, object window)
    {
        object itemsList = GetField(window, "ItemsList");
        object sourceItems = GetProperty(GetProperty(window, "DataContext"), "Items");
        object alphaItem = GetCollectionItem(sourceItems, 0);
        ValidateGeneratedItemTemplateTextBlock(
            presentationCore,
            itemsList,
            alphaItem,
            "item alpha",
            "container trigger inactive",
            "template trigger inactive",
            "compiled DataTemplate inactive generated item container",
            "compiled ItemContainerStyle trigger inactive generated value",
            "compiled DataTemplate inactive generated TextBlock",
            "compiled DataTemplate inactive generated TextBlock binding",
            "compiled DataTemplate trigger inactive generated value");

        object betaItem = GetCollectionItem(sourceItems, 1);
        ValidateGeneratedItemTemplateTextBlock(
            presentationCore,
            itemsList,
            betaItem,
            "item beta",
            "container trigger active",
            "template trigger active",
            "compiled DataTemplate active generated item container",
            "compiled ItemContainerStyle trigger active generated value",
            "compiled DataTemplate active generated TextBlock",
            "compiled DataTemplate active generated TextBlock binding",
            "compiled DataTemplate trigger active generated value");
    }

    private static void ValidatePostShowItemContainerAlternation(object window)
    {
        object alternationItemsList = GetField(window, "AlternationItemsList");
        object sourceItems = GetProperty(GetProperty(window, "DataContext"), "Items");
        Type itemsControlType = GetRequiredType(alternationItemsList.GetType().Assembly, "System.Windows.Controls.ItemsControl");

        ValidateGeneratedAlternationContainer(
            alternationItemsList,
            GetCollectionItem(sourceItems, 0),
            itemsControlType,
            expectedIndex: 0,
            "compiled alternation first item container index");
        ValidateGeneratedAlternationContainer(
            alternationItemsList,
            GetCollectionItem(sourceItems, 1),
            itemsControlType,
            expectedIndex: 1,
            "compiled alternation second item container index");
        ValidateGeneratedAlternationContainer(
            alternationItemsList,
            GetCollectionItem(sourceItems, 2),
            itemsControlType,
            expectedIndex: 0,
            "compiled alternation third item container index");
    }

    private static void ValidateGeneratedAlternationContainer(
        object alternationItemsList,
        object item,
        Type itemsControlType,
        int expectedIndex,
        string description)
    {
        Invoke(alternationItemsList, "ScrollIntoView", item);
        Invoke(alternationItemsList, "UpdateLayout");

        object itemContainerGenerator = GetProperty(alternationItemsList, "ItemContainerGenerator");
        object itemContainer = Invoke(itemContainerGenerator, "ContainerFromItem", item);
        AssertType(itemContainer, "System.Windows.Controls.ListBoxItem", description);
        AssertEqual(expectedIndex, GetDependencyPropertyValue(itemContainer, itemsControlType, "AlternationIndexProperty"), description);
    }

    private static void ValidatePostShowItemStringFormat(Assembly presentationCore, object window)
    {
        object stringFormatItemsList = GetField(window, "StringFormatItemsList");
        object sourceLabels = GetProperty(GetProperty(window, "DataContext"), "Labels");

        ValidateGeneratedStringFormatContainer(
            presentationCore,
            stringFormatItemsList,
            GetCollectionItem(sourceLabels, 0),
            "formatted label alpha",
            "compiled ItemStringFormat first generated item text");
        ValidateGeneratedStringFormatContainer(
            presentationCore,
            stringFormatItemsList,
            GetCollectionItem(sourceLabels, 1),
            "formatted label beta",
            "compiled ItemStringFormat second generated item text");
        ValidateGeneratedStringFormatContainer(
            presentationCore,
            stringFormatItemsList,
            GetCollectionItem(sourceLabels, 2),
            "formatted label gamma",
            "compiled ItemStringFormat collection-change generated item text");
    }

    private static void ValidateGeneratedStringFormatContainer(
        Assembly presentationCore,
        object stringFormatItemsList,
        object item,
        string expectedText,
        string description)
    {
        Invoke(stringFormatItemsList, "ScrollIntoView", item);
        Invoke(stringFormatItemsList, "UpdateLayout");

        object itemContainerGenerator = GetProperty(stringFormatItemsList, "ItemContainerGenerator");
        object itemContainer = Invoke(itemContainerGenerator, "ContainerFromItem", item);
        AssertType(itemContainer, "System.Windows.Controls.ListBoxItem", description);
        AssertEqual("formatted {0}", GetProperty(itemContainer, "ContentStringFormat"), "compiled ItemStringFormat generated container format");
        Invoke(itemContainer, "ApplyTemplate");
        Invoke(itemContainer, "UpdateLayout");

        object textBlock = FindVisualDescendantByTypeName(presentationCore, itemContainer, "System.Windows.Controls.TextBlock")
            ?? throw new InvalidOperationException("Expected ItemStringFormat container to generate a TextBlock.");
        AssertEqual(expectedText, GetProperty(textBlock, "Text"), description);
    }

    private static void ValidatePostShowGroupStyleHeader(Assembly presentationCore, object window)
    {
        object groupedItemsList = GetField(window, "GroupedItemsList");
        Invoke(groupedItemsList, "ApplyTemplate");
        Invoke(groupedItemsList, "UpdateLayout");

        object groupHeaderTextBlock = FindVisualDescendantByName(presentationCore, groupedItemsList, "GroupHeaderTextBlock")
            ?? throw new InvalidOperationException("Expected grouped ListBox to generate GroupHeaderTextBlock.");
        AssertType(groupHeaderTextBlock, "System.Windows.Controls.TextBlock", "compiled GroupStyle generated header TextBlock");
        AssertEqual("primary group", GetProperty(groupHeaderTextBlock, "Text"), "compiled GroupStyle header generated binding");
        AssertEqual("group header template", GetProperty(groupHeaderTextBlock, "Tag"), "compiled GroupStyle header generated value");
    }

    private static void ValidateGeneratedItemTemplateTextBlock(
        Assembly presentationCore,
        object itemsList,
        object item,
        string expectedText,
        string expectedContainerTag,
        string expectedTag,
        string itemContainerDescription,
        string itemContainerTagDescription,
        string textBlockDescription,
        string bindingDescription,
        string tagDescription)
    {
        Invoke(itemsList, "ScrollIntoView", item);
        Invoke(itemsList, "UpdateLayout");

        object itemContainerGenerator = GetProperty(itemsList, "ItemContainerGenerator");
        object itemContainer = Invoke(itemContainerGenerator, "ContainerFromItem", item);
        AssertType(itemContainer, "System.Windows.Controls.ListBoxItem", itemContainerDescription);
        AssertEqual(expectedContainerTag, GetProperty(itemContainer, "Tag"), itemContainerTagDescription);
        Invoke(itemContainer, "ApplyTemplate");
        Invoke(itemContainer, "UpdateLayout");

        object itemTextBlock = FindVisualDescendantByName(presentationCore, itemContainer, "ItemTextBlock")
            ?? throw new InvalidOperationException("Expected generated item container to contain ItemTextBlock.");
        AssertType(itemTextBlock, "System.Windows.Controls.TextBlock", textBlockDescription);
        AssertEqual(expectedText, GetProperty(itemTextBlock, "Text"), bindingDescription);
        AssertEqual(expectedTag, GetProperty(itemTextBlock, "Tag"), tagDescription);
    }

    private static void ValidatePostShowItemTemplateSelector(Assembly presentationCore, object window)
    {
        object selectorItemsList = GetField(window, "SelectorItemsList");
        object sourceItems = GetProperty(GetProperty(window, "DataContext"), "Items");

        ValidateGeneratedSelectedTemplateTextBlock(
            presentationCore,
            selectorItemsList,
            GetCollectionItem(sourceItems, 0),
            "item alpha",
            "selector alpha template",
            "compiled DataTemplateSelector alpha generated item container",
            "compiled DataTemplateSelector alpha generated TextBlock",
            "compiled DataTemplateSelector alpha generated TextBlock binding",
            "compiled DataTemplateSelector alpha generated value");

        ValidateGeneratedSelectedTemplateTextBlock(
            presentationCore,
            selectorItemsList,
            GetCollectionItem(sourceItems, 1),
            "item beta",
            "selector default template",
            "compiled DataTemplateSelector default generated item container",
            "compiled DataTemplateSelector default generated TextBlock",
            "compiled DataTemplateSelector default generated TextBlock binding",
            "compiled DataTemplateSelector default generated value");
    }

    private static void ValidateGeneratedSelectedTemplateTextBlock(
        Assembly presentationCore,
        object selectorItemsList,
        object item,
        string expectedText,
        string expectedTag,
        string itemContainerDescription,
        string textBlockDescription,
        string bindingDescription,
        string tagDescription)
    {
        Invoke(selectorItemsList, "ScrollIntoView", item);
        Invoke(selectorItemsList, "UpdateLayout");

        object itemContainerGenerator = GetProperty(selectorItemsList, "ItemContainerGenerator");
        object itemContainer = Invoke(itemContainerGenerator, "ContainerFromItem", item);
        AssertType(itemContainer, "System.Windows.Controls.ListBoxItem", itemContainerDescription);
        Invoke(itemContainer, "ApplyTemplate");
        Invoke(itemContainer, "UpdateLayout");

        object itemTextBlock = FindVisualDescendantByName(presentationCore, itemContainer, "SelectorTemplateTextBlock")
            ?? throw new InvalidOperationException("Expected selector-generated item container to contain SelectorTemplateTextBlock.");
        AssertType(itemTextBlock, "System.Windows.Controls.TextBlock", textBlockDescription);
        AssertEqual(expectedText, GetProperty(itemTextBlock, "Text"), bindingDescription);
        AssertEqual(expectedTag, GetProperty(itemTextBlock, "Tag"), tagDescription);
    }

    private static void ValidatePostShowItemContainerStyleSelector(Assembly presentationCore, object window)
    {
        object styleSelectorItemsList = GetField(window, "StyleSelectorItemsList");
        object sourceItems = GetProperty(GetProperty(window, "DataContext"), "Items");

        ValidateGeneratedStyleSelectorItem(
            presentationCore,
            styleSelectorItemsList,
            GetCollectionItem(sourceItems, 0),
            "item alpha",
            "style selector alpha container",
            "compiled ItemContainerStyleSelector alpha generated item container",
            "compiled ItemContainerStyleSelector alpha generated container style",
            "compiled ItemContainerStyleSelector alpha generated TextBlock",
            "compiled ItemContainerStyleSelector alpha generated TextBlock binding");

        ValidateGeneratedStyleSelectorItem(
            presentationCore,
            styleSelectorItemsList,
            GetCollectionItem(sourceItems, 1),
            "item beta",
            "style selector default container",
            "compiled ItemContainerStyleSelector default generated item container",
            "compiled ItemContainerStyleSelector default generated container style",
            "compiled ItemContainerStyleSelector default generated TextBlock",
            "compiled ItemContainerStyleSelector default generated TextBlock binding");
    }

    private static void ValidateGeneratedStyleSelectorItem(
        Assembly presentationCore,
        object styleSelectorItemsList,
        object item,
        string expectedText,
        string expectedContainerTag,
        string itemContainerDescription,
        string itemContainerTagDescription,
        string textBlockDescription,
        string bindingDescription)
    {
        Invoke(styleSelectorItemsList, "ScrollIntoView", item);
        Invoke(styleSelectorItemsList, "UpdateLayout");

        object itemContainerGenerator = GetProperty(styleSelectorItemsList, "ItemContainerGenerator");
        object itemContainer = Invoke(itemContainerGenerator, "ContainerFromItem", item);
        AssertType(itemContainer, "System.Windows.Controls.ListBoxItem", itemContainerDescription);
        AssertEqual(expectedContainerTag, GetProperty(itemContainer, "Tag"), itemContainerTagDescription);
        Invoke(itemContainer, "ApplyTemplate");
        Invoke(itemContainer, "UpdateLayout");

        object itemTextBlock = FindVisualDescendantByName(presentationCore, itemContainer, "StyleSelectorItemTextBlock")
            ?? throw new InvalidOperationException("Expected style-selector-generated item container to contain StyleSelectorItemTextBlock.");
        AssertType(itemTextBlock, "System.Windows.Controls.TextBlock", textBlockDescription);
        AssertEqual(expectedText, GetProperty(itemTextBlock, "Text"), bindingDescription);
        AssertEqual("style selector item template", GetProperty(itemTextBlock, "Tag"), "compiled ItemContainerStyleSelector generated TextBlock tag");
    }

    private static void ValidatePostShowImplicitDataTemplate(Assembly presentationCore, object window)
    {
        object implicitTemplateHost = GetField(window, "ImplicitTemplateHost");
        Invoke(implicitTemplateHost, "ApplyTemplate");
        Invoke(implicitTemplateHost, "UpdateLayout");

        object detailTextBlock = FindVisualDescendantByName(presentationCore, implicitTemplateHost, "ImplicitDetailTextBlock")
            ?? throw new InvalidOperationException("Expected implicit data template host to contain ImplicitDetailTextBlock.");
        AssertType(detailTextBlock, "System.Windows.Controls.TextBlock", "compiled implicit DataTemplate generated TextBlock");
        AssertEqual("detail from implicit template", GetProperty(detailTextBlock, "Text"), "compiled implicit DataTemplate generated TextBlock binding");
        AssertEqual("implicit data template", GetProperty(detailTextBlock, "Tag"), "compiled implicit DataTemplate generated value");
    }

    private static void ValidatePostShowContentTemplateSelector(Assembly presentationCore, object window)
    {
        object selectorTemplateHost = GetField(window, "SelectorTemplateHost");
        Invoke(selectorTemplateHost, "ApplyTemplate");
        Invoke(selectorTemplateHost, "UpdateLayout");

        object detailTextBlock = FindVisualDescendantByName(presentationCore, selectorTemplateHost, "SelectedDetailTextBlock")
            ?? throw new InvalidOperationException("Expected ContentTemplateSelector host to contain SelectedDetailTextBlock.");
        AssertType(detailTextBlock, "System.Windows.Controls.TextBlock", "compiled ContentTemplateSelector generated TextBlock");
        AssertEqual("detail from implicit template", GetProperty(detailTextBlock, "Text"), "compiled ContentTemplateSelector generated TextBlock binding");
        AssertEqual("content template selector selected", GetProperty(detailTextBlock, "Tag"), "compiled ContentTemplateSelector generated value");
    }

    private static void ValidatePostShowHierarchicalDataTemplate(Assembly presentationCore, object window)
    {
        object nodeTree = GetField(window, "NodeTree");
        object sourceNodes = GetProperty(GetProperty(window, "DataContext"), "Nodes");
        object rootNode = GetCollectionItem(sourceNodes, 0);
        Invoke(nodeTree, "UpdateLayout");

        object rootContainer = Invoke(GetProperty(nodeTree, "ItemContainerGenerator"), "ContainerFromItem", rootNode);
        AssertType(rootContainer, "System.Windows.Controls.TreeViewItem", "compiled HierarchicalDataTemplate root container");
        Invoke(rootContainer, "ApplyTemplate");
        SetProperty(rootContainer, "IsExpanded", true);
        Invoke(rootContainer, "UpdateLayout");
        Invoke(nodeTree, "UpdateLayout");

        object rootTextBlock = FindVisualDescendantByName(presentationCore, rootContainer, "NodeTextBlock")
            ?? throw new InvalidOperationException("Expected generated root TreeViewItem to contain NodeTextBlock.");
        AssertType(rootTextBlock, "System.Windows.Controls.TextBlock", "compiled HierarchicalDataTemplate root generated TextBlock");
        AssertEqual("root node", GetProperty(rootTextBlock, "Text"), "compiled HierarchicalDataTemplate root generated TextBlock binding");
        AssertEqual("hierarchical template", GetProperty(rootTextBlock, "Tag"), "compiled HierarchicalDataTemplate root generated value");

        object rootChildren = GetProperty(rootNode, "Children");
        AssertCollectionCount(GetProperty(rootContainer, "Items"), expected: 2, "compiled HierarchicalDataTemplate generated child items");
        object childNode = GetCollectionItem(rootChildren, 0);
        object childContainer = Invoke(GetProperty(rootContainer, "ItemContainerGenerator"), "ContainerFromItem", childNode);
        AssertType(childContainer, "System.Windows.Controls.TreeViewItem", "compiled HierarchicalDataTemplate child container");
        Invoke(childContainer, "ApplyTemplate");
        Invoke(childContainer, "UpdateLayout");

        object childTextBlock = FindVisualDescendantByName(presentationCore, childContainer, "NodeTextBlock")
            ?? throw new InvalidOperationException("Expected generated child TreeViewItem to contain NodeTextBlock.");
        AssertType(childTextBlock, "System.Windows.Controls.TextBlock", "compiled HierarchicalDataTemplate child generated TextBlock");
        AssertEqual("child alpha", GetProperty(childTextBlock, "Text"), "compiled HierarchicalDataTemplate child generated TextBlock binding");
        AssertEqual("hierarchical template", GetProperty(childTextBlock, "Tag"), "compiled HierarchicalDataTemplate child generated value");
    }

    private static void ValidatePostShowTabControl(Assembly presentationCore, object window)
    {
        object tabControl = GetField(window, "SmokeTabControl");
        Invoke(tabControl, "ApplyTemplate");
        Invoke(tabControl, "UpdateLayout");

        object items = GetProperty(tabControl, "Items");
        object betaTab = GetCollectionItem(items, 1);
        AssertSame(betaTab, GetProperty(tabControl, "SelectedItem"), "compiled TabControl post-show selected item");
        AssertEqual(1, GetProperty(tabControl, "SelectedIndex"), "compiled TabControl post-show selected index");
        AssertSame(GetProperty(betaTab, "Content"), GetProperty(tabControl, "SelectedContent"), "compiled TabControl post-show selected content");

        object betaContent = FindVisualDescendantByName(presentationCore, tabControl, "BetaTabContent")
            ?? throw new InvalidOperationException("Expected selected TabControl content to contain BetaTabContent.");
        AssertType(betaContent, "System.Windows.Controls.TextBlock", "compiled TabControl beta generated content");
        AssertEqual("beta tab content", GetProperty(betaContent, "Text"), "compiled TabControl beta generated content text");
        AssertEqual("tab beta content", GetProperty(betaContent, "Tag"), "compiled TabControl beta generated content tag");

        SetProperty(tabControl, "SelectedIndex", 0);
        Invoke(tabControl, "UpdateLayout");

        object alphaTab = GetCollectionItem(items, 0);
        AssertSame(alphaTab, GetProperty(tabControl, "SelectedItem"), "compiled TabControl selected item after index change");
        AssertEqual(0, GetProperty(tabControl, "SelectedIndex"), "compiled TabControl selected index after change");
        AssertSame(GetProperty(alphaTab, "Content"), GetProperty(tabControl, "SelectedContent"), "compiled TabControl selected content after change");

        object alphaContent = FindVisualDescendantByName(presentationCore, tabControl, "AlphaTabContent")
            ?? throw new InvalidOperationException("Expected selected TabControl content to contain AlphaTabContent.");
        AssertType(alphaContent, "System.Windows.Controls.TextBlock", "compiled TabControl alpha generated content");
        AssertEqual("alpha tab content", GetProperty(alphaContent, "Text"), "compiled TabControl alpha generated content text");
        AssertEqual("tab alpha content", GetProperty(alphaContent, "Tag"), "compiled TabControl alpha generated content tag");

        SetProperty(tabControl, "SelectedIndex", 1);
        Invoke(tabControl, "UpdateLayout");
    }

    private static void ValidatePostShowSectionControls(Assembly presentationCore, object window)
    {
        object expander = GetField(window, "SmokeExpander");
        Invoke(expander, "ApplyTemplate");
        Invoke(expander, "UpdateLayout");

        object expanderHeader = FindVisualDescendantByName(presentationCore, expander, "ExpanderHeaderTextBlock")
            ?? throw new InvalidOperationException("Expected Expander to generate ExpanderHeaderTextBlock.");
        AssertType(expanderHeader, "System.Windows.Controls.TextBlock", "compiled Expander generated header");
        AssertEqual("detail from implicit template", GetProperty(expanderHeader, "Text"), "compiled Expander generated header binding");
        AssertEqual("expander header template", GetProperty(expanderHeader, "Tag"), "compiled Expander generated header tag");

        object expanderContent = FindVisualDescendantByName(presentationCore, expander, "ExpanderContentText")
            ?? throw new InvalidOperationException("Expected expanded Expander to generate ExpanderContentText.");
        AssertType(expanderContent, "System.Windows.Controls.TextBlock", "compiled Expander generated content");
        AssertEqual("updated greeting from property change", GetProperty(expanderContent, "Text"), "compiled Expander generated content binding");
        AssertEqual("expander content", GetProperty(expanderContent, "Tag"), "compiled Expander generated content tag");

        SetProperty(expander, "IsExpanded", false);
        AssertEqual(false, GetProperty(expander, "IsExpanded"), "compiled Expander collapsed state");
        SetProperty(expander, "IsExpanded", true);
        Invoke(expander, "UpdateLayout");
        AssertEqual(true, GetProperty(expander, "IsExpanded"), "compiled Expander restored expanded state");

        object groupBox = GetField(window, "SmokeGroupBox");
        Invoke(groupBox, "ApplyTemplate");
        Invoke(groupBox, "UpdateLayout");

        object groupHeader = FindVisualDescendantByName(presentationCore, groupBox, "GroupBoxHeaderTextBlock")
            ?? throw new InvalidOperationException("Expected GroupBox to generate GroupBoxHeaderTextBlock.");
        AssertType(groupHeader, "System.Windows.Controls.TextBlock", "compiled GroupBox generated header");
        AssertEqual("detail from implicit template", GetProperty(groupHeader, "Text"), "compiled GroupBox generated header binding");
        AssertEqual("group box header template", GetProperty(groupHeader, "Tag"), "compiled GroupBox generated header tag");

        object groupContent = FindVisualDescendantByName(presentationCore, groupBox, "GroupBoxContentText")
            ?? throw new InvalidOperationException("Expected GroupBox to generate GroupBoxContentText.");
        AssertType(groupContent, "System.Windows.Controls.TextBlock", "compiled GroupBox generated content");
        AssertEqual("run bound command", GetProperty(groupContent, "Text"), "compiled GroupBox generated content binding");
        AssertEqual("group box content", GetProperty(groupContent, "Tag"), "compiled GroupBox generated content tag");
    }

    private static void ValidateObjectDataProvider(object window)
    {
        object provider = Invoke(window, "TryFindResource", "ProviderGreeting");
        AssertType(provider, "System.Windows.Data.ObjectDataProvider", "compiled ObjectDataProvider resource");
        AssertEqual(false, GetProperty(provider, "IsAsynchronous"), "compiled ObjectDataProvider synchronous flag");
        AssertEqual("CreateProviderGreeting", GetProperty(provider, "MethodName"), "compiled ObjectDataProvider method name");
        Type providerFactoryType = window.GetType().Assembly.GetType("ProGPU.Wpf.RealXamlCompilerHarness.ProviderDataFactory", throwOnError: true)
            ?? throw new TypeLoadException("ProGPU.Wpf.RealXamlCompilerHarness.ProviderDataFactory");
        AssertSame(providerFactoryType, GetProperty(provider, "ObjectType"), "compiled ObjectDataProvider object type");
        AssertType(GetProperty(provider, "ObjectInstance"), "ProGPU.Wpf.RealXamlCompilerHarness.ProviderDataFactory", "compiled ObjectDataProvider object instance");
        AssertEqual("provider data 7", GetProperty(provider, "Data"), "compiled ObjectDataProvider data");

        object methodParameters = GetProperty(provider, "MethodParameters");
        AssertCollectionCount(methodParameters, expected: 2, "compiled ObjectDataProvider method parameters");
        AssertEqual("provider", GetCollectionItem(methodParameters, 0), "compiled ObjectDataProvider first parameter");
        AssertEqual("7", GetCollectionItem(methodParameters, 1), "compiled ObjectDataProvider second parameter");

        object providerGreetingBlock = GetField(window, "ProviderGreetingBlock");
        AssertType(providerGreetingBlock, "System.Windows.Controls.TextBlock", "compiled ObjectDataProvider TextBlock");
        AssertEqual("provider data 7", GetProperty(providerGreetingBlock, "Text"), "compiled ObjectDataProvider bound text");

        object bindingExpression = GetBindingExpression(providerGreetingBlock, "TextProperty");
        object parentBinding = GetProperty(bindingExpression, "ParentBinding");
        AssertSame(provider, GetProperty(parentBinding, "Source"), "compiled ObjectDataProvider binding source");
    }

    private static void ValidateXmlDataProvider(object window)
    {
        object provider = Invoke(window, "TryFindResource", "ProviderXml");
        AssertType(provider, "System.Windows.Data.XmlDataProvider", "compiled XmlDataProvider resource");
        AssertEqual("/Smoke/Message", GetProperty(provider, "XPath"), "compiled XmlDataProvider XPath");
        AssertEqual(false, GetProperty(provider, "IsAsynchronous"), "compiled XmlDataProvider synchronous flag");

        object xmlProviderBlock = GetField(window, "XmlProviderBlock");
        AssertType(xmlProviderBlock, "System.Windows.Controls.TextBlock", "compiled XmlDataProvider TextBlock");
        AssertEqual("xml provider text", GetProperty(xmlProviderBlock, "Text"), "compiled XmlDataProvider XPath bound text");

        object bindingExpression = GetBindingExpression(xmlProviderBlock, "TextProperty");
        object parentBinding = GetProperty(bindingExpression, "ParentBinding");
        AssertSame(provider, GetProperty(parentBinding, "Source"), "compiled XmlDataProvider binding source");
        AssertEqual("@Text", GetProperty(parentBinding, "XPath"), "compiled XmlDataProvider binding XPath");
    }

    private static void ValidateStoryboardEventTrigger(object window)
    {
        object storyboardTargetBlock = GetField(window, "StoryboardTargetBlock");
        AssertType(storyboardTargetBlock, "System.Windows.Controls.TextBlock", "compiled Storyboard target TextBlock");
        AssertEqual("compiled storyboard target", GetProperty(storyboardTargetBlock, "Text"), "compiled Storyboard target text");
        AssertEqual(1.0, GetProperty(storyboardTargetBlock, "Opacity"), "compiled Storyboard target initial opacity");
        AssertEqual(0, GetProperty(window, "StoryboardTargetLoadedCount"), "compiled Storyboard target initial Loaded count");

        object triggers = GetProperty(storyboardTargetBlock, "Triggers");
        AssertCollectionCount(triggers, expected: 1, "compiled EventTrigger collection");
        object eventTrigger = GetCollectionItem(triggers, 0);
        AssertType(eventTrigger, "System.Windows.EventTrigger", "compiled EventTrigger");
        AssertEqual("Loaded", GetProperty(GetProperty(eventTrigger, "RoutedEvent"), "Name"), "compiled EventTrigger routed event");

        object actions = GetProperty(eventTrigger, "Actions");
        AssertCollectionCount(actions, expected: 1, "compiled EventTrigger actions");
        object beginStoryboard = GetCollectionItem(actions, 0);
        AssertType(beginStoryboard, "System.Windows.Media.Animation.BeginStoryboard", "compiled BeginStoryboard action");

        object storyboard = GetProperty(beginStoryboard, "Storyboard");
        AssertType(storyboard, "System.Windows.Media.Animation.Storyboard", "compiled Storyboard");
        object children = GetProperty(storyboard, "Children");
        AssertCollectionCount(children, expected: 1, "compiled Storyboard children");
        object doubleAnimation = GetCollectionItem(children, 0);
        AssertType(doubleAnimation, "System.Windows.Media.Animation.DoubleAnimation", "compiled DoubleAnimation");
        AssertEqual(0.37, GetProperty(doubleAnimation, "To"), "compiled DoubleAnimation target value");
        AssertEqual("00:00:00", GetProperty(doubleAnimation, "Duration").ToString(), "compiled DoubleAnimation duration");
        AssertEqual("HoldEnd", GetProperty(doubleAnimation, "FillBehavior").ToString(), "compiled DoubleAnimation fill behavior");

        object storyboardTriggerButton = GetField(window, "StoryboardTriggerButton");
        AssertType(storyboardTriggerButton, "System.Windows.Controls.Button", "compiled click Storyboard trigger Button");
        AssertEqual("run storyboard trigger", GetProperty(storyboardTriggerButton, "Content"), "compiled click Storyboard trigger Button content");
        AssertEqual(1.0, GetProperty(storyboardTriggerButton, "Opacity"), "compiled click Storyboard trigger Button initial opacity");

        object clickTriggers = GetProperty(storyboardTriggerButton, "Triggers");
        AssertCollectionCount(clickTriggers, expected: 1, "compiled click EventTrigger collection");
        object clickEventTrigger = GetCollectionItem(clickTriggers, 0);
        AssertType(clickEventTrigger, "System.Windows.EventTrigger", "compiled click EventTrigger");
        AssertEqual("Click", GetProperty(GetProperty(clickEventTrigger, "RoutedEvent"), "Name"), "compiled click EventTrigger routed event");

        object clickActions = GetProperty(clickEventTrigger, "Actions");
        AssertCollectionCount(clickActions, expected: 1, "compiled click EventTrigger actions");
        object clickBeginStoryboard = GetCollectionItem(clickActions, 0);
        AssertType(clickBeginStoryboard, "System.Windows.Media.Animation.BeginStoryboard", "compiled click BeginStoryboard action");

        object clickStoryboard = GetProperty(clickBeginStoryboard, "Storyboard");
        AssertType(clickStoryboard, "System.Windows.Media.Animation.Storyboard", "compiled click Storyboard");
        object clickChildren = GetProperty(clickStoryboard, "Children");
        AssertCollectionCount(clickChildren, expected: 1, "compiled click Storyboard children");
        object clickDoubleAnimation = GetCollectionItem(clickChildren, 0);
        AssertType(clickDoubleAnimation, "System.Windows.Media.Animation.DoubleAnimation", "compiled click DoubleAnimation");
        AssertEqual(0.64, GetProperty(clickDoubleAnimation, "To"), "compiled click DoubleAnimation target value");
        AssertEqual("00:00:00", GetProperty(clickDoubleAnimation, "Duration").ToString(), "compiled click DoubleAnimation duration");
        AssertEqual("HoldEnd", GetProperty(clickDoubleAnimation, "FillBehavior").ToString(), "compiled click DoubleAnimation fill behavior");
    }

    private static void ValidatePostShowClickStoryboardEventTrigger(object window, Action flushRender)
    {
        object storyboardTriggerButton = GetField(window, "StoryboardTriggerButton");
        AssertEqual(1.0, GetProperty(storyboardTriggerButton, "Opacity"), "compiled click Storyboard trigger Button pre-click opacity");

        Invoke(storyboardTriggerButton, "OnClick");
        flushRender();

        AssertEqual(0.64, GetProperty(storyboardTriggerButton, "Opacity"), "compiled click Storyboard trigger Button post-click opacity");
    }

    private static void ValidatePostShowStyleTriggerActions(object window, Action flushRender)
    {
        object triggerActionButton = GetField(window, "TriggerActionButton");
        AssertEqual(1.0, GetProperty(triggerActionButton, "Opacity"), "compiled style Trigger action initial opacity");

        SetProperty(triggerActionButton, "IsEnabled", false);
        flushRender();
        AssertClose(0.41, Convert.ToDouble(GetProperty(triggerActionButton, "Opacity")), 0.0001, "compiled style Trigger EnterActions opacity");

        SetProperty(triggerActionButton, "IsEnabled", true);
        flushRender();
        AssertClose(1.0, Convert.ToDouble(GetProperty(triggerActionButton, "Opacity")), 0.0001, "compiled style Trigger ExitActions opacity");
    }

    private static void ValidatePostShowMultiTriggerActions(object window, Action flushRender)
    {
        object multiTriggerActionButton = GetField(window, "MultiTriggerActionButton");
        AssertEqual(true, GetProperty(multiTriggerActionButton, "IsEnabled"), "compiled MultiTrigger action initial enabled state");
        AssertEqual(false, GetProperty(multiTriggerActionButton, "IsDefault"), "compiled MultiTrigger action initial default state");
        AssertEqual(1.0, GetProperty(multiTriggerActionButton, "Opacity"), "compiled style MultiTrigger action initial opacity");

        SetProperty(multiTriggerActionButton, "IsDefault", true);
        flushRender();
        AssertClose(0.74, Convert.ToDouble(GetProperty(multiTriggerActionButton, "Opacity")), 0.0001, "compiled style MultiTrigger EnterActions opacity");

        SetProperty(multiTriggerActionButton, "IsEnabled", false);
        flushRender();
        AssertClose(1.0, Convert.ToDouble(GetProperty(multiTriggerActionButton, "Opacity")), 0.0001, "compiled style MultiTrigger ExitActions opacity");

        SetProperty(multiTriggerActionButton, "IsEnabled", true);
        flushRender();
        AssertClose(0.74, Convert.ToDouble(GetProperty(multiTriggerActionButton, "Opacity")), 0.0001, "compiled style MultiTrigger restored enter opacity");

        SetProperty(multiTriggerActionButton, "IsDefault", false);
        flushRender();
        AssertClose(1.0, Convert.ToDouble(GetProperty(multiTriggerActionButton, "Opacity")), 0.0001, "compiled style MultiTrigger restored opacity");
    }

    private static void ValidatePostShowDataTriggerActions(object window, Action flushDataBindAndRender)
    {
        object dataContext = GetProperty(window, "DataContext");
        object dataTriggerActionButton = GetField(window, "DataTriggerActionButton");
        AssertEqual(false, GetProperty(dataContext, "IsTriggerActionActive"), "compiled DataTrigger action initial view-model state");
        AssertEqual(1.0, GetProperty(dataTriggerActionButton, "Opacity"), "compiled style DataTrigger action initial opacity");

        SetProperty(dataContext, "IsTriggerActionActive", true);
        flushDataBindAndRender();
        AssertClose(0.52, Convert.ToDouble(GetProperty(dataTriggerActionButton, "Opacity")), 0.0001, "compiled style DataTrigger EnterActions opacity");

        SetProperty(dataContext, "IsTriggerActionActive", false);
        flushDataBindAndRender();
        AssertClose(1.0, Convert.ToDouble(GetProperty(dataTriggerActionButton, "Opacity")), 0.0001, "compiled style DataTrigger ExitActions opacity");
    }

    private static void ValidatePostShowMultiDataTriggerActions(object window, Action flushDataBindAndRender)
    {
        object dataContext = GetProperty(window, "DataContext");
        object multiDataTriggerActionButton = GetField(window, "MultiDataTriggerActionButton");
        AssertEqual(false, GetProperty(dataContext, "IsMultiTriggerActionReady"), "compiled MultiDataTrigger action initial ready state");
        AssertEqual(false, GetProperty(dataContext, "IsMultiTriggerActionArmed"), "compiled MultiDataTrigger action initial armed state");
        AssertEqual(1.0, GetProperty(multiDataTriggerActionButton, "Opacity"), "compiled style MultiDataTrigger action initial opacity");

        SetProperty(dataContext, "IsMultiTriggerActionReady", true);
        flushDataBindAndRender();
        AssertClose(1.0, Convert.ToDouble(GetProperty(multiDataTriggerActionButton, "Opacity")), 0.0001, "compiled style MultiDataTrigger partial-condition opacity");

        SetProperty(dataContext, "IsMultiTriggerActionArmed", true);
        flushDataBindAndRender();
        AssertClose(0.63, Convert.ToDouble(GetProperty(multiDataTriggerActionButton, "Opacity")), 0.0001, "compiled style MultiDataTrigger EnterActions opacity");

        SetProperty(dataContext, "IsMultiTriggerActionReady", false);
        flushDataBindAndRender();
        AssertClose(1.0, Convert.ToDouble(GetProperty(multiDataTriggerActionButton, "Opacity")), 0.0001, "compiled style MultiDataTrigger ExitActions opacity");

        SetProperty(dataContext, "IsMultiTriggerActionArmed", false);
        flushDataBindAndRender();
        AssertClose(1.0, Convert.ToDouble(GetProperty(multiDataTriggerActionButton, "Opacity")), 0.0001, "compiled style MultiDataTrigger restored opacity");
    }

    private static void ValidateMarkupExtension(object window)
    {
        object markupExtensionBlock = GetField(window, "MarkupExtensionBlock");
        AssertType(markupExtensionBlock, "System.Windows.Controls.TextBlock", "compiled MarkupExtension TextBlock");
        AssertEqual("compiled markup extension", GetProperty(markupExtensionBlock, "Text"), "compiled MarkupExtension provided text");
    }

    private static void ValidateMergedResourceDictionary(object window, object application)
    {
        object resources = GetProperty(application, "Resources");
        object expectedBrush = Invoke(application, "TryFindResource", "MergedAccentBrush");
        object expectedMargin = Invoke(application, "TryFindResource", "MergedBlockMargin");

        object mergedResourceBlock = GetField(window, "MergedResourceBlock");
        AssertType(mergedResourceBlock, "System.Windows.Controls.TextBlock", "compiled merged-resource TextBlock");
        AssertEqual("compiled merged resource", GetProperty(mergedResourceBlock, "Text"), "compiled merged-resource TextBlock text");
        AssertSame(expectedBrush, GetProperty(mergedResourceBlock, "Foreground"), "compiled merged-resource foreground");

        object actualMargin = GetProperty(mergedResourceBlock, "Margin");
        AssertEqual(GetProperty(expectedMargin, "Left"), GetProperty(actualMargin, "Left"), "compiled merged-resource margin left");
        AssertEqual(GetProperty(expectedMargin, "Top"), GetProperty(actualMargin, "Top"), "compiled merged-resource margin top");
        AssertEqual(GetProperty(expectedMargin, "Right"), GetProperty(actualMargin, "Right"), "compiled merged-resource margin right");
        AssertEqual(GetProperty(expectedMargin, "Bottom"), GetProperty(actualMargin, "Bottom"), "compiled merged-resource margin bottom");

        Assembly presentationFramework = window.GetType().BaseType?.Assembly
            ?? throw new InvalidOperationException("Expected compiled window to derive from PresentationFramework Window.");
        object componentResourceKey = Create(
            presentationFramework,
            "System.Windows.ComponentResourceKey",
            window.GetType(),
            "SmokeComponentAccentBrush");
        AssertType(componentResourceKey, "System.Windows.ComponentResourceKey", "compiled ComponentResourceKey lookup key");
        AssertSame(window.GetType(), GetProperty(componentResourceKey, "TypeInTargetAssembly"), "compiled ComponentResourceKey target type");
        AssertEqual("SmokeComponentAccentBrush", GetProperty(componentResourceKey, "ResourceId"), "compiled ComponentResourceKey resource id");

        object componentResourceBrush = GetDictionaryValue(resources, componentResourceKey);
        object componentResourceBlock = GetField(window, "ComponentResourceBlock");
        AssertType(componentResourceBlock, "System.Windows.Controls.TextBlock", "compiled ComponentResourceKey TextBlock");
        AssertEqual("compiled component resource", GetProperty(componentResourceBlock, "Text"), "compiled ComponentResourceKey TextBlock text");
        AssertSame(componentResourceBrush, GetProperty(componentResourceBlock, "Foreground"), "compiled ComponentResourceKey foreground");
        AssertSame(componentResourceBrush, Invoke(application, "TryFindResource", componentResourceKey), "compiled ComponentResourceKey application lookup");
        AssertEqual("#FF2F6B54", GetProperty(GetProperty(componentResourceBlock, "Foreground"), "Color").ToString(), "compiled ComponentResourceKey brush color");
    }

    private static void ValidateScopedResourceLookup(object window, object application)
    {
        object rootPanel = GetField(window, "SmokeRootPanel");
        object rootResources = GetProperty(rootPanel, "Resources");
        object scopedBrush = GetDictionaryValue(rootResources, "ScopedAccentBrush");
        object scopedMargin = GetDictionaryValue(rootResources, "ScopedBlockMargin");

        object scopedResourceBlock = GetField(window, "ScopedResourceBlock");
        AssertType(scopedResourceBlock, "System.Windows.Controls.TextBlock", "compiled scoped-resource TextBlock");
        AssertEqual("compiled scoped resource", GetProperty(scopedResourceBlock, "Text"), "compiled scoped-resource TextBlock text");
        AssertSame(scopedBrush, GetProperty(scopedResourceBlock, "Foreground"), "compiled scoped-resource foreground");
        AssertSame(scopedBrush, Invoke(rootPanel, "FindResource", "ScopedAccentBrush"), "compiled root-panel FindResource scoped brush");
        AssertSame(scopedBrush, Invoke(scopedResourceBlock, "FindResource", "ScopedAccentBrush"), "compiled child FindResource scoped brush");
        AssertSame(scopedBrush, InvokeNullable(scopedResourceBlock, "TryFindResource", "ScopedAccentBrush")!, "compiled child TryFindResource scoped brush");
        AssertEqual("#FF6B4E9B", GetProperty(scopedBrush, "Color").ToString(), "compiled scoped-resource brush color");

        object actualMargin = GetProperty(scopedResourceBlock, "Margin");
        AssertEqual(GetProperty(scopedMargin, "Left"), GetProperty(actualMargin, "Left"), "compiled scoped-resource margin left");
        AssertEqual(GetProperty(scopedMargin, "Top"), GetProperty(actualMargin, "Top"), "compiled scoped-resource margin top");
        AssertEqual(GetProperty(scopedMargin, "Right"), GetProperty(actualMargin, "Right"), "compiled scoped-resource margin right");
        AssertEqual(GetProperty(scopedMargin, "Bottom"), GetProperty(actualMargin, "Bottom"), "compiled scoped-resource margin bottom");

        object applicationBrush = Invoke(application, "FindResource", "AccentBrush");
        AssertSame(applicationBrush, Invoke(scopedResourceBlock, "FindResource", "AccentBrush"), "compiled child FindResource application fallback");
        AssertSame(applicationBrush, InvokeNullable(scopedResourceBlock, "TryFindResource", "AccentBrush")!, "compiled child TryFindResource application fallback");
        AssertEqual(null, InvokeNullable(scopedResourceBlock, "TryFindResource", "DefinitelyMissingResource"), "compiled child TryFindResource missing resource");

        try
        {
            Invoke(scopedResourceBlock, "FindResource", "DefinitelyMissingResource");
            throw new InvalidOperationException("Expected missing FindResource lookup to throw.");
        }
        catch (TargetInvocationException ex)
            when (string.Equals(
                ex.InnerException?.GetType().FullName,
                "System.Windows.ResourceReferenceKeyNotFoundException",
                StringComparison.Ordinal))
        {
        }
    }

    private static void ValidateUnsharedResource(object window, object application)
    {
        object resources = GetProperty(application, "Resources");
        object dictionaryBrush = GetDictionaryValue(resources, "UnsharedAccentBrush");
        object secondDictionaryBrush = GetDictionaryValue(resources, "UnsharedAccentBrush");
        AssertNotSame(dictionaryBrush, secondDictionaryBrush, "compiled x:Shared=false dictionary lookup");

        object borderA = GetField(window, "UnsharedResourceBorderA");
        object borderB = GetField(window, "UnsharedResourceBorderB");
        AssertType(borderA, "System.Windows.Controls.Border", "compiled unshared-resource first Border");
        AssertType(borderB, "System.Windows.Controls.Border", "compiled unshared-resource second Border");

        object backgroundA = GetProperty(borderA, "Background");
        object backgroundB = GetProperty(borderB, "Background");
        AssertType(backgroundA, "System.Windows.Media.SolidColorBrush", "compiled unshared-resource first brush");
        AssertType(backgroundB, "System.Windows.Media.SolidColorBrush", "compiled unshared-resource second brush");
        AssertEqual("#FF4D6F8E", GetProperty(backgroundA, "Color").ToString(), "compiled unshared-resource first color");
        AssertEqual("#FF4D6F8E", GetProperty(backgroundB, "Color").ToString(), "compiled unshared-resource second color");
        AssertNotSame(backgroundA, backgroundB, "compiled x:Shared=false StaticResource consumers");
        AssertNotSame(dictionaryBrush, backgroundA, "compiled x:Shared=false dictionary and first consumer");
    }

    private static void ValidateFreezableBrushResource(object resources)
    {
        object freezableBrush = GetDictionaryValue(resources, "FreezableAccentBrush");
        AssertType(freezableBrush, "System.Windows.Media.SolidColorBrush", "compiled Freezable brush");
        AssertEqual("#FFB15E3B", GetProperty(freezableBrush, "Color").ToString(), "compiled Freezable brush color");
        AssertEqual(true, GetProperty(freezableBrush, "CanFreeze"), "compiled Freezable brush can freeze");
        AssertEqual(true, GetProperty(freezableBrush, "IsFrozen"), "compiled Freezable brush initial BAML frozen state");

        Invoke(freezableBrush, "Freeze");
        AssertEqual(true, GetProperty(freezableBrush, "IsFrozen"), "compiled Freezable brush idempotent frozen state");

        object clone = Invoke(freezableBrush, "Clone");
        AssertType(clone, "System.Windows.Media.SolidColorBrush", "compiled Freezable brush clone");
        AssertNotSame(freezableBrush, clone, "compiled Freezable brush clone instance");
        AssertEqual(false, GetProperty(clone, "IsFrozen"), "compiled Freezable brush clone mutable state");
        AssertEqual("#FFB15E3B", GetProperty(clone, "Color").ToString(), "compiled Freezable brush clone color");

        SetProperty(clone, "Opacity", 0.5);
        AssertEqual(0.5, GetProperty(clone, "Opacity"), "compiled Freezable brush clone mutable opacity");

        object currentValueClone = Invoke(clone, "CloneCurrentValue");
        AssertType(currentValueClone, "System.Windows.Media.SolidColorBrush", "compiled Freezable current-value clone");
        AssertNotSame(clone, currentValueClone, "compiled Freezable current-value clone instance");
        AssertEqual(false, GetProperty(currentValueClone, "IsFrozen"), "compiled Freezable current-value clone mutable state");
        AssertEqual(0.5, GetProperty(currentValueClone, "Opacity"), "compiled Freezable current-value clone opacity");
    }

    private static void ValidateFreezableGradientBrushResource(object resources)
    {
        object gradientBrush = GetDictionaryValue(resources, "FreezableGradientBrush");
        AssertType(gradientBrush, "System.Windows.Media.LinearGradientBrush", "compiled Freezable gradient brush");
        AssertEqual("Reflect", GetProperty(gradientBrush, "SpreadMethod").ToString(), "compiled Freezable gradient brush spread method");
        AssertEqual("RelativeToBoundingBox", GetProperty(gradientBrush, "MappingMode").ToString(), "compiled Freezable gradient brush mapping mode");
        AssertPoint(GetProperty(gradientBrush, "StartPoint"), 0.0, 0.0, "compiled Freezable gradient brush start point");
        AssertPoint(GetProperty(gradientBrush, "EndPoint"), 1.0, 1.0, "compiled Freezable gradient brush end point");
        AssertEqual(true, GetProperty(gradientBrush, "CanFreeze"), "compiled Freezable gradient brush can freeze");
        AssertEqual(true, GetProperty(gradientBrush, "IsFrozen"), "compiled Freezable gradient brush initial BAML frozen state");

        object stops = GetProperty(gradientBrush, "GradientStops");
        AssertType(stops, "System.Windows.Media.GradientStopCollection", "compiled Freezable gradient stop collection");
        AssertCollectionCount(stops, expected: 3, "compiled Freezable gradient stop count");
        AssertEqual(true, GetProperty(stops, "IsFrozen"), "compiled Freezable gradient stop collection frozen state");
        ValidateGradientStop(GetCollectionItem(stops, 0), "#FF2F6B54", 0.0, expectedFrozen: true, "first");
        object middleStop = GetCollectionItem(stops, 1);
        ValidateGradientStop(middleStop, "#FFB15E3B", 0.5, expectedFrozen: true, "middle");
        ValidateGradientStop(GetCollectionItem(stops, 2), "#FF356D9E", 1.0, expectedFrozen: true, "last");

        Invoke(gradientBrush, "Freeze");
        AssertEqual(true, GetProperty(gradientBrush, "IsFrozen"), "compiled Freezable gradient brush idempotent frozen state");

        object clone = Invoke(gradientBrush, "Clone");
        AssertType(clone, "System.Windows.Media.LinearGradientBrush", "compiled Freezable gradient brush clone");
        AssertNotSame(gradientBrush, clone, "compiled Freezable gradient brush clone instance");
        AssertEqual(false, GetProperty(clone, "IsFrozen"), "compiled Freezable gradient brush clone mutable state");

        object cloneStops = GetProperty(clone, "GradientStops");
        AssertNotSame(stops, cloneStops, "compiled Freezable gradient brush clone stop collection");
        AssertEqual(false, GetProperty(cloneStops, "IsFrozen"), "compiled Freezable gradient brush clone stop collection mutable state");
        object cloneMiddleStop = GetCollectionItem(cloneStops, 1);
        AssertNotSame(middleStop, cloneMiddleStop, "compiled Freezable gradient brush clone stop instance");
        AssertEqual(false, GetProperty(cloneMiddleStop, "IsFrozen"), "compiled Freezable gradient brush clone stop mutable state");

        SetProperty(cloneMiddleStop, "Offset", 0.65);
        SetProperty(clone, "Opacity", 0.75);
        AssertEqual(0.65, GetProperty(cloneMiddleStop, "Offset"), "compiled Freezable gradient brush clone mutable stop offset");
        AssertEqual(0.75, GetProperty(clone, "Opacity"), "compiled Freezable gradient brush clone mutable opacity");

        object currentValueClone = Invoke(clone, "CloneCurrentValue");
        AssertType(currentValueClone, "System.Windows.Media.LinearGradientBrush", "compiled Freezable gradient current-value clone");
        AssertNotSame(clone, currentValueClone, "compiled Freezable gradient current-value clone instance");
        AssertEqual(false, GetProperty(currentValueClone, "IsFrozen"), "compiled Freezable gradient current-value clone mutable state");
        AssertEqual(0.75, GetProperty(currentValueClone, "Opacity"), "compiled Freezable gradient current-value clone opacity");

        object currentValueStops = GetProperty(currentValueClone, "GradientStops");
        AssertNotSame(cloneStops, currentValueStops, "compiled Freezable gradient current-value clone stop collection");
        ValidateGradientStop(GetCollectionItem(currentValueStops, 1), "#FFB15E3B", 0.65, expectedFrozen: false, "current-value middle");
    }

    private static void ValidateGradientStop(object stop, string expectedColor, double expectedOffset, bool expectedFrozen, string description)
    {
        AssertType(stop, "System.Windows.Media.GradientStop", $"compiled Freezable gradient {description} stop");
        AssertEqual(expectedColor, GetProperty(stop, "Color").ToString(), $"compiled Freezable gradient {description} stop color");
        AssertEqual(expectedOffset, GetProperty(stop, "Offset"), $"compiled Freezable gradient {description} stop offset");
        AssertEqual(expectedFrozen, GetProperty(stop, "IsFrozen"), $"compiled Freezable gradient {description} stop frozen state");
    }

    private static void ValidateNestedUserControl(object window)
    {
        object nestedControl = GetField(window, "NestedControl");
        AssertType(nestedControl, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeUserControl", "compiled nested UserControl");

        object foundNestedControl = Invoke(window, "FindName", "NestedControl");
        AssertSame(nestedControl, foundNestedControl, "compiled nested UserControl namescope lookup");

        object resources = GetProperty(nestedControl, "Resources");
        object userControlBrush = GetDictionaryValue(resources, "UserControlBrush");
        AssertEqual("#FF3F6E5A", GetProperty(userControlBrush, "Color").ToString(), "compiled UserControl brush color");

        object controlTitle = GetField(nestedControl, "ControlTitle");
        AssertType(controlTitle, "System.Windows.Controls.TextBlock", "compiled UserControl title TextBlock");
        AssertEqual("compiled user control", GetProperty(controlTitle, "Text"), "compiled UserControl title text");
        AssertSame(userControlBrush, GetProperty(controlTitle, "Foreground"), "compiled UserControl resource brush");

        object elementNameMirror = GetField(nestedControl, "ElementNameMirror");
        AssertType(elementNameMirror, "System.Windows.Controls.TextBlock", "compiled UserControl element-name TextBlock");
        AssertEqual("compiled user control", GetProperty(elementNameMirror, "Text"), "compiled UserControl ElementName binding value");
        AssertBindingPath(elementNameMirror, "TextProperty", "Text", "compiled UserControl ElementName binding path");

        object controlEventButton = GetField(nestedControl, "ControlEventButton");
        AssertType(controlEventButton, "System.Windows.Controls.Button", "compiled UserControl event Button");
        AssertEqual("user control event", GetProperty(controlEventButton, "Content"), "compiled UserControl event Button content");
        AssertEqual(0, GetProperty(nestedControl, "ControlClickCount"), "compiled UserControl initial click count");
        Invoke(controlEventButton, "OnClick");
        AssertEqual(1, GetProperty(nestedControl, "ControlClickCount"), "compiled UserControl click handler count");
        AssertEqual("ControlEventButton", GetProperty(nestedControl, "LastControlClickSenderName"), "compiled UserControl click sender name");
        AssertEqual("Click", GetProperty(nestedControl, "LastControlClickRoutedEventName"), "compiled UserControl click routed event name");
    }

    private static void ValidateReadOnlyGridCollectionsAndAttachedProperties(object window)
    {
        object layoutGrid = GetField(window, "AttachedLayoutGrid");
        AssertType(layoutGrid, "System.Windows.Controls.Grid", "compiled attached-layout Grid");
        AssertCollectionCount(GetProperty(layoutGrid, "RowDefinitions"), expected: 2, "compiled Grid row definitions");
        AssertCollectionCount(GetProperty(layoutGrid, "ColumnDefinitions"), expected: 3, "compiled Grid column definitions");
        AssertCollectionCount(GetProperty(layoutGrid, "Children"), expected: 2, "compiled Grid children");
        AssertEqual("Auto", GetProperty(GetCollectionItem(GetProperty(layoutGrid, "RowDefinitions"), 0), "Height").ToString(), "compiled shorthand Grid first row");
        AssertEqual("80", GetProperty(GetCollectionItem(GetProperty(layoutGrid, "ColumnDefinitions"), 1), "Width").ToString(), "compiled shorthand Grid fixed column");
        AssertEqual("*", GetProperty(GetCollectionItem(GetProperty(layoutGrid, "ColumnDefinitions"), 2), "Width").ToString(), "compiled shorthand Grid star column");

        object firstCell = GetField(window, "GridFirstCell");
        AssertType(firstCell, "System.Windows.Controls.TextBlock", "compiled Grid first cell");
        AssertEqual("grid alpha", GetProperty(firstCell, "Text"), "compiled Grid first-cell text");
        AssertEqual(0, GetDependencyPropertyValue(firstCell, layoutGrid.GetType(), "RowProperty"), "compiled Grid first-cell row");
        AssertEqual(0, GetDependencyPropertyValue(firstCell, layoutGrid.GetType(), "ColumnProperty"), "compiled Grid first-cell column");

        object secondCell = GetField(window, "GridSecondCell");
        AssertType(secondCell, "System.Windows.Controls.TextBlock", "compiled Grid second cell");
        AssertEqual("grid beta", GetProperty(secondCell, "Text"), "compiled Grid second-cell text");
        AssertEqual(1, GetDependencyPropertyValue(secondCell, layoutGrid.GetType(), "RowProperty"), "compiled Grid second-cell row");
        AssertEqual(1, GetDependencyPropertyValue(secondCell, layoutGrid.GetType(), "ColumnProperty"), "compiled Grid second-cell column");
    }

    private static void ValidateLayoutPanels(object window)
    {
        object layoutPanel = GetField(window, "LayoutPanelSmoke");
        AssertType(layoutPanel, "System.Windows.Controls.StackPanel", "compiled layout panel host");
        AssertCollectionCount(GetProperty(layoutPanel, "Children"), expected: 6, "compiled layout panel host children");

        object dockPanel = GetField(window, "DockPanelSmoke");
        AssertType(dockPanel, "System.Windows.Controls.DockPanel", "compiled DockPanel");
        AssertEqual(false, GetProperty(dockPanel, "LastChildFill"), "compiled DockPanel LastChildFill");
        AssertCollectionCount(GetProperty(dockPanel, "Children"), expected: 2, "compiled DockPanel children");

        object dockLeft = GetField(window, "DockPanelLeftChild");
        AssertType(dockLeft, "System.Windows.Controls.TextBlock", "compiled DockPanel left child");
        AssertEqual("dock left", GetProperty(dockLeft, "Text"), "compiled DockPanel left child text");
        AssertEqual("Left", GetDependencyPropertyValue(dockLeft, dockPanel.GetType(), "DockProperty").ToString(), "compiled DockPanel left attached Dock");

        object dockRight = GetField(window, "DockPanelRightChild");
        AssertType(dockRight, "System.Windows.Controls.TextBlock", "compiled DockPanel right child");
        AssertEqual("dock right", GetProperty(dockRight, "Text"), "compiled DockPanel right child text");
        AssertEqual("Right", GetDependencyPropertyValue(dockRight, dockPanel.GetType(), "DockProperty").ToString(), "compiled DockPanel right attached Dock");

        object canvas = GetField(window, "CanvasSmoke");
        AssertType(canvas, "System.Windows.Controls.Canvas", "compiled Canvas");
        AssertEqual(120.0, GetProperty(canvas, "Width"), "compiled Canvas width");
        AssertEqual(32.0, GetProperty(canvas, "Height"), "compiled Canvas height");
        AssertCollectionCount(GetProperty(canvas, "Children"), expected: 1, "compiled Canvas children");

        object canvasChild = GetField(window, "CanvasChild");
        AssertType(canvasChild, "System.Windows.Controls.TextBlock", "compiled Canvas child");
        AssertEqual("canvas child", GetProperty(canvasChild, "Text"), "compiled Canvas child text");
        AssertEqual(12.0, GetDependencyPropertyValue(canvasChild, canvas.GetType(), "LeftProperty"), "compiled Canvas left attached property");
        AssertEqual(6.0, GetDependencyPropertyValue(canvasChild, canvas.GetType(), "TopProperty"), "compiled Canvas top attached property");

        object wrapPanel = GetField(window, "WrapPanelSmoke");
        AssertType(wrapPanel, "System.Windows.Controls.WrapPanel", "compiled WrapPanel");
        AssertEqual("Horizontal", GetProperty(wrapPanel, "Orientation").ToString(), "compiled WrapPanel orientation");
        AssertEqual(64.0, GetProperty(wrapPanel, "ItemWidth"), "compiled WrapPanel item width");
        AssertEqual(20.0, GetProperty(wrapPanel, "ItemHeight"), "compiled WrapPanel item height");
        AssertCollectionCount(GetProperty(wrapPanel, "Children"), expected: 2, "compiled WrapPanel children");

        object wrapFirst = GetField(window, "WrapFirstChild");
        AssertType(wrapFirst, "System.Windows.Controls.TextBlock", "compiled WrapPanel first child");
        AssertEqual("wrap one", GetProperty(wrapFirst, "Text"), "compiled WrapPanel first child text");

        object wrapSecond = GetField(window, "WrapSecondChild");
        AssertType(wrapSecond, "System.Windows.Controls.TextBlock", "compiled WrapPanel second child");
        AssertEqual("wrap two", GetProperty(wrapSecond, "Text"), "compiled WrapPanel second child text");

        object uniformGrid = GetField(window, "UniformGridSmoke");
        AssertType(uniformGrid, "System.Windows.Controls.Primitives.UniformGrid", "compiled UniformGrid");
        AssertEqual(2, GetProperty(uniformGrid, "Rows"), "compiled UniformGrid rows");
        AssertEqual(2, GetProperty(uniformGrid, "Columns"), "compiled UniformGrid columns");
        AssertEqual(1, GetProperty(uniformGrid, "FirstColumn"), "compiled UniformGrid first column");
        AssertCollectionCount(GetProperty(uniformGrid, "Children"), expected: 3, "compiled UniformGrid children");

        object uniformFirst = GetField(window, "UniformFirstChild");
        AssertType(uniformFirst, "System.Windows.Controls.TextBlock", "compiled UniformGrid first child");
        AssertEqual("uniform one", GetProperty(uniformFirst, "Text"), "compiled UniformGrid first child text");

        object uniformSecond = GetField(window, "UniformSecondChild");
        AssertType(uniformSecond, "System.Windows.Controls.TextBlock", "compiled UniformGrid second child");
        AssertEqual("uniform two", GetProperty(uniformSecond, "Text"), "compiled UniformGrid second child text");

        object uniformThird = GetField(window, "UniformThirdChild");
        AssertType(uniformThird, "System.Windows.Controls.TextBlock", "compiled UniformGrid third child");
        AssertEqual("uniform three", GetProperty(uniformThird, "Text"), "compiled UniformGrid third child text");

        object sharedScope = GetField(window, "SharedSizeScopePanel");
        AssertType(sharedScope, "System.Windows.Controls.StackPanel", "compiled shared-size scope panel");
        AssertEqual(true, GetDependencyPropertyValue(sharedScope, GetField(window, "SharedSizeGridA").GetType(), "IsSharedSizeScopeProperty"), "compiled Grid shared-size scope flag");
        AssertCollectionCount(GetProperty(sharedScope, "Children"), expected: 2, "compiled shared-size scope children");

        object sharedGridA = GetField(window, "SharedSizeGridA");
        AssertType(sharedGridA, "System.Windows.Controls.Grid", "compiled shared-size first Grid");
        AssertEqual(220.0, GetProperty(sharedGridA, "Width"), "compiled shared-size first Grid width");
        AssertCollectionCount(GetProperty(sharedGridA, "ColumnDefinitions"), expected: 2, "compiled shared-size first Grid columns");
        AssertCollectionCount(GetProperty(sharedGridA, "Children"), expected: 2, "compiled shared-size first Grid children");

        object sharedGridANameColumn = GetField(window, "SharedSizeGridANameColumn");
        AssertEqual("SharedLabelColumn", GetProperty(sharedGridANameColumn, "SharedSizeGroup"), "compiled shared-size first column group");

        object sharedAHeader = GetField(window, "SharedSizeAHeader");
        AssertType(sharedAHeader, "System.Windows.Controls.TextBlock", "compiled shared-size first header");
        AssertEqual("A", GetProperty(sharedAHeader, "Text"), "compiled shared-size first header text");

        object sharedAValue = GetField(window, "SharedSizeAValue");
        AssertType(sharedAValue, "System.Windows.Controls.TextBlock", "compiled shared-size first value");
        AssertEqual("short value", GetProperty(sharedAValue, "Text"), "compiled shared-size first value text");
        AssertEqual(1, GetDependencyPropertyValue(sharedAValue, sharedGridA.GetType(), "ColumnProperty"), "compiled shared-size first value column");

        object sharedGridB = GetField(window, "SharedSizeGridB");
        AssertType(sharedGridB, "System.Windows.Controls.Grid", "compiled shared-size second Grid");
        AssertEqual(220.0, GetProperty(sharedGridB, "Width"), "compiled shared-size second Grid width");
        AssertCollectionCount(GetProperty(sharedGridB, "ColumnDefinitions"), expected: 2, "compiled shared-size second Grid columns");
        AssertCollectionCount(GetProperty(sharedGridB, "Children"), expected: 2, "compiled shared-size second Grid children");

        object sharedGridBNameColumn = GetField(window, "SharedSizeGridBNameColumn");
        AssertEqual("SharedLabelColumn", GetProperty(sharedGridBNameColumn, "SharedSizeGroup"), "compiled shared-size second column group");

        object sharedBHeader = GetField(window, "SharedSizeBHeader");
        AssertType(sharedBHeader, "System.Windows.Controls.TextBlock", "compiled shared-size second header");
        AssertEqual("shared size label wider", GetProperty(sharedBHeader, "Text"), "compiled shared-size second header text");

        object sharedBValue = GetField(window, "SharedSizeBValue");
        AssertType(sharedBValue, "System.Windows.Controls.TextBlock", "compiled shared-size second value");
        AssertEqual("shared value", GetProperty(sharedBValue, "Text"), "compiled shared-size second value text");
        AssertEqual(1, GetDependencyPropertyValue(sharedBValue, sharedGridB.GetType(), "ColumnProperty"), "compiled shared-size second value column");

        object splitterGrid = GetField(window, "GridSplitterGrid");
        AssertType(splitterGrid, "System.Windows.Controls.Grid", "compiled GridSplitter grid");
        AssertEqual(180.0, GetProperty(splitterGrid, "Width"), "compiled GridSplitter grid width");
        AssertEqual(32.0, GetProperty(splitterGrid, "Height"), "compiled GridSplitter grid height");
        AssertCollectionCount(GetProperty(splitterGrid, "ColumnDefinitions"), expected: 3, "compiled GridSplitter grid columns");
        AssertCollectionCount(GetProperty(splitterGrid, "Children"), expected: 3, "compiled GridSplitter grid children");

        object splitterLeft = GetField(window, "GridSplitterLeftPane");
        AssertType(splitterLeft, "System.Windows.Controls.TextBlock", "compiled GridSplitter left pane");
        AssertEqual("split left", GetProperty(splitterLeft, "Text"), "compiled GridSplitter left text");
        AssertEqual(0, GetDependencyPropertyValue(splitterLeft, splitterGrid.GetType(), "ColumnProperty"), "compiled GridSplitter left column");

        object splitter = GetField(window, "GridSplitterSmoke");
        AssertType(splitter, "System.Windows.Controls.GridSplitter", "compiled GridSplitter");
        AssertEqual(5.0, GetProperty(splitter, "Width"), "compiled GridSplitter width");
        AssertEqual("Stretch", GetProperty(splitter, "HorizontalAlignment").ToString(), "compiled GridSplitter horizontal alignment");
        AssertEqual("Stretch", GetProperty(splitter, "VerticalAlignment").ToString(), "compiled GridSplitter vertical alignment");
        AssertEqual("PreviousAndNext", GetProperty(splitter, "ResizeBehavior").ToString(), "compiled GridSplitter resize behavior");
        AssertEqual("Columns", GetProperty(splitter, "ResizeDirection").ToString(), "compiled GridSplitter resize direction");
        AssertEqual(true, GetProperty(splitter, "ShowsPreview"), "compiled GridSplitter preview setting");
        AssertEqual(3.0, GetProperty(splitter, "DragIncrement"), "compiled GridSplitter drag increment");
        AssertEqual(7.0, GetProperty(splitter, "KeyboardIncrement"), "compiled GridSplitter keyboard increment");
        AssertEqual(1, GetDependencyPropertyValue(splitter, splitterGrid.GetType(), "ColumnProperty"), "compiled GridSplitter column");

        object splitterRight = GetField(window, "GridSplitterRightPane");
        AssertType(splitterRight, "System.Windows.Controls.TextBlock", "compiled GridSplitter right pane");
        AssertEqual("split right", GetProperty(splitterRight, "Text"), "compiled GridSplitter right text");
        AssertEqual(2, GetDependencyPropertyValue(splitterRight, splitterGrid.GetType(), "ColumnProperty"), "compiled GridSplitter right column");
    }

    private static void ValidatePostShowSharedSizeGridLayout(object window)
    {
        object sharedScope = GetField(window, "SharedSizeScopePanel");
        Invoke(sharedScope, "UpdateLayout");
        Invoke(window, "UpdateLayout");

        object firstColumn = GetField(window, "SharedSizeGridANameColumn");
        object secondColumn = GetField(window, "SharedSizeGridBNameColumn");
        double firstWidth = Convert.ToDouble(GetProperty(firstColumn, "ActualWidth"));
        double secondWidth = Convert.ToDouble(GetProperty(secondColumn, "ActualWidth"));

        if (firstWidth <= 0 || secondWidth <= 0)
        {
            throw new InvalidOperationException(
                $"Expected compiled shared-size Grid columns to be measured, got '{firstWidth}' and '{secondWidth}'.");
        }

        AssertClose(firstWidth, secondWidth, 0.001, "compiled shared-size Grid column width");
    }

    private static void ValidatePostShowGridSplitterDrag(object window)
    {
        object splitterGrid = GetField(window, "GridSplitterGrid");
        Invoke(splitterGrid, "UpdateLayout");
        Invoke(window, "UpdateLayout");

        object columnDefinitions = GetProperty(splitterGrid, "ColumnDefinitions");
        object leftColumn = GetCollectionItem(columnDefinitions, 0);
        object rightColumn = GetCollectionItem(columnDefinitions, 2);
        double leftBefore = Convert.ToDouble(GetProperty(leftColumn, "ActualWidth"));
        double rightBefore = Convert.ToDouble(GetProperty(rightColumn, "ActualWidth"));

        if (leftBefore <= 0 || rightBefore <= 0)
        {
            throw new InvalidOperationException(
                $"Expected compiled GridSplitter columns to be measured, got '{leftBefore}' and '{rightBefore}'.");
        }

        object splitter = GetField(window, "GridSplitterSmoke");
        SetProperty(splitter, "ShowsPreview", false);

        Assembly presentationFramework = splitter.GetType().Assembly;
        object started = Create(presentationFramework, "System.Windows.Controls.Primitives.DragStartedEventArgs", 0.0, 0.0);
        object delta = Create(presentationFramework, "System.Windows.Controls.Primitives.DragDeltaEventArgs", 12.0, 0.0);
        object completed = Create(presentationFramework, "System.Windows.Controls.Primitives.DragCompletedEventArgs", 12.0, 0.0, false);

        Invoke(splitter, "RaiseEvent", started);
        Invoke(splitter, "RaiseEvent", delta);
        Invoke(splitter, "RaiseEvent", completed);
        Invoke(splitterGrid, "UpdateLayout");
        Invoke(window, "UpdateLayout");

        double leftAfter = Convert.ToDouble(GetProperty(leftColumn, "ActualWidth"));
        double rightAfter = Convert.ToDouble(GetProperty(rightColumn, "ActualWidth"));
        AssertClose(leftBefore + 12.0, leftAfter, 0.001, "compiled GridSplitter dragged left column width");
        AssertClose(rightBefore - 12.0, rightAfter, 0.001, "compiled GridSplitter dragged right column width");
        SetProperty(splitter, "ShowsPreview", true);
    }

    private static void ValidatePostShowSliderThumbDrag(object window)
    {
        object dataContext = GetProperty(window, "DataContext");
        object slider = GetField(window, "RangeValueSlider");
        object progress = GetField(window, "RangeValueProgress");

        Invoke(slider, "ApplyTemplate");
        Invoke(slider, "UpdateLayout");
        Invoke(window, "UpdateLayout");

        object track = GetProperty(slider, "Track");
        AssertType(track, "System.Windows.Controls.Primitives.Track", "compiled Slider template Track");
        object thumb = GetProperty(track, "Thumb");
        AssertType(thumb, "System.Windows.Controls.Primitives.Thumb", "compiled Slider template Thumb");

        double dragDelta = 14.0;
        SetProperty(slider, "Value", 40.0);
        Invoke(slider, "UpdateLayout");
        double valueDelta = Convert.ToDouble(Invoke(track, "ValueFromDistance", dragDelta, 0.0));
        double expectedValue = Math.Max(
            Convert.ToDouble(GetProperty(slider, "Minimum")),
            Math.Min(Convert.ToDouble(GetProperty(slider, "Maximum")), 40.0 + valueDelta));

        Assembly presentationFramework = slider.GetType().Assembly;
        object started = Create(presentationFramework, "System.Windows.Controls.Primitives.DragStartedEventArgs", 0.0, 0.0);
        object delta = Create(presentationFramework, "System.Windows.Controls.Primitives.DragDeltaEventArgs", dragDelta, 0.0);
        object completed = Create(presentationFramework, "System.Windows.Controls.Primitives.DragCompletedEventArgs", dragDelta, 0.0, false);

        Invoke(thumb, "RaiseEvent", started);
        Invoke(thumb, "RaiseEvent", delta);
        Invoke(thumb, "RaiseEvent", completed);
        Invoke(slider, "UpdateLayout");

        AssertClose(expectedValue, Convert.ToDouble(GetProperty(slider, "Value")), 0.0001, "compiled Slider thumb drag value");
        AssertClose(expectedValue, Convert.ToDouble(GetProperty(dataContext, "RangeValue")), 0.0001, "compiled Slider thumb drag source value");
        AssertClose(expectedValue, Convert.ToDouble(GetProperty(progress, "Value")), 0.0001, "compiled Slider thumb drag progress value");
    }

    private static void ValidateScrollingControls(object window)
    {
        object scrollingPanel = GetField(window, "ScrollingSmokePanel");
        AssertType(scrollingPanel, "System.Windows.Controls.StackPanel", "compiled scrolling panel host");
        AssertCollectionCount(GetProperty(scrollingPanel, "Children"), expected: 2, "compiled scrolling panel host children");

        object scrollViewer = GetField(window, "ScrollViewerSmoke");
        AssertType(scrollViewer, "System.Windows.Controls.ScrollViewer", "compiled ScrollViewer");
        AssertEqual(160.0, GetProperty(scrollViewer, "Width"), "compiled ScrollViewer width");
        AssertEqual(48.0, GetProperty(scrollViewer, "Height"), "compiled ScrollViewer height");
        AssertEqual(false, GetProperty(scrollViewer, "CanContentScroll"), "compiled ScrollViewer CanContentScroll");
        AssertEqual("Disabled", GetProperty(scrollViewer, "HorizontalScrollBarVisibility").ToString(), "compiled ScrollViewer horizontal visibility");
        AssertEqual("Visible", GetProperty(scrollViewer, "VerticalScrollBarVisibility").ToString(), "compiled ScrollViewer vertical visibility");

        object scrollContent = GetField(window, "ScrollViewerContent");
        AssertType(scrollContent, "System.Windows.Controls.StackPanel", "compiled ScrollViewer content");
        AssertSame(scrollContent, GetProperty(scrollViewer, "Content"), "compiled ScrollViewer content object");
        AssertCollectionCount(GetProperty(scrollContent, "Children"), expected: 6, "compiled ScrollViewer content children");
        object firstItem = GetField(window, "ScrollViewerFirstItem");
        AssertType(firstItem, "System.Windows.Controls.TextBlock", "compiled ScrollViewer first item");
        AssertEqual("scroll first", GetProperty(firstItem, "Text"), "compiled ScrollViewer first item text");
        object sixthItem = GetField(window, "ScrollViewerSixthItem");
        AssertType(sixthItem, "System.Windows.Controls.TextBlock", "compiled ScrollViewer sixth item");
        AssertEqual("scroll sixth", GetProperty(sixthItem, "Text"), "compiled ScrollViewer sixth item text");

        object scrollBar = GetField(window, "VerticalScrollBarSmoke");
        AssertType(scrollBar, "System.Windows.Controls.Primitives.ScrollBar", "compiled vertical ScrollBar");
        AssertEqual("Vertical", GetProperty(scrollBar, "Orientation").ToString(), "compiled ScrollBar orientation");
        AssertEqual(0.0, GetProperty(scrollBar, "Minimum"), "compiled ScrollBar minimum");
        AssertEqual(10.0, GetProperty(scrollBar, "Maximum"), "compiled ScrollBar maximum");
        AssertEqual(4.0, GetProperty(scrollBar, "Value"), "compiled ScrollBar initial value");
        AssertEqual(1.0, GetProperty(scrollBar, "SmallChange"), "compiled ScrollBar small change");
        AssertEqual(3.0, GetProperty(scrollBar, "LargeChange"), "compiled ScrollBar large change");
        AssertEqual(2.0, GetProperty(scrollBar, "ViewportSize"), "compiled ScrollBar viewport size");

        SetProperty(scrollBar, "Value", 7.0);
        AssertEqual(7.0, GetProperty(scrollBar, "Value"), "compiled ScrollBar updated value");

        SetProperty(scrollBar, "Value", 4.0);
        EventInfo scrollEvent = scrollBar.GetType().GetEvent(
            "Scroll",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(scrollBar.GetType().FullName, "Scroll");
        var scrollRecorder = new ScrollEventRecorder();
        Delegate scrollHandler = CreateScrollEventHandler(scrollEvent, scrollRecorder);
        scrollEvent.AddEventHandler(scrollBar, scrollHandler);
        try
        {
            ExecuteScrollBarCommand(scrollBar, "LineDownCommand", 5.0, "SmallIncrement", scrollRecorder, "compiled ScrollBar LineDown command");
            ExecuteScrollBarCommand(scrollBar, "LineUpCommand", 4.0, "SmallDecrement", scrollRecorder, "compiled ScrollBar LineUp command");
            ExecuteScrollBarCommand(scrollBar, "PageDownCommand", 7.0, "LargeIncrement", scrollRecorder, "compiled ScrollBar PageDown command");
            ExecuteScrollBarCommand(scrollBar, "PageUpCommand", 4.0, "LargeDecrement", scrollRecorder, "compiled ScrollBar PageUp command");
            ExecuteScrollBarCommand(scrollBar, "ScrollToBottomCommand", 10.0, "Last", scrollRecorder, "compiled ScrollBar ScrollToBottom command");
            ExecuteScrollBarCommand(scrollBar, "ScrollToTopCommand", 0.0, "First", scrollRecorder, "compiled ScrollBar ScrollToTop command");
        }
        finally
        {
            scrollEvent.RemoveEventHandler(scrollBar, scrollHandler);
        }
    }

    private static void ExecuteScrollBarCommand(
        object scrollBar,
        string commandFieldName,
        double expectedValue,
        string expectedScrollEventType,
        ScrollEventRecorder scrollRecorder,
        string description)
    {
        object command = GetStaticField(scrollBar.GetType(), commandFieldName);
        AssertEqual(true, InvokeTwoArgumentCommand(command, "CanExecute", null, scrollBar), $"{description} CanExecute");
        InvokeTwoArgumentCommand(command, "Execute", null, scrollBar);
        AssertEqual(expectedValue, GetProperty(scrollBar, "Value"), $"{description} value");
        scrollRecorder.AssertLast(expectedScrollEventType, expectedValue, $"{description} ScrollEvent");
    }

    private static Delegate CreateScrollEventHandler(EventInfo scrollEvent, ScrollEventRecorder recorder)
    {
        Type handlerType = scrollEvent.EventHandlerType
            ?? throw new InvalidOperationException($"Expected '{scrollEvent.Name}' to expose a handler type.");
        MethodInfo invoke = handlerType.GetMethod("Invoke")
            ?? throw new MissingMethodException(handlerType.FullName, "Invoke");
        ParameterInfo[] parameters = invoke.GetParameters();
        if (parameters.Length != 2)
        {
            throw new InvalidOperationException($"Expected '{handlerType.FullName}' to be a two-argument event handler.");
        }

        MethodInfo recordMethod = typeof(ScrollEventRecorder).GetMethod(
            nameof(ScrollEventRecorder.Record),
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMethodException(typeof(ScrollEventRecorder).FullName, nameof(ScrollEventRecorder.Record));
        var senderParameter = System.Linq.Expressions.Expression.Parameter(parameters[0].ParameterType, "sender");
        var argsParameter = System.Linq.Expressions.Expression.Parameter(parameters[1].ParameterType, "args");
        var call = System.Linq.Expressions.Expression.Call(
            System.Linq.Expressions.Expression.Constant(recorder),
            recordMethod,
            System.Linq.Expressions.Expression.Convert(senderParameter, typeof(object)),
            System.Linq.Expressions.Expression.Convert(argsParameter, typeof(EventArgs)));

        return System.Linq.Expressions.Expression.Lambda(handlerType, call, senderParameter, argsParameter).Compile();
    }

    private static void ValidatePostShowScrollingControls(object window)
    {
        object scrollViewer = GetField(window, "ScrollViewerSmoke");
        Invoke(scrollViewer, "UpdateLayout");
        double scrollableHeight = Convert.ToDouble(GetProperty(scrollViewer, "ScrollableHeight"));
        if (scrollableHeight <= 0)
        {
            throw new InvalidOperationException($"Expected compiled ScrollViewer scrollable height to be positive, got '{scrollableHeight}'.");
        }

        double targetOffset = Math.Min(12.0, scrollableHeight);
        Invoke(scrollViewer, "ScrollToVerticalOffset", targetOffset);
        Invoke(window, "UpdateLayout");

        AssertEqual(targetOffset, GetProperty(scrollViewer, "VerticalOffset"), "compiled ScrollViewer vertical offset");
    }

    private static void ValidateDateSelectionControls(object window)
    {
        object datePanel = GetField(window, "DateSelectionSmokePanel");
        AssertType(datePanel, "System.Windows.Controls.StackPanel", "compiled date-selection panel host");
        AssertCollectionCount(GetProperty(datePanel, "Children"), expected: 2, "compiled date-selection panel children");

        object calendar = GetField(window, "CalendarSmoke");
        AssertType(calendar, "System.Windows.Controls.Calendar", "compiled Calendar");
        AssertEqual("Month", GetProperty(calendar, "DisplayMode").ToString(), "compiled Calendar display mode");
        AssertEqual("SingleDate", GetProperty(calendar, "SelectionMode").ToString(), "compiled Calendar selection mode");
        AssertEqual("Monday", GetProperty(calendar, "FirstDayOfWeek").ToString(), "compiled Calendar first day of week");
        AssertEqual(false, GetProperty(calendar, "IsTodayHighlighted"), "compiled Calendar today highlight");
        AssertDate(GetProperty(calendar, "DisplayDateStart"), 2026, 1, 1, "compiled Calendar display start");
        AssertDate(GetProperty(calendar, "DisplayDateEnd"), 2026, 12, 31, "compiled Calendar display end");
        AssertDate(GetProperty(calendar, "DisplayDate"), 2026, 6, 1, "compiled Calendar display date");
        AssertDate(GetProperty(calendar, "SelectedDate"), 2026, 6, 17, "compiled Calendar selected date");
        object selectedDates = GetProperty(calendar, "SelectedDates");
        AssertCollectionCount(selectedDates, expected: 1, "compiled Calendar selected dates");
        AssertDate(GetCollectionItem(selectedDates, 0), 2026, 6, 17, "compiled Calendar selected date collection item");

        SetProperty(calendar, "SelectedDate", new DateTime(2026, 6, 21));
        AssertDate(GetProperty(calendar, "SelectedDate"), 2026, 6, 21, "compiled Calendar updated selected date");
        AssertCollectionCount(selectedDates, expected: 1, "compiled Calendar updated selected dates");
        AssertDate(GetCollectionItem(selectedDates, 0), 2026, 6, 21, "compiled Calendar updated selected date collection item");

        object datePicker = GetField(window, "DatePickerSmoke");
        AssertType(datePicker, "System.Windows.Controls.DatePicker", "compiled DatePicker");
        AssertEqual(160.0, GetProperty(datePicker, "Width"), "compiled DatePicker width");
        AssertEqual("Monday", GetProperty(datePicker, "FirstDayOfWeek").ToString(), "compiled DatePicker first day of week");
        AssertEqual(false, GetProperty(datePicker, "IsTodayHighlighted"), "compiled DatePicker today highlight");
        AssertEqual("Short", GetProperty(datePicker, "SelectedDateFormat").ToString(), "compiled DatePicker selected date format");
        AssertEqual(false, GetProperty(datePicker, "IsDropDownOpen"), "compiled DatePicker initial drop-down state");
        AssertDate(GetProperty(datePicker, "DisplayDateStart"), 2026, 1, 1, "compiled DatePicker display start");
        AssertDate(GetProperty(datePicker, "DisplayDateEnd"), 2026, 12, 31, "compiled DatePicker display end");
        AssertDate(GetProperty(datePicker, "SelectedDate"), 2026, 6, 18, "compiled DatePicker selected date");

        SetProperty(datePicker, "SelectedDate", new DateTime(2026, 7, 4));
        AssertDate(GetProperty(datePicker, "SelectedDate"), 2026, 7, 4, "compiled DatePicker updated selected date");
    }

    private static void ValidateImplicitMergedStyle(object window, object application)
    {
        object implicitStyleCheckBox = GetField(window, "ImplicitStyleCheckBox");
        AssertType(implicitStyleCheckBox, "System.Windows.Controls.CheckBox", "compiled implicit-style CheckBox");
        AssertEqual(true, GetProperty(implicitStyleCheckBox, "IsChecked"), "compiled implicit-style CheckBox checked state");

        object expectedStyle = Invoke(application, "TryFindResource", implicitStyleCheckBox.GetType());
        AssertType(expectedStyle, "System.Windows.Style", "merged implicit CheckBox style");
        AssertSame(expectedStyle, GetProperty(implicitStyleCheckBox, "Style"), "compiled implicit CheckBox style");
        AssertEqual("implicit merged style", GetProperty(implicitStyleCheckBox, "Tag"), "compiled implicit CheckBox style tag");

        object expectedMargin = Invoke(application, "TryFindResource", "MergedBlockMargin");
        object actualMargin = GetProperty(implicitStyleCheckBox, "Margin");
        AssertEqual(GetProperty(expectedMargin, "Top"), GetProperty(actualMargin, "Top"), "compiled implicit CheckBox style margin top");
    }

    private static void ValidateToggleChoiceControls(object window)
    {
        object panel = GetField(window, "ToggleChoicePanel");
        AssertType(panel, "System.Windows.Controls.StackPanel", "compiled toggle/radio panel");
        AssertCollectionCount(GetProperty(panel, "Children"), expected: 3, "compiled toggle/radio panel children");

        object checkBox = GetField(window, "ToggleChoiceCheckBox");
        AssertType(checkBox, "System.Windows.Controls.CheckBox", "compiled ToggleButton CheckBox");
        AssertEqual("toggle choice", GetProperty(checkBox, "Content"), "compiled ToggleButton CheckBox content");
        AssertEqual(false, GetProperty(checkBox, "IsChecked"), "compiled ToggleButton CheckBox initial checked state");
        AssertEqual(0, GetProperty(window, "ToggleChoiceCheckedCount"), "compiled ToggleButton initial Checked count");
        AssertEqual(0, GetProperty(window, "ToggleChoiceUncheckedCount"), "compiled ToggleButton initial Unchecked count");

        Invoke(checkBox, "OnClick");
        AssertEqual(true, GetProperty(checkBox, "IsChecked"), "compiled ToggleButton CheckBox checked state");
        AssertEqual(1, GetProperty(window, "ToggleChoiceCheckedCount"), "compiled ToggleButton Checked count");
        AssertEqual("ToggleChoiceCheckBox", GetProperty(window, "LastToggleChoiceCheckedSenderName"), "compiled ToggleButton Checked sender");
        AssertEqual("Checked", GetProperty(window, "LastToggleChoiceCheckedRoutedEventName"), "compiled ToggleButton Checked routed event");

        Invoke(checkBox, "OnClick");
        AssertEqual(false, GetProperty(checkBox, "IsChecked"), "compiled ToggleButton CheckBox unchecked state");
        AssertEqual(1, GetProperty(window, "ToggleChoiceUncheckedCount"), "compiled ToggleButton Unchecked count");
        AssertEqual("ToggleChoiceCheckBox", GetProperty(window, "LastToggleChoiceUncheckedSenderName"), "compiled ToggleButton Unchecked sender");
        AssertEqual("Unchecked", GetProperty(window, "LastToggleChoiceUncheckedRoutedEventName"), "compiled ToggleButton Unchecked routed event");

        object alpha = GetField(window, "RadioChoiceAlpha");
        object beta = GetField(window, "RadioChoiceBeta");
        AssertType(alpha, "System.Windows.Controls.RadioButton", "compiled alpha RadioButton");
        AssertType(beta, "System.Windows.Controls.RadioButton", "compiled beta RadioButton");
        AssertEqual("choice alpha", GetProperty(alpha, "Content"), "compiled alpha RadioButton content");
        AssertEqual("choice beta", GetProperty(beta, "Content"), "compiled beta RadioButton content");
        AssertEqual("SmokeChoiceGroup", GetProperty(alpha, "GroupName"), "compiled alpha RadioButton group");
        AssertEqual("SmokeChoiceGroup", GetProperty(beta, "GroupName"), "compiled beta RadioButton group");
        AssertEqual(false, GetProperty(alpha, "IsChecked"), "compiled alpha RadioButton initial checked state");
        AssertEqual(false, GetProperty(beta, "IsChecked"), "compiled beta RadioButton initial checked state");
        AssertEqual(0, GetProperty(window, "ChoiceRadioCheckedCount"), "compiled RadioButton initial Checked count");
        AssertEqual(0, GetProperty(window, "ChoiceRadioUncheckedCount"), "compiled RadioButton initial Unchecked count");

        Invoke(alpha, "OnClick");
        AssertEqual(true, GetProperty(alpha, "IsChecked"), "compiled alpha RadioButton checked state");
        AssertEqual(false, GetProperty(beta, "IsChecked"), "compiled beta RadioButton unchecked after alpha click");
        AssertEqual(1, GetProperty(window, "ChoiceRadioCheckedCount"), "compiled RadioButton alpha Checked count");
        AssertEqual("RadioChoiceAlpha", GetProperty(window, "LastChoiceRadioCheckedSenderName"), "compiled RadioButton alpha Checked sender");
        AssertEqual("Checked", GetProperty(window, "LastChoiceRadioCheckedRoutedEventName"), "compiled RadioButton alpha Checked routed event");

        Invoke(beta, "OnClick");
        AssertEqual(false, GetProperty(alpha, "IsChecked"), "compiled alpha RadioButton unchecked by group manager");
        AssertEqual(true, GetProperty(beta, "IsChecked"), "compiled beta RadioButton checked state");
        AssertEqual(2, GetProperty(window, "ChoiceRadioCheckedCount"), "compiled RadioButton beta Checked count");
        AssertEqual(1, GetProperty(window, "ChoiceRadioUncheckedCount"), "compiled RadioButton alpha Unchecked count");
        AssertEqual("RadioChoiceBeta", GetProperty(window, "LastChoiceRadioCheckedSenderName"), "compiled RadioButton beta Checked sender");
        AssertEqual("RadioChoiceAlpha", GetProperty(window, "LastChoiceRadioUncheckedSenderName"), "compiled RadioButton alpha Unchecked sender");
        AssertEqual("Unchecked", GetProperty(window, "LastChoiceRadioUncheckedRoutedEventName"), "compiled RadioButton alpha Unchecked routed event");
    }

    private static void ValidateXamlEventHandler(object window)
    {
        object eventButton = GetField(window, "EventButton");
        AssertType(eventButton, "System.Windows.Controls.Button", "compiled event Button");
        AssertEqual("run xaml event", GetProperty(eventButton, "Content"), "compiled event Button content");
        AssertEqual(0, GetProperty(window, "XamlClickCount"), "XAML event initial click count");

        Invoke(eventButton, "OnClick");

        AssertEqual(1, GetProperty(window, "XamlClickCount"), "compiled XAML Click handler count");
        AssertEqual("EventButton", GetProperty(window, "LastXamlClickSenderName"), "compiled XAML Click sender name");
        AssertEqual("Click", GetProperty(window, "LastXamlClickRoutedEventName"), "compiled XAML Click routed event name");
    }

    private static void ValidateRepeatButton(object window)
    {
        object repeatButton = GetField(window, "RepeatActionButton");
        AssertType(repeatButton, "System.Windows.Controls.Primitives.RepeatButton", "compiled RepeatButton");
        AssertEqual("repeat action", GetProperty(repeatButton, "Content"), "compiled RepeatButton content");
        AssertEqual(250, GetProperty(repeatButton, "Delay"), "compiled RepeatButton delay");
        AssertEqual(75, GetProperty(repeatButton, "Interval"), "compiled RepeatButton interval");
        AssertEqual(0, GetProperty(window, "RepeatButtonClickCount"), "compiled RepeatButton initial click count");

        Invoke(repeatButton, "OnClick");
        Invoke(repeatButton, "OnClick");

        AssertEqual(2, GetProperty(window, "RepeatButtonClickCount"), "compiled RepeatButton Click handler count");
        AssertEqual("RepeatActionButton", GetProperty(window, "LastRepeatButtonClickSenderName"), "compiled RepeatButton Click sender name");
        AssertEqual("Click", GetProperty(window, "LastRepeatButtonClickRoutedEventName"), "compiled RepeatButton Click routed event name");
    }

    private static void ValidateThumbDragManager(object window)
    {
        object rootPanel = GetField(window, "SmokeRootPanel");
        AssertType(rootPanel, "System.Windows.Controls.StackPanel", "compiled root StackPanel");

        object thumb = GetField(window, "DragManagerThumb");
        AssertType(thumb, "System.Windows.Controls.Primitives.Thumb", "compiled Thumb drag manager");
        AssertEqual(24.0, GetProperty(thumb, "Width"), "compiled Thumb width");
        AssertEqual(18.0, GetProperty(thumb, "Height"), "compiled Thumb height");
        AssertEqual("drag manager thumb", GetProperty(thumb, "Tag"), "compiled Thumb tag");
        AssertEqual(false, GetProperty(thumb, "Focusable"), "compiled Thumb focusable metadata");
        AssertEqual(false, GetProperty(thumb, "IsDragging"), "compiled Thumb initial dragging state");
        AssertEqual(0, GetProperty(window, "ThumbDragStartedCount"), "compiled Thumb initial DragStarted count");
        AssertEqual(0, GetProperty(window, "ThumbDragDeltaCount"), "compiled Thumb initial DragDelta count");
        AssertEqual(0, GetProperty(window, "ThumbDragCompletedCount"), "compiled Thumb initial DragCompleted count");
        AssertEqual(0, GetProperty(window, "BubbledThumbDragDeltaCount"), "compiled Thumb initial bubbled DragDelta count");

        Assembly presentationFramework = thumb.GetType().Assembly;
        object started = Create(presentationFramework, "System.Windows.Controls.Primitives.DragStartedEventArgs", 2.5, 3.5);
        object delta = Create(presentationFramework, "System.Windows.Controls.Primitives.DragDeltaEventArgs", 4.0, 6.0);
        object completed = Create(presentationFramework, "System.Windows.Controls.Primitives.DragCompletedEventArgs", 8.0, 10.0, true);

        Invoke(thumb, "RaiseEvent", started);
        AssertEqual(1, GetProperty(window, "ThumbDragStartedCount"), "compiled Thumb DragStarted handler count");
        AssertEqual("DragManagerThumb", GetProperty(window, "LastThumbDragStartedSenderName"), "compiled Thumb DragStarted sender");
        AssertEqual("DragStarted", GetProperty(window, "LastThumbDragStartedRoutedEventName"), "compiled Thumb DragStarted routed event");
        AssertEqual(2.5, GetProperty(window, "LastThumbDragStartedHorizontalOffset"), "compiled Thumb DragStarted horizontal offset");
        AssertEqual(3.5, GetProperty(window, "LastThumbDragStartedVerticalOffset"), "compiled Thumb DragStarted vertical offset");

        Invoke(thumb, "RaiseEvent", delta);
        AssertEqual(1, GetProperty(window, "ThumbDragDeltaCount"), "compiled Thumb DragDelta handler count");
        AssertEqual("DragManagerThumb", GetProperty(window, "LastThumbDragDeltaSenderName"), "compiled Thumb DragDelta sender");
        AssertEqual("DragDelta", GetProperty(window, "LastThumbDragDeltaRoutedEventName"), "compiled Thumb DragDelta routed event");
        AssertEqual(4.0, GetProperty(window, "LastThumbDragDeltaHorizontalChange"), "compiled Thumb DragDelta horizontal change");
        AssertEqual(6.0, GetProperty(window, "LastThumbDragDeltaVerticalChange"), "compiled Thumb DragDelta vertical change");
        AssertEqual(1, GetProperty(window, "BubbledThumbDragDeltaCount"), "compiled Thumb bubbled DragDelta handler count");
        AssertEqual("SmokeRootPanel", GetProperty(window, "LastBubbledThumbDragDeltaSenderName"), "compiled Thumb bubbled DragDelta sender");
        AssertEqual("DragManagerThumb", GetProperty(window, "LastBubbledThumbDragDeltaOriginalSourceName"), "compiled Thumb bubbled DragDelta source");
        AssertEqual("DragDelta", GetProperty(window, "LastBubbledThumbDragDeltaRoutedEventName"), "compiled Thumb bubbled DragDelta routed event");
        AssertEqual(4.0, GetProperty(window, "LastBubbledThumbDragDeltaHorizontalChange"), "compiled Thumb bubbled DragDelta horizontal change");
        AssertEqual(6.0, GetProperty(window, "LastBubbledThumbDragDeltaVerticalChange"), "compiled Thumb bubbled DragDelta vertical change");

        Invoke(thumb, "RaiseEvent", completed);
        AssertEqual(1, GetProperty(window, "ThumbDragCompletedCount"), "compiled Thumb DragCompleted handler count");
        AssertEqual("DragManagerThumb", GetProperty(window, "LastThumbDragCompletedSenderName"), "compiled Thumb DragCompleted sender");
        AssertEqual("DragCompleted", GetProperty(window, "LastThumbDragCompletedRoutedEventName"), "compiled Thumb DragCompleted routed event");
        AssertEqual(8.0, GetProperty(window, "LastThumbDragCompletedHorizontalChange"), "compiled Thumb DragCompleted horizontal change");
        AssertEqual(10.0, GetProperty(window, "LastThumbDragCompletedVerticalChange"), "compiled Thumb DragCompleted vertical change");
        AssertEqual(true, GetProperty(window, "LastThumbDragCompletedCanceled"), "compiled Thumb DragCompleted canceled state");
    }

    private static void ValidateStyleEventSetter(object window)
    {
        object styledEventButton = GetField(window, "StyledEventButton");
        AssertType(styledEventButton, "System.Windows.Controls.Button", "compiled EventSetter Button");
        AssertEqual("run style event", GetProperty(styledEventButton, "Content"), "compiled EventSetter Button content");
        AssertEqual("event setter style", GetProperty(styledEventButton, "Tag"), "compiled EventSetter style setter");

        object style = GetProperty(styledEventButton, "Style");
        AssertType(style, "System.Windows.Style", "compiled EventSetter style");
        object eventSetters = GetProperty(style, "Setters");
        AssertAtLeast(2, GetProperty(eventSetters, "Count"), "compiled EventSetter style setters");
        AssertEqual(0, GetProperty(window, "StyledClickCount"), "compiled EventSetter initial click count");

        Invoke(styledEventButton, "OnClick");

        AssertEqual(1, GetProperty(window, "StyledClickCount"), "compiled EventSetter Click handler count");
        AssertEqual("StyledEventButton", GetProperty(window, "LastStyledClickSenderName"), "compiled EventSetter Click sender name");
        AssertEqual("Click", GetProperty(window, "LastStyledClickRoutedEventName"), "compiled EventSetter Click routed event name");
    }

    private static void ValidateStyleAndDataTrigger(object window, object application)
    {
        object resources = GetProperty(application, "Resources");
        object accentBrush = GetDictionaryValue(resources, "AccentBrush");
        object replacementAccentBrush = GetDictionaryValue(resources, "ReplacementAccentBrush");
        object expectedStyle = GetDictionaryValue(resources, "TriggeredButtonStyle");
        object expectedPropertyStyle = GetDictionaryValue(resources, "PropertyTriggeredButtonStyle");
        object expectedMultiPropertyStyle = GetDictionaryValue(resources, "MultiPropertyTriggeredButtonStyle");
        object expectedTriggerActionStyle = GetDictionaryValue(resources, "TriggerActionButtonStyle");
        object expectedDataTriggerActionStyle = GetDictionaryValue(resources, "DataTriggerActionButtonStyle");
        object expectedMultiDataTriggerActionStyle = GetDictionaryValue(resources, "MultiDataTriggerActionButtonStyle");
        object expectedMultiTriggerActionStyle = GetDictionaryValue(resources, "MultiTriggerActionButtonStyle");
        object expectedMultiStyle = GetDictionaryValue(resources, "MultiTriggeredButtonStyle");
        object dataContext = GetProperty(window, "DataContext");

        object triggeredButton = GetField(window, "TriggeredButton");
        AssertType(triggeredButton, "System.Windows.Controls.Button", "compiled triggered Button");
        AssertSame(expectedStyle, GetProperty(triggeredButton, "Style"), "compiled Button triggered style");
        AssertEqual("style trigger target", GetProperty(triggeredButton, "Content"), "compiled Button trigger content binding");
        AssertEqual(false, GetProperty(dataContext, "IsWarning"), "style trigger initial view-model state");
        AssertEqual(false, GetProperty(dataContext, "IsCritical"), "multi trigger initial critical view-model state");
        AssertEqual("trigger inactive", GetProperty(triggeredButton, "Tag"), "compiled DataTrigger inactive value");
        AssertSame(accentBrush, GetProperty(triggeredButton, "Background"), "compiled DataTrigger inactive brush");

        object propertyTriggeredButton = GetField(window, "PropertyTriggeredButton");
        AssertType(propertyTriggeredButton, "System.Windows.Controls.Button", "compiled property-triggered Button");
        AssertSame(expectedPropertyStyle, GetProperty(propertyTriggeredButton, "Style"), "compiled Button property Trigger style");
        AssertEqual("property trigger target", GetProperty(propertyTriggeredButton, "Content"), "compiled Button property Trigger content");
        AssertEqual("property trigger inactive", GetProperty(propertyTriggeredButton, "Tag"), "compiled property Trigger inactive value");
        AssertSame(accentBrush, GetProperty(propertyTriggeredButton, "Background"), "compiled property Trigger inactive brush");
        object propertyTriggers = GetProperty(expectedPropertyStyle, "Triggers");
        AssertCollectionCount(propertyTriggers, expected: 1, "compiled property Trigger count");
        object propertyTrigger = GetCollectionItem(propertyTriggers, 0);
        AssertType(propertyTrigger, "System.Windows.Trigger", "compiled property Trigger metadata");
        AssertEqual("IsEnabled", GetProperty(GetProperty(propertyTrigger, "Property"), "Name"), "compiled property Trigger source property");
        AssertEqual(false, GetProperty(propertyTrigger, "Value"), "compiled property Trigger value");
        object propertyTriggerSetters = GetProperty(propertyTrigger, "Setters");
        AssertCollectionCount(propertyTriggerSetters, expected: 2, "compiled property Trigger setters");
        SetProperty(propertyTriggeredButton, "IsEnabled", false);
        AssertEqual("property trigger active", GetProperty(propertyTriggeredButton, "Tag"), "compiled property Trigger active value");
        AssertSame(replacementAccentBrush, GetProperty(propertyTriggeredButton, "Background"), "compiled property Trigger active brush");
        SetProperty(propertyTriggeredButton, "IsEnabled", true);
        AssertEqual("property trigger inactive", GetProperty(propertyTriggeredButton, "Tag"), "compiled property Trigger restored value");
        AssertSame(accentBrush, GetProperty(propertyTriggeredButton, "Background"), "compiled property Trigger restored brush");

        object multiPropertyTriggeredButton = GetField(window, "MultiPropertyTriggeredButton");
        AssertType(multiPropertyTriggeredButton, "System.Windows.Controls.Button", "compiled multi-property-triggered Button");
        AssertSame(expectedMultiPropertyStyle, GetProperty(multiPropertyTriggeredButton, "Style"), "compiled Button MultiTrigger style");
        AssertEqual("multi property trigger target", GetProperty(multiPropertyTriggeredButton, "Content"), "compiled Button MultiTrigger content");
        AssertEqual(true, GetProperty(multiPropertyTriggeredButton, "IsEnabled"), "compiled MultiTrigger enabled condition");
        AssertEqual(true, GetProperty(multiPropertyTriggeredButton, "IsDefault"), "compiled MultiTrigger default condition");
        AssertEqual("multi property trigger active", GetProperty(multiPropertyTriggeredButton, "Tag"), "compiled MultiTrigger active value");
        AssertSame(replacementAccentBrush, GetProperty(multiPropertyTriggeredButton, "Background"), "compiled MultiTrigger active brush");
        object multiPropertyTriggers = GetProperty(expectedMultiPropertyStyle, "Triggers");
        AssertCollectionCount(multiPropertyTriggers, expected: 1, "compiled MultiTrigger count");
        object multiPropertyTrigger = GetCollectionItem(multiPropertyTriggers, 0);
        AssertType(multiPropertyTrigger, "System.Windows.MultiTrigger", "compiled MultiTrigger metadata");
        object multiPropertyConditions = GetProperty(multiPropertyTrigger, "Conditions");
        AssertCollectionCount(multiPropertyConditions, expected: 2, "compiled MultiTrigger condition count");
        object enabledCondition = GetCollectionItem(multiPropertyConditions, 0);
        object defaultCondition = GetCollectionItem(multiPropertyConditions, 1);
        AssertEqual("IsEnabled", GetProperty(GetProperty(enabledCondition, "Property"), "Name"), "compiled MultiTrigger first condition property");
        AssertEqual(true, GetProperty(enabledCondition, "Value"), "compiled MultiTrigger first condition value");
        AssertEqual("IsDefault", GetProperty(GetProperty(defaultCondition, "Property"), "Name"), "compiled MultiTrigger second condition property");
        AssertEqual(true, GetProperty(defaultCondition, "Value"), "compiled MultiTrigger second condition value");
        object multiPropertyTriggerSetters = GetProperty(multiPropertyTrigger, "Setters");
        AssertCollectionCount(multiPropertyTriggerSetters, expected: 2, "compiled MultiTrigger setters");
        SetProperty(multiPropertyTriggeredButton, "IsDefault", false);
        AssertEqual("multi property trigger inactive", GetProperty(multiPropertyTriggeredButton, "Tag"), "compiled MultiTrigger default false value");
        AssertSame(accentBrush, GetProperty(multiPropertyTriggeredButton, "Background"), "compiled MultiTrigger default false brush");
        SetProperty(multiPropertyTriggeredButton, "IsDefault", true);
        AssertEqual("multi property trigger active", GetProperty(multiPropertyTriggeredButton, "Tag"), "compiled MultiTrigger reactivated value");
        AssertSame(replacementAccentBrush, GetProperty(multiPropertyTriggeredButton, "Background"), "compiled MultiTrigger reactivated brush");
        SetProperty(multiPropertyTriggeredButton, "IsEnabled", false);
        AssertEqual("multi property trigger inactive", GetProperty(multiPropertyTriggeredButton, "Tag"), "compiled MultiTrigger disabled value");
        AssertSame(accentBrush, GetProperty(multiPropertyTriggeredButton, "Background"), "compiled MultiTrigger disabled brush");
        SetProperty(multiPropertyTriggeredButton, "IsEnabled", true);
        AssertEqual("multi property trigger active", GetProperty(multiPropertyTriggeredButton, "Tag"), "compiled MultiTrigger enabled restored value");
        AssertSame(replacementAccentBrush, GetProperty(multiPropertyTriggeredButton, "Background"), "compiled MultiTrigger enabled restored brush");

        object triggerActionButton = GetField(window, "TriggerActionButton");
        AssertType(triggerActionButton, "System.Windows.Controls.Button", "compiled trigger-action Button");
        AssertSame(expectedTriggerActionStyle, GetProperty(triggerActionButton, "Style"), "compiled Button Trigger action style");
        AssertEqual("trigger action target", GetProperty(triggerActionButton, "Content"), "compiled Button Trigger action content");
        object triggerActionTriggers = GetProperty(expectedTriggerActionStyle, "Triggers");
        AssertCollectionCount(triggerActionTriggers, expected: 1, "compiled Trigger action trigger count");
        object triggerActionTrigger = GetCollectionItem(triggerActionTriggers, 0);
        AssertType(triggerActionTrigger, "System.Windows.Trigger", "compiled Trigger action metadata");
        AssertEqual("IsEnabled", GetProperty(GetProperty(triggerActionTrigger, "Property"), "Name"), "compiled Trigger action source property");
        AssertEqual(false, GetProperty(triggerActionTrigger, "Value"), "compiled Trigger action value");
        AssertCollectionCount(GetProperty(triggerActionTrigger, "Setters"), expected: 0, "compiled Trigger action setter count");
        object enterActions = GetProperty(triggerActionTrigger, "EnterActions");
        AssertCollectionCount(enterActions, expected: 1, "compiled Trigger EnterActions count");
        object enterBeginStoryboard = GetCollectionItem(enterActions, 0);
        AssertType(enterBeginStoryboard, "System.Windows.Media.Animation.BeginStoryboard", "compiled Trigger EnterActions BeginStoryboard");
        object enterStoryboard = GetProperty(enterBeginStoryboard, "Storyboard");
        AssertType(enterStoryboard, "System.Windows.Media.Animation.Storyboard", "compiled Trigger EnterActions Storyboard");
        object enterStoryboardChildren = GetProperty(enterStoryboard, "Children");
        AssertCollectionCount(enterStoryboardChildren, expected: 1, "compiled Trigger EnterActions Storyboard children");
        object enterDoubleAnimation = GetCollectionItem(enterStoryboardChildren, 0);
        AssertType(enterDoubleAnimation, "System.Windows.Media.Animation.DoubleAnimation", "compiled Trigger EnterActions DoubleAnimation");
        AssertEqual(0.41, GetProperty(enterDoubleAnimation, "To"), "compiled Trigger EnterActions target value");
        AssertEqual("00:00:00", GetProperty(enterDoubleAnimation, "Duration").ToString(), "compiled Trigger EnterActions duration");
        AssertEqual("HoldEnd", GetProperty(enterDoubleAnimation, "FillBehavior").ToString(), "compiled Trigger EnterActions fill behavior");
        object exitActions = GetProperty(triggerActionTrigger, "ExitActions");
        AssertCollectionCount(exitActions, expected: 1, "compiled Trigger ExitActions count");
        object exitBeginStoryboard = GetCollectionItem(exitActions, 0);
        AssertType(exitBeginStoryboard, "System.Windows.Media.Animation.BeginStoryboard", "compiled Trigger ExitActions BeginStoryboard");
        object exitStoryboard = GetProperty(exitBeginStoryboard, "Storyboard");
        AssertType(exitStoryboard, "System.Windows.Media.Animation.Storyboard", "compiled Trigger ExitActions Storyboard");
        object exitStoryboardChildren = GetProperty(exitStoryboard, "Children");
        AssertCollectionCount(exitStoryboardChildren, expected: 1, "compiled Trigger ExitActions Storyboard children");
        object exitDoubleAnimation = GetCollectionItem(exitStoryboardChildren, 0);
        AssertType(exitDoubleAnimation, "System.Windows.Media.Animation.DoubleAnimation", "compiled Trigger ExitActions DoubleAnimation");
        AssertEqual(1.0, GetProperty(exitDoubleAnimation, "To"), "compiled Trigger ExitActions target value");
        AssertEqual("00:00:00", GetProperty(exitDoubleAnimation, "Duration").ToString(), "compiled Trigger ExitActions duration");
        AssertEqual("HoldEnd", GetProperty(exitDoubleAnimation, "FillBehavior").ToString(), "compiled Trigger ExitActions fill behavior");

        object dataTriggerActionButton = GetField(window, "DataTriggerActionButton");
        AssertType(dataTriggerActionButton, "System.Windows.Controls.Button", "compiled data-trigger-action Button");
        AssertSame(expectedDataTriggerActionStyle, GetProperty(dataTriggerActionButton, "Style"), "compiled Button DataTrigger action style");
        AssertEqual("data trigger action target", GetProperty(dataTriggerActionButton, "Content"), "compiled Button DataTrigger action content");
        object dataTriggerActionTriggers = GetProperty(expectedDataTriggerActionStyle, "Triggers");
        AssertCollectionCount(dataTriggerActionTriggers, expected: 1, "compiled DataTrigger action trigger count");
        object dataTriggerActionTrigger = GetCollectionItem(dataTriggerActionTriggers, 0);
        AssertType(dataTriggerActionTrigger, "System.Windows.DataTrigger", "compiled DataTrigger action metadata");
        AssertBindingObjectPath(GetProperty(dataTriggerActionTrigger, "Binding"), "IsTriggerActionActive", "compiled DataTrigger action binding path");
        AssertEqual("True", GetProperty(dataTriggerActionTrigger, "Value").ToString(), "compiled DataTrigger action value");
        AssertCollectionCount(GetProperty(dataTriggerActionTrigger, "Setters"), expected: 0, "compiled DataTrigger action setter count");
        object dataEnterActions = GetProperty(dataTriggerActionTrigger, "EnterActions");
        AssertCollectionCount(dataEnterActions, expected: 1, "compiled DataTrigger EnterActions count");
        object dataEnterBeginStoryboard = GetCollectionItem(dataEnterActions, 0);
        AssertType(dataEnterBeginStoryboard, "System.Windows.Media.Animation.BeginStoryboard", "compiled DataTrigger EnterActions BeginStoryboard");
        object dataEnterStoryboard = GetProperty(dataEnterBeginStoryboard, "Storyboard");
        AssertType(dataEnterStoryboard, "System.Windows.Media.Animation.Storyboard", "compiled DataTrigger EnterActions Storyboard");
        object dataEnterStoryboardChildren = GetProperty(dataEnterStoryboard, "Children");
        AssertCollectionCount(dataEnterStoryboardChildren, expected: 1, "compiled DataTrigger EnterActions Storyboard children");
        object dataEnterDoubleAnimation = GetCollectionItem(dataEnterStoryboardChildren, 0);
        AssertType(dataEnterDoubleAnimation, "System.Windows.Media.Animation.DoubleAnimation", "compiled DataTrigger EnterActions DoubleAnimation");
        AssertEqual(0.52, GetProperty(dataEnterDoubleAnimation, "To"), "compiled DataTrigger EnterActions target value");
        AssertEqual("00:00:00", GetProperty(dataEnterDoubleAnimation, "Duration").ToString(), "compiled DataTrigger EnterActions duration");
        AssertEqual("HoldEnd", GetProperty(dataEnterDoubleAnimation, "FillBehavior").ToString(), "compiled DataTrigger EnterActions fill behavior");
        object dataExitActions = GetProperty(dataTriggerActionTrigger, "ExitActions");
        AssertCollectionCount(dataExitActions, expected: 1, "compiled DataTrigger ExitActions count");
        object dataExitBeginStoryboard = GetCollectionItem(dataExitActions, 0);
        AssertType(dataExitBeginStoryboard, "System.Windows.Media.Animation.BeginStoryboard", "compiled DataTrigger ExitActions BeginStoryboard");
        object dataExitStoryboard = GetProperty(dataExitBeginStoryboard, "Storyboard");
        AssertType(dataExitStoryboard, "System.Windows.Media.Animation.Storyboard", "compiled DataTrigger ExitActions Storyboard");
        object dataExitStoryboardChildren = GetProperty(dataExitStoryboard, "Children");
        AssertCollectionCount(dataExitStoryboardChildren, expected: 1, "compiled DataTrigger ExitActions Storyboard children");
        object dataExitDoubleAnimation = GetCollectionItem(dataExitStoryboardChildren, 0);
        AssertType(dataExitDoubleAnimation, "System.Windows.Media.Animation.DoubleAnimation", "compiled DataTrigger ExitActions DoubleAnimation");
        AssertEqual(1.0, GetProperty(dataExitDoubleAnimation, "To"), "compiled DataTrigger ExitActions target value");
        AssertEqual("00:00:00", GetProperty(dataExitDoubleAnimation, "Duration").ToString(), "compiled DataTrigger ExitActions duration");
        AssertEqual("HoldEnd", GetProperty(dataExitDoubleAnimation, "FillBehavior").ToString(), "compiled DataTrigger ExitActions fill behavior");

        object multiDataTriggerActionButton = GetField(window, "MultiDataTriggerActionButton");
        AssertType(multiDataTriggerActionButton, "System.Windows.Controls.Button", "compiled multi-data-trigger-action Button");
        AssertSame(expectedMultiDataTriggerActionStyle, GetProperty(multiDataTriggerActionButton, "Style"), "compiled Button MultiDataTrigger action style");
        AssertEqual("multi data trigger action target", GetProperty(multiDataTriggerActionButton, "Content"), "compiled Button MultiDataTrigger action content");
        object multiDataTriggerActionTriggers = GetProperty(expectedMultiDataTriggerActionStyle, "Triggers");
        AssertCollectionCount(multiDataTriggerActionTriggers, expected: 1, "compiled MultiDataTrigger action trigger count");
        object multiDataTriggerActionTrigger = GetCollectionItem(multiDataTriggerActionTriggers, 0);
        AssertType(multiDataTriggerActionTrigger, "System.Windows.MultiDataTrigger", "compiled MultiDataTrigger action metadata");
        object multiDataTriggerActionConditions = GetProperty(multiDataTriggerActionTrigger, "Conditions");
        AssertCollectionCount(multiDataTriggerActionConditions, expected: 2, "compiled MultiDataTrigger action condition count");
        object readyActionCondition = GetCollectionItem(multiDataTriggerActionConditions, 0);
        object armedActionCondition = GetCollectionItem(multiDataTriggerActionConditions, 1);
        AssertBindingObjectPath(GetProperty(readyActionCondition, "Binding"), "IsMultiTriggerActionReady", "compiled MultiDataTrigger action first binding path");
        AssertEqual("True", GetProperty(readyActionCondition, "Value").ToString(), "compiled MultiDataTrigger action first value");
        AssertBindingObjectPath(GetProperty(armedActionCondition, "Binding"), "IsMultiTriggerActionArmed", "compiled MultiDataTrigger action second binding path");
        AssertEqual("True", GetProperty(armedActionCondition, "Value").ToString(), "compiled MultiDataTrigger action second value");
        AssertCollectionCount(GetProperty(multiDataTriggerActionTrigger, "Setters"), expected: 0, "compiled MultiDataTrigger action setter count");
        object multiDataEnterActions = GetProperty(multiDataTriggerActionTrigger, "EnterActions");
        AssertCollectionCount(multiDataEnterActions, expected: 1, "compiled MultiDataTrigger EnterActions count");
        object multiDataEnterBeginStoryboard = GetCollectionItem(multiDataEnterActions, 0);
        AssertType(multiDataEnterBeginStoryboard, "System.Windows.Media.Animation.BeginStoryboard", "compiled MultiDataTrigger EnterActions BeginStoryboard");
        object multiDataEnterStoryboard = GetProperty(multiDataEnterBeginStoryboard, "Storyboard");
        AssertType(multiDataEnterStoryboard, "System.Windows.Media.Animation.Storyboard", "compiled MultiDataTrigger EnterActions Storyboard");
        object multiDataEnterStoryboardChildren = GetProperty(multiDataEnterStoryboard, "Children");
        AssertCollectionCount(multiDataEnterStoryboardChildren, expected: 1, "compiled MultiDataTrigger EnterActions Storyboard children");
        object multiDataEnterDoubleAnimation = GetCollectionItem(multiDataEnterStoryboardChildren, 0);
        AssertType(multiDataEnterDoubleAnimation, "System.Windows.Media.Animation.DoubleAnimation", "compiled MultiDataTrigger EnterActions DoubleAnimation");
        AssertEqual(0.63, GetProperty(multiDataEnterDoubleAnimation, "To"), "compiled MultiDataTrigger EnterActions target value");
        AssertEqual("00:00:00", GetProperty(multiDataEnterDoubleAnimation, "Duration").ToString(), "compiled MultiDataTrigger EnterActions duration");
        AssertEqual("HoldEnd", GetProperty(multiDataEnterDoubleAnimation, "FillBehavior").ToString(), "compiled MultiDataTrigger EnterActions fill behavior");
        object multiDataExitActions = GetProperty(multiDataTriggerActionTrigger, "ExitActions");
        AssertCollectionCount(multiDataExitActions, expected: 1, "compiled MultiDataTrigger ExitActions count");
        object multiDataExitBeginStoryboard = GetCollectionItem(multiDataExitActions, 0);
        AssertType(multiDataExitBeginStoryboard, "System.Windows.Media.Animation.BeginStoryboard", "compiled MultiDataTrigger ExitActions BeginStoryboard");
        object multiDataExitStoryboard = GetProperty(multiDataExitBeginStoryboard, "Storyboard");
        AssertType(multiDataExitStoryboard, "System.Windows.Media.Animation.Storyboard", "compiled MultiDataTrigger ExitActions Storyboard");
        object multiDataExitStoryboardChildren = GetProperty(multiDataExitStoryboard, "Children");
        AssertCollectionCount(multiDataExitStoryboardChildren, expected: 1, "compiled MultiDataTrigger ExitActions Storyboard children");
        object multiDataExitDoubleAnimation = GetCollectionItem(multiDataExitStoryboardChildren, 0);
        AssertType(multiDataExitDoubleAnimation, "System.Windows.Media.Animation.DoubleAnimation", "compiled MultiDataTrigger ExitActions DoubleAnimation");
        AssertEqual(1.0, GetProperty(multiDataExitDoubleAnimation, "To"), "compiled MultiDataTrigger ExitActions target value");
        AssertEqual("00:00:00", GetProperty(multiDataExitDoubleAnimation, "Duration").ToString(), "compiled MultiDataTrigger ExitActions duration");
        AssertEqual("HoldEnd", GetProperty(multiDataExitDoubleAnimation, "FillBehavior").ToString(), "compiled MultiDataTrigger ExitActions fill behavior");

        object multiTriggerActionButton = GetField(window, "MultiTriggerActionButton");
        AssertType(multiTriggerActionButton, "System.Windows.Controls.Button", "compiled multi-trigger-action Button");
        AssertSame(expectedMultiTriggerActionStyle, GetProperty(multiTriggerActionButton, "Style"), "compiled Button MultiTrigger action style");
        AssertEqual("multi trigger action target", GetProperty(multiTriggerActionButton, "Content"), "compiled Button MultiTrigger action content");
        AssertEqual(true, GetProperty(multiTriggerActionButton, "IsEnabled"), "compiled MultiTrigger action enabled condition");
        AssertEqual(false, GetProperty(multiTriggerActionButton, "IsDefault"), "compiled MultiTrigger action initial default condition");
        object multiTriggerActionTriggers = GetProperty(expectedMultiTriggerActionStyle, "Triggers");
        AssertCollectionCount(multiTriggerActionTriggers, expected: 1, "compiled MultiTrigger action trigger count");
        object multiTriggerActionTrigger = GetCollectionItem(multiTriggerActionTriggers, 0);
        AssertType(multiTriggerActionTrigger, "System.Windows.MultiTrigger", "compiled MultiTrigger action metadata");
        object multiTriggerActionConditions = GetProperty(multiTriggerActionTrigger, "Conditions");
        AssertCollectionCount(multiTriggerActionConditions, expected: 2, "compiled MultiTrigger action condition count");
        object actionEnabledCondition = GetCollectionItem(multiTriggerActionConditions, 0);
        object actionDefaultCondition = GetCollectionItem(multiTriggerActionConditions, 1);
        AssertEqual("IsEnabled", GetProperty(GetProperty(actionEnabledCondition, "Property"), "Name"), "compiled MultiTrigger action first condition property");
        AssertEqual(true, GetProperty(actionEnabledCondition, "Value"), "compiled MultiTrigger action first condition value");
        AssertEqual("IsDefault", GetProperty(GetProperty(actionDefaultCondition, "Property"), "Name"), "compiled MultiTrigger action second condition property");
        AssertEqual(true, GetProperty(actionDefaultCondition, "Value"), "compiled MultiTrigger action second condition value");
        AssertCollectionCount(GetProperty(multiTriggerActionTrigger, "Setters"), expected: 0, "compiled MultiTrigger action setter count");
        object multiEnterActions = GetProperty(multiTriggerActionTrigger, "EnterActions");
        AssertCollectionCount(multiEnterActions, expected: 1, "compiled MultiTrigger EnterActions count");
        object multiEnterBeginStoryboard = GetCollectionItem(multiEnterActions, 0);
        AssertType(multiEnterBeginStoryboard, "System.Windows.Media.Animation.BeginStoryboard", "compiled MultiTrigger EnterActions BeginStoryboard");
        object multiEnterStoryboard = GetProperty(multiEnterBeginStoryboard, "Storyboard");
        AssertType(multiEnterStoryboard, "System.Windows.Media.Animation.Storyboard", "compiled MultiTrigger EnterActions Storyboard");
        object multiEnterStoryboardChildren = GetProperty(multiEnterStoryboard, "Children");
        AssertCollectionCount(multiEnterStoryboardChildren, expected: 1, "compiled MultiTrigger EnterActions Storyboard children");
        object multiEnterDoubleAnimation = GetCollectionItem(multiEnterStoryboardChildren, 0);
        AssertType(multiEnterDoubleAnimation, "System.Windows.Media.Animation.DoubleAnimation", "compiled MultiTrigger EnterActions DoubleAnimation");
        AssertEqual(0.74, GetProperty(multiEnterDoubleAnimation, "To"), "compiled MultiTrigger EnterActions target value");
        AssertEqual("00:00:00", GetProperty(multiEnterDoubleAnimation, "Duration").ToString(), "compiled MultiTrigger EnterActions duration");
        AssertEqual("HoldEnd", GetProperty(multiEnterDoubleAnimation, "FillBehavior").ToString(), "compiled MultiTrigger EnterActions fill behavior");
        object multiExitActions = GetProperty(multiTriggerActionTrigger, "ExitActions");
        AssertCollectionCount(multiExitActions, expected: 1, "compiled MultiTrigger ExitActions count");
        object multiExitBeginStoryboard = GetCollectionItem(multiExitActions, 0);
        AssertType(multiExitBeginStoryboard, "System.Windows.Media.Animation.BeginStoryboard", "compiled MultiTrigger ExitActions BeginStoryboard");
        object multiExitStoryboard = GetProperty(multiExitBeginStoryboard, "Storyboard");
        AssertType(multiExitStoryboard, "System.Windows.Media.Animation.Storyboard", "compiled MultiTrigger ExitActions Storyboard");
        object multiExitStoryboardChildren = GetProperty(multiExitStoryboard, "Children");
        AssertCollectionCount(multiExitStoryboardChildren, expected: 1, "compiled MultiTrigger ExitActions Storyboard children");
        object multiExitDoubleAnimation = GetCollectionItem(multiExitStoryboardChildren, 0);
        AssertType(multiExitDoubleAnimation, "System.Windows.Media.Animation.DoubleAnimation", "compiled MultiTrigger ExitActions DoubleAnimation");
        AssertEqual(1.0, GetProperty(multiExitDoubleAnimation, "To"), "compiled MultiTrigger ExitActions target value");
        AssertEqual("00:00:00", GetProperty(multiExitDoubleAnimation, "Duration").ToString(), "compiled MultiTrigger ExitActions duration");
        AssertEqual("HoldEnd", GetProperty(multiExitDoubleAnimation, "FillBehavior").ToString(), "compiled MultiTrigger ExitActions fill behavior");

        object multiTriggeredButton = GetField(window, "MultiTriggeredButton");
        AssertType(multiTriggeredButton, "System.Windows.Controls.Button", "compiled multi-triggered Button");
        AssertSame(expectedMultiStyle, GetProperty(multiTriggeredButton, "Style"), "compiled Button MultiDataTrigger style");
        AssertEqual("style trigger target", GetProperty(multiTriggeredButton, "Content"), "compiled Button MultiDataTrigger content binding");
        AssertEqual("multi trigger inactive", GetProperty(multiTriggeredButton, "Tag"), "compiled MultiDataTrigger inactive value");
        AssertSame(accentBrush, GetProperty(multiTriggeredButton, "Background"), "compiled MultiDataTrigger inactive brush");

        SetProperty(dataContext, "IsWarning", true);
        AssertEqual(true, GetProperty(dataContext, "IsWarning"), "style trigger updated view-model state");
        AssertEqual("trigger active", GetProperty(triggeredButton, "Tag"), "compiled DataTrigger active value");
        AssertSame(replacementAccentBrush, GetProperty(triggeredButton, "Background"), "compiled DataTrigger active brush");
        AssertEqual("multi trigger inactive", GetProperty(multiTriggeredButton, "Tag"), "compiled MultiDataTrigger partial-condition value");
        AssertSame(accentBrush, GetProperty(multiTriggeredButton, "Background"), "compiled MultiDataTrigger partial-condition brush");

        SetProperty(dataContext, "IsCritical", true);
        AssertEqual(true, GetProperty(dataContext, "IsCritical"), "multi trigger updated critical view-model state");
        AssertEqual("multi trigger active", GetProperty(multiTriggeredButton, "Tag"), "compiled MultiDataTrigger active value");
        AssertSame(replacementAccentBrush, GetProperty(multiTriggeredButton, "Background"), "compiled MultiDataTrigger active brush");
    }

    private static void ValidateRoutedCommand(object window)
    {
        object inputBox = GetField(window, "InputBox");
        object routedCommandButton = GetField(window, "RoutedCommandButton");
        AssertType(routedCommandButton, "System.Windows.Controls.Button", "compiled routed command Button");
        AssertEqual("run routed command", GetProperty(routedCommandButton, "Content"), "compiled routed command Button content");
        AssertSame(inputBox, GetProperty(routedCommandButton, "CommandTarget"), "compiled routed command target");

        object commandParameter = GetProperty(routedCommandButton, "CommandParameter");
        AssertEqual("routed command payload", commandParameter, "compiled routed command parameter");

        object routedCommand = GetProperty(routedCommandButton, "Command");
        AssertType(routedCommand, "System.Windows.Input.RoutedUICommand", "compiled routed command");
        AssertEqual("SmokeRoutedCommand", GetProperty(routedCommand, "Name"), "compiled routed command name");
        AssertEqual(0, GetProperty(window, "RoutedCommandExecutionCount"), "routed command initial execution count");

        object canExecute = InvokeTwoArgumentCommand(routedCommand, "CanExecute", commandParameter, inputBox);
        AssertEqual(true, canExecute, "routed command CanExecute result");
        AssertAtLeast(1, GetProperty(window, "RoutedCommandCanExecuteCount"), "routed command CanExecute handler count");

        InvokeTwoArgumentCommand(routedCommand, "Execute", commandParameter, inputBox);
        AssertEqual(1, GetProperty(window, "RoutedCommandExecutionCount"), "routed command execution count");
        AssertEqual("routed command payload", GetProperty(window, "LastRoutedCommandParameter"), "routed command executed parameter");

        object classCommandTarget = GetField(window, "ClassCommandTargetBox");
        AssertType(classCommandTarget, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeClassCommandTextBox", "compiled class command target");
        AssertEqual("class command target", GetProperty(classCommandTarget, "Text"), "compiled class command target text");
        object classCommandButton = GetField(window, "ClassCommandButton");
        AssertType(classCommandButton, "System.Windows.Controls.Button", "compiled class command Button");
        AssertEqual("run class command", GetProperty(classCommandButton, "Content"), "compiled class command Button content");
        AssertSame(classCommandTarget, GetProperty(classCommandButton, "CommandTarget"), "compiled class command target binding");
        object classCommandParameter = GetProperty(classCommandButton, "CommandParameter");
        AssertEqual("class command payload", classCommandParameter, "compiled class command parameter");

        object classCommand = GetProperty(classCommandButton, "Command");
        AssertType(classCommand, "System.Windows.Input.RoutedUICommand", "compiled class routed command");
        AssertEqual("SmokeClassRoutedCommand", GetProperty(classCommand, "Name"), "compiled class routed command name");
        AssertEqual(0, GetProperty(classCommandTarget, "ClassCommandExecutionCount"), "class command initial execution count");
        AssertEqual(true, InvokeTwoArgumentCommand(classCommand, "CanExecute", classCommandParameter, classCommandTarget), "class command CanExecute result");
        AssertAtLeast(1, GetProperty(classCommandTarget, "ClassCommandCanExecuteCount"), "class command CanExecute handler count");

        Invoke(classCommandButton, "OnClick");
        AssertEqual(1, GetProperty(classCommandTarget, "ClassCommandExecutionCount"), "class command execution count");
        AssertEqual("class command payload", GetProperty(classCommandTarget, "LastClassCommandParameter"), "class command executed parameter");

        SetProperty(classCommandTarget, "IsClassCommandEnabled", false);
        AssertEqual(false, InvokeTwoArgumentCommand(classCommand, "CanExecute", classCommandParameter, classCommandTarget), "class command disabled CanExecute result");
        AssertAtLeast(2, GetProperty(classCommandTarget, "ClassCommandCanExecuteCount"), "class command disabled CanExecute handler count");
        SetProperty(classCommandTarget, "IsClassCommandEnabled", true);
    }

    private static void ValidateInputBinding(object window)
    {
        object inputBindings = GetProperty(window, "InputBindings");
        AssertCollectionCount(inputBindings, expected: 1, "compiled Window input bindings");

        object keyBinding = GetCollectionItem(inputBindings, 0);
        AssertType(keyBinding, "System.Windows.Input.KeyBinding", "compiled KeyBinding");
        AssertEqual("F6", GetProperty(keyBinding, "Key").ToString(), "compiled KeyBinding key");
        AssertEqual("Control", GetProperty(keyBinding, "Modifiers").ToString(), "compiled KeyBinding modifiers");
        AssertEqual("input binding payload", GetProperty(keyBinding, "CommandParameter"), "compiled KeyBinding command parameter");

        object keyGesture = GetProperty(keyBinding, "Gesture");
        AssertType(keyGesture, "System.Windows.Input.KeyGesture", "compiled KeyGesture");
        AssertEqual("F6", GetProperty(keyGesture, "Key").ToString(), "compiled KeyGesture key");
        AssertEqual("Control", GetProperty(keyGesture, "Modifiers").ToString(), "compiled KeyGesture modifiers");

        object inputBox = GetField(window, "InputBox");
        object command = GetProperty(keyBinding, "Command");
        AssertType(command, "System.Windows.Input.RoutedUICommand", "compiled KeyBinding routed command");
        AssertEqual("SmokeRoutedCommand", GetProperty(command, "Name"), "compiled KeyBinding routed command name");
        AssertEqual(1, GetProperty(window, "RoutedCommandExecutionCount"), "input binding routed command initial execution count");

        object canExecute = InvokeTwoArgumentCommand(command, "CanExecute", GetProperty(keyBinding, "CommandParameter"), inputBox);
        AssertEqual(true, canExecute, "compiled KeyBinding command CanExecute result");
        InvokeTwoArgumentCommand(command, "Execute", GetProperty(keyBinding, "CommandParameter"), inputBox);

        AssertEqual(2, GetProperty(window, "RoutedCommandExecutionCount"), "compiled KeyBinding command execution count");
        AssertEqual("input binding payload", GetProperty(window, "LastRoutedCommandParameter"), "compiled KeyBinding command executed parameter");
    }

    private static void ValidateMouseBinding(object window)
    {
        object mouseBindingSurface = GetField(window, "MouseBindingSurface");
        AssertType(mouseBindingSurface, "System.Windows.Controls.TextBlock", "compiled MouseBinding surface");
        AssertEqual("mouse binding surface", GetProperty(mouseBindingSurface, "Tag"), "compiled MouseBinding surface tag");

        object inputBindings = GetProperty(mouseBindingSurface, "InputBindings");
        AssertCollectionCount(inputBindings, expected: 1, "compiled MouseBinding surface input bindings");

        object mouseBinding = GetCollectionItem(inputBindings, 0);
        AssertType(mouseBinding, "System.Windows.Input.MouseBinding", "compiled MouseBinding");
        AssertEqual("RightClick", GetProperty(mouseBinding, "MouseAction").ToString(), "compiled MouseBinding action");
        AssertEqual("mouse binding payload", GetProperty(mouseBinding, "CommandParameter"), "compiled MouseBinding command parameter");

        object mouseGesture = GetProperty(mouseBinding, "Gesture");
        AssertType(mouseGesture, "System.Windows.Input.MouseGesture", "compiled MouseGesture");
        AssertEqual("RightClick", GetProperty(mouseGesture, "MouseAction").ToString(), "compiled MouseGesture action");
        AssertEqual("None", GetProperty(mouseGesture, "Modifiers").ToString(), "compiled MouseGesture modifiers");

        object command = GetProperty(mouseBinding, "Command");
        AssertType(command, "System.Windows.Input.RoutedUICommand", "compiled MouseBinding routed command");
        AssertEqual("SmokeRoutedCommand", GetProperty(command, "Name"), "compiled MouseBinding routed command name");
    }

    private static void ValidateMenuItems(object window)
    {
        object menu = GetField(window, "SmokeMenu");
        AssertType(menu, "System.Windows.Controls.Menu", "compiled Menu");
        AssertCollectionCount(GetProperty(menu, "Items"), expected: 1, "compiled Menu items");

        object fileMenuItem = GetField(window, "FileMenuItem");
        AssertType(fileMenuItem, "System.Windows.Controls.MenuItem", "compiled parent MenuItem");
        AssertEqual("_File", GetProperty(fileMenuItem, "Header"), "compiled parent MenuItem header");
        object fileMenuItems = GetProperty(fileMenuItem, "Items");
        AssertCollectionCount(fileMenuItems, expected: 4, "compiled parent MenuItem children");

        object commandItem = GetField(window, "MenuCommandItem");
        AssertType(commandItem, "System.Windows.Controls.MenuItem", "compiled command MenuItem");
        AssertEqual("Run _Command", GetProperty(commandItem, "Header"), "compiled command MenuItem header");
        AssertSame(menu, GetProperty(commandItem, "CommandTarget"), "compiled command MenuItem target");
        AssertEqual("menu command payload", GetProperty(commandItem, "CommandParameter"), "compiled command MenuItem parameter");
        object command = GetProperty(commandItem, "Command");
        AssertType(command, "System.Windows.Input.RoutedUICommand", "compiled command MenuItem routed command");
        AssertEqual("SmokeRoutedCommand", GetProperty(command, "Name"), "compiled command MenuItem routed command name");
        AssertSame(commandItem, GetCollectionItem(fileMenuItems, 0), "compiled command MenuItem collection position");

        object separator = GetCollectionItem(fileMenuItems, 1);
        AssertType(separator, "System.Windows.Controls.Separator", "compiled Menu separator");

        object clickItem = GetField(window, "MenuClickItem");
        AssertType(clickItem, "System.Windows.Controls.MenuItem", "compiled click MenuItem");
        AssertEqual("_Click", GetProperty(clickItem, "Header"), "compiled click MenuItem header");
        AssertSame(clickItem, GetCollectionItem(fileMenuItems, 2), "compiled click MenuItem collection position");
        AssertEqual(0, GetProperty(window, "MenuClickCount"), "compiled MenuItem initial click count");

        RaiseMenuItemClick(clickItem);

        AssertEqual(1, GetProperty(window, "MenuClickCount"), "compiled MenuItem Click handler count");
        AssertEqual("MenuClickItem", GetProperty(window, "LastMenuClickSenderName"), "compiled MenuItem Click sender name");
        AssertEqual("Click", GetProperty(window, "LastMenuClickRoutedEventName"), "compiled MenuItem Click routed event name");
        object checkableItem = GetField(window, "MenuCheckableItem");
        AssertType(checkableItem, "System.Windows.Controls.MenuItem", "compiled checkable MenuItem");
        AssertEqual("_Checkable", GetProperty(checkableItem, "Header"), "compiled checkable MenuItem header");
        AssertEqual(true, GetProperty(checkableItem, "IsCheckable"), "compiled checkable MenuItem is checkable");
        AssertEqual(false, GetProperty(checkableItem, "IsChecked"), "compiled checkable MenuItem initial checked state");
        AssertSame(checkableItem, GetCollectionItem(fileMenuItems, 3), "compiled checkable MenuItem collection position");
        AssertEqual(0, GetProperty(window, "MenuCheckableCheckedCount"), "compiled checkable MenuItem initial checked count");
        AssertEqual(0, GetProperty(window, "MenuCheckableUncheckedCount"), "compiled checkable MenuItem initial unchecked count");

        Invoke(checkableItem, "OnClick");

        AssertEqual(true, GetProperty(checkableItem, "IsChecked"), "compiled checkable MenuItem checked state");
        AssertEqual(1, GetProperty(window, "MenuCheckableCheckedCount"), "compiled checkable MenuItem Checked handler count");
        AssertEqual("MenuCheckableItem", GetProperty(window, "LastMenuCheckableCheckedSenderName"), "compiled checkable MenuItem Checked sender name");
        AssertEqual("Checked", GetProperty(window, "LastMenuCheckableCheckedRoutedEventName"), "compiled checkable MenuItem Checked routed event name");

        Invoke(checkableItem, "OnClick");

        AssertEqual(false, GetProperty(checkableItem, "IsChecked"), "compiled checkable MenuItem unchecked state");
        AssertEqual(1, GetProperty(window, "MenuCheckableUncheckedCount"), "compiled checkable MenuItem Unchecked handler count");
        AssertEqual("MenuCheckableItem", GetProperty(window, "LastMenuCheckableUncheckedSenderName"), "compiled checkable MenuItem Unchecked sender name");
        AssertEqual("Unchecked", GetProperty(window, "LastMenuCheckableUncheckedRoutedEventName"), "compiled checkable MenuItem Unchecked routed event name");
        AssertEqual(2, GetProperty(window, "RoutedCommandExecutionCount"), "compiled command MenuItem initial routed command count");

        object commandCanExecute = InvokeTwoArgumentCommand(
            command,
            "CanExecute",
            GetProperty(commandItem, "CommandParameter"),
            GetProperty(commandItem, "CommandTarget"));
        AssertEqual(true, commandCanExecute, "compiled command MenuItem CanExecute result");
        InvokeTwoArgumentCommand(
            command,
            "Execute",
            GetProperty(commandItem, "CommandParameter"),
            GetProperty(commandItem, "CommandTarget"));

        AssertEqual(3, GetProperty(window, "RoutedCommandExecutionCount"), "compiled command MenuItem routed command count");
        AssertEqual("menu command payload", GetProperty(window, "LastRoutedCommandParameter"), "compiled command MenuItem routed command parameter");
    }

    private static void ValidateContextMenuAndToolTip(object window)
    {
        object contextButton = GetField(window, "ContextMenuButton");
        AssertType(contextButton, "System.Windows.Controls.Button", "compiled ContextMenu owner Button");
        AssertEqual("context menu target", GetProperty(contextButton, "Content"), "compiled ContextMenu owner Button content");

        object contextMenu = GetProperty(contextButton, "ContextMenu");
        AssertType(contextMenu, "System.Windows.Controls.ContextMenu", "compiled ContextMenu");
        AssertEqual("ContextButtonMenu", GetProperty(contextMenu, "Name"), "compiled ContextMenu name");
        object contextMenuItems = GetProperty(contextMenu, "Items");
        AssertCollectionCount(contextMenuItems, expected: 3, "compiled ContextMenu items");

        object commandItem = GetCollectionItem(contextMenuItems, 0);
        AssertType(commandItem, "System.Windows.Controls.MenuItem", "compiled ContextMenu command item");
        AssertEqual("ContextCommandItem", GetProperty(commandItem, "Name"), "compiled ContextMenu command item name");
        AssertEqual("Run Context _Command", GetProperty(commandItem, "Header"), "compiled ContextMenu command item header");
        AssertEqual("context menu command payload", GetProperty(commandItem, "CommandParameter"), "compiled ContextMenu command item parameter");
        object command = GetProperty(commandItem, "Command");
        AssertType(command, "System.Windows.Input.RoutedUICommand", "compiled ContextMenu routed command");
        AssertEqual("SmokeRoutedCommand", GetProperty(command, "Name"), "compiled ContextMenu routed command name");

        object separator = GetCollectionItem(contextMenuItems, 1);
        AssertType(separator, "System.Windows.Controls.Separator", "compiled ContextMenu separator");

        object clickItem = GetCollectionItem(contextMenuItems, 2);
        AssertType(clickItem, "System.Windows.Controls.MenuItem", "compiled ContextMenu click item");
        AssertEqual("ContextClickItem", GetProperty(clickItem, "Name"), "compiled ContextMenu click item name");
        AssertEqual("Context _Click", GetProperty(clickItem, "Header"), "compiled ContextMenu click item header");
        AssertEqual(0, GetProperty(window, "ContextMenuClickCount"), "compiled ContextMenu initial click count");

        RaiseMenuItemClick(clickItem);

        AssertEqual(1, GetProperty(window, "ContextMenuClickCount"), "compiled ContextMenu Click handler count");
        AssertEqual("ContextClickItem", GetProperty(window, "LastContextMenuClickSenderName"), "compiled ContextMenu Click sender name");
        AssertEqual("Click", GetProperty(window, "LastContextMenuClickRoutedEventName"), "compiled ContextMenu Click routed event name");
        AssertEqual(3, GetProperty(window, "RoutedCommandExecutionCount"), "compiled ContextMenu initial routed command count");

        object commandCanExecute = InvokeTwoArgumentCommand(
            command,
            "CanExecute",
            GetProperty(commandItem, "CommandParameter"),
            contextButton);
        AssertEqual(true, commandCanExecute, "compiled ContextMenu CanExecute result");
        InvokeTwoArgumentCommand(
            command,
            "Execute",
            GetProperty(commandItem, "CommandParameter"),
            contextButton);

        AssertEqual(4, GetProperty(window, "RoutedCommandExecutionCount"), "compiled ContextMenu routed command count");
        AssertEqual("context menu command payload", GetProperty(window, "LastRoutedCommandParameter"), "compiled ContextMenu routed command parameter");

        object toolTip = GetProperty(contextButton, "ToolTip");
        AssertType(toolTip, "System.Windows.Controls.ToolTip", "compiled ToolTip");
        AssertEqual("ContextButtonToolTip", GetProperty(toolTip, "Name"), "compiled ToolTip name");
        AssertEqual("Right", GetProperty(toolTip, "Placement").ToString(), "compiled ToolTip placement");
        object toolTipContent = GetProperty(toolTip, "Content");
        AssertType(toolTipContent, "System.Windows.Controls.TextBlock", "compiled ToolTip content");
        AssertEqual("ContextButtonToolTipText", GetProperty(toolTipContent, "Name"), "compiled ToolTip content name");
        AssertEqual("compiled tooltip text", GetProperty(toolTipContent, "Tag"), "compiled ToolTip content tag");
        AssertEqual("compiled ToolTip content", GetProperty(toolTipContent, "Text"), "compiled ToolTip content text");
    }

    private static void ValidateToolBarAndStatusBar(object window)
    {
        object toolBarTray = GetField(window, "SmokeToolBarTray");
        AssertType(toolBarTray, "System.Windows.Controls.ToolBarTray", "compiled ToolBarTray");
        object toolBars = GetProperty(toolBarTray, "ToolBars");
        AssertCollectionCount(toolBars, expected: 1, "compiled ToolBarTray toolbars");

        object toolBar = GetField(window, "SmokeToolBar");
        AssertType(toolBar, "System.Windows.Controls.ToolBar", "compiled ToolBar");
        AssertSame(toolBar, GetCollectionItem(toolBars, 0), "compiled ToolBarTray child toolbar");
        AssertEqual("Smoke tools", GetProperty(toolBar, "Header"), "compiled ToolBar header");
        object toolBarItems = GetProperty(toolBar, "Items");
        AssertCollectionCount(toolBarItems, expected: 3, "compiled ToolBar items");

        object commandButton = GetField(window, "ToolBarCommandButton");
        AssertType(commandButton, "System.Windows.Controls.Button", "compiled ToolBar command Button");
        AssertSame(commandButton, GetCollectionItem(toolBarItems, 0), "compiled ToolBar command item");
        AssertEqual("Run toolbar", GetProperty(commandButton, "Content"), "compiled ToolBar command Button content");
        AssertEqual("toolbar command payload", GetProperty(commandButton, "CommandParameter"), "compiled ToolBar command parameter");
        object command = GetProperty(commandButton, "Command");
        AssertType(command, "System.Windows.Input.RoutedUICommand", "compiled ToolBar routed command");
        AssertEqual("SmokeRoutedCommand", GetProperty(command, "Name"), "compiled ToolBar routed command name");
        AssertEqual(4, GetProperty(window, "RoutedCommandExecutionCount"), "compiled ToolBar initial routed command count");

        object commandCanExecute = InvokeTwoArgumentCommand(
            command,
            "CanExecute",
            GetProperty(commandButton, "CommandParameter"),
            toolBar);
        AssertEqual(true, commandCanExecute, "compiled ToolBar CanExecute result");
        InvokeTwoArgumentCommand(
            command,
            "Execute",
            GetProperty(commandButton, "CommandParameter"),
            toolBar);

        AssertEqual(5, GetProperty(window, "RoutedCommandExecutionCount"), "compiled ToolBar routed command count");
        AssertEqual("toolbar command payload", GetProperty(window, "LastRoutedCommandParameter"), "compiled ToolBar routed command parameter");

        object toolBarSeparator = GetField(window, "ToolBarSeparator");
        AssertType(toolBarSeparator, "System.Windows.Controls.Separator", "compiled ToolBar separator");
        AssertSame(toolBarSeparator, GetCollectionItem(toolBarItems, 1), "compiled ToolBar separator item");

        object toolBarToggle = GetField(window, "ToolBarToggle");
        AssertType(toolBarToggle, "System.Windows.Controls.Primitives.ToggleButton", "compiled ToolBar ToggleButton");
        AssertSame(toolBarToggle, GetCollectionItem(toolBarItems, 2), "compiled ToolBar toggle item");
        AssertEqual("Toggle toolbar", GetProperty(toolBarToggle, "Content"), "compiled ToolBar ToggleButton content");
        AssertEqual(true, GetProperty(toolBarToggle, "IsChecked"), "compiled ToolBar ToggleButton checked state");

        object statusBar = GetField(window, "SmokeStatusBar");
        AssertType(statusBar, "System.Windows.Controls.Primitives.StatusBar", "compiled StatusBar");
        object statusItems = GetProperty(statusBar, "Items");
        AssertCollectionCount(statusItems, expected: 3, "compiled StatusBar items");

        object readyItem = GetField(window, "StatusReadyItem");
        AssertType(readyItem, "System.Windows.Controls.Primitives.StatusBarItem", "compiled StatusBarItem");
        AssertSame(readyItem, GetCollectionItem(statusItems, 0), "compiled StatusBar ready item");
        AssertEqual("Ready", GetProperty(readyItem, "Content"), "compiled StatusBarItem content");

        object statusSeparator = GetCollectionItem(statusItems, 1);
        AssertType(statusSeparator, "System.Windows.Controls.Separator", "compiled StatusBar separator");

        object statusText = GetField(window, "StatusTextBlock");
        AssertType(statusText, "System.Windows.Controls.TextBlock", "compiled StatusBar TextBlock");
        AssertSame(statusText, GetCollectionItem(statusItems, 2), "compiled StatusBar TextBlock item");
        AssertEqual("status text", GetProperty(statusText, "Tag"), "compiled StatusBar TextBlock tag");
        AssertEqual("detail from implicit template", GetProperty(statusText, "Text"), "compiled StatusBar TextBlock binding");
    }

    private static void ValidateRangeControls(object window)
    {
        object dataContext = GetProperty(window, "DataContext");

        object slider = GetField(window, "RangeValueSlider");
        AssertType(slider, "System.Windows.Controls.Slider", "compiled Slider");
        AssertEqual(0.0, GetProperty(slider, "Minimum"), "compiled Slider minimum");
        AssertEqual(100.0, GetProperty(slider, "Maximum"), "compiled Slider maximum");
        AssertEqual(25.0, GetProperty(slider, "TickFrequency"), "compiled Slider tick frequency");
        AssertEqual(false, GetProperty(slider, "IsSnapToTickEnabled"), "compiled Slider snap-to-tick state");
        AssertEqual(42.0, GetProperty(slider, "Value"), "compiled Slider initial value");
        AssertBindingPath(slider, "ValueProperty", "RangeValue", "compiled Slider Value binding path");

        object progress = GetField(window, "RangeValueProgress");
        AssertType(progress, "System.Windows.Controls.ProgressBar", "compiled ProgressBar");
        AssertEqual(0.0, GetProperty(progress, "Minimum"), "compiled ProgressBar minimum");
        AssertEqual(100.0, GetProperty(progress, "Maximum"), "compiled ProgressBar maximum");
        AssertEqual(12.0, GetProperty(progress, "Height"), "compiled ProgressBar height");
        AssertEqual(42.0, GetProperty(progress, "Value"), "compiled ProgressBar initial value");
        AssertBindingPath(progress, "ValueProperty", "RangeValue", "compiled ProgressBar Value binding path");

        SetProperty(slider, "Value", 64.0);

        AssertEqual(64.0, GetProperty(dataContext, "RangeValue"), "compiled Slider two-way value source update");
        AssertEqual(64.0, GetProperty(progress, "Value"), "compiled ProgressBar value after source update");

        AssertEqual(0.1, GetProperty(slider, "SmallChange"), "compiled Slider small change default");
        AssertEqual(1.0, GetProperty(slider, "LargeChange"), "compiled Slider large change default");
        SetProperty(slider, "Value", 40.0);
        AssertEqual(40.0, GetProperty(dataContext, "RangeValue"), "compiled Slider command baseline source update");
        ExecuteSliderCommand(slider, dataContext, progress, "IncreaseSmall", 40.1, "compiled Slider IncreaseSmall command");
        ExecuteSliderCommand(slider, dataContext, progress, "DecreaseSmall", 40.0, "compiled Slider DecreaseSmall command");
        ExecuteSliderCommand(slider, dataContext, progress, "IncreaseLarge", 41.0, "compiled Slider IncreaseLarge command");
        ExecuteSliderCommand(slider, dataContext, progress, "DecreaseLarge", 40.0, "compiled Slider DecreaseLarge command");
        ExecuteSliderCommand(slider, dataContext, progress, "MaximizeValue", 100.0, "compiled Slider MaximizeValue command");
        ExecuteSliderCommand(slider, dataContext, progress, "MinimizeValue", 0.0, "compiled Slider MinimizeValue command");
    }

    private static void ExecuteSliderCommand(
        object slider,
        object dataContext,
        object progress,
        string commandPropertyName,
        double expectedValue,
        string description)
    {
        object command = GetStaticProperty(slider.GetType(), commandPropertyName);
        AssertEqual(true, InvokeTwoArgumentCommand(command, "CanExecute", null, slider), $"{description} CanExecute");
        InvokeTwoArgumentCommand(command, "Execute", null, slider);
        AssertClose(expectedValue, Convert.ToDouble(GetProperty(slider, "Value")), 0.0001, $"{description} value");
        AssertClose(expectedValue, Convert.ToDouble(GetProperty(dataContext, "RangeValue")), 0.0001, $"{description} source value");
        AssertClose(expectedValue, Convert.ToDouble(GetProperty(progress, "Value")), 0.0001, $"{description} progress value");
    }

    private static void RaiseMenuItemClick(object menuItem)
    {
        FieldInfo clickEventField = menuItem.GetType().GetField(
            "ClickEvent",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)
            ?? throw new MissingFieldException(menuItem.GetType().FullName, "ClickEvent");
        object clickEvent = clickEventField.GetValue(null)
            ?? throw new InvalidOperationException("Expected MenuItem.ClickEvent to be initialized.");
        Type routedEventArgsType = clickEvent.GetType().Assembly.GetType(
            "System.Windows.RoutedEventArgs",
            throwOnError: true)
            ?? throw new TypeLoadException("System.Windows.RoutedEventArgs");
        object routedEventArgs = Activator.CreateInstance(routedEventArgsType, clickEvent, menuItem)
            ?? throw new InvalidOperationException("Failed to create MenuItem Click RoutedEventArgs.");

        Invoke(menuItem, "RaiseEvent", routedEventArgs);
    }

    private static void ValidateTemplateAndDynamicResource(object window, object application)
    {
        object resources = GetProperty(application, "Resources");
        object accentBrush = GetDictionaryValue(resources, "AccentBrush");
        object replacementAccentBrush = GetDictionaryValue(resources, "ReplacementAccentBrush");
        object expectedTemplate = GetDictionaryValue(resources, "SmokeButtonTemplate");

        object templatedButton = GetField(window, "TemplatedButton");
        AssertType(templatedButton, "System.Windows.Controls.Button", "compiled templated Button");
        AssertEqual("templated button", GetProperty(templatedButton, "Content"), "compiled templated Button content");
        AssertSame(expectedTemplate, GetProperty(templatedButton, "Template"), "compiled Button control template");

        object templateResources = GetProperty(expectedTemplate, "Resources");
        object templateBorderBrush = GetDictionaryValue(templateResources, "TemplateBorderBrush");
        AssertType(templateBorderBrush, "System.Windows.Media.SolidColorBrush", "compiled ControlTemplate scoped resource brush");
        AssertEqual("#FF6B4E9B", GetProperty(templateBorderBrush, "Color").ToString(), "compiled ControlTemplate scoped resource brush color");

        AssertEqual(true, Invoke(templatedButton, "ApplyTemplate"), "compiled Button template application");

        object templateBorder = Invoke(expectedTemplate, "FindName", "TemplateBorder", templatedButton);
        AssertType(templateBorder, "System.Windows.Controls.Border", "compiled ControlTemplate named part");
        AssertSame(accentBrush, GetProperty(templateBorder, "Background"), "compiled ControlTemplate dynamic resource initial value");
        AssertSame(templateBorderBrush, GetProperty(templateBorder, "BorderBrush"), "compiled ControlTemplate scoped BorderBrush");
        object templateBorderThickness = GetProperty(templateBorder, "BorderThickness");
        AssertEqual(2.0, GetProperty(templateBorderThickness, "Left"), "compiled ControlTemplate scoped BorderThickness left");
        AssertEqual(2.0, GetProperty(templateBorderThickness, "Top"), "compiled ControlTemplate scoped BorderThickness top");
        AssertEqual(2.0, GetProperty(templateBorderThickness, "Right"), "compiled ControlTemplate scoped BorderThickness right");
        AssertEqual(2.0, GetProperty(templateBorderThickness, "Bottom"), "compiled ControlTemplate scoped BorderThickness bottom");
        AssertEqual(1.0, GetProperty(templateBorder, "Opacity"), "compiled ControlTemplate trigger initial opacity");
        ValidateTemplateVisualStateManager(templateBorder);

        SetDictionaryValue(resources, "AccentBrush", replacementAccentBrush);
        AssertSame(replacementAccentBrush, GetProperty(templateBorder, "Background"), "compiled ControlTemplate dynamic resource update");

        SetProperty(templatedButton, "IsEnabled", false);
        AssertEqual(false, GetProperty(templatedButton, "IsEnabled"), "compiled ControlTemplate trigger source state");
        AssertEqual(0.42, GetProperty(templateBorder, "Opacity"), "compiled ControlTemplate trigger disabled opacity");
    }

    private static void ValidateTemplateVisualStateManager(object templateBorder)
    {
        Type visualStateManagerType = templateBorder.GetType().Assembly.GetType(
            "System.Windows.VisualStateManager",
            throwOnError: true)
            ?? throw new TypeLoadException("System.Windows.VisualStateManager");

        object groups = InvokeStatic(visualStateManagerType, "GetVisualStateGroups", templateBorder);
        AssertCollectionCount(groups, expected: 1, "compiled VisualStateManager group collection");

        object commonStates = GetCollectionItem(groups, 0);
        AssertType(commonStates, "System.Windows.VisualStateGroup", "compiled VisualStateGroup");
        AssertEqual("CommonStates", GetProperty(commonStates, "Name"), "compiled VisualStateGroup name");

        object states = GetProperty(commonStates, "States");
        AssertCollectionCount(states, expected: 2, "compiled VisualState entries");
        object normalState = GetCollectionItem(states, 0);
        object pressedState = GetCollectionItem(states, 1);
        AssertType(normalState, "System.Windows.VisualState", "compiled Normal VisualState");
        AssertType(pressedState, "System.Windows.VisualState", "compiled Pressed VisualState");
        AssertEqual("Normal", GetProperty(normalState, "Name"), "compiled Normal VisualState name");
        AssertEqual("Pressed", GetProperty(pressedState, "Name"), "compiled Pressed VisualState name");

        object pressedStoryboard = GetProperty(pressedState, "Storyboard");
        AssertType(pressedStoryboard, "System.Windows.Media.Animation.Storyboard", "compiled Pressed VisualState storyboard");
        object pressedAnimations = GetProperty(pressedStoryboard, "Children");
        AssertCollectionCount(pressedAnimations, expected: 1, "compiled Pressed VisualState storyboard animations");
        object pressedAnimation = GetCollectionItem(pressedAnimations, 0);
        AssertType(pressedAnimation, "System.Windows.Media.Animation.DoubleAnimation", "compiled Pressed VisualState animation");
        AssertEqual(0.73, GetProperty(pressedAnimation, "To"), "compiled Pressed VisualState animation target value");
        AssertEqual("00:00:00", GetProperty(pressedAnimation, "Duration").ToString(), "compiled Pressed VisualState animation duration");
    }

    private static void ValidatePostShowTemplateVisualStateManager(object window, Action flushRender)
    {
        object templatedButton = GetField(window, "TemplatedButton");
        object template = GetProperty(templatedButton, "Template");
        object templateBorder = Invoke(template, "FindName", "TemplateBorder", templatedButton);
        Type visualStateManagerType = templateBorder.GetType().Assembly.GetType(
            "System.Windows.VisualStateManager",
            throwOnError: true)
            ?? throw new TypeLoadException("System.Windows.VisualStateManager");

        AssertEqual(true, InvokeStatic(visualStateManagerType, "GoToElementState", templateBorder, "Pressed", false), "compiled VisualStateManager Pressed transition");
        flushRender();

        AssertEqual(0.73, GetProperty(templateBorder, "Opacity"), "compiled VisualStateManager Pressed opacity");
    }

    private static void ValidateItemsBindingAndTemplate(object window)
    {
        object dataContext = GetProperty(window, "DataContext");
        object sourceItems = GetProperty(dataContext, "Items");
        AssertCollectionCount(sourceItems, expected: 2, "view-model items");
        object sourceLabels = GetProperty(dataContext, "Labels");
        AssertCollectionCount(sourceLabels, expected: 2, "view-model labels");

        object itemsList = GetField(window, "ItemsList");
        AssertType(itemsList, "System.Windows.Controls.ListBox", "compiled item ListBox");
        AssertSame(sourceItems, GetProperty(itemsList, "ItemsSource"), "compiled ListBox ItemsSource binding");
        AssertCollectionCount(GetProperty(itemsList, "Items"), expected: 2, "compiled ListBox generated items");
        AssertSame(GetCollectionItem(sourceItems, 1), GetProperty(itemsList, "SelectedItem"), "compiled ListBox initial selected item");

        object firstItem = GetCollectionItem(sourceItems, 0);
        SetProperty(itemsList, "SelectedItem", firstItem);
        AssertSame(firstItem, GetProperty(dataContext, "SelectedItem"), "compiled ListBox two-way selected item binding");

        object itemTemplate = GetProperty(itemsList, "ItemTemplate");
        AssertType(itemTemplate, "System.Windows.DataTemplate", "compiled ListBox item template");
        object itemContainerStyle = GetProperty(itemsList, "ItemContainerStyle");
        AssertType(itemContainerStyle, "System.Windows.Style", "compiled ListBox item container style");
        AssertEqual("System.Windows.Controls.ListBoxItem", GetProperty(itemContainerStyle, "TargetType").ToString(), "compiled ListBox item container style target");
        object itemContainerSetters = GetProperty(itemContainerStyle, "Setters");
        AssertCollectionCount(itemContainerSetters, expected: 1, "compiled ItemContainerStyle setters");
        object itemContainerSetter = GetCollectionItem(itemContainerSetters, 0);
        AssertType(itemContainerSetter, "System.Windows.Setter", "compiled ItemContainerStyle setter");
        AssertEqual("Tag", GetProperty(GetProperty(itemContainerSetter, "Property"), "Name"), "compiled ItemContainerStyle setter property");
        AssertEqual("container trigger inactive", GetProperty(itemContainerSetter, "Value"), "compiled ItemContainerStyle default setter value");
        object itemContainerStyleTriggers = GetProperty(itemContainerStyle, "Triggers");
        AssertCollectionCount(itemContainerStyleTriggers, expected: 1, "compiled ItemContainerStyle triggers");
        object itemContainerStyleTrigger = GetCollectionItem(itemContainerStyleTriggers, 0);
        AssertType(itemContainerStyleTrigger, "System.Windows.DataTrigger", "compiled ItemContainerStyle DataTrigger");
        AssertBindingObjectPath(GetProperty(itemContainerStyleTrigger, "Binding"), "Name", "compiled ItemContainerStyle DataTrigger binding path");
        AssertEqual("item beta", GetProperty(itemContainerStyleTrigger, "Value"), "compiled ItemContainerStyle DataTrigger value");
        object itemContainerStyleTriggerSetters = GetProperty(itemContainerStyleTrigger, "Setters");
        AssertCollectionCount(itemContainerStyleTriggerSetters, expected: 1, "compiled ItemContainerStyle DataTrigger setters");
        object itemContainerStyleTriggerSetter = GetCollectionItem(itemContainerStyleTriggerSetters, 0);
        AssertType(itemContainerStyleTriggerSetter, "System.Windows.Setter", "compiled ItemContainerStyle DataTrigger setter");
        AssertEqual("Tag", GetProperty(GetProperty(itemContainerStyleTriggerSetter, "Property"), "Name"), "compiled ItemContainerStyle DataTrigger setter property");
        AssertEqual("container trigger active", GetProperty(itemContainerStyleTriggerSetter, "Value"), "compiled ItemContainerStyle DataTrigger setter value");

        object templateRoot = Invoke(itemTemplate, "LoadContent");
        AssertType(templateRoot, "System.Windows.Controls.TextBlock", "compiled DataTemplate root");
        AssertEqual("ItemTextBlock", GetProperty(templateRoot, "Name"), "compiled DataTemplate named root");
        AssertEqual("template trigger inactive", GetProperty(templateRoot, "Tag"), "compiled DataTemplate root default tag");
        AssertBindingPath(templateRoot, "TextProperty", "Name", "compiled DataTemplate text binding path");
        object dataTemplateTriggers = GetProperty(itemTemplate, "Triggers");
        AssertCollectionCount(dataTemplateTriggers, expected: 1, "compiled DataTemplate triggers");
        object dataTemplateTrigger = GetCollectionItem(dataTemplateTriggers, 0);
        AssertType(dataTemplateTrigger, "System.Windows.DataTrigger", "compiled DataTemplate DataTrigger");
        AssertBindingObjectPath(GetProperty(dataTemplateTrigger, "Binding"), "Name", "compiled DataTemplate DataTrigger binding path");
        AssertEqual("item beta", GetProperty(dataTemplateTrigger, "Value"), "compiled DataTemplate DataTrigger value");
        object dataTemplateTriggerSetters = GetProperty(dataTemplateTrigger, "Setters");
        AssertCollectionCount(dataTemplateTriggerSetters, expected: 1, "compiled DataTemplate DataTrigger setters");
        object dataTemplateTriggerSetter = GetCollectionItem(dataTemplateTriggerSetters, 0);
        AssertType(dataTemplateTriggerSetter, "System.Windows.Setter", "compiled DataTemplate DataTrigger setter");
        AssertEqual("ItemTextBlock", GetProperty(dataTemplateTriggerSetter, "TargetName"), "compiled DataTemplate DataTrigger setter target");
        AssertEqual("Tag", GetProperty(GetProperty(dataTemplateTriggerSetter, "Property"), "Name"), "compiled DataTemplate DataTrigger setter property");
        AssertEqual("template trigger active", GetProperty(dataTemplateTriggerSetter, "Value"), "compiled DataTemplate DataTrigger setter value");

        object alphaTemplate = Invoke(window, "TryFindResource", "AlphaItemTemplate");
        AssertType(alphaTemplate, "System.Windows.DataTemplate", "compiled DataTemplateSelector alpha template resource");
        object alphaTemplateRoot = Invoke(alphaTemplate, "LoadContent");
        AssertType(alphaTemplateRoot, "System.Windows.Controls.TextBlock", "compiled DataTemplateSelector alpha template root");
        AssertEqual("SelectorTemplateTextBlock", GetProperty(alphaTemplateRoot, "Name"), "compiled DataTemplateSelector alpha template named root");
        AssertEqual("selector alpha template", GetProperty(alphaTemplateRoot, "Tag"), "compiled DataTemplateSelector alpha template tag");
        AssertBindingPath(alphaTemplateRoot, "TextProperty", "Name", "compiled DataTemplateSelector alpha binding path");

        object defaultTemplate = Invoke(window, "TryFindResource", "DefaultItemTemplate");
        AssertType(defaultTemplate, "System.Windows.DataTemplate", "compiled DataTemplateSelector default template resource");
        object defaultTemplateRoot = Invoke(defaultTemplate, "LoadContent");
        AssertType(defaultTemplateRoot, "System.Windows.Controls.TextBlock", "compiled DataTemplateSelector default template root");
        AssertEqual("SelectorTemplateTextBlock", GetProperty(defaultTemplateRoot, "Name"), "compiled DataTemplateSelector default template named root");
        AssertEqual("selector default template", GetProperty(defaultTemplateRoot, "Tag"), "compiled DataTemplateSelector default template tag");
        AssertBindingPath(defaultTemplateRoot, "TextProperty", "Name", "compiled DataTemplateSelector default binding path");

        object selector = Invoke(window, "TryFindResource", "SmokeItemTemplateSelector");
        AssertType(selector, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeItemTemplateSelector", "compiled DataTemplateSelector resource");
        AssertSame(alphaTemplate, GetProperty(selector, "AlphaTemplate"), "compiled DataTemplateSelector alpha template property");
        AssertSame(defaultTemplate, GetProperty(selector, "DefaultTemplate"), "compiled DataTemplateSelector default template property");

        object selectorItemsList = GetField(window, "SelectorItemsList");
        AssertType(selectorItemsList, "System.Windows.Controls.ListBox", "compiled selector ListBox");
        AssertSame(sourceItems, GetProperty(selectorItemsList, "ItemsSource"), "compiled DataTemplateSelector ListBox ItemsSource binding");
        AssertSame(selector, GetProperty(selectorItemsList, "ItemTemplateSelector"), "compiled ListBox ItemTemplateSelector binding");
        AssertCollectionCount(GetProperty(selectorItemsList, "Items"), expected: 2, "compiled DataTemplateSelector generated items");

        object alphaContainerStyle = Invoke(window, "TryFindResource", "AlphaItemContainerSelectorStyle");
        AssertType(alphaContainerStyle, "System.Windows.Style", "compiled ItemContainerStyleSelector alpha style resource");
        AssertEqual("System.Windows.Controls.ListBoxItem", GetProperty(alphaContainerStyle, "TargetType").ToString(), "compiled ItemContainerStyleSelector alpha style target");
        object defaultContainerStyle = Invoke(window, "TryFindResource", "DefaultItemContainerSelectorStyle");
        AssertType(defaultContainerStyle, "System.Windows.Style", "compiled ItemContainerStyleSelector default style resource");
        AssertEqual("System.Windows.Controls.ListBoxItem", GetProperty(defaultContainerStyle, "TargetType").ToString(), "compiled ItemContainerStyleSelector default style target");
        object containerStyleSelector = Invoke(window, "TryFindResource", "SmokeItemContainerStyleSelector");
        AssertType(containerStyleSelector, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeItemContainerStyleSelector", "compiled ItemContainerStyleSelector resource");
        AssertSame(alphaContainerStyle, GetProperty(containerStyleSelector, "AlphaStyle"), "compiled ItemContainerStyleSelector alpha style property");
        AssertSame(defaultContainerStyle, GetProperty(containerStyleSelector, "DefaultStyle"), "compiled ItemContainerStyleSelector default style property");

        object styleSelectorItemsList = GetField(window, "StyleSelectorItemsList");
        AssertType(styleSelectorItemsList, "System.Windows.Controls.ListBox", "compiled style selector ListBox");
        AssertSame(sourceItems, GetProperty(styleSelectorItemsList, "ItemsSource"), "compiled ItemContainerStyleSelector ListBox ItemsSource binding");
        AssertSame(containerStyleSelector, GetProperty(styleSelectorItemsList, "ItemContainerStyleSelector"), "compiled ListBox ItemContainerStyleSelector binding");
        AssertCollectionCount(GetProperty(styleSelectorItemsList, "Items"), expected: 2, "compiled ItemContainerStyleSelector generated items");

        object displayMemberItemsList = GetField(window, "DisplayMemberItemsList");
        AssertType(displayMemberItemsList, "System.Windows.Controls.ListBox", "compiled DisplayMemberPath ListBox");
        AssertSame(sourceItems, GetProperty(displayMemberItemsList, "ItemsSource"), "compiled DisplayMemberPath ListBox ItemsSource binding");
        AssertEqual("Name", GetProperty(displayMemberItemsList, "DisplayMemberPath"), "compiled ListBox DisplayMemberPath");
        AssertEqual("Category", GetProperty(displayMemberItemsList, "SelectedValuePath"), "compiled ListBox SelectedValuePath");
        AssertBindingPath(displayMemberItemsList, "SelectedValueProperty", "SelectedCategory", "compiled ListBox SelectedValue binding path");
        AssertEqual("secondary group", GetProperty(dataContext, "SelectedCategory"), "compiled ListBox initial selected category source");
        AssertEqual("secondary group", GetProperty(displayMemberItemsList, "SelectedValue"), "compiled ListBox initial selected value");
        AssertSame(GetCollectionItem(sourceItems, 1), GetProperty(displayMemberItemsList, "SelectedItem"), "compiled ListBox selected item by value path");
        SetProperty(displayMemberItemsList, "SelectedValue", "primary group");
        AssertEqual("primary group", GetProperty(dataContext, "SelectedCategory"), "compiled ListBox two-way selected value source update");
        AssertSame(GetCollectionItem(sourceItems, 0), GetProperty(displayMemberItemsList, "SelectedItem"), "compiled ListBox selected item after selected value update");

        object sortedItemsViewSource = Invoke(window, "TryFindResource", "SortedItemsView");
        AssertType(sortedItemsViewSource, "System.Windows.Data.CollectionViewSource", "compiled CollectionViewSource resource");
        object sortDescriptions = GetProperty(sortedItemsViewSource, "SortDescriptions");
        AssertCollectionCount(sortDescriptions, expected: 1, "compiled CollectionViewSource sort descriptions");
        object sortDescription = GetCollectionItem(sortDescriptions, 0);
        AssertEqual("Name", GetProperty(sortDescription, "PropertyName"), "compiled CollectionViewSource sort property");
        AssertEqual("Descending", GetProperty(sortDescription, "Direction").ToString(), "compiled CollectionViewSource sort direction");

        object sortedItemsList = GetField(window, "SortedItemsList");
        AssertType(sortedItemsList, "System.Windows.Controls.ListBox", "compiled sorted ListBox");
        object sortedItemsView = GetProperty(sortedItemsViewSource, "View");
        AssertSame(sortedItemsView, GetProperty(sortedItemsList, "ItemsSource"), "compiled ListBox CollectionViewSource binding");
        object sortedItems = GetProperty(sortedItemsList, "Items");
        AssertCollectionCount(sortedItems, expected: 2, "compiled sorted ListBox generated items");
        AssertEqual("item beta", GetProperty(GetCollectionItem(sortedItems, 0), "Name"), "compiled CollectionViewSource initial first item");
        AssertEqual("item alpha", GetProperty(GetCollectionItem(sortedItems, 1), "Name"), "compiled CollectionViewSource initial second item");

        object liveSortedItemsViewSource = Invoke(window, "TryFindResource", "LiveSortedItemsView");
        AssertType(liveSortedItemsViewSource, "System.Windows.Data.CollectionViewSource", "compiled live sorted CollectionViewSource resource");
        AssertEqual(true, GetProperty(liveSortedItemsViewSource, "IsLiveSortingRequested"), "compiled live CollectionViewSource sorting request");
        AssertEqual(true, GetProperty(liveSortedItemsViewSource, "CanChangeLiveSorting"), "compiled live CollectionViewSource can change sorting");
        AssertEqual(true, GetProperty(liveSortedItemsViewSource, "IsLiveSorting"), "compiled live CollectionViewSource sorting state");
        object liveSortDescriptions = GetProperty(liveSortedItemsViewSource, "SortDescriptions");
        AssertCollectionCount(liveSortDescriptions, expected: 1, "compiled live CollectionViewSource sort descriptions");
        object liveSortDescription = GetCollectionItem(liveSortDescriptions, 0);
        AssertEqual("Name", GetProperty(liveSortDescription, "PropertyName"), "compiled live CollectionViewSource sort property");
        AssertEqual("Descending", GetProperty(liveSortDescription, "Direction").ToString(), "compiled live CollectionViewSource sort direction");
        object liveSortingProperties = GetProperty(liveSortedItemsViewSource, "LiveSortingProperties");
        AssertCollectionCount(liveSortingProperties, expected: 1, "compiled live CollectionViewSource sorting properties");
        AssertEqual("Name", GetCollectionItem(liveSortingProperties, 0), "compiled live CollectionViewSource sorting property");

        object liveSortedItemsList = GetField(window, "LiveSortedItemsList");
        AssertType(liveSortedItemsList, "System.Windows.Controls.ListBox", "compiled live sorted ListBox");
        object liveSortedItemsView = GetProperty(liveSortedItemsViewSource, "View");
        AssertSame(liveSortedItemsView, GetProperty(liveSortedItemsList, "ItemsSource"), "compiled ListBox live CollectionViewSource binding");
        object liveSortedItems = GetProperty(liveSortedItemsList, "Items");
        AssertCollectionCount(liveSortedItems, expected: 2, "compiled live sorted ListBox generated items");
        AssertEqual("item beta", GetProperty(GetCollectionItem(liveSortedItems, 0), "Name"), "compiled live CollectionViewSource initial first item");
        AssertEqual("item alpha", GetProperty(GetCollectionItem(liveSortedItems, 1), "Name"), "compiled live CollectionViewSource initial second item");

        object compositeItemsList = GetField(window, "CompositeItemsList");
        AssertType(compositeItemsList, "System.Windows.Controls.ListBox", "compiled CompositeCollection ListBox");
        object compositeItemsSource = GetProperty(compositeItemsList, "ItemsSource");
        AssertType(compositeItemsSource, "System.Windows.Data.CompositeCollection", "compiled CompositeCollection source");
        object compositeItemsContainer = GetCollectionItem(compositeItemsSource, 1);
        AssertType(compositeItemsContainer, "System.Windows.Data.CollectionContainer", "compiled CompositeCollection container");
        object compositeSourceItems = GetProperty(compositeItemsContainer, "Collection");
        AssertCollectionCount(compositeSourceItems, expected: 2, "compiled CompositeCollection static source items");
        object compositeItems = GetProperty(compositeItemsList, "Items");
        AssertCollectionCount(compositeItems, expected: 4, "compiled CompositeCollection initial flattened items");
        AssertEqual("composite header", GetCollectionItem(compositeItems, 0), "compiled CompositeCollection header item");
        AssertEqual("item alpha", GetProperty(GetCollectionItem(compositeItems, 1), "Name"), "compiled CompositeCollection initial first collection item");
        AssertEqual("item beta", GetProperty(GetCollectionItem(compositeItems, 2), "Name"), "compiled CompositeCollection initial second collection item");
        AssertEqual("composite footer", GetCollectionItem(compositeItems, 3), "compiled CompositeCollection footer item");

        object alternationItemsList = GetField(window, "AlternationItemsList");
        AssertType(alternationItemsList, "System.Windows.Controls.ListBox", "compiled alternation ListBox");
        AssertSame(sourceItems, GetProperty(alternationItemsList, "ItemsSource"), "compiled alternation ListBox ItemsSource binding");
        AssertEqual(2, GetProperty(alternationItemsList, "AlternationCount"), "compiled ListBox AlternationCount");
        AssertCollectionCount(GetProperty(alternationItemsList, "Items"), expected: 2, "compiled alternation ListBox generated items");

        object stringFormatItemsList = GetField(window, "StringFormatItemsList");
        AssertType(stringFormatItemsList, "System.Windows.Controls.ListBox", "compiled ItemStringFormat ListBox");
        AssertSame(sourceLabels, GetProperty(stringFormatItemsList, "ItemsSource"), "compiled ItemStringFormat ListBox ItemsSource binding");
        AssertEqual("formatted {0}", GetProperty(stringFormatItemsList, "ItemStringFormat"), "compiled ListBox ItemStringFormat");
        AssertCollectionCount(GetProperty(stringFormatItemsList, "Items"), expected: 2, "compiled ItemStringFormat ListBox generated items");

        object filteredItemsViewSource = Invoke(window, "TryFindResource", "FilteredItemsView");
        AssertType(filteredItemsViewSource, "System.Windows.Data.CollectionViewSource", "compiled filtered CollectionViewSource resource");
        object filteredItemsList = GetField(window, "FilteredItemsList");
        AssertType(filteredItemsList, "System.Windows.Controls.ListBox", "compiled filtered ListBox");
        object filteredItemsView = GetProperty(filteredItemsViewSource, "View");
        AssertSame(filteredItemsView, GetProperty(filteredItemsList, "ItemsSource"), "compiled ListBox filtered CollectionViewSource binding");
        object filteredItems = GetProperty(filteredItemsList, "Items");
        AssertCollectionCount(filteredItems, expected: 1, "compiled filtered ListBox generated items");
        AssertEqual("item beta", GetProperty(GetCollectionItem(filteredItems, 0), "Name"), "compiled CollectionViewSource filtered item");
        if (Convert.ToInt32(GetProperty(window, "FilteredItemsFilterCount")) <= 0)
        {
            throw new InvalidOperationException("Expected compiled CollectionViewSource Filter handler to run.");
        }

        object liveFilteredItemsViewSource = Invoke(window, "TryFindResource", "LiveFilteredItemsView");
        AssertType(liveFilteredItemsViewSource, "System.Windows.Data.CollectionViewSource", "compiled live filtered CollectionViewSource resource");
        AssertEqual(true, GetProperty(liveFilteredItemsViewSource, "IsLiveFilteringRequested"), "compiled live CollectionViewSource filtering request");
        AssertEqual(true, GetProperty(liveFilteredItemsViewSource, "CanChangeLiveFiltering"), "compiled live CollectionViewSource can change filtering");
        AssertEqual(true, GetProperty(liveFilteredItemsViewSource, "IsLiveFiltering"), "compiled live CollectionViewSource filtering state");
        object liveFilteringProperties = GetProperty(liveFilteredItemsViewSource, "LiveFilteringProperties");
        AssertCollectionCount(liveFilteringProperties, expected: 1, "compiled live CollectionViewSource filtering properties");
        AssertEqual("Name", GetCollectionItem(liveFilteringProperties, 0), "compiled live CollectionViewSource filtering property");

        object liveFilteredItemsList = GetField(window, "LiveFilteredItemsList");
        AssertType(liveFilteredItemsList, "System.Windows.Controls.ListBox", "compiled live filtered ListBox");
        object liveFilteredItemsView = GetProperty(liveFilteredItemsViewSource, "View");
        AssertSame(liveFilteredItemsView, GetProperty(liveFilteredItemsList, "ItemsSource"), "compiled ListBox live filtered CollectionViewSource binding");
        object liveFilteredItems = GetProperty(liveFilteredItemsList, "Items");
        AssertCollectionCount(liveFilteredItems, expected: 1, "compiled live filtered ListBox generated items");
        AssertEqual("item beta", GetProperty(GetCollectionItem(liveFilteredItems, 0), "Name"), "compiled live CollectionViewSource filtered item");

        object groupedItemsViewSource = Invoke(window, "TryFindResource", "GroupedItemsView");
        AssertType(groupedItemsViewSource, "System.Windows.Data.CollectionViewSource", "compiled grouped CollectionViewSource resource");
        object groupDescriptions = GetProperty(groupedItemsViewSource, "GroupDescriptions");
        AssertCollectionCount(groupDescriptions, expected: 1, "compiled CollectionViewSource group descriptions");
        object groupDescription = GetCollectionItem(groupDescriptions, 0);
        AssertType(groupDescription, "System.Windows.Data.PropertyGroupDescription", "compiled CollectionViewSource group description");
        AssertEqual("Category", GetProperty(groupDescription, "PropertyName"), "compiled CollectionViewSource group property");

        object groupedItemsList = GetField(window, "GroupedItemsList");
        AssertType(groupedItemsList, "System.Windows.Controls.ListBox", "compiled grouped ListBox");
        object groupedItemsView = GetProperty(groupedItemsViewSource, "View");
        AssertSame(groupedItemsView, GetProperty(groupedItemsList, "ItemsSource"), "compiled ListBox grouped CollectionViewSource binding");
        object groupStyles = GetProperty(groupedItemsList, "GroupStyle");
        AssertCollectionCount(groupStyles, expected: 1, "compiled ListBox GroupStyle entries");
        object groupStyle = GetCollectionItem(groupStyles, 0);
        AssertType(groupStyle, "System.Windows.Controls.GroupStyle", "compiled ListBox GroupStyle");
        object groupHeaderTemplate = GetProperty(groupStyle, "HeaderTemplate");
        AssertType(groupHeaderTemplate, "System.Windows.DataTemplate", "compiled GroupStyle HeaderTemplate");
        object groupHeaderTemplateRoot = Invoke(groupHeaderTemplate, "LoadContent");
        AssertType(groupHeaderTemplateRoot, "System.Windows.Controls.TextBlock", "compiled GroupStyle HeaderTemplate root");
        AssertEqual("GroupHeaderTextBlock", GetProperty(groupHeaderTemplateRoot, "Name"), "compiled GroupStyle HeaderTemplate named root");
        AssertEqual("group header template", GetProperty(groupHeaderTemplateRoot, "Tag"), "compiled GroupStyle HeaderTemplate root tag");
        AssertBindingPath(groupHeaderTemplateRoot, "TextProperty", "Name", "compiled GroupStyle HeaderTemplate binding path");
        object groups = GetProperty(groupedItemsView, "Groups");
        AssertCollectionCount(groups, expected: 2, "compiled CollectionViewSource initial groups");
        ValidateCollectionViewGroup(GetCollectionItem(groups, 0), "primary group", expectedItemCount: 1, "initial primary");
        ValidateCollectionViewGroup(GetCollectionItem(groups, 1), "secondary group", expectedItemCount: 1, "initial secondary");

        object liveGroupedItemsViewSource = Invoke(window, "TryFindResource", "LiveGroupedItemsView");
        AssertType(liveGroupedItemsViewSource, "System.Windows.Data.CollectionViewSource", "compiled live grouped CollectionViewSource resource");
        AssertEqual(true, GetProperty(liveGroupedItemsViewSource, "IsLiveGroupingRequested"), "compiled live CollectionViewSource grouping request");
        AssertEqual(true, GetProperty(liveGroupedItemsViewSource, "CanChangeLiveGrouping"), "compiled live CollectionViewSource can change grouping");
        AssertEqual(true, GetProperty(liveGroupedItemsViewSource, "IsLiveGrouping"), "compiled live CollectionViewSource grouping state");
        object liveGroupDescriptions = GetProperty(liveGroupedItemsViewSource, "GroupDescriptions");
        AssertCollectionCount(liveGroupDescriptions, expected: 1, "compiled live CollectionViewSource group descriptions");
        object liveGroupDescription = GetCollectionItem(liveGroupDescriptions, 0);
        AssertType(liveGroupDescription, "System.Windows.Data.PropertyGroupDescription", "compiled live CollectionViewSource group description");
        AssertEqual("Category", GetProperty(liveGroupDescription, "PropertyName"), "compiled live CollectionViewSource group property");
        object liveGroupingProperties = GetProperty(liveGroupedItemsViewSource, "LiveGroupingProperties");
        AssertCollectionCount(liveGroupingProperties, expected: 1, "compiled live CollectionViewSource grouping properties");
        AssertEqual("Category", GetCollectionItem(liveGroupingProperties, 0), "compiled live CollectionViewSource grouping property");

        object liveGroupedItemsList = GetField(window, "LiveGroupedItemsList");
        AssertType(liveGroupedItemsList, "System.Windows.Controls.ListBox", "compiled live grouped ListBox");
        object liveGroupedItemsView = GetProperty(liveGroupedItemsViewSource, "View");
        AssertSame(liveGroupedItemsView, GetProperty(liveGroupedItemsList, "ItemsSource"), "compiled ListBox live grouped CollectionViewSource binding");
        object liveGroupStyles = GetProperty(liveGroupedItemsList, "GroupStyle");
        AssertCollectionCount(liveGroupStyles, expected: 1, "compiled live ListBox GroupStyle entries");
        object liveGroupStyle = GetCollectionItem(liveGroupStyles, 0);
        AssertType(liveGroupStyle, "System.Windows.Controls.GroupStyle", "compiled live ListBox GroupStyle");
        object liveGroupHeaderTemplate = GetProperty(liveGroupStyle, "HeaderTemplate");
        AssertType(liveGroupHeaderTemplate, "System.Windows.DataTemplate", "compiled live GroupStyle HeaderTemplate");
        object liveGroupHeaderTemplateRoot = Invoke(liveGroupHeaderTemplate, "LoadContent");
        AssertType(liveGroupHeaderTemplateRoot, "System.Windows.Controls.TextBlock", "compiled live GroupStyle HeaderTemplate root");
        AssertEqual("LiveGroupHeaderTextBlock", GetProperty(liveGroupHeaderTemplateRoot, "Name"), "compiled live GroupStyle HeaderTemplate named root");
        AssertEqual("live group header template", GetProperty(liveGroupHeaderTemplateRoot, "Tag"), "compiled live GroupStyle HeaderTemplate root tag");
        AssertBindingPath(liveGroupHeaderTemplateRoot, "TextProperty", "Name", "compiled live GroupStyle HeaderTemplate binding path");
        object liveGroups = GetProperty(liveGroupedItemsView, "Groups");
        AssertCollectionCount(liveGroups, expected: 2, "compiled live CollectionViewSource initial groups");
        ValidateCollectionViewGroup(GetCollectionItem(liveGroups, 0), "primary group", expectedItemCount: 1, "live initial primary");
        ValidateCollectionViewGroup(GetCollectionItem(liveGroups, 1), "secondary group", expectedItemCount: 1, "live initial secondary");

        object currencyItemsViewSource = Invoke(window, "TryFindResource", "CurrencyItemsView");
        AssertType(currencyItemsViewSource, "System.Windows.Data.CollectionViewSource", "compiled current-item CollectionViewSource resource");
        object currencyItemsView = GetProperty(currencyItemsViewSource, "View");
        object currencyItemsList = GetField(window, "CurrencyItemsList");
        AssertType(currencyItemsList, "System.Windows.Controls.ListBox", "compiled current-item ListBox");
        AssertSame(currencyItemsView, GetProperty(currencyItemsList, "ItemsSource"), "compiled ListBox current-item CollectionViewSource binding");
        AssertEqual(true, GetProperty(currencyItemsList, "IsSynchronizedWithCurrentItem"), "compiled ListBox current-item synchronization");
        SetProperty(currencyItemsList, "SelectedIndex", 1);
        AssertSame(GetCollectionItem(sourceItems, 1), GetProperty(currencyItemsView, "CurrentItem"), "compiled CollectionViewSource current item after selector selection");
        AssertEqual(1, GetProperty(currencyItemsView, "CurrentPosition"), "compiled CollectionViewSource current position after selector selection");
        Invoke(currencyItemsView, "MoveCurrentToPosition", 0);
        AssertEqual(0, GetProperty(currencyItemsList, "SelectedIndex"), "compiled ListBox selection after current-position move");
        AssertSame(GetCollectionItem(sourceItems, 0), GetProperty(currencyItemsList, "SelectedItem"), "compiled ListBox selected item after current-position move");

        object thirdItem = Create(window.GetType().Assembly, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeItem", "item gamma");
        AddToCollection(sourceItems, thirdItem);
        AddToCollection(sourceLabels, "label gamma");
        AssertCollectionCount(GetProperty(itemsList, "Items"), expected: 3, "compiled ListBox collection-change items");
        AssertCollectionCount(GetProperty(alternationItemsList, "Items"), expected: 3, "compiled alternation ListBox collection-change items");
        AssertCollectionCount(GetProperty(stringFormatItemsList, "Items"), expected: 3, "compiled ItemStringFormat ListBox collection-change items");
        AssertCollectionCount(sortedItems, expected: 3, "compiled sorted ListBox collection-change items");
        AssertEqual("item gamma", GetProperty(GetCollectionItem(sortedItems, 0), "Name"), "compiled CollectionViewSource collection-change first item");
        AssertCollectionCount(liveSortedItems, expected: 3, "compiled live sorted ListBox collection-change items");
        AssertEqual("item gamma", GetProperty(GetCollectionItem(liveSortedItems, 0), "Name"), "compiled live CollectionViewSource collection-change first item");
        object compositeThirdItem = Create(window.GetType().Assembly, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeItem", "item gamma");
        AddToCollection(compositeSourceItems, compositeThirdItem);
        AssertCollectionCount(compositeItems, expected: 5, "compiled CompositeCollection collection-change flattened items");
        AssertEqual("item gamma", GetProperty(GetCollectionItem(compositeItems, 3), "Name"), "compiled CompositeCollection collection-change appended collection item");
        AssertEqual("composite footer", GetCollectionItem(compositeItems, 4), "compiled CompositeCollection collection-change footer item");
        AssertCollectionCount(filteredItems, expected: 1, "compiled filtered CollectionViewSource collection-change items");
        AssertEqual("item beta", GetProperty(GetCollectionItem(filteredItems, 0), "Name"), "compiled filtered CollectionViewSource collection-change item");
        AssertCollectionCount(liveFilteredItems, expected: 1, "compiled live filtered CollectionViewSource collection-change items");
        AssertEqual("item beta", GetProperty(GetCollectionItem(liveFilteredItems, 0), "Name"), "compiled live filtered CollectionViewSource collection-change item");
        groups = GetProperty(groupedItemsView, "Groups");
        AssertCollectionCount(groups, expected: 2, "compiled CollectionViewSource collection-change groups");
        ValidateCollectionViewGroup(GetCollectionItem(groups, 0), "primary group", expectedItemCount: 2, "collection-change primary");
        ValidateCollectionViewGroup(GetCollectionItem(groups, 1), "secondary group", expectedItemCount: 1, "collection-change secondary");
        liveGroups = GetProperty(liveGroupedItemsView, "Groups");
        AssertCollectionCount(liveGroups, expected: 2, "compiled live CollectionViewSource collection-change groups");
        ValidateCollectionViewGroup(GetCollectionItem(liveGroups, 0), "primary group", expectedItemCount: 2, "live collection-change primary");
        ValidateCollectionViewGroup(GetCollectionItem(liveGroups, 1), "secondary group", expectedItemCount: 1, "live collection-change secondary");

        object refreshFirstItem = GetCollectionItem(sourceItems, 0);
        object refreshSecondItem = GetCollectionItem(sourceItems, 1);
        SetProperty(refreshFirstItem, "Name", "item omega");
        Invoke(sortedItemsView, "Refresh");
        AssertEqual("item omega", GetProperty(GetCollectionItem(sortedItems, 0), "Name"), "compiled CollectionViewSource property-change refresh first item");

        SetProperty(refreshSecondItem, "Name", "item delta");
        Invoke(filteredItemsView, "Refresh");
        AssertCollectionCount(filteredItems, expected: 0, "compiled filtered CollectionViewSource property-change removed items");
        SetProperty(thirdItem, "Name", "item beta");
        Invoke(filteredItemsView, "Refresh");
        AssertCollectionCount(filteredItems, expected: 1, "compiled filtered CollectionViewSource property-change accepted items");
        AssertSame(thirdItem, GetCollectionItem(filteredItems, 0), "compiled filtered CollectionViewSource property-change accepted item");

        SetProperty(thirdItem, "Category", "secondary group");
        Invoke(groupedItemsView, "Refresh");
        groups = GetProperty(groupedItemsView, "Groups");
        AssertCollectionCount(groups, expected: 2, "compiled CollectionViewSource property-change refresh groups");
        ValidateCollectionViewGroup(GetCollectionItem(groups, 0), "primary group", expectedItemCount: 1, "property-change primary");
        ValidateCollectionViewGroup(GetCollectionItem(groups, 1), "secondary group", expectedItemCount: 2, "property-change secondary");

        SetProperty(refreshFirstItem, "Name", "item alpha");
        SetProperty(refreshSecondItem, "Name", "item beta");
        SetProperty(thirdItem, "Name", "item gamma");
        SetProperty(thirdItem, "Category", "primary group");
        Invoke(sortedItemsView, "Refresh");
        Invoke(filteredItemsView, "Refresh");
        Invoke(groupedItemsView, "Refresh");
        AssertEqual("item gamma", GetProperty(GetCollectionItem(sortedItems, 0), "Name"), "compiled CollectionViewSource property-change restored first item");
    }

    private static void ValidatePostShowLiveCollectionViewShaping(object window, Action flushDataBind)
    {
        object dataContext = GetProperty(window, "DataContext");
        object sourceItems = GetProperty(dataContext, "Items");
        AssertCollectionCount(sourceItems, expected: 3, "post-show live view-model items");

        object liveSortedItemsList = GetField(window, "LiveSortedItemsList");
        object liveSortedItems = GetProperty(liveSortedItemsList, "Items");
        AssertCollectionCount(liveSortedItems, expected: 3, "post-show live sorted ListBox items");
        AssertEqual("item gamma", GetProperty(GetCollectionItem(liveSortedItems, 0), "Name"), "post-show live CollectionViewSource initial first item");

        object liveFilteredItemsList = GetField(window, "LiveFilteredItemsList");
        object liveFilteredItems = GetProperty(liveFilteredItemsList, "Items");
        AssertCollectionCount(liveFilteredItems, expected: 1, "post-show live filtered CollectionViewSource initial items");

        object liveGroupedItemsViewSource = Invoke(window, "TryFindResource", "LiveGroupedItemsView");
        object liveGroupedItemsView = GetProperty(liveGroupedItemsViewSource, "View");
        object liveGroups = GetProperty(liveGroupedItemsView, "Groups");
        AssertCollectionCount(liveGroups, expected: 2, "post-show live grouped CollectionViewSource initial groups");
        ValidateCollectionViewGroup(GetCollectionItem(liveGroups, 0), "primary group", expectedItemCount: 2, "post-show live initial primary");
        ValidateCollectionViewGroup(GetCollectionItem(liveGroups, 1), "secondary group", expectedItemCount: 1, "post-show live initial secondary");

        object refreshFirstItem = GetCollectionItem(sourceItems, 0);
        object refreshSecondItem = GetCollectionItem(sourceItems, 1);
        object refreshThirdItem = GetCollectionItem(sourceItems, 2);
        AssertSame(refreshSecondItem, GetCollectionItem(liveFilteredItems, 0), "post-show live filtered CollectionViewSource initial item");

        SetProperty(refreshFirstItem, "Name", "item omega");
        flushDataBind();
        AssertEqual("item omega", GetProperty(GetCollectionItem(liveSortedItems, 0), "Name"), "compiled live CollectionViewSource property-change first item");

        SetProperty(refreshSecondItem, "Name", "item delta");
        flushDataBind();
        AssertCollectionCount(liveFilteredItems, expected: 0, "compiled live filtered CollectionViewSource property-change removed items");

        SetProperty(refreshThirdItem, "Name", "item beta");
        flushDataBind();
        AssertCollectionCount(liveFilteredItems, expected: 1, "compiled live filtered CollectionViewSource property-change accepted items");
        AssertSame(refreshThirdItem, GetCollectionItem(liveFilteredItems, 0), "compiled live filtered CollectionViewSource property-change accepted item");

        SetProperty(refreshThirdItem, "Category", "secondary group");
        flushDataBind();
        liveGroups = GetProperty(liveGroupedItemsView, "Groups");
        AssertCollectionCount(liveGroups, expected: 2, "compiled live CollectionViewSource property-change grouped groups");
        ValidateCollectionViewGroup(GetCollectionItem(liveGroups, 0), "primary group", expectedItemCount: 1, "live property-change primary");
        ValidateCollectionViewGroup(GetCollectionItem(liveGroups, 1), "secondary group", expectedItemCount: 2, "live property-change secondary");

        SetProperty(refreshFirstItem, "Name", "item alpha");
        SetProperty(refreshSecondItem, "Name", "item beta");
        SetProperty(refreshThirdItem, "Name", "item gamma");
        SetProperty(refreshThirdItem, "Category", "primary group");
        flushDataBind();
        AssertEqual("item gamma", GetProperty(GetCollectionItem(liveSortedItems, 0), "Name"), "compiled live CollectionViewSource property-change restored first item");
        AssertCollectionCount(liveFilteredItems, expected: 1, "compiled live filtered CollectionViewSource property-change restored items");
        AssertSame(refreshSecondItem, GetCollectionItem(liveFilteredItems, 0), "compiled live filtered CollectionViewSource property-change restored item");
        liveGroups = GetProperty(liveGroupedItemsView, "Groups");
        AssertCollectionCount(liveGroups, expected: 2, "compiled live CollectionViewSource property-change restored groups");
        ValidateCollectionViewGroup(GetCollectionItem(liveGroups, 0), "primary group", expectedItemCount: 2, "live property-change restored primary");
        ValidateCollectionViewGroup(GetCollectionItem(liveGroups, 1), "secondary group", expectedItemCount: 1, "live property-change restored secondary");
    }

    private static void ValidateCollectionViewGroup(
        object group,
        string expectedName,
        int expectedItemCount,
        string description)
    {
        AssertEqual(expectedName, GetProperty(group, "Name"), $"compiled CollectionViewSource {description} group name");
        AssertEqual(expectedItemCount, GetProperty(group, "ItemCount"), $"compiled CollectionViewSource {description} group item count");
        AssertCollectionCount(GetProperty(group, "Items"), expected: expectedItemCount, $"compiled CollectionViewSource {description} group items");
    }

    private static void ValidateComboBox(object window)
    {
        object dataContext = GetProperty(window, "DataContext");
        object sourceItems = GetProperty(dataContext, "Items");

        object comboBox = GetField(window, "ItemsComboBox");
        AssertType(comboBox, "System.Windows.Controls.ComboBox", "compiled ComboBox");
        AssertSame(sourceItems, GetProperty(comboBox, "ItemsSource"), "compiled ComboBox ItemsSource binding");
        AssertCollectionCount(GetProperty(comboBox, "Items"), expected: 3, "compiled ComboBox collection-change items");
        AssertEqual("Name", GetProperty(comboBox, "DisplayMemberPath"), "compiled ComboBox DisplayMemberPath");
        AssertEqual("Category", GetProperty(comboBox, "SelectedValuePath"), "compiled ComboBox SelectedValuePath");
        AssertBindingPath(comboBox, "SelectedValueProperty", "ComboSelectedCategory", "compiled ComboBox SelectedValue binding path");
        AssertEqual("secondary group", GetProperty(dataContext, "ComboSelectedCategory"), "compiled ComboBox initial selected category source");
        AssertEqual("secondary group", GetProperty(comboBox, "SelectedValue"), "compiled ComboBox initial selected value");
        AssertSame(GetCollectionItem(sourceItems, 1), GetProperty(comboBox, "SelectedItem"), "compiled ComboBox selected item by value path");
        AssertEqual(1, GetProperty(comboBox, "SelectedIndex"), "compiled ComboBox initial selected index");

        SetProperty(comboBox, "SelectedValue", "primary group");

        AssertEqual("primary group", GetProperty(dataContext, "ComboSelectedCategory"), "compiled ComboBox two-way selected value source update");
        AssertEqual("primary group", GetProperty(comboBox, "SelectedValue"), "compiled ComboBox updated selected value");
        AssertSame(GetCollectionItem(sourceItems, 0), GetProperty(comboBox, "SelectedItem"), "compiled ComboBox selected item after selected value update");
        AssertEqual(0, GetProperty(comboBox, "SelectedIndex"), "compiled ComboBox updated selected index");
    }

    private static void ValidateSelectorSelectionChangedEvents(object window)
    {
        object panel = GetField(window, "SelectorEventPanel");
        AssertType(panel, "System.Windows.Controls.StackPanel", "compiled selector event panel");
        AssertCollectionCount(GetProperty(panel, "Children"), expected: 3, "compiled selector event panel children");

        object listBox = GetField(window, "SelectionEventListBox");
        AssertType(listBox, "System.Windows.Controls.ListBox", "compiled SelectionChanged ListBox");
        object listBoxItems = GetProperty(listBox, "Items");
        AssertCollectionCount(listBoxItems, expected: 2, "compiled SelectionChanged ListBox items");
        object listBoxAlpha = GetCollectionItem(listBoxItems, 0);
        object listBoxBeta = GetCollectionItem(listBoxItems, 1);
        AssertEqual("selection alpha", GetProperty(listBoxAlpha, "Content"), "compiled SelectionChanged ListBox alpha content");
        AssertEqual("selection beta", GetProperty(listBoxBeta, "Content"), "compiled SelectionChanged ListBox beta content");
        AssertEqual(-1, GetProperty(listBox, "SelectedIndex"), "compiled SelectionChanged ListBox initial selected index");
        AssertEqual(0, GetProperty(window, "ListBoxSelectionChangedCount"), "compiled ListBox SelectionChanged initial count");

        SetProperty(listBox, "SelectedIndex", 0);
        AssertSame(listBoxAlpha, GetProperty(listBox, "SelectedItem"), "compiled SelectionChanged ListBox selected alpha item");
        AssertEqual(1, GetProperty(window, "ListBoxSelectionChangedCount"), "compiled ListBox SelectionChanged alpha count");
        AssertEqual("SelectionEventListBox", GetProperty(window, "LastListBoxSelectionSenderName"), "compiled ListBox SelectionChanged sender");
        AssertEqual("SelectionChanged", GetProperty(window, "LastListBoxSelectionRoutedEventName"), "compiled ListBox SelectionChanged routed event");
        AssertEqual(1, GetProperty(window, "LastListBoxSelectionAddedCount"), "compiled ListBox SelectionChanged alpha added count");
        AssertEqual(0, GetProperty(window, "LastListBoxSelectionRemovedCount"), "compiled ListBox SelectionChanged alpha removed count");
        AssertEqual("selection alpha", GetProperty(window, "LastListBoxSelectionAddedItem"), "compiled ListBox SelectionChanged alpha added item");

        SetProperty(listBox, "SelectedIndex", 1);
        AssertSame(listBoxBeta, GetProperty(listBox, "SelectedItem"), "compiled SelectionChanged ListBox selected beta item");
        AssertEqual(2, GetProperty(window, "ListBoxSelectionChangedCount"), "compiled ListBox SelectionChanged beta count");
        AssertEqual(1, GetProperty(window, "LastListBoxSelectionAddedCount"), "compiled ListBox SelectionChanged beta added count");
        AssertEqual(1, GetProperty(window, "LastListBoxSelectionRemovedCount"), "compiled ListBox SelectionChanged beta removed count");
        AssertEqual("selection beta", GetProperty(window, "LastListBoxSelectionAddedItem"), "compiled ListBox SelectionChanged beta added item");
        AssertEqual("selection alpha", GetProperty(window, "LastListBoxSelectionRemovedItem"), "compiled ListBox SelectionChanged alpha removed item");

        object comboBox = GetField(window, "SelectionEventComboBox");
        AssertType(comboBox, "System.Windows.Controls.ComboBox", "compiled SelectionChanged ComboBox");
        object comboBoxItems = GetProperty(comboBox, "Items");
        AssertCollectionCount(comboBoxItems, expected: 2, "compiled SelectionChanged ComboBox items");
        object comboAlpha = GetCollectionItem(comboBoxItems, 0);
        object comboBeta = GetCollectionItem(comboBoxItems, 1);
        AssertEqual("combo alpha", GetProperty(comboAlpha, "Content"), "compiled SelectionChanged ComboBox alpha content");
        AssertEqual("combo beta", GetProperty(comboBeta, "Content"), "compiled SelectionChanged ComboBox beta content");
        AssertEqual(-1, GetProperty(comboBox, "SelectedIndex"), "compiled SelectionChanged ComboBox initial selected index");
        AssertEqual(0, GetProperty(window, "ComboBoxSelectionChangedCount"), "compiled ComboBox SelectionChanged initial count");

        SetProperty(comboBox, "SelectedIndex", 0);
        AssertSame(comboAlpha, GetProperty(comboBox, "SelectedItem"), "compiled SelectionChanged ComboBox selected alpha item");
        AssertEqual(1, GetProperty(window, "ComboBoxSelectionChangedCount"), "compiled ComboBox SelectionChanged alpha count");
        AssertEqual("SelectionEventComboBox", GetProperty(window, "LastComboBoxSelectionSenderName"), "compiled ComboBox SelectionChanged sender");
        AssertEqual("SelectionChanged", GetProperty(window, "LastComboBoxSelectionRoutedEventName"), "compiled ComboBox SelectionChanged routed event");
        AssertEqual(1, GetProperty(window, "LastComboBoxSelectionAddedCount"), "compiled ComboBox SelectionChanged alpha added count");
        AssertEqual(0, GetProperty(window, "LastComboBoxSelectionRemovedCount"), "compiled ComboBox SelectionChanged alpha removed count");
        AssertEqual("combo alpha", GetProperty(window, "LastComboBoxSelectionAddedItem"), "compiled ComboBox SelectionChanged alpha added item");

        SetProperty(comboBox, "SelectedIndex", 1);
        AssertSame(comboBeta, GetProperty(comboBox, "SelectedItem"), "compiled SelectionChanged ComboBox selected beta item");
        AssertEqual(2, GetProperty(window, "ComboBoxSelectionChangedCount"), "compiled ComboBox SelectionChanged beta count");
        AssertEqual(1, GetProperty(window, "LastComboBoxSelectionAddedCount"), "compiled ComboBox SelectionChanged beta added count");
        AssertEqual(1, GetProperty(window, "LastComboBoxSelectionRemovedCount"), "compiled ComboBox SelectionChanged beta removed count");
        AssertEqual("combo beta", GetProperty(window, "LastComboBoxSelectionAddedItem"), "compiled ComboBox SelectionChanged beta added item");
        AssertEqual("combo alpha", GetProperty(window, "LastComboBoxSelectionRemovedItem"), "compiled ComboBox SelectionChanged alpha removed item");

        object multiListBox = GetField(window, "MultiSelectionEventListBox");
        AssertType(multiListBox, "System.Windows.Controls.ListBox", "compiled multi-selection ListBox");
        AssertEqual("Multiple", GetProperty(multiListBox, "SelectionMode").ToString(), "compiled multi-selection ListBox mode");
        object multiItems = GetProperty(multiListBox, "Items");
        AssertCollectionCount(multiItems, expected: 3, "compiled multi-selection ListBox items");
        object multiAlpha = GetCollectionItem(multiItems, 0);
        object multiBeta = GetCollectionItem(multiItems, 1);
        object multiGamma = GetCollectionItem(multiItems, 2);
        AssertEqual("multi alpha", GetProperty(multiAlpha, "Content"), "compiled multi-selection alpha content");
        AssertEqual("multi beta", GetProperty(multiBeta, "Content"), "compiled multi-selection beta content");
        AssertEqual("multi gamma", GetProperty(multiGamma, "Content"), "compiled multi-selection gamma content");
        object selectedItems = GetProperty(multiListBox, "SelectedItems");
        AssertCollectionCount(selectedItems, expected: 0, "compiled multi-selection initial selected items");
        AssertEqual(0, GetProperty(window, "MultiListBoxSelectionChangedCount"), "compiled multi-selection initial count");

        SetProperty(multiAlpha, "IsSelected", true);
        AssertCollectionCount(selectedItems, expected: 1, "compiled multi-selection alpha selected items");
        AssertSame(multiAlpha, GetCollectionItem(selectedItems, 0), "compiled multi-selection alpha selected item");
        AssertEqual(1, GetProperty(window, "MultiListBoxSelectionChangedCount"), "compiled multi-selection alpha count");
        AssertEqual("MultiSelectionEventListBox", GetProperty(window, "LastMultiListBoxSelectionSenderName"), "compiled multi-selection sender");
        AssertEqual("SelectionChanged", GetProperty(window, "LastMultiListBoxSelectionRoutedEventName"), "compiled multi-selection routed event");
        AssertEqual(1, GetProperty(window, "LastMultiListBoxSelectionAddedCount"), "compiled multi-selection alpha added count");
        AssertEqual(0, GetProperty(window, "LastMultiListBoxSelectionRemovedCount"), "compiled multi-selection alpha removed count");
        AssertEqual("multi alpha", GetProperty(window, "LastMultiListBoxSelectionAddedItem"), "compiled multi-selection alpha added item");

        SetProperty(multiBeta, "IsSelected", true);
        AssertCollectionCount(selectedItems, expected: 2, "compiled multi-selection beta selected items");
        AssertSame(multiAlpha, GetCollectionItem(selectedItems, 0), "compiled multi-selection retained alpha selected item");
        AssertSame(multiBeta, GetCollectionItem(selectedItems, 1), "compiled multi-selection beta selected item");
        AssertEqual(2, GetProperty(window, "MultiListBoxSelectionChangedCount"), "compiled multi-selection beta count");
        AssertEqual(1, GetProperty(window, "LastMultiListBoxSelectionAddedCount"), "compiled multi-selection beta added count");
        AssertEqual(0, GetProperty(window, "LastMultiListBoxSelectionRemovedCount"), "compiled multi-selection beta removed count");
        AssertEqual("multi beta", GetProperty(window, "LastMultiListBoxSelectionAddedItem"), "compiled multi-selection beta added item");

        SetProperty(multiAlpha, "IsSelected", false);
        AssertCollectionCount(selectedItems, expected: 1, "compiled multi-selection alpha removed selected items");
        AssertSame(multiBeta, GetCollectionItem(selectedItems, 0), "compiled multi-selection beta remains selected item");
        AssertEqual(3, GetProperty(window, "MultiListBoxSelectionChangedCount"), "compiled multi-selection alpha removed count");
        AssertEqual(0, GetProperty(window, "LastMultiListBoxSelectionAddedCount"), "compiled multi-selection alpha removed added count");
        AssertEqual(1, GetProperty(window, "LastMultiListBoxSelectionRemovedCount"), "compiled multi-selection alpha removed removed count");
        AssertEqual("multi alpha", GetProperty(window, "LastMultiListBoxSelectionRemovedItem"), "compiled multi-selection alpha removed item");
    }

    private static void ValidateListViewGridView(object window)
    {
        object dataContext = GetProperty(window, "DataContext");
        object sourceItems = GetProperty(dataContext, "Items");

        object listView = GetField(window, "GridItemsListView");
        AssertType(listView, "System.Windows.Controls.ListView", "compiled GridView ListView");
        AssertSame(sourceItems, GetProperty(listView, "ItemsSource"), "compiled GridView ListView ItemsSource binding");
        AssertCollectionCount(GetProperty(listView, "Items"), expected: 3, "compiled GridView ListView collection-change items");
        AssertSame(GetCollectionItem(sourceItems, 1), GetProperty(listView, "SelectedItem"), "compiled GridView ListView selected item");
        AssertEqual(1, GetProperty(listView, "SelectedIndex"), "compiled GridView ListView selected index");

        object gridView = GetProperty(listView, "View");
        AssertType(gridView, "System.Windows.Controls.GridView", "compiled GridView view");
        AssertEqual(false, GetProperty(gridView, "AllowsColumnReorder"), "compiled GridView column reorder setting");
        object columns = GetProperty(gridView, "Columns");
        AssertCollectionCount(columns, expected: 2, "compiled GridView columns");

        object nameColumn = GetCollectionItem(columns, 0);
        AssertType(nameColumn, "System.Windows.Controls.GridViewColumn", "compiled GridView name column");
        AssertEqual("Name", GetProperty(nameColumn, "Header"), "compiled GridView name column header");
        AssertEqual(120.0, GetProperty(nameColumn, "Width"), "compiled GridView name column width");
        AssertBindingObjectPath(GetProperty(nameColumn, "DisplayMemberBinding"), "Name", "compiled GridView name DisplayMemberBinding path");

        object categoryColumn = GetCollectionItem(columns, 1);
        AssertType(categoryColumn, "System.Windows.Controls.GridViewColumn", "compiled GridView category column");
        AssertEqual("Category", GetProperty(categoryColumn, "Header"), "compiled GridView category column header");
        AssertEqual(140.0, GetProperty(categoryColumn, "Width"), "compiled GridView category column width");
        AssertBindingObjectPath(GetProperty(categoryColumn, "DisplayMemberBinding"), "Category", "compiled GridView category DisplayMemberBinding path");

        SetProperty(listView, "SelectedIndex", 0);

        AssertSame(GetCollectionItem(sourceItems, 0), GetProperty(listView, "SelectedItem"), "compiled GridView ListView selected item after index update");
        AssertEqual(0, GetProperty(listView, "SelectedIndex"), "compiled GridView ListView selected index after update");
    }

    private static void ValidateDataGrid(object window)
    {
        object dataContext = GetProperty(window, "DataContext");
        object sourceItems = GetProperty(dataContext, "Items");

        object dataGrid = GetField(window, "ItemsDataGrid");
        AssertType(dataGrid, "System.Windows.Controls.DataGrid", "compiled DataGrid");
        AssertSame(sourceItems, GetProperty(dataGrid, "ItemsSource"), "compiled DataGrid ItemsSource binding");
        AssertCollectionCount(GetProperty(dataGrid, "Items"), expected: 3, "compiled DataGrid collection-change items");
        AssertEqual(false, GetProperty(dataGrid, "AutoGenerateColumns"), "compiled DataGrid auto-generate columns");
        AssertEqual(false, GetProperty(dataGrid, "CanUserAddRows"), "compiled DataGrid add rows");
        AssertEqual(true, GetProperty(dataGrid, "IsReadOnly"), "compiled DataGrid read-only state");
        AssertEqual("Horizontal", GetProperty(dataGrid, "GridLinesVisibility").ToString(), "compiled DataGrid grid-lines visibility");
        AssertEqual("Column", GetProperty(dataGrid, "HeadersVisibility").ToString(), "compiled DataGrid headers visibility");
        AssertEqual("IncludeHeader", GetProperty(dataGrid, "ClipboardCopyMode").ToString(), "compiled DataGrid clipboard copy mode");
        AssertBindingPath(dataGrid, "SelectedItemProperty", "SelectedItem", "compiled DataGrid SelectedItem binding path");
        AssertSame(GetCollectionItem(sourceItems, 0), GetProperty(dataGrid, "SelectedItem"), "compiled DataGrid initial selected item");

        object columns = GetProperty(dataGrid, "Columns");
        AssertCollectionCount(columns, expected: 3, "compiled DataGrid columns");
        object nameColumn = GetCollectionItem(columns, 0);
        AssertType(nameColumn, "System.Windows.Controls.DataGridTextColumn", "compiled DataGrid name column");
        AssertEqual("Name", GetProperty(nameColumn, "Header"), "compiled DataGrid name column header");
        AssertBindingObjectPath(GetProperty(nameColumn, "Binding"), "Name", "compiled DataGrid name binding path");
        AssertBindingObjectPath(GetProperty(nameColumn, "ClipboardContentBinding"), "Name", "compiled DataGrid name clipboard binding path");

        object categoryColumn = GetCollectionItem(columns, 1);
        AssertType(categoryColumn, "System.Windows.Controls.DataGridTextColumn", "compiled DataGrid category column");
        AssertEqual("Category", GetProperty(categoryColumn, "Header"), "compiled DataGrid category column header");
        AssertBindingObjectPath(GetProperty(categoryColumn, "Binding"), "Category", "compiled DataGrid category binding path");
        AssertBindingObjectPath(GetProperty(categoryColumn, "ClipboardContentBinding"), "Category", "compiled DataGrid category clipboard binding path");

        object activeColumn = GetCollectionItem(columns, 2);
        AssertType(activeColumn, "System.Windows.Controls.DataGridCheckBoxColumn", "compiled DataGrid active column");
        AssertEqual("Active", GetProperty(activeColumn, "Header"), "compiled DataGrid active column header");
        AssertBindingObjectPath(GetProperty(activeColumn, "Binding"), "IsActive", "compiled DataGrid active binding path");
        AssertBindingObjectPath(GetProperty(activeColumn, "ClipboardContentBinding"), "IsActive", "compiled DataGrid active clipboard binding path");
        AssertEqual(true, GetProperty(GetCollectionItem(sourceItems, 1), "IsActive"), "compiled DataGrid active item value");

        SetProperty(dataGrid, "SelectedIndex", 1);

        AssertSame(GetCollectionItem(sourceItems, 1), GetProperty(dataGrid, "SelectedItem"), "compiled DataGrid selected item after index update");
        AssertSame(GetCollectionItem(sourceItems, 1), GetProperty(dataContext, "SelectedItem"), "compiled DataGrid two-way selected item source update");
        ValidateDataGridClipboardContent(dataGrid, sourceItems, columns, window);
    }

    private static void ValidateDataGridClipboardContent(object dataGrid, object sourceItems, object columns, object window)
    {
        object selectedItem = GetCollectionItem(sourceItems, 1);

        object headerArgs = CreateDataGridRowClipboardEventArgs(dataGrid, item: null, startColumnDisplayIndex: 0, endColumnDisplayIndex: 2, isColumnHeadersRow: true);
        Invoke(dataGrid, "OnCopyingRowClipboardContent", headerArgs);

        AssertEqual(1, GetProperty(window, "DataGridClipboardRowEventCount"), "compiled DataGrid clipboard header event count");
        AssertEqual(1, GetProperty(window, "DataGridClipboardHeaderEventCount"), "compiled DataGrid clipboard header-row count");
        AssertEqual(3, GetProperty(window, "DataGridClipboardLastCellCount"), "compiled DataGrid clipboard header cell count");
        AssertEqual("Name", GetProperty(window, "LastDataGridClipboardFirstColumnHeader"), "compiled DataGrid clipboard first header");
        AssertEqual("Name\tCategory\tActive", GetProperty(window, "LastDataGridClipboardHeaderText"), "compiled DataGrid clipboard formatted header row");

        object rowArgs = CreateDataGridRowClipboardEventArgs(dataGrid, selectedItem, startColumnDisplayIndex: 0, endColumnDisplayIndex: 2, isColumnHeadersRow: false);
        Invoke(dataGrid, "OnCopyingRowClipboardContent", rowArgs);

        AssertEqual(2, GetProperty(window, "DataGridClipboardRowEventCount"), "compiled DataGrid clipboard row event count");
        AssertEqual(1, GetProperty(window, "DataGridClipboardHeaderEventCount"), "compiled DataGrid clipboard header count after row");
        AssertEqual(3, GetProperty(window, "DataGridClipboardLastCellCount"), "compiled DataGrid clipboard row cell count");
        AssertEqual("item beta", GetProperty(window, "LastDataGridClipboardItemName"), "compiled DataGrid clipboard row item");
        AssertEqual("item beta", GetProperty(window, "LastDataGridClipboardFirstCellContent"), "compiled DataGrid clipboard first cell");
        AssertEqual("secondary group", GetProperty(window, "LastDataGridClipboardSecondCellContent"), "compiled DataGrid clipboard second cell");
        AssertEqual("True", GetProperty(window, "LastDataGridClipboardThirdCellContent"), "compiled DataGrid clipboard third cell");
        AssertEqual("item beta\tsecondary group\tTrue", GetProperty(window, "LastDataGridClipboardRowText"), "compiled DataGrid clipboard formatted row");
    }

    private static void ValidatePostShowDataGridRows(Assembly presentationCore, object window)
    {
        object dataGrid = GetField(window, "ItemsDataGrid");
        object sourceItems = GetProperty(GetProperty(window, "DataContext"), "Items");
        object item = GetCollectionItem(sourceItems, 1);
        object columns = GetProperty(dataGrid, "Columns");
        object nameColumn = GetCollectionItem(columns, 0);
        object activeColumn = GetCollectionItem(columns, 2);

        Invoke(dataGrid, "ApplyTemplate");
        Invoke(dataGrid, "ScrollIntoView", item, activeColumn);
        Invoke(dataGrid, "UpdateLayout");

        object itemContainerGenerator = GetProperty(dataGrid, "ItemContainerGenerator");
        object row = Invoke(itemContainerGenerator, "ContainerFromItem", item);
        AssertType(row, "System.Windows.Controls.DataGridRow", "compiled DataGrid generated row");
        AssertSame(item, GetProperty(row, "Item"), "compiled DataGrid generated row item");
        AssertEqual(true, GetProperty(row, "IsSelected"), "compiled DataGrid generated row selected state");
        Invoke(row, "ApplyTemplate");
        Invoke(row, "UpdateLayout");
        Invoke(dataGrid, "UpdateLayout");

        object cellsPresenter = FindVisualDescendantByTypeName(
                presentationCore,
                row,
                "System.Windows.Controls.Primitives.DataGridCellsPresenter")
            ?? throw new InvalidOperationException("Expected compiled DataGrid row to generate a DataGridCellsPresenter.");
        Invoke(cellsPresenter, "ApplyTemplate");
        Invoke(cellsPresenter, "UpdateLayout");
        Invoke(dataGrid, "UpdateLayout");

        object nameCellContent = InvokeDataGridColumnGetCellContent(nameColumn, row);
        AssertType(nameCellContent, "System.Windows.Controls.TextBlock", "compiled DataGrid generated name cell content");
        AssertEqual("item beta", GetProperty(nameCellContent, "Text"), "compiled DataGrid generated name cell text");

        object activeCellContent = InvokeDataGridColumnGetCellContent(activeColumn, row);
        AssertType(activeCellContent, "System.Windows.Controls.CheckBox", "compiled DataGrid generated active cell content");
        AssertEqual(true, GetProperty(activeCellContent, "IsChecked"), "compiled DataGrid generated active cell value");
    }

    private static void ValidateImplicitDataTemplate(object window)
    {
        object dataContext = GetProperty(window, "DataContext");
        object detail = GetProperty(dataContext, "Detail");
        AssertType(detail, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeDetail", "compiled implicit DataTemplate detail model");
        AssertEqual("detail from implicit template", GetProperty(detail, "Title"), "compiled implicit DataTemplate detail title");

        object implicitTemplateHost = GetField(window, "ImplicitTemplateHost");
        AssertType(implicitTemplateHost, "System.Windows.Controls.ContentControl", "compiled implicit DataTemplate host");
        AssertSame(detail, GetProperty(implicitTemplateHost, "Content"), "compiled implicit DataTemplate host content binding");
    }

    private static void ValidateContentTemplateSelector(object window)
    {
        object dataContext = GetProperty(window, "DataContext");
        object detail = GetProperty(dataContext, "Detail");
        object selectedTemplate = Invoke(window, "TryFindResource", "SelectedDetailTemplate");
        AssertType(selectedTemplate, "System.Windows.DataTemplate", "compiled ContentTemplateSelector selected template resource");
        object selectedTemplateRoot = Invoke(selectedTemplate, "LoadContent");
        AssertType(selectedTemplateRoot, "System.Windows.Controls.TextBlock", "compiled ContentTemplateSelector selected template root");
        AssertEqual("SelectedDetailTextBlock", GetProperty(selectedTemplateRoot, "Name"), "compiled ContentTemplateSelector selected template named root");
        AssertEqual("content template selector selected", GetProperty(selectedTemplateRoot, "Tag"), "compiled ContentTemplateSelector selected template tag");
        AssertBindingPath(selectedTemplateRoot, "TextProperty", "Title", "compiled ContentTemplateSelector selected binding path");

        object fallbackTemplate = Invoke(window, "TryFindResource", "FallbackDetailTemplate");
        AssertType(fallbackTemplate, "System.Windows.DataTemplate", "compiled ContentTemplateSelector fallback template resource");
        object fallbackTemplateRoot = Invoke(fallbackTemplate, "LoadContent");
        AssertType(fallbackTemplateRoot, "System.Windows.Controls.TextBlock", "compiled ContentTemplateSelector fallback template root");
        AssertEqual("SelectedDetailTextBlock", GetProperty(fallbackTemplateRoot, "Name"), "compiled ContentTemplateSelector fallback template named root");
        AssertEqual("content template selector fallback", GetProperty(fallbackTemplateRoot, "Tag"), "compiled ContentTemplateSelector fallback template tag");
        AssertBindingPath(fallbackTemplateRoot, "TextProperty", "Title", "compiled ContentTemplateSelector fallback binding path");

        object selector = Invoke(window, "TryFindResource", "SmokeDetailTemplateSelector");
        AssertType(selector, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeDetailTemplateSelector", "compiled ContentTemplateSelector resource");
        AssertSame(selectedTemplate, GetProperty(selector, "SelectedTemplate"), "compiled ContentTemplateSelector selected template property");
        AssertSame(fallbackTemplate, GetProperty(selector, "FallbackTemplate"), "compiled ContentTemplateSelector fallback template property");

        object selectorTemplateHost = GetField(window, "SelectorTemplateHost");
        AssertType(selectorTemplateHost, "System.Windows.Controls.ContentControl", "compiled ContentTemplateSelector host");
        AssertSame(detail, GetProperty(selectorTemplateHost, "Content"), "compiled ContentTemplateSelector host content binding");
        AssertSame(selector, GetProperty(selectorTemplateHost, "ContentTemplateSelector"), "compiled ContentControl ContentTemplateSelector binding");
    }

    private static void ValidateHierarchicalDataTemplate(object window)
    {
        object dataContext = GetProperty(window, "DataContext");
        object sourceNodes = GetProperty(dataContext, "Nodes");
        AssertCollectionCount(sourceNodes, expected: 1, "view-model hierarchical nodes");
        object rootNode = GetCollectionItem(sourceNodes, 0);
        AssertType(rootNode, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeNode", "compiled hierarchical root model");
        AssertEqual("root node", GetProperty(rootNode, "Name"), "compiled hierarchical root model name");
        object rootChildren = GetProperty(rootNode, "Children");
        AssertCollectionCount(rootChildren, expected: 2, "compiled hierarchical root child models");
        AssertEqual("child alpha", GetProperty(GetCollectionItem(rootChildren, 0), "Name"), "compiled hierarchical first child model name");

        object nodeTemplate = Invoke(window, "TryFindResource", "SmokeNodeTemplate");
        AssertType(nodeTemplate, "System.Windows.HierarchicalDataTemplate", "compiled HierarchicalDataTemplate resource");
        AssertBindingObjectPath(GetProperty(nodeTemplate, "ItemsSource"), "Children", "compiled HierarchicalDataTemplate child ItemsSource path");
        object nodeTemplateRoot = Invoke(nodeTemplate, "LoadContent");
        AssertType(nodeTemplateRoot, "System.Windows.Controls.TextBlock", "compiled HierarchicalDataTemplate root");
        AssertEqual("NodeTextBlock", GetProperty(nodeTemplateRoot, "Name"), "compiled HierarchicalDataTemplate named root");
        AssertEqual("hierarchical template", GetProperty(nodeTemplateRoot, "Tag"), "compiled HierarchicalDataTemplate root tag");
        AssertBindingPath(nodeTemplateRoot, "TextProperty", "Name", "compiled HierarchicalDataTemplate text binding path");

        object nodeTree = GetField(window, "NodeTree");
        AssertType(nodeTree, "System.Windows.Controls.TreeView", "compiled hierarchical TreeView");
        AssertSame(sourceNodes, GetProperty(nodeTree, "ItemsSource"), "compiled TreeView ItemsSource binding");
        AssertSame(nodeTemplate, GetProperty(nodeTree, "ItemTemplate"), "compiled TreeView item template");
        AssertCollectionCount(GetProperty(nodeTree, "Items"), expected: 1, "compiled TreeView generated root items");
    }

    private static void ValidateExplicitTreeViewItems(object window)
    {
        object tree = GetField(window, "ExplicitTree");
        AssertType(tree, "System.Windows.Controls.TreeView", "compiled explicit TreeView");
        object treeItems = GetProperty(tree, "Items");
        AssertCollectionCount(treeItems, expected: 2, "compiled explicit TreeView items");

        object alpha = GetField(window, "ExplicitTreeAlpha");
        object alphaChild = GetField(window, "ExplicitTreeAlphaChild");
        object beta = GetField(window, "ExplicitTreeBeta");
        AssertType(alpha, "System.Windows.Controls.TreeViewItem", "compiled explicit alpha TreeViewItem");
        AssertType(alphaChild, "System.Windows.Controls.TreeViewItem", "compiled explicit alpha child TreeViewItem");
        AssertType(beta, "System.Windows.Controls.TreeViewItem", "compiled explicit beta TreeViewItem");
        AssertEqual("explicit alpha", GetProperty(alpha, "Header"), "compiled explicit alpha TreeViewItem header");
        AssertEqual("explicit alpha child", GetProperty(alphaChild, "Header"), "compiled explicit alpha child TreeViewItem header");
        AssertEqual("explicit beta", GetProperty(beta, "Header"), "compiled explicit beta TreeViewItem header");
        AssertCollectionCount(GetProperty(alpha, "Items"), expected: 1, "compiled explicit alpha TreeViewItem children");
        AssertSame(alphaChild, GetCollectionItem(GetProperty(alpha, "Items"), 0), "compiled explicit alpha child item");
        AssertEqual(false, GetProperty(alpha, "IsExpanded"), "compiled explicit alpha initial expanded state");
        AssertEqual(false, GetProperty(alpha, "IsSelected"), "compiled explicit alpha initial selected state");
        AssertEqual(false, GetProperty(beta, "IsSelected"), "compiled explicit beta initial selected state");

        SetProperty(alpha, "IsExpanded", true);
        AssertEqual(true, GetProperty(alpha, "IsExpanded"), "compiled explicit alpha expanded state");
        AssertEqual(1, GetProperty(window, "ExplicitTreeExpandedCount"), "compiled TreeViewItem Expanded count");
        AssertEqual("ExplicitTreeAlpha", GetProperty(window, "LastExplicitTreeExpandedSenderName"), "compiled TreeViewItem Expanded sender");
        AssertEqual("Expanded", GetProperty(window, "LastExplicitTreeExpandedRoutedEventName"), "compiled TreeViewItem Expanded routed event");

        SetProperty(alpha, "IsExpanded", false);
        AssertEqual(false, GetProperty(alpha, "IsExpanded"), "compiled explicit alpha collapsed state");
        AssertEqual(1, GetProperty(window, "ExplicitTreeCollapsedCount"), "compiled TreeViewItem Collapsed count");
        AssertEqual("ExplicitTreeAlpha", GetProperty(window, "LastExplicitTreeCollapsedSenderName"), "compiled TreeViewItem Collapsed sender");
        AssertEqual("Collapsed", GetProperty(window, "LastExplicitTreeCollapsedRoutedEventName"), "compiled TreeViewItem Collapsed routed event");

        SetProperty(alpha, "IsSelected", true);
        AssertEqual(true, GetProperty(alpha, "IsSelected"), "compiled explicit alpha selected state");
        AssertEqual(false, GetProperty(beta, "IsSelected"), "compiled explicit beta unselected after alpha selection");
        AssertSame(alpha, GetProperty(tree, "SelectedItem"), "compiled explicit TreeView selected alpha item");
        AssertEqual(1, GetProperty(window, "ExplicitTreeSelectedCount"), "compiled TreeViewItem alpha Selected count");
        AssertEqual("ExplicitTreeAlpha", GetProperty(window, "LastExplicitTreeSelectedSenderName"), "compiled TreeViewItem alpha Selected sender");
        AssertEqual("Selected", GetProperty(window, "LastExplicitTreeSelectedRoutedEventName"), "compiled TreeViewItem alpha Selected routed event");

        SetProperty(beta, "IsSelected", true);
        AssertEqual(false, GetProperty(alpha, "IsSelected"), "compiled explicit alpha unselected by TreeView manager");
        AssertEqual(true, GetProperty(beta, "IsSelected"), "compiled explicit beta selected state");
        AssertSame(beta, GetProperty(tree, "SelectedItem"), "compiled explicit TreeView selected beta item");
        AssertEqual(2, GetProperty(window, "ExplicitTreeSelectedCount"), "compiled TreeViewItem beta Selected count");
        AssertEqual(1, GetProperty(window, "ExplicitTreeUnselectedCount"), "compiled TreeViewItem alpha Unselected count");
        AssertEqual("ExplicitTreeBeta", GetProperty(window, "LastExplicitTreeSelectedSenderName"), "compiled TreeViewItem beta Selected sender");
        AssertEqual("ExplicitTreeAlpha", GetProperty(window, "LastExplicitTreeUnselectedSenderName"), "compiled TreeViewItem alpha Unselected sender");
        AssertEqual("Unselected", GetProperty(window, "LastExplicitTreeUnselectedRoutedEventName"), "compiled TreeViewItem alpha Unselected routed event");
    }

    private static void ValidateTabControl(object window)
    {
        object tabControl = GetField(window, "SmokeTabControl");
        AssertType(tabControl, "System.Windows.Controls.TabControl", "compiled TabControl");
        AssertEqual(1, GetProperty(tabControl, "SelectedIndex"), "compiled TabControl selected index");

        object items = GetProperty(tabControl, "Items");
        AssertCollectionCount(items, expected: 2, "compiled TabControl items");

        object alphaTab = GetCollectionItem(items, 0);
        AssertType(alphaTab, "System.Windows.Controls.TabItem", "compiled TabControl alpha tab");
        AssertEqual("alpha tab", GetProperty(alphaTab, "Header"), "compiled TabControl alpha header");
        object alphaContent = GetProperty(alphaTab, "Content");
        AssertType(alphaContent, "System.Windows.Controls.TextBlock", "compiled TabControl alpha content");
        AssertEqual("AlphaTabContent", GetProperty(alphaContent, "Name"), "compiled TabControl alpha content name");
        AssertEqual("alpha tab content", GetProperty(alphaContent, "Text"), "compiled TabControl alpha content text");
        AssertEqual("tab alpha content", GetProperty(alphaContent, "Tag"), "compiled TabControl alpha content tag");

        object betaTab = GetCollectionItem(items, 1);
        AssertType(betaTab, "System.Windows.Controls.TabItem", "compiled TabControl beta tab");
        AssertEqual("beta tab", GetProperty(betaTab, "Header"), "compiled TabControl beta header");
        object betaContent = GetProperty(betaTab, "Content");
        AssertType(betaContent, "System.Windows.Controls.TextBlock", "compiled TabControl beta content");
        AssertEqual("BetaTabContent", GetProperty(betaContent, "Name"), "compiled TabControl beta content name");
        AssertEqual("beta tab content", GetProperty(betaContent, "Text"), "compiled TabControl beta content text");
        AssertEqual("tab beta content", GetProperty(betaContent, "Tag"), "compiled TabControl beta content tag");

        AssertSame(betaTab, GetProperty(tabControl, "SelectedItem"), "compiled TabControl selected item");
        AssertSame(betaContent, GetProperty(tabControl, "SelectedContent"), "compiled TabControl selected content");
    }

    private static void ValidateSectionControls(object window)
    {
        object dataContext = GetProperty(window, "DataContext");
        object detail = GetProperty(dataContext, "Detail");

        object expanderHeaderTemplate = Invoke(window, "TryFindResource", "ExpanderHeaderTemplate");
        AssertType(expanderHeaderTemplate, "System.Windows.DataTemplate", "compiled Expander HeaderTemplate resource");
        object expanderHeaderRoot = Invoke(expanderHeaderTemplate, "LoadContent");
        AssertType(expanderHeaderRoot, "System.Windows.Controls.TextBlock", "compiled Expander HeaderTemplate root");
        AssertEqual("ExpanderHeaderTextBlock", GetProperty(expanderHeaderRoot, "Name"), "compiled Expander HeaderTemplate named root");
        AssertEqual("expander header template", GetProperty(expanderHeaderRoot, "Tag"), "compiled Expander HeaderTemplate root tag");
        AssertBindingPath(expanderHeaderRoot, "TextProperty", "Title", "compiled Expander HeaderTemplate binding path");

        object groupBoxHeaderTemplate = Invoke(window, "TryFindResource", "GroupBoxHeaderTemplate");
        AssertType(groupBoxHeaderTemplate, "System.Windows.DataTemplate", "compiled GroupBox HeaderTemplate resource");
        object groupBoxHeaderRoot = Invoke(groupBoxHeaderTemplate, "LoadContent");
        AssertType(groupBoxHeaderRoot, "System.Windows.Controls.TextBlock", "compiled GroupBox HeaderTemplate root");
        AssertEqual("GroupBoxHeaderTextBlock", GetProperty(groupBoxHeaderRoot, "Name"), "compiled GroupBox HeaderTemplate named root");
        AssertEqual("group box header template", GetProperty(groupBoxHeaderRoot, "Tag"), "compiled GroupBox HeaderTemplate root tag");
        AssertBindingPath(groupBoxHeaderRoot, "TextProperty", "Title", "compiled GroupBox HeaderTemplate binding path");

        object expander = GetField(window, "SmokeExpander");
        AssertType(expander, "System.Windows.Controls.Expander", "compiled Expander");
        AssertSame(detail, GetProperty(expander, "Header"), "compiled Expander header binding");
        AssertSame(expanderHeaderTemplate, GetProperty(expander, "HeaderTemplate"), "compiled Expander HeaderTemplate binding");
        AssertEqual(true, GetProperty(expander, "IsExpanded"), "compiled Expander expanded state");
        object expanderContent = GetProperty(expander, "Content");
        AssertType(expanderContent, "System.Windows.Controls.TextBlock", "compiled Expander content");
        AssertEqual("ExpanderContentText", GetProperty(expanderContent, "Name"), "compiled Expander content name");
        AssertEqual("expander content", GetProperty(expanderContent, "Tag"), "compiled Expander content tag");
        AssertBindingPath(expanderContent, "TextProperty", "Greeting", "compiled Expander content binding path");

        object groupBox = GetField(window, "SmokeGroupBox");
        AssertType(groupBox, "System.Windows.Controls.GroupBox", "compiled GroupBox");
        AssertSame(detail, GetProperty(groupBox, "Header"), "compiled GroupBox header binding");
        AssertSame(groupBoxHeaderTemplate, GetProperty(groupBox, "HeaderTemplate"), "compiled GroupBox HeaderTemplate binding");
        object groupContent = GetProperty(groupBox, "Content");
        AssertType(groupContent, "System.Windows.Controls.TextBlock", "compiled GroupBox content");
        AssertEqual("GroupBoxContentText", GetProperty(groupContent, "Name"), "compiled GroupBox content name");
        AssertEqual("group box content", GetProperty(groupContent, "Tag"), "compiled GroupBox content tag");
        AssertBindingPath(groupContent, "TextProperty", "ButtonText", "compiled GroupBox content binding path");
    }

    private static void ValidateAdornerDecorator(object window)
    {
        object decorator = GetField(window, "SmokeAdornerDecorator");
        AssertType(decorator, "System.Windows.Documents.AdornerDecorator", "compiled AdornerDecorator");

        object adornedButton = GetField(window, "AdornedButton");
        AssertType(adornedButton, "System.Windows.Controls.Button", "compiled adorned Button");
        AssertSame(adornedButton, GetProperty(decorator, "Child"), "compiled AdornerDecorator child");
        AssertEqual("adorned button", GetProperty(adornedButton, "Content"), "compiled adorned Button content");
        AssertEqual("adorned button", GetProperty(adornedButton, "Tag"), "compiled adorned Button tag");
    }

    private static void ValidatePostShowAdornerLayer(Assembly presentationFramework, Assembly compilerHarness, object window)
    {
        object adornedButton = GetField(window, "AdornedButton");
        Type adornerLayerType = GetRequiredType(presentationFramework, "System.Windows.Documents.AdornerLayer");
        object adornerLayer = InvokeStatic(adornerLayerType, "GetAdornerLayer", adornedButton);
        AssertType(adornerLayer, "System.Windows.Documents.AdornerLayer", "compiled AdornerLayer");

        object adorner = Create(compilerHarness, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeAdorner", adornedButton);
        AssertType(adorner, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeAdorner", "compiled SmokeAdorner");
        AssertSame(adornedButton, GetProperty(adorner, "AdornedElement"), "compiled SmokeAdorner adorned element");
        AssertEqual(false, GetProperty(adorner, "IsHitTestVisible"), "compiled SmokeAdorner hit testing");

        Invoke(adornerLayer, "Add", adorner);
        object adorners = Invoke(adornerLayer, "GetAdorners", adornedButton);
        AssertCollectionCount(adorners, expected: 1, "compiled AdornerLayer adorners");
        AssertSame(adorner, GetCollectionItem(adorners, 0), "compiled AdornerLayer added adorner");

        Invoke(adornerLayer, "Remove", adorner);
    }

    private static void ValidateAccessKeyFocusScope(Assembly presentationCore, object window)
    {
        object focusScope = GetField(window, "AccessKeyFocusScope");
        AssertType(focusScope, "System.Windows.Controls.StackPanel", "compiled access-key focus scope");

        object accessLabel = GetField(window, "AccessTargetLabel");
        AssertType(accessLabel, "System.Windows.Controls.Label", "compiled access-key Label");
        AssertEqual("_Access target", GetProperty(accessLabel, "Content"), "compiled access-key Label content");

        object accessTarget = GetField(window, "AccessTargetBox");
        AssertType(accessTarget, "System.Windows.Controls.TextBox", "compiled access-key target TextBox");
        AssertEqual("access target", GetProperty(accessTarget, "Text"), "compiled access-key target text");

        object alternateAccessTarget = GetField(window, "AlternateAccessTargetBox");
        AssertType(alternateAccessTarget, "System.Windows.Controls.TextBox", "compiled alternate access-key target TextBox");
        AssertEqual("alternate access target", GetProperty(alternateAccessTarget, "Text"), "compiled alternate access-key target text");

        object accessText = GetField(window, "StandaloneAccessText");
        AssertType(accessText, "System.Windows.Controls.AccessText", "compiled standalone AccessText");
        AssertEqual("_Standalone access text", GetProperty(accessText, "Text"), "compiled standalone AccessText text");

        Type focusManagerType = GetRequiredType(presentationCore, "System.Windows.Input.FocusManager");
        AssertEqual(true, InvokeStatic(focusManagerType, "GetIsFocusScope", focusScope), "compiled FocusManager focus scope");
    }

    private static void ValidateDependencyPropertyCore(object window)
    {
        object scope = GetField(window, "DependencyPropertyScopePanel");
        AssertType(scope, "System.Windows.Controls.StackPanel", "compiled dependency-property scope panel");
        AssertEqual(3, GetProperty(GetProperty(scope, "Children"), "Count"), "compiled dependency-property scope child count");

        object target = GetField(window, "DependencyPropertyTarget");
        AssertType(target, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeDependencyPropertyControl", "compiled dependency-property target");
        AssertSame(target, GetCollectionItem(GetProperty(scope, "Children"), 0), "compiled dependency-property scope child");
        AssertEqual(10, GetProperty(target, "CoercedLevel"), "compiled coerced dependency property value");
        AssertEqual(1, GetProperty(target, "CoercedLevelChangedCount"), "compiled coerced dependency property initial change count");
        AssertEqual(0, GetProperty(target, "LastOldCoercedLevel"), "compiled coerced dependency property initial old value");
        AssertEqual(10, GetProperty(target, "LastNewCoercedLevel"), "compiled coerced dependency property initial new value");

        Assembly compilerHarness = target.GetType().Assembly;
        Type dependencyPropertiesType = GetRequiredType(
            compilerHarness,
            "ProGPU.Wpf.RealXamlCompilerHarness.SmokeDependencyProperties");
        AssertEqual("inherited smoke", InvokeStatic(dependencyPropertiesType, "GetInheritedLabel", scope), "compiled inherited attached property scope value");
        AssertEqual("inherited smoke", InvokeStatic(dependencyPropertiesType, "GetInheritedLabel", target), "compiled inherited attached property target value");

        InvokeStatic(dependencyPropertiesType, "SetInheritedLabel", scope, "updated inherited smoke");
        AssertEqual("updated inherited smoke", InvokeStatic(dependencyPropertiesType, "GetInheritedLabel", target), "compiled inherited attached property updated target value");

        InvokeStatic(dependencyPropertiesType, "SetInheritedLabel", target, "local inherited smoke");
        InvokeStatic(dependencyPropertiesType, "SetInheritedLabel", scope, "parent ignored by local value");
        AssertEqual("local inherited smoke", InvokeStatic(dependencyPropertiesType, "GetInheritedLabel", target), "compiled inherited attached property local precedence");

        SetProperty(target, "CoercedLevel", -4);
        AssertEqual(0, GetProperty(target, "CoercedLevel"), "compiled coerced dependency property minimum value");
        AssertEqual(2, GetProperty(target, "CoercedLevelChangedCount"), "compiled coerced dependency property minimum change count");
        AssertEqual(10, GetProperty(target, "LastOldCoercedLevel"), "compiled coerced dependency property minimum old value");
        AssertEqual(0, GetProperty(target, "LastNewCoercedLevel"), "compiled coerced dependency property minimum new value");

        SetProperty(target, "CoercedLevel", 7);
        AssertEqual(7, GetProperty(target, "CoercedLevel"), "compiled coerced dependency property mid value");
        AssertEqual(3, GetProperty(target, "CoercedLevelChangedCount"), "compiled coerced dependency property mid change count");
        AssertEqual(0, GetProperty(target, "LastOldCoercedLevel"), "compiled coerced dependency property mid old value");
        AssertEqual(7, GetProperty(target, "LastNewCoercedLevel"), "compiled coerced dependency property mid new value");

        object ownerTarget = GetField(window, "DependencyPropertyOwnerTarget");
        AssertType(ownerTarget, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeDependencyPropertyOwnerControl", "compiled dependency-property owner target");
        AssertSame(ownerTarget, GetCollectionItem(GetProperty(scope, "Children"), 1), "compiled dependency-property owner child");
        AssertEqual(30, GetProperty(ownerTarget, "OwnerLevel"), "compiled AddOwner dependency property coerced value");
        AssertEqual(1, GetProperty(ownerTarget, "OwnerLevelChangedCount"), "compiled AddOwner dependency property initial change count");
        AssertEqual(24, GetProperty(ownerTarget, "LastOldOwnerLevel"), "compiled AddOwner dependency property initial old value");
        AssertEqual(30, GetProperty(ownerTarget, "LastNewOwnerLevel"), "compiled AddOwner dependency property initial new value");
        AssertEqual(true, GetProperty(ownerTarget, "HasOwnerLevelLocalValue"), "compiled AddOwner dependency property local value");
        AssertEqual("Local", GetProperty(ownerTarget, "OwnerLevelBaseValueSource"), "compiled AddOwner dependency property local value source");
        AssertEqual(true, GetProperty(ownerTarget, "IsOwnerLevelCoerced"), "compiled AddOwner dependency property coerced source");
        AssertEqual(false, GetProperty(ownerTarget, "IsOwnerLevelCurrent"), "compiled AddOwner dependency property current source before SetCurrentValue");

        Invoke(ownerTarget, "ClearOwnerLevelValue");
        AssertEqual(24, GetProperty(ownerTarget, "OwnerLevel"), "compiled ClearValue dependency property metadata default");
        AssertEqual(2, GetProperty(ownerTarget, "OwnerLevelChangedCount"), "compiled ClearValue dependency property change count");
        AssertEqual(30, GetProperty(ownerTarget, "LastOldOwnerLevel"), "compiled ClearValue dependency property old value");
        AssertEqual(24, GetProperty(ownerTarget, "LastNewOwnerLevel"), "compiled ClearValue dependency property new value");
        AssertEqual(false, GetProperty(ownerTarget, "HasOwnerLevelLocalValue"), "compiled ClearValue dependency property local value removed");
        AssertEqual("Default", GetProperty(ownerTarget, "OwnerLevelBaseValueSource"), "compiled ClearValue dependency property default source");
        AssertEqual(false, GetProperty(ownerTarget, "IsOwnerLevelCoerced"), "compiled ClearValue dependency property coerced source");

        Invoke(ownerTarget, "SetCurrentOwnerLevel", 28);
        AssertEqual(28, GetProperty(ownerTarget, "OwnerLevel"), "compiled SetCurrentValue dependency property value");
        AssertEqual(3, GetProperty(ownerTarget, "OwnerLevelChangedCount"), "compiled SetCurrentValue dependency property change count");
        AssertEqual(24, GetProperty(ownerTarget, "LastOldOwnerLevel"), "compiled SetCurrentValue dependency property old value");
        AssertEqual(28, GetProperty(ownerTarget, "LastNewOwnerLevel"), "compiled SetCurrentValue dependency property new value");
        AssertEqual(true, GetProperty(ownerTarget, "HasOwnerLevelLocalValue"), "compiled SetCurrentValue dependency property local value");
        AssertEqual("Default", GetProperty(ownerTarget, "OwnerLevelBaseValueSource"), "compiled SetCurrentValue dependency property base source");
        AssertEqual(true, GetProperty(ownerTarget, "IsOwnerLevelCurrent"), "compiled SetCurrentValue dependency property current source");

        object metadataTarget = GetField(window, "DependencyPropertyMetadataTarget");
        AssertType(metadataTarget, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeOverrideMetadataControl", "compiled dependency-property metadata target");
        AssertSame(metadataTarget, GetCollectionItem(GetProperty(scope, "Children"), 2), "compiled dependency-property metadata child");
        AssertEqual("override metadata label", GetProperty(metadataTarget, "ModeLabel"), "compiled OverrideMetadata dependency property default");
        AssertEqual("Default", GetProperty(metadataTarget, "ModeLabelBaseValueSource"), "compiled OverrideMetadata dependency property value source");
    }

    private static void ValidateCustomRoutedEvent(object window)
    {
        object scope = GetField(window, "CustomRoutedEventScopePanel");
        AssertType(scope, "System.Windows.Controls.StackPanel", "compiled custom routed event scope panel");
        AssertEqual(1, GetProperty(GetProperty(scope, "Children"), "Count"), "compiled custom routed event scope child count");

        object source = GetField(window, "CustomRoutedEventSource");
        AssertType(source, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeRoutedEventSource", "compiled custom routed event source");
        AssertSame(source, GetCollectionItem(GetProperty(scope, "Children"), 0), "compiled custom routed event scope child");
        AssertEqual(0, GetProperty(window, "CustomRoutedEventSourceCount"), "compiled custom routed event source initial count");
        AssertEqual(0, GetProperty(window, "CustomRoutedEventScopeCount"), "compiled custom routed event scope initial count");

        object args = Invoke(source, "RaiseSmokeBubbled", "compiled routed payload");
        AssertType(args, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeRoutedEventArgs", "compiled custom routed event args");
        AssertEqual("compiled routed payload", GetProperty(args, "Payload"), "compiled custom routed event payload");
        AssertEqual(true, GetProperty(args, "Handled"), "compiled custom routed event final handled state");
        AssertEqual(1, GetProperty(window, "CustomRoutedEventSourceCount"), "compiled custom routed event source count");
        AssertEqual(1, GetProperty(window, "CustomRoutedEventScopeCount"), "compiled custom routed event scope count");
        AssertEqual("CustomRoutedEventSource", GetProperty(window, "LastCustomRoutedEventSourceSenderName"), "compiled custom routed event source sender");
        AssertEqual("CustomRoutedEventSource", GetProperty(window, "LastCustomRoutedEventSourceOriginalSourceName"), "compiled custom routed event source original source");
        AssertEqual("SmokeBubbled", GetProperty(window, "LastCustomRoutedEventSourceRoutedEventName"), "compiled custom routed event source routed event");
        AssertEqual(false, GetProperty(window, "LastCustomRoutedEventSourceHandled"), "compiled custom routed event source handled state");
        AssertEqual("compiled routed payload", GetProperty(window, "LastCustomRoutedEventSourcePayload"), "compiled custom routed event source payload");
        AssertEqual("CustomRoutedEventScopePanel", GetProperty(window, "LastCustomRoutedEventScopeSenderName"), "compiled custom routed event scope sender");
        AssertEqual("CustomRoutedEventSource", GetProperty(window, "LastCustomRoutedEventScopeOriginalSourceName"), "compiled custom routed event scope original source");
        AssertEqual("SmokeBubbled", GetProperty(window, "LastCustomRoutedEventScopeRoutedEventName"), "compiled custom routed event scope routed event");
        AssertEqual(false, GetProperty(window, "LastCustomRoutedEventScopeHandled"), "compiled custom routed event scope handled state");
        AssertEqual("compiled routed payload", GetProperty(window, "LastCustomRoutedEventScopePayload"), "compiled custom routed event scope payload");
    }

    private static void ValidateClassRoutedEvent(object window)
    {
        object scope = GetField(window, "ClassRoutedEventScopePanel");
        AssertType(scope, "System.Windows.Controls.StackPanel", "compiled class routed event scope panel");
        AssertEqual(1, GetProperty(GetProperty(scope, "Children"), "Count"), "compiled class routed event scope child count");

        object source = GetField(window, "ClassRoutedEventSource");
        AssertType(source, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeClassRoutedEventSource", "compiled class routed event source");
        AssertSame(source, GetCollectionItem(GetProperty(scope, "Children"), 0), "compiled class routed event scope child");
        AssertEqual(0, GetProperty(source, "ClassHandlerCount"), "compiled class routed event class handler initial count");
        AssertEqual(0, GetProperty(window, "ClassRoutedEventSourceCount"), "compiled class routed event source initial count");
        AssertEqual(0, GetProperty(window, "ClassRoutedEventScopeCount"), "compiled class routed event scope initial count");
        AssertEqual(0, GetProperty(window, "ClassRoutedEventHandledTooScopeCount"), "compiled class routed event handled-too initial count");

        object args = Invoke(source, "RaiseSmokeClassBubbled", "compiled class routed payload");
        AssertType(args, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeRoutedEventArgs", "compiled class routed event args");
        AssertEqual("compiled class routed payload", GetProperty(args, "Payload"), "compiled class routed event payload");
        AssertEqual(true, GetProperty(args, "Handled"), "compiled class routed event final handled state");
        AssertEqual(1, GetProperty(source, "ClassHandlerCount"), "compiled class routed event class handler count");
        AssertEqual("ClassRoutedEventSource", GetProperty(source, "LastClassHandlerSenderName"), "compiled class routed event class handler sender");
        AssertEqual("ClassRoutedEventSource", GetProperty(source, "LastClassHandlerOriginalSourceName"), "compiled class routed event class handler original source");
        AssertEqual("SmokeClassBubbled", GetProperty(source, "LastClassHandlerRoutedEventName"), "compiled class routed event class handler routed event");
        AssertEqual(false, GetProperty(source, "LastClassHandlerHandled"), "compiled class routed event class handler handled state");
        AssertEqual("compiled class routed payload", GetProperty(source, "LastClassHandlerPayload"), "compiled class routed event class handler payload");
        AssertEqual(1, GetProperty(window, "ClassRoutedEventSourceCount"), "compiled class routed event source count");
        AssertEqual(0, GetProperty(window, "ClassRoutedEventScopeCount"), "compiled class routed event skipped normal scope count");
        AssertEqual(1, GetProperty(window, "ClassRoutedEventHandledTooScopeCount"), "compiled class routed event handled-too scope count");
        AssertEqual("ClassRoutedEventSource", GetProperty(window, "LastClassRoutedEventSourceSenderName"), "compiled class routed event source sender");
        AssertEqual("ClassRoutedEventSource", GetProperty(window, "LastClassRoutedEventSourceOriginalSourceName"), "compiled class routed event source original source");
        AssertEqual("SmokeClassBubbled", GetProperty(window, "LastClassRoutedEventSourceRoutedEventName"), "compiled class routed event source routed event");
        AssertEqual(false, GetProperty(window, "LastClassRoutedEventSourceHandled"), "compiled class routed event source handled state");
        AssertEqual("compiled class routed payload", GetProperty(window, "LastClassRoutedEventSourcePayload"), "compiled class routed event source payload");
        AssertEqual("ClassRoutedEventScopePanel", GetProperty(window, "LastClassRoutedEventHandledTooScopeSenderName"), "compiled class routed event handled-too scope sender");
        AssertEqual("ClassRoutedEventSource", GetProperty(window, "LastClassRoutedEventHandledTooScopeOriginalSourceName"), "compiled class routed event handled-too scope original source");
        AssertEqual("SmokeClassBubbled", GetProperty(window, "LastClassRoutedEventHandledTooScopeRoutedEventName"), "compiled class routed event handled-too scope routed event");
        AssertEqual(true, GetProperty(window, "LastClassRoutedEventHandledTooScopeHandled"), "compiled class routed event handled-too scope handled state");
        AssertEqual("compiled class routed payload", GetProperty(window, "LastClassRoutedEventHandledTooScopePayload"), "compiled class routed event handled-too scope payload");
    }

    private static void ValidatePostShowAccessKeyFocusScope(Assembly presentationCore, object window)
    {
        Invoke(window, "UpdateLayout");

        object focusScope = GetField(window, "AccessKeyFocusScope");
        object accessLabel = GetField(window, "AccessTargetLabel");
        object accessTarget = GetField(window, "AccessTargetBox");
        object alternateAccessTarget = GetField(window, "AlternateAccessTargetBox");
        AssertSame(accessTarget, GetProperty(accessLabel, "Target"), "compiled access-key Label target");

        Type focusManagerType = GetRequiredType(presentationCore, "System.Windows.Input.FocusManager");
        AssertSame(accessTarget, InvokeStatic(focusManagerType, "GetFocusedElement", focusScope), "compiled FocusManager focused element");

        Type keyboardType = GetRequiredType(presentationCore, "System.Windows.Input.Keyboard");
        AssertSame(alternateAccessTarget, InvokeStatic(keyboardType, "Focus", alternateAccessTarget), "compiled FocusManager alternate Keyboard.Focus target");
        AssertSame(alternateAccessTarget, GetStaticProperty(keyboardType, "FocusedElement"), "compiled FocusManager alternate keyboard focused element");
        AssertSame(alternateAccessTarget, InvokeStatic(focusManagerType, "GetFocusedElement", focusScope), "compiled FocusManager live logical focus update");

        InvokeStatic(focusManagerType, "SetFocusedElement", focusScope, accessTarget);
        AssertSame(accessTarget, InvokeStatic(focusManagerType, "GetFocusedElement", focusScope), "compiled FocusManager logical focus restore");

        Type presentationSourceType = GetRequiredType(presentationCore, "System.Windows.PresentationSource");
        object source = InvokeStatic(presentationSourceType, "FromVisual", window);
        Type accessKeyManagerType = GetRequiredType(presentationCore, "System.Windows.Input.AccessKeyManager");

        AssertEqual(true, InvokeStatic(accessKeyManagerType, "IsKeyRegistered", source, "A"), "compiled Label access key registered");
        InvokeStatic(accessKeyManagerType, "ProcessKey", source, "A", false);

        AssertSame(accessTarget, GetStaticProperty(keyboardType, "FocusedElement"), "compiled Label access key focused target");
        InvokeStatic(keyboardType, "ClearFocus");
    }

    private static void ValidateNavigationFrame(object window)
    {
        object frame = GetField(window, "SourceNavigationFrame");
        AssertType(frame, "System.Windows.Controls.Frame", "compiled source Frame");
        AssertEqual("Hidden", GetProperty(frame, "NavigationUIVisibility").ToString(), "compiled Frame navigation UI visibility");
        AssertContains("SmokePage.xaml", GetProperty(frame, "Source")?.ToString() ?? string.Empty, "compiled Frame source");
    }

    private static void ValidatePostShowNavigationFrame(object window, Action flushDispatcherOperations)
    {
        object frame = GetField(window, "SourceNavigationFrame");
        Invoke(frame, "UpdateLayout");

        object page = GetProperty(frame, "Content");
        AssertType(page, "ProGPU.Wpf.RealXamlCompilerHarness.SmokePage", "compiled source Page content");
        AssertEqual("compiled source page", GetProperty(page, "Title"), "compiled source Page title");
        AssertEqual(0, GetProperty(page, "PageClickCount"), "compiled source Page initial click count");
        AssertAtLeast(1, GetProperty(window, "FrameNavigatingCount"), "compiled Frame initial Navigating event count");
        AssertAtLeast(1, GetProperty(window, "FrameNavigatedCount"), "compiled Frame initial Navigated event count");
        AssertAtLeast(1, GetProperty(window, "FrameLoadCompletedCount"), "compiled Frame initial LoadCompleted event count");
        AssertFrameNavigationEventState(
            window,
            Convert.ToInt32(GetProperty(window, "FrameLoadCompletedCount")),
            "New",
            "ProGPU.Wpf.RealXamlCompilerHarness.SmokePage",
            "SmokePage.xaml",
            "compiled Frame initial navigation events");

        object pagePanel = Invoke(page, "FindName", "SourceNavigationPagePanel");
        AssertType(pagePanel, "System.Windows.Controls.StackPanel", "compiled Page content panel");
        AssertSame(pagePanel, GetProperty(page, "Content"), "compiled Page content");
        AssertCollectionCount(GetProperty(pagePanel, "Children"), expected: 2, "compiled Page content panel children");

        object pageText = Invoke(page, "FindName", "SourceNavigationPageText");
        AssertType(pageText, "System.Windows.Controls.TextBlock", "compiled Page content text");
        AssertSame(pageText, GetCollectionItem(GetProperty(pagePanel, "Children"), 0), "compiled Page content text child");
        AssertEqual("source page content", GetProperty(pageText, "Tag"), "compiled Page content text tag");
        AssertEqual("compiled source page content", GetProperty(pageText, "Text"), "compiled Page content text");

        object pageButton = Invoke(page, "FindName", "SourceNavigationPageButton");
        AssertType(pageButton, "System.Windows.Controls.Button", "compiled Page content button");
        AssertSame(pageButton, GetCollectionItem(GetProperty(pagePanel, "Children"), 1), "compiled Page content button child");
        AssertEqual("source page button", GetProperty(pageButton, "Tag"), "compiled Page content button tag");
        AssertEqual("compiled page button", GetProperty(pageButton, "Content"), "compiled Page content button content");

        Invoke(pageButton, "OnClick");
        AssertEqual(1, GetProperty(page, "PageClickCount"), "compiled source Page click handler count");
        AssertEqual("SourceNavigationPageButton", GetProperty(page, "LastPageClickSenderName"), "compiled source Page click sender");
        AssertEqual("Click", GetProperty(page, "LastPageClickRoutedEventName"), "compiled source Page click routed event");

        ValidateFrameJournalNavigation(window, frame, flushDispatcherOperations);
    }

    private static void ValidateFrameJournalNavigation(object window, object frame, Action flushDispatcherOperations)
    {
        int navigationCount = Convert.ToInt32(GetProperty(window, "FrameLoadCompletedCount"));

        SetProperty(frame, "Source", new Uri("SmokeSecondPage.xaml", UriKind.Relative));
        flushDispatcherOperations();
        Invoke(frame, "UpdateLayout");
        AssertFrameNavigationEventState(
            window,
            ++navigationCount,
            "New",
            "ProGPU.Wpf.RealXamlCompilerHarness.SmokeSecondPage",
            "SmokeSecondPage.xaml",
            "compiled Frame second navigation events");

        object secondPage = GetProperty(frame, "Content");
        AssertType(secondPage, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeSecondPage", "compiled second Page content");
        AssertEqual("compiled second page", GetProperty(secondPage, "Title"), "compiled second Page title");
        object secondPagePanel = Invoke(secondPage, "FindName", "SourceNavigationSecondPagePanel");
        AssertType(secondPagePanel, "System.Windows.Controls.StackPanel", "compiled second Page content panel");
        AssertSame(secondPagePanel, GetProperty(secondPage, "Content"), "compiled second Page content");
        AssertCollectionCount(GetProperty(secondPagePanel, "Children"), expected: 1, "compiled second Page content panel children");
        object secondPageText = Invoke(secondPage, "FindName", "SourceNavigationSecondPageText");
        AssertType(secondPageText, "System.Windows.Controls.TextBlock", "compiled second Page content text");
        AssertSame(secondPageText, GetCollectionItem(GetProperty(secondPagePanel, "Children"), 0), "compiled second Page content text child");
        AssertEqual("second page content", GetProperty(secondPageText, "Tag"), "compiled second Page content text tag");
        AssertEqual("compiled second page content", GetProperty(secondPageText, "Text"), "compiled second Page content text");

        AssertEqual(true, GetProperty(frame, "CanGoBack"), "compiled Frame journal can go back");
        AssertEqual(false, GetProperty(frame, "CanGoForward"), "compiled Frame journal cannot go forward before back");

        Invoke(frame, "GoBack");
        flushDispatcherOperations();
        Invoke(frame, "UpdateLayout");
        AssertFrameNavigationEventState(
            window,
            ++navigationCount,
            "Back",
            "ProGPU.Wpf.RealXamlCompilerHarness.SmokePage",
            null,
            "compiled Frame journal back navigation events");
        object firstPageAgain = GetProperty(frame, "Content");
        AssertType(firstPageAgain, "ProGPU.Wpf.RealXamlCompilerHarness.SmokePage", "compiled Frame journal back content");
        AssertEqual("compiled source page", GetProperty(firstPageAgain, "Title"), "compiled Frame journal back title");
        AssertEqual(false, GetProperty(frame, "CanGoBack"), "compiled Frame journal cannot go back after returning");
        AssertEqual(true, GetProperty(frame, "CanGoForward"), "compiled Frame journal can go forward");

        Invoke(frame, "GoForward");
        flushDispatcherOperations();
        Invoke(frame, "UpdateLayout");
        AssertFrameNavigationEventState(
            window,
            ++navigationCount,
            "Forward",
            "ProGPU.Wpf.RealXamlCompilerHarness.SmokeSecondPage",
            null,
            "compiled Frame journal forward navigation events");
        object secondPageAgain = GetProperty(frame, "Content");
        AssertType(secondPageAgain, "ProGPU.Wpf.RealXamlCompilerHarness.SmokeSecondPage", "compiled Frame journal forward content");
        AssertEqual("compiled second page", GetProperty(secondPageAgain, "Title"), "compiled Frame journal forward title");
    }

    private static void AssertFrameNavigationEventState(
        object window,
        int expectedCount,
        string expectedMode,
        string expectedContentType,
        string? expectedUriSubstring,
        string description)
    {
        AssertEqual(expectedCount, GetProperty(window, "FrameNavigatingCount"), $"{description} Navigating count");
        AssertEqual(expectedCount, GetProperty(window, "FrameNavigatedCount"), $"{description} Navigated count");
        AssertEqual(expectedCount, GetProperty(window, "FrameLoadCompletedCount"), $"{description} LoadCompleted count");
        AssertEqual("SourceNavigationFrame", GetProperty(window, "LastFrameNavigatingSenderName"), $"{description} Navigating sender");
        AssertEqual("SourceNavigationFrame", GetProperty(window, "LastFrameNavigatedSenderName"), $"{description} Navigated sender");
        AssertEqual("SourceNavigationFrame", GetProperty(window, "LastFrameLoadCompletedSenderName"), $"{description} LoadCompleted sender");
        AssertEqual(expectedMode, GetProperty(window, "LastFrameNavigatingNavigationMode"), $"{description} Navigating mode");
        AssertEqual(expectedContentType, GetProperty(window, "LastFrameNavigatedContentType"), $"{description} Navigated content type");
        AssertEqual(expectedContentType, GetProperty(window, "LastFrameLoadCompletedContentType"), $"{description} LoadCompleted content type");

        if (expectedUriSubstring != null)
        {
            AssertContains(
                expectedUriSubstring,
                GetProperty(window, "LastFrameNavigatingUri")?.ToString() ?? string.Empty,
                $"{description} Navigating URI");
            AssertContains(
                expectedUriSubstring,
                GetProperty(window, "LastFrameNavigatedUri")?.ToString() ?? string.Empty,
                $"{description} Navigated URI");
            AssertContains(
                expectedUriSubstring,
                GetProperty(window, "LastFrameLoadCompletedUri")?.ToString() ?? string.Empty,
                $"{description} LoadCompleted URI");
        }
    }

    private static ActivationRecorder RegisterPortableActivation(
        Assembly presentationFramework,
        Assembly presentationCore,
        Assembly compilerHarness,
        object application,
        out Type activationServiceType)
    {
        activationServiceType = GetRequiredType(presentationFramework, PortableWindowActivationServiceTypeName);
        MethodInfo register = activationServiceType.GetMethod(
            "Register",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(activationServiceType.FullName, "Register");

        var recorder = new ActivationRecorder(presentationFramework, presentationCore, compilerHarness, application, activationServiceType);
        register.Invoke(
            null,
            new object?[]
            {
                new Func<object, object>(recorder.Activate),
                new Action<object>(recorder.Show),
                new Action<object>(recorder.Hide),
                new Action<object, object>(recorder.SetWindowState),
                new Action<object, string>(recorder.SetTitle),
                new Action<object, double, double>(recorder.SetClientSize),
                new Action<object, double, double>(recorder.SetPosition),
                new Action<object, bool>(recorder.SetTopmost),
                new Action<object, object, object>(recorder.SetWindowBorder),
                new Action<object>(recorder.Close),
                new Action<object>(recorder.Run),
                new Action<object>(recorder.Dispose),
                new Func<object, bool>(_ => false),
                new Func<object, IntPtr>(recorder.GetHandle),
                null,
                new Func<object, bool>(recorder.RequestActivation),
                null
            });

        RegisterPortableMessageBox(presentationFramework);
        AssertEqual(true, GetStaticProperty(activationServiceType, "IsEnabled"), "portable activation enabled");
        return recorder;
    }

    private static void RegisterPortableMessageBox(Assembly presentationFramework)
    {
        Type serviceType = GetRequiredType(presentationFramework, PortableMessageBoxServiceTypeName);
        MethodInfo register = serviceType.GetMethod(
                "Register",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(Func<object, object>) },
                modifiers: null)
            ?? throw new MissingMethodException(serviceType.FullName, "Register");

        register.Invoke(
            null,
            new object[] { (Func<object, object>)ShowPortableMessageBox });
        AssertEqual(true, GetStaticProperty(serviceType, "IsEnabled"), "portable MessageBox service enabled");
    }

    private static object ShowPortableMessageBox(object request)
    {
        return GetProperty(request, "FallbackResult");
    }

    private static void ValidatePortableMessageBox(Assembly presentationFramework, object window)
    {
        Type serviceType = GetRequiredType(presentationFramework, PortableMessageBoxServiceTypeName);
        AssertEqual(true, GetStaticProperty(serviceType, "IsEnabled"), "portable MessageBox service enabled");

        Type messageBoxType = GetRequiredType(presentationFramework, "System.Windows.MessageBox");
        Type windowType = GetRequiredType(presentationFramework, "System.Windows.Window");
        Type buttonType = GetRequiredType(presentationFramework, "System.Windows.MessageBoxButton");
        Type imageType = GetRequiredType(presentationFramework, "System.Windows.MessageBoxImage");
        Type resultType = GetRequiredType(presentationFramework, "System.Windows.MessageBoxResult");
        Type optionsType = GetRequiredType(presentationFramework, "System.Windows.MessageBoxOptions");

        object yesNoCancel = Enum.Parse(buttonType, "YesNoCancel");
        object okCancel = Enum.Parse(buttonType, "OKCancel");
        object warning = Enum.Parse(imageType, "Warning");
        object information = Enum.Parse(imageType, "Information");
        object noneResult = Enum.Parse(resultType, "None");
        object no = Enum.Parse(resultType, "No");
        object ok = Enum.Parse(resultType, "OK");
        object noneOptions = Enum.Parse(optionsType, "None");

        MethodInfo noOwnerShow = messageBoxType.GetMethod(
                "Show",
                BindingFlags.Static | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(string), typeof(string), buttonType, imageType, resultType, optionsType },
                modifiers: null)
            ?? throw new MissingMethodException(messageBoxType.FullName, "Show");
        object noOwnerResult = noOwnerShow.Invoke(
                null,
                new[] { "portable message", "portable caption", yesNoCancel, warning, no, noneOptions })
            ?? throw new InvalidOperationException("MessageBox.Show returned null.");
        AssertEqual(no, noOwnerResult, "portable MessageBox no-owner default result");

        MethodInfo ownerShow = messageBoxType.GetMethod(
                "Show",
                BindingFlags.Static | BindingFlags.Public,
                binder: null,
                types: new[] { windowType, typeof(string), typeof(string), buttonType, imageType, resultType, optionsType },
                modifiers: null)
            ?? throw new MissingMethodException(messageBoxType.FullName, "Show");
        object ownerResult = ownerShow.Invoke(
                null,
                new[] { window, "portable owner message", "portable owner caption", okCancel, information, noneResult, noneOptions })
            ?? throw new InvalidOperationException("MessageBox.Show returned null.");
        AssertEqual(ok, ownerResult, "portable MessageBox owner fallback result");
    }

    private static void ValidatePortableClipboard(Assembly presentationCore)
    {
        Type serviceType = GetRequiredType(presentationCore, PortableClipboardServiceTypeName);
        AssertEqual(true, GetStaticProperty(serviceType, "IsEnabled"), "portable Clipboard service enabled");

        Type clipboardType = GetRequiredType(presentationCore, "System.Windows.Clipboard");
        Type dataFormatsType = GetRequiredType(presentationCore, "System.Windows.DataFormats");
        Type dataObjectInterfaceType = GetRequiredType(presentationCore, "System.Windows.IDataObject");
        object unicodeText = GetStaticField(dataFormatsType, "UnicodeText");

        InvokeStatic(clipboardType, "Clear");
        AssertEqual(false, InvokeStatic(clipboardType, "ContainsText"), "portable Clipboard initial text state");

        InvokeStatic(clipboardType, "SetText", "portable clipboard text");
        AssertEqual(true, InvokeStatic(clipboardType, "ContainsText"), "portable Clipboard text state after SetText");
        AssertEqual("portable clipboard text", InvokeStatic(clipboardType, "GetText"), "portable Clipboard GetText");

        object dataObject = InvokeStatic(clipboardType, "GetDataObject");
        AssertEqual(true, dataObjectInterfaceType.IsInstanceOfType(dataObject), "portable Clipboard data object contract");
        AssertEqual(
            "portable clipboard text",
            Invoke(dataObject, "GetData", unicodeText, false),
            "portable Clipboard data object unicode text");
        AssertEqual(true, InvokeStatic(clipboardType, "IsCurrent", dataObject), "portable Clipboard current data object");

        InvokeStatic(clipboardType, "Flush");
        AssertEqual("portable clipboard text", InvokeStatic(clipboardType, "GetText"), "portable Clipboard flushed text");

        InvokeStatic(clipboardType, "Clear");
        AssertEqual(false, InvokeStatic(clipboardType, "ContainsText"), "portable Clipboard cleared text state");
        AssertEqual(string.Empty, InvokeStatic(clipboardType, "GetText"), "portable Clipboard cleared text");
    }

    private static void ValidatePortableFileDialogs(Assembly presentationFramework)
    {
        Type serviceType = GetRequiredType(presentationFramework, PortableFileDialogServiceTypeName);
        AssertEqual(true, GetStaticProperty(serviceType, "IsEnabled"), "portable file dialog service enabled");

        MethodInfo registerMethod = serviceType.GetMethod(
            "Register",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(Func<object, string?>) },
            modifiers: null)
            ?? throw new MissingMethodException(serviceType.FullName, "Register");

        string tempDirectory = Path.Combine(Path.GetTempPath(), "progpu-wpf-file-dialog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        string openPath = Path.Combine(tempDirectory, "open.txt");
        string savePathWithoutExtension = Path.Combine(tempDirectory, "saved");
        string savePath = savePathWithoutExtension + ".txt";
        File.WriteAllText(openPath, "portable file dialog");

        int requestCount = 0;
        var seenKinds = new List<string>();
        Func<object, string?> handler = request =>
        {
            string kind = GetProperty(request, "Kind").ToString() ?? string.Empty;
            seenKinds.Add(kind);
            requestCount++;

            return kind switch
            {
                "SaveFile" => savePathWithoutExtension,
                "PickFolder" => tempDirectory,
                _ => openPath
            };
        };

        IDisposable? registration = null;
        try
        {
            registration = (IDisposable?)registerMethod.Invoke(null, new object[] { handler });

            object openDialog = Create(presentationFramework, "Microsoft.Win32.OpenFileDialog");
            SetProperty(openDialog, "Filter", "Text files (*.txt)|*.txt|All files (*.*)|*.*");
            AssertEqual(true, Invoke(openDialog, "ShowDialog"), "portable OpenFileDialog result");
            AssertEqual(openPath, GetProperty(openDialog, "FileName"), "portable OpenFileDialog FileName");
            AssertEqual("open.txt", GetProperty(openDialog, "SafeFileName"), "portable OpenFileDialog SafeFileName");

            object saveDialog = Create(presentationFramework, "Microsoft.Win32.SaveFileDialog");
            SetProperty(saveDialog, "DefaultExt", "txt");
            SetProperty(saveDialog, "OverwritePrompt", false);
            AssertEqual(true, Invoke(saveDialog, "ShowDialog"), "portable SaveFileDialog result");
            AssertEqual(savePath, GetProperty(saveDialog, "FileName"), "portable SaveFileDialog FileName");
            AssertEqual("saved.txt", GetProperty(saveDialog, "SafeFileName"), "portable SaveFileDialog SafeFileName");

            object folderDialog = Create(presentationFramework, "Microsoft.Win32.OpenFolderDialog");
            AssertEqual(true, Invoke(folderDialog, "ShowDialog"), "portable OpenFolderDialog result");
            AssertEqual(tempDirectory, GetProperty(folderDialog, "FolderName"), "portable OpenFolderDialog FolderName");
            AssertEqual(Path.GetFileName(tempDirectory), GetProperty(folderDialog, "SafeFolderName"), "portable OpenFolderDialog SafeFolderName");

            AssertEqual(3, requestCount, "portable file dialog request count");
            AssertEqual("OpenFile", seenKinds[0], "portable file dialog open request kind");
            AssertEqual("SaveFile", seenKinds[1], "portable file dialog save request kind");
            AssertEqual("PickFolder", seenKinds[2], "portable file dialog folder request kind");
        }
        finally
        {
            registration?.Dispose();
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static void ClearPortableService(Assembly assembly, string typeName)
    {
        assembly.GetType(typeName, throwOnError: false)?.GetMethod(
            "Clear",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
    }

    private static object Create(Assembly assembly, string typeName, params object?[] parameters)
    {
        Type type = GetRequiredType(assembly, typeName);
        return Activator.CreateInstance(type, parameters)
            ?? throw new InvalidOperationException($"Failed to create '{typeName}'.");
    }

    private static object CreateInternal(Type type, params object?[] parameters)
    {
        return Activator.CreateInstance(
                type,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: parameters,
                culture: null)
            ?? throw new InvalidOperationException($"Failed to create '{type.FullName}'.");
    }

    private static Type GetRequiredType(Assembly assembly, string typeName)
    {
        return assembly.GetType(typeName, throwOnError: true)
            ?? throw new TypeLoadException($"Could not load '{typeName}' from '{assembly.FullName}'.");
    }

    private static object GetProperty(object instance, string propertyName)
    {
        for (Type? type = instance.GetType(); type != null; type = type.BaseType)
        {
            PropertyInfo? property = type.GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property != null)
            {
                return property.GetValue(instance)
                    ?? throw new InvalidOperationException($"Expected '{type.FullName}.{propertyName}' to have a value.");
            }
        }

        throw new MissingMemberException(instance.GetType().FullName, propertyName);
    }

    private static object GetStaticProperty(Type type, string propertyName)
    {
        return type.GetProperty(
            propertyName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
            ?? throw new InvalidOperationException($"Expected '{type.FullName}.{propertyName}' to have a value.");
    }

    private static void SetStaticProperty(Type type, string propertyName, object? value)
    {
        PropertyInfo property = type.GetProperty(
                propertyName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(type.FullName, propertyName);

        property.SetValue(null, value);
    }

    private static object GetStaticField(Type type, string fieldName)
    {
        return type.GetField(
            fieldName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
            ?? throw new InvalidOperationException($"Expected '{type.FullName}.{fieldName}' to have a value.");
    }

    private static object? TryGetStaticProperty(Type type, string propertyName)
    {
        return type.GetProperty(
            propertyName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
    }

    private static object? TryGetProperty(object instance, string propertyName)
    {
        for (Type? type = instance.GetType(); type != null; type = type.BaseType)
        {
            PropertyInfo? property = type.GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property != null)
            {
                return property.GetValue(instance);
            }
        }

        return null;
    }

    private static (double X, double Y) GetElementCenterInWindow(Assembly presentationCore, object element, object window)
    {
        object windowPoint = GetElementCenterPointInWindow(element, window);
        object transformToDevice = GetTransformToDevice(presentationCore, window);
        (double x, double y) = TransformPoint(transformToDevice, windowPoint);

        return (x, y);
    }

    private static object GetElementCenterPointInWindow(object element, object window)
    {
        double width = Convert.ToDouble(GetProperty(element, "ActualWidth"));
        double height = Convert.ToDouble(GetProperty(element, "ActualHeight"));
        object renderSize = GetProperty(element, "RenderSize");
        if (width <= 0)
        {
            width = Convert.ToDouble(GetProperty(renderSize, "Width"));
        }

        if (height <= 0)
        {
            height = Convert.ToDouble(GetProperty(renderSize, "Height"));
        }

        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException(
                $"Expected '{element.GetType().FullName}' to have a non-empty arranged size.");
        }

        Type pointType = renderSize.GetType().Assembly.GetType("System.Windows.Point", throwOnError: true)
            ?? throw new TypeLoadException("Could not load 'System.Windows.Point'.");
        object center = Activator.CreateInstance(pointType, width / 2d, height / 2d)
            ?? throw new InvalidOperationException("Failed to create a WPF Point for portable mouse input.");
        return Invoke(element, "TranslatePoint", center, window);
    }

    private static (double X, double Y) GetVisibleElementInputPointInWindow(
        Assembly presentationCore,
        object element,
        object window)
    {
        double width = Convert.ToDouble(GetProperty(element, "ActualWidth"));
        double height = Convert.ToDouble(GetProperty(element, "ActualHeight"));
        object renderSize = GetProperty(element, "RenderSize");
        if (width <= 0)
        {
            width = Convert.ToDouble(GetProperty(renderSize, "Width"));
        }

        if (height <= 0)
        {
            height = Convert.ToDouble(GetProperty(renderSize, "Height"));
        }

        Type pointType = renderSize.GetType().Assembly.GetType("System.Windows.Point", throwOnError: true)
            ?? throw new TypeLoadException("Could not load 'System.Windows.Point'.");
        object transformToDevice = GetTransformToDevice(presentationCore, window);
        foreach (double verticalFraction in new[] { 0.1, 0.3, 0.5, 0.7, 0.9 })
        {
            object localPoint = Activator.CreateInstance(pointType, width / 2d, height * verticalFraction)
                ?? throw new InvalidOperationException("Failed to create a WPF Point for portable mouse input.");
            object windowPoint = Invoke(element, "TranslatePoint", localPoint, window);
            object? hit = InvokeNullable(window, "InputHitTest", windowPoint);
            if (hit != null && IsVisualDescendantOrSelf(presentationCore, element, hit))
            {
                return TransformPoint(transformToDevice, windowPoint);
            }
        }

        return GetElementCenterInWindow(presentationCore, element, window);
    }

    private static bool IsVisualDescendantOrSelf(Assembly presentationCore, object ancestor, object candidate)
    {
        Type visualTreeHelperType = GetRequiredType(presentationCore, "System.Windows.Media.VisualTreeHelper");
        object? current = candidate;
        while (current != null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            try
            {
                current = InvokeStatic(visualTreeHelperType, "GetParent", current);
            }
            catch (TargetInvocationException)
            {
                return false;
            }
        }

        return false;
    }

    private static object GetTransformToDevice(Assembly presentationCore, object visual)
    {
        Type presentationSourceType = GetRequiredType(presentationCore, "System.Windows.PresentationSource");
        object source = InvokeStatic(presentationSourceType, "FromVisual", visual);
        object compositionTarget = GetProperty(source, "CompositionTarget");
        return GetProperty(compositionTarget, "TransformToDevice");
    }

    private static (double X, double Y) TransformPoint(object matrix, object point)
    {
        double x = Convert.ToDouble(GetProperty(point, "X"));
        double y = Convert.ToDouble(GetProperty(point, "Y"));
        double m11 = Convert.ToDouble(GetProperty(matrix, "M11"));
        double m12 = Convert.ToDouble(GetProperty(matrix, "M12"));
        double m21 = Convert.ToDouble(GetProperty(matrix, "M21"));
        double m22 = Convert.ToDouble(GetProperty(matrix, "M22"));
        double offsetX = Convert.ToDouble(GetProperty(matrix, "OffsetX"));
        double offsetY = Convert.ToDouble(GetProperty(matrix, "OffsetY"));

        return (
            (x * m11) + (y * m21) + offsetX,
            (x * m12) + (y * m22) + offsetY);
    }

    private sealed class ScrollEventRecorder
    {
        private readonly List<string> _eventTypes = new();
        private readonly List<double> _newValues = new();

        public void Record(object sender, EventArgs args)
        {
            _eventTypes.Add(GetProperty(args, "ScrollEventType").ToString() ?? string.Empty);
            _newValues.Add(Convert.ToDouble(GetProperty(args, "NewValue")));
        }

        public void AssertLast(string expectedEventType, double expectedNewValue, string description)
        {
            if (_eventTypes.Count == 0)
            {
                throw new InvalidOperationException($"Expected {description} to record a Scroll event.");
            }

            int index = _eventTypes.Count - 1;
            AssertEqual(expectedEventType, _eventTypes[index], $"{description} type");
            AssertEqual(expectedNewValue, _newValues[index], $"{description} new value");
        }
    }

    private static object? FindVisualDescendantByName(Assembly presentationCore, object root, string name)
    {
        if (string.Equals(TryGetProperty(root, "Name")?.ToString(), name, StringComparison.Ordinal))
        {
            return root;
        }

        Type visualTreeHelperType = GetRequiredType(presentationCore, "System.Windows.Media.VisualTreeHelper");
        int count = Convert.ToInt32(InvokeStatic(visualTreeHelperType, "GetChildrenCount", root));
        for (int i = 0; i < count; i++)
        {
            object child = InvokeStatic(visualTreeHelperType, "GetChild", root, i);
            object? match = FindVisualDescendantByName(presentationCore, child, name);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private static object? FindVisualDescendantByTypeName(Assembly presentationCore, object root, string typeName)
    {
        if (string.Equals(root.GetType().FullName, typeName, StringComparison.Ordinal))
        {
            return root;
        }

        Type visualTreeHelperType = GetRequiredType(presentationCore, "System.Windows.Media.VisualTreeHelper");
        int count = Convert.ToInt32(InvokeStatic(visualTreeHelperType, "GetChildrenCount", root));
        for (int i = 0; i < count; i++)
        {
            object child = InvokeStatic(visualTreeHelperType, "GetChild", root, i);
            object? match = FindVisualDescendantByTypeName(presentationCore, child, typeName);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private static void SetProperty(object instance, string propertyName, object? value)
    {
        instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(instance, value);
    }

    private static object GetField(object instance, string fieldName)
    {
        for (Type? type = instance.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo? field = type.GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                return field.GetValue(instance)
                    ?? throw new InvalidOperationException($"Expected '{type.FullName}.{fieldName}' to have a value.");
            }
        }

        throw new MissingFieldException(instance.GetType().FullName, fieldName);
    }

    private static object GetDictionaryValue(object dictionary, object key)
    {
        if (dictionary is IDictionary nonGenericDictionary && nonGenericDictionary.Contains(key))
        {
            return nonGenericDictionary[key]
                ?? throw new InvalidOperationException($"Dictionary key '{key}' had a null value.");
        }

        object value = Invoke(dictionary, "get_Item", key);
        if (value == null)
        {
            throw new InvalidOperationException($"Dictionary key '{key}' had a null value.");
        }

        return value;
    }

    private static void SetDictionaryValue(object dictionary, object key, object value)
    {
        if (dictionary is IDictionary nonGenericDictionary)
        {
            nonGenericDictionary[key] = value;
            return;
        }

        Invoke(dictionary, "set_Item", key, value);
    }

    private static object GetCollectionItem(object collection, int index)
    {
        if (collection is IList list)
        {
            return list[index]
                ?? throw new InvalidOperationException($"Collection item {index} had a null value.");
        }

        if (collection is IEnumerable enumerable)
        {
            int currentIndex = 0;
            foreach (object? item in enumerable)
            {
                if (currentIndex == index)
                {
                    return item
                        ?? throw new InvalidOperationException($"Collection item {index} had a null value.");
                }

                currentIndex++;
            }
        }

        return Invoke(collection, "get_Item", index);
    }

    private static object GetFirstCollectionItemOfType(object collection, string expectedFullName, string description)
    {
        if (collection is IEnumerable enumerable)
        {
            foreach (object? item in enumerable)
            {
                if (item != null && string.Equals(item.GetType().FullName, expectedFullName, StringComparison.Ordinal))
                {
                    return item;
                }
            }
        }

        throw new InvalidOperationException($"Expected {description} to contain '{expectedFullName}'.");
    }

    private static object GetDependencyPropertyValue(object dependencyObject, Type ownerType, string dependencyPropertyFieldName)
    {
        FieldInfo dependencyProperty = ownerType.GetField(
            dependencyPropertyFieldName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)
            ?? throw new MissingFieldException(ownerType.FullName, dependencyPropertyFieldName);
        return Invoke(dependencyObject, "GetValue", dependencyProperty.GetValue(null));
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

    private static void AssertBindingPath(
        object dependencyObject,
        string dependencyPropertyFieldName,
        string expectedPath,
        string description)
    {
        object bindingExpression = GetBindingExpression(dependencyObject, dependencyPropertyFieldName);
        object parentBinding = GetProperty(bindingExpression, "ParentBinding");
        object path = GetProperty(parentBinding, "Path");
        AssertEqual(expectedPath, GetProperty(path, "Path"), description);
    }

    private static void AssertBindingObjectPath(object binding, string expectedPath, string description)
    {
        object path = GetProperty(binding, "Path");
        AssertEqual(expectedPath, GetProperty(path, "Path"), description);
    }

    private static object GetBindingExpression(object dependencyObject, string dependencyPropertyFieldName)
    {
        FieldInfo dependencyProperty = dependencyObject.GetType().GetField(
            dependencyPropertyFieldName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)
            ?? throw new MissingFieldException(dependencyObject.GetType().FullName, dependencyPropertyFieldName);
        MethodInfo getBindingExpression = dependencyObject.GetType().GetMethod(
            "GetBindingExpression",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMethodException(dependencyObject.GetType().FullName, "GetBindingExpression");

        object? bindingExpression = getBindingExpression.Invoke(dependencyObject, new[] { dependencyProperty.GetValue(null) });
        if (bindingExpression == null)
        {
            throw new InvalidOperationException(
                $"Expected '{dependencyObject.GetType().FullName}.{dependencyPropertyFieldName}' to have a binding expression.");
        }

        return bindingExpression;
    }

    private static object GetPriorityBindingExpression(object dependencyObject, string dependencyPropertyFieldName)
    {
        FieldInfo dependencyProperty = dependencyObject.GetType().GetField(
            dependencyPropertyFieldName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)
            ?? throw new MissingFieldException(dependencyObject.GetType().FullName, dependencyPropertyFieldName);
        Type bindingOperationsType = dependencyObject.GetType().Assembly.GetType(
            "System.Windows.Data.BindingOperations",
            throwOnError: true)
            ?? throw new TypeLoadException("System.Windows.Data.BindingOperations");
        MethodInfo getPriorityBindingExpression = bindingOperationsType.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "GetPriorityBindingExpression", StringComparison.Ordinal) &&
                candidate.GetParameters().Length == 2)
            ?? throw new MissingMethodException(bindingOperationsType.FullName, "GetPriorityBindingExpression");

        object? bindingExpression = getPriorityBindingExpression.Invoke(null, new[] { dependencyObject, dependencyProperty.GetValue(null) });
        if (bindingExpression == null)
        {
            throw new InvalidOperationException(
                $"Expected '{dependencyObject.GetType().FullName}.{dependencyPropertyFieldName}' to have a priority binding expression.");
        }

        return bindingExpression;
    }

    private static object GetMultiBindingExpression(object dependencyObject, string dependencyPropertyFieldName)
    {
        FieldInfo dependencyProperty = dependencyObject.GetType().GetField(
            dependencyPropertyFieldName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)
            ?? throw new MissingFieldException(dependencyObject.GetType().FullName, dependencyPropertyFieldName);
        Type bindingOperationsType = dependencyObject.GetType().Assembly.GetType(
            "System.Windows.Data.BindingOperations",
            throwOnError: true)
            ?? throw new TypeLoadException("System.Windows.Data.BindingOperations");
        MethodInfo getMultiBindingExpression = bindingOperationsType.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "GetMultiBindingExpression", StringComparison.Ordinal) &&
                candidate.GetParameters().Length == 2)
            ?? throw new MissingMethodException(bindingOperationsType.FullName, "GetMultiBindingExpression");

        object? bindingExpression = getMultiBindingExpression.Invoke(null, new[] { dependencyObject, dependencyProperty.GetValue(null) });
        if (bindingExpression == null)
        {
            throw new InvalidOperationException(
                $"Expected '{dependencyObject.GetType().FullName}.{dependencyPropertyFieldName}' to have a multi binding expression.");
        }

        return bindingExpression;
    }

    private static object Invoke(object instance, string methodName, params object?[] parameters)
    {
        MethodInfo method = instance.GetType().GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate =>
            {
                if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                {
                    return false;
                }

                ParameterInfo[] candidateParameters = candidate.GetParameters();
                return candidateParameters.Length == parameters.Length;
            })
            ?? throw new MissingMethodException(instance.GetType().FullName, methodName);

        return method.Invoke(instance, parameters) ?? new object();
    }

    private static object? InvokeNullable(object instance, string methodName, params object?[] parameters)
    {
        MethodInfo method = instance.GetType().GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate =>
            {
                if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                {
                    return false;
                }

                ParameterInfo[] candidateParameters = candidate.GetParameters();
                return candidateParameters.Length == parameters.Length;
            })
            ?? throw new MissingMethodException(instance.GetType().FullName, methodName);

        return method.Invoke(instance, parameters);
    }

    private static object InvokeDataGridColumnGetCellContent(object column, object row)
    {
        MethodInfo method = column.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(candidate =>
            {
                if (!string.Equals(candidate.Name, "GetCellContent", StringComparison.Ordinal))
                {
                    return false;
                }

                ParameterInfo[] parameters = candidate.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(row.GetType());
            })
            .OrderBy(candidate => GetTypeDistance(row.GetType(), candidate.GetParameters()[0].ParameterType))
            .FirstOrDefault()
            ?? throw new MissingMethodException(column.GetType().FullName, "GetCellContent");

        return method.Invoke(column, new[] { row })
            ?? throw new InvalidOperationException(
                $"Expected '{column.GetType().FullName}.GetCellContent(...)' to return generated cell content.");
    }

    private static object CreateDataGridRowClipboardEventArgs(
        object dataGrid,
        object? item,
        int startColumnDisplayIndex,
        int endColumnDisplayIndex,
        bool isColumnHeadersRow)
    {
        Type argsType = dataGrid.GetType().Assembly.GetType(
            "System.Windows.Controls.DataGridRowClipboardEventArgs",
            throwOnError: true)
            ?? throw new TypeLoadException("System.Windows.Controls.DataGridRowClipboardEventArgs");

        return Activator.CreateInstance(
                argsType,
                item,
                startColumnDisplayIndex,
                endColumnDisplayIndex,
                isColumnHeadersRow)
            ?? throw new InvalidOperationException("Expected DataGridRowClipboardEventArgs construction to return a value.");
    }

    private static int GetTypeDistance(Type concreteType, Type targetType)
    {
        var distance = 0;
        for (Type? current = concreteType; current != null; current = current.BaseType)
        {
            if (current == targetType)
            {
                return distance;
            }

            distance++;
        }

        return int.MaxValue;
    }

    private static object InvokeStatic(Type type, string methodName, params object?[] parameters)
    {
        MethodInfo method = type.GetMethods(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate =>
            {
                if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                {
                    return false;
                }

                ParameterInfo[] candidateParameters = candidate.GetParameters();
                return candidateParameters.Length == parameters.Length;
            })
            ?? throw new MissingMethodException(type.FullName, methodName);

        return method.Invoke(null, parameters) ?? new object();
    }

    private static object InvokeTwoArgumentCommand(object command, string methodName, object? parameter, object target)
    {
        MethodInfo method = command.GetType().GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate =>
            {
                if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                {
                    return false;
                }

                ParameterInfo[] candidateParameters = candidate.GetParameters();
                return candidateParameters.Length == 2 &&
                    candidateParameters[1].ParameterType.IsAssignableFrom(target.GetType());
            })
            ?? throw new MissingMethodException(command.GetType().FullName, methodName);

        return method.Invoke(command, new[] { parameter, target }) ?? new object();
    }

    private static void TryInvoke(object instance, string methodName, params object?[] parameters)
    {
        try
        {
            Invoke(instance, methodName, parameters);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void AssertCollectionCount(object collection, int expected, string description)
    {
        object count =
            collection is Array array ? array.Length :
            collection is ICollection nonGenericCollection ? nonGenericCollection.Count :
            GetProperty(collection, "Count");
        AssertEqual(expected, count, description);
    }

    private static void AssertType(object instance, string expectedFullName, string description)
    {
        if (!string.Equals(instance.GetType().FullName, expectedFullName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected {description} to be '{expectedFullName}', got '{instance.GetType().FullName}'.");
        }
    }

    private static void AssertSame(object expected, object actual, string description)
    {
        if (!ReferenceEquals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {description} to reference the same object.");
        }
    }

    private static void AssertNotSame(object expectedDifferent, object actual, string description)
    {
        if (ReferenceEquals(expectedDifferent, actual))
        {
            throw new InvalidOperationException($"Expected {description} to reference different objects.");
        }
    }

    private static void AssertEqual(object? expected, object? actual, string description)
    {
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {description} to be '{expected}', got '{actual}'.");
        }
    }

    private static void AssertClose(double expected, double actual, double tolerance, string description)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException(
                $"Expected {description} to be within '{tolerance}' of '{expected}', got '{actual}'.");
        }
    }

    private static void AssertPoint(object? actual, double expectedX, double expectedY, string description)
    {
        if (actual == null)
        {
            throw new InvalidOperationException($"Expected {description} to be a point, got null.");
        }

        AssertEqual(expectedX, GetProperty(actual, "X"), $"{description} X");
        AssertEqual(expectedY, GetProperty(actual, "Y"), $"{description} Y");
    }

    private static void AssertDate(object? actual, int expectedYear, int expectedMonth, int expectedDay, string description)
    {
        if (actual is not DateTime actualDate)
        {
            throw new InvalidOperationException($"Expected {description} to be a DateTime, got '{actual}'.");
        }

        if (actualDate.Year != expectedYear || actualDate.Month != expectedMonth || actualDate.Day != expectedDay)
        {
            throw new InvalidOperationException(
                $"Expected {description} to be '{expectedYear:D4}-{expectedMonth:D2}-{expectedDay:D2}', got '{actualDate:yyyy-MM-dd}'.");
        }
    }

    private static void AssertContains(string expectedSubstring, string actual, string description)
    {
        if (!actual.Contains(expectedSubstring, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected {description} to contain '{expectedSubstring}', got '{actual}'.");
        }
    }

    private static void AssertAtLeast(int expectedMinimum, object actual, string description)
    {
        int actualValue = Convert.ToInt32(actual);
        if (actualValue < expectedMinimum)
        {
            throw new InvalidOperationException($"Expected {description} to be at least {expectedMinimum}, got {actualValue}.");
        }
    }

    private static string FindArtifactAssembly(string repoRoot, string assemblyName)
    {
        string artifactsRoot = Path.Combine(repoRoot, "artifacts", "bin", assemblyName);
        if (!Directory.Exists(artifactsRoot))
        {
            throw new DirectoryNotFoundException($"Artifacts directory was not found: {artifactsRoot}");
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
            ?? throw new FileNotFoundException($"Could not locate a net10.0 {assemblyName}.dll artifact.", artifactsRoot);
    }

    private static string? TryFindArtifactAssembly(string repoRoot, AssemblyName assemblyName)
    {
        if (assemblyName.Name == null)
        {
            return null;
        }

        string artifactsRoot = Path.Combine(repoRoot, "artifacts", "bin", assemblyName.Name);
        if (!Directory.Exists(artifactsRoot))
        {
            return null;
        }

        return Directory
            .GetFiles(artifactsRoot, $"{assemblyName.Name}.dll", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}net10.0{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
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

    private sealed class ActivationRecorder : IDisposable
    {
        private readonly Assembly _presentationFramework;
        private readonly Assembly _presentationCore;
        private readonly Assembly _compilerHarness;
        private readonly object _application;
        private readonly Type _activationServiceType;
        private readonly IDisposable? _mediaContextRenderRegistration;
        private object? _activation;
        private bool _isDisposed;
        private bool _isFlushingWpfDispatcher;

        public ActivationRecorder(
            Assembly presentationFramework,
            Assembly presentationCore,
            Assembly compilerHarness,
            object application,
            Type activationServiceType)
        {
            _presentationFramework = presentationFramework;
            _presentationCore = presentationCore;
            _compilerHarness = compilerHarness;
            _application = application;
            _activationServiceType = activationServiceType;
            _mediaContextRenderRegistration = RegisterMediaContextRenderService();
        }

        public int ActivateCount { get; private set; }

        public int ShowCount { get; private set; }

        public int RunCount { get; private set; }

        public int RenderRequestCount { get; private set; }

        public int CloseCount { get; private set; }

        public int DisposeCount { get; private set; }

        public object Activate(object window)
        {
            if (ActivateCount != 0)
            {
                throw new InvalidOperationException("Expected exactly one startup window activation.");
            }

            AssertType(window, MainWindowTypeName, "activated startup window");
            AssertSame(GetRequiredType(_compilerHarness, MainWindowTypeName), window.GetType(), "activated startup window type");
            ValidateMainWindow(_presentationCore, window, _application);

            object presentationSource = CreatePortablePresentationSource(window);
            ActivateCount++;
            _activation = new RecordingActivation(window, presentationSource)
            {
                Title = GetProperty(window, "Title").ToString() ?? string.Empty,
                Width = Convert.ToDouble(GetProperty(window, "Width")),
                Height = Convert.ToDouble(GetProperty(window, "Height")),
                Left = Convert.ToDouble(GetProperty(window, "Left")),
                Top = Convert.ToDouble(GetProperty(window, "Top")),
                Topmost = Convert.ToBoolean(GetProperty(window, "Topmost"))
            };
            return _activation;
        }

        public void Show(object activation)
        {
            AssertSameActivation(activation);
            ShowCount++;
            var typedActivation = (RecordingActivation)activation;
            typedActivation.IsVisible = true;
            FlushDispatcherOperations(typedActivation.Window, "Loaded", "Render");
        }

        public bool RequestActivation(object activation)
        {
            AssertSameActivation(activation);
            return true;
        }

        public void Hide(object activation)
        {
            AssertSameActivation(activation);
            ((RecordingActivation)activation).IsVisible = false;
        }

        public void SetWindowState(object activation, object windowState)
        {
            AssertSameActivation(activation);
            ((RecordingActivation)activation).WindowState = windowState;
        }

        public void SetTitle(object activation, string title)
        {
            AssertSameActivation(activation);
            ((RecordingActivation)activation).Title = title;
        }

        public void SetClientSize(object activation, double width, double height)
        {
            AssertSameActivation(activation);
            ((RecordingActivation)activation).Width = width;
            ((RecordingActivation)activation).Height = height;
        }

        public void SetPosition(object activation, double left, double top)
        {
            AssertSameActivation(activation);
            var typedActivation = (RecordingActivation)activation;
            typedActivation.Left = left;
            typedActivation.Top = top;
        }

        public void SetTopmost(object activation, bool topmost)
        {
            AssertSameActivation(activation);
            ((RecordingActivation)activation).Topmost = topmost;
        }

        public void SetWindowBorder(object activation, object resizeMode, object windowStyle)
        {
            AssertSameActivation(activation);
            var typedActivation = (RecordingActivation)activation;
            typedActivation.ResizeMode = resizeMode;
            typedActivation.WindowStyle = windowStyle;
        }

        public void Close(object activation)
        {
            AssertSameActivation(activation);
            CloseCount++;
            ((RecordingActivation)activation).IsClosed = true;
        }

        public void Run(object activation)
        {
            AssertSameActivation(activation);
            RunCount++;
            var typedActivation = (RecordingActivation)activation;
            AssertEqual(true, typedActivation.IsVisible, "startup window visible before run");
            AssertEqual("ProGPU WPF XAML smoke", typedActivation.Title, "activated window title");
            AssertEqual(420.0, typedActivation.Width, "activated window width");
            AssertEqual(340.0, typedActivation.Height, "activated window height");
            AssertEqual(false, typedActivation.Topmost, "activated window topmost");
            Invoke(typedActivation.Window, "UpdateLayout");
            ValidatePostShowLoadedEvent(typedActivation.Window);
            ValidatePostShowCommandManagerRequery(
                _presentationCore,
                typedActivation.Window,
                () => FlushDispatcherOperations(typedActivation.Window, "Background"));
            ValidatePostShowClickStoryboardEventTrigger(
                typedActivation.Window,
                () => FlushDispatcherOperations(typedActivation.Window, "Render"));
            ValidatePostShowStyleTriggerActions(
                typedActivation.Window,
                () => FlushDispatcherOperations(typedActivation.Window, "Render"));
            ValidatePostShowMultiTriggerActions(
                typedActivation.Window,
                () => FlushDispatcherOperations(typedActivation.Window, "Render"));
            ValidatePostShowDataTriggerActions(
                typedActivation.Window,
                () => FlushDispatcherOperations(typedActivation.Window, "DataBind", "Render"));
            ValidatePostShowMultiDataTriggerActions(
                typedActivation.Window,
                () => FlushDispatcherOperations(typedActivation.Window, "DataBind", "Render"));
            ValidatePostShowTemplateVisualStateManager(
                typedActivation.Window,
                () => FlushDispatcherOperations(typedActivation.Window, "Render"));
            ValidatePostShowItemTemplateTriggerActivation(_presentationCore, typedActivation.Window);
            ValidatePostShowItemContainerAlternation(typedActivation.Window);
            ValidatePostShowItemStringFormat(_presentationCore, typedActivation.Window);
            ValidatePostShowGroupStyleHeader(_presentationCore, typedActivation.Window);
            ValidatePostShowItemTemplateSelector(_presentationCore, typedActivation.Window);
            ValidatePostShowItemContainerStyleSelector(_presentationCore, typedActivation.Window);
            ValidatePostShowLiveCollectionViewShaping(
                typedActivation.Window,
                () => FlushDispatcherOperations(typedActivation.Window, "DataBind"));
            ValidatePostShowDataGridRows(_presentationCore, typedActivation.Window);
            ValidatePostShowImplicitDataTemplate(_presentationCore, typedActivation.Window);
            ValidatePostShowContentTemplateSelector(_presentationCore, typedActivation.Window);
            ValidatePostShowHierarchicalDataTemplate(_presentationCore, typedActivation.Window);
            ValidatePostShowTabControl(_presentationCore, typedActivation.Window);
            ValidatePostShowSectionControls(_presentationCore, typedActivation.Window);
            ValidatePostShowAdornerLayer(_presentationFramework, _compilerHarness, typedActivation.Window);
            ValidatePostShowAccessKeyFocusScope(_presentationCore, typedActivation.Window);
            ValidatePortableAccessKeyActivation(typedActivation.Window);
            ValidatePortableKeyboardNavigationActivation(typedActivation.Window);
            ValidatePostShowNavigationFrame(
                typedActivation.Window,
                () => FlushDispatcherOperations(typedActivation.Window, "Render"));
            ValidatePostShowSharedSizeGridLayout(typedActivation.Window);
            ValidatePostShowGridSplitterDrag(typedActivation.Window);
            ValidatePostShowSliderThumbDrag(typedActivation.Window);
            ValidatePostShowScrollingControls(typedActivation.Window);
            ValidatePortableInputBindingActivation(typedActivation.Window);
            ValidatePortableMouseBindingActivation(typedActivation.Window);
            ValidatePortableTextInputActivation(typedActivation.Window);
            ValidatePortableMouseClickActivation(typedActivation.Window);
            ValidatePortableMouseWheelActivation(typedActivation.Window);
            ValidatePortableMessageBox(_presentationFramework, typedActivation.Window);
        }

        public void Dispose(object activation)
        {
            AssertSameActivation(activation);
            DisposeCount++;
            var typedActivation = (RecordingActivation)activation;
            if (!typedActivation.IsDisposed)
            {
                typedActivation.DisposePresentationSource();
                typedActivation.IsDisposed = true;
            }
        }

        public IntPtr GetHandle(object activation)
        {
            AssertSameActivation(activation);
            var typedActivation = (RecordingActivation)activation;
            return (IntPtr)GetProperty(typedActivation.PresentationSource, "Handle");
        }

        public void ValidateAfterRun()
        {
            AssertEqual(1, ActivateCount, "startup window activation count");
            AssertEqual(1, ShowCount, "startup window show count");
            if (RunCount != 1)
            {
                throw new InvalidOperationException(
                    $"Expected portable run-loop count to be '1', got '{RunCount}'. " +
                    $"MainWindow={DescribeMainWindow()}, Activation={DescribeActivation()}.");
            }
            AssertEqual(true, RenderRequestCount > 0, "portable MediaContext render request count");
            AssertEqual(1, CloseCount, "startup window close count");
            AssertEqual(1, DisposeCount, "startup window dispose count");

            if (_activation is not RecordingActivation activation)
            {
                throw new InvalidOperationException("Application.Run did not create a recording activation.");
            }

            AssertEqual(true, activation.IsClosed, "recorded activation close state");
            AssertEqual(true, activation.IsDisposed, "recorded activation dispose state");
            ValidatePostShowBindingFeatures(activation.Window);
            ValidateLoadedEventHandlerState(activation.Window);
            ValidatePostShowItemTemplateTriggerActivation(_presentationCore, activation.Window);
            ValidatePostShowItemContainerAlternation(activation.Window);
            ValidatePostShowItemStringFormat(_presentationCore, activation.Window);
            ValidatePostShowGroupStyleHeader(_presentationCore, activation.Window);
            ValidatePostShowItemTemplateSelector(_presentationCore, activation.Window);
            ValidatePostShowItemContainerStyleSelector(_presentationCore, activation.Window);
            ValidatePostShowImplicitDataTemplate(_presentationCore, activation.Window);
            ValidatePostShowContentTemplateSelector(_presentationCore, activation.Window);
            ValidatePostShowHierarchicalDataTemplate(_presentationCore, activation.Window);
            ValidateTabControl(activation.Window);
            ValidateSectionControls(activation.Window);
            AssertEqual("mouse binding payload", GetProperty(activation.Window, "LastRoutedCommandParameter"), "portable mouse MouseBinding persisted command parameter");
            AssertEqual("portable x", GetProperty(GetField(activation.Window, "InputBox"), "Text"), "portable text input persisted TextBox text");
            AssertAtLeast(2, GetProperty(activation.Window, "XamlClickCount"), "portable mouse routed Click persisted count");
            AssertEqual("EventButton", GetProperty(activation.Window, "LastXamlClickSenderName"), "portable mouse routed Click persisted sender name");
            AssertEqual("Click", GetProperty(activation.Window, "LastXamlClickRoutedEventName"), "portable mouse routed Click persisted event name");
            AssertAtLeast(1, GetProperty(activation.Window, "XamlGotMouseCaptureCount"), "portable mouse GotMouseCapture persisted count");
            AssertAtLeast(1, GetProperty(activation.Window, "XamlLostMouseCaptureCount"), "portable mouse LostMouseCapture persisted count");
            AssertAtLeast(1, GetProperty(activation.Window, "XamlMouseWheelCount"), "portable mouse wheel persisted count");
            AssertEqual(120, GetProperty(activation.Window, "LastXamlMouseWheelDelta"), "portable mouse wheel persisted delta");
            AssertEqual("EventButton", GetProperty(activation.Window, "LastXamlMouseWheelSenderName"), "portable mouse wheel persisted sender name");
            AssertEqual("MouseWheel", GetProperty(activation.Window, "LastXamlMouseWheelRoutedEventName"), "portable mouse wheel persisted event name");
        }

        private void AssertSameActivation(object activation)
        {
            if (!ReferenceEquals(_activation, activation))
            {
                throw new InvalidOperationException("Portable activation callback received an unknown activation object.");
            }
        }

        private void FlushDispatcherOperations(object window, params string[] markerPriorityNames)
        {
            if (_isFlushingWpfDispatcher)
            {
                return;
            }

            _isFlushingWpfDispatcher = true;
            try
            {
                FlushDispatcherOperationsCore(window, markerPriorityNames);
            }
            finally
            {
                _isFlushingWpfDispatcher = false;
            }
        }

        private void FlushDispatcherOperationsCore(object window, params string[] markerPriorityNames)
        {
            MethodInfo flushMethod = _activationServiceType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .SingleOrDefault(static method =>
                    method.Name == "FlushDispatcherOperations" &&
                    method.GetParameters().Length == 2)
                ?? throw new MissingMethodException(_activationServiceType.FullName, "FlushDispatcherOperations");
            Type dispatcherPriorityType = flushMethod.GetParameters()[1].ParameterType;

            foreach (string markerPriorityName in markerPriorityNames)
            {
                object markerPriority = Enum.Parse(dispatcherPriorityType, markerPriorityName);
                flushMethod.Invoke(null, new[] { window, markerPriority });
            }
        }

        private IDisposable? RegisterMediaContextRenderService()
        {
            Type serviceType = GetRequiredType(_presentationCore, PortableMediaContextRenderServiceTypeName);

            MethodInfo? register = serviceType.GetMethod(
                "Register",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(Action<TimeSpan>) },
                modifiers: null);
            if (register != null)
            {
                return (IDisposable?)register.Invoke(null, new object[] { (Action<TimeSpan>)RequestRenderFromMediaContext });
            }

            register = serviceType.GetMethod(
                "Register",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(Action) },
                modifiers: null);
            if (register == null)
            {
                throw new MissingMethodException(serviceType.FullName, "Register");
            }

            return (IDisposable?)register.Invoke(null, new object[] { (Action)RequestRenderFromMediaContext });
        }

        private void RequestRenderFromMediaContext()
        {
            RequestRenderFromMediaContext(TimeSpan.Zero);
        }

        private void RequestRenderFromMediaContext(TimeSpan delay)
        {
            if (_isDisposed || _activation is not RecordingActivation)
            {
                return;
            }

            RenderRequestCount++;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _mediaContextRenderRegistration?.Dispose();
            _isDisposed = true;
        }

        private object CreatePortablePresentationSource(object window)
        {
            Type sourceType = GetRequiredType(_presentationCore, PortablePresentationSourceTypeName);
            object source = Activator.CreateInstance(
                sourceType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: Array.Empty<object>(),
                culture: null)
                ?? throw new InvalidOperationException($"Failed to create '{PortablePresentationSourceTypeName}'.");
            SetProperty(source, "RootVisual", window);
            return source;
        }

        private void ValidatePortableInputBindingActivation(object window)
        {
            object inputBox = GetField(window, "InputBox");
            Type keyboardType = GetRequiredType(_presentationCore, "System.Windows.Input.Keyboard");
            object focused = InvokeStatic(keyboardType, "Focus", inputBox);
            AssertSame(inputBox, focused, "portable Application.Run input KeyBinding focused target");

            int initialExecutionCount = Convert.ToInt32(GetProperty(window, "RoutedCommandExecutionCount"));
            object keyDown = CreatePortableInputEvent("KeyDown", "F6", scanCode: 0, modifiersName: "Control");
            Invoke(window, "HandlePortableInput", keyDown);

            AssertEqual(true, GetProperty(keyDown, "Handled"), "portable Application.Run input KeyBinding handled state");
            AssertEqual(initialExecutionCount + 1, GetProperty(window, "RoutedCommandExecutionCount"), "portable Application.Run input KeyBinding command execution count");
            AssertEqual("input binding payload", GetProperty(window, "LastRoutedCommandParameter"), "portable Application.Run input KeyBinding command parameter");

            object keyUp = CreatePortableInputEvent("KeyUp", "F6", scanCode: 0, modifiersName: "None");
            Invoke(window, "HandlePortableInput", keyUp);
            AssertEqual(initialExecutionCount + 1, GetProperty(window, "RoutedCommandExecutionCount"), "portable Application.Run input KeyBinding ignores key up");

            object classCommandTarget = GetField(window, "ClassCommandTargetBox");
            SetProperty(classCommandTarget, "IsClassCommandEnabled", true);
            object classFocused = InvokeStatic(keyboardType, "Focus", classCommandTarget);
            AssertSame(classCommandTarget, classFocused, "portable Application.Run class input KeyBinding focused target");

            int initialClassExecutionCount = Convert.ToInt32(GetProperty(classCommandTarget, "ClassCommandExecutionCount"));
            object classKeyDown = CreatePortableInputEvent("KeyDown", "F7", scanCode: 0, modifiersName: "Control");
            Invoke(window, "HandlePortableInput", classKeyDown);

            AssertEqual(true, GetProperty(classKeyDown, "Handled"), "portable Application.Run class input KeyBinding handled state");
            AssertEqual(initialClassExecutionCount + 1, GetProperty(classCommandTarget, "ClassCommandExecutionCount"), "portable Application.Run class input KeyBinding command execution count");
            AssertEqual("class input payload", GetProperty(classCommandTarget, "LastClassCommandParameter"), "portable Application.Run class input KeyBinding command parameter");

            object classKeyUp = CreatePortableInputEvent("KeyUp", "F7", scanCode: 0, modifiersName: "None");
            Invoke(window, "HandlePortableInput", classKeyUp);
            AssertEqual(initialClassExecutionCount + 1, GetProperty(classCommandTarget, "ClassCommandExecutionCount"), "portable Application.Run class input KeyBinding ignores key up");

            InvokeStatic(keyboardType, "ClearFocus");
            AssertEqual(null, TryGetStaticProperty(keyboardType, "FocusedElement"), "portable Application.Run input KeyBinding clear focus");
        }

        private void ValidatePortableAccessKeyActivation(object window)
        {
            object accessTarget = GetField(window, "AccessTargetBox");
            Type keyboardType = GetRequiredType(_presentationCore, "System.Windows.Input.Keyboard");
            InvokeStatic(keyboardType, "ClearFocus");

            object accessText = CreatePortableInputEvent("TextInput", key: null, scanCode: 0, character: 'a', modifiersName: "Alt");
            Invoke(window, "HandlePortableInput", accessText);

            AssertEqual(true, GetProperty(accessText, "Handled"), "portable Application.Run access key handled state");
            AssertSame(accessTarget, GetStaticProperty(keyboardType, "FocusedElement"), "portable Application.Run access key focused target");

            InvokeStatic(keyboardType, "ClearFocus");
            AssertEqual(null, TryGetStaticProperty(keyboardType, "FocusedElement"), "portable Application.Run access key clear focus");
        }

        private void ValidatePortableKeyboardNavigationActivation(object window)
        {
            object accessTarget = GetField(window, "AccessTargetBox");
            object alternateAccessTarget = GetField(window, "AlternateAccessTargetBox");
            Type keyboardType = GetRequiredType(_presentationCore, "System.Windows.Input.Keyboard");

            AssertSame(accessTarget, InvokeStatic(keyboardType, "Focus", accessTarget), "portable Application.Run Tab navigation initial focus");

            object tabDown = CreatePortableInputEvent("KeyDown", "Tab", scanCode: 0, modifiersName: "None");
            Invoke(window, "HandlePortableInput", tabDown);

            AssertEqual(true, GetProperty(tabDown, "Handled"), "portable Application.Run Tab navigation handled state");
            AssertSame(alternateAccessTarget, GetStaticProperty(keyboardType, "FocusedElement"), "portable Application.Run Tab navigation focused target");

            Invoke(window, "HandlePortableInput", CreatePortableInputEvent("KeyUp", "Tab", scanCode: 0, modifiersName: "None"));

            object shiftTabDown = CreatePortableInputEvent("KeyDown", "Tab", scanCode: 0, modifiersName: "Shift");
            Invoke(window, "HandlePortableInput", shiftTabDown);

            AssertEqual(true, GetProperty(shiftTabDown, "Handled"), "portable Application.Run Shift+Tab navigation handled state");
            AssertSame(accessTarget, GetStaticProperty(keyboardType, "FocusedElement"), "portable Application.Run Shift+Tab navigation focused target");

            Invoke(window, "HandlePortableInput", CreatePortableInputEvent("KeyUp", "Tab", scanCode: 0, modifiersName: "None"));

            InvokeStatic(keyboardType, "ClearFocus");
            AssertEqual(null, TryGetStaticProperty(keyboardType, "FocusedElement"), "portable Application.Run Tab navigation clear focus");
        }

        private void ValidatePortableMouseBindingActivation(object window)
        {
            object mouseBindingSurface = GetField(window, "MouseBindingSurface");
            Invoke(window, "UpdateLayout");
            Invoke(mouseBindingSurface, "UpdateLayout");
            (double x, double y) = GetElementCenterInWindow(_presentationCore, mouseBindingSurface, window);
            object? directHit = InvokeNullable(window, "InputHitTest", GetElementCenterPointInWindow(mouseBindingSurface, window));

            int initialExecutionCount = Convert.ToInt32(GetProperty(window, "RoutedCommandExecutionCount"));
            object mouseMove = CreatePortableInputEvent("MouseMove", x: x, y: y);
            Invoke(window, "HandlePortableInput", mouseMove);

            Type mouseType = GetRequiredType(_presentationCore, "System.Windows.Input.Mouse");
            object? directlyOverAfterMove = TryGetStaticProperty(mouseType, "DirectlyOver");
            if (directlyOverAfterMove == null)
            {
                throw new InvalidOperationException(
                    $"Expected portable Application.Run mouse move to update Mouse.DirectlyOver for MouseBinding. " +
                    $"MoveHandled={GetProperty(mouseMove, "Handled")}, Input=({x}, {y}), InputHitTest={DescribeInputElement(directHit)}.");
            }

            object rightDown = CreatePortableInputEvent("MouseDown", x: x, y: y, buttonName: "Right");
            Invoke(window, "HandlePortableInput", rightDown);

            AssertPortableMouseBindingCommand(
                window,
                mouseBindingSurface,
                directHit,
                initialExecutionCount + 1,
                x,
                y,
                "portable Application.Run mouse MouseBinding command execution count");
            AssertEqual("mouse binding payload", GetProperty(window, "LastRoutedCommandParameter"), "portable Application.Run mouse MouseBinding command parameter");

            Invoke(window, "HandlePortableInput", CreatePortableInputEvent("MouseUp", x: x, y: y, buttonName: "Right"));
            AssertEqual(initialExecutionCount + 1, GetProperty(window, "RoutedCommandExecutionCount"), "portable Application.Run mouse MouseBinding ignores mouse up");
        }

        private void AssertPortableMouseBindingCommand(
            object window,
            object mouseBindingSurface,
            object? directHit,
            int expectedExecutionCount,
            double x,
            double y,
            string description)
        {
            object actualExecutionCount = GetProperty(window, "RoutedCommandExecutionCount");
            if (Equals(expectedExecutionCount, actualExecutionCount))
            {
                return;
            }

            Type mouseType = GetRequiredType(_presentationCore, "System.Windows.Input.Mouse");
            object? directlyOver = TryGetStaticProperty(mouseType, "DirectlyOver");
            object? captured = TryGetStaticProperty(mouseType, "Captured");
            throw new InvalidOperationException(
                $"Expected {description} to be '{expectedExecutionCount}', got '{actualExecutionCount}'. " +
                $"Input=({x}, {y}), DirectlyOver={DescribeInputElement(directlyOver)}, " +
                $"InputHitTest={DescribeInputElement(directHit)}, " +
                $"Captured={DescribeInputElement(captured)}, " +
                $"Surface.IsMouseOver={GetProperty(mouseBindingSurface, "IsMouseOver")}, " +
                $"Surface.IsMouseDirectlyOver={GetProperty(mouseBindingSurface, "IsMouseDirectlyOver")}.");
        }

        private static string DescribeInputElement(object? element)
        {
            if (element == null)
            {
                return "<null>";
            }

            object? name = TryGetProperty(element, "Name");
            return $"{element.GetType().FullName}(Name={name ?? "<null>"})";
        }

        private void ValidatePortableTextInputActivation(object window)
        {
            object inputBox = GetField(window, "InputBox");
            Type keyboardType = GetRequiredType(_presentationCore, "System.Windows.Input.Keyboard");
            SetProperty(inputBox, "Text", "portable ");
            Invoke(inputBox, "Select", "portable ".Length, 0);
            object focused = InvokeStatic(keyboardType, "Focus", inputBox);
            AssertSame(inputBox, focused, "portable Application.Run text input focused target");

            object textInput = CreatePortableInputEvent("TextInput", key: null, scanCode: 0, character: 'x', modifiersName: "None");
            Invoke(window, "HandlePortableInput", textInput);

            AssertEqual(true, GetProperty(textInput, "Handled"), "portable Application.Run text input handled state");
            AssertEqual("portable x", GetProperty(inputBox, "Text"), "portable Application.Run text input TextBox text");
            AssertEqual("portable x".Length, GetProperty(inputBox, "SelectionStart"), "portable Application.Run text input caret index");
            AssertEqual(0, GetProperty(inputBox, "SelectionLength"), "portable Application.Run text input selection length");

            InvokeStatic(keyboardType, "ClearFocus");
            AssertEqual(null, TryGetStaticProperty(keyboardType, "FocusedElement"), "portable Application.Run text input clear focus");
        }

        private void ValidatePortableMouseClickActivation(object window)
        {
            object eventButton = GetField(window, "EventButton");
            Invoke(window, "UpdateLayout");
            Invoke(eventButton, "UpdateLayout");
            (double x, double y) = GetVisibleElementInputPointInWindow(_presentationCore, eventButton, window);

            int initialClickCount = Convert.ToInt32(GetProperty(window, "XamlClickCount"));
            int initialGotCaptureCount = Convert.ToInt32(GetProperty(window, "XamlGotMouseCaptureCount"));
            int initialLostCaptureCount = Convert.ToInt32(GetProperty(window, "XamlLostMouseCaptureCount"));
            Type mouseType = GetRequiredType(_presentationCore, "System.Windows.Input.Mouse");

            Invoke(window, "HandlePortableInput", CreatePortableInputEvent("MouseMove", x: x, y: y));
            Invoke(window, "HandlePortableInput", CreatePortableInputEvent("MouseDown", x: x, y: y, buttonName: "Left"));
            object capturedAfterDown = TryGetStaticProperty(mouseType, "Captured")
                ?? throw new InvalidOperationException(
                    $"Expected portable Application.Run mouse capture after mouse down at ({x}, {y}); " +
                    $"DirectlyOver={DescribeInputElement(TryGetStaticProperty(mouseType, "DirectlyOver"))}, " +
                    $"Button.IsMouseOver={GetProperty(eventButton, "IsMouseOver")}, " +
                    $"Button.IsMouseDirectlyOver={GetProperty(eventButton, "IsMouseDirectlyOver")}.");
            AssertSame(eventButton, capturedAfterDown, "portable Application.Run mouse captured element after down");
            AssertEqual(true, GetProperty(eventButton, "IsMouseCaptured"), "portable Application.Run mouse ButtonBase IsMouseCaptured after down");
            AssertEqual(true, GetProperty(eventButton, "IsPressed"), "portable Application.Run mouse ButtonBase IsPressed after down");
            AssertEqual(initialGotCaptureCount + 1, GetProperty(window, "XamlGotMouseCaptureCount"), "portable Application.Run mouse GotMouseCapture count");
            AssertEqual("EventButton", GetProperty(window, "LastXamlGotMouseCaptureSenderName"), "portable Application.Run mouse GotMouseCapture sender name");
            AssertEqual("GotMouseCapture", GetProperty(window, "LastXamlGotMouseCaptureRoutedEventName"), "portable Application.Run mouse GotMouseCapture event name");

            Invoke(window, "HandlePortableInput", CreatePortableInputEvent("MouseUp", x: x, y: y, buttonName: "Left"));
            AssertEqual(null, TryGetStaticProperty(mouseType, "Captured"), "portable Application.Run mouse captured element after up");
            AssertEqual(false, GetProperty(eventButton, "IsMouseCaptured"), "portable Application.Run mouse ButtonBase IsMouseCaptured after up");
            AssertEqual(false, GetProperty(eventButton, "IsPressed"), "portable Application.Run mouse ButtonBase IsPressed after up");
            AssertEqual(initialLostCaptureCount + 1, GetProperty(window, "XamlLostMouseCaptureCount"), "portable Application.Run mouse LostMouseCapture count");
            AssertEqual("EventButton", GetProperty(window, "LastXamlLostMouseCaptureSenderName"), "portable Application.Run mouse LostMouseCapture sender name");
            AssertEqual("LostMouseCapture", GetProperty(window, "LastXamlLostMouseCaptureRoutedEventName"), "portable Application.Run mouse LostMouseCapture event name");

            AssertEqual(initialClickCount + 1, GetProperty(window, "XamlClickCount"), "portable Application.Run mouse routed Click count");
            AssertEqual("EventButton", GetProperty(window, "LastXamlClickSenderName"), "portable Application.Run mouse routed Click sender name");
            AssertEqual("Click", GetProperty(window, "LastXamlClickRoutedEventName"), "portable Application.Run mouse routed Click event name");
        }

        private void ValidatePortableMouseWheelActivation(object window)
        {
            object eventButton = GetField(window, "EventButton");
            Invoke(window, "UpdateLayout");
            Invoke(eventButton, "UpdateLayout");
            (double x, double y) = GetVisibleElementInputPointInWindow(_presentationCore, eventButton, window);

            int initialWheelCount = Convert.ToInt32(GetProperty(window, "XamlMouseWheelCount"));
            Invoke(window, "HandlePortableInput", CreatePortableInputEvent("MouseWheel", x: x, y: y, deltaY: 1));

            AssertEqual(initialWheelCount + 1, GetProperty(window, "XamlMouseWheelCount"), "portable Application.Run mouse wheel routed event count");
            AssertEqual(120, GetProperty(window, "LastXamlMouseWheelDelta"), "portable Application.Run mouse wheel routed event delta");
            AssertEqual("EventButton", GetProperty(window, "LastXamlMouseWheelSenderName"), "portable Application.Run mouse wheel sender name");
            AssertEqual("MouseWheel", GetProperty(window, "LastXamlMouseWheelRoutedEventName"), "portable Application.Run mouse wheel routed event name");
        }

        private object CreatePortableInputEvent(
            string kindName,
            string? key = null,
            int scanCode = 0,
            string modifiersName = "None",
            char? character = null,
            double x = 0,
            double y = 0,
            double deltaX = 0,
            double deltaY = 0,
            string buttonName = "None")
        {
            Assembly presentationFramework = _activationServiceType.Assembly;
            Type argsType = GetRequiredType(presentationFramework, "System.Windows.PortableInputEventArgs");
            Type kindType = GetRequiredType(presentationFramework, "System.Windows.PortableInputEventKind");
            Type buttonType = GetRequiredType(presentationFramework, "System.Windows.PortableMouseButton");
            Type modifiersType = GetRequiredType(presentationFramework, "System.Windows.PortableInputModifiers");

            return Activator.CreateInstance(
                argsType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new object?[]
                {
                    Enum.Parse(kindType, kindName),
                    key,
                    scanCode,
                    character,
                    x,
                    y,
                    deltaX,
                    deltaY,
                    Enum.Parse(buttonType, buttonName),
                    Enum.Parse(modifiersType, modifiersName)
                },
                culture: null)
                ?? throw new InvalidOperationException($"Failed to create '{argsType.FullName}'.");
        }

        private string DescribeMainWindow()
        {
            object? mainWindow = TryGetProperty(_application, "MainWindow");
            if (mainWindow == null)
            {
                return "<null>";
            }

            object? portableActivation = TryGetProperty(mainWindow, "PortableWindowActivation");
            return $"{mainWindow.GetType().FullName}, PortableWindowActivation={(portableActivation == null ? "<null>" : portableActivation.GetType().FullName)}";
        }

        private string DescribeActivation()
        {
            return _activation == null ? "<null>" : _activation.GetType().FullName ?? "<unknown>";
        }
    }

    private sealed class RecordingActivation
    {
        public RecordingActivation(object window, object presentationSource)
        {
            Window = window;
            PresentationSource = presentationSource;
        }

        public object Window { get; }

        public object PresentationSource { get; }

        public bool IsVisible { get; set; }

        public bool IsClosed { get; set; }

        public bool IsDisposed { get; set; }

        public string Title { get; set; } = string.Empty;

        public double Width { get; set; }

        public double Height { get; set; }

        public double Left { get; set; }

        public double Top { get; set; }

        public bool Topmost { get; set; }

        public object? WindowState { get; set; }

        public object? ResizeMode { get; set; }

        public object? WindowStyle { get; set; }

        public void DisposePresentationSource()
        {
            if (PresentationSource is IDisposable disposable)
            {
                disposable.Dispose();
                return;
            }

            MethodInfo? dispose = PresentationSource.GetType().GetMethod(
                nameof(IDisposable.Dispose),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null);
            dispose?.Invoke(PresentationSource, Array.Empty<object>());
        }
    }

    private sealed class WpfAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly string _repoRoot;
        private readonly string _presentationFrameworkPath;
        private readonly string _presentationCorePath;
        private readonly string _compilerHarnessPath;
        private readonly AssemblyDependencyResolver _resolver;

        public WpfAssemblyLoadContext(
            string repoRoot,
            string presentationFrameworkPath,
            string presentationCorePath,
            string compilerHarnessPath)
            : base(isCollectible: true)
        {
            _repoRoot = repoRoot;
            _presentationFrameworkPath = presentationFrameworkPath;
            _presentationCorePath = presentationCorePath;
            _compilerHarnessPath = compilerHarnessPath;
            _resolver = new AssemblyDependencyResolver(compilerHarnessPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.Equals(assemblyName.Name, CompilerHarnessAssemblyName, StringComparison.Ordinal))
            {
                return LoadFromAssemblyPath(_compilerHarnessPath);
            }

            if (string.Equals(assemblyName.Name, "PresentationFramework", StringComparison.Ordinal))
            {
                return LoadFromAssemblyPath(_presentationFrameworkPath);
            }

            if (string.Equals(assemblyName.Name, "PresentationCore", StringComparison.Ordinal))
            {
                return LoadFromAssemblyPath(_presentationCorePath);
            }

            string? artifactAssemblyPath = TryFindArtifactAssembly(_repoRoot, assemblyName);
            if (artifactAssemblyPath != null)
            {
                return LoadFromAssemblyPath(artifactAssemblyPath);
            }

            string outputAssemblyPath = Path.Combine(
                AppContext.BaseDirectory,
                $"{assemblyName.Name}.dll");
            if (File.Exists(outputAssemblyPath))
            {
                return LoadFromAssemblyPath(outputAssemblyPath);
            }

            string? resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
            return resolvedPath == null ? null : LoadFromAssemblyPath(resolvedPath);
        }
    }
}
