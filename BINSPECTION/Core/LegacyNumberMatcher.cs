using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BINSPECTION.Models;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace BINSPECTION.Core
{
    // Scores each row of the uploaded legacy-system reference spreadsheet
    // (see LegacyNumberImporter) against every live, numbered characteristic
    // on this drawing, and ranks the candidates best-first - the basis the
    // user asked for, since legacy balloon numbers themselves have no
    // relationship to Binspection's own numbering.
    //
    // Originally this matched on nominal dimension value alone, with a
    // tight admission band (0.01) acting as a hard yes/no gate - exactly
    // one candidate in band auto-applied with no review, more than one was
    // an unresolved "Ambiguous" the user had to pick by hand. Reworked
    // (2026-09-18) into a weighted multi-signal score - nominal closeness,
    // tolerance-range closeness, dimension-family agreement, and Class/
    // Method agreement - because the old single-signal gate treated a
    // razor-close nominal match and a coincidental one identically, and
    // discarded two columns (Upper/Lower Limit, Class/Method) the
    // spreadsheet already provides. EVERY row now gets a ranked candidate
    // list (LegacyMatchEntry.Candidates), even a "clean" single-candidate
    // one - LegacyConversionWindow shows every row's top suggestion for the
    // user to confirm or override, rather than silently auto-applying the
    // ones that used to look unambiguous.
    //
    // A legacy spreadsheet can have a separate column whose own cell text
    // combines an operation label with a sheet-within-operation number
    // ("OP 30 (3)" - see LegacyNumberImporter.OpItemCol; NOT the legacy
    // balloon number itself, that's a different column entirely - see
    // LegacyBalloonRow.LegacyBalloonNumber's remarks) - such a spreadsheet
    // is really multiple independent numbering schemes, one per operation,
    // sharing one file, each operation's items measured on its own
    // SolidWorks sheet (this shop names a drawing's sheets after the
    // operation they document, e.g. "OP30").
    //
    // 2026-09-21 history on this specific signal (kept because it explains
    // why the current implementation looks the way it does):
    //   1. Original attempt hard-filtered by fuzzy-string-matching
    //      OperationLabel against Characteristic.SheetName directly -
    //      removed per user feedback that it "is not working 100%".
    //   2. Scoping was moved to the caller as a user-selectable dropdown
    //      (one Operation filtered at a time) - disabled shortly after
    //      because it broke matching for every row (suspected WPF
    //      ComboBox/TwoWay-binding issue, never confirmed).
    //   3. With NO operation scoping at all, real data showed the actual
    //      cost of skipping this signal: a single live characteristic
    //      with a common nominal got claimed by 45 unrelated legacy rows
    //      from other operations, because nothing stopped a legacy row
    //      from OP10 being scored against a candidate that's only ever
    //      lived on OP30's sheet. Confirmed by the user this is the
    //      actual bug to fix, and that this shop's SolidWorks sheets ARE
    //      named after their operation (e.g. a sheet literally named
    //      "OP30" corresponds to legacy rows whose OperationLabel is
    //      "OP 30").
    // This version restores the hard filter, but keyed off a normalized
    // "OP<digits>" comparison (NormalizeOperationKey) instead of a fuzzy
    // string match - precise rather than approximate, so "OP 30", "OP-30",
    // and "OP30 - MILL" all collapse to the same key and compare exactly.
    // A row or candidate whose text doesn't contain a recognizable
    // "OP<digits>" pattern is left unscoped (not excluded) rather than
    // guessed at - see NormalizeOperationKey's remarks.
    public static class LegacyNumberMatcher
    {
        // How close a candidate's nominal can be to the legacy row's own
        // (as a fraction of the legacy nominal's own magnitude) while
        // still being considered a match at all - opened from an earlier
        // exact-only requirement to 10% per explicit user request ("open
        // the range for the nominal to be acceptable within 10% of
        // accuracy"). See Match() for how this combines with
        // MinimumNominalToleranceBand for a near-zero legacy nominal.
        public const double NominalTolerancePercent = 0.10;

        // A pure 10%-of-magnitude band would shrink to zero for a legacy
        // nominal that's itself zero (or very small), making an otherwise
        // reasonable close match impossible to admit at all - this floor
        // keeps the band from collapsing below a sensible minimum
        // (roughly half the smallest increment these dimensions are
        // practically read to). Whichever of the two is larger wins - see
        // Match()'s ToleranceBand calculation.
        public const double MinimumNominalToleranceBand = 0.0005;

        // Score weights, out of 100 - see ScoreCandidate's remarks for how
        // a signal that has no data on one side (e.g. no Class column on
        // this spreadsheet) is left OUT of both the numerator and
        // denominator rather than counted as a mismatch.
        private const double NominalWeight = 55.0;
        private const double ToleranceWeight = 20.0;
        // private const double FamilyWeight = 15.0;
        private const double ClassMethodWeight = 10.0;

        // Matches "OP" (any case) followed by optional space/dash then
        // digits, ANYWHERE in the text - so "OP 30", "OP-30", "OP30", and
        // a longer sheet name like "OP30 - MILL SETUP" all extract the
        // same digits. Only the first match is used.
        private static readonly Regex OperationKeyPattern = new Regex(@"OP\s*-?\s*(\d+)", RegexOptions.IgnoreCase);

        // Reduces an operation label (legacy row) or a SolidWorks sheet
        // name (live candidate) down to a canonical "OP<digits>" key, e.g.
        // "OP 30", "OP-30", and "OP30 - MILL" all become "OP30". Returns
        // null when no "OP<digits>" pattern is found at all - a legacy row
        // with no OperationLabel, or a candidate on a sheet not named
        // after an operation (e.g. "Sheet1"), is left UNSCOPED rather than
        // guessed at, since there's nothing reliable to compare. See the
        // class remarks for why an exact digit-key comparison replaced the
        // earlier fuzzy string match.
        private static string NormalizeOperationKey(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            Match match = OperationKeyPattern.Match(text);

            return match.Success ? "OP" + match.Groups[1].Value : null;
        }

        // Matches a parenthesized number anywhere in the text, e.g. the
        // "(4)" in a SolidWorks sheet literally named "OP 30 (4)" - the
        // SAME sheet-within-operation number LegacyNumberImporter already
        // parses off the legacy spreadsheet's OP column as
        // LegacyBalloonRow.OperationSheetNumber (see its remarks). This
        // shop's real spreadsheet has ONE OperationLabel ("OP 30") spread
        // across SEVERAL pages/sheets (sheet 2, 3, 4, 5, ...) of that same
        // operation's paperwork - NormalizeOperationKey alone collapses
        // ALL of them to the identical "OP30" key, which is exactly why
        // the operation-only scope filter (added earlier the same day)
        // let every one of a 159-row workbook's rows through against an
        // 18-candidate single-sheet pool: confirmed live via a real debug
        // log where literally every row showed "key=OP30, 0 excluded"
        // regardless of its own "(sheet 2)"/"(sheet 4)"/etc. label, and
        // the user independently confirmed only balloons #141-169 (all
        // "(sheet 4)") actually belong to the sheet being converted. This
        // page-level key closes that gap - see its use in Match().
        private static readonly Regex SheetPageKeyPattern = new Regex(@"\((\d+)\)");

        private static string NormalizePageKey(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            Match match = SheetPageKeyPattern.Match(text);

            return match.Success ? match.Groups[1].Value : null;
        }

        // A coarse grouping of swDimensionType_e - deliberately NOT a
        // diameter/radius split. Nothing else in this codebase currently
        // distinguishes a diameter dimension from a plain linear one via
        // Type2 (see BalloonPlacementService.ShouldShiftLeft and
        // ReportGenerator.TryGetDimensionFormat, the only other Type2
        // consumers - neither splits Radial), and guessing at an unproven
        // SolidWorks API call here would break this project's own
        // practice of only trusting SW API behavior once confirmed live
        // (see [[binspection_create_balloons_multi_sheet_silent_abort]],
        // [[binspection_reference_dimension_filter]]). If a real diameter/
        // radius signal turns out to matter, it needs its own live-data
        // confirmation pass before landing here.
        private enum DimensionFamily
        {
            Unknown,
            Linear,
            Angular,
            Ordinate,
            Chamfer,
        }

        // Per-candidate data resolved once up front (one COM round-trip
        // per characteristic, not per legacy row) - nominal/tolerance via
        // the same ReportGenerator helper Create Balloons' report uses,
        // plus the live dimension's own Type2 for the family signal.
        private class CandidateInfo
        {
            public double Nominal;
            public double PlusTolerance;
            public double MinusTolerance;
            public DimensionFamily Family;
        }

        // debugLog (optional): when non-null, every resolution failure and
        // every legacy row's full candidate breakdown (in-band AND the
        // nearest out-of-band near-misses) is appended as plain text -
        // see LegacyConversionWindow.LoadSpreadsheet_Click, which writes
        // this to a file and opens it so a "why didn't this match"
        // question can be answered by reading it directly instead of
        // attaching a debugger to the live SolidWorks-hosted add-in.
        public static LegacyMatchResult Match(
            ModelDoc2 model,
            List<Characteristic> characteristics,
            List<LegacyBalloonRow> legacyRows,
            List<string> debugLog = null)
        {
            LegacyMatchResult result = new LegacyMatchResult();

            if (model == null || characteristics == null || legacyRows == null)
                return result;

            List<Characteristic> candidates = characteristics
                .Where(c => !c.IsUnnumbered)
                .ToList();

            Dictionary<Characteristic, CandidateInfo> infoByCharacteristic =
                new Dictionary<Characteristic, CandidateInfo>();

            // One operation key AND one page-within-operation key per
            // candidate, computed once up front from its own SheetName -
            // see NormalizeOperationKey/NormalizePageKey's remarks.
            Dictionary<Characteristic, string> operationKeyByCharacteristic =
                new Dictionary<Characteristic, string>();

            Dictionary<Characteristic, string> pageKeyByCharacteristic =
                new Dictionary<Characteristic, string>();

            List<string> resolutionFailures = debugLog != null ? new List<string>() : null;

            foreach (Characteristic characteristic in candidates)
            {
                string failureReason;
                CandidateInfo info = ResolveCandidateInfo(model, characteristic, out failureReason);

                if (info != null)
                {
                    infoByCharacteristic[characteristic] = info;
                    operationKeyByCharacteristic[characteristic] = NormalizeOperationKey(characteristic.SheetName);
                    pageKeyByCharacteristic[characteristic] = NormalizePageKey(characteristic.SheetName);
                }
                else if (resolutionFailures != null)
                {
                    resolutionFailures.Add(
                        "  (" + characteristic.DisplayNumber + ")  " +
                        (characteristic.DimensionName ?? "(no name)") + "  -  " + failureReason);
                }
            }

            if (debugLog != null)
            {
                debugLog.Add(
                    "Live characteristics: " + characteristics.Count +
                    " total, " + (characteristics.Count - candidates.Count) + " unnumbered (skipped), " +
                    infoByCharacteristic.Count + " resolved as usable candidates, " +
                    resolutionFailures.Count + " failed to resolve.");

                if (resolutionFailures.Count > 0)
                {
                    debugLog.Add(string.Empty);
                    debugLog.Add("--- Failed to resolve (never considered a match for anything) ---");
                    debugLog.AddRange(resolutionFailures);
                }

                debugLog.Add(string.Empty);
                debugLog.Add("--- Legacy rows (" + legacyRows.Count + ") ---");
            }

            foreach (LegacyBalloonRow legacyRow in legacyRows)
            {
                LegacyMatchEntry entry = new LegacyMatchEntry { LegacyRow = legacyRow };

                double roundedLegacyNominal = Math.Round(legacyRow.Nominal, 4);
                DimensionFamily legacyFamily = GuessLegacyFamily(legacyRow.RawNominalText);

                // 10% of the legacy nominal's own magnitude, floored so a
                // near-zero legacy nominal doesn't collapse the band to
                // nothing - see MinimumNominalToleranceBand's remarks.
                double toleranceBand =
                    Math.Max(Math.Abs(roundedLegacyNominal) * NominalTolerancePercent, MinimumNominalToleranceBand);

                // Hard operation scope for this row - null when the row's
                // OperationLabel doesn't contain a recognizable "OP<digits>"
                // pattern, which leaves every candidate unscoped (see
                // NormalizeOperationKey's remarks).
                string legacyOperationKey = NormalizeOperationKey(legacyRow.OperationLabel);

                // Page-within-operation scope - a SECOND, independent gate
                // on top of the operation key above. See NormalizePageKey's
                // remarks for why this is required: one OperationLabel
                // ("OP 30") commonly spans several pages/sheets of that
                // operation's own paperwork, and the operation key alone
                // can't tell them apart.
                string legacyPageKey =
                    string.IsNullOrWhiteSpace(legacyRow.OperationSheetNumber)
                        ? null
                        : legacyRow.OperationSheetNumber.Trim();

                List<(Characteristic Characteristic, CandidateInfo Info, double Delta, bool InBand, ScoredCandidate Scored)> allDeltas =
                    debugLog != null
                        ? new List<(Characteristic, CandidateInfo, double, bool, ScoredCandidate)>()
                        : null;

                int outOfOperationScopeCount = 0;

                foreach (KeyValuePair<Characteristic, CandidateInfo> kvp in infoByCharacteristic)
                {
                    Characteristic characteristic = kvp.Key;
                    CandidateInfo info = kvp.Value;

                    // Out-of-scope candidates are dropped BEFORE nominal
                    // scoring even runs, per the user's explicit "the OP
                    // sheet number is the key - it has to be matched
                    // before any other dimensions can be read" - a
                    // candidate on a different operation's sheet is never
                    // a match no matter how close its nominal is.
                    //
                    // Fail-CLOSED on the candidate side as of 2026-09-21
                    // (user report, real stress-test spreadsheet: every
                    // loaded row was "OP 30", yet matches kept landing on
                    // characteristics spanning the whole drawing's number
                    // range - "it is matching any balloons in the legacy
                    // sheet instead of first filtering the results"). The
                    // original version only excluded a candidate when BOTH
                    // sides resolved to a DIFFERING key, leaving a
                    // candidate with no SheetName at all (an older
                    // characteristic that predates SheetName tracking, or
                    // one created by a PRIOR Legacy Conversion "new" row -
                    // see Apply_Click, which sets SheetName = null for
                    // those on purpose) wrongly treated as always eligible
                    // for every legacy row's scope, defeating the filter
                    // for exactly the characteristics most likely to be
                    // wrong. Now: whenever the LEGACY row itself names an
                    // operation, a candidate is only in scope if its OWN
                    // key resolves AND matches - an unresolvable candidate
                    // key no longer gets a free pass. A legacy row with NO
                    // recognizable operation (legacyOperationKey null) is
                    // still fully unscoped, same as before - there is
                    // nothing to filter by in that case.
                    string candidateOperationKey = operationKeyByCharacteristic[characteristic];

                    bool outOfOperationScope =
                        legacyOperationKey != null &&
                        !string.Equals(legacyOperationKey, candidateOperationKey, StringComparison.OrdinalIgnoreCase);

                    // Same fail-closed logic as the operation key, one
                    // level down: whenever the LEGACY row names a page
                    // within its operation, a candidate is only in scope
                    // if its own page key resolves AND matches - added
                    // 2026-09-21 (real debug-log data, user report: every
                    // one of 159 rows shared the same "OP30" operation key
                    // regardless of which page it was really on, so this
                    // check was the only thing missing - see
                    // NormalizePageKey's remarks for the full story).
                    string candidatePageKey = pageKeyByCharacteristic[characteristic];

                    bool outOfPageScope =
                        legacyPageKey != null &&
                        !string.Equals(legacyPageKey, candidatePageKey, StringComparison.OrdinalIgnoreCase);

                    if (outOfOperationScope || outOfPageScope)
                    {
                        outOfOperationScopeCount++;
                        continue;
                    }

                    double roundedCandidateNominal = Math.Round(info.Nominal, 4);
                    double nominalDelta = Math.Abs(roundedCandidateNominal - roundedLegacyNominal);
                    bool inBand = nominalDelta <= toleranceBand;

                    ScoredCandidate scored = null;

                    if (inBand)
                    {
                        scored = ScoreCandidate(legacyRow, legacyFamily, characteristic, info, nominalDelta, toleranceBand);
                        entry.Candidates.Add(scored);
                    }

                    allDeltas?.Add((characteristic, info, nominalDelta, inBand, scored));
                }

                entry.Candidates.Sort((a, b) => b.Score.CompareTo(a.Score));

                result.Entries.Add(entry);

                if (debugLog != null)
                {
                    AppendRowDebug(
                        debugLog, legacyRow, entry, allDeltas, toleranceBand,
                        legacyOperationKey, legacyPageKey, outOfOperationScopeCount, infoByCharacteristic.Count);
                }
            }

            return result;
        }

        // Appends one legacy row's full candidate breakdown to debugLog -
        // every in-band candidate (sorted by score, same order the review
        // grid shows) plus the 5 nearest near-misses just outside the
        // tolerance band by nominal delta.
        private static void AppendRowDebug(
            List<string> debugLog,
            LegacyBalloonRow legacyRow,
            LegacyMatchEntry entry,
            List<(Characteristic Characteristic, CandidateInfo Info, double Delta, bool InBand, ScoredCandidate Scored)> allDeltas,
            double toleranceBand,
            string legacyOperationKey,
            string legacyPageKey,
            int outOfOperationScopeCount,
            int totalCandidateCount)
        {
            debugLog.Add(string.Empty);
            debugLog.Add(
                "Legacy Balloon #" + legacyRow.LegacyBalloonNumber +
                (legacyRow.OperationLabel != null
                    ? "  operation=" + legacyRow.OperationLabel +
                        (legacyRow.OperationSheetNumber != null ? " (sheet " + legacyRow.OperationSheetNumber + ")" : string.Empty)
                    : string.Empty) +
                "  (raw: \"" + (legacyRow.RawNominalText ?? "?") + "\")" +
                "  nominal=" + legacyRow.Nominal.ToString("0.####") +
                (legacyRow.UpperLimit.HasValue ? "  upper=" + legacyRow.UpperLimit.Value.ToString("0.####") : string.Empty) +
                (legacyRow.LowerLimit.HasValue ? "  lower=" + legacyRow.LowerLimit.Value.ToString("0.####") : string.Empty) +
                (legacyRow.Class != null ? "  class=" + legacyRow.Class : string.Empty) +
                (legacyRow.Method != null ? "  method=" + legacyRow.Method : string.Empty));

            debugLog.Add(
                "  Operation/page scope: key=" +
                (legacyOperationKey ?? "(unscoped - no OP<digits> found in operation label)") +
                (legacyPageKey != null ? "-" + legacyPageKey : " (no page number - page not enforced)") +
                ", " + outOfOperationScopeCount + " of " + totalCandidateCount +
                " live candidate(s) excluded as a different operation's sheet/page.");

            var inBand = allDeltas.Where(d => d.InBand).OrderByDescending(d => d.Scored.Score).ToList();
            var nearMisses = allDeltas.Where(d => !d.InBand).OrderBy(d => d.Delta).Take(5).ToList();

            debugLog.Add(
                "  Tolerance band: +/-" + toleranceBand.ToString("0.####") +
                " (" + (NominalTolerancePercent * 100).ToString("0") + "% of " +
                Math.Abs(legacyRow.Nominal).ToString("0.####") + ", floored at " +
                MinimumNominalToleranceBand.ToString("0.####") + ")");

            if (inBand.Count == 0)
            {
                debugLog.Add("  No nominal match within the tolerance band.");
            }
            else
            {
                debugLog.Add("  Candidates in range:");

                foreach (var row in inBand)
                {
                    debugLog.Add(
                        "    (" + row.Characteristic.DisplayNumber + ")  " +
                        (row.Characteristic.DimensionName ?? "(no name)") +
                        "  sheet=" + (row.Characteristic.SheetName ?? "?") +
                        "  live_nominal=" + row.Info.Nominal.ToString("0.####") +
                        "  delta=" + row.Delta.ToString("0.####") +
                        "  score=" + row.Scored.Score.ToString("0") + "%" +
                        "  (" + row.Scored.Reason + ")");
                }
            }

            if (nearMisses.Count > 0)
            {
                debugLog.Add("  Nearest candidates OUTSIDE the tolerance band (for reference only):");

                foreach (var row in nearMisses)
                {
                    debugLog.Add(
                        "    (" + row.Characteristic.DisplayNumber + ")  " +
                        (row.Characteristic.DimensionName ?? "(no name)") +
                        "  sheet=" + (row.Characteristic.SheetName ?? "?") +
                        "  live_nominal=" + row.Info.Nominal.ToString("0.####") +
                        "  delta=" + row.Delta.ToString("0.####"));
                }
            }

            debugLog.Add(
                entry.BestMatch != null
                    ? "  => Legacy Balloon #" + legacyRow.LegacyBalloonNumber + " best match: (" +
                        entry.BestMatch.DisplayNumber + ") at " + entry.BestScore.Value.ToString("0") + "%"
                    : "  => Legacy Balloon #" + legacyRow.LegacyBalloonNumber + ": no match.");
        }

        private static CandidateInfo ResolveCandidateInfo(
            ModelDoc2 model, Characteristic characteristic, out string failureReason)
        {
            failureReason = null;

            if (string.IsNullOrEmpty(characteristic.PersistentRefId))
            {
                failureReason = "no PersistentRefId (never linked to a live dimension)";
                return null;
            }

            try
            {
                IDisplayDimension dim =
                    PersistentReferenceHelper.ResolveDimension(model, characteristic.PersistentRefId);

                if (dim == null)
                {
                    // A GD&T Feature Control Frame's PersistentRefId points
                    // at an IGtol annotation, NOT an IDisplayDimension - per
                    // ReportGenerator.ResolveCharacteristicDisplayText's own
                    // remarks, this is EXPECTED and "never resolves to a
                    // plain IDisplayDimension in the first place" for every
                    // GD&T/note/surface-finish/hole-callout characteristic,
                    // not a dangling-reference error. Bug found + fixed
                    // 2026-09-21 (user report, real debug-log data: every
                    // GD&T "Position"/"Profile of a Surface" characteristic
                    // showed "PersistentRefId did not resolve to a live
                    // dimension" and was silently dropped from the
                    // candidate pool BEFORE any legacy row could ever match
                    // it, no matter how close the nominal or how well the
                    // font-decoding/instance-count fixes worked - this
                    // method simply never tried anything but dimension
                    // resolution). ReportGenerator's own report already
                    // handles this by falling back to
                    // Characteristic.GdtToleranceValue - a GD&T frame's
                    // tolerance-zone value, captured once at balloon-
                    // creation time (see that field's remarks) - treated as
                    // Upper Limit with Lower Limit forced to 0, same
                    // one-sided-zone convention ReportGenerator.
                    // ResolveLimitCellValues uses. Reused here as the
                    // candidate's comparable "nominal" - matches exactly
                    // how LegacyNumberImporter treats a GD&T legacy row's
                    // own number (e.g. "Position .010 A B" -> nominal=0.01,
                    // upper=0.01, lower=0).
                    if (characteristic.GdtToleranceValue.HasValue)
                    {
                        double gdtValue = characteristic.GdtToleranceValue.Value;

                        return new CandidateInfo
                        {
                            Nominal = gdtValue,
                            PlusTolerance = gdtValue,
                            MinusTolerance = 0,
                            Family = DimensionFamily.Unknown,
                        };
                    }

                    failureReason = "PersistentRefId did not resolve to a live dimension";
                    return null;
                }

                double nominal, plusTolerance, minusTolerance;
                bool resolved;

                ReportGenerator.ReadDimensionValues(
                    model, dim, out nominal, out plusTolerance, out minusTolerance, out resolved);

                if (!resolved)
                {
                    failureReason = "resolved to a live dimension, but its value could not be read (dangling?)";
                    return null;
                }

                return new CandidateInfo
                {
                    Nominal = nominal,
                    PlusTolerance = plusTolerance,
                    MinusTolerance = minusTolerance,
                    Family = GetLiveFamily(dim),
                };
            }
            catch (Exception ex)
            {
                failureReason = "threw while resolving: " + ex.Message;
                return null;
            }
        }

        private static DimensionFamily GetLiveFamily(IDisplayDimension dim)
        {
            try
            {
                switch ((swDimensionType_e)dim.Type2)
                {
                    case swDimensionType_e.swAngularDimension:
                    case swDimensionType_e.swAngularOrdinateDimension:
                        return DimensionFamily.Angular;

                    case swDimensionType_e.swOrdinateDimension:
                    case swDimensionType_e.swHorOrdinateDimension:
                    case swDimensionType_e.swVertOrdinateDimension:
                        return DimensionFamily.Ordinate;

                    case swDimensionType_e.swChamferDimension:
                        return DimensionFamily.Chamfer;

                    default:
                        return DimensionFamily.Linear;
                }
            }
            catch
            {
                return DimensionFamily.Unknown;
            }
        }

        // Legacy spreadsheets have no dedicated "dimension type" column,
        // but LegacyBalloonRow.RawNominalText preserves the ORIGINAL cell
        // text from before LegacyNumberImporter strips it down to a bare
        // number - a degree symbol/suffix surviving there is the one
        // family clue reliably recoverable from a real legacy export.
        // Deliberately does NOT guess at diameter (Ø) or radius (R)
        // prefixes - see DimensionFamily's remarks for why there's no
        // live-side family to cross-check that guess against yet.
        private static DimensionFamily GuessLegacyFamily(string rawNominalText)
        {
            if (string.IsNullOrEmpty(rawNominalText))
                return DimensionFamily.Unknown;

            if (rawNominalText.IndexOf('°') >= 0 ||
                rawNominalText.IndexOf("DEG", StringComparison.OrdinalIgnoreCase) >= 0)
                return DimensionFamily.Angular;

            return DimensionFamily.Unknown;
        }

        // Combines every signal that has data on BOTH sides into one 0-100
        // score. A signal missing data on either side (no Upper/Lower on
        // the spreadsheet, a Basic/toleranceless live dimension, no Class
        // column, a candidate that's never had Class/Method set) is left
        // OUT of both the running total and the weight sum entirely -
        // never counted as a point against the candidate, since "we don't
        // know" is not the same as "it doesn't match."
        private static ScoredCandidate ScoreCandidate(
            LegacyBalloonRow legacyRow,
            DimensionFamily legacyFamily,
            Characteristic characteristic,
            CandidateInfo info,
            double nominalDelta,
            double toleranceBand)
        {
            List<string> reasons = new List<string>();

            // Tolerance-range and Class/Method re-enabled 2026-09-21 as
            // tie-breakers, once real debug-log data confirmed the
            // operation scope filter (above) was already doing its job
            // correctly - "wrong sheet" reports turned out to be several
            // genuinely different same-sheet characteristics sharing one
            // common nominal (e.g. three different features all at
            // 0.500" on the same operation's sheet), which nominal-only
            // scoring has no way to tell apart. FamilyWeight/dimension-
            // family agreement is STILL commented out below - unrelated to
            // this fix, kept off per the earlier caution about not having
            // a live-confirmed diameter/radius signal (see DimensionFamily's
            // remarks) - restore it the same way if it's ever needed too.
            double totalWeight = NominalWeight;
            double nominalScore = Math.Max(0.0, 1.0 - (nominalDelta / toleranceBand));
            double earnedWeight = nominalScore * NominalWeight;

            reasons.Add(
                nominalDelta < 0.0001
                    ? "nominal exact"
                    : "nominal within " + nominalDelta.ToString("0.####") +
                        " (" + (nominalDelta / toleranceBand * 100).ToString("0") + "% of band)");

            bool legacyHasRange = legacyRow.UpperLimit.HasValue && legacyRow.LowerLimit.HasValue;
            bool liveHasRange = info.PlusTolerance != 0 || info.MinusTolerance != 0;

            if (legacyHasRange && liveHasRange)
            {
                double legacyRange = legacyRow.UpperLimit.Value - legacyRow.LowerLimit.Value;
                double liveRange = info.PlusTolerance - info.MinusTolerance;
                double rangeDelta = Math.Abs(legacyRange - liveRange);

                double toleranceScore =
                    Math.Max(0.0, 1.0 - (rangeDelta / Math.Max(Math.Abs(legacyRange), 0.0001)));

                totalWeight += ToleranceWeight;
                earnedWeight += toleranceScore * ToleranceWeight;

                reasons.Add(toleranceScore >= 0.75 ? "tolerance close" : "tolerance differs");
            }

            /*
            if (legacyFamily != DimensionFamily.Unknown && info.Family != DimensionFamily.Unknown)
            {
                totalWeight += FamilyWeight;

                if (legacyFamily == info.Family)
                {
                    earnedWeight += FamilyWeight;
                    reasons.Add("type matches");
                }
                else
                {
                    reasons.Add("type differs");
                }
            }
            */

            bool classComparable =
                !string.IsNullOrEmpty(legacyRow.Class) && !string.IsNullOrEmpty(characteristic.Class);
            bool methodComparable =
                !string.IsNullOrEmpty(legacyRow.Method) && !string.IsNullOrEmpty(characteristic.Method);

            if (classComparable || methodComparable)
            {
                int comparableCount = (classComparable ? 1 : 0) + (methodComparable ? 1 : 0);
                int agreeCount = 0;

                if (classComparable &&
                    string.Equals(legacyRow.Class, characteristic.Class, StringComparison.OrdinalIgnoreCase))
                    agreeCount++;

                if (methodComparable &&
                    string.Equals(legacyRow.Method, characteristic.Method, StringComparison.OrdinalIgnoreCase))
                    agreeCount++;

                double agreementFraction = (double)agreeCount / comparableCount;

                totalWeight += ClassMethodWeight;
                earnedWeight += agreementFraction * ClassMethodWeight;

                reasons.Add(
                    agreementFraction >= 1.0
                        ? "class/method match"
                        : agreementFraction <= 0.0
                            ? "class/method conflict"
                            : "class/method partial match");
            }

            double score = totalWeight > 0 ? (earnedWeight / totalWeight) * 100.0 : 0.0;

            return new ScoredCandidate
            {
                Characteristic = characteristic,
                Score = score,
                Reason = string.Join(", ", reasons),
                Nominal = info.Nominal,
            };
        }
    }
}
