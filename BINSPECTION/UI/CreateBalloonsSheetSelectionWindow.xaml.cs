using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BINSPECTION.Models;

namespace BINSPECTION.UI
{
    // Shown every time Create Balloons is run (CommandManagerHandler.
    // OnCreateBalloons) so the user picks which sheets to populate THIS
    // run, and optionally reserves a balloon-number range for a sheet (or
    // the same range on several sheets), instead of Create Balloons
    // silently acting on every sheet with no numbering control. The
    // window's own "Create" button carries both settings back out
    // together - see CharacteristicManager.GetNextNumberForSheet for how
    // a configured range is actually used once numbering runs.
    //
    // This picker's checkbox is the ONLY place that decides which sheets
    // get scanned/ballooned - Sheet Tolerance Selection used to have its
    // own "Populated" checkbox that looked like it controlled the same
    // thing, but it never actually gated anything downstream, which let
    // dimensions/notes on a sheet nobody meant to touch get pulled in and
    // ballooned onto whichever sheet WAS selected here. That checkbox was
    // removed; see OnCreateBalloons.BelongsOnSheet for the matching fix on
    // the scan/filter side.
    public partial class CreateBalloonsSheetSelectionWindow : Window
    {
        public class SheetRow
        {
            public string Name { get; set; }
            public bool IsChecked { get; set; }

            // Plain strings, not int? - kept as free text and parsed on
            // Create so an in-progress/blank entry never fights the
            // DataGrid's own value-conversion (see this codebase's
            // standing preference for plain text + post-hoc validation
            // over relying on WPF/WinForms editor-cell type conversion,
            // e.g. [[binspection-legacy-number-matching]]'s combo-box
            // saga).
            public string RangeStart { get; set; }
            public string RangeEnd { get; set; }
        }

        public ObservableCollection<SheetRow> Sheets { get; } = new ObservableCollection<SheetRow>();

        // Null if the user cancelled.
        public List<string> SelectedSheets { get; private set; }

        // Null if the user cancelled. Always non-null (possibly empty) once
        // Create succeeds - one entry per sheet that has a valid range set.
        public List<BalloonNumberRange> NumberRanges { get; private set; }

        public CreateBalloonsSheetSelectionWindow(
            List<string> sheetNames,
            IEnumerable<string> previouslySelected,
            IEnumerable<BalloonNumberRange> previousRanges)
        {
            InitializeComponent();
            WindowSizing.FitToScreen(this);
            DataContext = this;

            HashSet<string> selected =
                previouslySelected != null
                    ? new HashSet<string>(previouslySelected)
                    : new HashSet<string>();

            Dictionary<string, BalloonNumberRange> rangesBySheet =
                new Dictionary<string, BalloonNumberRange>();

            if (previousRanges != null)
            {
                foreach (BalloonNumberRange range in previousRanges)
                {
                    if (range.SheetNames == null)
                        continue;

                    foreach (string sheetName in range.SheetNames)
                    {
                        rangesBySheet[sheetName] = range;
                    }
                }
            }

            foreach (string name in sheetNames ?? new List<string>())
            {
                BalloonNumberRange existingRange;
                rangesBySheet.TryGetValue(name, out existingRange);

                Sheets.Add(new SheetRow
                {
                    Name = name,
                    IsChecked = selected.Contains(name),
                    RangeStart = existingRange != null ? existingRange.RangeStart.ToString() : null,
                    RangeEnd = existingRange != null ? existingRange.RangeEnd.ToString() : null,
                });
            }

            StartSheetCombo.ItemsSource = sheetNames;
            EndSheetCombo.ItemsSource = sheetNames;
        }

        // Fills the same Range Start/End into every sheet row between the
        // chosen Start/End sheet (inclusive, by position in the sheet-tab
        // order the grid was built with - see the constructor), so a
        // multi-page range no longer has to be typed into each row by hand.
        // Order of the two combo picks doesn't matter - whichever sheet
        // comes first in the drawing is treated as the start.
        private void ApplyRange_Click(object sender, RoutedEventArgs e)
        {
            string startSheet = StartSheetCombo.SelectedItem as string;
            string endSheet = EndSheetCombo.SelectedItem as string;

            if (startSheet == null || endSheet == null)
            {
                MessageBox.Show(this, "Pick both a start sheet and an end sheet.", "Create Balloons");
                return;
            }

            int start, end;

            if (!int.TryParse((SpanRangeStartBox.Text ?? "").Trim(), out start) ||
                !int.TryParse((SpanRangeEndBox.Text ?? "").Trim(), out end))
            {
                MessageBox.Show(this, "Enter a whole-number Range Start and Range End to apply.", "Create Balloons");
                return;
            }

            if (start < 1 || end < start)
            {
                MessageBox.Show(this, "Range Start must be 1 or greater and no larger than Range End.", "Create Balloons");
                return;
            }

            int startIndex = IndexOfSheet(startSheet);
            int endIndex = IndexOfSheet(endSheet);

            if (startIndex > endIndex)
            {
                int swap = startIndex;
                startIndex = endIndex;
                endIndex = swap;
            }

            for (int i = startIndex; i <= endIndex; i++)
            {
                Sheets[i].RangeStart = start.ToString();
                Sheets[i].RangeEnd = end.ToString();
            }

            SheetsGrid.Items.Refresh();
        }

        private int IndexOfSheet(string name)
        {
            for (int i = 0; i < Sheets.Count; i++)
            {
                if (Sheets[i].Name == name)
                    return i;
            }

            return -1;
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (SheetRow row in Sheets)
                row.IsChecked = true;

            SheetsGrid.Items.Refresh();
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (SheetRow row in Sheets)
                row.IsChecked = false;

            SheetsGrid.Items.Refresh();
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            // Commit whatever cell is still mid-edit (e.g. the user typed a
            // range value and clicked Create without tabbing off the cell
            // first) before reading row values back out.
            SheetsGrid.CommitEdit(DataGridEditingUnit.Row, true);

            List<string> chosen = Sheets.Where(r => r.IsChecked).Select(r => r.Name).ToList();

            if (chosen.Count == 0)
            {
                MessageBox.Show(this, "Check at least one sheet to balloon.", "Create Balloons");
                return;
            }

            List<BalloonNumberRange> ranges = new List<BalloonNumberRange>();

            foreach (SheetRow row in Sheets)
            {
                bool hasStart = !string.IsNullOrWhiteSpace(row.RangeStart);
                bool hasEnd = !string.IsNullOrWhiteSpace(row.RangeEnd);

                if (!hasStart && !hasEnd)
                    continue;

                int start, end;

                if (!hasStart || !hasEnd ||
                    !int.TryParse(row.RangeStart.Trim(), out start) ||
                    !int.TryParse(row.RangeEnd.Trim(), out end))
                {
                    MessageBox.Show(
                        this,
                        "Sheet \"" + row.Name + "\" has an incomplete or invalid number range - fill in both a Range Start and Range End (whole numbers), or clear both.",
                        "Create Balloons");
                    return;
                }

                if (start < 1 || end < start)
                {
                    MessageBox.Show(
                        this,
                        "Sheet \"" + row.Name + "\"'s number range is invalid - Range Start must be 1 or greater and no larger than Range End.",
                        "Create Balloons");
                    return;
                }

                ranges.Add(new BalloonNumberRange
                {
                    SheetNames = new List<string> { row.Name },
                    RangeStart = start,
                    RangeEnd = end,
                });
            }

            SelectedSheets = chosen;
            NumberRanges = ranges;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
