using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace BINSPECTION.Core
{
    // Splits a hole callout's displayed text into its individual feature
    // values (a thread designation, a tap drill diameter, a depth, a
    // countersink/counterbore diameter, an angle, "THRU ALL", etc.), each
    // labeled in plain English (e.g. "DEPTH .35", "COUNTER SINK DIA .14") -
    // per an explicit user request for a tapped+countersunk hole to read as
    // one line per real feature rather than merged/garbled text.
    //
    // Confirmed against a real drawing (2026-09-14, see GetText/
    // GetHoleCalloutVariables diagnostic dump this replaced): a hole
    // callout's above/below text can combine more than one independent
    // feature value onto the SAME visual line - e.g.
    // "<HOLE-SINK><MOD-DIAM> 20.32 X 100°, NEAR SIDE" is genuinely THREE
    // values (a countersink diameter, an angle, and a trailing note), joined
    // with " X " and ",". Splitting only on newlines (the previous
    // implementation) silently merged those into one balloon/one report row.
    //
    // GetHoleCalloutVariables() looks like the more direct source (one entry
    // per real feature value, already separated), but the same diagnostic
    // showed it returns MORE variables than are ever actually shown on the
    // drawing (a tapped hole's Thread Description/Thread Class came back
    // even though neither appears in the callout text at all), and one
    // variable (a "Thru" status) showed up twice for what's really one
    // visible "THRU ALL". Building report rows from that list directly
    // produced phantom rows and a duplicate. The callout's own displayed
    // text is the one thing guaranteed to match what's actually printed and
    // what an inspector actually reads, so it stays the source of truth -
    // this just splits it more finely than a plain newline split can.
    //
    // 2026-09-16: reworked to label each split-out value in plain English
    // instead of leaving the raw symbol/tag text - and to fix a real data
    // gap a user report surfaced. A tapped+countersunk hole's FULL callout
    // is three lines: a thread designation on top ("M3 X 0.5 6H"), the tap
    // drill diameter+depth in the middle ("<MOD-DIAM> .100±.005
    // <HOLE-DEPTH> .350±.005"), and the countersink diameter+angle below
    // ("<HOLE-SINK><MOD-DIAM> .140±.005 X 82°±1.000°"). The OLD
    // implementation only ever read swDimensionTextCalloutAbove/Below -
    // which captures the top and bottom lines fine, but the middle
    // diameter+depth line is neither of those (it's the dimension's own
    // Prefix+value+Suffix, sandwiched between the two callout lines) - so
    // that middle line's values were silently dropped entirely, not
    // garbled, just missing. GetSegments now ALSO reads
    // swDimensionTextPrefix/Suffix and splices them in between Above and
    // Below to recover that line - see BuildMiddleLine's remarks; this part
    // is best-effort/not yet confirmed against a real drawing (no live
    // SolidWorks session available while writing this), unlike
    // MOD-DIAM/HOLE-SINK's Above/Below usage which WAS previously confirmed
    // live. Worth double-checking DIA/DEPTH actually show up after this
    // change, and reporting back if they still don't so this can be
    // corrected against the real API behavior instead of guessed at again.
    public static class HoleCalloutExtractor
    {
        // SolidWorks embeds symbol markup directly in hole callout text as
        // "<TAG-NAME>" rather than the glyph itself, and each tag also maps
        // to the real symbol character used to prefix its value in the
        // split-out segment (see BuildLabel) - e.g. "<HOLE-SINK><MOD-DIAM>"
        // becomes "⌵⌀" (countersink + diameter), matching exactly what the
        // real drawing shows. Confirmed 2026-09-16 against a real M3
        // tapped+countersunk hole's own drawing callout (user-supplied
        // screenshot: "⌀ .100±.005 ▽ .350±.005" / "⌵⌀ .140±.005 X
        // 82°±1.000°") - a plain-English word version of these labels was
        // tried first per an earlier request, then replaced with these
        // symbols per a follow-up request to match the drawing exactly
        // ("this should apply to the whole report" - Balloon Manager's grid
        // and the exported report both source from this same method's
        // output, so one change here covers both). All four are ordinary
        // Unicode characters (not the special "SolidWorks GDT" PUA font
        // GtolTextFormatter/GdtSymbolFontTranslator need for true GD&T
        // frame symbols) - ⌀/⌵ render in any normal font, confirmed by the
        // fact they were already showing correctly on the drawing itself.
        // HOLE-CBORE/HOLE-SPOT are added from SOLIDWORKS' own documented
        // calloutformat.txt variable names (help.solidworks.com "Modifying
        // the Hole Callout Format" / GoEngineer "Customizing the SOLIDWORKS
        // Hole Callout File") - not yet separately confirmed against a real
        // drawing. An unrecognized tag just contributes no symbol (its
        // value still comes through, unlabeled) rather than being guessed
        // at.
        private static readonly Dictionary<string, string> TagLabels =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "MOD-DIAM", "⌀" }, // ⌀ diameter
                { "HOLE-SINK", "⌵" }, // ⌵ countersink
                { "HOLE-DEPTH", "▽" }, // ▽ depth
                { "HOLE-CBORE", "⌴" }, // ⌴ counterbore
                { "HOLE-SPOT", "SF" }, // no standard Unicode spotface symbol - left as text
            };

        private static readonly Regex TagPattern =
            new Regex("<([^>]*)>", RegexOptions.Compiled);

        // A metric thread designation ("M3 X 0.5 6H", "M6X1", ...) uses its
        // own " X " for pitch notation - a whole line matching this must
        // never be run through the value tokenizer below (which would shred
        // it into "M3" + "0.5 6H"), and is instead kept as one untouched
        // segment. This is exactly the bug a 2026-09-16 user report
        // surfaced - the previous implementation had no such exception.
        private static readonly Regex ThreadDesignationLine =
            new Regex(@"^\s*M\d+(\.\d+)?\s*X", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // One tag-labeled numeric value within a (non-thread-designation)
        // line: an optional run of one or more symbol tags, then the number
        // itself, then an optional "°" (captured separately from any
        // tolerance, since SolidWorks writes an angle's own degree sign
        // immediately after the nominal - "82°±1.000°" - before the
        // tolerance's repeated one), then an optional "±tolerance"
        // (discarded - see class remarks on why the Dimension cell never
        // shows tolerance). Matches are found in sequence via Regex.Matches,
        // so any connective text between values (" X ", ",", plain
        // whitespace) is simply skipped rather than needing its own rule.
        private static readonly Regex TaggedValue = new Regex(
            @"(?<tags>(?:<[^>]+>)*)\s*(?<value>[\d.]+)(?<deg>°)?(?:\s*±\s*[\d.]+°?)?",
            RegexOptions.Compiled);

        public static List<string> GetSegments(ModelDoc2 model, DisplayDimension dim)
        {
            try
            {
                // NOTE: a chamfer dimension ("12.70 X 45.00°") does NOT
                // carry combined text through swDimensionTextCalloutAbove/
                // CalloutBelow the way a hole callout does - confirmed
                // 2026-09-15 against a real drawing (every
                // swDimensionTextParts_e slot came back empty for a genuine
                // Type2 == swChamferDimension dimension). A chamfer's
                // distance+angle are read via Core/ChamferValueReader.cs
                // instead (IDimension.GetSystemChamferValues), not here.
                //
                // NOTE: gating this on dim.IsHoleCallout() (as an earlier
                // version of this method did) turned out to be wrong -
                // confirmed 2026-09-15 against a real drawing containing a
                // DimXpert-driven hole callout (tapped + countersunk hole,
                // IsDimXpert()=True) whose CalloutAbove/Below text was
                // genuinely populated even though IsHoleCallout() reported
                // False for it - so that gate silently dropped every
                // DimXpert hole callout back to the single-value path.
                if (dim == null)
                    return null;

                string above = dim.GetText((int)swDimensionTextParts_e.swDimensionTextCalloutAbove);
                string below = dim.GetText((int)swDimensionTextParts_e.swDimensionTextCalloutBelow);
                string middle = BuildMiddleLine(model, dim);

                List<string> rawLines = new[] { above, middle, below }
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                if (rawLines.Count == 0)
                    return null;

                List<string> segments = new List<string>();

                foreach (string rawLine in rawLines)
                {
                    // Each source can itself be multiple physical lines
                    // (SolidWorks uses "\r\n" internally) - split on any
                    // common line ending BEFORE tag-scanning, deliberately
                    // NOT via AnnotationScanner.SplitLines/
                    // BalloonManager.NormalizeNoteText, since that strips
                    // "<...>" markup outright (it treats it as SolidWorks
                    // rich-text formatting noise) - which would destroy the
                    // "<MOD-DIAM>"/"<HOLE-SINK>"/etc. tags this method needs
                    // intact to build labels.
                    foreach (string piece in rawLine.Split(
                        new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None))
                    {
                        string line = piece.Trim();

                        if (line.Length > 0)
                            segments.AddRange(SplitLine(line));
                    }
                }

                return segments.Count > 0 ? segments : null;
            }
            catch
            {
                return null;
            }
        }

        // Recovers the ONE line neither CalloutAbove nor CalloutBelow
        // captures: a hole callout's own tap-drill-diameter+depth line,
        // sandwiched between the thread designation above it and the
        // countersink/counterbore line below it.
        //
        // CONFIRMED live 2026-09-16 (a real diagnostic dump, not a guess)
        // this line's actual numbers come back ALREADY spelled out
        // literally inside Prefix/Suffix - e.g. Prefix=
        // "<MOD-DIAM> .1 <HOLE-DEPTH> .35", Suffix="" for a real M3 tapped +
        // countersunk hole. That is DIFFERENT from an ordinary dimension's
        // Prefix/Suffix, which are pure symbol/text decoration around
        // SolidWorks' own auto-inserted numeric value and never contain a
        // digit themselves (e.g. Prefix="<MOD-DIAM>" alone, confirmed in the
        // same dump for a plain "⌀0.095" callout). The first version of this
        // method always appended the dimension's own live-read nominal after
        // Prefix - correct for the second case, but for the first it
        // produced a real duplication bug ("<HOLE-DEPTH> .35" + the
        // unrelated live value "0.07" read as "DEPTH .350.07"). Checking
        // whether Prefix/Suffix already contain a digit is what
        // distinguishes the two cases - only insert the live value when
        // neither one does.
        private static string BuildMiddleLine(ModelDoc2 model, DisplayDimension dim)
        {
            if (model == null)
                return null;

            string prefix = dim.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix);
            string suffix = dim.GetText((int)swDimensionTextParts_e.swDimensionTextSuffix);

            if (string.IsNullOrWhiteSpace(prefix) && string.IsNullOrWhiteSpace(suffix))
                return null;

            if (ContainsDigit(prefix) || ContainsDigit(suffix))
                return (prefix ?? string.Empty) + (suffix ?? string.Empty);

            double nominal, plusTolerance, minusTolerance;
            bool resolved;

            ReportGenerator.ReadDimensionValues(
                model, dim, out nominal, out plusTolerance, out minusTolerance, out resolved);

            if (!resolved)
                return null;

            return (prefix ?? string.Empty) + nominal.ToString("0.####", CultureInfo.InvariantCulture) +
                (suffix ?? string.Empty);
        }

        private static bool ContainsDigit(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            foreach (char c in text)
            {
                if (char.IsDigit(c))
                    return true;
            }

            return false;
        }

        // One line can combine more than one feature value. A trailing
        // plain-text note (", NEAR SIDE") is split off first and passed
        // through unlabeled/untranslated (existing behavior); the remaining
        // value-bearing part is either kept whole (a thread designation -
        // see ThreadDesignationLine) or tokenized into one labeled segment
        // per TaggedValue match.
        private static IEnumerable<string> SplitLine(string line)
        {
            string[] commaPieces = line.Split(',');

            string valuePart = commaPieces[0].Trim();

            if (valuePart.Length > 0)
            {
                if (ThreadDesignationLine.IsMatch(valuePart))
                {
                    yield return StripUnknownTags(valuePart);
                }
                else
                {
                    bool any = false;

                    foreach (Match match in TaggedValue.Matches(valuePart))
                    {
                        if (!match.Groups["value"].Success)
                            continue;

                        any = true;
                        yield return BuildLabeledSegment(match);
                    }

                    if (!any)
                        yield return StripUnknownTags(valuePart);
                }
            }

            for (int i = 1; i < commaPieces.Length; i++)
            {
                string trimmed = StripUnknownTags(commaPieces[i].Trim());

                if (trimmed.Length > 0)
                    yield return trimmed;
            }
        }

        // "⌵⌀ .140", "▽ .350", "82°", "M3 X 0.5 6H" (no tags at all - label
        // stays empty) - joins every recognized tag's symbol in the match
        // (in the order they appeared, no separator between them - "⌵⌀" not
        // "⌵ ⌀", matching how they're printed adjacent on the real drawing),
        // puts that in front of the value with one space, and keeps a
        // degree sign as the literal "°" character right after the value
        // (matching the drawing's own "82°" rather than spelling out "DEG").
        private static string BuildLabeledSegment(Match match)
        {
            string label = string.Concat(
                TagPattern.Matches(match.Groups["tags"].Value)
                    .Cast<Match>()
                    .Select(m =>
                    {
                        string text;
                        return TagLabels.TryGetValue(m.Groups[1].Value, out text) ? text : null;
                    })
                    .Where(s => !string.IsNullOrEmpty(s)));

            string value = match.Groups["value"].Value;
            bool isDegree = match.Groups["deg"].Success;

            string segment = label.Length > 0 ? label + " " + value : value;

            return isDegree ? segment + "°" : segment;
        }

        // Drops any remaining "<...>" markup (an unrecognized tag, or
        // SolidWorks rich-text formatting noise) from text that isn't going
        // through the tag-aware label builder above - same fallback
        // treatment BalloonManager.NormalizeNoteText gives ordinary note
        // text.
        private static string StripUnknownTags(string text)
        {
            return TagPattern.Replace(text, string.Empty).Trim();
        }

        // Live re-display text for BalloonGridService's grid refresh - the
        // same segments GetSegments splits a combined hole-callout/chamfer
        // value into at creation time, rejoined with " X " so a grouped
        // balloon's grid row keeps showing exactly what was captured instead
        // of BalloonGridService falling back to a raw GetDimension2(0) read,
        // which has no guarantee of landing on the distance sub-dimension
        // rather than the angle one. Only for a GENUINELY combined value (2+
        // segments) - a single-segment hole callout (a plain drilled hole,
        // say) still goes through BalloonGridService's normal nominal
        // formatting, unchanged, rather than losing that to this raw-text
        // path. Null otherwise.
        public static string GetCombinedDisplayText(ModelDoc2 model, DisplayDimension dim)
        {
            List<string> segments = GetSegments(model, dim);

            return segments != null && segments.Count > 1
                ? string.Join(" X ", segments)
                : null;
        }
    }
}
