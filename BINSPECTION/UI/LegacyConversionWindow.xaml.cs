using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using BINSPECTION.Core;
using BINSPECTION.Models;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;

namespace BINSPECTION.UI
{
    // WPF replacement for the WinForms LegacyNumberResolutionForm, reached
    // from BalloonManagerWindow's "Legacy Conversion..." button rather than
    // its own SolidWorks toolbar command (the former OnMatchLegacyNumbers).
    // Reuses the same underlying pipeline unchanged - LegacyNumberImporter/
    // LegacyNumberMatcher/LegacyRenumberService - only the review grid's
    // presentation is WPF now.
    //
    // Kept as plain code-behind acting as its own minimal view-model
    // (DataContext = this), consistent with the rest of this add-in's WPF
    // windows.
    public partial class LegacyConversionWindow : Window
    {
        // One editable review row. UI-only - Legacy/Default mirror what
        // LegacyNumberResolutionForm tracked per row; Assign starts blank
        // for every row (including matched ones) - blank means "use the
        // match shown", same UX rule that form used.
        public class LegacyRow
        {
            public LegacyBalloonRow Legacy { get; set; }
            public string LegacyBalloonNumber { get; set; }

            // "(4)", etc. - the sheet-within-operation number (LegacyBalloonRow.
            // OperationSheetNumber), NOT the operation label - changed
            // 2026-09-21 (explicit user request: "instead of having OP as
            // the column, use the Sheet Number (1), (2), etc") since that's
            // what actually scopes matching now (see LegacyNumberMatcher's
            // page-key gate) - the operation label alone doesn't distinguish
            // rows the way the sheet number does. Blank when this
            // spreadsheet's ID column has no combined operation+item shape.
            public string SheetNumber { get; set; }

            public string Nominal { get; set; }

            // The MATCHED live characteristic's own nominal value, straight
            // off the SolidWorks dimension - added 2026-09-21 (explicit user
            // request: "i need to see what the nominal value is for the
            // actual dimension pulled") so the legacy spreadsheet's nominal
            // and the live drawing's nominal can be compared side by side
            // instead of only trusting the Score/Why columns. "-" when
            // nothing matched at all (no candidate to read a nominal from).
            public string MatchedNominal { get; set; }

            public string Method { get; set; }
            public string Class { get; set; }
            public string MatchesBalloon { get; set; }

            // The top candidate's score, e.g. "94%" - "-" when nothing
            // scored within LegacyNumberMatcher.NominalAdmissionBand at
            // all. See LegacyNumberMatcher.ScoreCandidate for how this is
            // computed.
            public string Score { get; set; }

            public string Reason { get; set; }
            public Characteristic Default { get; set; }
            public string Assign { get; set; } = "";
        }

        private readonly ModelDoc2 _model;
        private readonly string _dataFilePath;
        private readonly ProjectData _projectData;

        // Sheets BalloonManagerWindow's LegacyConversionSheetSelectionWindow
        // picker gate confirmed before this window ever opened. Null means
        // no restriction (every characteristic eligible) - kept as a
        // fallback for robustness, not a code path this window's own
        // caller actually takes.
        private readonly HashSet<string> _selectedSheets;

        private Dictionary<string, Characteristic> _eligibleByDisplayNumber;

        // The full, unfiltered set of rows the last spreadsheet load
        // produced - Operation/SelectedOperation filter against this
        // without needing to re-read the file from disk.
        private List<LegacyBalloonRow> _loadedLegacyRows;

        public ObservableCollection<LegacyRow> Rows { get; } = new ObservableCollection<LegacyRow>();

        // TEMPORARY DISABLED (2026-09-21, explicit user request - "the
        // operation selection in the legacy conversion can be commented
        // out, it is causing an issue where no balloons are being
        // matched, for now leave it out") - the selectable Operation
        // filter added earlier the same day. Commented out, not deleted -
        // the XAML ComboBox bound to these is also commented out (see
        // LegacyConversionWindow.xaml), and RunMatch() below no longer
        // filters by operation at all (every loaded row is considered,
        // same as before this feature existed). Root cause of the "no
        // balloons matched" symptom not yet confirmed - suspect is the
        // ComboBox's SelectedItem getting reset to null by WPF when
        // Operations.Clear() runs (before the re-populate loop finishes),
        // which would flow back through this TwoWay binding and could
        // leave SelectedOperation in an unexpected state independent of
        // the _selectedOperation backing field LoadSpreadsheet_Click sets
        // directly. Restore by un-commenting this block, the matching
        // filter block in RunMatch(), and the ComboBox in the XAML.
        /*
        public ObservableCollection<string> Operations { get; } = new ObservableCollection<string>();

        private const string AllOperationsLabel = "(All)";

        private string _selectedOperation = AllOperationsLabel;

        public string SelectedOperation
        {
            get => _selectedOperation;
            set
            {
                _selectedOperation = value;
                RunMatch();
            }
        }
        */

