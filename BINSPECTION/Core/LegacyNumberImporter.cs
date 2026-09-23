using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BINSPECTION.Models;
using OfficeOpenXml;

namespace BINSPECTION.Core
{
    // Reads the legacy-system reference spreadsheet the user uploads via
    // "Legacy Conversion" (see UI/LegacyConversionWindow.xaml.cs) into plain
    // LegacyBalloonRow data, ready for LegacyNumberMatcher.
    //
    // Real legacy inspection-record exports (e.g. the "5FM-R-08.5.1"
    // InspectionXpert-style template also used by Core/ReportGenerator's
    // own output) do NOT have a clean header-on-row-1 flat table: the
    // report letterhead occupies the first dozen or so rows, the real
    // column header repeats every time the table restarts (once per
    // printed page in the original template), and a row's "nominal" is
    // often only recoverable as the midpoint of separate Upper/Lower Limit
    // columns rather than one clean value column. This reader is built
    // around that shape rather than assuming row 1 is the header.
    public static class LegacyNumberImporter
    {
        // Column header is split into whitespace/punctuation tokens and
        // matched by whole token, not substring - "DIM #" (token "dim")
        // must NOT match "DIMENSION" (token "dimension"), which is exactly
        // the collision a plain Contains("dim") would cause.
        private static readonly string[] BalloonIdTokens = { "legacy", "balloon" };
        private static readonly string[] GenericIdTokens = { "dim", "item", "number", "char", "no" };
        private static readonly string[] NominalTokens = { "nominal", "dimension", "value", "size" };
        private static readonly string[] ClassTokens = { "class" };
        private static readonly string[] MethodTokens = { "method", "gage", "gauge" };

        // A header like "OP    #" identifies a column whose own cell text
        // combines an OPERATION (e.g. "OP 30") with a SHEET NUMBER WITHIN
        // that operation's own paperwork (e.g. "(3)" - the 3rd page of OP
        // 30's inspection record) into one string per row - "OP 30 (3)".
        // Per explicit user correction (2026-09-21): the parenthesized
        // number here is NEVER the legacy balloon number - an earlier
        // version of this importer wrongly read it as one, which surfaced
        // as the debug log/review grid showing a completely wrong "Legacy
        // #" for every GD&T/dimensional row on a spreadsheet using this
        // column shape. The REAL legacy balloon number lives in its own
        // ordinary ID column (e.g. "DIM #" - already handled by
        // BalloonIdTokens/GenericIdTokens below, same as any spreadsheet
        // without this OP column at all). See OpItemPattern/
        // TryParseOpItemCell for how the combined text is split, and
        // LegacyBalloonRow.OperationLabel for why the operation half still
        // matters on its own (LegacyNumberMatcher uses it to hard-filter
        // candidates to the matching drawing sheet - see
        // [[binspection_legacy_number_matching]]'s 2026-09-21 OP-scoping
        // note, added because a legacy row was matching against dimensions
        // from an unrelated operation's sheet just because the nominal
        // happened to line up).
        private static readonly string[] OpItemTokens = { "op" };

        // "OP 30 (3)", "OP30(3)", "OP-30 (3)" etc. - group 1 is the
        // operation label, group 2 is the sheet-within-operation number
        // (see OpItemTokens' remarks - NOT a balloon number).
        private static readonly Regex OpItemPattern =
            new Regex(@"^\s*(OP\s*-?\s*\d+)\s*\(\s*([0-9.]+)\s*\)\s*$", RegexOptions.IgnoreCase);

        // Throws with a user-facing message when NOTHING in the whole
        // workbook looks like a usable table - the caller shows that
        // message directly rather than a stack trace. Rows or sheets that
        // individually fail to parse are just skipped, not fatal.
        public static List<LegacyBalloonRow> Load(string path)
        {
            ExcelLicense.EnsureSet();

            List<LegacyBalloonRow> rows = new List<LegacyBalloonRow>();

            using (ExcelPackage package = new ExcelPackage(new FileInfo(path)))
            {
                foreach (ExcelWorksheet sheet in package.Workbook.Worksheets)
                {
                    if (sheet.Dimension == null)
                        continue;

                    rows.AddRange(ReadSheet(sheet));
                }
            }

            rows = DeduplicateAcrossSheets(rows);

            if (rows.Count == 0)
            {
                throw new InvalidOperationException(
                    "Couldn't find a usable table in this spreadsheet. Expected a header row naming the " +
                    "legacy balloon/dimension number (e.g. \"DIM #\", \"Balloon\", \"Legacy Number\") and either " +
                    "a nominal/dimension value column or both an \"Upper Limit\" and \"Lower Limit\" column.");
            }

            return rows;
        }

