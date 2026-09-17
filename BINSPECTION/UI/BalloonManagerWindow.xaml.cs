using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BINSPECTION.Core;
using BINSPECTION.Models;
using SolidWorks.Interop.sldworks;

namespace BINSPECTION.UI
{
    // Single home for reassigning balloon numbers AND editing attributes:
    // Group/Ungroup/Add/Un-Number/Re-Number and manual number entry all act
    // on checkboxes in this grid instead of a SolidWorks graphics-area
    // selection, "Legacy Conversion..." folds in what used to be the
    // separate Match Legacy Numbers command, and the Method/Classification
    // columns fold in what used to be the separate Edit Attributes/Edit All
    // Attributes commands. Every action here applies
    // and saves immediately (see Core/BalloonGridService) rather than
    // being staged behind an OK/Cancel commit, since these are live edits
    // to the drawing's actual balloon annotations - Cancel can't undo
    // those.
    //
    // Kept as plain code-behind acting as its own minimal view-model
    // (DataContext = this), consistent with SheetToleranceSelectionWindow.
    public partial class BalloonManagerWindow : Window
    {
        // One editable grid row. UI-only - Source is the real
        // BalloonGridRow this row represents.
        public class GridRow
        {
            public BalloonGridRow Source { get; set; }
            public bool Selected { get; set; }
            public string DisplayNumber { get; set; }
            public string DimensionDisplay { get; set; }
            public string SheetName { get; set; }
            public bool HasBalloon { get; set; }
            public bool IsUnnumbered { get; set; }
            public string LegacyNumber { get; set; }
            public string ManualNumberInput { get; set; }
            public bool HasCharacteristic { get; set; }
            public string Method { get; set; }
            public string Class { get; set; }
            public bool IsBasic { get; set; }
        }

        private readonly ModelDoc2 _model;
        private readonly DrawingDoc _drawing;
        private readonly string _dataFilePath;
        private readonly ProjectData _projectData;

        public ObservableCollection<string> SheetNames { get; } = new ObservableCollection<string>();

        public ObservableCollection<GridRow> Rows { get; } = new ObservableCollection<GridRow>();

        // Method/Classification option lists - shared with the project's
        // saved options (Models/ProjectData.cs), same type-to-add
        // convention the retired attribute editors used.
        public ObservableCollection<string> MethodOptions { get; } = new ObservableCollection<string>();

        public ObservableCollection<string> ClassOptions { get; } = new ObservableCollection<string>();

        // Bound to the "Set Method"/"Set Classification" combo boxes above
        // the grid (ApplyBulkAttributes_Click) - plain auto-properties are
        // enough since nothing else needs to react to them changing, unlike
        // SelectedSheet.
        public string BulkMethod { get; set; }

        public string BulkClass { get; set; }

        private string _selectedSheet;

        // Clears SolidWorks' own live selection before switching which
        // sheet this window is filtered to. User report (2026-09-16): SW
        // crashed while a balloon selected via RowsGrid_SelectionChanged's
        // ZoomToAndHighlight (a live Select3 on that balloon's annotation)
        // was still selected, then the Sheet filter combo above the grid
        // was changed to a different sheet. RefreshRows below rescans
        // EVERY dimension across the WHOLE drawing (DimensionScanner.
        // GetAllDimensions), regardless of this filter - leaving a stale,
        // now-cross-sheet live selection in place while that full rescan
        // runs is the most likely destabilizing factor, so it's cleared
        // first as a preventive measure even though the exact native
        // failure point wasn't reproduced under a debugger. RefreshRows
        // itself is also now hardened the same way ZoomToAndHighlight is
        // (HandleProcessCorruptedStateExceptions) as a second line of
        // defense.
        public string SelectedSheet
        {
            get => _selectedSheet;
            set
            {
                _selectedSheet = value;

                try
                {
                    _model.ClearSelection2(true);
                }
                catch
                {
                }

                RefreshRows();
            }
        }

