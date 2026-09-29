using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BINSPECTION.Core;
using BINSPECTION.Models;

namespace BINSPECTION.UI
{
    // The one dialog behind both Add Hole Callout and Edit Hole Callout:
    // type, size (table-driven defaults - see HoleSizeTable), quantity,
    // depths/THRU, thread class, and an optional per-value tolerance. Every
    // edit re-runs HoleCalloutBuilder, so the grid of characteristics and
    // the note preview always show exactly what Create/Save will produce.
    // Plain code-behind with named controls, same convention as this
    // project's other windows; the caller reads Result after ShowDialog.
    public partial class HoleCalloutWindow : Window
    {
        public class ValueRow : INotifyPropertyChanged
        {
            private string _text;
            private string _plus;
            private string _minus;

            public string Key { get; set; }

            public bool CanTolerance { get; set; }

            // Where this row lands under the balloon: "N" for the first
            // (the balloon's own number), ".1", ".2"... after it.
            public string Position { get; set; }

            public string Text
            {
                get => _text;
                set { _text = value; Changed(nameof(Text)); }
            }

            public string Plus
            {
                get => _plus;
                set { _plus = value; Changed(nameof(Plus)); }
            }

            public string Minus
            {
                get => _minus;
                set { _minus = value; Changed(nameof(Minus)); }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private static readonly (HoleCalloutType Type, string Label)[] Types =
        {
            (HoleCalloutType.Drill, "Drill"),
            (HoleCalloutType.Tapped, "Tapped"),
            (HoleCalloutType.Counterbore, "Counterbore"),
            (HoleCalloutType.Countersink, "Countersink"),
            (HoleCalloutType.Spotface, "Spotface"),
        };

        private static readonly Dictionary<string, string> PreviewSymbols = new Dictionary<string, string>
        {
            { "<MOD-DIAM>", "⌀" },
            { "<HOLE-DEPTH>", "▽" },
            { "<HOLE-SINK>", "⌵" },
            { "<HOLE-CBORE>", "⌴" },
            { "<HOLE-SPOT>", "SF" },
            { "<MOD-DEG>", "°" },
            { "<MOD-PM>", "±" },
        };

        private readonly double _metersPerUnit;
        private readonly string _units;
        private readonly double? _measuredDiameter;

        // Tolerances typed so far, by value key - survives the grid being
        // rebuilt when a field change adds/removes rows.
        private readonly Dictionary<string, HoleCalloutTolerance> _tolerances =
            new Dictionary<string, HoleCalloutTolerance>();

        // Row order the user set with Move Up/Down (value keys) - see
        // HoleCalloutDefinition.RowOrder.
        private List<string> _rowOrder = new List<string>();

        // Remembered styles folder (AppSettings.HoleCalloutStyleFolder).
        private string _styleFolder;

        private bool _loading = true;

        public ObservableCollection<ValueRow> Values { get; } = new ObservableCollection<ValueRow>();

        public HoleCalloutDefinition Result { get; private set; }

        public HoleCalloutWindow(
            HoleCalloutDefinition initial,
            bool isEdit,
            string headerText,
            double metersPerUnit)
        {
            InitializeComponent();
            WindowSizing.FitToScreen(this);
            DataContext = this;

            _metersPerUnit = metersPerUnit;
            _units = initial.Units ?? "in";
            _measuredDiameter = initial.MeasuredDiameter;

            Title = isEdit ? "Edit Hole Callout" : "Add Hole Callout";
            OkButton.Content = isEdit ? "Save" : "Create";
            HeaderText.Text = (headerText ?? "") +
                (string.IsNullOrEmpty(headerText) ? "" : "\n") +
                "Values are in " + (_units == "mm" ? "millimeters" : "inches") + ".";

            foreach (var type in Types)
                TypeCombo.Items.Add(type.Label);

            for (int i = 0; i <= 5; i++)
                DecimalsCombo.Items.Add(i.ToString(CultureInfo.InvariantCulture));

            foreach (HoleCalloutTolerance tol in initial.Tolerances ?? new List<HoleCalloutTolerance>())
                _tolerances[tol.Key] = tol;

            _rowOrder = new List<string>(initial.RowOrder ?? new List<string>());

            WriteForm(initial);

            _styleFolder = Settings.SettingsManager.Load().HoleCalloutStyleFolder;
            PopulateStyles(initial.Type);

            _loading = false;

            Refresh(rebuildValues: true);
        }

        // ---- Form <-> definition ----

        private void WriteForm(HoleCalloutDefinition def)
        {
            bool wasLoading = _loading;
            _loading = true;

            TypeCombo.SelectedIndex = Array.FindIndex(Types, t => t.Type == def.Type);
            PopulateSizes(def.Type);
            SizeCombo.Text = def.SizeName ?? "";

            QuantityBox.Text = def.Quantity.ToString(CultureInfo.InvariantCulture);
            DecimalsCombo.SelectedIndex = Math.Max(0, Math.Min(5, def.DecimalPlaces));

            DrillDiameterBox.Text = Format(def, def.DrillDiameter);
            SetDepth(def, DrillDepthBox, DrillThruCheck, def.DrillDepth);

            ThreadDesignationBox.Text = def.ThreadDesignation ?? "";
            ThreadClassBox.Text = def.ThreadClass ?? "";
            SetDepth(def, ThreadDepthBox, ThreadThruCheck, def.ThreadDepth);

            AddCountersinkCheck.IsChecked = def.AddCountersink;

            CounterboreDiameterBox.Text = Format(def, def.CounterboreDiameter);
            CounterboreDepthBox.Text = Format(def, def.CounterboreDepth);
            CountersinkDiameterBox.Text = Format(def, def.CountersinkDiameter);
            CountersinkAngleBox.Text = def.CountersinkAngle?.ToString(CultureInfo.InvariantCulture) ?? "";
            SpotfaceDiameterBox.Text = Format(def, def.SpotfaceDiameter);
            SpotfaceDepthBox.Text = Format(def, def.SpotfaceDepth);

            UpdateVisibility(def.Type);

            _loading = wasLoading;
        }

        private HoleCalloutDefinition ReadForm()
        {
            HoleCalloutType type = SelectedType();

            HoleCalloutDefinition def = new HoleCalloutDefinition
            {
                Type = type,
                SizeName = SizeCombo.Text?.Trim(),
                Units = _units,
                DecimalPlaces = DecimalsCombo.SelectedIndex >= 0 ? DecimalsCombo.SelectedIndex : 3,
                Quantity = int.TryParse(QuantityBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int qty) ? qty : 0,
                DrillDiameter = Parse(DrillDiameterBox.Text),
                DrillDepth = DrillThruCheck.IsChecked == true ? (double?)null : Parse(DrillDepthBox.Text) ?? -1,
                AddCountersink = AddCountersinkCheck.IsChecked == true,
                MeasuredDiameter = _measuredDiameter,
            };

            def.RowOrder = new List<string>(_rowOrder);

            if (type == HoleCalloutType.Tapped)
            {
                def.ThreadDesignation = ThreadDesignationBox.Text?.Trim();
                def.ThreadClass = ThreadClassBox.Text?.Trim();
                def.ThreadDepth = ThreadThruCheck.IsChecked == true ? (double?)null : Parse(ThreadDepthBox.Text) ?? -1;
            }

            if (type == HoleCalloutType.Counterbore)
            {
                def.CounterboreDiameter = Parse(CounterboreDiameterBox.Text);
                def.CounterboreDepth = Parse(CounterboreDepthBox.Text);
            }

            if (HoleCalloutBuilder.HasCountersink(def))
            {
                def.CountersinkDiameter = Parse(CountersinkDiameterBox.Text);
                def.CountersinkAngle = Parse(CountersinkAngleBox.Text);
            }

            if (type == HoleCalloutType.Spotface)
            {
                def.SpotfaceDiameter = Parse(SpotfaceDiameterBox.Text);
                def.SpotfaceDepth = Parse(SpotfaceDepthBox.Text);
            }

            // Only tolerances for values this callout actually has, and
            // only real ones.
            HashSet<string> keys = new HashSet<string>(HoleCalloutBuilder.BuildRows(def).Select(r => r.Key));

            def.Tolerances = _tolerances.Values
                .Where(t => keys.Contains(t.Key) && (t.Plus != 0 || t.Minus != 0))
                .ToList();

            return def;
        }

        // ---- Refresh ----

        private void Refresh(bool rebuildValues)
        {
            if (_loading)
                return;

            HoleCalloutDefinition def = ReadForm();
            List<HoleCalloutRow> rows = HoleCalloutBuilder.BuildRows(def);

            bool sameKeys = Values.Select(v => v.Key).SequenceEqual(rows.Select(r => r.Key));

            if (rebuildValues || !sameKeys)
            {
                foreach (ValueRow old in Values)
                    old.PropertyChanged -= ValueRow_PropertyChanged;

                Values.Clear();

                foreach (HoleCalloutRow row in rows)
                {
                    _tolerances.TryGetValue(row.Key, out HoleCalloutTolerance tol);

                    ValueRow valueRow = new ValueRow
                    {
                        Key = row.Key,
                        Text = row.Text,
                        CanTolerance = row.Value.Nominal.HasValue,
                        Position = Values.Count == 0 ? "N" : "." + Values.Count,
                        Plus = tol != null && tol.Plus != 0 ? tol.Plus.ToString(CultureInfo.InvariantCulture) : "",
                        Minus = tol != null && tol.Minus != 0 ? Math.Abs(tol.Minus).ToString(CultureInfo.InvariantCulture) : "",
                    };

                    valueRow.PropertyChanged += ValueRow_PropertyChanged;
                    Values.Add(valueRow);
                }
            }
            else
            {
                for (int i = 0; i < rows.Count; i++)
                    Values[i].Text = rows[i].Text;
            }

            string noteText = HoleCalloutBuilder.BuildNoteText(def);

            foreach (KeyValuePair<string, string> symbol in PreviewSymbols)
                noteText = noteText.Replace(symbol.Key, symbol.Value);

            PreviewText.Text = noteText;

            List<string> errors = HoleCalloutBuilder.Validate(def);
            errors.AddRange(ToleranceErrors());

            ErrorText.Text = string.Join("\n", errors);
            OkButton.IsEnabled = errors.Count == 0;
        }

        private void ValueRow_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ValueRow.Plus) && e.PropertyName != nameof(ValueRow.Minus))
                return;

