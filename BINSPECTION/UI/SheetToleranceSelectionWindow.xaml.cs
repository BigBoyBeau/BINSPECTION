using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using BINSPECTION.Models;

namespace BINSPECTION.UI
{
    // Lets the user define/assign "Sheet Tolerance" sets (grouped
    // decimal-place and angular tolerance values) per sheet. Shown by
    // CommandManagerHandler.OnSheetToleranceSelection; the result is saved
    // into the drawing's ProjectData via PersistenceManager.SaveProject.
    //
    // Used to also have a "Populated" checkbox per sheet that wrote
    // ProjectData.ActiveSheets - removed because it never actually gated
    // which sheets got scanned/ballooned (only the Create Balloons sheet
    // picker's own checkbox does that), so having a second, disconnected
    // "is this sheet active" checkbox here just looked like it controlled
    // scope when it didn't. That picker is now the sole owner of
    // ActiveSheets; see UI/CreateBalloonsSheetSelectionWindow.
    //
    // Kept as plain code-behind acting as its own minimal view-model
    // (DataContext = this) rather than introducing a separate MVVM
    // framework - consistent with the rest of the add-in's UI, which is
    // all plain code-behind too (see UI/BalloonManagerWindow).
    public partial class SheetToleranceSelectionWindow : Window, INotifyPropertyChanged
    {
        // One row of the sheets grid. UI-only - not itself serialized;
        // BuildResult() below translates it into ProjectData's
        // SheetToleranceAssignments shape.
        public class SheetRow
        {
            public string Name { get; set; }
            public SheetToleranceSet AssignedSet { get; set; }
        }

        private readonly List<Characteristic> _existingCharacteristics;

        // Kept so BuildResult() can carry forward ClassOptions/
        // MethodOptions/NumberRanges - this window has no UI for any of
        // those, so a freshly-constructed ProjectData without them would
        // reset each to its class default on every save
        // (PersistenceManager.SaveProject overwrites the whole file, no
        // merge).
        private readonly ProjectData _existing;

        public ObservableCollection<SheetRow> Sheets { get; } = new ObservableCollection<SheetRow>();

        public ObservableCollection<SheetToleranceSet> ToleranceSets { get; } = new ObservableCollection<SheetToleranceSet>();

