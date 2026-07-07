// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//
//
//
//  Contents:  Unicode classification entry point
//
//

using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Media.TextFormatting;

namespace MS.Internal
{
    /// <summary>
    /// This class is used as a level on indirection for classes in managed c++ to be able to utilize methods
    /// from the static class Classification.
    /// We cannot make MC++ reference PresentationCore.dll since this will result in cirular reference.
    /// </summary>
    internal class ClassificationUtility : MS.Internal.Text.TextInterface.IClassification
    {
        // We have restored this list from WPF 3.x.
        // The original list can be found under
        // $/Dev10/pu/WPF/wpf/src/Core/CSharp/MS/Internal/Shaping/Script.cs
        internal static readonly bool[] ScriptCaretInfo = new bool[]
        {
            /* Default              */    false,
            /* Arabic               */    false,
            /* Armenian             */    false,
            /* Bengali              */    true,
            /* Bopomofo             */    false,
            /* Braille              */    false,
            /* Buginese             */    true,
            /* Buhid                */    false,
            /* CanadianSyllabics    */    false,
            /* Cherokee             */    false,
            /* CJKIdeographic       */    false,
            /* Coptic               */    false,
            /* CypriotSyllabary     */    false,
            /* Cyrillic             */    false,
            /* Deseret              */    false,
            /* Devanagari           */    true,
            /* Ethiopic             */    false,
            /* Georgian             */    false,
            /* Glagolitic           */    false,
            /* Gothic               */    false,
            /* Greek                */    false,
            /* Gujarati             */    true,
            /* Gurmukhi             */    true,
            /* Hangul               */    true,
            /* Hanunoo              */    false,
            /* Hebrew               */    true,
            /* Kannada              */    true,
            /* Kana                 */    false,
            /* Kharoshthi           */    true,
            /* Khmer                */    true,
            /* Lao                  */    true,
            /* Latin                */    false,
            /* Limbu                */    true,
            /* LinearB              */    false,
            /* Malayalam            */    true,
            /* MathematicalAlphanumericSymbols */ false,
            /* Mongolian            */    true,
            /* MusicalSymbols       */    false,
            /* Myanmar              */    true,
            /* NewTaiLue            */    true,
            /* Ogham                */    false,
            /* OldItalic            */    false,
            /* OldPersianCuneiform  */    false,
            /* Oriya                */    true,
            /* Osmanya              */    false,
            /* Runic                */    false,
            /* Shavian              */    false,
            /* Sinhala              */    true,
            /* SylotiNagri          */    true,
            /* Syriac               */    false,
            /* Tagalog              */    false,
            /* Tagbanwa             */    false,
            /* TaiLe                */    false,
            /* Tamil                */    true,
            /* Telugu               */    true,
            /* Thaana               */    true,
            /* Thai                 */    true,
            /* Tibetan              */    true,
            /* Tifinagh             */    false,
            /* UgariticCuneiform    */    false,
            /* Yi                   */    false,
            /* Digit                */    false,
            /* Control              */    false,
            /* Mirror               */    false,
        };

        private static ClassificationUtility _classificationUtilityInstance = new ClassificationUtility();

        internal static ClassificationUtility Instance
        {
            get
            {
                return _classificationUtilityInstance;
            }
        }

        public void GetCharAttribute(
                                    int unicodeScalar,
                                    out bool isCombining,
                                    out bool needsCaretInfo,
                                    out bool isIndic,
                                    out bool isDigit,
                                    out bool isLatin,
                                    out bool isStrong
                                    )
        {
            CharacterAttribute charAttribute = Classification.CharAttributeOf((int)Classification.GetUnicodeClass(unicodeScalar));

            byte itemClass = charAttribute.ItemClass;
            isCombining = (itemClass == (byte)ItemClass.SimpleMarkClass 
                        || itemClass == (byte)ItemClass.ComplexMarkClass
                        || Classification.IsIVS(unicodeScalar));

            isStrong = (itemClass == (byte)ItemClass.StrongClass);
            
            int script = charAttribute.Script;
            needsCaretInfo = ScriptCaretInfo[script];

            ScriptID scriptId = (ScriptID)script;
            isDigit = scriptId == ScriptID.Digit;
            isLatin = scriptId == ScriptID.Latin;
            if (isLatin)
            {
                isIndic = false;
            }
            else
            {
                isIndic = IsScriptIndic(scriptId);
            }
        }

