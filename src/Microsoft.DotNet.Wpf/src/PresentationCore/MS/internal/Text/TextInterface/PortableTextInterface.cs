// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using ProGpuSfntFontFace = ProGPU.Text.SfntFontFace;
using ProGpuSfntGlyphBounds = ProGPU.Text.SfntGlyphBounds;
using ProGpuSfntFontSubsetter = ProGPU.Text.SfntFontSubsetter;
using ProGpuSfntHorizontalGlyphMetrics = ProGPU.Text.SfntHorizontalGlyphMetrics;
using ProGpuSfntSimpleGlyphMetrics = ProGPU.Text.SfntSimpleGlyphMetrics;
using ProGpuSfntSimpleGlyphRun = ProGPU.Text.SfntSimpleGlyphRun;
using ProGpuSfntSimpleGlyphShaper = ProGPU.Text.SfntSimpleGlyphShaper;

namespace MS.Internal.Text.TextInterface
{
    internal enum FactoryType
    {
        Shared,
        Isolated
    }

    internal enum FontWeight
    {
        Thin = 100,
        ExtraLight = 200,
        UltraLight = 200,
        Light = 300,
        Normal = 400,
        Regular = 400,
        Medium = 500,
        DemiBold = 600,
        SemiBOLD = 600,
        Bold = 700,
        ExtraBold = 800,
        UltraBold = 800,
        Black = 900,
        Heavy = 900,
        ExtraBlack = 950,
        UltraBlack = 950
    }

    internal enum FontStyle
    {
        Normal = 0,
        Oblique = 1,
        Italic = 2
    }

    internal enum FontStretch
    {
        Undefined = 0,
        UltraCondensed = 1,
        ExtraCondensed = 2,
        Condensed = 3,
        SemiCondensed = 4,
        Normal = 5,
        Medium = 5,
        SemiExpanded = 6,
        Expanded = 7,
        ExtraExpanded = 8,
        UltraExpanded = 9
    }

    [Flags]
    internal enum FontSimulations
    {
        None = 0x0000,
        Bold = 0x0001,
        Oblique = 0x0002
    }

    internal enum FontFaceType
    {
        CFF,
        TrueType,
        TrueTypeCollection,
        Type1,
        Vector,
        Bitmap,
        Unknown
    }

    internal enum OpenTypeTableTag
    {
        TTO_GSUB = 0x47535542,
        TTO_GPOS = 0x47504F53,
        TTO_GDEF = 0x47444546
    }

    internal enum InformationalStringID
    {
        CopyrightNotice,
        VersionStrings,
        Trademark,
        Manufacturer,
        Designer,
        DesignerURL,
        Description,
        FontVendorURL,
        LicenseDescription,
        SampleText,
        Win32SubFamilyNames,
        WIN32FamilyNames,
        PreferredSubFamilyNames,
        PreferredFamilyNames
    }