        // True once anything in this window has changed the drawing/project
        // data - lets the caller know whether CharacteristicManager needs
        // refreshing after Close.
        public bool DataChanged { get; private set; }

        // Debounces RowsGrid_SelectionChanged's jump-to-dimension work (see
        // its remarks) - a 2026-09-16 user report of the app "not
        // responding" after clicking/arrowing through several already-
        // ballooned rows in a row. Each selection change was firing
        // ZoomToAndHighlight's full ActivateSheet + Select3 +
        // ViewZoomToSelection + GraphicsRedraw2 round-trip into SolidWorks
        // SYNCHRONOUSLY on the UI thread - flipping through rows faster
        // than SolidWorks finishes each one queues those calls up, and the
        // whole app (this window included, since the SolidWorks call blocks
        // the UI thread) appears to hang while it works through the
        // backlog. This is NOT the same failure as
        // [[binspection_balloon_manager_dangling_dimension_crash]] or
        // [[binspection_balloon_manager_sheet_switch_crash]] (both native
        // access violations - HandleProcessCorruptedStateExceptions can't
        // help a hang, there's no exception to catch) - only the LAST
        // selection in a fast sequence should actually trigger the
        // SolidWorks round-trip, which is what this timer enforces.
        private readonly DispatcherTimer _zoomDebounceTimer;

        public BalloonManagerWindow(ModelDoc2 model, DrawingDoc drawing, string dataFilePath, ProjectData projectData)
        {
            InitializeComponent();
            WindowSizing.FitToScreen(this);
            DataContext = this;

            _model = model;
            _drawing = drawing;
            _dataFilePath = dataFilePath;
            _projectData = projectData;

            _zoomDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _zoomDebounceTimer.Tick += ZoomDebounceTimer_Tick;
            Closed += (s, e) => _zoomDebounceTimer.Stop();

            foreach (string sheetName in DrawingSheetHelper.GetSheetNames(drawing))
                SheetNames.Add(sheetName);

            foreach (string option in projectData.MethodOptions)
                MethodOptions.Add(option);

            foreach (string option in projectData.ClassOptions)
                ClassOptions.Add(option);

            string currentSheet = (drawing.GetCurrentSheet() as Sheet)?.GetName();

            _selectedSheet = SheetNames.Contains(currentSheet) ? currentSheet : SheetNames.FirstOrDefault();

            RefreshRows();
        }

        // HandleProcessCorruptedStateExceptions + the app.config policy it
        // requires (see App.config) are what let the catch below actually
        // receive an AccessViolationException/SEHException instead of
        // taking SolidWorks down outright - same defense
        // ZoomToAndHighlight already has (see
        // [[binspection_balloon_manager_dangling_dimension_crash]]), applied
        // here too after a 2026-09-16 report of a crash while switching the
        // Sheet filter combo with a balloon still selected - see
        // SelectedSheet's remarks for the preventive ClearSelection2 this
        // is paired with.
        [HandleProcessCorruptedStateExceptions]
        private void RefreshRows()
        {
            Rows.Clear();

            List<BalloonGridRow> data;

            try
            {
                data = BalloonGridService.BuildRows(_model, _drawing, _projectData, _selectedSheet);
            }
            catch
            {
                MessageBox.Show(
                    this,
                    "Could not refresh the grid - SolidWorks returned an error while rescanning the drawing. " +
                    "Try clicking elsewhere on the sheet to clear any selection, then reopen Balloon Manager.",
                    "Balloon Manager");

                return;
            }

            foreach (BalloonGridRow row in data)
            {
                Rows.Add(new GridRow
                {
                    Source = row,
                    DisplayNumber = row.DisplayNumber,
                    DimensionDisplay = row.DimensionDisplay,
                    SheetName = row.SheetName,
                    HasBalloon = row.HasBalloon,
                    IsUnnumbered = row.IsUnnumbered,
                    LegacyNumber = row.LegacyNumber,
                    HasCharacteristic = row.HasCharacteristic,
                    Method = row.Method,
                    Class = row.Class,
                    IsBasic = row.IsBasic,
                });
            }
        }