        private SheetToleranceSet _selectedToleranceSet;
        public SheetToleranceSet SelectedToleranceSet
        {
            get => _selectedToleranceSet;
            set
            {
                _selectedToleranceSet = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedEntries));
                SetNameTextBox.Text = value?.Name ?? string.Empty;
            }
        }

        public ObservableCollection<ToleranceEntry> SelectedEntries => SelectedToleranceSet?.Entries;

        // Inspection-record header fields with no SolidWorks-sourced
        // value - see Models/ReportHeaderSettings.cs for why each of these
        // exists and why "Inspection Date" isn't one of them. Backed by
        // plain fields (not a nested settings object) so each TextBox can
        // bind directly without a wrapper INotifyPropertyChanged type.
        private string _customer;
        public string Customer { get => _customer; set { _customer = value; OnPropertyChanged(); } }

        private string _reportIssuedBy;
        public string ReportIssuedBy { get => _reportIssuedBy; set { _reportIssuedBy = value; OnPropertyChanged(); } }

        private string _lotSize;
        public string LotSize { get => _lotSize; set { _lotSize = value; OnPropertyChanged(); } }

        private string _sampleSizeChart;
        public string SampleSizeChart { get => _sampleSizeChart; set { _sampleSizeChart = value; OnPropertyChanged(); } }

        private string _sampleSize;
        public string SampleSize { get => _sampleSize; set { _sampleSize = value; OnPropertyChanged(); } }

        private string _sampleFrequency;
        public string SampleFrequency { get => _sampleFrequency; set { _sampleFrequency = value; OnPropertyChanged(); } }

        private string _operatorEmployeeNumber;
        public string OperatorEmployeeNumber { get => _operatorEmployeeNumber; set { _operatorEmployeeNumber = value; OnPropertyChanged(); } }

        private string _inspectorEmployeeNumber;
        public string InspectorEmployeeNumber { get => _inspectorEmployeeNumber; set { _inspectorEmployeeNumber = value; OnPropertyChanged(); } }

        private string _cmmReportId;
        public string CmmReportId { get => _cmmReportId; set { _cmmReportId = value; OnPropertyChanged(); } }

        // Null if the user cancelled.
        public ProjectData Result { get; private set; }

        public SheetToleranceSelectionWindow(List<string> sheetNames, ProjectData existing)
        {
            InitializeComponent();
            WindowSizing.FitToScreen(this);
            DataContext = this;

            _existingCharacteristics = existing?.Characteristics ?? new List<Characteristic>();
            _existing = existing;

            ReportHeaderSettings existingHeader = existing?.ReportHeaderSettings ?? new ReportHeaderSettings();
            Customer = existingHeader.Customer;
            ReportIssuedBy = existingHeader.ReportIssuedBy;
            LotSize = existingHeader.LotSize;
            SampleSizeChart = existingHeader.SampleSizeChart;
            SampleSize = existingHeader.SampleSize;
            SampleFrequency = existingHeader.SampleFrequency;
            OperatorEmployeeNumber = existingHeader.OperatorEmployeeNumber;
            InspectorEmployeeNumber = existingHeader.InspectorEmployeeNumber;
            CmmReportId = existingHeader.CmmReportId;

            var setsByName = new Dictionary<string, SheetToleranceSet>();

            if (existing?.ToleranceSets != null)
            {
                foreach (SheetToleranceSet set in existing.ToleranceSets)
                {
                    ToleranceSets.Add(set);
                    setsByName[set.Name] = set;
                }
            }

            foreach (string sheetName in sheetNames ?? new List<string>())
            {
                SheetToleranceSet assigned = null;

                if (existing?.SheetToleranceAssignments != null &&
                    existing.SheetToleranceAssignments.TryGetValue(sheetName, out string setName))
                {
                    setsByName.TryGetValue(setName, out assigned);
                }

                Sheets.Add(new SheetRow
                {
                    Name = sheetName,
                    AssignedSet = assigned,
                });
            }

            if (ToleranceSets.Count > 0)
                SelectedToleranceSet = ToleranceSets[0];
        }

        private void AddSet_Click(object sender, RoutedEventArgs e)
        {
            int n = ToleranceSets.Count + 1;
            string name = "Sheet Tolerance " + n;

            while (ToleranceSets.Any(s => s.Name == name))
            {
                n++;
                name = "Sheet Tolerance " + n;
            }

            SheetToleranceSet set = SheetToleranceSet.CreateDefault(name);

            ToleranceSets.Add(set);
            ToleranceSetsList.SelectedItem = set;
        }

        private void RemoveSet_Click(object sender, RoutedEventArgs e)
        {
            SheetToleranceSet removed = SelectedToleranceSet;

            if (removed == null)
                return;

            ToleranceSets.Remove(removed);

            foreach (SheetRow row in Sheets)
            {
                if (row.AssignedSet == removed)
                    row.AssignedSet = null;
            }

            SheetsGrid.Items.Refresh();

            SelectedToleranceSet = ToleranceSets.FirstOrDefault();
        }

        private void RenameSet_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedToleranceSet == null)
                return;

            string newName = SetNameTextBox.Text?.Trim();

            if (string.IsNullOrEmpty(newName))
            {
                MessageBox.Show(this, "Enter a name for the tolerance set.", "Sheet Tolerance Selection");
                return;
            }

            if (ToleranceSets.Any(s => s != SelectedToleranceSet && s.Name == newName))
            {
                MessageBox.Show(this, "A tolerance set with that name already exists.", "Sheet Tolerance Selection");
                return;
            }

            SelectedToleranceSet.Name = newName;

            ToleranceSetsList.Items.Refresh();
            SheetsGrid.Items.Refresh();
        }

        private void AddEntry_Click(object sender, RoutedEventArgs e)
        {
            SelectedToleranceSet?.Entries.Add(new ToleranceEntry { DecimalPlaces = 1, IsAngular = false, Value = 0 });
        }

        private void RemoveEntry_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedToleranceSet == null)
                return;

            if (EntriesGrid.SelectedItem is ToleranceEntry entry)
                SelectedToleranceSet.Entries.Remove(entry);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var seenNames = new HashSet<string>();

            foreach (SheetToleranceSet set in ToleranceSets)
            {
                if (string.IsNullOrWhiteSpace(set.Name))
                {
                    MessageBox.Show(this, "Every tolerance set needs a name.", "Sheet Tolerance Selection");
                    return;
                }

                if (!seenNames.Add(set.Name))
                {
                    MessageBox.Show(
                        this,
                        "Tolerance set names must be unique: \"" + set.Name + "\" is used more than once.",
                        "Sheet Tolerance Selection");
                    return;
                }
            }

            Result = BuildResult();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private ProjectData BuildResult()
        {
            var data = new ProjectData
            {
                Characteristics = _existingCharacteristics,
                ToleranceSets = ToleranceSets.ToList(),
                SheetToleranceAssignments = new Dictionary<string, string>(),
                ReportHeaderSettings = new ReportHeaderSettings
                {
                    Customer = Customer,
                    ReportIssuedBy = ReportIssuedBy,
                    LotSize = LotSize,
                    SampleSizeChart = SampleSizeChart,
                    SampleSize = SampleSize,
                    SampleFrequency = SampleFrequency,
                    OperatorEmployeeNumber = OperatorEmployeeNumber,
                    InspectorEmployeeNumber = InspectorEmployeeNumber,
                    CmmReportId = CmmReportId,
                },
            };

            if (_existing != null)
            {
                data.ClassOptions = _existing.ClassOptions ?? data.ClassOptions;
                data.MethodOptions = _existing.MethodOptions ?? data.MethodOptions;
                data.NumberRanges = _existing.NumberRanges ?? data.NumberRanges;

                // ActiveSheets belongs to the Create Balloons sheet picker
                // now (this window has no UI for it any more) - carry
                // whatever it last set forward so saving Sheet Tolerance
                // doesn't wipe it back to empty.
                data.ActiveSheets = _existing.ActiveSheets ?? data.ActiveSheets;
            }

            foreach (SheetRow row in Sheets)
            {
                if (row.AssignedSet != null)
                    data.SheetToleranceAssignments[row.Name] = row.AssignedSet.Name;
            }

            return data;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