            ValueRow row = (ValueRow)sender;

            double plus = Parse(row.Plus) ?? 0;
            double minus = Parse(row.Minus) ?? 0;

            // The "− Tol" column takes a magnitude ("0.005" means -0.005);
            // a typed minus sign is accepted too.
            _tolerances[row.Key] = new HoleCalloutTolerance { Key = row.Key, Plus = plus, Minus = -Math.Abs(minus) };

            Refresh(rebuildValues: false);
        }

        private IEnumerable<string> ToleranceErrors()
        {
            foreach (ValueRow row in Values.Where(v => v.CanTolerance))
            {
                if (!string.IsNullOrWhiteSpace(row.Plus) && !Parse(row.Plus).HasValue)
                    yield return "\"" + row.Text + "\": + Tol isn't a number.";

                if (!string.IsNullOrWhiteSpace(row.Minus) && !Parse(row.Minus).HasValue)
                    yield return "\"" + row.Text + "\": − Tol isn't a number.";
            }
        }

        // ---- Events ----

        private void TypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
                return;

            HoleCalloutDefinition def = ReadForm();
            string previousSize = def.SizeName;

            _loading = true;
            PopulateSizes(def.Type);
            PopulateStyles(def.Type);
            _loading = false;

            // Carry the size across the switch when it maps (tapped
            // "1/4-20 UNC" <-> clearance "1/4"), otherwise suggest one from
            // the measured edge diameter.
            string carried = MapSize(previousSize, def.Type) ?? SuggestSize(def.Type);

