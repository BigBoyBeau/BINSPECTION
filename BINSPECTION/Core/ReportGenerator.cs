using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BINSPECTION.Models;
using OfficeOpenXml;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace BINSPECTION.Core
{
    // Builds the "Inspection Report" workbook by filling in the company's
    // own controlled QMS form (the "5FM-R-08.5.1" template,
    // INSPECTION REPORTING TEMPLATE.xltx) rather than generating a sheet
    // from scratch. That template - normally filled by InspectionXpert - has
    // two sheets, "Sheet3" ("MASTER", every characteristic) and "Sheet4"
    // ("NON CMM", every characteristic except Method == "CMM" ones), each
    // with a data-row template at row 19 whose cells hold literal
    // "iex:INSPECTIONXPERT/..." placeholder strings (an XPath-shaped token
    // per column) instead of real values. This class discovers which
    // column holds which placeholder by reading row 19 itself - so it never
    // hardcodes a column letter - then writes real BINSPECTION data into
    // those columns for one row per characteristic, duplicating row 19's
    // formatting onto additional rows as needed. The same placeholder
    // convention is used for a handful of header-block cells (rows 1-18),
    // which get resolved once per sheet from live SolidWorks custom
    // properties.
    //
    // Reads FROM the characteristics list the caller already loaded from
    // the external .binspection.json file, not from whatever
    // CharacteristicManager happens to have in memory - same reasoning as
    // everywhere else in this add-in: the JSON file is the source of truth.
    public static class ReportGenerator
    {
        private const string CmmMethodName = "CMM";

        // The company's controlled inspection-record template. Both sheets
        // it ships with ("Sheet3"/MASTER, "Sheet4"/NON CMM) are filled in
        // place - see FillTemplateSheet.
        private const string TemplatePath =
            @"M:\SolidWorks Settings\Inspection Report Templates\INSPECTION REPORTING TEMPLATE.xltx";

        // Row 19 is the data-row template on both sheets (see SheetConfig.
        // DataStartRow in the legacy macro port this template also feeds -
        // InspectionProcessor.Core.Models.SheetConfig - same convention).
        private const int DataStartRow = 19;

        // Header-block placeholders only ever appear in rows 1-18 (above
        // the column-header row) on either sheet.
        private const int HeaderBlockLastRow = 18;

        // Major/Critical/100% highlight fill spans the same columns the
        // legacy macros highlighted (C:H, adapted here to the template's
        // own A:H real-data span).
        private const int HighlightLastColumn = 8;

        // Which real BINSPECTION value a data-row placeholder column maps
        // to. Discovered per sheet by reading row 19's own cell text - see
        // DiscoverRowFieldColumns - never assumed by column letter.
        private enum RowField
        {
            DrawingSheet,
            BalloonNumber,
            CharacteristicValue,
            Classification,
            UpperLimit,
            LowerLimit,
            InspectionMethod,
            Process,
        }

        // The handful of live SolidWorks values the header-block
        // placeholders resolve to. Some placeholders (Customer/VendorName,
        // "Ballooned by") have no BINSPECTION equivalent and are left blank
        // for manual fill-in rather than guessed - see
        // ResolveHeaderPlaceholderValue.
        private struct HeaderFieldValues
        {
            public string PartNumber;
            public string PartDescription;
            public string OpRev;
            public ReportHeaderSettings ManualFields;
        }

        // Cell addresses for the header fields that have no "iex:"
        // placeholder in the template at all - see
        // Models/ReportHeaderSettings.cs's remarks. Written directly by
        // address (unlike the placeholder-discovered fields) because
        // there's no placeholder text to search for; same address on both
        // Sheet3 and Sheet4, since each sheet carries its own independent
        // copy of the header block.
        private const string LotSizeCellAddress = "D8";
        private const string SampleSizeChartCellAddress = "D9";
        private const string SampleSizeCellAddress = "D10";
        private const string SampleFrequencyCellAddress = "D11";
        private const string OperatorEmployeeNumberCellAddress = "I10";
        private const string InspectorEmployeeNumberCellAddress = "I11";
        private const string CmmReportIdCellAddress = "I12";

        // Returns true and writes the workbook to outputPath on success.
        // Returns false (and logs why) rather than throwing, so a report
        // failure never takes down the whole "Generate Report" command.
        // headerSettings supplies the handful of header-block fields
        // BINSPECTION has no live SolidWorks source for (Customer, Report
        // Issued By, Lot Size, etc.) - edited via the Sheet Tolerance
        // Selection window and passed straight through from ProjectData.
        // Null is treated the same as an all-blank ReportHeaderSettings.
        // toleranceSets/sheetToleranceAssignments - the sheet-level default
        // tolerance rules (see Models/SheetToleranceSet.cs), passed straight
        // through from the loaded ProjectData so a dimension with no
        // tolerance of its own (and not Basic) can fall back to its sheet's
        // rule for the matching decimal-place count - see
        // ApplySheetToleranceFallback. Null/empty just skips the fallback
        // (old behavior: no tolerance stays no tolerance), same
        // graceful-degradation convention as every other optional input
        // here.
        public static bool BuildInspectionReport(
            ModelDoc2 model,
            List<Characteristic> characteristics,
            string outputPath,
            ReportHeaderSettings headerSettings = null,
            List<SheetToleranceSet> toleranceSets = null,
            Dictionary<string, string> sheetToleranceAssignments = null)
        {
            if (model == null || characteristics == null || string.IsNullOrEmpty(outputPath))
                return false;

            try
            {
                ExcelLicense.EnsureSet();

                FileInfo templateFile = new FileInfo(TemplatePath);

                if (!templateFile.Exists)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"ReportGenerator.BuildInspectionReport Error: template not found at \"{TemplatePath}\".");

                    return false;
                }

                FileInfo outputFile = new FileInfo(outputPath);

                // Unnumbered characteristics are intentionally excluded
                // from inspection (see
                // Core/BalloonGridService.MarkUnnumbered) - they never
                // show up on the sheet, so they never show up here
                // either.
                List<Characteristic> numbered = characteristics.FindAll(
                    c => !c.IsUnnumbered);

                List<Characteristic> cmmRows = numbered.FindAll(
                    c => string.Equals(c.Method, CmmMethodName, StringComparison.OrdinalIgnoreCase));

                List<Characteristic> nonCmmRows = numbered.FindAll(
                    c => !string.Equals(c.Method, CmmMethodName, StringComparison.OrdinalIgnoreCase));

                HeaderFieldValues headerValues = ResolveHeaderFieldValues(model, headerSettings ?? new ReportHeaderSettings());

                // ExcelPackage(newFile, template) overwrites newFile (if it
                // already exists) only when Save() is called - no need to
                // pre-delete it ourselves.
                using (ExcelPackage package = new ExcelPackage(outputFile, templateFile))
                {
                    FillTemplateSheet(
                        package.Workbook.Worksheets["Sheet3"], model, numbered, headerValues,
                        toleranceSets, sheetToleranceAssignments, cmmCountForI5: null);

                    FillTemplateSheet(
                        package.Workbook.Worksheets["Sheet4"], model, nonCmmRows, headerValues,
                        toleranceSets, sheetToleranceAssignments, cmmCountForI5: cmmRows.Count);

                    package.Save();
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ReportGenerator.BuildInspectionReport Error: {ex}");

                return false;
            }
        }

        // Fills one template sheet in place: resolves the header-block
        // placeholders, writes the literal CMM-excluded count into I5 when
        // this sheet excludes CMM rows (Sheet4/"NON CMM" only - Sheet3's I5
        // is already a formula pulling from Sheet4!I5 in the template, so
        // it's left untouched), then writes one row per characteristic
        // starting at DataStartRow - severity-sorted, highlighted, with a
        // page break between severity groups - duplicating row 19's own
        // formatting onto additional rows once the template's pre-formatted
        // rows run out.
        private static void FillTemplateSheet(
            ExcelWorksheet sheet,
            ModelDoc2 model,
            List<Characteristic> rows,
            HeaderFieldValues headerValues,
            List<SheetToleranceSet> toleranceSets,
            Dictionary<string, string> sheetToleranceAssignments,
            int? cmmCountForI5)
        {
            if (sheet == null)
                return;

            ReplaceHeaderPlaceholders(sheet, headerValues);
            WriteManualHeaderFields(sheet, headerValues.ManualFields);

            if (cmmCountForI5.HasValue)
                sheet.Cells["I5"].Value = cmmCountForI5.Value;

            int templateLastRow = sheet.Dimension != null ? sheet.Dimension.End.Row : DataStartRow;
            int lastColumn = sheet.Dimension != null ? sheet.Dimension.End.Column : HighlightLastColumn;

            Dictionary<int, RowField> fieldColumns = DiscoverRowFieldColumns(sheet, lastColumn);

            List<Characteristic> sorted = SortForReport(rows);

            // Every member of a group - anchor included - gets its own
            // decimal sub-number (the "DIM #" column) in the report; see
            // BuildReportNumbers' remarks.
            Dictionary<Characteristic, string> reportNumbers = BuildReportNumbers(sorted);

            int row = DataStartRow;
            int firstDataRow = row;
            int previousRank = 0;

            foreach (Characteristic characteristic in sorted)
            {
                if (row > templateLastRow)
                    sheet.InsertRow(row, 1, DataStartRow);

                double nominal;
                double plusTolerance;
                double minusTolerance;
                bool resolved;

                ReadDimensionValues(
                    model, characteristic, out nominal, out plusTolerance, out minusTolerance, out resolved);

                if (resolved)
                {
                    ApplySheetToleranceFallback(
                        model, characteristic, ref plusTolerance, ref minusTolerance,
                        toleranceSets, sheetToleranceAssignments);
                }

                object upperLimitValue;
                object lowerLimitValue;

                ResolveLimitCellValues(
                    characteristic, nominal, plusTolerance, minusTolerance,
                    out upperLimitValue, out lowerLimitValue);

                foreach (KeyValuePair<int, RowField> entry in fieldColumns)
                {
                    ExcelRange cell = sheet.Cells[row, entry.Key];

                    switch (entry.Value)
                    {
                        case RowField.DrawingSheet:
                            cell.Value = characteristic.SheetName;
                            break;
                        case RowField.BalloonNumber:
                            cell.Value = reportNumbers[characteristic];
                            break;
                        case RowField.CharacteristicValue:
                            WriteCharacteristicCell(model, cell, characteristic);
                            break;
                        case RowField.Classification:
                            cell.Value = characteristic.Class;
                            break;
                        case RowField.UpperLimit:
                            cell.Value = upperLimitValue;
                            break;
                        case RowField.LowerLimit:
                            cell.Value = lowerLimitValue;
                            break;
                        case RowField.InspectionMethod:
                            cell.Value = characteristic.Method;
                            break;
                        case RowField.Process:
                            // No BINSPECTION equivalent - clear the
                            // template's placeholder rather than leave raw
                            // "iex:..." text visible.
                            cell.Value = null;
                            break;
                    }
                }

                SeverityClassifier.ApplyRowFill(
                    sheet.Cells[row, 1, row, HighlightLastColumn].Style, characteristic.Class);

                // A severity-rank change starts a new printed group - push
                // it onto its own page (matching PageBreakService.
                // PushGroupOntoOwnPage's intent). ExcelRow.PageBreak
                // inserts the break AFTER the row it's set on, so the
                // previous row (the last row of the group that just ended)
                // is the one that gets it.
                int currentRank = SeverityClassifier.RankOf(characteristic.Class);

                if (row > firstDataRow && currentRank != previousRank)
                    sheet.Row(row - 1).PageBreak = true;

                previousRank = currentRank;

                row++;
            }

            int lastDataRow = row - 1;

            // Only grow the print area - never shrink it below the
            // template's own default, which intentionally ships a few
            // extra blank, already-formatted rows.
            if (lastDataRow >= firstDataRow && lastDataRow > templateLastRow)
                sheet.PrinterSettings.PrintArea = sheet.Cells[1, 1, lastDataRow, lastColumn];
        }

        // Reads row 19's own cell text on this sheet and records which
        // column holds which "iex:.../@xxx" data-row placeholder, so the
        // per-row writer above never has to assume a fixed column letter.
        private static Dictionary<int, RowField> DiscoverRowFieldColumns(ExcelWorksheet sheet, int lastColumn)
        {
            Dictionary<int, RowField> columns = new Dictionary<int, RowField>();

            for (int col = 1; col <= lastColumn; col++)
            {
                string text = sheet.Cells[DataStartRow, col].Text;

                if (string.IsNullOrEmpty(text) || !text.StartsWith("iex:", StringComparison.Ordinal))
                    continue;

                RowField? field = MatchRowField(text);

                if (field.HasValue)
                    columns[col] = field.Value;
            }

            return columns;
        }

        private static RowField? MatchRowField(string placeholderText)
        {
            if (placeholderText.EndsWith("/@drawingSheet", StringComparison.Ordinal))
                return RowField.DrawingSheet;

            if (placeholderText.EndsWith("/@prefix", StringComparison.Ordinal))
                return RowField.BalloonNumber;

            if (placeholderText.EndsWith("/@dim_upper_limit", StringComparison.Ordinal))
                return RowField.UpperLimit;

            if (placeholderText.EndsWith("/@dim_lower_limit", StringComparison.Ordinal))
                return RowField.LowerLimit;

            if (placeholderText.EndsWith("/@inspectionMethod", StringComparison.Ordinal))
                return RowField.InspectionMethod;

            if (placeholderText.EndsWith("/@classification", StringComparison.Ordinal))
                return RowField.Classification;

            if (placeholderText.EndsWith("/@process", StringComparison.Ordinal))
                return RowField.Process;

            if (placeholderText.EndsWith("/@value", StringComparison.Ordinal))
                return RowField.CharacteristicValue;

            return null;
        }

        // Scans the header block (rows 1-18) for "iex:..." placeholder
        // cells and replaces each with its resolved value (or blanks it,
        // for a placeholder with no BINSPECTION equivalent) - see
        // ResolveHeaderPlaceholderValue. Column-agnostic for the same
        // reason DiscoverRowFieldColumns is: the template reuses some
        // placeholders in more than one cell (e.g. PartNumber appears
        // twice), and this replaces every occurrence found rather than
        // hardcoding addresses.
        private static void ReplaceHeaderPlaceholders(ExcelWorksheet sheet, HeaderFieldValues headerValues)
        {
            int lastColumn = sheet.Dimension != null ? sheet.Dimension.End.Column : HighlightLastColumn;

            for (int rowNum = 1; rowNum <= HeaderBlockLastRow; rowNum++)
            {
                for (int col = 1; col <= lastColumn; col++)
                {
                    ExcelRange cell = sheet.Cells[rowNum, col];
                    string text = cell.Text;

                    if (string.IsNullOrEmpty(text) || !text.StartsWith("iex:", StringComparison.Ordinal))
                        continue;

                    cell.Value = ResolveHeaderPlaceholderValue(text, headerValues);
                }
            }
        }

        private static string ResolveHeaderPlaceholderValue(string placeholderText, HeaderFieldValues headerValues)
        {
            if (placeholderText.Contains("[@Name='PartNumber']"))
                return headerValues.PartNumber;

            if (placeholderText.Contains("[@Name='PartName']"))
                return headerValues.PartDescription;

            if (placeholderText.Contains("[@Name='DocumentNumber']"))
                return headerValues.PartNumber;

            if (placeholderText.Contains("[@Name='PartRevision']"))
                return headerValues.OpRev;

            // "Customer" (VENDOR/@VendorName) and "Ballooned by" - no
            // SolidWorks custom property backs either, so both come from
            // the manual ReportHeaderSettings edited via the Sheet
            // Tolerance Selection window instead.
            if (placeholderText.Contains("/VENDOR/@VendorName"))
                return headerValues.ManualFields?.Customer;

            if (placeholderText.Contains("[@propName='Ballooned by']"))
                return headerValues.ManualFields?.ReportIssuedBy;

            return null;
        }

        // Writes the header fields that have no "iex:" placeholder at all
        // in the template (see the *CellAddress constants above) - these
        // are genuinely blank input cells in the blank template, not
        // something the placeholder-scan in ReplaceHeaderPlaceholders can
        // find. A null/blank ReportHeaderSettings value just clears
        // whatever the template shipped in that cell (nothing, normally).
        private static void WriteManualHeaderFields(ExcelWorksheet sheet, ReportHeaderSettings manualFields)
        {
            if (manualFields == null)
                return;

            sheet.Cells[LotSizeCellAddress].Value = manualFields.LotSize;
            sheet.Cells[SampleSizeChartCellAddress].Value = manualFields.SampleSizeChart;
            sheet.Cells[SampleSizeCellAddress].Value = manualFields.SampleSize;
            sheet.Cells[SampleFrequencyCellAddress].Value = manualFields.SampleFrequency;
            sheet.Cells[OperatorEmployeeNumberCellAddress].Value = manualFields.OperatorEmployeeNumber;
            sheet.Cells[InspectorEmployeeNumberCellAddress].Value = manualFields.InspectorEmployeeNumber;
            sheet.Cells[CmmReportIdCellAddress].Value = manualFields.CmmReportId;
        }

        private static HeaderFieldValues ResolveHeaderFieldValues(ModelDoc2 model, ReportHeaderSettings manualFields)
        {
            ModelDoc2 partModel;
            string partConfigName;

            TryResolveMainPart(model, out partModel, out partConfigName);

            return new HeaderFieldValues
            {
                PartNumber = partModel != null ? GetCustomProperty(partModel, partConfigName, "PMSI DWG NUM") : null,
                PartDescription = partModel != null ? GetCustomProperty(partModel, partConfigName, "PART DESC") : null,
                OpRev = partModel != null ? GetCustomProperty(partModel, partConfigName, "OP REV") : null,
                ManualFields = manualFields,
            };
        }

        // Sorts by severity first (Characteristic.Class, via
        // SeverityClassifier.RankOf - lower rank prints first, e.g.
        // Critical before Minor - matching the template's own baked-in
        // Excel custom sort list "Critical,Major,Minor,Incidental"), then
        // by the same Number/SubNumber order the report has always used,
        // as a tie-break within a severity group. Fully orders every pair
        // (rank, then Number, then SubNumber), so List<T>.Sort's lack of a
        // stability guarantee doesn't matter here.
        private static List<Characteristic> SortForReport(List<Characteristic> characteristics)
        {
            List<Characteristic> sorted = new List<Characteristic>(characteristics);

            sorted.Sort((a, b) =>
            {
                int severityCompare =
                    SeverityClassifier.RankOf(a.Class).CompareTo(SeverityClassifier.RankOf(b.Class));

                if (severityCompare != 0)
                    return severityCompare;

                int numberCompare = a.Number.CompareTo(b.Number);

                return numberCompare != 0
                    ? numberCompare
                    : (a.SubNumber ?? 0).CompareTo(b.SubNumber ?? 0);
            });

            return sorted;
        }

        // Decides what goes in the Upper Limit/Lower Limit cells, in
        // priority order:
        //   1. A GD&T frame with a captured tolerance value
        //      (GdtToleranceValue, read live off the frame itself - see
        //      GtolTextFormatter.GdtFrameResult.ToleranceValue) - Upper =
        //      that value, Lower = 0. A feature-control frame's tolerance
        //      is a one-sided zone, not a +/- range around Nominal, so it
        //      is never combined with the dimension read below.
        //   2. Otherwise, Nominal + Plus/Minus Tolerance - by the time this
        //      is called, plusTolerance/minusTolerance already reflect
        //      ApplySheetToleranceFallback's sheet-default substitution
        //      when the dimension itself carried no real tolerance value,
        //      so a Basic dimension with no real tolerance (a common case -
        //      see BalloonGridService.SetBasic's remarks: switching a
        //      dimension to Basic never touches its underlying min/max
        //      values) still gets real numeric limits from its sheet's
        //      rule, per an explicit user request, rather than the literal
        //      "BASIC" text this used to show here - the Dimension/
        //      Characteristic cell's own text still says "BASIC" (see
        //      WriteCharacteristicCell), only these two numeric columns
        //      changed.
        private static void ResolveLimitCellValues(
            Characteristic characteristic,
            double nominal,
            double plusTolerance,
            double minusTolerance,
            out object upperLimitValue,
            out object lowerLimitValue)
        {
            if (characteristic.GdtToleranceValue.HasValue)
            {
                upperLimitValue = characteristic.GdtToleranceValue.Value;
                lowerLimitValue = 0d;
                return;
            }

            upperLimitValue = nominal + plusTolerance;
            lowerLimitValue = nominal + minusTolerance;
        }

        // Builds the "DIM #" text for every row in the report. This is
        // deliberately NOT the same as Characteristic.DisplayNumber: on the
        // drawing, a group's first/anchor member is still stamped with the
        // bare whole number (e.g. "6") and only later members show a
        // decimal sub-number ("6.1", "6.2") - see BalloonGridService.
        // GroupBalloons. That reads fine on the drawing, but in the report
        // it makes the anchor row look like it isn't really part of the
        // group. So here, every member of a group - anchor included - gets
        // its own decimal sub-number starting at ".1"; a standalone
        // characteristic (the only member of its Number) still shows the
        // plain whole number. The balloon's own on-drawing text is
        // untouched by any of this.
        private static Dictionary<Characteristic, string> BuildReportNumbers(
            List<Characteristic> characteristics)
        {
            Dictionary<Characteristic, string> reportNumbers =
                new Dictionary<Characteristic, string>();

            foreach (IGrouping<int, Characteristic> group in
                characteristics.GroupBy(c => c.Number))
            {
                List<Characteristic> members = group
                    .OrderBy(c => c.SubNumber ?? 0)
                    .ToList();

                if (members.Count == 1)
                {
                    reportNumbers[members[0]] = members[0].Number.ToString();
                    continue;
                }

                for (int i = 0; i < members.Count; i++)
                    reportNumbers[members[i]] = members[i].Number + "." + (i + 1);
            }

            return reportNumbers;
        }

        // Writes the Characteristic ("DIMENSION") cell. Three cases, in
        // priority order:
        //
        // 1. characteristic.GdtBoxText is set (a GD&T frame read straight
        //    from a live Gtol - see GtolTextFormatter.BuildBoxText) - write
        //    it as one rich-text run in the real "SolidWorks GDT" font. That
        //    font's own glyphs already draw the compartment box and
        //    dividers, so no separate Excel cell border is needed here -
        //    the box IS the text.
        // 2. Otherwise, fall back to GdtSymbolFontTranslator's best-effort
        //    phrase translation of the plain DimensionName text (covers
        //    manually-typed GD&T text, legacy-format frames, and old data
        //    saved before GdtBoxText existed). If it recognizes a symbol,
        //    add a plain Excel cell border around the whole cell so the
        //    callout at least reads as "boxed", even without real
        //    per-compartment dividers.
        // 3. Nothing recognized at all - plain string value, no rich text,
        //    no border, so an ordinary dimension's characteristic text
        //    never pays for any of this.
        // Best-effort: for a plain (non-GD&T/non-hole-callout/non-note/non-
        // surface-finish) dimension, ALWAYS re-resolves the live dimension
        // and recomputes BalloonGridService.GetDimensionDisplay's text
        // fresh, live, at report-generation time - never trusts whatever
        // happens to be baked into the persisted Characteristic.DimensionName
        // label. This is deliberate, not merely a "looks stale" heuristic:
        // that persisted label has changed shape more than once (the raw
        // SolidWorks name "RD2@DrawingView3", then briefly a tolerance-
        // decorated "0.14 +0.0003/+0" before tolerance was removed from
        // GetDimensionDisplay entirely - see [[binspection_report_export]]
        // conversation history), and a report generated against an older
        // drawing's already-persisted data should never show any of those
        // stale shapes. Falls back to the persisted text only if the live
        // dimension can no longer be resolved at all (deleted/moved) - a
        // stale label beats a blank cell - or for every other characteristic
        // kind (GD&T/note/surface-finish/hole-callout siblings), whose
        // PersistentRefId never resolves to a plain IDisplayDimension in the
        // first place, so this is a no-op for them.
        private static string ResolveCharacteristicDisplayText(ModelDoc2 model, Characteristic characteristic)
        {
            string text = characteristic.DimensionName;

            if (model == null || string.IsNullOrEmpty(characteristic.PersistentRefId))
                return text;

            try
            {
                IDisplayDimension displayDimension =
                    PersistentReferenceHelper.ResolveDimension(model, characteristic.PersistentRefId);

                Dimension swDim =
                    displayDimension?.GetDimension2(0) as Dimension;

                if (displayDimension == null || swDim == null)
                    return text;

                return BalloonGridService.GetDimensionDisplay(
                    model, (DisplayDimension)displayDimension, swDim, characteristic.IsBasic);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ReportGenerator.ResolveCharacteristicDisplayText Error: {ex}");

                return text;
            }
        }

        private static void WriteCharacteristicCell(ModelDoc2 model, ExcelRange cell, Characteristic characteristic)
        {
            string text = ResolveCharacteristicDisplayText(model, characteristic);

            if (!string.IsNullOrEmpty(characteristic.GdtBoxText))
            {
                OfficeOpenXml.Style.ExcelRichText boxRun = cell.RichText.Add(characteristic.GdtBoxText);
                boxRun.FontName = GdtSymbolFontTranslator.FontFamilyName;
                return;
            }

            IReadOnlyList<GdtTextRun> runs = GdtSymbolFontTranslator.Translate(text);

            bool hasSymbol = false;

            foreach (GdtTextRun run in runs)
            {
                if (run.IsSymbol)
                {
                    hasSymbol = true;
                    break;
                }
            }

            if (!hasSymbol)
            {
                cell.Value = text;
                return;
            }

            foreach (GdtTextRun run in runs)
            {
                OfficeOpenXml.Style.ExcelRichText richTextRun =
                    cell.RichText.Add(run.Text);

                if (run.IsSymbol)
                    richTextRun.FontName = GdtSymbolFontTranslator.FontFamilyName;
            }

            cell.Style.Border.BorderAround(OfficeOpenXml.Style.ExcelBorderStyle.Thin);
        }

        // Best-effort: finds the part shown in this drawing and the
        // configuration the drawing actually displays, by walking the
        // drawing's views for the first one with a resolvable
        // ReferencedDocument. BINSPECTION only ever deals with single-part
        // drawings, so the first hit is treated as "the" part.
        private static bool TryResolveMainPart(
            ModelDoc2 drawingModel,
            out ModelDoc2 partModel,
            out string configName)
        {
            partModel = null;
            configName = null;

            try
            {
                DrawingDoc drawing = drawingModel as DrawingDoc;

                if (drawing == null)
                    return false;

                View view = (View)drawing.GetFirstView();

                while (view != null)
                {
                    ModelDoc2 referenced = view.ReferencedDocument;

                    if (referenced != null)
                    {
                        partModel = referenced;
                        configName = view.ReferencedConfiguration;
                        return true;
                    }

                    view = (View)view.GetNextView();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ReportGenerator.TryResolveMainPart Error: {ex}");
            }

            return false;
        }

        // Best-effort: reads a single custom property's resolved value
        // (falling back to its raw value) from the given document/
        // configuration. Returns null rather than throwing if the property
        // manager or property can't be read.
        private static string GetCustomProperty(
            ModelDoc2 doc,
            string configName,
            string propertyName)
        {
            try
            {
                ICustomPropertyManager manager =
                    doc.Extension.CustomPropertyManager[configName ?? ""];

                string valOut;
                string resolvedValOut;
                bool wasResolved;
                bool linkToProperty;

                manager.Get6(
                    propertyName,
                    false,
                    out valOut,
                    out resolvedValOut,
                    out wasResolved,
                    out linkToProperty);

                return !string.IsNullOrEmpty(resolvedValOut) ? resolvedValOut : valOut;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ReportGenerator.GetCustomProperty Error ({propertyName}): {ex}");

                return null;
            }
        }

        // Best-effort: resolves the characteristic's dimension via its
        // saved persistent reference and reads its current nominal value
        // (in the drawing's own display units) and tolerance. Leaves
        // everything at 0 if the dimension can't be resolved or any read
        // fails - a report with a blank value for one row beats a report
        // that fails to generate at all.
        //
        // Internal (not private) so CommandManagerHandler (the "Edit All
        // Attributes" bulk editor's read-only Dimension column, and
        // LegacyNumberMatcher's nominal-value comparison) can reuse the
        // exact same value-reading logic.
        internal static void ReadDimensionValues(
            ModelDoc2 model,
            Characteristic characteristic,
            out double nominal,
            out double plusTolerance,
            out double minusTolerance,
            out bool resolved)
        {
            nominal = 0;
            plusTolerance = 0;
            minusTolerance = 0;
            resolved = false;

            IDisplayDimension displayDimension =
                PersistentReferenceHelper.ResolveDimension(
                    model,
                    characteristic.PersistentRefId);

            if (displayDimension == null)
                return;

            ReadDimensionValues(model, displayDimension, out nominal, out plusTolerance, out minusTolerance, out resolved);
        }

        // Same as above, for a caller (e.g. Core/BalloonGridService.cs) that
        // already holds a live IDisplayDimension directly - typically
        // straight from DimensionScanner - and so has no need to re-resolve
        // one from a Characteristic's PersistentRefId first.
        internal static void ReadDimensionValues(
            ModelDoc2 model,
            IDisplayDimension displayDimension,
            out double nominal,
            out double plusTolerance,
            out double minusTolerance,
            out bool resolved)
        {
            nominal = 0;
            plusTolerance = 0;
            minusTolerance = 0;
            resolved = false;

            try
            {
                if (displayDimension == null)
                    return;

                IDimension swDim = displayDimension.GetDimension2(0);

                if (swDim == null)
                    return;

                // In the drawing's own display units (in/mm/etc.) - this is
                // what should actually show up in the report, not raw
                // system units (always meters).
                nominal = swDim.GetUserValueIn(model);

                resolved = true;

                object systemValueObj = swDim.GetSystemValue3(
                    (int)swInConfigurationOpts_e.swThisConfiguration,
                    null);

                double[] systemValues = systemValueObj as double[];

                double nominalMeters =
                    (systemValues != null && systemValues.Length > 0)
                        ? systemValues[0]
                        : 0;

                // IDimensionTolerance's min/max always come back in
                // meters, regardless of the document's display units.
                // Reusing the ratio between the system-unit and
                // display-unit nominal already read above as a linear
                // scale factor avoids re-implementing SolidWorks' own
                // unit-conversion logic here.
                double unitScale =
                    (nominalMeters != 0) ? nominal / nominalMeters : 1.0;

                IDimensionTolerance tolerance = swDim.Tolerance;

                if (tolerance != null)
                {
                    plusTolerance = tolerance.GetMaxValue() * unitScale;
                    minusTolerance = tolerance.GetMinValue() * unitScale;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ReportGenerator.ReadDimensionValues Error: {ex}");
            }
        }

        // Best-effort: when a dimension has no real tolerance of its own
        // (both plus/minus read as 0 - including a Basic dimension, which
        // this deliberately does NOT skip: switching a dimension to Basic
        // never touches its underlying min/max tolerance values, per an
        // explicit user request that a Basic dimension with nothing real
        // behind it should still get a usable numeric limit rather than no
        // tolerance information at all), falls back to its sheet's own
        // default tolerance rule for the SAME decimal-place count the
        // dimension is actually displayed with - e.g. a dimension shown as
        // "1.00" (2 decimal places) picks up the sheet's 2-decimal-place
        // tolerance entry, not its 3-decimal one. This is a per-project
        // user-configured rule (Models/SheetToleranceSet.cs, edited via the
        // Sheet Tolerance Selection window), not anything SolidWorks itself
        // applies - a dimension that already carries its own real
        // tolerance is never touched here. No-ops (leaves plus/
        // minusTolerance at 0) if the characteristic has no assigned sheet/
        // tolerance set, the set has no entry for this decimal-place/
        // angular combination, or the live dimension can't be re-resolved
        // to read its displayed precision.
        private static void ApplySheetToleranceFallback(
            ModelDoc2 model,
            Characteristic characteristic,
            ref double plusTolerance,
            ref double minusTolerance,
            List<SheetToleranceSet> toleranceSets,
            Dictionary<string, string> sheetToleranceAssignments)
        {
            if (plusTolerance != 0 || minusTolerance != 0)
                return;

            if (toleranceSets == null || toleranceSets.Count == 0 ||
                sheetToleranceAssignments == null || string.IsNullOrEmpty(characteristic.SheetName))
                return;

            string setName;

            if (!sheetToleranceAssignments.TryGetValue(characteristic.SheetName, out setName) ||
                string.IsNullOrEmpty(setName))
                return;

            SheetToleranceSet toleranceSet = toleranceSets.FirstOrDefault(
                s => string.Equals(s.Name, setName, StringComparison.OrdinalIgnoreCase));

            if (toleranceSet == null)
                return;

            int decimalPlaces;
            bool isAngular;

            if (!TryGetDimensionFormat(model, characteristic, out decimalPlaces, out isAngular))
                return;

            ToleranceEntry entry = toleranceSet.Entries.FirstOrDefault(
                e => e.DecimalPlaces == decimalPlaces && e.IsAngular == isAngular);

            if (entry == null)
                return;

            plusTolerance = entry.Value;
            minusTolerance = -entry.Value;
        }

        // Best-effort: re-resolves the live dimension and reads how many
        // decimal places it's actually displayed with (IDisplayDimension.
        // GetPrimaryPrecision2 - confirmed via reflection on the installed
        // interop assembly to return the decimal-place count directly for a
        // decimal-format dimension) and whether it's an angular dimension
        // (swDimensionType_e.swAngularDimension/swAngularOrdinateDimension)
        // rather than linear - both needed to pick the matching
        // ToleranceEntry. Returns false if the dimension can't be
        // re-resolved or either read fails.
        private static bool TryGetDimensionFormat(
            ModelDoc2 model,
            Characteristic characteristic,
            out int decimalPlaces,
            out bool isAngular)
        {
            decimalPlaces = 0;
            isAngular = false;

            try
            {
                IDisplayDimension displayDimension =
                    PersistentReferenceHelper.ResolveDimension(model, characteristic.PersistentRefId);

                if (displayDimension == null)
                    return false;

                decimalPlaces = displayDimension.GetPrimaryPrecision2();

                swDimensionType_e type = (swDimensionType_e)displayDimension.Type2;

                isAngular =
                    type == swDimensionType_e.swAngularDimension ||
                    type == swDimensionType_e.swAngularOrdinateDimension;

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ReportGenerator.TryGetDimensionFormat Error: {ex}");

                return false;
            }
        }

    }
}