        /// <summary>
        /// Returns true if specified script is Indic.
        /// </summary>
        private static bool IsScriptIndic(ScriptID scriptId)
        {
            if (scriptId == ScriptID.Bengali
                 || scriptId == ScriptID.Devanagari
                 || scriptId == ScriptID.Gurmukhi
                 || scriptId == ScriptID.Gujarati
                 || scriptId == ScriptID.Kannada
                 || scriptId == ScriptID.Malayalam
                 || scriptId == ScriptID.Oriya
                 || scriptId == ScriptID.Tamil
                 || scriptId == ScriptID.Telugu)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
    /// <summary>
    /// Hold the classification table pointers. 
    /// </summary>    
    internal static class Classification
    {
        /// <summary>
        /// This structure has a cloned one in the unmanaged side. Doing any change in this
        /// structure should have the same change on unmanaged side too.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct CombiningMarksClassificationData
        {
            internal IntPtr CombiningCharsIndexes; // Two dimentional array of base char classes,
            internal int    CombiningCharsIndexesTableLength;
            internal int    CombiningCharsIndexesTableSegmentLength;
            
            internal IntPtr CombiningMarkIndexes; // Combining mark classes array, with length = length
            internal int    CombiningMarkIndexesTableLength;
            
            internal IntPtr CombinationChars; // Two dimentional array of combined characters
            internal int    CombinationCharsBaseCount;
            internal int    CombinationCharsMarkCount;
        }
        
        /// <summary>
        /// This structure has a cloned one in the unmanaged side. doing any change in  that
        /// structure should have same change in the unmanaged side too.
        /// </summary>    
        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct RawClassificationTables
        {
            internal IntPtr UnicodeClasses;
            internal IntPtr CharacterAttributes;
            internal IntPtr Mirroring;
            internal CombiningMarksClassificationData CombiningMarksClassification;
        };

        [DllImport(DllImport.PresentationNative, EntryPoint="MILGetClassificationTables")]
        internal static extern void MILGetClassificationTables(out RawClassificationTables ct);
        static Classification()
        {
            if (OperatingSystem.IsWindows())
            {
                unsafe
                {
                    RawClassificationTables ct = new RawClassificationTables();
                    MILGetClassificationTables(out ct);

                    _unicodeClassTable = ct.UnicodeClasses;
                    _charAttributeTable = ct.CharacterAttributes;
                    _mirroredCharTable = ct.Mirroring;
                    _combiningMarksClassification = ct.CombiningMarksClassification;
                }
            }
            else
            {
                _unicodeClassTable = IntPtr.Zero;
                _charAttributeTable = IntPtr.Zero;
                _mirroredCharTable = IntPtr.Zero;
                _combiningMarksClassification = default;
            }
        }

        /// <summary>
        /// Lookup Unicode character class for a Unicode UTF16 value
        /// </summary>
        public static short GetUnicodeClassUTF16(char codepoint)
        {
            if (_unicodeClassTable == IntPtr.Zero)
            {
                return GetManagedUnicodeClass(codepoint);
            }

            unsafe 
            {
                short **plane0 = UnicodeClassTable[0];
                Invariant.Assert((long)plane0 >= (long)UnicodeClass.Max);

                short* pcc = plane0[codepoint >> 8];
                return ((long) pcc < (long) UnicodeClass.Max ?
                    (short)pcc : pcc[codepoint & 0xFF]);
            }
        }


        /// <summary>
        /// Lookup Unicode character class for a Unicode scalar value
        /// </summary>
        public static short GetUnicodeClass(int unicodeScalar)
        {
            if (_unicodeClassTable == IntPtr.Zero)
            {
                return GetManagedUnicodeClass(unicodeScalar);
            }

            unsafe
            {
                Invariant.Assert(unicodeScalar >= 0 && unicodeScalar <= 0x10FFFF);
                short **ppcc = UnicodeClassTable[((unicodeScalar >> 16) & 0xFF) % 17];

                if ((long)ppcc < (long)UnicodeClass.Max)
                    return (short)ppcc;

                short *pcc = ppcc[(unicodeScalar & 0xFFFF) >> 8];

                if ((long)pcc < (long)UnicodeClass.Max)
                    return (short)pcc;

                return pcc[unicodeScalar & 0xFF];
            }
        }


        /// <summary>
        /// Lookup script ID for a Unicode scalar value
        /// </summary>
        public static ScriptID GetScript(int unicodeScalar)
        {
            return (ScriptID)CharAttributeOf(GetUnicodeClass(unicodeScalar)).Script;
        }


        /// <summary>
        /// Compute Unicode scalar value from unicode codepoint stream
        /// </summary>
        internal static int UnicodeScalar(
            CharacterBufferRange unicodeString,
            out int              sizeofChar
            )
        {
            Invariant.Assert(unicodeString.CharacterBuffer != null && unicodeString.Length > 0);

            int ch = unicodeString[0];
            sizeofChar = 1;

            if (    unicodeString.Length >= 2
                &&  (ch & 0xFC00) == 0xD800
                &&  (unicodeString[1] & 0xFC00) == 0xDC00
                )
            {
                ch = (((ch & 0x03FF) << 10) | (unicodeString[1] & 0x3FF)) + 0x10000;
                sizeofChar++;
            }

            return ch;
        }


        /// <summary>
        /// Check whether the character is combining mark
        /// </summary>
        public static bool IsCombining(int unicodeScalar)
        {
            byte itemClass = CharAttributeOf(GetUnicodeClass(unicodeScalar)).ItemClass;

            return itemClass == (byte)ItemClass.SimpleMarkClass
                || itemClass == (byte)ItemClass.ComplexMarkClass
                || IsIVS(unicodeScalar);
        }

        /// <summary>
        /// Check whether the character is a joiner character
        /// </summary>
        public static bool IsJoiner(int unicodeScalar)
        {
            byte itemClass = CharAttributeOf(GetUnicodeClass(unicodeScalar)).ItemClass;

            return itemClass == (byte)ItemClass.JoinerClass;
        }

        /// <summary>
        /// Check whether the character is an IVS selector character
        /// </summary>
        public static bool IsIVS(int unicodeScalar)
        {
            // An Ideographic Variation Sequence (IVS) is a sequence of two
            // coded characters, the first being a character with the
            // Unified_Ideograph property, the second being a variation
            // selector character in the range U+E0100 to U+E01EF.
            return unicodeScalar >= 0xE0100 && unicodeScalar <= 0xE01EF;
        }

        /// <summary>
        /// Scan UTF16 character string until a character with specified attributes is found
        /// </summary>
        /// <returns>character index of first character matching the attribute.</returns>
        public static int AdvanceUntilUTF16(
            CharacterBuffer     charBuffer,
            int                 offsetToFirstChar,
            int                 stringLength,
            ushort              mask,
            out ushort          charFlags
            )
        {
            int i = offsetToFirstChar;
            int limit = offsetToFirstChar + stringLength;
            charFlags = 0;

            while (i < limit)
            {
                ushort flags = CharAttributeOf(GetUnicodeClassUTF16(charBuffer[i])).Flags;

                if((flags & mask) != 0)
                    break;

                charFlags |= flags;
                i++;
            }
            return i - offsetToFirstChar;
        }

        /// <summary>
        /// Scan character string until a character that is not the specified ItemClass is found
        /// </summary>
        /// <returns>character index of first character that is not the specified ItemClass</returns>
        public static int AdvanceWhile(
            CharacterBufferRange unicodeString, 
            ItemClass            itemClass 
            )
        {            
            int i     = 0;
            int limit = unicodeString.Length;
            int sizeofChar = 0; 
            
            while (i < limit)
            {
                int ch = Classification.UnicodeScalar(
                    new CharacterBufferRange(unicodeString, i, limit - i), 
                    out sizeofChar
                    ); 
            
                byte currentClass = CharAttributeOf(GetUnicodeClass(ch)).ItemClass;
                if (currentClass != (byte) itemClass)
                    break;
                
                i += sizeofChar;
            }
            
            return i;
        }

        private static unsafe short*** UnicodeClassTable => (short***)_unicodeClassTable;

        private static unsafe CharacterAttribute* CharAttributeTable => (CharacterAttribute*)_charAttributeTable;

        internal static CharacterAttribute CharAttributeOf(int charClass)
        {
            if (_charAttributeTable == IntPtr.Zero)
            {
                return ManagedCharAttributeOf(charClass);
            }

            unsafe
            {
                Invariant.Assert(charClass >= 0 && charClass < (int) UnicodeClass.Max);
                return CharAttributeTable[charClass]; 
            }
        }

        private static short GetManagedUnicodeClass(int unicodeScalar)
        {
            Invariant.Assert(unicodeScalar >= 0 && unicodeScalar <= 0x10FFFF);

            if (unicodeScalar == '\r' || unicodeScalar == '\n' || unicodeScalar == 0x0085 || unicodeScalar == 0x2028 || unicodeScalar == 0x2029)
            {
                return ManagedLineBreakClass;
            }

            if (unicodeScalar == '\t')
            {
                return ManagedTabClass;
            }

            if (unicodeScalar == 0x200C || unicodeScalar == 0x200D)
            {
                return ManagedJoinerClass;
            }

            UnicodeCategory category = GetUnicodeCategory(unicodeScalar);
            switch (category)
            {
                case UnicodeCategory.DecimalDigitNumber:
                    return ManagedDigitClass;

                case UnicodeCategory.SpaceSeparator:
                    return ManagedSpaceClass;

                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                    return ManagedLineBreakClass;

                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.SpacingCombiningMark:
                case UnicodeCategory.EnclosingMark:
                    return ManagedCombiningClass;

                case UnicodeCategory.Control:
                case UnicodeCategory.Format:
                case UnicodeCategory.Surrogate:
                case UnicodeCategory.OtherNotAssigned:
                    return ManagedControlClass;

                case UnicodeCategory.PrivateUse:
                    return ManagedPrivateUseClass;

                case UnicodeCategory.UppercaseLetter:
                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.TitlecaseLetter:
                case UnicodeCategory.ModifierLetter:
                case UnicodeCategory.OtherLetter:
                    return GetManagedLetterClass(unicodeScalar);

                default:
                    return ManagedPunctuationClass;
            }
        }

        private static UnicodeCategory GetUnicodeCategory(int unicodeScalar)
        {
            return unicodeScalar <= char.MaxValue
                ? CharUnicodeInfo.GetUnicodeCategory((char)unicodeScalar)
                : CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(unicodeScalar), 0);
        }

        private static short GetManagedLetterClass(int unicodeScalar)
        {
            if (unicodeScalar >= 0x0590 && unicodeScalar <= 0x05FF)
            {
                return ManagedHebrewClass;
            }

            if ((unicodeScalar >= 0x0600 && unicodeScalar <= 0x06FF) ||
                (unicodeScalar >= 0x0750 && unicodeScalar <= 0x077F) ||
                (unicodeScalar >= 0x08A0 && unicodeScalar <= 0x08FF) ||
                (unicodeScalar >= 0xFB50 && unicodeScalar <= 0xFDFF) ||
                (unicodeScalar >= 0xFE70 && unicodeScalar <= 0xFEFF))
            {
                return ManagedArabicClass;
            }

            if ((unicodeScalar >= 0x3040 && unicodeScalar <= 0x30FF) ||
                (unicodeScalar >= 0x3400 && unicodeScalar <= 0x4DBF) ||
                (unicodeScalar >= 0x4E00 && unicodeScalar <= 0x9FFF) ||
                (unicodeScalar >= 0xAC00 && unicodeScalar <= 0xD7AF) ||
                (unicodeScalar >= 0xF900 && unicodeScalar <= 0xFAFF))
            {
                return ManagedCjkClass;
            }

            return ManagedLatinClass;
        }

        private static CharacterAttribute ManagedCharAttributeOf(int charClass)
        {
            return charClass switch
            {
                ManagedLatinClass => CreateManagedAttribute(
                    ScriptID.Latin,
                    ItemClass.StrongClass,
                    CharacterAttributeFlags.CharacterLetter | CharacterAttributeFlags.CharacterFastText,
                    DirectionClass.Left),
                ManagedDigitClass => CreateManagedAttribute(
                    ScriptID.Digit,
                    ItemClass.DigitClass,
                    CharacterAttributeFlags.CharacterDigit | CharacterAttributeFlags.CharacterFastText,
                    DirectionClass.EuropeanNumber),
                ManagedSpaceClass => CreateManagedAttribute(
                    ScriptID.Default,
                    ItemClass.WeakClass,
                    CharacterAttributeFlags.CharacterSpace | CharacterAttributeFlags.CharacterFastText,
                    DirectionClass.WhiteSpace),
                ManagedLineBreakClass => CreateManagedAttribute(
                    ScriptID.Control,
                    ItemClass.ControlClass,
                    CharacterAttributeFlags.CharacterLineBreak | CharacterAttributeFlags.CharacterParaBreak | CharacterAttributeFlags.CharacterCRLF,
                    DirectionClass.ParagraphSeparator),
                ManagedControlClass => CreateManagedAttribute(
                    ScriptID.Control,
                    ItemClass.ControlClass,
                    CharacterAttributeFlags.CharacterFormatAnchor,
                    DirectionClass.BoundaryNeutral),
                ManagedCombiningClass => CreateManagedAttribute(
                    ScriptID.Default,
                    ItemClass.SimpleMarkClass,
                    CharacterAttributeFlags.CharacterComplex,
                    DirectionClass.NonSpacingMark),
                ManagedJoinerClass => CreateManagedAttribute(
                    ScriptID.Control,
                    ItemClass.JoinerClass,
                    CharacterAttributeFlags.CharacterComplex,
                    DirectionClass.BoundaryNeutral),
                ManagedCjkClass => CreateManagedAttribute(
                    ScriptID.CJKIdeographic,
                    ItemClass.StrongClass,
                    CharacterAttributeFlags.CharacterLetter | CharacterAttributeFlags.CharacterIdeo | CharacterAttributeFlags.CharacterComplex,
                    DirectionClass.Left),
                ManagedArabicClass => CreateManagedAttribute(
                    ScriptID.Arabic,
                    ItemClass.StrongClass,
                    CharacterAttributeFlags.CharacterLetter | CharacterAttributeFlags.CharacterRTL | CharacterAttributeFlags.CharacterComplex,
                    DirectionClass.ArabicLetter),
                ManagedHebrewClass => CreateManagedAttribute(
                    ScriptID.Hebrew,
                    ItemClass.StrongClass,
                    CharacterAttributeFlags.CharacterLetter | CharacterAttributeFlags.CharacterRTL | CharacterAttributeFlags.CharacterComplex,
                    DirectionClass.Right),
                ManagedTabClass => CreateManagedAttribute(
                    ScriptID.Control,
                    ItemClass.WeakClass,
                    CharacterAttributeFlags.CharacterSpace | CharacterAttributeFlags.CharacterFastText,
                    DirectionClass.WhiteSpace),
                ManagedPrivateUseClass => CreateManagedAttribute(
                    ScriptID.Default,
                    ItemClass.WeakClass,
                    CharacterAttributeFlags.CharacterFastText,
                    DirectionClass.OtherNeutral),
                _ => CreateManagedAttribute(
                    ScriptID.Default,
                    ItemClass.WeakClass,
                    CharacterAttributeFlags.CharacterFastText,
                    DirectionClass.OtherNeutral)
            };
        }

        private static CharacterAttribute CreateManagedAttribute(
            ScriptID script,
            ItemClass itemClass,
            CharacterAttributeFlags flags,
            DirectionClass bidi)
        {
            return new CharacterAttribute
            {
                Script = (byte)script,
                ItemClass = (byte)itemClass,
                Flags = (ushort)flags,
                BreakType = (byte)CharBreakingType.NoBreak,
                BiDi = bidi,
                LineBreak = 0
            };
        }

        private const short ManagedDefaultClass = 0;
        private const short ManagedLatinClass = 1;
        private const short ManagedDigitClass = 2;
        private const short ManagedSpaceClass = 3;
        private const short ManagedLineBreakClass = 4;
        private const short ManagedControlClass = 5;
        private const short ManagedCombiningClass = 6;
        private const short ManagedJoinerClass = 7;
        private const short ManagedCjkClass = 8;
        private const short ManagedArabicClass = 9;
        private const short ManagedHebrewClass = 10;
        private const short ManagedPunctuationClass = 11;
        private const short ManagedTabClass = 12;
        private const short ManagedPrivateUseClass = 13;

        private static readonly IntPtr _unicodeClassTable;
        private static readonly IntPtr _charAttributeTable;
        private static readonly IntPtr _mirroredCharTable;
        private static readonly CombiningMarksClassificationData _combiningMarksClassification;
    }
}