            if (carried != null && SizeCombo.Items.Contains(carried))
            {
                ApplySize(def, carried);
            }
            else
            {
                WriteForm(def);
            }

            Refresh(rebuildValues: true);
        }

        private void SizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || SizeCombo.SelectedItem == null)
                return;

            ApplySize(ReadForm(), (string)SizeCombo.SelectedItem);
            Refresh(rebuildValues: true);
        }

        private void Decimals_SelectionChanged(object sender, SelectionChangedEventArgs e) => Refresh(rebuildValues: false);

        private void Field_Changed(object sender, TextChangedEventArgs e) => Refresh(rebuildValues: false);

        private void Thru_Click(object sender, RoutedEventArgs e)
        {
            DrillDepthBox.IsEnabled = DrillThruCheck.IsChecked != true;
            ThreadDepthBox.IsEnabled = ThreadThruCheck.IsChecked != true;

            UpdateVisibility(SelectedType());
            Refresh(rebuildValues: false);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            HoleCalloutDefinition def = ReadForm();
            List<string> errors = HoleCalloutBuilder.Validate(def).Concat(ToleranceErrors()).ToList();

            if (errors.Count > 0)
            {
                MessageBox.Show(string.Join("\n", errors), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = def;
            DialogResult = true;
        }

        // ---- Styles ----

        private void StyleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
                return;

            HoleCalloutStyleEntry entry = StyleCombo.SelectedItem as HoleCalloutStyleEntry;

            if (entry != null)
                ApplyStyleFile(entry.Path);
        }

        private void LoadStyle_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Load Hole Callout Style",
                Filter = HoleCalloutStyle.FileFilter,
                InitialDirectory = ExistingStyleFolder(),
            };

            if (dialog.ShowDialog(this) != true)
                return;

            RememberStyleFolder(System.IO.Path.GetDirectoryName(dialog.FileName));
            ApplyStyleFile(dialog.FileName);
        }

        private void SaveStyle_Click(object sender, RoutedEventArgs e)
        {
            HoleCalloutDefinition def = ReadForm();
            List<string> errors = HoleCalloutBuilder.Validate(def).Concat(ToleranceErrors()).ToList();

            if (errors.Count > 0)
            {
                MessageBox.Show("Fix these before saving the style:\n\n" + string.Join("\n", errors),
                    Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save Hole Callout Style",
                Filter = HoleCalloutStyle.FileFilter,
                DefaultExt = HoleCalloutStyle.FileExtension,
                InitialDirectory = ExistingStyleFolder(),
                FileName = HoleCalloutStyleService.SuggestFileName(def),
            };

            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                HoleCalloutStyleService.Save(dialog.FileName, def);
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("HoleCalloutWindow.SaveStyle", ex);
                MessageBox.Show("The style couldn't be saved: " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // A style saved into a subfolder (e.g. per customer) still
            // belongs to the same styles library - only move the remembered
            // folder when the file was saved outside it.
            string savedFolder = System.IO.Path.GetDirectoryName(dialog.FileName);

            if (string.IsNullOrEmpty(_styleFolder) ||
                !savedFolder.StartsWith(_styleFolder, StringComparison.OrdinalIgnoreCase))
                RememberStyleFolder(savedFolder);

            PopulateStyles(def.Type, dialog.FileName);
        }

        private void ApplyStyleFile(string path)
        {
            HoleCalloutStyle style;

            try
            {
                style = HoleCalloutStyleService.Load(path);
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("HoleCalloutWindow.ApplyStyleFile", ex);
                MessageBox.Show("That style file couldn't be read: " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            HoleCalloutDefinition def = HoleCalloutStyleService.ApplyTo(style, ReadForm());

            _tolerances.Clear();

            foreach (HoleCalloutTolerance tol in def.Tolerances ?? new List<HoleCalloutTolerance>())
                _tolerances[tol.Key] = tol;

            _rowOrder = new List<string>(def.RowOrder ?? new List<string>());

            WriteForm(def);

            bool wasLoading = _loading;
            _loading = true;
            PopulateStyles(def.Type, path);
            _loading = wasLoading;

            Refresh(rebuildValues: true);
        }

        // Styles in the remembered folder for this type; `selectPath`
        // pre-selects one (the style just applied or saved).
        private void PopulateStyles(HoleCalloutType type, string selectPath = null)
        {
            bool wasLoading = _loading;
            _loading = true;

            StyleCombo.Items.Clear();

            List<HoleCalloutStyleEntry> entries = HoleCalloutStyleService.List(_styleFolder, type);

            foreach (HoleCalloutStyleEntry entry in entries)
                StyleCombo.Items.Add(entry);

            StyleCombo.SelectedItem = entries.FirstOrDefault(
                s => string.Equals(s.Path, selectPath, StringComparison.OrdinalIgnoreCase));

            StyleCombo.IsEnabled = entries.Count > 0;

            string typeLabel = Types.First(t => t.Type == type).Label;

            StyleFolderText.Text = string.IsNullOrEmpty(_styleFolder)
                ? "No styles folder yet - use Load Style... or Save Style... once and it's remembered."
                : entries.Count == 0
                    ? "No " + typeLabel + " styles in " + _styleFolder
                    : entries.Count + " " + typeLabel + " style(s) in " + _styleFolder;

            _loading = wasLoading;
        }

        private void RememberStyleFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder))
                return;

            _styleFolder = folder;

            // Load-modify-save so other settings (e.g. Balloon Manager's
            // snap toggle) are never clobbered.
            Settings.AppSettings settings = Settings.SettingsManager.Load();
            settings.HoleCalloutStyleFolder = folder;
            Settings.SettingsManager.Save(settings);
        }

        private string ExistingStyleFolder() =>
            !string.IsNullOrEmpty(_styleFolder) && System.IO.Directory.Exists(_styleFolder) ? _styleFolder : null;

        private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelectedRow(-1);

        private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelectedRow(+1);

        // Moves the selected characteristic row one place and remembers the
        // whole order, so it survives later field edits and is saved with
        // the callout (the first row becomes the balloon's own number).
        private void MoveSelectedRow(int delta)
        {
            ValueRow selected = ValuesGrid.SelectedItem as ValueRow;

            if (selected == null)
            {
                MessageBox.Show("Click a row in the list first, then move it.", Title);
                return;
            }

            List<string> keys = Values.Select(v => v.Key).ToList();
            int index = keys.IndexOf(selected.Key);
            int target = index + delta;

            if (index < 0 || target < 0 || target >= keys.Count)
                return;

            keys[index] = keys[target];
            keys[target] = selected.Key;
            _rowOrder = keys;

            Refresh(rebuildValues: true);

            ValueRow moved = Values.FirstOrDefault(v => v.Key == selected.Key);

            if (moved != null)
            {
                ValuesGrid.SelectedItem = moved;
                ValuesGrid.ScrollIntoView(moved);
            }
        }

        // ---- Helpers ----

        private void ApplySize(HoleCalloutDefinition def, string sizeName)
        {
            if (def.Type == HoleCalloutType.Tapped)
            {
                ThreadSize thread = HoleSizeTable.FindThread(sizeName);

                if (thread != null)
                    HoleCalloutBuilder.ApplyThreadDefaults(def, thread, _metersPerUnit);
            }
            else
            {
                ScrewSize screw = HoleSizeTable.FindScrew(sizeName);

                if (screw != null)
                    HoleCalloutBuilder.ApplyScrewDefaults(def, screw, _metersPerUnit);
            }

            def.SizeName = sizeName;

            WriteForm(def);
        }

        private string MapSize(string previousSize, HoleCalloutType newType)
        {
            if (string.IsNullOrEmpty(previousSize))
                return null;

            if (newType == HoleCalloutType.Tapped)
            {
                // Clearance "1/4" -> the first (coarse) thread for that screw.
                return HoleSizeTable.ThreadSizes.FirstOrDefault(t => t.Screw.Name == previousSize)?.Designation ??
                       HoleSizeTable.FindThread(previousSize)?.Designation;
            }

            return HoleSizeTable.FindThread(previousSize)?.Screw.Name ??
                   HoleSizeTable.FindScrew(previousSize)?.Name;
        }

        private string SuggestSize(HoleCalloutType type)
        {
            if (!_measuredDiameter.HasValue)
                return null;

            if (type == HoleCalloutType.Tapped)
                return HoleSizeTable.ClosestThreadByTapDrill(_measuredDiameter.Value, _metersPerUnit)?.Designation;

            if (type == HoleCalloutType.Drill)
                return null;

            return HoleSizeTable.ClosestScrewByClearance(_measuredDiameter.Value, _metersPerUnit)?.Name;
        }

        private void PopulateSizes(HoleCalloutType type)
        {
            SizeCombo.Items.Clear();

            IEnumerable<string> names = type == HoleCalloutType.Tapped
                ? HoleSizeTable.ThreadSizes.Select(t => t.Designation)
                : HoleSizeTable.ScrewSizes.Select(s => s.Name);

            foreach (string name in names)
                SizeCombo.Items.Add(name);
        }

        private void UpdateVisibility(HoleCalloutType type)
        {
            ThreadGroup.Visibility = Show(type == HoleCalloutType.Tapped);
            CounterboreGroup.Visibility = Show(type == HoleCalloutType.Counterbore);
            SpotfaceGroup.Visibility = Show(type == HoleCalloutType.Spotface);

            bool canAddCountersink = type == HoleCalloutType.Drill || type == HoleCalloutType.Tapped;
            AddCountersinkCheck.Visibility = Show(canAddCountersink);

            CountersinkGroup.Visibility = Show(
                type == HoleCalloutType.Countersink || (canAddCountersink && AddCountersinkCheck.IsChecked == true));

            HoleGroup.Header = type == HoleCalloutType.Tapped ? "Tap drill" : "Hole";

            DrillDepthBox.IsEnabled = DrillThruCheck.IsChecked != true;
            ThreadDepthBox.IsEnabled = ThreadThruCheck.IsChecked != true;
        }

        private HoleCalloutType SelectedType() =>
            TypeCombo.SelectedIndex >= 0 ? Types[TypeCombo.SelectedIndex].Type : HoleCalloutType.Drill;

        private static void SetDepth(HoleCalloutDefinition def, TextBox box, CheckBox thru, double? depth)
        {
            thru.IsChecked = !depth.HasValue;
            box.Text = depth.HasValue ? Format(def, depth) : "";
            box.IsEnabled = depth.HasValue;
        }

        private static string Format(HoleCalloutDefinition def, double? value) =>
            value.HasValue ? HoleCalloutBuilder.FormatLength(def, value.Value) : "";

        private static double? Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                return value;

            return null;
        }

        private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
    }
}
