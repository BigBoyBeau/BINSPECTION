using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BINSPECTION.UI
{
    // Shown every time Legacy Conversion is run (BalloonManagerWindow.
    // LegacyConversion_Click) so the user picks which sheets' balloons are
    // eligible for conversion THIS run, instead of every characteristic in
    // the whole drawing being offered as a match/renumber target regardless
    // of which sheet it's actually on. Mirrors
    // RefreshBalloonsSheetSelectionWindow - no number ranges, since Legacy
    // Conversion never hands out a brand new automatic number the way
    // Create Balloons does (a "new" row just takes the next free one).
    public partial class LegacyConversionSheetSelectionWindow : Window
    {
        public class SheetRow
        {
            public string Name { get; set; }
            public bool IsChecked { get; set; }
        }

        public ObservableCollection<SheetRow> Sheets { get; } = new ObservableCollection<SheetRow>();

        // Null if the user cancelled.
        public List<string> SelectedSheets { get; private set; }

        public LegacyConversionSheetSelectionWindow(
            List<string> sheetNames,
            IEnumerable<string> previouslySelected)
        {
            InitializeComponent();
            WindowSizing.FitToScreen(this);
            DataContext = this;

            HashSet<string> selected =
                previouslySelected != null
                    ? new HashSet<string>(previouslySelected)
                    : new HashSet<string>();

            foreach (string name in sheetNames ?? new List<string>())
            {
                Sheets.Add(new SheetRow
                {
                    Name = name,
                    IsChecked = selected.Contains(name),
                });
            }
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

        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            SheetsGrid.CommitEdit(DataGridEditingUnit.Row, true);

            List<string> chosen = Sheets.Where(r => r.IsChecked).Select(r => r.Name).ToList();

            if (chosen.Count == 0)
            {
                MessageBox.Show(this, "Check at least one sheet to convert.", "Legacy Conversion");
                return;
            }

            SelectedSheets = chosen;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