        public LegacyConversionWindow(
            ModelDoc2 model,
            string dataFilePath,
            ProjectData projectData,
            List<string> selectedSheets = null)
        {
            InitializeComponent();
            WindowSizing.FitToScreen(this);
            DataContext = this;

            _model = model;
            _dataFilePath = dataFilePath;
            _projectData = projectData;

            _selectedSheets =
                selectedSheets != null
                    ? new HashSet<string>(selectedSheets, StringComparer.OrdinalIgnoreCase)
                    : null;
        }

        // True if characteristic is in scope for this Legacy Conversion run -
        // its own sheet is one the user checked in the picker, OR its sheet
        // isn't known at all. An unknown SheetName (an older characteristic
        // that predates SheetName tracking - see Models/Characteristics.cs
        // remarks - or one of this window's own "new" rows, which never get
        // one either) can't be safely excluded by a filter that has no idea
        // which sheet it actually belongs to, so it stays eligible
        // regardless of what's checked.
        private bool IsInScope(Characteristic characteristic)
        {
            return _selectedSheets == null ||
                string.IsNullOrEmpty(characteristic.SheetName) ||
                _selectedSheets.Contains(characteristic.SheetName);
        }

        private string _loadedSpreadsheetPath;

        private void LoadSpreadsheet_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Select the legacy system reference spreadsheet",
                Filter = "Excel Workbook (*.xlsx)|*.xlsx|All Files (*.*)|*.*",
            };

            if (dialog.ShowDialog() != true)
                return;

            List<LegacyBalloonRow> legacyRows;

            try
            {
                legacyRows = LegacyNumberImporter.Load(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not read the spreadsheet: " + ex.Message, "Legacy Conversion");
                return;
            }

            if (legacyRows.Count == 0)
            {
                MessageBox.Show(this, "No usable legacy balloon rows were found in that spreadsheet.", "Legacy Conversion");
                return;
            }

            _loadedLegacyRows = legacyRows;
            _loadedSpreadsheetPath = dialog.FileName;

            // TEMPORARY DISABLED - see Operations' remarks above. The
            // Operation ComboBox population used to happen here.

            RunMatch();
        }