        // Some legacy export templates repeat the entire dimension table
        // across more than one worksheet tab (e.g. a "Master" copy and a
        // "Non CMM" copy of the same inspection record) - reading every
        // sheet is still correct in general (a sheet-specific subset of
        // rows is common too), but an identical (legacy number, nominal)
        // pair appearing on more than one sheet is that same physical
        // characteristic restated, not two different ones, so only the
        // first occurrence is kept. A genuine collision - two DIFFERENT
        // legacy numbers landing on the same nominal - is left alone; that
        // is a real ambiguity for LegacyNumberMatcher to flag, not an
        // import artifact.
        // Every cell's raw text is read through here rather than
        // sheet.Cells[row, col].Text directly - GtolTextFormatter.
        // DecodeBoxedText un-translates any "SolidWorks GDT" font
        // characters baked directly into a cell (confirmed live,
        // 2026-09-21 - a GD&T row's dimension cell in the user's real
        // legacy file has that font applied and shows as a single odd
        // glyph, meaningless and unparseable as a number without this)
        // back into plain text matching the shape BINSPECTION's own GD&T
        // text uses. A no-op pass-through for any cell that wasn't encoded
        // this way - safe to call on every column, not just the ones
        // known to carry GD&T info.
        private static string GetCellText(ExcelWorksheet sheet, int row, int col)
        {
            return GtolTextFormatter.DecodeBoxedText(sheet.Cells[row, col].Text);
        }

        private static List<LegacyBalloonRow> DeduplicateAcrossSheets(List<LegacyBalloonRow> rows)
        {
            // OperationLabel included in the key alongside (LegacyBalloonNumber,
            // Nominal) - without it, OP 10 item "2" and OP 20 item "2"
            // sharing a nominal by pure coincidence would wrongly look
            // like the same restated row instead of two different
            // physical features on two different operations. This also
            // means several genuinely-identical rows WITHIN one table
            // (the same OP+item measured/recorded more than once, e.g. a
            // sampled feature) collapse to one here too - not just the
            // "same table on two worksheet tabs" case this was originally
            // written for - which is fine: they'd all resolve to the same
            // conversion outcome anyway (see LegacyRenumberService.
            // BuildPlan's own belt-and-suspenders dedup for requests that
            // reach it despite not being collapsed here, e.g. a manual
            // Assign override producing the same effective request twice).
            HashSet<(string, double, string)> seen = new HashSet<(string, double, string)>();
            List<LegacyBalloonRow> deduplicated = new List<LegacyBalloonRow>();

            foreach (LegacyBalloonRow row in rows)
            {
                var key = (row.LegacyBalloonNumber, Math.Round(row.Nominal, 4), row.OperationLabel);

                if (seen.Add(key))
                    deduplicated.Add(row);
            }

            return deduplicated;
        }

        // One worksheet's column roles, established from whichever row
        // most recently looked like a header. Re-detected every time a
        // header-shaped row is seen, since the source template repeats its
        // header (once per printed page) rather than stating it once.
        private class ColumnMap
        {
            public int BalloonCol = -1;
            public int GenericIdCol = -1;
            public int OpItemCol = -1;
            public int NominalCol = -1;
            public int UpperCol = -1;
            public int LowerCol = -1;
            public int ClassCol = -1;
            public int MethodCol = -1;

            // OpItemCol is deliberately NOT one of the ID sources here - it
            // supplies OperationLabel/OperationSheetNumber only, never the
            // actual legacy balloon number (see OpItemTokens' remarks) - a
            // table still needs a real ID column (BalloonCol/GenericIdCol)
            // to be usable at all, exactly as if OpItemCol didn't exist.
            public bool IsUsable => (BalloonCol >= 0 || GenericIdCol >= 0) &&
                (NominalCol >= 0 || (UpperCol >= 0 && LowerCol >= 0));
        }

        private static List<LegacyBalloonRow> ReadSheet(ExcelWorksheet sheet)
        {
            List<LegacyBalloonRow> rows = new List<LegacyBalloonRow>();

            int lastRow = sheet.Dimension.End.Row;
            int lastCol = sheet.Dimension.End.Column;

            ColumnMap columns = null;

            for (int row = 1; row <= lastRow; row++)
            {
                ColumnMap candidateHeader = TryReadHeaderRow(sheet, row, lastCol);

                if (candidateHeader != null)
                {
                    columns = candidateHeader;
                    continue;
                }

                if (columns == null)
                    continue;

                LegacyBalloonRow parsed = TryReadDataRow(sheet, row, columns);

                if (parsed != null)
                    rows.Add(parsed);
            }

            return rows;
        }