        private List<BalloonGridRow> CheckedSourceRows()
        {
            return Rows.Where(r => r.Selected).Select(r => r.Source).ToList();
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            List<BalloonGridRow> checkedRows = CheckedSourceRows();

            if (checkedRows.Count == 0)
            {
                MessageBox.Show(this, "Check one or more unballooned dimensions first.", "Balloon Manager");
                return;
            }

            BalloonGridService.AddBalloons(_model, _dataFilePath, checkedRows, _projectData);
            DataChanged = true;
            RefreshRows();
        }

        private void Group_Click(object sender, RoutedEventArgs e)
        {
            string error = BalloonGridService.GroupBalloons(_model, _dataFilePath, CheckedSourceRows(), _projectData);

            if (error != null)
            {
                MessageBox.Show(this, error, "Balloon Manager");
                return;
            }

            DataChanged = true;
            RefreshRows();
        }

        private void Ungroup_Click(object sender, RoutedEventArgs e)
        {
            string error = BalloonGridService.UngroupBalloons(_model, _dataFilePath, CheckedSourceRows(), _projectData);

            if (error != null)
            {
                MessageBox.Show(this, error, "Balloon Manager");
                return;
            }

            DataChanged = true;
            RefreshRows();
        }

        private void MarkUnnumbered_Click(object sender, RoutedEventArgs e)
        {
            List<BalloonGridRow> checkedRows = CheckedSourceRows();

            if (checkedRows.Count == 0)
            {
                MessageBox.Show(this, "Check one or more balloons/dimensions first.", "Balloon Manager");
                return;
            }

            BalloonGridService.MarkUnnumbered(_model, _dataFilePath, checkedRows, _projectData);
            DataChanged = true;
            RefreshRows();
        }

        private void ReNumber_Click(object sender, RoutedEventArgs e)
        {
            List<BalloonGridRow> checkedRows = CheckedSourceRows();

            if (checkedRows.Count == 0)
            {
                MessageBox.Show(this, "Check one or more unnumbered rows first.", "Balloon Manager");
                return;
            }

            BalloonGridService.ReNumberBalloons(_model, _dataFilePath, checkedRows, _projectData);
            DataChanged = true;
            RefreshRows();
        }

        private void LegacyConversion_Click(object sender, RoutedEventArgs e)
        {
            LegacyConversionWindow window = new LegacyConversionWindow(_model, _dataFilePath, _projectData);

            bool? accepted = window.ShowDialog();

            if (accepted == true)
            {
                DataChanged = true;
                RefreshRows();
            }
        }

        private void RowsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit)
                return;

            GridRow row = e.Row.Item as GridRow;

            if (row == null)
                return;

