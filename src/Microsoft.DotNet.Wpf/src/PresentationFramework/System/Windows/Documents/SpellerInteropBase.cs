// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using System.Globalization;
using System.Windows.Controls;

namespace System.Windows.Documents
{
    internal abstract class SpellerInteropBase: IDisposable
    {
        #region Internal Types

        // Callback delegates for EnumTextSegments method.
        internal delegate bool EnumSentencesCallback(ISpellerSentence sentence, object data);
        internal delegate bool EnumTextSegmentsCallback(ISpellerSegment textSegment, object data);

        /// <summary>
        /// Identifies, by position, a sub-string of a source text string
        /// </summary>
        internal interface ITextRange
        {
            int Start { get; }
            int Length { get; }
        }

        /// <summary>
        /// Represents the spell-checkers notion of a 'word'
        /// </summary>
        internal interface ISpellerSegment
        {
            /// <summary>
            /// Source String for which <see cref="TextRange"/> provides a position
            /// </summary>
            string SourceString { get; }

            /// <summary>
            /// Identifies sub-words, if any. 
            /// </summary>
            IReadOnlyList<ISpellerSegment> SubSegments { get; }

            /// <summary>
            /// Obtains the position of this segment in it's source text string
            /// </summary>
            ITextRange TextRange { get; }

            /// <summary>
            /// Text represented by <see cref="TextRange"/>
            /// </summary>
            string Text { get; }

            /// <summary>
            /// Queries the spell-checker to obtain suggestions for this segment
            /// </summary>
            IReadOnlyList<string> Suggestions { get; }

            /// <summary>
            /// Returns true if the segment has no spelling errors
            /// </summary>
            bool IsClean { get; }

            /// <summary>
            /// Enumerates a segment's subsegments, making a callback on each iteration.
            /// </summary>
            /// <param name="segmentCallback"></param>
            /// <param name="data"></param>
            void EnumSubSegments(EnumTextSegmentsCallback segmentCallback, object data);
        }

        /// <summary>
        /// Represents the spell-checker's notion of a 'sentence', which is in turn made 
        /// up of 'segments' (words) and 'sub-segments'
        /// </summary>
        internal interface ISpellerSentence
        {
            IReadOnlyList<ISpellerSegment> Segments { get; }
            
            /// <summary>
            /// Returns the final symbol offset of a sentence.
            /// </summary>
            int EndOffset { get; }
        }

        [Flags]
        internal enum SpellerMode
        {
            None                          = 0x0000,
            WordBreaking                  = 0x0001, 
            SpellingErrors                = 0x0002, 
            Suggestions                   = 0x0004,
            SpellingErrorsWithSuggestions = SpellingErrors | Suggestions, 
            All                           = WordBreaking | SpellingErrorsWithSuggestions,
        };

        #endregion Internal Types

        #region IDispose

        public abstract void Dispose();
        protected abstract void Dispose(bool disposing);

        #endregion 

        #region Factory

        public static SpellerInteropBase CreateInstance()
        {
            if (!System.OperatingSystem.IsWindows())
            {
                return new NullSpellerInterop();
            }

            SpellerInteropBase spellerInterop = null;

            bool winRTSupport = false;
            
            try
            {
                spellerInterop = new WinRTSpellerInterop();
                winRTSupport = true;
            }
            catch (PlatformNotSupportedException)
            {
                winRTSupport = false;
            }
            catch (NotSupportedException)
            {
                // Any other exception besides PlatformNotSupportedException
                // indicates that WinRT API's are supportable on this OS 
                // platform, but failed to initialize for some reason.
                winRTSupport = true;
            }

            if (!winRTSupport)
            {
                try
                {
                    spellerInterop = new NLGSpellerInterop();
                }
                catch (Exception ex) when 
                    (ex is DllNotFoundException || ex is EntryPointNotFoundException)
                {
                    return null;
                }
            }

            return spellerInterop;
        }

        #endregion Factory

        #region Abstract Methods

        internal abstract void SetLocale(CultureInfo culture);


        // Helper for methods that need to iterate over segments within a text run.
        // Returns the total number of segments encountered.
        internal abstract int EnumTextSegments(char[] text, int count,
            EnumSentencesCallback sentenceCallback, EnumTextSegmentsCallback segmentCallback, object data);

        /// <summary>
        /// Unloads given custom dictionary
        /// </summary>
        /// <param name="lexicon"></param>
        internal abstract void UnloadDictionary(object dictionary); 

         /// <summary>
        /// Loads custom dictionary
        /// </summary>
        /// <param name="lexiconFilePath"></param>
        /// <returns></returns>
        internal abstract object LoadDictionary(string lexiconFilePath);


        /// <summary>
        /// Loads custom dictionary.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="trustedFolder"></param>
        /// <returns></returns>
        /// <remarks>
        /// There are 2 kinds of files we're trying to load here: Files specified by user directly, and files
        /// which we created and filled with data from pack Uri locations specified by user.
        /// These 'trusted' files are placed under <paramref name="trustedFolder"/>.
        ///
        /// Files specified in <paramref name="trustedFolder"/> are wrapped in FileIOPermission.Assert(),
        /// providing read access to trusted files under <paramref name="trustedFolder"/>, i.e. additionally
        /// we're making sure that specified trusted locations are under the trusted Folder.
        ///
        /// This is needed to differentiate a case when user passes in a local path location which just happens to be under
        /// trusted folder. We still want to fail in this case, since we want to trust only files that we've created.
        /// </remarks>
        internal abstract object LoadDictionary(Uri item, string trustedFolder);