        private static ColumnMap TryReadHeaderRow(ExcelWorksheet sheet, int row, int lastCol)
        {
            ColumnMap map = new ColumnMap();

            for (int col = 1; col <= lastCol; col++)
            {
                string header = GetCellText(sheet, row, col);

                if (string.IsNullOrWhiteSpace(header))
                    continue;

                string normalized = header.Trim().ToLowerInvariant();
                string[] tokens = Regex.Split(normalized, @"[^a-z0-9]+");

                if (map.BalloonCol < 0 && tokens.Any(t => BalloonIdTokens.Contains(t)))
                    map.BalloonCol = col;
                else if (map.GenericIdCol < 0 && tokens.Any(t => GenericIdTokens.Contains(t)))
                    map.GenericIdCol = col;
                else if (map.OpItemCol < 0 && tokens.Any(t => OpItemTokens.Contains(t)))
                    map.OpItemCol = col;

                if (map.NominalCol < 0 && tokens.Any(t => NominalTokens.Contains(t)))
                    map.NominalCol = col;

                if (map.UpperCol < 0 && normalized.Contains("upper"))
                    map.UpperCol = col;

                if (map.LowerCol < 0 && normalized.Contains("lower"))
                    map.LowerCol = col;

                if (map.ClassCol < 0 && tokens.Any(t => ClassTokens.Contains(t)))
                    map.ClassCol = col;

                if (map.MethodCol < 0 && tokens.Any(t => MethodTokens.Contains(t)))
                    map.MethodCol = col;
            }

            return map.IsUsable ? map : null;
        }

        private static LegacyBalloonRow TryReadDataRow(ExcelWorksheet sheet, int row, ColumnMap columns)
        {
            // The REAL legacy balloon number, ALWAYS from the ordinary ID
            // column (BalloonCol/GenericIdCol, e.g. "DIM #") - never from
            // OpItemCol, which supplies OperationLabel/OperationSheetNumber
            // separately below and NEVER a balloon number (see
            // OpItemTokens' remarks for the bug this fixed).
            string numberText = null;

            if (columns.BalloonCol >= 0)
                numberText = GetCellText(sheet, row, columns.BalloonCol)?.Trim();

            if (string.IsNullOrEmpty(numberText) && columns.GenericIdCol >= 0)
                numberText = GetCellText(sheet, row, columns.GenericIdCol)?.Trim();

            if (string.IsNullOrEmpty(numberText))
                return null;

            // Independent of numberText above - a stray note or blank cell
            // here just leaves OperationLabel/OperationSheetNumber null,
            // it never makes the row itself unusable (the real ID column
            // already resolved above is what decides that).
            string operationLabel = null;
            string operationSheetNumber = null;

            if (columns.OpItemCol >= 0)
            {
                string opCellText = GetCellText(sheet, row, columns.OpItemCol)?.Trim();

                TryParseOpItemCell(opCellText, out operationLabel, out operationSheetNumber);
            }

            double nominal;
            string rawNominalText;

            if (!TryGetNominal(sheet, row, columns, out nominal, out rawNominalText))
                return null;

            // Read independently of whichever branch above supplied
            // Nominal - unlike TryGetNominal's own Upper/Lower fallback
            // (only used when there's no usable Nominal column at all),
            // LegacyNumberMatcher wants the actual limit VALUES whenever
            // they're on the sheet, even for a row whose Nominal came from
            // its own dedicated column. See TryReadLimits' remarks.
            double? upperLimit, lowerLimit;

            TryReadLimits(sheet, row, columns, out upperLimit, out lowerLimit);

            return new LegacyBalloonRow
            {
                LegacyBalloonNumber = numberText,
                OperationLabel = operationLabel,
                OperationSheetNumber = operationSheetNumber,
                Nominal = nominal,
                RawNominalText = rawNominalText,
                UpperLimit = upperLimit,
                LowerLimit = lowerLimit,
                Class = columns.ClassCol >= 0
                    ? NullIfEmpty(GetCellText(sheet, row, columns.ClassCol))
                    : null,
                Method = columns.MethodCol >= 0
                    ? NullIfEmpty(GetCellText(sheet, row, columns.MethodCol))
                    : null,
            };
        }

        // Splits "OP 30 (3)" into operationLabel="OP 30" and
        // sheetNumber="3" - see OpItemTokens' remarks for why that second
        // value is a SHEET NUMBER, not a balloon number.
        private static bool TryParseOpItemCell(string text, out string operationLabel, out string sheetNumber)
        {
            operationLabel = null;
            sheetNumber = null;

            if (string.IsNullOrEmpty(text))
                return false;

            Match match = OpItemPattern.Match(text);

            if (!match.Success)
                return false;

            operationLabel = match.Groups[1].Value.Trim();
            sheetNumber = match.Groups[2].Value.Trim();

            return true;
        }

