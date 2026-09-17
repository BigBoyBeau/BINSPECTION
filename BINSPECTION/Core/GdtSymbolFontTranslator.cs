using System;
using System.Collections.Generic;
using System.Linq;

namespace BINSPECTION.Core
{
    // Translates the plain-English GD&T characteristic-type words that show
    // up in imported/typed characteristic text (e.g. "Profile of a Surface
    // 0.2 X Y Z", the style GtolTextFormatter.GetFrameText also produces)
    // into the matching single-character symbol from the "SolidWorks GDT"
    // TrueType font - the same font SolidWorks itself ships for typing GD&T
    // symbols into notes. Everything else in the text (the tolerance value,
    // datum letters, "/" composite separator) is left as plain text; only
    // the recognized characteristic-type word is swapped for its symbol
    // character.
    //
    // The character codes below were recovered by rendering every
    // private-use-area code point in "solidworks gdt.ttf" to an image and
    // reading off which GD&T symbol each one draws (the font's own glyph
    // names are auto-generated "uniXXXX" placeholders, not descriptive, so
    // there was no metadata to read this from directly). The font defines
    // the same 14-ish symbol set twice, 0x200 codepoints apart - a
    // self-bordered variant (matching a real feature-control-frame
    // compartment's box) and a bare variant with no border. This class uses
    // the bare variant (starting at U+E161) since these characters get
    // mixed inline with plain-font text that has no matching border. Using
    // (char)0xNNNN instead of a literal character keeps these code points
    // unambiguous in source (private-use-area glyphs render as nothing/a
    // placeholder box in any font but "SolidWorks GDT" itself).
    public static class GdtSymbolFontTranslator
    {
        // The font must be installed on whatever machine opens the
        // generated report for these characters to render as symbols
        // instead of the usual "tofu" placeholder box - EPPlus can set the
        // font name on a rich-text run, but it can't embed the font file
        // itself into the .xlsx.
        public const string FontFamilyName = "SolidWorks GDT";

        // Longest phrase first, so "Profile of a Surface" is tried before
        // any shorter phrase that happens to share its leading words.
        private static readonly List<KeyValuePair<string, char>> SymbolsByPhraseLengthDesc =
            new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase)
            {
                { "Profile of a Surface", (char)0xE168 },
                { "Profile of a Line", (char)0xE167 },
                { "Circular Runout", (char)0xE175 },
                { "Total Runout", (char)0xE176 },
                { "Controlled Radius", (char)0xE163 },
                { "Perpendicularity", (char)0xE16A },
                { "Cylindricity", (char)0xE165 },
                { "Parallelism", (char)0xE16B },
                { "Concentricity", (char)0xE16F },
                { "Angularity", (char)0xE169 },
                { "Straightness", (char)0xE161 },
                { "Circularity", (char)0xE164 },
                { "Flatness", (char)0xE162 },
                { "Position", (char)0xE16E },
                { "Symmetry", (char)0xE171 },
            }
            .OrderByDescending(kvp => kvp.Key.Length)
            .ToList();

        public static IReadOnlyList<GdtTextRun> Translate(string text)
        {
            List<GdtTextRun> runs = new List<GdtTextRun>();

            if (string.IsNullOrEmpty(text))
            {
                runs.Add(new GdtTextRun(text ?? "", false));
                return runs;
            }

            // GtolTextFormatter joins a composite/multi-frame callout with
            // " / " - split the same way so each frame's leading symbol
            // word is matched independently (a continuation frame with no
            // repeated symbol name, e.g. "0.01 A B", is left untouched).
            string[] segments = text.Split(
                new[] { " / " }, StringSplitOptions.None);

            for (int i = 0; i < segments.Length; i++)
            {
                if (i > 0)
                    runs.Add(new GdtTextRun(" / ", false));

                AppendSegment(segments[i], runs);
            }

            return runs;
        }

        private static void AppendSegment(string segment, List<GdtTextRun> runs)
        {
            char symbolChar;
            string matchedPrefix = FindLeadingSymbolPhrase(segment, out symbolChar);

            if (matchedPrefix == null)
            {
                runs.Add(new GdtTextRun(segment, false));
                return;
            }

            runs.Add(new GdtTextRun(symbolChar.ToString(), true));

            string remainder = segment.Substring(matchedPrefix.Length);

            if (remainder.Length > 0)
                runs.Add(new GdtTextRun(remainder, false));
        }

        // Matches a known characteristic-type phrase at the start of
        // segment (after any leading whitespace), requiring a word
        // boundary right after it (so "Position" doesn't match a
        // hypothetical "Positional ..."). Returns the matched substring of
        // segment itself (leading whitespace included) so the caller can
        // just Substring(...) past it, or null if nothing matched.
        private static string FindLeadingSymbolPhrase(string segment, out char symbolChar)
        {
            symbolChar = '\0';

            string trimmed = segment.TrimStart();
            int leadingWhitespace = segment.Length - trimmed.Length;

            foreach (KeyValuePair<string, char> candidate in SymbolsByPhraseLengthDesc)
            {
                string phrase = candidate.Key;

                if (trimmed.Length < phrase.Length)
                    continue;

                if (string.Compare(
                        trimmed, 0, phrase, 0, phrase.Length,
                        StringComparison.OrdinalIgnoreCase) != 0)
                    continue;

                bool followedByLetter =
                    trimmed.Length > phrase.Length &&
                    char.IsLetter(trimmed[phrase.Length]);

                if (followedByLetter)
                    continue;

                symbolChar = candidate.Value;
                return segment.Substring(0, leadingWhitespace + phrase.Length);
            }

            return null;
        }
    }

    // One piece of translated text: either plain text, or a single symbol
    // character meant to be rendered in GdtSymbolFontTranslator.FontFamilyName.
    public struct GdtTextRun
    {
        public GdtTextRun(string text, bool isSymbol)
        {
            Text = text;
            IsSymbol = isSymbol;
        }

        public string Text { get; }

        public bool IsSymbol { get; }
    }
}