        /// <summary>
        /// Releases all currently loaded lexicons.
        /// </summary>
        internal abstract void ReleaseAllLexicons();

        /// <summary>
        /// Sets the speller mode to be wordbreaking only, wordbreaking + spellchecking or 
        /// wordbreaking+spellchecking+suggestion generation
        /// </summary>
        internal abstract SpellerMode Mode
        {
            set;
        }

        /// <summary>
        /// Tells the spellchecker whether to check for multi-word spelling errors
        /// </summary>
        internal abstract bool MultiWordMode
        {
             set;
        }

        /// <summary>
        /// Sets spelling reform options
        /// </summary>
        /// <param name="culture"></param>
        /// <param name="spellingReform"></param>
        internal abstract void SetReformMode(CultureInfo culture, SpellingReform spellingReform);

        /// <summary>
        /// Returns true if we have an engine capable of proofing the specified language.
        /// </summary>
        /// <param name="culture"></param>
        /// <returns></returns>
        internal abstract bool CanSpellCheck(CultureInfo culture);

        #endregion Abstract Methods

        private sealed class NullSpellerInterop : SpellerInteropBase
        {
            private static readonly IReadOnlyList<ISpellerSegment> EmptySegments = Array.Empty<ISpellerSegment>();
            private static readonly IReadOnlyList<string> EmptySuggestions = Array.Empty<string>();

            public override void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            protected override void Dispose(bool disposing)
            {
            }

            internal override void SetLocale(CultureInfo culture)
            {
            }

            internal override int EnumTextSegments(
                char[] text,
                int count,
                EnumSentencesCallback sentenceCallback,
                EnumTextSegmentsCallback segmentCallback,
                object data)
            {
                ArgumentNullException.ThrowIfNull(text);

                if (count <= 0)
                {
                    return 0;
                }

                count = Math.Min(count, text.Length);
                string sourceString = new string(text, 0, count);
                List<ISpellerSegment> segments = BreakIntoSegments(sourceString, count);
                bool continueIteration = true;

                if (segmentCallback != null)
                {
                    for (int i = 0; continueIteration && i < segments.Count; i++)
                    {
                        continueIteration = segmentCallback(segments[i], data);
                    }
                }

                if (continueIteration && sentenceCallback != null)
                {
                    continueIteration = sentenceCallback(new Sentence(segments, count), data);
                }

                return segments.Count;
            }

            private static List<ISpellerSegment> BreakIntoSegments(string sourceString, int count)
            {
                var segments = new List<ISpellerSegment>();

                for (int index = 0; index < count;)
                {
                    while (index < count && IsWordBreakCharacter(sourceString[index]))
                    {
                        index++;
                    }

                    int start = index;
                    while (index < count && !IsWordBreakCharacter(sourceString[index]))
                    {
                        index++;
                    }

                    int length = index - start;
                    if (length > 0)
                    {
                        segments.Add(new Segment(sourceString, start, length));
                    }
                }

                return segments;
            }

            private static bool IsWordBreakCharacter(char value)
            {
                return char.IsWhiteSpace(value) || char.IsPunctuation(value) || char.IsSymbol(value);
            }

            private sealed class Sentence : ISpellerSentence
            {
                public Sentence(IReadOnlyList<ISpellerSegment> segments, int endOffset)
                {
                    Segments = segments;
                    EndOffset = endOffset;
                }

                public IReadOnlyList<ISpellerSegment> Segments { get; }

                public int EndOffset { get; }
            }

            internal override void UnloadDictionary(object dictionary)
            {
            }

            internal override object LoadDictionary(string lexiconFilePath)
            {
                return null;
            }

            internal override object LoadDictionary(Uri item, string trustedFolder)
            {
                return null;
            }

            internal override void ReleaseAllLexicons()
            {
            }

            internal override SpellerMode Mode
            {
                set
                {
                }
            }

            internal override bool MultiWordMode
            {
                set
                {
                }
            }

            internal override void SetReformMode(CultureInfo culture, SpellingReform spellingReform)
            {
            }

            internal override bool CanSpellCheck(CultureInfo culture)
            {
                return false;
            }

            private sealed class Segment : ISpellerSegment, ITextRange
            {
                public Segment(string sourceString, int start, int length)
                {
                    SourceString = sourceString;
                    Start = start;
                    Length = length;
                }

                public string SourceString { get; }

                public IReadOnlyList<ISpellerSegment> SubSegments => EmptySegments;

                public ITextRange TextRange => this;

                public string Text => SourceString.Substring(Start, Length);

                public IReadOnlyList<string> Suggestions => EmptySuggestions;

                public bool IsClean => true;

                public int Start { get; }

                public int Length { get; }

                public void EnumSubSegments(EnumTextSegmentsCallback segmentCallback, object data)
                {
                }
            }
        }
    }
}