        // Prefers the Dimension/Nominal column's own value when present and
        // numeric - that's the actual designed value, and the one the user
        // wants identifying the balloon on the drawing. Falls back to the
        // midpoint of Upper/Lower Limit only when the Dimension column is
        // missing, or its free-text value doesn't parse cleanly (a real
        // legacy export's dimension column routinely carries thread
        // callouts, multi-line notes, or "THRU ALL"/"NEAR SIDE" suffixes -
        // the limit columns are a reliable fallback for those rows).
        private static bool TryGetNominal(
            ExcelWorksheet sheet,
            int row,
            ColumnMap columns,
            out double nominal,
            out string rawText)
        {
            nominal = 0;
            rawText = null;

            if (columns.NominalCol >= 0)
            {
                string nominalText = GetCellText(sheet, row, columns.NominalCol)?.Trim();

                if (TryParseNumber(nominalText, out nominal))
                {
                    rawText = nominalText;
                    return true;
                }
            }

            double? upper, lower;

            if (TryReadLimits(sheet, row, columns, out upper, out lower))
            {
                nominal = (upper.Value + lower.Value) / 2.0;
                rawText = upper.Value.ToString(CultureInfo.InvariantCulture) + " / " + lower.Value.ToString(CultureInfo.InvariantCulture);
                return true;
            }

            return false;
        }

        // Reads Upper/Lower Limit as plain numeric values, independent of
        // TryGetNominal's own use of them as a midpoint fallback - a
        // separate signal (the tolerance RANGE a legacy row implies) that
        // LegacyNumberMatcher scores on its own, alongside Nominal rather
        // than instead of it. Returns false (both out params null) if
        // either column is missing or doesn't parse - a row can still be
        // usable without this, it just won't have a tolerance-range signal
        // to match on.
        private static bool TryReadLimits(
            ExcelWorksheet sheet,
            int row,
            ColumnMap columns,
            out double? upperLimit,
            out double? lowerLimit)
        {
            upperLimit = null;
            lowerLimit = null;

            if (columns.UpperCol < 0 || columns.LowerCol < 0)
                return false;

            string upperText = GetCellText(sheet, row, columns.UpperCol)?.Trim();
            string lowerText = GetCellText(sheet, row, columns.LowerCol)?.Trim();

            double upper, lower;

            if (!TryParseNumber(upperText, out upper) || !TryParseNumber(lowerText, out lower))
                return false;

            upperLimit = upper;
            lowerLimit = lower;

            return true;
        }

        private static string NullIfEmpty(string text)
        {
            string trimmed = text?.Trim();

            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }

        // A leading instance-count callout ("4X", "8X", "3X ...") is
        // common GD&T shorthand for "this feature repeats N times" - NOT
        // part of the dimension value itself. Matched anywhere the digits
        // sit right before "X" at the very start of the cell text, since
        // that's the only place this shorthand actually appears in a real
        // legacy export.
        private static readonly Regex LeadingInstanceCountPattern = new Regex(@"^\s*\d+\s*[Xx]\s*");

        // Legacy sheets tend to store dimensions as text with units,
        // symbols, or trailing notes mixed in (e.g. "Ø.500", "12.5 mm",
        // "100.00 NEAR SIDE") rather than a clean number - strip everything
        // except digits, a leading minus, and the decimal point before
        // parsing. Anything that still doesn't parse cleanly (a thread
        // callout, "NA", "BASIC", multi-line notes) is treated as not a
        // usable number rather than guessed at.
        //
        // Bug found + fixed 2026-09-21 (user report: "it is missing manual
        // inputs on the spreadsheet like '4X R.06'" - confirmed via real
        // debug-log data showing "8X R.06" parsed as nominal=8.06 and
        // "4X .010 MIN" parsed as nominal=4.01, both wrong): the old
        // strip-everything-but-digits approach doesn't distinguish a
        // leading instance count from the actual value - it just
        // concatenates every digit run left in the string in order, so
        // the "8" from "8X" and the "06" from ".06" merged into "8.06"
        // instead of the real value, 0.06. Now the instance-count prefix
        // is stripped FIRST (LeadingInstanceCountPattern), before the
        // existing digit-only cleanup runs on what's left - "8X R.06" ->
        // "R.06" -> ".06" -> 0.06. A cell that's ONLY a count with no real
        // value after it (e.g. "4X BREAK SHARP EDGES") now correctly finds
        // no usable number at all instead of parsing the count itself as
        // if it were the nominal (previously misread as nominal=4).
        private static bool TryParseNumber(string text, out double value)
        {
            value = 0;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            // A genuine dimension value is never multiple lines - a
            // multi-line cell here is a note (e.g. a roughness
            // specification with several lines of digits) that would
            // otherwise concatenate into a plausible-looking but bogus
            // number.
            if (text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0)
                return false;

            string withoutInstanceCount = LeadingInstanceCountPattern.Replace(text, string.Empty);

            string cleaned = Regex.Replace(withoutInstanceCount, @"[^0-9.\-]", "");

            return !string.IsNullOrEmpty(cleaned) &&
                double.TryParse(
                    cleaned,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value);
        }
    }
}