    internal enum DWriteFontFeatureTag
    {
        AlternateAnnotationForms,
        AlternateHalfWidth,
        AlternativeFractions,
        CapitalSpacing,
        CaseSensitiveForms,
        ContextualAlternates,
        ContextualLigatures,
        ContextualSwash,
        DiscretionaryLigatures,
        ExpertForms,
        Fractions,
        FullWidth,
        HalfWidth,
        HistoricalForms,
        HistoricalLigatures,
        HojoKanjiForms,
        JIS04Forms,
        JIS78Forms,
        JIS83Forms,
        JIS90Forms,
        Kerning,
        LiningFigures,
        MathematicalGreek,
        NLCKanjiForms,
        OldStyleFigures,
        Ordinals,
        PetiteCapitals,
        PetiteCapitalsFromCapitals,
        ProportionalAlternateWidth,
        ProportionalFigures,
        ProportionalWidths,
        QuarterWidths,
        RubyNotationForms,
        ScientificInferiors,
        SimplifiedForms,
        SlashedZero,
        SmallCapitals,
        SmallCapitalsFromCapitals,
        StandardLigatures,
        StylisticAlternates,
        StylisticSet1,
        StylisticSet2,
        StylisticSet3,
        StylisticSet4,
        StylisticSet5,
        StylisticSet6,
        StylisticSet7,
        StylisticSet8,
        StylisticSet9,
        StylisticSet10,
        StylisticSet11,
        StylisticSet12,
        StylisticSet13,
        StylisticSet14,
        StylisticSet15,
        StylisticSet16,
        StylisticSet17,
        StylisticSet18,
        StylisticSet19,
        StylisticSet20,
        Subscript,
        Superscript,
        Swash,
        TabularFigures,
        ThirdWidths,
        Titling,
        TraditionalForms,
        TraditionalNameForms,
        Unicase
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DWriteFontFeature
    {
        internal DWriteFontFeatureTag nameTag;
        internal uint parameter;

        internal DWriteFontFeature(DWriteFontFeatureTag nameTag, uint parameter)
        {
            this.nameTag = nameTag;
            this.parameter = parameter;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GlyphOffset
    {
        internal int du;
        internal int dv;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GlyphMetrics
    {
        internal int LeftSideBearing;
        internal uint AdvanceWidth;
        internal int RightSideBearing;
        internal int TopSideBearing;
        internal uint AdvanceHeight;
        internal int BottomSideBearing;
        internal int VerticalOriginY;
    }

    internal sealed class FontMetrics
    {
        internal ushort DesignUnitsPerEm = 1;
        internal ushort Ascent;
        internal ushort Descent;
        internal short LineGap;
        internal ushort CapHeight;
        internal ushort XHeight;
        internal short UnderlinePosition;
        internal ushort UnderlineThickness;
        internal short StrikethroughPosition;
        internal ushort StrikethroughThickness;

        internal double Baseline => DesignUnitsPerEm == 0 ? 0 : (Ascent + LineGap * 0.5) / DesignUnitsPerEm;

        internal double LineSpacing => DesignUnitsPerEm == 0 ? 0 : (double)(Ascent + Descent + LineGap) / DesignUnitsPerEm;
    }

    internal interface IClassification
    {
        void GetCharAttribute(
            int unicodeScalar,
            out bool isCombining,
            out bool needsCaretInfo,
            out bool isIndic,
            out bool isDigit,
            out bool isLatin,
            out bool isStrong);
    }

    internal interface IFontSource
    {
        bool IsFile { get; }
        bool IsComposite { get; }
        Uri Uri { get; }
        bool IsAppSpecific { get; }
        string GetUriString();
        string ToStringUpperInvariant();
        DateTime GetLastWriteTimeUtc();
        UnmanagedMemoryStream GetUnmanagedStream();
        void TestFileOpenable();
        Stream GetStream();
    }

    internal interface IFontSourceFactory
    {
        IFontSource Create(string uriString);
    }

    internal static class LocalizedErrorMsgs
    {
        internal static string EnumeratorNotStarted { get; set; }
        internal static string EnumeratorReachedEnd { get; set; }
    }

    internal sealed class LocalizedStrings : Dictionary<CultureInfo, string>
    {
        internal uint StringsCount => checked((uint)Count);

        internal bool FindLocaleName(string localeName, out uint index)
        {
            uint current = 0;
            foreach (CultureInfo cultureInfo in Keys)
            {
                if (string.Equals(cultureInfo.Name, localeName, StringComparison.OrdinalIgnoreCase))
                {
                    index = current;
                    return true;
                }

                current++;
            }

            index = uint.MaxValue;
            return false;
        }

        internal string GetLocaleName(uint index)
        {
            return GetAt(index).Key.Name;
        }

        internal string GetString(uint index)
        {
            return GetAt(index).Value;
        }

        private KeyValuePair<CultureInfo, string> GetAt(uint index)
        {
            uint current = 0;
            foreach (KeyValuePair<CultureInfo, string> pair in this)
            {
                if (current == index)
                {
                    return pair;
                }

                current++;
            }

            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    internal sealed unsafe class Factory
    {
        private readonly IFontSourceCollectionFactory _fontSourceCollectionFactory;
        private readonly IFontSourceFactory _fontSourceFactory;
        private FontCollection _systemFontCollection;
        private readonly object _systemFontCollectionLock = new object();

        private Factory(IFontSourceCollectionFactory fontSourceCollectionFactory, IFontSourceFactory fontSourceFactory)
        {
            _fontSourceCollectionFactory = fontSourceCollectionFactory;
            _fontSourceFactory = fontSourceFactory;
        }

        internal Native.IDWriteFactory* DWriteFactory => null;

        internal static Factory Create(
            FactoryType factoryType,
            IFontSourceCollectionFactory fontSourceCollectionFactory,
            IFontSourceFactory fontSourceFactory)
        {
            return new Factory(fontSourceCollectionFactory, fontSourceFactory);
        }

        internal FontFile CreateFontFile(Uri filePathUri)
        {
            CreateFontSource(filePathUri)?.TestFileOpenable();
            return new FontFile(filePathUri);
        }

        internal FontFace CreateFontFace(Uri filePathUri, uint faceIndex)
        {
            return CreateFontFace(filePathUri, faceIndex, FontSimulations.None);
        }

        internal FontFace CreateFontFace(Uri filePathUri, uint faceIndex, FontSimulations fontSimulationFlags)
        {
            PortableFontData fontData = PortableFontData.LoadFace(filePathUri, CreateFontSource(filePathUri), faceIndex);
            Font font = Font.CreateStandalone(fontData, fontSimulationFlags);
            return font.GetFontFace();
        }

        internal FontCollection GetSystemFontCollection()
        {
            if (_systemFontCollection == null)
            {
                lock (_systemFontCollectionLock)
                {
                    if (_systemFontCollection == null)
                    {
                        _systemFontCollection = FontCollection.FromUris(GetSystemFontUris(), _fontSourceFactory);
                    }
                }
            }

            return _systemFontCollection;
        }

        internal FontCollection GetFontCollection(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);

            IFontSourceCollection fontSources = _fontSourceCollectionFactory?.Create(uri.AbsoluteUri);
            if (fontSources != null)
            {
                return FontCollection.FromFontSources(fontSources);
            }

            return FontCollection.FromUris(new[] { uri }, _fontSourceFactory);
        }

        internal TextAnalyzer CreateTextAnalyzer()
        {
            return new TextAnalyzer();
        }

        internal static bool IsLocalUri(Uri uri)
        {
            return uri.IsFile && uri.IsLoopback && !uri.IsUnc;
        }

        private IFontSource CreateFontSource(Uri uri)
        {
            return _fontSourceFactory?.Create(uri.AbsoluteUri);
        }

        private static IEnumerable<Uri> GetSystemFontUris()
        {
            string bundledSymbolFont = Path.Combine(
                AppContext.BaseDirectory,
                "LibreWPF",
                "Fonts",
                "LibreWPF.FluentSymbols.ttf");
            if (File.Exists(bundledSymbolFont))
            {
                yield return new Uri(bundledSymbolFont, UriKind.Absolute);
            }

            foreach (string directory in GetSystemFontDirectories())
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (string file in files)
                {
                    if (PortableFontData.IsSupportedFontPath(file))
                    {
                        yield return new Uri(file, UriKind.Absolute);
                    }
                }
            }
        }

        private static IEnumerable<string> GetSystemFontDirectories()
        {
            if (OperatingSystem.IsMacOS())
            {
                yield return "/System/Library/Fonts";
                yield return "/System/Library/Fonts/Supplemental";
                yield return "/Library/Fonts";
                yield return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library",
                    "Fonts");
            }
            else if (OperatingSystem.IsLinux())
            {
                yield return "/usr/share/fonts";
                yield return "/usr/local/share/fonts";
                yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts");
                yield return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local",
                    "share",
                    "fonts");
            }
            else if (OperatingSystem.IsWindows())
            {
                yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            }
        }
    }

    internal sealed class FontCollection
    {
        internal static readonly FontCollection Empty = new FontCollection(Array.Empty<FontFamily>());

        private static readonly (string FamilyName, string[] Candidates)[] s_portableFamilyAliases =
        {
            ("Arial", new[] { "Arial", "Helvetica Neue", "Helvetica", "SF Pro Text", ".SF NS Text", "Roboto", "Liberation Sans", "DejaVu Sans", "Noto Sans", "LibreWPF Fluent Symbols" }),
            ("Segoe UI", new[] { "Arial", "Helvetica Neue", "Helvetica", "SF Pro Text", ".SF NS Text", "Roboto", "Liberation Sans", "DejaVu Sans", "Noto Sans" }),
            ("Segoe UI Light", new[] { "Arial", "Helvetica Neue", "Helvetica", "SF Pro Text", ".SF NS Text", "Roboto", "Liberation Sans", "DejaVu Sans", "Noto Sans" }),
            ("Segoe UI Semibold", new[] { "Arial", "Helvetica Neue", "Helvetica", "SF Pro Text", ".SF NS Text", "Roboto", "Liberation Sans", "DejaVu Sans", "Noto Sans" }),
            ("Segoe Fluent Icons", new[] { "LibreWPF Fluent Symbols" }),
            ("Segoe MDL2 Assets", new[] { "LibreWPF Fluent Symbols" }),
            ("Calibri", new[] { "Arial", "Helvetica Neue", "Helvetica", "SF Pro Text", ".SF NS Text", "Roboto", "Liberation Sans", "DejaVu Sans", "Noto Sans" }),
            ("Cambria", new[] { "Georgia", "Times New Roman", "Times", "Liberation Serif", "DejaVu Serif", "Noto Serif" }),
            ("Consolas", new[] { "Menlo", "Monaco", "Courier New", "Courier", "Liberation Mono", "DejaVu Sans Mono", "Noto Sans Mono" }),
            ("Comic Sans MS", new[] { "Comic Sans MS", "Comic Sans", "Comic Relief", "Chilanka", "URW Chancery L", "Z003", "Arial", "Helvetica Neue", "Helvetica", "Roboto", "Liberation Sans", "DejaVu Sans", "Noto Sans" }),
            ("Courier New", new[] { "Courier New", "Courier", "Menlo", "Monaco", "Liberation Mono", "DejaVu Sans Mono", "Noto Sans Mono" }),
            ("Microsoft Sans Serif", new[] { "Arial", "Helvetica Neue", "Helvetica", "SF Pro Text", ".SF NS Text", "Roboto", "Liberation Sans", "DejaVu Sans", "Noto Sans" }),
            ("Tahoma", new[] { "Arial", "Helvetica Neue", "Helvetica", "SF Pro Text", ".SF NS Text", "Roboto", "Liberation Sans", "DejaVu Sans", "Noto Sans" }),
            ("Verdana", new[] { "Arial", "Helvetica Neue", "Helvetica", "SF Pro Text", ".SF NS Text", "Roboto", "Liberation Sans", "DejaVu Sans", "Noto Sans" }),
            ("Times New Roman", new[] { "Times New Roman", "Times", "Georgia", "Liberation Serif", "DejaVu Serif", "Noto Serif" })
        };

        private readonly IReadOnlyList<FontFamily> _families;

        internal FontCollection(IReadOnlyList<FontFamily> families)
        {
            _families = families;
        }

        internal static FontCollection FromUris(IEnumerable<Uri> uris, IFontSourceFactory fontSourceFactory)
        {
            List<PortableFontData> fonts = new List<PortableFontData>();

            foreach (Uri uri in uris)
            {
                IFontSource fontSource = fontSourceFactory?.Create(uri.AbsoluteUri);
                AddFontsFromSource(fontSource, uri, fonts);
            }

            return FromFontData(fonts);
        }

        internal static FontCollection FromFontSources(IEnumerable<IFontSource> fontSources)
        {
            List<PortableFontData> fonts = new List<PortableFontData>();

            foreach (IFontSource fontSource in fontSources)
            {
                if (fontSource.IsComposite)
                {
                    continue;
                }

                AddFontsFromSource(fontSource, fontSource.Uri, fonts);
            }

            return FromFontData(fonts);
        }

        private static void AddFontsFromSource(IFontSource fontSource, Uri uri, List<PortableFontData> fonts)
        {
            try
            {
                fonts.AddRange(PortableFontData.LoadFaces(uri, fontSource));
            }
            catch (Exception ex) when (ex is FileFormatException || ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
            }
        }

        private static FontCollection FromFontData(IEnumerable<PortableFontData> fontData)
        {
            Dictionary<string, List<Font>> familyMap = new Dictionary<string, List<Font>>(StringComparer.OrdinalIgnoreCase);

            foreach (PortableFontData data in fontData)
            {
                string familyName = data.FamilyName;
                if (!familyMap.TryGetValue(familyName, out List<Font> familyFonts))
                {
                    familyFonts = new List<Font>();
                    familyMap.Add(familyName, familyFonts);
                }

                familyFonts.Add(new Font(data, FontSimulations.None));
            }

            List<FontFamily> families = new List<FontFamily>(familyMap.Count);
            foreach (KeyValuePair<string, List<Font>> pair in familyMap)
            {
                LocalizedStrings familyNames = pair.Value.Count > 0
                    ? pair.Value[0].FontData.GetNameStrings(PortableFontData.NameIdPreferredFamily, PortableFontData.NameIdFamily, pair.Key)
                    : PortableFontData.CreateInvariantStrings(pair.Key);

                FontFamily family = new FontFamily(pair.Key, familyNames, pair.Value);
                foreach (Font font in pair.Value)
                {
                    font.SetFamily(family);
                }

                families.Add(family);
            }

            families.Sort((left, right) => string.Compare(left.OrdinalName, right.OrdinalName, StringComparison.OrdinalIgnoreCase));
            return families.Count == 0 ? Empty : new FontCollection(families);
        }

        internal uint FamilyCount => checked((uint)_families.Count);

        internal FontFamily this[uint familyIndex] => _families[checked((int)familyIndex)];

        internal FontFamily this[string familyName]
        {
            get
            {
                return FindFamilyName(familyName, out uint index) ? this[index] : null;
            }
        }

        internal bool FindFamilyName(string familyName, out uint index)
        {
            if (TryFindFamilyName(familyName, out index))
            {
                return true;
            }

            foreach ((string aliasName, string[] candidates) in s_portableFamilyAliases)
            {
                if (!string.Equals(aliasName, familyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (string candidate in candidates)
                {
                    if (TryFindFamilyName(candidate, out index))
                    {
                        return true;
                    }
                }

                break;
            }

            index = uint.MaxValue;
            return false;
        }

        private bool TryFindFamilyName(string familyName, out uint index)
        {
            for (int i = 0; i < _families.Count; i++)
            {
                if (string.Equals(_families[i].OrdinalName, familyName, StringComparison.OrdinalIgnoreCase))
                {
                    index = checked((uint)i);
                    return true;
                }
            }

            index = uint.MaxValue;
            return false;
        }

        internal Font GetFontFromFontFace(FontFace fontFace)
        {
            ArgumentNullException.ThrowIfNull(fontFace);

            Font faceFont = fontFace.Font;
            if (faceFont != null)
            {
                return faceFont;
            }

            for (int familyIndex = 0; familyIndex < _families.Count; familyIndex++)
            {
                foreach (Font font in _families[familyIndex])
                {
                    if (font.FontData.HasSameSource(fontFace.FontData))
                    {
                        return font;
                    }
                }
            }

            return Font.CreateStandalone(fontFace.FontData, fontFace.SimulationFlags);
        }
    }

    internal sealed class FontFamily : IEnumerable<Font>
    {
        private readonly IReadOnlyList<Font> _fonts;

        internal FontFamily(string ordinalName, LocalizedStrings familyNames, IReadOnlyList<Font> fonts)
        {
            OrdinalName = ordinalName;
            FamilyNames = familyNames;
            _fonts = fonts;
        }

        internal LocalizedStrings FamilyNames { get; }

        internal bool IsPhysical => true;

        internal bool IsComposite => false;

        internal string OrdinalName { get; }

        internal uint Count => checked((uint)_fonts.Count);

        internal FontMetrics Metrics => _fonts.Count == 0 ? new FontMetrics() : _fonts[0].Metrics;

        internal FontMetrics DisplayMetrics(float emSize, float pixelsPerDip)
        {
            return Metrics;
        }

        internal Font GetFirstMatchingFont(FontWeight weight, FontStretch stretch, FontStyle style)
        {
            if (_fonts.Count == 0)
            {
                return null;
            }

            Font bestFont = _fonts[0];
            int bestScore = int.MaxValue;

            foreach (Font font in _fonts)
            {
                int score = Math.Abs((int)font.Weight - (int)weight) * 2
                    + Math.Abs((int)font.Stretch - (int)stretch) * 25
                    + (font.Style == style ? 0 : 1000);

                if (score < bestScore)
                {
                    bestFont = font;
                    bestScore = score;
                }
            }

            return bestFont;
        }

        public IEnumerator<Font> GetEnumerator()
        {
            return _fonts.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    internal sealed class Font
    {
        private FontFamily _family;
        private readonly FontSimulations _simulationFlags;

        internal Font(PortableFontData fontData, FontSimulations simulationFlags)
        {
            FontData = fontData;
            _simulationFlags = simulationFlags;
            FaceNames = fontData.GetNameStrings(PortableFontData.NameIdPreferredSubfamily, PortableFontData.NameIdSubfamily, fontData.FaceName);
        }

        internal PortableFontData FontData { get; }

        internal FontFamily Family => _family;

        internal FontWeight Weight => FontData.Weight;

        internal FontStretch Stretch => FontData.Stretch;

        internal FontStyle Style => FontData.Style;

        internal bool IsSymbolFont => FontData.IsSymbolFont;

        internal LocalizedStrings FaceNames { get; }

        internal FontSimulations SimulationFlags => _simulationFlags;

        internal FontMetrics Metrics => FontData.Metrics;

        internal double Version => FontData.Version;

        internal bool HasDWriteFont => false;

        internal IntPtr DWriteFontAddRef => IntPtr.Zero;

        internal bool HasCharacter(uint unicodeScalar)
        {
            return FontData.GetGlyphIndex(unicodeScalar) != 0;
        }

        internal FontMetrics DisplayMetrics(float emSize, float pixelsPerDip)
        {
            return Metrics;
        }

        internal static void ResetFontFaceCache()
        {
        }

        internal FontFace GetFontFace()
        {
            return new FontFace(this, FontData, _simulationFlags);
        }

        internal bool GetInformationalStrings(InformationalStringID informationalStringID, out LocalizedStrings localizedStrings)
        {
            return FontData.TryGetInformationalStrings(informationalStringID, out localizedStrings);
        }

        internal void SetFamily(FontFamily family)
        {
            _family = family;
        }

        internal static Font CreateStandalone(PortableFontData fontData, FontSimulations simulationFlags)
        {
            Font font = new Font(fontData, simulationFlags);
            FontFamily family = new FontFamily(
                fontData.FamilyName,
                fontData.GetNameStrings(PortableFontData.NameIdPreferredFamily, PortableFontData.NameIdFamily, fontData.FamilyName),
                new[] { font });

            font.SetFamily(family);
            return font;
        }
    }

    internal sealed unsafe class FontFace : IDisposable
    {
        private readonly Font _font;
        private readonly PortableFontData _fontData;
        private readonly FontSimulations _simulationFlags;

        internal FontFace(Font font, PortableFontData fontData, FontSimulations simulationFlags)
        {
            _font = font;
            _fontData = fontData;
            _simulationFlags = simulationFlags;
        }

        internal Font Font => _font;

        internal PortableFontData FontData => _fontData;

        internal FontFaceType Type => _fontData.FaceType;

        internal uint Index => _fontData.FaceIndex;

        internal FontSimulations SimulationFlags => _simulationFlags;

        internal bool IsSymbolFont => _fontData.IsSymbolFont;

        internal FontMetrics Metrics => _fontData.Metrics;

        internal ushort GlyphCount => _fontData.GlyphCount;

        internal IntPtr DWriteFontFaceAddRef => IntPtr.Zero;

        internal FontFile GetFileZero()
        {
            return new FontFile(_fontData.SourceUri);
        }

        internal void AddRef()
        {
        }

        internal void Release()
        {
            Dispose();
        }

        internal void GetDesignGlyphMetrics(ushort* pGlyphIndices, uint glyphCount, GlyphMetrics* pGlyphMetrics)
        {
            for (uint i = 0; i < glyphCount; i++)
            {
                pGlyphMetrics[i] = _fontData.GetGlyphMetrics(pGlyphIndices[i]);
            }
        }

        internal void GetDisplayGlyphMetrics(
            ushort* pGlyphIndices,
            uint glyphCount,
            GlyphMetrics* pGlyphMetrics,
            float emSize,
            bool useDisplayNatural,
            bool isSideways,
            float pixelsPerDip)
        {
            GetDesignGlyphMetrics(pGlyphIndices, glyphCount, pGlyphMetrics);
        }

        internal void GetArrayOfGlyphIndices(uint* pCodePoints, uint glyphCount, ushort* pGlyphIndices)
        {
            for (uint i = 0; i < glyphCount; i++)
            {
                pGlyphIndices[i] = _fontData.GetGlyphIndex(pCodePoints[i]);
            }
        }

        internal bool TryGetFontTable(OpenTypeTableTag openTypeTableTag, out byte[] tableData)
        {
            return _fontData.TryGetTable((uint)openTypeTableTag, out tableData);
        }

        internal bool ReadFontEmbeddingRights(out ushort fsType)
        {
            return _fontData.TryGetEmbeddingRights(out fsType);
        }

        public void Dispose()
        {
        }
    }

    internal sealed class PortableFontData
    {
        internal const ushort NameIdCopyright = 0;
        internal const ushort NameIdFamily = 1;
        internal const ushort NameIdSubfamily = 2;
        internal const ushort NameIdUniqueIdentifier = 3;
        internal const ushort NameIdFullName = 4;
        internal const ushort NameIdVersion = 5;
        internal const ushort NameIdTrademark = 7;
        internal const ushort NameIdManufacturer = 8;
        internal const ushort NameIdDesigner = 9;
        internal const ushort NameIdDescription = 10;
        internal const ushort NameIdVendorUrl = 11;
        internal const ushort NameIdDesignerUrl = 12;
        internal const ushort NameIdLicense = 13;
        internal const ushort NameIdSampleText = 19;
        internal const ushort NameIdPreferredFamily = 16;
        internal const ushort NameIdPreferredSubfamily = 17;

        private const uint TagTrueTypeCollection = 0x74746366;
        private const uint TagHead = 0x68656164;
        private const uint TagHhea = 0x68686561;
        private const uint TagMaxp = 0x6D617870;
        private const uint TagHmtx = 0x686D7478;
        private const uint TagCmap = 0x636D6170;
        private const uint TagName = 0x6E616D65;
        private const uint TagOs2 = 0x4F532F32;
        private const uint TagPost = 0x706F7374;
        private const uint TagCff = 0x43464620;

        private byte[] _data;
        private volatile ProGpuSfntFontFace _sfntFace;
        private readonly IFontSource _fontSource;
        private readonly object _fontDataLock = new object();
        private readonly Dictionary<uint, TableRecord> _tables = new Dictionary<uint, TableRecord>();
        private readonly Dictionary<ushort, LocalizedStrings> _nameStrings = new Dictionary<ushort, LocalizedStrings>();
        private readonly uint _faceOffset;
        private readonly bool _isCollection;

        private PortableFontData(
            byte[] data,
            Uri sourceUri,
            IFontSource fontSource,
            uint faceIndex,
            uint faceOffset,
            bool isCollection,
            bool retainFontData)
        {
            _data = data;
            _fontSource = fontSource;
            SourceUri = sourceUri;
            FaceIndex = faceIndex;
            _faceOffset = faceOffset;
            _isCollection = isCollection;
            _sfntFace = ProGpuSfntFontFace.Load(data, checked((int)faceIndex));

            ParseTableDirectory();
            ParseNames();

            FamilyName = GetFirstName(NameIdPreferredFamily, NameIdFamily)
                ?? Path.GetFileNameWithoutExtension(SourceUri.IsFile ? SourceUri.LocalPath : SourceUri.AbsoluteUri);
            FaceName = GetFirstName(NameIdPreferredSubfamily, NameIdSubfamily) ?? "Regular";
            FullName = GetFirstName(NameIdFullName) ?? string.Concat(FamilyName, " ", FaceName).Trim();

            (Metrics, Version) = ParseMetrics();
            (Weight, Stretch, Style) = ParseOs2AndStyle();
            FaceType = _isCollection ? FontFaceType.TrueTypeCollection : (_tables.ContainsKey(TagCff) ? FontFaceType.CFF : FontFaceType.TrueType);
            GlyphCount = ParseGlyphCount();
            IsSymbolFont = _sfntFace.UsesSymbolCharacterMap;

            // System font discovery needs only the compact metadata captured above. Keeping every
            // complete font file resident made the portable catalog retain hundreds of megabytes
            // (and multi-gigabyte TTC files on some systems). Rehydrate only the selected faces.
            if (!retainFontData)
            {
                _sfntFace = null;
                _data = null;
            }
        }

        private PortableFontData(
            Uri sourceUri,
            IFontSource fontSource,
            uint faceIndex,
            uint faceOffset,
            bool isCollection,
            CatalogFaceMetadata metadata)
        {
            SourceUri = sourceUri;
            FaceIndex = faceIndex;
            _faceOffset = faceOffset;
            _isCollection = isCollection;
            _fontSource = fontSource;

            foreach (KeyValuePair<uint, TableRecord> table in metadata.Tables)
            {
                _tables.Add(table.Key, table.Value);
            }

            foreach (KeyValuePair<ushort, LocalizedStrings> name in metadata.NameStrings)
            {
                _nameStrings.Add(name.Key, name.Value);
            }

            FamilyName = metadata.FamilyName;
            FaceName = metadata.FaceName;
            FullName = metadata.FullName;
            Metrics = metadata.Metrics;
            Weight = metadata.Weight;
            Stretch = metadata.Stretch;
            Style = metadata.Style;
            FaceType = metadata.FaceType;
            GlyphCount = metadata.GlyphCount;
            IsSymbolFont = metadata.IsSymbolFont;
            Version = metadata.Version;
        }

        internal Uri SourceUri { get; }

        internal uint FaceIndex { get; }

        internal string FamilyName { get; }

        internal string FaceName { get; }

        internal string FullName { get; }

        internal FontMetrics Metrics { get; }

        internal FontWeight Weight { get; }

        internal FontStretch Stretch { get; }

        internal FontStyle Style { get; }

        internal FontFaceType FaceType { get; }

        internal ushort GlyphCount { get; }

        internal bool IsSymbolFont { get; }

        internal double Version { get; }

        internal bool IsFontDataResident => _data != null && _sfntFace != null;

        internal static bool IsSupportedFontPath(string path)
        {
            string extension = Path.GetExtension(path);
            return string.Equals(extension, ".ttf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".otf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".ttc", StringComparison.OrdinalIgnoreCase);
        }

        internal static IReadOnlyList<PortableFontData> LoadFaces(Uri uri, IFontSource fontSource)
        {
            if ((fontSource != null && fontSource.IsFile) || (fontSource == null && uri.IsFile))
            {
                return LoadFileCatalogFaces(uri, fontSource);
            }

            byte[] data = ReadFontBytes(uri, fontSource);
            List<uint> faceOffsets = GetFaceOffsets(data);
            List<PortableFontData> faces = new List<PortableFontData>(faceOffsets.Count);
            bool isCollection = ReadUInt(data, 0) == TagTrueTypeCollection;

            for (int i = 0; i < faceOffsets.Count; i++)
            {
                faces.Add(CreateFace(
                    data,
                    uri,
                    fontSource,
                    checked((uint)i),
                    faceOffsets[i],
                    isCollection,
                    retainFontData: false));
            }

            return faces;
        }

        private static IReadOnlyList<PortableFontData> LoadFileCatalogFaces(Uri uri, IFontSource fontSource)
        {
            string path = fontSource != null ? fontSource.Uri.LocalPath : uri.LocalPath;
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.RandomAccess);

            (uint[] faceOffsets, bool isCollection) = ReadFileFaceOffsets(stream, uri);
            var faces = new List<PortableFontData>(faceOffsets.Length);
            for (int index = 0; index < faceOffsets.Length; index++)
            {
                CatalogFaceMetadata metadata = ReadCatalogFaceMetadata(stream, uri, faceOffsets[index], isCollection);
                faces.Add(new PortableFontData(
                    uri,
                    fontSource,
                    checked((uint)index),
                    faceOffsets[index],
                    isCollection,
                    metadata));
            }

            return faces;
        }

        internal static PortableFontData LoadFace(Uri uri, IFontSource fontSource, uint faceIndex)
        {
            byte[] data = ReadFontBytes(uri, fontSource);
            List<uint> faceOffsets = GetFaceOffsets(data);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(faceIndex, checked((uint)faceOffsets.Count), nameof(faceIndex));

            bool isCollection = ReadUInt(data, 0) == TagTrueTypeCollection;
            return CreateFace(
                data,
                uri,
                fontSource,
                faceIndex,
                faceOffsets[checked((int)faceIndex)],
                isCollection,
                retainFontData: true);
        }

        internal static LocalizedStrings CreateInvariantStrings(string value)
        {
            LocalizedStrings strings = new LocalizedStrings();
            if (!string.IsNullOrEmpty(value))
            {
                strings[CultureInfo.InvariantCulture] = value;
            }

            return strings;
        }

        internal bool HasSameSource(PortableFontData other)
        {
            return other != null
                && FaceIndex == other.FaceIndex
                && string.Equals(SourceUri.AbsoluteUri, other.SourceUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        }

        internal LocalizedStrings GetNameStrings(ushort preferredNameId, ushort fallbackNameId, string fallback)
        {
            if (_nameStrings.TryGetValue(preferredNameId, out LocalizedStrings preferred) && preferred.Count > 0)
            {
                return preferred;
            }

            if (_nameStrings.TryGetValue(fallbackNameId, out LocalizedStrings fallbackStrings) && fallbackStrings.Count > 0)
            {
                return fallbackStrings;
            }

            return CreateInvariantStrings(fallback);
        }

        internal bool TryGetInformationalStrings(InformationalStringID informationalStringID, out LocalizedStrings localizedStrings)
        {
            ushort nameId = informationalStringID switch
            {
                InformationalStringID.CopyrightNotice => NameIdCopyright,
                InformationalStringID.VersionStrings => NameIdVersion,
                InformationalStringID.Trademark => NameIdTrademark,
                InformationalStringID.Manufacturer => NameIdManufacturer,
                InformationalStringID.Designer => NameIdDesigner,
                InformationalStringID.DesignerURL => NameIdDesignerUrl,
                InformationalStringID.Description => NameIdDescription,
                InformationalStringID.FontVendorURL => NameIdVendorUrl,
                InformationalStringID.LicenseDescription => NameIdLicense,
                InformationalStringID.SampleText => NameIdSampleText,
                InformationalStringID.Win32SubFamilyNames => NameIdSubfamily,
                InformationalStringID.WIN32FamilyNames => NameIdFamily,
                InformationalStringID.PreferredSubFamilyNames => NameIdPreferredSubfamily,
                InformationalStringID.PreferredFamilyNames => NameIdPreferredFamily,
                _ => 0
            };

            if (nameId != 0 && _nameStrings.TryGetValue(nameId, out localizedStrings) && localizedStrings.Count > 0)
            {
                return true;
            }

            localizedStrings = null;
            return false;
        }

        internal ushort GetGlyphIndex(uint codePoint)
        {
            return GetSfntFace().TryGetGlyphIndex(codePoint, out ushort glyphIndex)
                ? glyphIndex
                : (ushort)0;
        }

        internal GlyphMetrics GetGlyphMetrics(ushort glyphIndex)
        {
            if (glyphIndex >= GlyphCount)
            {
                throw new ArgumentOutOfRangeException(nameof(glyphIndex));
            }

            ProGpuSfntFontFace sfntFace = GetSfntFace();
            ProGpuSfntHorizontalGlyphMetrics horizontalMetrics = sfntFace.TryGetHorizontalGlyphMetrics(glyphIndex, out ProGpuSfntHorizontalGlyphMetrics metrics)
                ? metrics
                : new ProGpuSfntHorizontalGlyphMetrics(checked((ushort)(Metrics.DesignUnitsPerEm / 2)), 0);
            ProGpuSfntGlyphBounds bounds = sfntFace.TryGetGlyphBounds(glyphIndex, out ProGpuSfntGlyphBounds glyphBounds)
                ? glyphBounds
                : default;

            int blackBoxWidth = bounds.XMax - bounds.XMin;
            int advanceHeight = Metrics.Ascent + Metrics.Descent + Math.Max(0, (int)Metrics.LineGap);
            if (advanceHeight <= 0)
            {
                advanceHeight = Metrics.DesignUnitsPerEm;
            }

            return new GlyphMetrics
            {
                LeftSideBearing = horizontalMetrics.LeftSideBearing,
                AdvanceWidth = horizontalMetrics.AdvanceWidth,
                RightSideBearing = horizontalMetrics.AdvanceWidth - horizontalMetrics.LeftSideBearing - blackBoxWidth,
                TopSideBearing = Metrics.Ascent - bounds.YMax,
                AdvanceHeight = checked((uint)advanceHeight),
                BottomSideBearing = advanceHeight - Metrics.Ascent + bounds.YMin,
                VerticalOriginY = Metrics.Ascent
            };
        }

        internal bool TryGetTable(uint tag, out byte[] tableData)
        {
            if (GetSfntFace().TryGetTable(TagToString(tag), out ReadOnlyMemory<byte> tableDataMemory))
            {
                tableData = tableDataMemory.ToArray();
                return true;
            }

            if (_tables.TryGetValue(tag, out TableRecord table))
            {
                byte[] data = GetFontData();
                tableData = new byte[table.Length];
                Array.Copy(data, checked((int)table.Offset), tableData, 0, checked((int)table.Length));
                return true;
            }

            tableData = null;
            return false;
        }

        internal bool TryGetEmbeddingRights(out ushort fsType)
        {
            return GetSfntFace().TryGetEmbeddingRights(out fsType);
        }

        private static PortableFontData CreateFace(
            byte[] data,
            Uri uri,
            IFontSource fontSource,
            uint faceIndex,
            uint faceOffset,
            bool isCollection,
            bool retainFontData)
        {
            try
            {
                return new PortableFontData(data, uri, fontSource, faceIndex, faceOffset, isCollection, retainFontData);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is IndexOutOfRangeException || ex is OverflowException)
            {
                throw new FileFormatException(uri.AbsoluteUri, ex);
            }
        }

        private static byte[] ReadFontBytes(Uri uri, IFontSource fontSource)
        {
            if (fontSource != null)
            {
                if (fontSource.IsFile)
                {
                    return File.ReadAllBytes(fontSource.Uri.LocalPath);
                }

                using Stream stream = fontSource.GetStream();
                if (stream.CanSeek)
                {
                    long remainingLength = checked(stream.Length - stream.Position);
                    byte[] data = GC.AllocateUninitializedArray<byte>(checked((int)remainingLength));
                    stream.ReadExactly(data);
                    return data;
                }

                using MemoryStream memoryStream = new MemoryStream();
                stream.CopyTo(memoryStream);
                return memoryStream.ToArray();
            }

            if (!uri.IsFile)
            {
                throw new NotSupportedException("The portable WPF text interface only supports local font files when no font source factory is available.");
            }

            return File.ReadAllBytes(uri.LocalPath);
        }

        private static (uint[] faceOffsets, bool isCollection) ReadFileFaceOffsets(Stream stream, Uri uri)
        {
            Span<byte> header = stackalloc byte[12];
            ReadExactly(stream, 0, header, uri);
            if (ReadSpanUInt(header, 0) != TagTrueTypeCollection)
            {
                return (new[] { 0u }, false);
            }

            uint faceCountValue = ReadSpanUInt(header, 8);
            if (faceCountValue == 0 || faceCountValue > 4096)
            {
                throw new FileFormatException(uri.AbsoluteUri);
            }

            var faceOffsets = new uint[checked((int)faceCountValue)];
            Span<byte> offsetBytes = stackalloc byte[4];
            for (int index = 0; index < faceOffsets.Length; index++)
            {
                ReadExactly(stream, 12L + (index * 4L), offsetBytes, uri);
                faceOffsets[index] = ReadSpanUInt(offsetBytes, 0);
            }

            return (faceOffsets, true);
        }

        private static CatalogFaceMetadata ReadCatalogFaceMetadata(Stream stream, Uri uri, uint faceOffset, bool isCollection)
        {
            Span<byte> header = stackalloc byte[12];
            ReadExactly(stream, faceOffset, header, uri);
            uint sfntVersion = ReadSpanUInt(header, 0);
            if (sfntVersion != 0x00010000 && sfntVersion != 0x4F54544F)
            {
                throw new FileFormatException(uri.AbsoluteUri);
            }

            ushort tableCount = ReadSpanUShort(header, 4);
            if (tableCount > 4096)
            {
                throw new FileFormatException(uri.AbsoluteUri);
            }

            var tables = new Dictionary<uint, TableRecord>(tableCount);
            Span<byte> recordBytes = stackalloc byte[16];
            for (int index = 0; index < tableCount; index++)
            {
                ReadExactly(stream, checked((long)faceOffset + 12L + (index * 16L)), recordBytes, uri);
                uint tag = ReadSpanUInt(recordBytes, 0);
                uint offset = ReadSpanUInt(recordBytes, 8);
                uint length = ReadSpanUInt(recordBytes, 12);
                EnsureFileRange(stream, offset, length, uri);
                tables[tag] = new TableRecord(offset, length);
            }

            TableRecord head = RequireCatalogTable(tables, TagHead, uri);
            TableRecord hhea = RequireCatalogTable(tables, TagHhea, uri);
            TableRecord maxp = RequireCatalogTable(tables, TagMaxp, uri);
            _ = RequireCatalogTable(tables, TagHmtx, uri);
            TableRecord cmap = RequireCatalogTable(tables, TagCmap, uri);

            byte[] headData = ReadTablePrefix(stream, head, 54, uri);
            byte[] hheaData = ReadTablePrefix(stream, hhea, 12, uri);
            byte[] maxpData = ReadTablePrefix(stream, maxp, 6, uri);
            if (headData.Length < 54 || hheaData.Length < 12 || maxpData.Length < 6)
            {
                throw new FileFormatException(uri.AbsoluteUri);
            }

            byte[] os2Data = tables.TryGetValue(TagOs2, out TableRecord os2)
                ? ReadTablePrefix(stream, os2, 90, uri)
                : Array.Empty<byte>();
            byte[] postData = tables.TryGetValue(TagPost, out TableRecord post)
                ? ReadTablePrefix(stream, post, 12, uri)
                : Array.Empty<byte>();

            var metrics = new FontMetrics
            {
                DesignUnitsPerEm = ReadSpanUShort(headData, 18),
                Ascent = ToPositiveMetric(unchecked((short)ReadSpanUShort(hheaData, 4))),
                Descent = ToPositiveMetric(unchecked((short)ReadSpanUShort(hheaData, 6))),
                LineGap = unchecked((short)ReadSpanUShort(hheaData, 8)),
                CapHeight = 0,
                XHeight = 0,
                UnderlinePosition = 0,
                UnderlineThickness = 0,
                StrikethroughPosition = 0,
                StrikethroughThickness = 0
            };
            if (metrics.DesignUnitsPerEm == 0)
            {
                metrics.DesignUnitsPerEm = 1;
            }

            if (os2Data.Length >= 30)
            {
                metrics.StrikethroughThickness = ReadSpanUShort(os2Data, 26);
                metrics.StrikethroughPosition = unchecked((short)ReadSpanUShort(os2Data, 28));
            }

            if (os2Data.Length >= 90)
            {
                metrics.XHeight = ToPositiveMetric(unchecked((short)ReadSpanUShort(os2Data, 86)));
                metrics.CapHeight = ToPositiveMetric(unchecked((short)ReadSpanUShort(os2Data, 88)));
            }

            if (postData.Length >= 12)
            {
                metrics.UnderlinePosition = unchecked((short)ReadSpanUShort(postData, 8));
                metrics.UnderlineThickness = ToPositiveMetric(unchecked((short)ReadSpanUShort(postData, 10)));
            }

            if (metrics.CapHeight == 0)
            {
                metrics.CapHeight = checked((ushort)Math.Max(1, (metrics.DesignUnitsPerEm * 7) / 10));
            }

            if (metrics.XHeight == 0)
            {
                metrics.XHeight = checked((ushort)Math.Max(1, metrics.DesignUnitsPerEm / 2));
            }

            if (metrics.UnderlineThickness == 0)
            {
                metrics.UnderlineThickness = checked((ushort)Math.Max(1, metrics.DesignUnitsPerEm / 20));
            }

            FontWeight weight = FontWeight.Normal;
            FontStretch stretch = FontStretch.Normal;
            FontStyle style = FontStyle.Normal;
            if (os2Data.Length >= 10)
            {
                weight = (FontWeight)Math.Clamp((int)ReadSpanUShort(os2Data, 4), 1, 1000);
                ushort widthClass = ReadSpanUShort(os2Data, 6);
                if (widthClass is >= 1 and <= 9)
                {
                    stretch = (FontStretch)widthClass;
                }
            }

            if (os2Data.Length >= 64 && (ReadSpanUShort(os2Data, 62) & 0x0001) != 0)
            {
                style = FontStyle.Italic;
            }

            ushort macStyle = ReadSpanUShort(headData, 44);
            if ((macStyle & 0x0002) != 0)
            {
                style = FontStyle.Italic;
            }

            if ((macStyle & 0x0001) != 0 && (int)weight < (int)FontWeight.Bold)
            {
                weight = FontWeight.Bold;
            }

            var nameStrings = new Dictionary<ushort, LocalizedStrings>();
            if (tables.TryGetValue(TagName, out TableRecord name))
            {
                ParseCatalogNames(ReadTable(stream, name, uri), nameStrings);
            }

            string fallbackName = Path.GetFileNameWithoutExtension(uri.IsFile ? uri.LocalPath : uri.AbsoluteUri);
            string familyName = GetFirstCatalogName(nameStrings, NameIdPreferredFamily, NameIdFamily) ?? fallbackName;
            string faceName = GetFirstCatalogName(nameStrings, NameIdPreferredSubfamily, NameIdSubfamily) ?? "Regular";
            string fullName = GetFirstCatalogName(nameStrings, NameIdFullName) ?? string.Concat(familyName, " ", faceName).Trim();

            return new CatalogFaceMetadata(
                tables,
                nameStrings,
                familyName,
                faceName,
                fullName,
                metrics,
                weight,
                stretch,
                style,
                isCollection ? FontFaceType.TrueTypeCollection : (tables.ContainsKey(TagCff) ? FontFaceType.CFF : FontFaceType.TrueType),
                ReadSpanUShort(maxpData, 4),
                HasSymbolCharacterMap(stream, cmap, uri),
                unchecked((short)ReadSpanUShort(headData, 4)) + (ReadSpanUShort(headData, 6) / 65536.0));
        }

        private static void ParseCatalogNames(byte[] table, Dictionary<ushort, LocalizedStrings> nameStrings)
        {
            if (table.Length < 6)
            {
                return;
            }

            ushort count = ReadSpanUShort(table, 2);
            ushort stringOffset = ReadSpanUShort(table, 4);
            for (int index = 0; index < count; index++)
            {
                int recordOffset = checked(6 + (index * 12));
                if (recordOffset > table.Length - 12)
                {
                    break;
                }

                ushort platformId = ReadSpanUShort(table, recordOffset);
                ushort languageId = ReadSpanUShort(table, recordOffset + 4);
                ushort nameId = ReadSpanUShort(table, recordOffset + 6);
                ushort length = ReadSpanUShort(table, recordOffset + 8);
                ushort nameOffset = ReadSpanUShort(table, recordOffset + 10);
                int valueOffset = checked(stringOffset + nameOffset);
                if (valueOffset < 0 || valueOffset > table.Length || length > table.Length - valueOffset)
                {
                    continue;
                }

                string value = DecodeName(table.AsSpan(valueOffset, length), platformId);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (!nameStrings.TryGetValue(nameId, out LocalizedStrings strings))
                {
                    strings = new LocalizedStrings();
                    nameStrings[nameId] = strings;
                }

                CultureInfo culture = GetCulture(platformId, languageId);
                if (!strings.ContainsKey(culture))
                {
                    strings[culture] = value.Trim();
                }
            }
        }

        private static string GetFirstCatalogName(Dictionary<ushort, LocalizedStrings> nameStrings, params ushort[] nameIds)
        {
            foreach (ushort nameId in nameIds)
            {
                if (!nameStrings.TryGetValue(nameId, out LocalizedStrings strings))
                {
                    continue;
                }

                if (strings.TryGetValue(CultureInfo.GetCultureInfo("en-US"), out string english) && !string.IsNullOrEmpty(english))
                {
                    return english;
                }

                foreach (string value in strings.Values)
                {
                    if (!string.IsNullOrEmpty(value))
                    {
                        return value;
                    }
                }
            }

            return null;
        }

        private static bool HasSymbolCharacterMap(Stream stream, TableRecord cmap, Uri uri)
        {
            byte[] header = ReadTablePrefix(stream, cmap, 4, uri);
            if (header.Length < 4)
            {
                return false;
            }

            ushort subtableCount = ReadSpanUShort(header, 2);
            int prefixLength = checked(4 + (subtableCount * 8));
            byte[] records = ReadTablePrefix(stream, cmap, prefixLength, uri);
            if (records.Length < prefixLength)
            {
                return false;
            }

            bool hasUnicodeFormat4 = false;
            bool hasSymbolFormat4 = false;
            Span<byte> formatBytes = stackalloc byte[2];
            for (int index = 0; index < subtableCount; index++)
            {
                int offset = 4 + (index * 8);
                ushort platformId = ReadSpanUShort(records, offset);
                ushort encodingId = ReadSpanUShort(records, offset + 2);
                uint subtableOffset = ReadSpanUInt(records, offset + 4);
                if (subtableOffset > cmap.Length || cmap.Length - subtableOffset < 2)
                {
                    continue;
                }

                ReadExactly(stream, checked((long)cmap.Offset + subtableOffset), formatBytes, uri);
                ushort format = ReadSpanUShort(formatBytes, 0);
                bool isUnicode = platformId == 0 || (platformId == 3 && encodingId is 1 or 10);
                hasUnicodeFormat4 |= format == 4 && isUnicode;
                hasSymbolFormat4 |= format == 4 && platformId == 3 && encodingId == 0;
            }

            return !hasUnicodeFormat4 && hasSymbolFormat4;
        }

        private static TableRecord RequireCatalogTable(Dictionary<uint, TableRecord> tables, uint tag, Uri uri)
        {
            if (tables.TryGetValue(tag, out TableRecord table))
            {
                return table;
            }

            throw new FileFormatException(uri.AbsoluteUri);
        }

        private static byte[] ReadTable(Stream stream, TableRecord table, Uri uri)
        {
            byte[] data = GC.AllocateUninitializedArray<byte>(checked((int)table.Length));
            ReadExactly(stream, table.Offset, data, uri);
            return data;
        }

        private static byte[] ReadTablePrefix(Stream stream, TableRecord table, int maximumLength, Uri uri)
        {
            int length = checked((int)Math.Min(table.Length, checked((uint)maximumLength)));
            byte[] data = GC.AllocateUninitializedArray<byte>(length);
            ReadExactly(stream, table.Offset, data, uri);
            return data;
        }

        private static void EnsureFileRange(Stream stream, uint offset, uint length, Uri uri)
        {
            if (offset > stream.Length || length > stream.Length - offset)
            {
                throw new FileFormatException(uri.AbsoluteUri);
            }
        }

        private static void ReadExactly(Stream stream, long offset, Span<byte> destination, Uri uri)
        {
            if (offset < 0 || offset > stream.Length || destination.Length > stream.Length - offset)
            {
                throw new FileFormatException(uri.AbsoluteUri);
            }

            stream.Position = offset;
            stream.ReadExactly(destination);
        }

        private static ushort ReadSpanUShort(ReadOnlySpan<byte> data, int offset)
        {
            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        private static uint ReadSpanUInt(ReadOnlySpan<byte> data, int offset)
        {
            return ((uint)data[offset] << 24)
                | ((uint)data[offset + 1] << 16)
                | ((uint)data[offset + 2] << 8)
                | data[offset + 3];
        }

        private static string DecodeName(ReadOnlySpan<byte> bytes, ushort platformId)
        {
            string value = platformId == 0 || platformId == 3
                ? Encoding.BigEndianUnicode.GetString(bytes)
                : Encoding.Latin1.GetString(bytes);

            return value.Replace("\0", string.Empty);
        }

        private ProGpuSfntFontFace GetSfntFace()
        {
            ProGpuSfntFontFace sfntFace = _sfntFace;
            if (sfntFace != null)
            {
                return sfntFace;
            }

            lock (_fontDataLock)
            {
                sfntFace = _sfntFace;
                if (sfntFace == null)
                {
                    byte[] data = ReadFontBytes(SourceUri, _fontSource);
                    sfntFace = ProGpuSfntFontFace.Load(data, checked((int)FaceIndex));
                    _data = data;
                    _sfntFace = sfntFace;
                }
            }

            return sfntFace;
        }

        private byte[] GetFontData()
        {
            _ = GetSfntFace();
            return _data;
        }

        private static List<uint> GetFaceOffsets(byte[] data)
        {
            try
            {
                IReadOnlyList<ProGpuSfntFontFace> faces = ProGpuSfntFontFace.LoadFaces(data);
                List<uint> offsets = new List<uint>(faces.Count);
                foreach (ProGpuSfntFontFace face in faces)
                {
                    offsets.Add(face.BaseOffset);
                }

                return offsets;
            }
            catch (FormatException ex)
            {
                throw new FileFormatException("Invalid SFNT font data.", ex);
            }
        }

        private void ParseTableDirectory()
        {
            uint sfntVersion = ReadUInt(_faceOffset);
            if (sfntVersion != 0x00010000 && sfntVersion != 0x4F54544F)
            {
                throw new FileFormatException(SourceUri.AbsoluteUri);
            }

            ushort tableCount = ReadUShort(_faceOffset + 4);
            uint directoryOffset = _faceOffset + 12;
            for (int i = 0; i < tableCount; i++)
            {
                uint recordOffset = directoryOffset + checked((uint)(i * 16));
                uint tag = ReadUInt(recordOffset);
                uint tableOffset = ReadUInt(recordOffset + 8);
                uint tableLength = ReadUInt(recordOffset + 12);
                EnsureRange(tableOffset, tableLength);
                _tables[tag] = new TableRecord(tableOffset, tableLength);
            }

            RequireTable(TagHead);
            RequireTable(TagHhea);
            RequireTable(TagMaxp);
            RequireTable(TagHmtx);
            RequireTable(TagCmap);
        }

        private (FontMetrics metrics, double version) ParseMetrics()
        {
            TableRecord head = RequireTable(TagHead);
            TableRecord hhea = RequireTable(TagHhea);
            FontMetrics metrics = new FontMetrics
            {
                DesignUnitsPerEm = ReadUShort(head.Offset + 18),
                Ascent = ToPositiveMetric(ReadShort(hhea.Offset + 4)),
                Descent = ToPositiveMetric(ReadShort(hhea.Offset + 6)),
                LineGap = ReadShort(hhea.Offset + 8),
                CapHeight = 0,
                XHeight = 0,
                UnderlinePosition = 0,
                UnderlineThickness = 0,
                StrikethroughPosition = 0,
                StrikethroughThickness = 0
            };

            if (metrics.DesignUnitsPerEm == 0)
            {
                metrics.DesignUnitsPerEm = 1;
            }

            if (_tables.TryGetValue(TagOs2, out TableRecord os2))
            {
                metrics.StrikethroughThickness = ReadUShort(os2.Offset + 26);
                metrics.StrikethroughPosition = ReadShort(os2.Offset + 28);

                if (os2.Length >= 90)
                {
                    short sxHeight = ReadShort(os2.Offset + 86);
                    short sCapHeight = ReadShort(os2.Offset + 88);
                    metrics.XHeight = ToPositiveMetric(sxHeight);
                    metrics.CapHeight = ToPositiveMetric(sCapHeight);
                }
            }

            if (_tables.TryGetValue(TagPost, out TableRecord post) && post.Length >= 12)
            {
                metrics.UnderlinePosition = ReadShort(post.Offset + 8);
                metrics.UnderlineThickness = ToPositiveMetric(ReadShort(post.Offset + 10));
            }

            if (metrics.CapHeight == 0)
            {
                metrics.CapHeight = checked((ushort)Math.Max(1, (metrics.DesignUnitsPerEm * 7) / 10));
            }

            if (metrics.XHeight == 0)
            {
                metrics.XHeight = checked((ushort)Math.Max(1, metrics.DesignUnitsPerEm / 2));
            }

            if (metrics.UnderlineThickness == 0)
            {
                metrics.UnderlineThickness = checked((ushort)Math.Max(1, metrics.DesignUnitsPerEm / 20));
            }

            double version = ReadFixed(head.Offset + 4);

            return (metrics, version);
        }

        private (FontWeight weight, FontStretch stretch, FontStyle style) ParseOs2AndStyle()
        {
            FontWeight weight = FontWeight.Normal;
            FontStretch stretch = FontStretch.Normal;
            FontStyle style = FontStyle.Normal;

            if (_tables.TryGetValue(TagOs2, out TableRecord os2))
            {
                if (os2.Length >= 10)
                {
                    ushort weightClass = ReadUShort(os2.Offset + 4);
                    weight = (FontWeight)Math.Clamp((int)weightClass, 1, 1000);
                    ushort widthClass = ReadUShort(os2.Offset + 6);
                    if (widthClass >= 1 && widthClass <= 9)
                    {
                        stretch = (FontStretch)widthClass;
                    }
                }

                if (os2.Length >= 64)
                {
                    ushort fsSelection = ReadUShort(os2.Offset + 62);
                    if ((fsSelection & 0x0001) != 0)
                    {
                        style = FontStyle.Italic;
                    }
                }
            }

            TableRecord head = RequireTable(TagHead);
            ushort macStyle = ReadUShort(head.Offset + 44);
            if ((macStyle & 0x0002) != 0)
            {
                style = FontStyle.Italic;
            }

            if ((macStyle & 0x0001) != 0 && (int)weight < (int)FontWeight.Bold)
            {
                weight = FontWeight.Bold;
            }

            return (weight, stretch, style);
        }

        private ushort ParseGlyphCount()
        {
            return _sfntFace.TryGetGlyphCount(out ushort glyphCount) ? glyphCount : (ushort)0;
        }

        private void ParseNames()
        {
            if (!_tables.TryGetValue(TagName, out TableRecord name) || name.Length < 6)
            {
                return;
            }

            ushort count = ReadUShort(name.Offset + 2);
            ushort stringOffset = ReadUShort(name.Offset + 4);
            uint recordOffset = name.Offset + 6;

            for (int i = 0; i < count; i++)
            {
                uint offset = recordOffset + checked((uint)(i * 12));
                if (!CanRead(offset, 12))
                {
                    break;
                }

                ushort platformId = ReadUShort(offset);
                ushort languageId = ReadUShort(offset + 4);
                ushort nameId = ReadUShort(offset + 6);
                ushort length = ReadUShort(offset + 8);
                ushort nameOffset = ReadUShort(offset + 10);
                uint valueOffset = name.Offset + stringOffset + nameOffset;
                if (!CanRead(valueOffset, length))
                {
                    continue;
                }

                string value = DecodeName(platformId, valueOffset, length);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (!_nameStrings.TryGetValue(nameId, out LocalizedStrings strings))
                {
                    strings = new LocalizedStrings();
                    _nameStrings[nameId] = strings;
                }

                CultureInfo culture = GetCulture(platformId, languageId);
                if (!strings.ContainsKey(culture))
                {
                    strings[culture] = value.Trim();
                }
            }
        }

        private string GetFirstName(params ushort[] nameIds)
        {
            foreach (ushort nameId in nameIds)
            {
                if (_nameStrings.TryGetValue(nameId, out LocalizedStrings strings))
                {
                    if (strings.TryGetValue(CultureInfo.GetCultureInfo("en-US"), out string english) && !string.IsNullOrEmpty(english))
                    {
                        return english;
                    }

                    foreach (string value in strings.Values)
                    {
                        if (!string.IsNullOrEmpty(value))
                        {
                            return value;
                        }
                    }
                }
            }

            return null;
        }

        private TableRecord RequireTable(uint tag)
        {
            if (_tables.TryGetValue(tag, out TableRecord table))
            {
                return table;
            }

            throw new FileFormatException(SourceUri.AbsoluteUri);
        }

        private ushort ReadUShort(uint offset)
        {
            return ReadUShort(_data, offset);
        }

        private short ReadShort(uint offset)
        {
            return unchecked((short)ReadUShort(offset));
        }

        private uint ReadUInt(uint offset)
        {
            return ReadUInt(_data, offset);
        }

        private double ReadFixed(uint offset)
        {
            short major = ReadShort(offset);
            ushort minor = ReadUShort(offset + 2);
            return major + (minor / 65536.0);
        }

        private bool CanRead(uint offset, int length)
        {
            return offset <= _data.Length && length >= 0 && offset + (uint)length <= _data.Length;
        }

        private void EnsureRange(uint offset, uint length)
        {
            if (offset > _data.Length || length > _data.Length || offset + length > _data.Length)
            {
                throw new FileFormatException(SourceUri.AbsoluteUri);
            }
        }

        private string DecodeName(ushort platformId, uint offset, ushort length)
        {
            byte[] bytes = new byte[length];
            Array.Copy(_data, checked((int)offset), bytes, 0, length);

            string value = platformId == 0 || platformId == 3
                ? Encoding.BigEndianUnicode.GetString(bytes)
                : Encoding.Latin1.GetString(bytes);

            return value.Replace("\0", string.Empty);
        }

        private static CultureInfo GetCulture(ushort platformId, ushort languageId)
        {
            if (platformId == 3)
            {
                try
                {
                    return CultureInfo.GetCultureInfo(languageId);
                }
                catch (CultureNotFoundException)
                {
                }
            }

            return CultureInfo.InvariantCulture;
        }

        private static ushort ToPositiveMetric(short value)
        {
            int positive = value < 0 ? -value : value;
            return checked((ushort)Math.Clamp(positive, 0, ushort.MaxValue));
        }

        private static ushort ReadUShort(byte[] data, uint offset)
        {
            int index = checked((int)offset);
            return (ushort)((data[index] << 8) | data[index + 1]);
        }

        private static uint ReadUInt(byte[] data, uint offset)
        {
            int index = checked((int)offset);
            return ((uint)data[index] << 24)
                | ((uint)data[index + 1] << 16)
                | ((uint)data[index + 2] << 8)
                | data[index + 3];
        }

        private static string TagToString(uint tag)
        {
            return new string(new[]
            {
                (char)((tag >> 24) & 0xFF),
                (char)((tag >> 16) & 0xFF),
                (char)((tag >> 8) & 0xFF),
                (char)(tag & 0xFF)
            });
        }

        private sealed class CatalogFaceMetadata
        {
            internal CatalogFaceMetadata(
                Dictionary<uint, TableRecord> tables,
                Dictionary<ushort, LocalizedStrings> nameStrings,
                string familyName,
                string faceName,
                string fullName,
                FontMetrics metrics,
                FontWeight weight,
                FontStretch stretch,
                FontStyle style,
                FontFaceType faceType,
                ushort glyphCount,
                bool isSymbolFont,
                double version)
            {
                Tables = tables;
                NameStrings = nameStrings;
                FamilyName = familyName;
                FaceName = faceName;
                FullName = fullName;
                Metrics = metrics;
                Weight = weight;
                Stretch = stretch;
                Style = style;
                FaceType = faceType;
                GlyphCount = glyphCount;
                IsSymbolFont = isSymbolFont;
                Version = version;
            }

            internal Dictionary<uint, TableRecord> Tables { get; }
            internal Dictionary<ushort, LocalizedStrings> NameStrings { get; }
            internal string FamilyName { get; }
            internal string FaceName { get; }
            internal string FullName { get; }
            internal FontMetrics Metrics { get; }
            internal FontWeight Weight { get; }
            internal FontStretch Stretch { get; }
            internal FontStyle Style { get; }
            internal FontFaceType FaceType { get; }
            internal ushort GlyphCount { get; }
            internal bool IsSymbolFont { get; }
            internal double Version { get; }
        }

        private readonly struct TableRecord
        {
            internal TableRecord(uint offset, uint length)
            {
                Offset = offset;
                Length = length;
            }

            internal uint Offset { get; }

            internal uint Length { get; }
        }

    }

    internal sealed class FontFile : IDisposable
    {
        private readonly Uri _uri;

        internal FontFile(Uri uri)
        {
            _uri = uri;
        }

        internal string GetUriPath()
        {
            return _uri.IsFile ? _uri.LocalPath : _uri.AbsoluteUri;
        }

        public void Dispose()
        {
        }
    }

    internal sealed unsafe class TextAnalyzer
    {
        internal const char CharHyphen = '\x002d';

        internal delegate int CreateTextAnalysisSource(
            char* text,
            uint length,
            char* culture,
            void* factory,
            bool isRightToLeft,
            char* numberCulture,
            bool ignoreUserOverride,
            uint numberSubstitutionMethod,
            void** ppTextAnalysisSource);

        internal delegate void* CreateTextAnalysisSink();

        internal delegate void* GetScriptAnalysisList(void* textAnalysisSink);

        internal delegate void* GetNumberSubstitutionList(void* textAnalysisSink);

        internal static IList<MS.Internal.Span> Itemize(
            char* text,
            uint length,
            CultureInfo culture,
            Native.IDWriteFactory* pDWriteFactory,
            bool isRightToLeftParagraph,
            CultureInfo numberCulture,
            bool ignoreUserOverride,
            uint numberSubstitutionMethod,
            IClassification classificationUtility,
            CreateTextAnalysisSink createTextAnalysisSink,
            GetScriptAnalysisList getScriptAnalysisList,
            GetNumberSubstitutionList getNumberSubstitutionList,
            CreateTextAnalysisSource createTextAnalysisSource)
        {
            return new List<MS.Internal.Span>
            {
                new MS.Internal.Span(new ItemProps(numberCulture), checked((int)length))
            };
        }

        internal void GetGlyphsAndTheirPlacements(
            char* textString,
            uint textLength,
            Font font,
            ushort blankGlyphIndex,
            bool isSideways,
            bool isRightToLeft,
            CultureInfo cultureInfo,
            DWriteFontFeature[][] features,
            uint[] featureRangeLengths,
            double fontEmSize,
            double scalingFactor,
            float pixelsPerDip,
            TextFormattingMode textFormattingMode,
            ItemProps itemProps,
            out ushort[] clusterMap,
            out ushort[] glyphIndices,
            out int[] glyphAdvances,
            out GlyphOffset[] glyphOffsets)
        {
            ArgumentNullException.ThrowIfNull(font);

            ProGpuSfntSimpleGlyphRun glyphRun = ProGpuSfntSimpleGlyphShaper.CreateGlyphRun(
                new ReadOnlySpan<char>(textString, checked((int)textLength)),
                font.FontData.GetGlyphIndex,
                blankGlyphIndex,
                font.FontData.GetGlyphIndex(CharHyphen));
            clusterMap = glyphRun.ClusterMap;
            glyphIndices = glyphRun.GlyphIndices;
            glyphAdvances = new int[glyphIndices.Length];
            glyphOffsets = new GlyphOffset[glyphIndices.Length];

            FillGlyphPlacements(
                textString,
                clusterMap,
                textLength,
                glyphIndices,
                checked((uint)glyphIndices.Length),
                font,
                fontEmSize,
                scalingFactor,
                isSideways,
                glyphAdvances,
                glyphOffsets);
        }

        internal void GetGlyphs(
            char* textString,
            uint textLength,
            Font font,
            ushort blankGlyphIndex,
            bool isSideways,
            bool isRightToLeft,
            CultureInfo cultureInfo,
            DWriteFontFeature[][] features,
            uint[] featureRangeLengths,
            uint maxGlyphCount,
            TextFormattingMode textFormattingMode,
            ItemProps itemProps,
            ushort* clusterMap,
            ushort* textProps,
            ushort* glyphIndices,
            uint* glyphProps,
            int* pfCanGlyphAlone,
            out uint actualGlyphCount)
        {
            ArgumentNullException.ThrowIfNull(font);

            ProGpuSfntSimpleGlyphRun glyphRun = ProGpuSfntSimpleGlyphShaper.CreateGlyphRun(
                new ReadOnlySpan<char>(textString, checked((int)textLength)),
                font.FontData.GetGlyphIndex,
                blankGlyphIndex,
                font.FontData.GetGlyphIndex(CharHyphen));
            actualGlyphCount = checked((uint)glyphRun.GlyphIndices.Length);

            for (uint i = 0; i < textLength; i++)
            {
                if (clusterMap != null)
                {
                    clusterMap[i] = glyphRun.ClusterMap[i];
                }

                if (textProps != null)
                {
                    textProps[i] = 0;
                }

                if (pfCanGlyphAlone != null)
                {
                    pfCanGlyphAlone[i] = 1;
                }
            }

            if (actualGlyphCount > maxGlyphCount)
            {
                return;
            }

            for (uint i = 0; i < actualGlyphCount; i++)
            {
                glyphIndices[i] = glyphRun.GlyphIndices[i];
                if (glyphProps != null)
                {
                    glyphProps[i] = 0;
                }
            }
        }

        internal void GetGlyphPlacements(
            char* textString,
            ushort* clusterMap,
            ushort* textProps,
            uint textLength,
            ushort* glyphIndices,
            uint* glyphProps,
            uint glyphCount,
            Font font,
            double fontEmSize,
            double scalingFactor,
            bool isSideways,
            bool isRightToLeft,
            CultureInfo cultureInfo,
            DWriteFontFeature[][] features,
            uint[] featureRangeLengths,
            TextFormattingMode textFormattingMode,
            ItemProps itemProps,
            float pixelsPerDip,
            int* glyphAdvances,
            out GlyphOffset[] glyphOffsets)
        {
            ArgumentNullException.ThrowIfNull(font);

            glyphOffsets = new GlyphOffset[glyphCount];
            FillGlyphPlacements(
                textString,
                clusterMap,
                textLength,
                glyphIndices,
                glyphCount,
                font,
                fontEmSize,
                scalingFactor,
                isSideways,
                glyphAdvances,
                glyphOffsets);
        }

        private static void FillGlyphPlacements(
            char* textString,
            ushort* clusterMap,
            uint textLength,
            ushort* glyphIndices,
            uint glyphCount,
            Font font,
            double fontEmSize,
            double scalingFactor,
            bool isSideways,
            int* glyphAdvances,
            GlyphOffset[] glyphOffsets)
        {
            ProGpuSfntSimpleGlyphShaper.FillGlyphAdvances(
                new ReadOnlySpan<char>(textString, checked((int)textLength)),
                new ReadOnlySpan<ushort>(clusterMap, checked((int)textLength)),
                new ReadOnlySpan<ushort>(glyphIndices, checked((int)glyphCount)),
                glyphIndex =>
                {
                    GlyphMetrics metrics = font.FontData.GetGlyphMetrics(glyphIndex);
                    return new ProGpuSfntSimpleGlyphMetrics(metrics.AdvanceWidth, metrics.AdvanceHeight);
                },
                font.Metrics.DesignUnitsPerEm,
                fontEmSize,
                scalingFactor,
                isSideways,
                new Span<int>(glyphAdvances, checked((int)glyphCount)));

            for (int i = 0; i < glyphOffsets.Length; i++)
            {
                glyphOffsets[i] = default;
            }
        }

        private static void FillGlyphPlacements(
            char* textString,
            ushort[] clusterMap,
            uint textLength,
            ushort[] glyphIndices,
            uint glyphCount,
            Font font,
            double fontEmSize,
            double scalingFactor,
            bool isSideways,
            int[] glyphAdvances,
            GlyphOffset[] glyphOffsets)
        {
            fixed (ushort* pClusterMap = clusterMap)
            fixed (ushort* pGlyphIndices = glyphIndices)
            fixed (int* pGlyphAdvances = glyphAdvances)
            {
                FillGlyphPlacements(
                    textString,
                    pClusterMap,
                    textLength,
                    pGlyphIndices,
                    glyphCount,
                    font,
                    fontEmSize,
                    scalingFactor,
                    isSideways,
                    pGlyphAdvances,
                    glyphOffsets);
            }
        }

    }

    internal static class DWriteTypeConverter
    {
        internal static ushort Convert(TextFormattingMode textFormattingMode)
        {
            return textFormattingMode == TextFormattingMode.Display ? (ushort)1 : (ushort)0;
        }
    }

    internal sealed unsafe class ItemProps
    {
        internal ItemProps()
            : this(CultureInfo.InvariantCulture)
        {
        }

        internal ItemProps(CultureInfo digitCulture)
        {
            DigitCulture = digitCulture ?? CultureInfo.InvariantCulture;
        }

        internal void* NumberSubstitutionNoAddRef => null;

        internal void* ScriptAnalysis => null;

        internal CultureInfo DigitCulture { get; }

        internal bool HasExtendedCharacter => false;

        internal bool NeedsCaretInfo => false;

        internal bool IsIndic => false;

        internal bool IsLatin => true;

        internal bool HasCombiningMark => false;

        internal bool CanShapeTogether(ItemProps other)
        {
            return other != null && Equals(DigitCulture, other.DigitCulture);
        }
    }
}

namespace MS.Internal.Text.TextInterface.Native
{
    internal struct IDWriteFactory
    {
    }
}

namespace MS.Internal
{
    internal static unsafe class TrueTypeSubsetter
    {
        internal static byte[] ComputeSubset(void* fontData, int fileSize, Uri sourceUri, int directoryOffset, ushort[] glyphArray)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(fileSize);
            if (fontData == null)
            {
                throw new ArgumentNullException(nameof(fontData));
            }

            byte[] fontCopy = new byte[fileSize];
            Marshal.Copy((IntPtr)fontData, fontCopy, 0, fileSize);
            if (ProGpuSfntFontSubsetter.TryCreateGlyphIdPreservingSubset(
                fontCopy,
                directoryOffset,
                glyphArray,
                out byte[] subset))
            {
                return subset;
            }

            return fontCopy;
        }
    }

    internal static class NativeWPFDLLLoader
    {
        internal static void LoadDwrite()
        {
        }
    }
}