        // Re-runs the match against _loadedLegacyRows - called once right
        // after a spreadsheet loads (was also re-called on every OP filter
        // change - see Operations' remarks above for why that's disabled).
        private void RunMatch()
        {
            if (_loadedLegacyRows == null)
                return;

            // TEMPORARY DISABLED - see Operations' remarks above. Every
            // loaded row is used unfiltered for now, same as before the OP
            // filter feature existed.
            List<LegacyBalloonRow> legacyRows = _loadedLegacyRows;

            // Scoped to the sheets checked in LegacyConversionSheetSelectionWindow
            // before this window opened - a characteristic on a sheet the
            // user didn't check this run is neither auto-matched nor
            // offered as a manual "Assign" target, so it's left alone
            // entirely (can still be converted in a later run). See
            // IsInScope's remarks for why an unknown SheetName is always
            // included rather than excluded.
            List<Characteristic> scopedCharacteristics = _projectData.Characteristics
                .Where(IsInScope)
                .ToList();

            // TEMPORARY diagnostic (2026-09-21, explicit user request -
            // "there is a disconnect" between what's expected and what
            // Legacy Conversion actually matches): captures exactly what
            // LegacyNumberMatcher considered for every legacy row - every
            // candidate that resolved (or didn't, and why), every in-band
            // score, and the nearest out-of-band near-misses - so a wrong-
            // looking match can be diagnosed by reading a file instead of
            // attaching a debugger to the live SolidWorks-hosted add-in.
            // Remove this call (and the WriteMatchDebugLog/OpenDebugLog
            // methods below) once the disconnect is understood.
            List<string> debugLog = new List<string> { "=== Legacy Conversion match diagnostic ===" };

            debugLog.Add("Spreadsheet: " + _loadedSpreadsheetPath);
            debugLog.Add(
                "Sheets in scope: " +
                (_selectedSheets == null ? "(all - no restriction)" : string.Join(", ", _selectedSheets)));
            debugLog.Add("Operation filter: disabled (all " + legacyRows.Count + " legacy row(s) in scope)");
            debugLog.Add(string.Empty);

            LegacyMatchResult matchResult =
                LegacyNumberMatcher.Match(_model, scopedCharacteristics, legacyRows, debugLog);

            string debugLogPath = WriteMatchDebugLog(debugLog);

            OpenDebugLog(debugLogPath);

            // Every eligible balloon on the drawing (within this run's sheet
            // scope) is offered to every row, not narrowed per row - same
            // as the WinForms dialog this replaces, since any row
            // (including an already-matched one) can be redirected to any
            // balloon.
            List<Characteristic> eligibleCandidates = scopedCharacteristics
                .Where(c => !c.IsUnnumbered)
                .ToList();

            _eligibleByDisplayNumber = eligibleCandidates
                .GroupBy(c => c.DisplayNumber, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            Rows.Clear();

            List<(LegacyBalloonRow Row, string MatchesBalloon, string MatchedNominal, string Score, string Reason, Characteristic Default)> allRows =
                new List<(LegacyBalloonRow, string, string, string, string, Characteristic)>();

            // A runner-up candidate this close (in score points) to the
            // top one is worth calling out explicitly - the top pick is
            // still shown as the suggestion (every row always gets one, if
            // any candidate exists at all), but "verify before applying"
            // in the Reason column is the built-in check the user asked
            // for on anything that isn't a clear best pick.
            const double CloseCandidateGapPoints = 8.0;

            foreach (LegacyMatchEntry entry in matchResult.Entries)
            {
                if (entry.Candidates.Count == 0)
                {
                    allRows.Add((
                        entry.LegacyRow,
                        "-",
                        "-",
                        "-",
                        "No balloon matched this dimension value",
                        null));

                    continue;
                }

                ScoredCandidate best = entry.Candidates[0];

                string reason = best.Reason;

                if (entry.Candidates.Count > 1)
                {
                    ScoredCandidate runnerUp = entry.Candidates[1];

                    if (best.Score - runnerUp.Score < CloseCandidateGapPoints)
                    {
                        reason +=
                            " - close to (" + runnerUp.Characteristic.DisplayNumber + ") at " +
                            runnerUp.Score.ToString("0") + "%, verify before applying";
                    }
                }

                allRows.Add((
                    entry.LegacyRow,
                    "(" + best.Characteristic.DisplayNumber + ")  " + best.Characteristic.DimensionName,
                    best.Nominal.ToString("0.####"),
                    best.Score.ToString("0") + "%",
                    reason,
                    best.Characteristic));
            }

            foreach (var row in allRows.OrderBy(r => r.Row.LegacyBalloonNumber, new LegacyBalloonNumberComparer()))
            {
                Rows.Add(new LegacyRow
                {
                    Legacy = row.Row,
                    LegacyBalloonNumber = row.Row.LegacyBalloonNumber,
                    SheetNumber = string.IsNullOrEmpty(row.Row.OperationSheetNumber) ? null : "(" + row.Row.OperationSheetNumber + ")",
                    Nominal = row.Row.RawNominalText ?? row.Row.Nominal.ToString("0.####"),
                    MatchedNominal = row.MatchedNominal,
                    Method = row.Row.Method,
                    Class = row.Row.Class,
                    MatchesBalloon = row.MatchesBalloon,
                    Score = row.Score,
                    Reason = row.Reason,
                    Default = row.Default,
                });
            }
        }

        // TEMPORARY diagnostic - see LoadSpreadsheet_Click's remarks.
        // Written next to the sidecar JSON (same folder the user already
        // knows to look in) so it's easy to find; falls back to the temp
        // folder on the rare chance the drawing has no data file path yet.
        // Overwritten every run rather than appended - only the most
        // recent load's matching is ever relevant to look at.
        private string WriteMatchDebugLog(List<string> debugLog)
        {
            string directory =
                !string.IsNullOrEmpty(_dataFilePath) ? Path.GetDirectoryName(_dataFilePath) : null;

            string path = Path.Combine(
                !string.IsNullOrEmpty(directory) ? directory : Path.GetTempPath(),
                "LegacyConversionMatchDebug.txt");

            try
            {
                File.WriteAllLines(path, debugLog);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Matched, but couldn't write the debug log to " + path + ": " + ex.Message,
                    "Legacy Conversion");

                return null;
            }

            return path;
        }

