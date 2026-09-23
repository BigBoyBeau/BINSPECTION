using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using BINSPECTION.Core;
using BINSPECTION.Models;
using SolidWorks.Interop.sldworks;

namespace BINSPECTION.UI
{
    // Replaces the old Re-# behavior (auto-assign the next free number to
    // every checked, previously-De-Numbered row) with a form the user
    // types whatever number they actually want into - "whatever number it
    // needs to be," not just "the next available one." Every row
    // BalloonManagerPanel/BalloonManagerWindow had checked when Re-# was
    // clicked shows up here, ballooned or not, so this also covers what
    // Re-# used to do (give a de-numbered row a number again, possibly a
    // completely different one than it had before) as one case of the
    // general "set this row's number" action - including handing a
    // freed-up number to a totally different row than the one it came
    // from, since De-#'s ghost Characteristic is excluded from collision
    // checks (see DeNumber_Click's remarks in BalloonManagerPanel).
    public partial class RenumberBalloonsWindow : Window
    {
        public class RenumberRow
        {
            public BalloonGridRow Source { get; set; }
            public string DimensionDisplay { get; set; }
            public string OriginalNumber { get; set; }
            public string NewNumber { get; set; }
        }

        private readonly ModelDoc2 _model;
        private readonly string _dataFilePath;
        private readonly ProjectData _projectData;

        public ObservableCollection<RenumberRow> Rows { get; } = new ObservableCollection<RenumberRow>();

        // True if anything was actually applied (even if some other rows
        // in the same batch errored out) - the caller uses this to decide
        // whether to refresh/mark data changed, same as every other
        // BalloonGridService-backed action.
        public bool AnyApplied { get; private set; }

        public RenumberBalloonsWindow(
            ModelDoc2 model,
            string dataFilePath,
            ProjectData projectData,
            List<BalloonGridRow> checkedRows)
        {
            InitializeComponent();
            WindowSizing.FitToScreen(this);
            DataContext = this;

            _model = model;
            _dataFilePath = dataFilePath;
            _projectData = projectData;

            foreach (BalloonGridRow row in checkedRows)
            {
                string currentNumber = row.DisplayNumber ?? "";

                Rows.Add(new RenumberRow
                {
                    Source = row,
                    DimensionDisplay = row.DimensionDisplay,
                    OriginalNumber = currentNumber.Length == 0 ? "(none)" : currentNumber,
                    NewNumber = currentNumber,
                });
            }
        }

        // Applies every row whose New # actually differs from its
        // original number, in grid order, via the same
        // BalloonGridService.TryManualRenumber a single inline edit used
        // to call - so a collision with an existing balloon still offers
        // the same swap-or-cancel choice, and an invalid/blank entry is
        // reported instead of silently ignored or crashing the dialog.
        // Rows that fail are collected and reported together so one bad
        // entry doesn't block every other row in the batch from applying.
        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            RowsGrid.CommitEdit(DataGridEditingUnit.Row, true);

            List<string> errors = new List<string>();
            List<string> warnings = new List<string>();

            foreach (RenumberRow row in Rows)
            {
                string typed = (row.NewNumber ?? "").Trim();
                string original = row.Source.DisplayNumber ?? "";

                if (typed == original)
                    continue;

                if (typed.Length == 0)
                {
                    errors.Add(row.DimensionDisplay + ": cleared - use De-# instead to remove a balloon's number.");
                    continue;
                }

                GridOperationResult result = BalloonGridService.TryManualRenumber(
                    _model,
                    _dataFilePath,
                    row.Source,
                    typed,
                    _projectData,
                    targetNumber => MessageBox.Show(
                        this,
                        "Balloon " + targetNumber + " is already assigned to another characteristic. Swap the two numbers?",
                        "Re-Number Balloons",
                        MessageBoxButton.YesNo) == MessageBoxResult.Yes);

                if (result.Changed)
                {
                    AnyApplied = true;
                    row.OriginalNumber = row.Source.DisplayNumber;
                    row.NewNumber = row.Source.DisplayNumber;
                }

                foreach (string error in result.Errors)
                    errors.Add(row.DimensionDisplay + ": " + error);

                if (result.SaveFailed)
                    errors.Add(row.DimensionDisplay + ": " + result.SaveFailedMessage);

                foreach (string warning in result.Warnings)
                    warnings.Add(row.DimensionDisplay + ": " + warning);
            }

            RowsGrid.Items.Refresh();

            // Applied-with-a-caveat rows (e.g. balloon note not found on the
            // drawing) don't block closing - just make sure they're seen.
            if (warnings.Count > 0)
            {
                MessageBox.Show(
                    this,
                    string.Join(System.Environment.NewLine, warnings),
                    "Re-Number Balloons");
            }

            if (errors.Count > 0)
            {
                MessageBox.Show(
                    this,
                    "Some rows couldn't be renumbered:" + System.Environment.NewLine + System.Environment.NewLine +
                    string.Join(System.Environment.NewLine, errors) + System.Environment.NewLine + System.Environment.NewLine +
                    "Fix them and click Apply again, or Cancel to leave those rows unchanged.",
                    "Re-Number Balloons");

                // Leave the window open so the user can correct the rows
                // that failed - closing here would silently discard
                // whatever they typed for those.
                return;
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = AnyApplied;
        }
    }
}
