using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BINSPECTION.UI
{
    // Shown every time Refresh Balloons is run (CommandManagerHandler.
    // OnRefreshBalloons) so the user picks which sheets get their balloons
    // removed and redrawn THIS run, instead of it silently touching every
    // sheet in the drawing. Deliberately simpler than
    // CreateBalloonsSheetSelectionWindow - no number ranges, since Refresh
    // never hands out a new number, it only redraws balloons already on
    // file.
    public partial class RefreshBalloonsSheetSelectionWindow : Window
    {
        public class SheetRow
        {
            public string Name { get; set; }
            public bool IsChecked { get; set; }
        }

        public ObservableCollection<SheetRow> Sheets { get; } = new ObservableCollection<SheetRow>();

        // Null if the user cancelled.
        public List<string> SelectedSheets { get; private set; }

        public RefreshBalloonsSheetSelectionWindow(
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

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            SheetsGrid.CommitEdit(DataGridEditingUnit.Row, true);

            List<string> chosen = Sheets.Where(r => r.IsChecked).Select(r => r.Name).ToList();

            if (chosen.Count == 0)
            {
                MessageBox.Show(this, "Check at least one sheet to refresh.", "Refresh Balloons");
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
