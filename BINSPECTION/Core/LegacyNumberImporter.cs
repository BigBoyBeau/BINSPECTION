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
        private static List<LegacyBalloonRow> DeduplicateAcrossSheets(List<LegacyBalloonRow> rows)
        {
            HashSet<(string, double)> seen = new HashSet<(string, double)>();
            List<LegacyBalloonRow> deduplicated = new List<LegacyBalloonRow>();

            foreach (LegacyBalloonRow row in rows)
            {
                var key = (row.LegacyNumber, Math.Round(row.Nominal, 4));

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
            public int NominalCol = -1;
            public int UpperCol = -1;
            public int LowerCol = -1;
            public int ClassCol = -1;
            public int MethodCol = -1;

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
                string header = sheet.Cells[row, col].Text;

                if (string.IsNullOrWhiteSpace(header))
                    continue;

                string normalized = header.Trim().ToLowerInvariant();
                string[] tokens = Regex.Split(normalized, @"[^a-z0-9]+");

                if (map.BalloonCol < 0 && tokens.Any(t => BalloonIdTokens.Contains(t)))
                    map.BalloonCol = col;
                else if (map.GenericIdCol < 0 && tokens.Any(t => GenericIdTokens.Contains(t)))
                    map.GenericIdCol = col;

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
            string numberText = null;

            if (columns.BalloonCol >= 0)
                numberText = sheet.Cells[row, columns.BalloonCol].Text?.Trim();

            if (string.IsNullOrEmpty(numberText) && columns.GenericIdCol >= 0)
                numberText = sheet.Cells[row, columns.GenericIdCol].Text?.Trim();

            if (string.IsNullOrEmpty(numberText))
                return null;

            double nominal;
            string rawNominalText;

            if (!TryGetNominal(sheet, row, columns, out nominal, out rawNominalText))
                return null;

            return new LegacyBalloonRow
            {
                LegacyNumber = numberText,
                Nominal = nominal,
                RawNominalText = rawNominalText,
                Class = columns.ClassCol >= 0
                    ? NullIfEmpty(sheet.Cells[row, columns.ClassCol].Text)
                    : null,
                Method = columns.MethodCol >= 0
                    ? NullIfEmpty(sheet.Cells[row, columns.MethodCol].Text)
                    : null,
            };
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
                string nominalText = sheet.Cells[row, columns.NominalCol].Text?.Trim();

                if (TryParseNumber(nominalText, out nominal))
                {
                    rawText = nominalText;
                    return true;
                }
            }

            if (columns.UpperCol >= 0 && columns.LowerCol >= 0)
            {
                string upperText = sheet.Cells[row, columns.UpperCol].Text?.Trim();
                string lowerText = sheet.Cells[row, columns.LowerCol].Text?.Trim();

                double upper, lower;

                if (TryParseNumber(upperText, out upper) && TryParseNumber(lowerText, out lower))
                {
                    nominal = (upper + lower) / 2.0;
                    rawText = upperText + " / " + lowerText;
                    return true;
                }
            }

            return false;
        }

        private static string NullIfEmpty(string text)
        {
            string trimmed = text?.Trim();

            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }

        // Legacy sheets tend to store dimensions as text with units,
        // symbols, or trailing notes mixed in (e.g. "Ø.500", "12.5 mm",
        // "100.00 NEAR SIDE") rather than a clean number - strip everything
        // except digits, a leading minus, and the decimal point before
        // parsing. Anything that still doesn't parse cleanly (a thread
        // callout, "NA", "BASIC", multi-line notes) is treated as not a
        // usable number rather than guessed at.
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

            string cleaned = Regex.Replace(text, @"[^0-9.\-]", "");

            return !string.IsNullOrEmpty(cleaned) &&
                double.TryParse(
                    cleaned,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value);
        }
    }
}
