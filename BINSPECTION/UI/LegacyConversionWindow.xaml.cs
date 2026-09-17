using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
            public string LegacyNumber { get; set; }
            public string Nominal { get; set; }
            public string Method { get; set; }
            public string Class { get; set; }
            public string MatchesBalloon { get; set; }
            public string Reason { get; set; }
            public Characteristic Default { get; set; }
            public string Assign { get; set; } = "";
        }

        private readonly ModelDoc2 _model;
        private readonly string _dataFilePath;
        private readonly ProjectData _projectData;

        private Dictionary<string, Characteristic> _eligibleByDisplayNumber;

        public ObservableCollection<LegacyRow> Rows { get; } = new ObservableCollection<LegacyRow>();

        public LegacyConversionWindow(ModelDoc2 model, string dataFilePath, ProjectData projectData)
        {
            InitializeComponent();
            WindowSizing.FitToScreen(this);
            DataContext = this;

            _model = model;
            _dataFilePath = dataFilePath;
            _projectData = projectData;
        }

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

            LegacyMatchResult matchResult =
                LegacyNumberMatcher.Match(_model, _projectData.Characteristics, legacyRows);

            // Every eligible balloon on the drawing is offered to every row,
            // not narrowed per row - same as the WinForms dialog this
            // replaces, since any row (including an already-matched one)
            // can be redirected to any balloon.
            List<Characteristic> eligibleCandidates = _projectData.Characteristics
                .Where(c => !c.IsUnnumbered)
                .ToList();

            _eligibleByDisplayNumber = eligibleCandidates
                .GroupBy(c => c.DisplayNumber, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            Rows.Clear();

            List<(LegacyBalloonRow Row, string MatchesBalloon, string Reason, Characteristic Default)> allRows =
                new List<(LegacyBalloonRow, string, string, Characteristic)>();

            foreach (LegacyMatchEntry entry in matchResult.Matched)
            {
                allRows.Add((
                    entry.LegacyRow,
                    "(" + entry.Characteristic.DisplayNumber + ")  " + entry.Characteristic.DimensionName,
                    "Matched automatically",
                    entry.Characteristic));
            }

            foreach (LegacyAmbiguousEntry entry in matchResult.Ambiguous)
            {
                string candidateList = string.Join(", ", entry.Candidates.Select(c => "(" + c.DisplayNumber + ")"));

                allRows.Add((
                    entry.LegacyRow,
                    "-",
                    entry.Candidates.Count + " balloons share this dimension value: " + candidateList,
                    null));
            }

            foreach (LegacyBalloonRow row in matchResult.Unmatched)
            {
                allRows.Add((row, "-", "No balloon matched this dimension value", null));
            }

            foreach (var row in allRows.OrderBy(r => r.Row.LegacyNumber, new LegacyNumberComparer()))
            {
                Rows.Add(new LegacyRow
                {
                    Legacy = row.Row,
                    LegacyNumber = row.Row.LegacyNumber,
                    Nominal = row.Row.RawNominalText ?? row.Row.Nominal.ToString("0.####"),
                    Method = row.Row.Method,
                    Class = row.Row.Class,
                    MatchesBalloon = row.MatchesBalloon,
                    Reason = row.Reason,
                    Default = row.Default,
                });
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
                    row.LegacyNumber, out parsedNumber, out parsedSubNumber, out parsedDisplayNumber))
                {
                    newCharacteristicInvalidFormat.Add(row.LegacyNumber);
                    continue;
                }

                Characteristic newCharacteristic = new Characteristic
                {
                    Number = nextPlaceholderNumber++,
                    PersistentRefId = null,
                    DimensionName = "(added from legacy #" + row.LegacyNumber + ")",
                    IsUnnumbered = false,
                    SubNumber = null,
                    PreGroupNumber = null,
                    LegacyNumber = row.LegacyNumber,
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

            foreach (KeyValuePair<LegacyBalloonRow, Characteristic> assignment in resolvedAssignments)
            {
                assignment.Value.LegacyNumber = assignment.Key.LegacyNumber;

                if (assignment.Key.Method != null)
                    assignment.Value.Method = assignment.Key.Method;

                if (assignment.Key.Class != null)
                    assignment.Value.Class = assignment.Key.Class;

                requestedLegacyNumbers.Add(
                    new KeyValuePair<Characteristic, string>(assignment.Value, assignment.Key.LegacyNumber));
            }

            LegacyRenumberService.RenumberPlan renumberPlan =
                LegacyRenumberService.BuildPlan(characteristics, requestedLegacyNumbers);

            LegacyRenumberService.Apply(_model, renumberPlan, new BalloonManager());

            foreach (LegacyRenumberService.RenumberPlanEntry entry in renumberPlan.ToApply)
            {
                if (entry.IsDisplaced)
                    entry.Characteristic.LegacyNumber = null;
            }

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
                        "\"" + typed + "\" isn't one of the available Binspection balloon numbers for legacy #" +
                        row.LegacyNumber + ". Enter one of the numbers named in that row, type \"new\" to add a " +
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
                    displaced.Count + " balloon(s) with no legacy number of their own were moved out of the way to make room:");

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
                    " legacy number(s) could not be applied (recorded as a reference only) - resolve manually:");

                foreach (string collision in renumberPlan.Collisions)
                    summary.AppendLine("   " + collision);
            }

            if (renumberPlan.InvalidFormat.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine(
                    renumberPlan.InvalidFormat.Count +
                    " legacy number(s) aren't a valid balloon number format (recorded as a reference only): " +
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
                    string.Join(", ", leftUnmatched.Select(row => row.LegacyNumber)));
            }

            return summary.ToString();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        // Orders legacy numbers the way a person reading the source
        // spreadsheet would - "2" before "10", "9.1" before "9.2" - rather
        // than plain string order.
        private class LegacyNumberComparer : IComparer<string>
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