            if (e.Column == ManualNumberColumn)
            {
                TextBox textBox = e.EditingElement as TextBox;
                string typed = textBox?.Text?.Trim();

                if (string.IsNullOrEmpty(typed))
                    return;

                // Deferred until after this event finishes committing the
                // edit - rebuilding/clearing Rows from inside CellEditEnding
                // itself can conflict with the grid's own in-flight
                // edit-commit handling.
                Dispatcher.BeginInvoke(new Action(() => ApplyManualRenumber(row, typed)));
            }
            else if (e.Column == BasicColumn)
            {
                CheckBox checkBox = e.EditingElement as CheckBox;
                bool isBasic = checkBox?.IsChecked == true;

                // Deferred for the same reason as the manual-renumber
                // branch above - RefreshRows() from inside this event can
                // conflict with the grid's own in-flight edit-commit
                // handling.
                Dispatcher.BeginInvoke(new Action(() => ApplyBasicToggle(row, isBasic)));
            }
        }

        // Method/Classification changes commit via the ComboBoxes'
        // LostFocus instead of CellEditEnding - their DataGridTemplateColumn
        // cells never enter the grid's own "editing" state (the ComboBox is
        // interactive directly in the cell template), so CellEditEnding
        // never fires for them.
        private void AttributeCombo_LostFocus(object sender, RoutedEventArgs e)
        {
            GridRow row = (sender as FrameworkElement)?.DataContext as GridRow;

            if (row != null)
                ApplyAttributes(row);
        }

        // Method/Class never change which/how many rows exist or any
        // balloon's number, so this just persists in place - no need to
        // RefreshRows() and disrupt whatever cell the user is on next.
        private void ApplyAttributes(GridRow row)
        {
            if (!row.HasCharacteristic)
                return;

            BalloonGridService.UpdateAttributes(
                _dataFilePath, row.Source, row.Method, row.Class, _projectData);

            AddIfNew(MethodOptions, row.Method);
            AddIfNew(ClassOptions, row.Class);

            DataChanged = true;
        }

        // Writes the "Set Method"/"Set Classification" boxes to every
        // checked row in one action, instead of opening each row's own
        // combo box and retyping the same value - see BalloonGridService.
        // UpdateAttributesForRows for the actual field-by-field logic
        // (blank box = leave that field alone on every row).
        private void ApplyBulkAttributes_Click(object sender, RoutedEventArgs e)
        {
            List<BalloonGridRow> checkedRows = CheckedSourceRows()
                .Where(r => r.HasCharacteristic)
                .ToList();

            if (checkedRows.Count == 0)
            {
                MessageBox.Show(this, "Check one or more ballooned/numbered rows first.", "Balloon Manager");
                return;
            }

            if (string.IsNullOrWhiteSpace(BulkMethod) && string.IsNullOrWhiteSpace(BulkClass))
            {
                MessageBox.Show(this, "Enter a Method and/or Classification to apply first.", "Balloon Manager");
                return;
            }

            BalloonGridService.UpdateAttributesForRows(
                _dataFilePath, checkedRows, BulkMethod, BulkClass, _projectData);

            AddIfNew(MethodOptions, BulkMethod);
            AddIfNew(ClassOptions, BulkClass);

            DataChanged = true;
            RefreshRows();
        }

        private static void AddIfNew(ObservableCollection<string> options, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            string trimmed = value.Trim();

            if (!options.Any(o => string.Equals(o, trimmed, StringComparison.OrdinalIgnoreCase)))
                options.Add(trimmed);
        }

        private void ApplyManualRenumber(GridRow row, string typed)
        {
            string error;

            bool applied = BalloonGridService.TryManualRenumber(
                _model,
                _dataFilePath,
                row.Source,
                typed,
                _projectData,
                targetNumber => MessageBox.Show(
                    this,
                    "Balloon " + targetNumber + " is already assigned to another characteristic. Swap the two numbers?",
                    "Balloon Manager",
                    MessageBoxButton.YesNo) == MessageBoxResult.Yes,
                out error);

            if (!applied && error != null)
            {
                MessageBox.Show(this, error, "Balloon Manager");
            }

            DataChanged = true;
            RefreshRows();
        }

        // Fires when the user picks a different value in a row's own
        // "Sheet" combo (the per-row move target, not the SheetComboBox
        // filter above the grid) - moves that row's balloon (and any
        // sibling sharing its physical note) onto the picked sheet. Guarded
        // against row.Source.SheetName (the stable pre-move value, not the
        // GridRow's own SheetName the binding already updated) so the
        // initial SelectedItem set during RefreshRows can't itself trigger
        // a spurious move.
        private void SheetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox comboBox = sender as ComboBox;
            GridRow row = comboBox?.DataContext as GridRow;

            if (row?.Source == null)
                return;

            string targetSheet = comboBox.SelectedItem as string;

            if (string.IsNullOrEmpty(targetSheet) ||
                string.Equals(targetSheet, row.Source.SheetName, StringComparison.OrdinalIgnoreCase))
                return;

            string error;

            bool applied = BalloonGridService.MoveBalloonToSheet(
                _model, _dataFilePath, row.Source, targetSheet, _projectData, out error);

            if (!applied && error != null)
            {
                MessageBox.Show(this, error, "Balloon Manager");
            }

            DataChanged = true;
            RefreshRows();
        }

        private void ApplyBasicToggle(GridRow row, bool isBasic)
        {
            string error;

            bool applied = BalloonGridService.SetBasic(
                _model, _dataFilePath, row.Source, isBasic, _projectData, out error);

            if (!applied && error != null)
            {
                MessageBox.Show(this, error, "Balloon Manager");
            }

            DataChanged = true;
            RefreshRows();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // Selecting a grid row (as opposed to just checking its box) jumps
        // SolidWorks to that dimension/balloon, same convenience the SW
        // Inspection add-in's own panel gives - lets the user confirm which
        // physical dimension a row refers to without hunting for it on the
        // sheet. Best-effort: any ordinary failure here (annotation already
        // gone, wrong sheet, etc.) just leaves SW's view/selection alone
        // rather than disrupting the grid - but ResolveAnnotation must never
        // hand back a dangling dimension's own annotation for this to rely
        // on, since Select3-ing one has been observed to crash SolidWorks
        // itself (a native access violation, not a .NET exception the catch
        // below can stop) rather than just fail cleanly.
        //
        // Debounced via _zoomDebounceTimer (see its remarks) rather than
        // calling ZoomToAndHighlight directly here - clicking/arrowing
        // through several rows quickly used to fire one full SolidWorks
        // round-trip per row and back the UI thread up until the app looked
        // hung. Only the row still selected once the timer actually fires
        // (250ms after the LAST selection change) gets the round-trip.
        private void RowsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _zoomDebounceTimer.Stop();
            _zoomDebounceTimer.Start();
        }

        private void ZoomDebounceTimer_Tick(object sender, EventArgs e)
        {
            _zoomDebounceTimer.Stop();

            GridRow row = RowsGrid.SelectedItem as GridRow;

            if (row?.Source != null)
                ZoomToAndHighlight(row.Source);
        }

        // HandleProcessCorruptedStateExceptions + the app.config policy it
        // requires (see App.config) are what let the catch below actually
        // receive an AccessViolationException/SEHException - without both,
        // the CLR treats a native AV from Select3-ing a dangling annotation
        // as an uncatchable corrupted-state exception and SolidWorks itself
        // crashes (see [[binspection_balloon_manager_dangling_dimension_crash]]).
        [HandleProcessCorruptedStateExceptions]
        private void ZoomToAndHighlight(BalloonGridRow source)
        {
            try
            {
                if (!string.IsNullOrEmpty(source.SheetName))
                    DrawingSheetHelper.ActivateSheet(_drawing, source.SheetName);

                Annotation annotation = ResolveAnnotation(source);

                if (annotation == null)
                    return;

                _model.ClearSelection2(true);

                if (!annotation.Select3(false, null))
                    return;

                _model.ViewZoomToSelection();
                _model.GraphicsRedraw2();
            }
            catch
            {
                // Best-effort navigation only - swallow whatever came back
                // (COMException, AccessViolationException, SEHException, ...)
                // and just leave SW's view/selection alone.
            }
        }

        // The dimension itself when this row still has a live, resolvable
        // one (AnnotationSource), otherwise the balloon note already placed
        // for it (a GD&T/note/surface-finish balloon, a dimension whose live
        // annotation didn't resolve this scan, or one flagged
        // !IsDimensionResolved - see its remarks) - same fallback
        // BalloonManager itself uses elsewhere to find a balloon by its
        // displayed number.
        private Annotation ResolveAnnotation(BalloonGridRow source)
        {
            if (source.AnnotationSource != null && source.IsDimensionResolved)
                return source.AnnotationSource.GetAnnotation() as Annotation;

            return ResolveBalloonNote(source.Characteristic);
        }

        private Annotation ResolveBalloonNote(Characteristic characteristic)
        {
            if (characteristic == null || characteristic.IsUnnumbered || characteristic.Number <= 0)
                return null;

            Note note = new BalloonManager().FindExistingBalloon(_model, characteristic.DisplayNumber);

            return note?.GetAnnotation() as Annotation;
        }
    }
}