        // TEMPORARY diagnostic - see LoadSpreadsheet_Click's remarks.
        // Opens with whatever the user's own machine has associated with
        // .txt (Notepad, normally) - failure here (no path because writing
        // it failed above, or nothing registered to open .txt) is shown,
        // not silently swallowed, since the whole point of this file is
        // for the user to actually read it.
        private void OpenDebugLog(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Wrote the debug log to " + path + " but couldn't open it: " + ex.Message,
                    "Legacy Conversion");
            }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (Rows.Count == 0)
            {
                MessageBox.Show(this, "Load a spreadsheet first.", "Legacy Conversion");
                return;
            }

            RowsGrid.CommitEdit(DataGridEditingUnit.Row, true);

            Dictionary<LegacyBalloonRow, Characteristic> resolvedAssignments;
            List<LegacyBalloonRow> newCharacteristicRequests;

            if (!TryResolveAssignments(out resolvedAssignments, out newCharacteristicRequests))
                return;

            List<Characteristic> characteristics = _projectData.Characteristics;

            List<string> newCharacteristicInvalidFormat = new List<string>();
            HashSet<Characteristic> newlyCreatedCharacteristics = new HashSet<Characteristic>();

            int nextPlaceholderNumber = characteristics
                .Where(c => !c.IsUnnumbered)
                .Select(c => c.Number)
                .DefaultIfEmpty(0)
                .Max() + 1;

            foreach (LegacyBalloonRow row in newCharacteristicRequests)
            {
                int parsedNumber;
                int? parsedSubNumber;
                string parsedDisplayNumber;

                if (!LegacyRenumberService.TryParseDisplayNumber(
                    row.LegacyBalloonNumber, out parsedNumber, out parsedSubNumber, out parsedDisplayNumber))
                {
                    newCharacteristicInvalidFormat.Add(row.LegacyBalloonNumber);
                    continue;
                }

                Characteristic newCharacteristic = new Characteristic
                {
                    Number = nextPlaceholderNumber++,
                    PersistentRefId = null,
                    DimensionName = "(added from legacy balloon #" + row.LegacyBalloonNumber + ")",
                    IsUnnumbered = false,
                    SubNumber = null,
                    PreGroupNumber = null,
                    LegacyBalloonNumber = row.LegacyBalloonNumber,
                    SheetName = null,
                    Method = row.Method,
                    Class = row.Class,
                };

                characteristics.Add(newCharacteristic);
                resolvedAssignments[row] = newCharacteristic;
                newlyCreatedCharacteristics.Add(newCharacteristic);
            }

            int assignedCount = resolvedAssignments.Count;

            List<KeyValuePair<Characteristic, string>> requestedLegacyNumbers =
                new List<KeyValuePair<Characteristic, string>>();

            // Reverse lookup (Characteristic -> the legacy row that matched
            // it) so LegacyBalloonNumber/Method/Class can be stamped AFTER
            // BuildPlan runs, from whichever legacy row actually won -
            // stamping it here, before BuildPlan even sees these requests,
            // used to mean a characteristic whose match got REJECTED as an
            // unresolved Collision (see BuildPlan's remarks) still ended up
            // with a "Legacy Balloon #" on it that had nothing to do with
            // what actually happened to its real balloon number - confirmed
            // live 2026-09-21 ("the legacy balloon number is not pulling
            // the right number"). First-wins is fine for the handful of
            // characteristics with more than one AGREEING legacy row (the
            // same OP+item sampled several times - see
            // [[binspection_legacy_number_matching]]'s dedup note) since
            // they all carry the same Method/Class/LegacyBalloonNumber
            // anyway.
            Dictionary<Characteristic, LegacyBalloonRow> legacyRowByCharacteristic =
                new Dictionary<Characteristic, LegacyBalloonRow>();

            foreach (KeyValuePair<LegacyBalloonRow, Characteristic> assignment in resolvedAssignments)
            {
                if (!legacyRowByCharacteristic.ContainsKey(assignment.Value))
                    legacyRowByCharacteristic[assignment.Value] = assignment.Key;

                requestedLegacyNumbers.Add(
                    new KeyValuePair<Characteristic, string>(assignment.Value, assignment.Key.LegacyBalloonNumber));
            }

            LegacyRenumberService.RenumberPlan renumberPlan =
                LegacyRenumberService.BuildPlan(characteristics, requestedLegacyNumbers);

            LegacyRenumberService.Apply(_model, renumberPlan, new BalloonManager());

            void StampFromLegacyRow(Characteristic characteristic)
            {
                LegacyBalloonRow legacyRow;

                if (!legacyRowByCharacteristic.TryGetValue(characteristic, out legacyRow))
                    return;

                characteristic.LegacyBalloonNumber = legacyRow.LegacyBalloonNumber;

                if (legacyRow.Method != null)
                    characteristic.Method = legacyRow.Method;

                if (legacyRow.Class != null)
                    characteristic.Class = legacyRow.Class;
            }

            foreach (LegacyRenumberService.RenumberPlanEntry entry in renumberPlan.ToApply)
            {
                if (entry.IsDisplaced)
                {
                    entry.Characteristic.LegacyBalloonNumber = null;
                    continue;
                }

                StampFromLegacyRow(entry.Characteristic);
            }

            foreach (Characteristic characteristic in renumberPlan.AlreadyCorrect)
                StampFromLegacyRow(characteristic);

            PersistenceManager.SaveProject(_dataFilePath, _projectData);

            string summary = BuildSummary(
                Rows.Select(r => r.Legacy).ToList(),
                resolvedAssignments,
                renumberPlan,
                newlyCreatedCharacteristics,
                newCharacteristicInvalidFormat,
                assignedCount);

            MessageBox.Show(this, summary, "Legacy Conversion");

            DialogResult = true;
        }

        // Returns false (and leaves the dialog open with an explanatory
        // message) if any row has text that isn't blank and isn't one of
        // the balloon numbers actually offered to it.
        private bool TryResolveAssignments(
            out Dictionary<LegacyBalloonRow, Characteristic> assignments,
            out List<LegacyBalloonRow> newRequests)
        {
            assignments = new Dictionary<LegacyBalloonRow, Characteristic>();
            newRequests = new List<LegacyBalloonRow>();

            foreach (LegacyRow row in Rows)
            {
                string typed = row.Assign?.Trim();

                if (string.Equals(typed, "skip", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.Equals(typed, "new", StringComparison.OrdinalIgnoreCase))
                {
                    newRequests.Add(row.Legacy);
                    continue;
                }

                if (string.IsNullOrEmpty(typed))
                {
                    if (row.Default != null)
                        assignments[row.Legacy] = row.Default;

                    continue;
                }

                Characteristic match;

                if (_eligibleByDisplayNumber == null || !_eligibleByDisplayNumber.TryGetValue(typed, out match))
                {
                    MessageBox.Show(
                        this,
                        "\"" + typed + "\" isn't one of the available Binspection balloon numbers for legacy balloon #" +
                        row.LegacyBalloonNumber + ". Enter one of the numbers named in that row, type \"new\" to add a " +
                        "brand new characteristic, or leave it blank to use the match shown (or skip, if there is none).",
                        "Legacy Conversion");

                    return false;
                }

                assignments[row.Legacy] = match;
            }

            return true;
        }

        private static string BuildSummary(
            List<LegacyBalloonRow> legacyRows,
            Dictionary<LegacyBalloonRow, Characteristic> resolvedAssignments,
            LegacyRenumberService.RenumberPlan renumberPlan,
            HashSet<Characteristic> newlyCreatedCharacteristics,
            List<string> newCharacteristicInvalidFormat,
            int assignedCount)
        {
            List<LegacyRenumberService.RenumberPlanEntry> addedNew = renumberPlan.ToApply
                .Where(entry => !entry.IsDisplaced && newlyCreatedCharacteristics.Contains(entry.Characteristic))
                .ToList();

            List<LegacyRenumberService.RenumberPlanEntry> legacyRenumbers = renumberPlan.ToApply
                .Where(entry => !entry.IsDisplaced && !newlyCreatedCharacteristics.Contains(entry.Characteristic))
                .ToList();

            List<LegacyRenumberService.RenumberPlanEntry> displaced =
                renumberPlan.ToApply.Where(entry => entry.IsDisplaced).ToList();

            StringBuilder summary = new StringBuilder();

            summary.AppendLine(
                "Matched " + assignedCount + " of " + legacyRows.Count + " legacy balloon number(s), " +
                "renumbered " + legacyRenumbers.Count + " balloon(s), added " + addedNew.Count +
                " new characteristic(s).");

            if (legacyRenumbers.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine("Renumbered:");

                foreach (LegacyRenumberService.RenumberPlanEntry entry in legacyRenumbers)
                {
                    summary.AppendLine(
                        "   (" + entry.OldDisplayNumber + ")  ->  (" + entry.NewDisplayNumber + ")  " +
                        entry.Characteristic.DimensionName);
                }
            }

            if (addedNew.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine("Added as new characteristics (not tied to any drawing dimension):");

                foreach (LegacyRenumberService.RenumberPlanEntry entry in addedNew)
                    summary.AppendLine("   (" + entry.NewDisplayNumber + ")");
            }

            if (displaced.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine(
                    displaced.Count + " balloon(s) with no legacy balloon number of their own were moved out of the way to make room:");

                foreach (LegacyRenumberService.RenumberPlanEntry entry in displaced)
                {
                    summary.AppendLine(
                        "   (" + entry.OldDisplayNumber + ")  ->  (" + entry.NewDisplayNumber + ")  " +
                        entry.Characteristic.DimensionName);
                }
            }

            if (renumberPlan.Collisions.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine(
                    renumberPlan.Collisions.Count +
                    " legacy balloon number(s) could not be applied (recorded as a reference only) - resolve manually:");

                foreach (string collision in renumberPlan.Collisions)
                    summary.AppendLine("   " + collision);
            }

            if (renumberPlan.InvalidFormat.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine(
                    renumberPlan.InvalidFormat.Count +
                    " legacy balloon number(s) aren't a valid balloon number format (recorded as a reference only): " +
                    string.Join(", ", renumberPlan.InvalidFormat));
            }

            if (newCharacteristicInvalidFormat.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine(
                    newCharacteristicInvalidFormat.Count +
                    " new characteristic(s) couldn't be added - not a valid balloon number format: " +
                    string.Join(", ", newCharacteristicInvalidFormat));
            }

            HashSet<LegacyBalloonRow> accountedFor = new HashSet<LegacyBalloonRow>(resolvedAssignments.Keys);

            List<LegacyBalloonRow> leftUnmatched =
                legacyRows.Where(row => !accountedFor.Contains(row)).ToList();

            if (leftUnmatched.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine(
                    leftUnmatched.Count + " left unmatched: " +
                    string.Join(", ", leftUnmatched.Select(row => row.LegacyBalloonNumber)));
            }

            return summary.ToString();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        // Orders legacy balloon numbers the way a person reading the
        // source spreadsheet would - "2" before "10", "9.1" before "9.2" -
        // rather than plain string order.
        private class LegacyBalloonNumberComparer : IComparer<string>
        {
            public int Compare(string x, string y)
            {
                int majorX, minorX, majorY, minorY;

                bool parsedX = TryParse(x, out majorX, out minorX);
                bool parsedY = TryParse(y, out majorY, out minorY);

                if (parsedX && parsedY)
                {
                    int majorCompare = majorX.CompareTo(majorY);
                    return majorCompare != 0 ? majorCompare : minorX.CompareTo(minorY);
                }

                if (parsedX != parsedY)
                    return parsedX ? -1 : 1;

                return string.CompareOrdinal(x, y);
            }

            private static bool TryParse(string value, out int major, out int minor)
            {
                major = 0;
                minor = 0;

                if (string.IsNullOrEmpty(value))
                    return false;

                string[] parts = value.Split('.');

                if (!int.TryParse(parts[0], out major))
                    return false;

                if (parts.Length > 1 && !int.TryParse(parts[1], out minor))
                    return false;

                return true;
            }
        }
    }
}
