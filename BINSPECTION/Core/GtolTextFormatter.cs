using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace BINSPECTION.Core
{
    // Builds two things per GD&T feature control frame: a plain-English
    // display string (for Characteristic.DimensionName - JSON, UI grids,
    // legacy matching, anywhere the special font can't be used) and, where
    // possible, a "boxed" rich-text string using the real "SolidWorks GDT"
    // font that renders as an actual bordered feature-control-frame
    // compartment strip - matching how the frame looks on the drawing
    // itself. See ReportGenerator.WriteCharacteristicCell for where the
    // boxed text gets used, falling back to GdtSymbolFontTranslator's
    // plain-text-phrase translation when it isn't available (legacy-format
    // frames, or an unmapped symbol).
    //
    // SolidWorks changed the Gtol frame data model in 2022 (swGtolFormatType_e):
    // a frame created before 2022 only resolves through IGtol's older
    // GetFrameSymbols3/GetFrameValues methods (arrays of already-readable
    // strings - tolerance symbols come back as "<Library-SymbolName>" tags
    // like "<IGTOL-FLAT>", not font glyph codes), while a frame created in
    // 2022+ only resolves through IGtol.GetFrame -> IGtolFrame.GetSymbolXml
    // (a documented XML schema - see SOLIDWORKS API Help > "Gtol Frame XML
    // Schema"). Only the modern path is decomposed enough (separate
    // tolerance value/range-symbol/material-condition/datum-compartment
    // fields, rather than one pre-formatted string) to safely rebuild a
    // boxed compartment string - the legacy path's already-formatted
    // strings only feed the plain-English path.
    //
    // The boxed font mechanism itself (confirmed by rendering the real font
    // file, U:\Beaus Storage\backup\solidworks gdt.ttf, to images - not
    // guessed): every symbol glyph exists twice, 0x200 codepoints apart - a
    // bare variant and a "bordered" variant whose glyph draws its own
    // top/bottom box lines. Separately, the ENTIRE ASCII printable range has
    // a "boxed" shifted copy at codepoint+0xE000 (confirmed for digits,
    // '.', '-', letters, space, and '|' - the '|' shifted copy, 0xE07C,
    // draws as a compartment DIVIDER that closes one box and opens the
    // next). Stringing a bordered symbol glyph followed by shifted
    // characters produces one continuous bordered strip with dividers
    // exactly where the shifted '|' characters are - that's the whole
    // trick, no decoding of InspectionProcessor.Core's GdtFrameSplitService
    // needed (that class runs the shift in the opposite direction, to turn
    // already-boxed drawing text back into plain numbers).
    public static class GtolTextFormatter
    {
        private const int PuaShift = 0xE000;
        private const int BorderedOffset = 0x200;

        // The 14 tolerance-symbol codes SolidWorks' GTOL/IGTOL/GGTOL
        // libraries share (see gtol.sym), plus GGTOL's 2 extra codes -
        // shared by both the legacy "<Library-SymbolName>" tag format and
        // the modern XML <ToleranceSymbol>Library-SymbolName</ToleranceSymbol>
        // format, since both use the exact same symbol-name suffixes.
        private static readonly Dictionary<string, string> SymbolNames =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "ANGULAR", "Angularity" },
                { "CIRC", "Circularity" },
                { "CONC", "Concentricity" },
                { "CYL", "Cylindricity" },
                { "FLAT", "Flatness" },
                { "LPROF", "Profile of a Line" },
                { "PARA", "Parallelism" },
                { "PERP", "Perpendicularity" },
                { "POSI", "Position" },
                { "SPROF", "Profile of a Surface" },
                { "SRUN", "Circular Runout" },
                { "STRAIGHT", "Straightness" },
                { "SYMMETRY", "Symmetry" },
                { "TRUN", "Total Runout" },
                { "LONG", "Long" },
                { "AXIS", "Axis" },
            };

        // Bare (unbordered) "SolidWorks GDT" font codepoints for the same
        // symbol codes above - reverse-engineered by rendering the font
        // file (see [[binspection_gdt_symbol_font]] memory). The GGTOL-only
        // "LONG"/"AXIS" codes have no confirmed codepoint, so callouts using
        // them fall back to plain English text with no box.
        private static readonly Dictionary<string, char> SymbolGlyphs =
            new Dictionary<string, char>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "STRAIGHT", (char)0xE161 },
                { "FLAT", (char)0xE162 },
                { "CIRC", (char)0xE164 },
                { "CYL", (char)0xE165 },
                { "LPROF", (char)0xE167 },
                { "SPROF", (char)0xE168 },
                { "ANGULAR", (char)0xE169 },
                { "PERP", (char)0xE16A },
                { "PARA", (char)0xE16B },
                { "POSI", (char)0xE16E },
                { "CONC", (char)0xE16F },
                { "SYMMETRY", (char)0xE171 },
                { "SRUN", (char)0xE175 },
                { "TRUN", (char)0xE176 },
            };

        private const char DiameterGlyph = (char)0xE177;
        private const char MmcGlyph = (char)0xE16D;
        private const char LmcGlyph = (char)0xE16C;

        // Additional standalone "SolidWorks GDT" font codepoints found in a
        // real legacy spreadsheet export (2026-09-21, D:\stress test2.xlsx)
        // that DecodeBoxedText didn't recognize yet - confirmed by actually
        // rendering these exact codepoints through the real font file
        // (U:\Beaus Storage\backup\solidworks gdt.ttf, System.Drawing),
        // same method used to build the maps above. Unlike SymbolGlyphs'
        // GD&T tolerance-type symbols, these appeared as plain standalone
        // characters in otherwise-ordinary cell text (no bordered "+0x200"
        // variant observed, no boxed-ASCII framing around them), so they're
        // decoded directly rather than through TryGetSymbolWord/Bordered.
        //
        // DepthGlyph (0xE15E) rendered as a downward triangle with a bar on
        // top - the standard depth symbol, matching the SAME Unicode
        // character (▽, U+25BD) this codebase's HoleCalloutExtractor
        // already uses for depth callouts elsewhere (see
        // [[binspection_hole_callout_symbols]]) - kept consistent with that
        // existing convention rather than inventing a different mapping.
        //
        // CounterboreGlyph (0xE124) rendered as a squared-off "U" (open top,
        // flat bottom) - the standard counterbore/spotface symbol, matching
        // HoleCalloutExtractor's existing ⌴ (U+2334) mapping for the same
        // reason.
        //
        // CountersinkGlyph (0xE125) rendered as a plain "V" - the standard
        // countersink symbol, matching HoleCalloutExtractor's existing ⌵
        // (U+2335) mapping.
        //
        // UnequalDisposalGlyph (0xE40D) rendered as a circled "U" with
        // border bars, found sitting between a profile frame's two
        // tolerance-value halves ("Profile of a Surface .003[glyph].001 A B
        // C") - the ASME Y14.5 "unequally disposed profile tolerance"
        // modifier. Decoded to the bare letter 'U' (no circle), matching
        // this class's own existing precedent for Mmc/LmcGlyph below
        // (which decode to plain "M"/"L", not the circled Unicode forms).
        private const char DepthGlyph = (char)0xE15E;
        private const char CounterboreGlyph = (char)0xE124;
        private const char CountersinkGlyph = (char)0xE125;
        private const char UnequalDisposalGlyph = (char)0xE40D;

        // One frame's content, ready to display two ways: DisplayText (plain
        // English, always populated) and BoxText (the real font's boxed
        // compartment string, null when the frame's symbol/range-symbol
        // isn't one of the ones with a confirmed glyph above, or the frame
        // came from the legacy pre-2022 API).
        public struct GdtFrameResult
        {
            public string DisplayText;

            public string BoxText;

            // This frame's tolerance zone value (e.g. 0.004), parsed out of
            // FrameData.ToleranceValue - null when the frame has no numeric
            // tolerance to report (a plain BASIC box) or the text didn't
            // parse. Lets ReportGenerator populate a real Upper Limit for a
            // GD&T characteristic instead of leaving it blank - see
            // ReportGenerator's Upper/Lower Limit sourcing. A one-sided
            // zone, unlike a dimension's +/- tolerance: callers should
            // treat this as Upper Limit with Lower Limit = 0, never as a
            // +/- range.
            public double? ToleranceValue;
        }

        // First numeric token in the frame's raw tolerance text (see
        // FrameData.ToleranceValue's remarks - modern frames give a clean
        // number, legacy frames can carry extra pre-formatted text like a
        // trailing "M"/"L" modifier). Returns null rather than 0 when
        // nothing numeric is found, so callers don't mistake "couldn't
        // parse" for "a real zero-width tolerance."
        private static readonly Regex ToleranceNumber =
            new Regex(@"[0-9]+(?:\.[0-9]+)?", RegexOptions.Compiled);

        private static double? ParseToleranceValue(string rawToleranceValue)
        {
            if (string.IsNullOrWhiteSpace(rawToleranceValue))
                return null;

            Match match = ToleranceNumber.Match(rawToleranceValue);

            if (!match.Success)
                return null;

            double parsed;

            return double.TryParse(
                match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : (double?)null;
        }

        // Best-effort: returns one result per composite frame in this GD&T
        // annotation - almost always a single-element list, but a composite
        // callout ("Position 0.004 A B C" over "Position 0.001 A B") comes
        // back as two elements, one per frame. The caller (see
        // CommandManagerHandler.OnCreateBalloons) is the one that decides
        // what a multi-element result means for numbering - this method
        // only reads text. This GTOL's own datum identifier (if it's
        // defining a datum) is folded into the first element's DisplayText,
        // and any below-frame text lines are folded into the last one's
        // DisplayText (BoxText is left as the frame's own content only,
        // since neither addition has a confirmed boxed-glyph treatment).
        // Always returns at least one element - the old generic label if
        // nothing could be read - so a report row is never left blank.
        public static List<GdtFrameResult> GetFrames(IGtol gtol)
        {
            try
            {
                List<GdtFrameResult> results = new List<GdtFrameResult>();

                int frameCount = gtol.GetFrameCount();
                int format = gtol.GetFormat();
                bool isModern = format == (int)swGtolFormatType_e.GTOL_SW2022;

                // A composite frame's own symbol tag comes back empty for
                // every frame after the first (SolidWorks only draws the
                // characteristic symbol once, in the top compartment, since
                // the frames are stacked as one box on a drawing). Once
                // split into separate report rows they're no longer stacked
                // together, so each row needs its own copy of the symbol to
                // stay readable on its own - carry the last real symbol
                // code forward onto every blank-symbol continuation frame.
                string carriedSymbolCode = null;

                for (int frameNumber = 1; frameNumber <= frameCount; frameNumber++)
                {
                    FrameData? data = null;

                    try
                    {
                        data =
                            isModern
                                ? GetModernFrameData(gtol, frameNumber)
                                : GetLegacyFrameData(gtol, frameNumber);
                    }
                    catch (System.Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"GtolTextFormatter frame {frameNumber} Error: {ex}");
                    }

                    if (data == null)
                        continue;

                    FrameData frameData = data.Value;

                    if (!string.IsNullOrEmpty(frameData.SymbolCode))
                        carriedSymbolCode = frameData.SymbolCode;
                    else
                        frameData.SymbolCode = carriedSymbolCode;

                    string displayText = BuildDisplayText(frameData);

                    if (string.IsNullOrWhiteSpace(displayText))
                        continue;

                    // Attempted for legacy frames too, not just modern ones
                    // - BuildBoxText's own ASCII-only safety check (see
                    // TryAppendShifted) is what makes this safe: a legacy
                    // frame's pre-formatted text that happens to be plain
                    // ASCII gets the same real boxed-frame treatment a
                    // modern frame does, while one that embeds an actual
                    // Unicode symbol character still safely falls back to
                    // plain text instead of risking a garbage glyph.
                    string boxText = BuildBoxText(frameData);

                    results.Add(new GdtFrameResult
                    {
                        DisplayText = displayText.Trim(),
                        BoxText = boxText,
                        ToleranceValue = ParseToleranceValue(frameData.ToleranceValue),
                    });
                }

                if (results.Count == 0)
                {
                    results.Add(new GdtFrameResult
                    {
                        DisplayText = "GD&T Feature Control Frame",
                        BoxText = null,
                    });
                }

                string datumIdentifier = gtol.GetDatumIdentifier();

                if (!string.IsNullOrWhiteSpace(datumIdentifier))
                {
                    GdtFrameResult first = results[0];
                    first.DisplayText = "Datum " + datumIdentifier.Trim() + " " + first.DisplayText;
                    results[0] = first;
                }

                List<string> belowFrameLines = GetBelowFrameTextLines(gtol);

                if (belowFrameLines.Count > 0)
                {
                    int lastIndex = results.Count - 1;
                    GdtFrameResult last = results[lastIndex];
                    last.DisplayText = last.DisplayText + " " + string.Join(" ", belowFrameLines);
                    results[lastIndex] = last;
                }

                return results;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"GtolTextFormatter.GetFrames Error: {ex}");

                return new List<GdtFrameResult>
                {
                    new GdtFrameResult { DisplayText = "GD&T Feature Control Frame", BoxText = null },
                };
            }
        }

        // One frame's content, decomposed enough to build either the plain
        // English text or (modern frames only) the boxed font string.
        // SymbolCode is the raw "<Library-SymbolName>"-suffix code (e.g.
        // "FLAT") - not yet resolved to a friendly name or a glyph, so both
        // BuildDisplayText and BuildBoxText can each map it their own way.
        private struct FrameData
        {
            public string SymbolCode;

            // "phi"/"sPhi"/"sqr"/"deg"/"" - see Gtol Frame XML Schema's
            // <PrimaryRangeSymbol>. Empty/null for legacy frames, whose
            // GetFrameValues string already has any such symbol baked in as
            // plain text.
            public string RangeSymbolCode;

            public string ToleranceValue;

            // "M"/"L"/"" - MMC/LMC. Empty for legacy frames (same reason as
            // RangeSymbolCode).
            public string MaterialCondition;

            // One entry per datum compartment, already joined within a
            // compartment (e.g. a combined "C-D" datum feature is one
            // entry, "C-D", not two) - see BuildDisplayText/BuildBoxText for
            // how compartments get separated from each other.
            public List<string> DatumCompartments;
        }

        // Frames created in SOLIDWORKS 2022 or later - IGtol.GetFrame only
        // resolves for these (throws/returns null for older frames). Reads
        // the frame's full content from GetSymbolXml() per the documented
        // schema: <ToleranceSymbol>, <ToleranceRangeInfo><PrimaryToleranceValue>
        // (+ optional range/zone symbols), <MaterialCondition>, and
        // <DatumCompartment>...<DatumLetter> (possibly nested under
        // <Datums>/<SubDatums> - Descendants() finds every DatumLetter
        // regardless of nesting depth, joined with "-" per compartment to
        // preserve a combined-datum-feature callout like "C-D").
        private static FrameData? GetModernFrameData(IGtol gtol, int frameNumber)
        {
            IGtolFrame frame = gtol.GetFrame(frameNumber) as IGtolFrame;

            if (frame == null)
                return null;

            string xml = frame.GetSymbolXml();

            if (string.IsNullOrWhiteSpace(xml))
                return null;

            XElement root = XElement.Parse(xml);

            string symbolCode = ExtractSymbolCode((string)root.Element("ToleranceSymbol"));

            XElement rangeInfo = root.Element("ToleranceRangeInfo");
            string toleranceValue = rangeInfo != null ? (string)rangeInfo.Element("PrimaryToleranceValue") : null;
            string rangeSymbolCode = rangeInfo != null ? (string)rangeInfo.Element("PrimaryRangeSymbol") : null;

            XElement materialCondition = root.Element("MaterialCondition");
            string materialConditionCode =
                materialCondition != null && (string)materialCondition.Element("MaximumMaterialCondition") == "true"
                    ? "M"
                    : materialCondition != null && (string)materialCondition.Element("LeastMaterialCondition") == "true"
                        ? "L"
                        : "";

            List<string> datumCompartments = root.Elements("DatumCompartment")
                .Select(dc => string.Join(
                    "-",
                    dc.Descendants("DatumLetter")
                        .Select(el => (string)el)
                        .Where(s => !string.IsNullOrWhiteSpace(s))))
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            return new FrameData
            {
                SymbolCode = symbolCode,
                RangeSymbolCode = rangeSymbolCode,
                ToleranceValue = toleranceValue,
                MaterialCondition = materialConditionCode,
                DatumCompartments = datumCompartments,
            };
        }

        // Frames created before SOLIDWORKS 2022 - GetFrameSymbols3/
        // GetFrameValues only resolve for these. Both already return
        // human-readable, PRE-FORMATTED strings (tolerance/datum text
        // potentially already carrying their own symbol/modifier as plain
        // text, and a "<Library-SymbolName>" tag for the geometric
        // characteristic symbol) - unlike the modern XML path there's no
        // separate range-symbol/material-condition field to decompose, so
        // RangeSymbolCode/MaterialCondition are left blank and
        // ToleranceValue carries the whole pre-formatted string. That's
        // enough for BuildDisplayText, but not enough to safely rebuild a
        // boxed string (BuildBoxText is never called for legacy frames -
        // see GetFrames).
        private static FrameData? GetLegacyFrameData(IGtol gtol, int frameNumber)
        {
            string[] symbols = gtol.GetFrameSymbols3(frameNumber) as string[];
            string[] values = gtol.GetFrameValues((short)frameNumber) as string[];

            string symbolCode =
                symbols != null && symbols.Length > 0
                    ? ExtractSymbolCode(symbols[0])
                    : null;

            List<string> toleranceParts = new List<string>();
            List<string> datums = new List<string>();

            if (values != null)
            {
                // values[0]/[1] = Tolerance 1/2, values[2..4] = Datum 1-3
                // (see IGtol::GetFrameValues remarks).
                if (values.Length > 0 && !string.IsNullOrWhiteSpace(values[0]))
                    toleranceParts.Add(values[0].Trim());

                if (values.Length > 1 && !string.IsNullOrWhiteSpace(values[1]))
                    toleranceParts.Add(values[1].Trim());

                for (int i = 2; i <= 4 && i < values.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(values[i]))
                        datums.Add(values[i].Trim());
                }
            }

            return new FrameData
            {
                SymbolCode = symbolCode,
                RangeSymbolCode = null,
                ToleranceValue = string.Join(" ", toleranceParts),
                MaterialCondition = "",
                DatumCompartments = datums,
            };
        }

        // Plain-English text for Characteristic.DimensionName - e.g.
        // "Position ⌀0.004 M A B C-D".
        private static string BuildDisplayText(FrameData data)
        {
            List<string> parts = new List<string>();

            string symbolName;

            if (!string.IsNullOrEmpty(data.SymbolCode) &&
                SymbolNames.TryGetValue(data.SymbolCode, out symbolName))
                parts.Add(symbolName);
            else if (!string.IsNullOrEmpty(data.SymbolCode))
                parts.Add(data.SymbolCode);

            if (!string.IsNullOrWhiteSpace(data.ToleranceValue))
            {
                string toleranceText = FormatRangeSymbol(data.RangeSymbolCode) + data.ToleranceValue.Trim();

                if (data.MaterialCondition == "M")
                    toleranceText += " M";
                else if (data.MaterialCondition == "L")
                    toleranceText += " L";

                parts.Add(toleranceText);
            }

            if (data.DatumCompartments != null && data.DatumCompartments.Count > 0)
                parts.Add(string.Join(" ", data.DatumCompartments));

            return string.Join(" ", parts);
        }

        // The real "SolidWorks GDT" font's boxed compartment string - null
        // if the symbol (or, for a continuation frame with no symbol of its
        // own, nothing was ever carried forward) isn't one of the codes
        // with a confirmed glyph, or any piece of text that would need
        // shifting into the boxed-ASCII range contains a character outside
        // plain printable ASCII (see TryAppendShifted) - so the caller can
        // fall back to plain text rather than silently drawing a broken/
        // incomplete box or an unconfirmed/garbage glyph. See
        // GdtSymbolFontTranslator.FontFamilyName for the font name this
        // string must be rendered in.
        //
        // Called for BOTH modern and legacy-format frames (see GetFrames) -
        // a legacy frame's pre-formatted ToleranceValue/DatumCompartments
        // strings can legitimately contain a real Unicode symbol character
        // (e.g. an already-embedded "⌀") that has no confirmed boxed
        // codepoint of its own; TryAppendShifted's ASCII check is what
        // catches that case and safely bails out to the plain-text fallback
        // instead of shifting an arbitrary character into an undefined/
        // wrong PUA codepoint.
        private static string BuildBoxText(FrameData data)
        {
            char symbolGlyph;

            if (string.IsNullOrEmpty(data.SymbolCode) ||
                !SymbolGlyphs.TryGetValue(data.SymbolCode, out symbolGlyph))
                return null;

            StringBuilder box = new StringBuilder();

            box.Append(Bordered(symbolGlyph));

            // Only "phi" (plain diameter) has a confirmed bordered glyph of
            // its own - sPhi/sqr/deg don't, so rather than abort the WHOLE
            // frame's box over one unconfirmed glyph (leaving the user with
            // no boxed frame at all for an otherwise perfectly normal
            // callout), just omit that one range-symbol glyph and keep
            // building the rest of the box - the tolerance value/material
            // condition/datums after it still get the full real
            // feature-control-frame treatment. See the class remarks for
            // why guessing an unconfirmed codepoint isn't done instead.
            if (data.RangeSymbolCode == "phi")
                box.Append(Bordered(DiameterGlyph));

            if (string.IsNullOrWhiteSpace(data.ToleranceValue))
                return null; // a symbol with no value isn't a real box worth drawing

            // Confirmed by actually rendering candidate strings through the
            // real font (System.Drawing, this session's scratchpad) rather
            // than guessing: the symbol compartment and the tolerance-value
            // compartment do NOT share a wall automatically just because
            // they're adjacent characters - without this leading divider,
            // the symbol and the tolerance value run together with no
            // separating line at all, unlike every OTHER compartment
            // boundary (which already gets one, right before each datum
            // below).
            if (!TryAppendShifted(box, "|"))
                return null;

            if (!TryAppendShifted(box, data.ToleranceValue.Trim()))
                return null;

            if (data.MaterialCondition == "M")
                box.Append(Bordered(MmcGlyph));
            else if (data.MaterialCondition == "L")
                box.Append(Bordered(LmcGlyph));

            if (data.DatumCompartments != null)
            {
                foreach (string datum in data.DatumCompartments)
                {
                    if (!TryAppendShifted(box, "|") || !TryAppendShifted(box, datum))
                        return null;
                }
            }

            // Closes the box's right edge. Every INTERNAL boundary already
            // gets its own divider (the leading one above, and one before
            // each datum), but without this trailing one the box's last
            // compartment (whichever came last - a datum, the material
            // condition modifier, or the tolerance value itself if there's
            // neither) is left open on the right - confirmed the same way as
            // the leading divider above: rendered both ways through the
            // real font side by side, and only the version with this
            // trailing divider actually closes the frame the way a real
            // drawing's feature-control-frame does.
            if (!TryAppendShifted(box, "|"))
                return null;

            return box.ToString();
        }

        private static char Bordered(char bareGlyph) =>
            (char)(bareGlyph + BorderedOffset);

        // Shifts every character of ascii into the font's boxed-ASCII range
        // (confirmed for the full 0x20-0x7E printable range - see the class
        // remarks) and appends it to sb. Returns false without appending
        // anything if ascii contains a character outside that range (a real
        // Unicode symbol like "⌀", not plain ASCII text) - shifting such a
        // character by +0xE000 would land on an arbitrary, unconfirmed PUA
        // codepoint rather than a real glyph.
        private static bool TryAppendShifted(StringBuilder sb, string ascii)
        {
            foreach (char c in ascii)
            {
                if (c < 0x20 || c > 0x7E)
                    return false;
            }

            foreach (char c in ascii)
                sb.Append((char)(c + PuaShift));

            return true;
        }

        // Any text below the frame (e.g. a note attached under a composite
        // callout) - available on IGtol regardless of 2021/2022 format.
        private static List<string> GetBelowFrameTextLines(IGtol gtol)
        {
            List<string> lines = new List<string>();

            try
            {
                int lineCount = gtol.GetBelowFrameTextLineCount();

                // 1-based per IGtol::GetBelowFrameTextAt remarks.
                for (int i = 1; i <= lineCount; i++)
                {
                    string line = gtol.GetBelowFrameTextAt(i);

                    if (!string.IsNullOrWhiteSpace(line))
                        lines.Add(line.Trim());
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"GtolTextFormatter.GetBelowFrameTextLines Error: {ex}");
            }

            return lines;
        }

        // "<GTOL-ANGULAR>" (legacy) or "IGTOL-FLAT" (modern XML, no angle
        // brackets) -> "ANGULAR"/"FLAT" - just the symbol-name suffix, not
        // yet resolved to a friendly name or a glyph (see BuildDisplayText/
        // BuildBoxText for those). Returns null for an empty tag (which the
        // modern XML schema uses to mean "this frame is a composite with
        // the one before it").
        private static string ExtractSymbolCode(string rawTag)
        {
            if (string.IsNullOrEmpty(rawTag))
                return null;

            string trimmed = rawTag.Trim().TrimStart('<').TrimEnd('>');

            if (trimmed.Length == 0)
                return null;

            int dash = trimmed.IndexOf('-');

            return dash >= 0 ? trimmed.Substring(dash + 1) : trimmed;
        }

        // Reverses BuildBoxText's own encoding - turns text that already
        // has "SolidWorks GDT" font characters baked directly into it (not
        // rendered through the font, the actual character codes) back into
        // plain text matching the same shape BuildDisplayText produces for
        // BINSPECTION's own characteristics (e.g. "Flatness .005"). Added
        // 2026-09-21 for Core/LegacyNumberImporter.cs - confirmed live that
        // the user's real legacy spreadsheet applies this exact font to
        // GD&T rows' cells, so EPPlus's plain .Text read comes back as raw
        // PUA codepoints that mean nothing (and don't even parse as a
        // number) without this translation.
        //
        // Un-shifts a boxed-ASCII character (+0xE000, see TryAppendShifted)
        // back to plain ASCII, translates a bare OR bordered symbol glyph
        // to its English word (a leading space is inserted before it if
        // the output doesn't already end in one, so "Flatness" doesn't run
        // into whatever came before it), and renders the boxed '|'
        // compartment divider as a plain space rather than a literal pipe
        // (readable prose, not a re-drawn box). Any character OUTSIDE
        // these known PUA ranges - ordinary text - passes through
        // unchanged, so this is safe to call on a cell that was never
        // actually encoded this way at all (the common case for most
        // columns).
        public static string DecodeBoxedText(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return raw;

            StringBuilder result = new StringBuilder(raw.Length);

            foreach (char c in raw)
            {
                string symbolWord = TryGetSymbolWord(c);

                if (symbolWord != null)
                {
                    if (result.Length > 0 && result[result.Length - 1] != ' ')
                        result.Append(' ');

                    result.Append(symbolWord);
                    continue;
                }

                if (c == DiameterGlyph || c == Bordered(DiameterGlyph))
                {
                    result.Append('⌀');
                    continue;
                }

                if (c == MmcGlyph || c == Bordered(MmcGlyph))
                {
                    result.Append('M');
                    continue;
                }

                if (c == LmcGlyph || c == Bordered(LmcGlyph))
                {
                    result.Append('L');
                    continue;
                }

                if (c == DepthGlyph)
                {
                    result.Append('▽');
                    continue;
                }

                if (c == CounterboreGlyph)
                {
                    result.Append('⌴');
                    continue;
                }

                if (c == CountersinkGlyph)
                {
                    result.Append('⌵');
                    continue;
                }

                if (c == UnequalDisposalGlyph)
                {
                    result.Append('U');
                    continue;
                }

                int shiftedCode = c - PuaShift;

                if (shiftedCode >= 0x20 && shiftedCode <= 0x7E)
                {
                    char unshifted = (char)shiftedCode;
                    result.Append(unshifted == '|' ? ' ' : unshifted);
                    continue;
                }

                // The degree sign (0x00B0) shifted the SAME way as the
                // 0x20-0x7E ASCII range above, confirmed by rendering a
                // real legacy spreadsheet's "45°"/"90°"-style angle cells
                // through the actual font (2026-09-21, user report: several
                // angle values were showing as an undecoded box character
                // instead of the degree sign) - it just falls outside the
                // documented-at-the-time "printable ASCII only" range, so
                // it needs its own check rather than widening that range to
                // all of Latin-1 on a guess.
                if (shiftedCode == 0x00B0)
                {
                    result.Append('°');
                    continue;
                }

                result.Append(c);
            }

            return result.ToString().Trim();
        }

        // Bare or bordered - either variant of a known symbol glyph maps to
        // the same English word, since which one a source text used isn't
        // meaningful once decoded back to plain text.
        private static string TryGetSymbolWord(char c)
        {
            foreach (KeyValuePair<string, char> entry in SymbolGlyphs)
            {
                if (c == entry.Value || c == Bordered(entry.Value))
                {
                    string word;
                    return SymbolNames.TryGetValue(entry.Key, out word) ? word : entry.Key;
                }
            }

            return null;
        }

        // phi/sPhi/sqr/deg -> the symbol actually printed on a drawing, per
        // the Gtol Frame XML Schema's <PrimaryRangeSymbol>/<ToleranceZoneSymbol>
        // remarks. Plain-English display only - BuildBoxText has its own
        // glyph handling.
        private static string FormatRangeSymbol(string code)
        {
            switch (code)
            {
                case "phi":
                    return "⌀"; // Ø (diameter)
                case "sPhi":
                    return "S⌀"; // spherical diameter
                case "sqr":
                    return "□"; // □ (square)
                case "deg":
                    return "°"; // °
                default:
                    return "";
            }
        }
    }
}
