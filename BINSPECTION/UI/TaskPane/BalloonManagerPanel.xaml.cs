using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using BINSPECTION.Core;
using BINSPECTION.Models;
using BINSPECTION.Settings;
using SolidWorks.Interop.sldworks;

namespace BINSPECTION.UI.TaskPane
{
    // The Task Pane's real content - same role BalloonManagerWindow played
    // as a floating window, rebuilt as a WPF UserControl hosted in
    // SolidWorks' own docked Task Pane (see TaskPaneHostControl). Every
    // mutating action below calls the exact same BalloonGridService methods
    // BalloonManagerWindow did - only the View changed, not the Core logic
    // those methods apply to the live drawing.
    //
    // BalloonRowViewModel implements INotifyPropertyChanged (mirroring
    // BalloonManagerWindow's GridRow) and this class does too (DataContext =
    // this, same "plain code-behind as its own minimal view-model"
    // convention used throughout this project's other windows).
    public partial class BalloonManagerPanel : UserControl, INotifyPropertyChanged
    {
        private ModelDoc2 _model;
        private DrawingDoc _drawing;
        private string _dataFilePath;
        private ProjectData _projectData;

        // Debounces the jump-to-dimension SolidWorks round-trip the same
        // way BalloonManagerWindow's _zoomDebounceTimer did - expanding/
        // collapsing rows quickly would otherwise queue up one full
        // ActivateSheet+Select3+ViewZoomToSelection+GraphicsRedraw2 call per
        // row and make the add-in appear to hang.
        private readonly DispatcherTimer _zoomDebounceTimer;
        private BalloonGridRow _pendingZoomSource;

        public ObservableCollection<string> SheetNames { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> MethodOptions { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> ClassOptions { get; } = new ObservableCollection<string>();
        public ObservableCollection<BalloonRowViewModel> Rows { get; } = new ObservableCollection<BalloonRowViewModel>();

        private string _selectedSheet;
        public string SelectedSheet
        {
            get => _selectedSheet;
            set
            {
                if (_selectedSheet == value)
                    return;

                _selectedSheet = value;
                OnPropertyChanged(nameof(SelectedSheet));
                OnPropertyChanged(nameof(SubtitleText));

                try
                {
                    _model?.ClearSelection2(true);
                }
                catch
                {
                }

                RefreshRows();
            }
        }

        private bool _snapToSelectionEnabled;
        public bool SnapToSelectionEnabled
        {
            get => _snapToSelectionEnabled;
            set
            {
                if (_snapToSelectionEnabled == value)
                    return;

                _snapToSelectionEnabled = value;
                OnPropertyChanged(nameof(SnapToSelectionEnabled));
            }
        }

        public bool HasSelection => Rows.Any(r => r.Selected);

        public string CheckedCountText
        {
            get
            {
                int count = Rows.Count(r => r.Selected);
                return count == 1 ? "1 selected" : $"{count} selected";
            }
        }

        public string SubtitleText => string.IsNullOrEmpty(SelectedSheet) ? "No drawing" : SelectedSheet;

        public string FooterText
        {
            get
            {
                int checkedCount = Rows.Count(r => r.Selected);
                string text = $"{Rows.Count} characteristics shown";
                return checkedCount > 0 ? text + $" · {checkedCount} selected" : text;
            }
        }

        // True once anything here has changed the drawing/project data -
        // mirrors BalloonManagerWindow.DataChanged, same purpose (letting
        // the caller know whether CharacteristicManager needs refreshing).
        public bool DataChanged { get; private set; }

        // The other BINSPECTION commands, reachable from this panel instead
        // of only from the ribbon - each one needs _swApp (which this WPF
        // layer never holds), so these bubble up through TaskPaneHostControl
        // to CommandManagerHandler, which runs the exact same On* method the
        // ribbon button already does. Legacy Conversion doesn't need this -
        // it opens directly below, the same way BalloonManagerWindow always
        // did, since this panel already holds the model/data context it needs.
        public event EventHandler OpenSheetTolerancesRequested;
        public event EventHandler CreateBalloonsRequested;
        public event EventHandler RemoveBalloonsRequested;
        public event EventHandler DeleteAllBalloonsRequested;
        public event EventHandler SavePositionRequested;
        public event EventHandler RestorePositionRequested;
        public event EventHandler GenerateReportRequested;

        // "Reset" re-runs the exact same active-document lookup and fresh
        // disk load OnOpenBalloonManager already does every time this panel
        // is shown - the panel is a long-lived singleton (see LoadContext's
        // remarks), so nothing re-syncs it automatically if the user
        // switches SOLIDWORKS documents without reopening Balloon Manager,
        // which can leave rows from a previously viewed drawing on screen
        // even though the now-active drawing's own JSON has no balloon
        // data at all. This button exists to force that re-sync manually.
        public event EventHandler ResetRequested;

        public BalloonManagerPanel()
        {
            InitializeComponent();
            DataContext = this;

            _snapToSelectionEnabled = SettingsManager.Load().SnapToSelectionEnabled;

            _zoomDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _zoomDebounceTimer.Tick += ZoomDebounceTimer_Tick;

            MouseWheel += RootPanel_MouseWheel;
        }

        // Unlike a normal top-level WPF window, this panel is hosted via
        // ElementHost inside SOLIDWORKS' own native Task Pane control - a
        // mouse wheel event that reaches this handler still unhandled
        // (nothing scrollable underneath the cursor consumed it, or the
        // balloon grid's own ScrollViewer was already at its top/bottom
        // limit) would otherwise fall through the hosted control and get
        // read by SOLIDWORKS itself as a zoom command on the drawing view
        // behind the panel. Bubbling MouseWheel fires here last, after any
        // scrollable control inside the panel (the grid) already had its
        // normal chance to consume it, so this only swallows whatever's
        // genuinely left over.
        private void RootPanel_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
        }

        // Called by TaskPaneHostControl (via CommandManagerHandler.
        // OnOpenBalloonManager) each time the user opens Balloon Manager -
        // the Task Pane itself is a singleton created once in ConnectToSW,
        // so unlike BalloonManagerWindow's constructor this can run more
        // than once against a different document.
        public void LoadContext(ModelDoc2 model, DrawingDoc drawing, string dataFilePath, ProjectData projectData)
        {
            _model = model;
            _drawing = drawing;
            _dataFilePath = dataFilePath;
            _projectData = projectData;
            DataChanged = false;

            SheetNames.Clear();
            foreach (string sheetName in DrawingSheetHelper.GetSheetNames(drawing))
                SheetNames.Add(sheetName);

            MethodOptions.Clear();
            foreach (string option in projectData.MethodOptions ?? new System.Collections.Generic.List<string>())
                MethodOptions.Add(option);

            ClassOptions.Clear();
            foreach (string option in projectData.ClassOptions ?? new System.Collections.Generic.List<string>())
                ClassOptions.Add(option);

            string currentSheet = (drawing.GetCurrentSheet() as Sheet)?.GetName();

            _selectedSheet = SheetNames.Contains(currentSheet) ? currentSheet : SheetNames.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedSheet));
            OnPropertyChanged(nameof(SubtitleText));

            RefreshRows();
        }

        // Re-reads the sidecar JSON from disk - same reasoning as
        // BalloonManagerWindow.ReloadFromDisk: this panel is a singleton
        // that can stay "open" (docked) indefinitely, so it needs a way to
        // notice a change some other command made underneath it. Mutates
        // _projectData's own properties in place, same as the window did,
        // since CommandManagerHandler's caller holds the same ProjectData
        // reference.
        public void ReloadFromDisk()
        {
            if (string.IsNullOrEmpty(_dataFilePath))
                return;

            ProjectData fresh = PersistenceManager.LoadProject(_dataFilePath);

            _projectData.Characteristics = fresh.Characteristics;
            _projectData.ActiveSheets = fresh.ActiveSheets;
            _projectData.NumberRanges = fresh.NumberRanges;
            _projectData.ToleranceSets = fresh.ToleranceSets;
            _projectData.SheetToleranceAssignments = fresh.SheetToleranceAssignments;
            _projectData.ReportHeaderSettings = fresh.ReportHeaderSettings;
            _projectData.ClassOptions = fresh.ClassOptions;
            _projectData.MethodOptions = fresh.MethodOptions;

            MethodOptions.Clear();
            foreach (string option in _projectData.MethodOptions)
                MethodOptions.Add(option);

            ClassOptions.Clear();
            foreach (string option in _projectData.ClassOptions)
                ClassOptions.Add(option);

            DataChanged = true;
            RefreshRows();
        }

        [HandleProcessCorruptedStateExceptions]
        private void RefreshRows()
        {
            Rows.Clear();

            if (_model == null || _drawing == null || _projectData == null)
            {
                OnPropertyChanged(nameof(FooterText));
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(CheckedCountText));
                return;
            }

            System.Collections.Generic.List<BalloonGridRow> data;

            try
            {
                data = BalloonGridService.BuildRows(_model, _drawing, _projectData, _selectedSheet);
            }
            catch
            {
                MessageBox.Show(
                    "Could not refresh Balloon Manager - SolidWorks returned an error while rescanning the drawing. " +
                    "Try clicking elsewhere on the sheet to clear any selection, then reopen Balloon Manager.",
                    "Balloon Manager");

                OnPropertyChanged(nameof(FooterText));
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(CheckedCountText));
                return;
            }

            foreach (BalloonGridRow row in data)
            {
                BalloonRowViewModel rowViewModel = new BalloonRowViewModel(row);
                rowViewModel.PropertyChanged += Row_PropertyChanged;
                Rows.Add(rowViewModel);
            }

            OnPropertyChanged(nameof(FooterText));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(CheckedCountText));
        }

        private void Row_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BalloonRowViewModel.Selected))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(CheckedCountText));
                OnPropertyChanged(nameof(FooterText));
            }
        }

        private System.Collections.Generic.List<BalloonGridRow> CheckedSourceRows()
        {
            return Rows.Where(r => r.Selected).Select(r => r.Source).ToList();
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            System.Collections.Generic.List<BalloonGridRow> checkedRows = CheckedSourceRows();

            if (checkedRows.Count == 0)
            {
                MessageBox.Show("Check one or more unballooned dimensions first.", "Balloon Manager");
                return;
            }

            ReportResult(BalloonGridService.AddBalloons(_model, _dataFilePath, checkedRows, _projectData));
            RefreshRows();
        }

        private void Group_Click(object sender, RoutedEventArgs e)
        {
            if (ReportResult(BalloonGridService.GroupBalloons(_model, _dataFilePath, CheckedSourceRows(), _projectData)))
                RefreshRows();
        }

        private void Ungroup_Click(object sender, RoutedEventArgs e)
        {
            if (ReportResult(BalloonGridService.UngroupBalloons(_model, _dataFilePath, CheckedSourceRows(), _projectData)))
                RefreshRows();
        }

        // Shows whatever a BalloonGridService action needs to tell the user
        // (a refusal, per-row failures, warnings, a failed save) and
        // returns whether it actually changed anything.
        private bool ReportResult(GridOperationResult result)
        {
            if (result.Message != null)
                MessageBox.Show(result.Message, "Balloon Manager");

            if (result.Changed)
                DataChanged = true;

            return result.Changed;
        }

        // "De-#" (was "Un-#") - detaches the checked balloon(s) from their
        // number: the balloon note is deleted and the number is freed for
        // reuse (BalloonGridService.MarkUnnumbered underneath, unchanged),
        // but the underlying Characteristic record - and its Method/
        // Classification - is kept, not deleted, so the same
        // dimension/GD&T frame can be given a number again later. The
        // freed number itself isn't tied to this row any more: TryManual
        // Renumber's collision check explicitly skips IsUnnumbered rows,
        // so the freed number can be typed into any OTHER checked row via
        // Re-# without hitting a "number already in use" error.
        private void DeNumber_Click(object sender, RoutedEventArgs e)
        {
            System.Collections.Generic.List<BalloonGridRow> checkedRows = CheckedSourceRows();

            if (checkedRows.Count == 0)
            {
                MessageBox.Show("Check one or more balloons/dimensions first.", "Balloon Manager");
                return;
            }

            ReportResult(BalloonGridService.MarkUnnumbered(_model, _dataFilePath, checkedRows, _projectData));
            RefreshRows();
        }

        // Opens a form to type each checked row's balloon number directly,
        // instead of the old behavior of silently auto-assigning the next
        // free number - see RenumberBalloonsWindow's remarks.
        private void ReNumber_Click(object sender, RoutedEventArgs e)
        {
            System.Collections.Generic.List<BalloonGridRow> checkedRows = CheckedSourceRows();

            if (checkedRows.Count == 0)
            {
                MessageBox.Show("Check one or more balloons first.", "Balloon Manager");
                return;
            }

            RenumberBalloonsWindow window = new RenumberBalloonsWindow(_model, _dataFilePath, _projectData, checkedRows);
            bool? accepted = window.ShowDialog();

            if (accepted == true)
            {
                DataChanged = true;
                RefreshRows();
            }
        }

        private void LegacyConversion_Click(object sender, RoutedEventArgs e)
        {
            DrawingDoc drawing = _model as DrawingDoc;

            System.Collections.Generic.List<string> sheetNames = DrawingSheetHelper.GetSheetNames(drawing);

            if (sheetNames.Count == 0)
            {
                MessageBox.Show("No sheets were found in this drawing.", "Legacy Conversion");
                return;
            }

            System.Collections.Generic.List<string> previouslySelected =
                _projectData?.ActiveSheets != null && _projectData.ActiveSheets.Count > 0
                    ? _projectData.ActiveSheets
                    : sheetNames;

            LegacyConversionSheetSelectionWindow sheetPicker =
                new LegacyConversionSheetSelectionWindow(sheetNames, previouslySelected);

            bool? sheetsChosen = sheetPicker.ShowDialog();

            if (sheetsChosen != true || sheetPicker.SelectedSheets == null)
                return;

            LegacyConversionWindow window =
                new LegacyConversionWindow(_model, _dataFilePath, _projectData, sheetPicker.SelectedSheets);

            bool? accepted = window.ShowDialog();

            if (accepted == true)
            {
                DataChanged = true;
                RefreshRows();
            }
        }

        private void ApplyBulkAttributes_Click(object sender, RoutedEventArgs e)
        {
            System.Collections.Generic.List<BalloonGridRow> checkedRows = CheckedSourceRows()
                .Where(r => r.HasCharacteristic)
                .ToList();

            string bulkMethod = BulkMethodCombo.Text;
            string bulkClass = BulkClassCombo.Text;

            if (checkedRows.Count == 0)
            {
                MessageBox.Show("Check one or more ballooned/numbered rows first.", "Balloon Manager");
                return;
            }

            if (string.IsNullOrWhiteSpace(bulkMethod) && string.IsNullOrWhiteSpace(bulkClass))
            {
                MessageBox.Show("Enter a Method and/or Classification to apply first.", "Balloon Manager");
                return;
            }

            ReportResult(BalloonGridService.UpdateAttributesForRows(
                _dataFilePath, checkedRows, bulkMethod, bulkClass, _projectData));

            AddIfNew(MethodOptions, bulkMethod);
            AddIfNew(ClassOptions, bulkClass);

            RefreshRows();
        }

        private void ClearSelection_Click(object sender, RoutedEventArgs e)
        {
            foreach (BalloonRowViewModel row in Rows)
                row.Selected = false;
        }

        private void SheetTolerances_Click(object sender, RoutedEventArgs e)
        {
            OpenSheetTolerancesRequested?.Invoke(this, EventArgs.Empty);
        }

        private void CreateBalloons_Click(object sender, RoutedEventArgs e)
        {
            CreateBalloonsRequested?.Invoke(this, EventArgs.Empty);
        }

        private void RemoveBalloons_Click(object sender, RoutedEventArgs e)
        {
            RemoveBalloonsRequested?.Invoke(this, EventArgs.Empty);
        }

        private void DeleteAllBalloons_Click(object sender, RoutedEventArgs e)
        {
            DeleteAllBalloonsRequested?.Invoke(this, EventArgs.Empty);
        }

        private void SavePosition_Click(object sender, RoutedEventArgs e)
        {
            SavePositionRequested?.Invoke(this, EventArgs.Empty);
        }

        private void RestorePosition_Click(object sender, RoutedEventArgs e)
        {
            RestorePositionRequested?.Invoke(this, EventArgs.Empty);
        }

        private void GenerateReport_Click(object sender, RoutedEventArgs e)
        {
            GenerateReportRequested?.Invoke(this, EventArgs.Empty);
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            ResetRequested?.Invoke(this, EventArgs.Empty);
        }

        private static void AddIfNew(ObservableCollection<string> options, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            string trimmed = value.Trim();

            if (!options.Any(o => string.Equals(o, trimmed, StringComparison.OrdinalIgnoreCase)))
                options.Add(trimmed);
        }

        // Replaces the old expand-to-see-more-then-zoom behavior: every
        // column is visible all the time now, so selecting a row in the
        // grid (rather than expanding it) is what queues the jump-to-
        // dimension zoom, still debounced the same way.
        //
        // SelectedItem also changes for reasons that aren't a genuine user
        // click - most visibly, scrolling the grid with a row already
        // selected would re-fire this (row containers get recycled by
        // virtualization as they scroll past, and the recycled container
        // for the still-selected item re-applies IsSelected), snapping the
        // SW view back to that dimension on every scroll tick instead of
        // just once on click. ZoomDebounceTimer_Tick below clears
        // SelectedItem right after it actually zooms, so the row goes back
        // to its unselected look immediately and there's nothing left
        // "selected" for a later scroll to re-trigger.
        private void BalloonsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BalloonRowViewModel row = BalloonsGrid.SelectedItem as BalloonRowViewModel;

            if (row == null)
                return;

            _pendingZoomSource = row.Source;
            _zoomDebounceTimer.Stop();
            _zoomDebounceTimer.Start();
        }

        // Checking the row's own "Selected" bulk checkbox (used for Group/
        // Apply to Checked) should also jump to that dimension, same as
        // clicking the row itself - but it's a separate gesture from the
        // grid's full-row highlight, so it needs its own trigger rather
        // than relying on SelectionChanged. Click, not Checked/Unchecked,
        // for the same reason as BasicCheckBox_Click: Checked fires from
        // the TwoWay binding push whenever a row is (re)built with
        // Selected already true, not just from a real click.
        private void SelectedCheckBox_Click(object sender, RoutedEventArgs e)
        {
            BalloonRowViewModel row = (sender as FrameworkElement)?.Tag as BalloonRowViewModel;

            if (row == null)
                return;

            _pendingZoomSource = row.Source;
            _zoomDebounceTimer.Stop();
            _zoomDebounceTimer.Start();
        }

        private void MethodOrClass_LostFocus(object sender, RoutedEventArgs e)
        {
            BalloonRowViewModel row = (sender as FrameworkElement)?.Tag as BalloonRowViewModel;

            if (row == null || !row.HasCharacteristic)
                return;

            ReportResult(BalloonGridService.UpdateAttributes(_dataFilePath, row.Source, row.Method, row.Class, _projectData));

            AddIfNew(MethodOptions, row.Method);
            AddIfNew(ClassOptions, row.Class);
        }

        // Guarded against row.Source.SheetName (the stable pre-move value)
        // rather than the bound SheetName the ComboBox just set, same as
        // BalloonManagerWindow.SheetCombo_SelectionChanged - otherwise the
        // initial SelectedItem set while this row is built would itself
        // look like a move.
        private void SheetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox comboBox = sender as ComboBox;
            BalloonRowViewModel row = comboBox?.Tag as BalloonRowViewModel;

            if (row?.Source == null)
                return;

            string targetSheet = comboBox.SelectedItem as string;

            if (string.IsNullOrEmpty(targetSheet) ||
                string.Equals(targetSheet, row.Source.SheetName, StringComparison.OrdinalIgnoreCase))
                return;

            ReportResult(BalloonGridService.MoveBalloonToSheet(
                _model, _dataFilePath, row.Source, targetSheet, _projectData));

            // Always rebuilt, even on failure, so the row's Sheet combo
            // snaps back to wherever the balloon actually is.
            RefreshRows();
        }

        // Click, not Checked/Unchecked - CheckBox.IsChecked is bound
        // TwoWay, and Checked/Unchecked fire on ANY change to IsChecked,
        // including the one WPF raises the moment data-binding first sets
        // it while RefreshRows() below is still populating rows below.
        // That made every already-Basic row's checkbox re-fire this
        // handler purely from being displayed - which called SetBasic
        // (drawing a real SOLIDWORKS rectangle-sketch box, complete with
        // the "click first/last corner" status-bar prompt this class of
        // bug is named for below) and then RefreshRows() again, which
        // rebuilt every row and re-triggered the same thing for whichever
        // Basic rows hadn't been "consumed" yet - a real feedback loop,
        // not just something slow, and exactly what made a large drawing
        // with a lot of Basic dimensions look stuck cycling/drawing boxes
        // after Create Balloons handed off to this panel. Click only fires
        // from a genuine mouse/keyboard interaction (ButtonBase.OnClick),
        // never from a binding push, so this same handler is now safe to
        // leave wired to a bound-and-rebuilt CheckBox.
        private void BasicCheckBox_Click(object sender, RoutedEventArgs e)
        {
            BalloonRowViewModel row = (sender as FrameworkElement)?.Tag as BalloonRowViewModel;

            if (row == null)
                return;

            ReportResult(BalloonGridService.SetBasic(
                _model, _dataFilePath, row.Source, row.IsBasic, _projectData));

            // Always rebuilt, even on failure, so the Basic checkbox snaps
            // back to the dimension's real state.
            RefreshRows();
        }

        private void SnapToggle_Changed(object sender, RoutedEventArgs e)
        {
            AppSettings settings = SettingsManager.Load();
            settings.SnapToSelectionEnabled = SnapToSelectionEnabled;
            SettingsManager.Save(settings);
        }

        private void ZoomDebounceTimer_Tick(object sender, EventArgs e)
        {
            _zoomDebounceTimer.Stop();

            if (SnapToSelectionEnabled && _pendingZoomSource != null)
                ZoomToAndHighlight(_pendingZoomSource);

            _pendingZoomSource = null;

            // Drop the grid's own full-row highlight now that the jump
            // actually happened, so the row looks unselected again right
            // away instead of staying highlighted and available for a
            // later scroll-driven virtualization event to re-select and
            // re-queue another zoom (see BalloonsGrid_SelectionChanged's
            // remarks).
            BalloonsGrid.SelectedItem = null;
        }

        // HandleProcessCorruptedStateExceptions + the app.config policy it
        // requires (App.config's legacyCorruptedStateExceptionsPolicy) are
        // what let the catch below actually receive an
        // AccessViolationException/SEHException rather than the CLR
        // treating a native AV from Select3-ing a dangling annotation as an
        // uncatchable corrupted-state exception - same defense
        // BalloonManagerWindow.ZoomToAndHighlight relied on.
        [HandleProcessCorruptedStateExceptions]
        private void ZoomToAndHighlight(BalloonGridRow source)
        {
            try
            {
                if (!string.IsNullOrEmpty(source.SheetName))
                    DrawingSheetHelper.ActivateSheet(_drawing, source.SheetName);

                IAnnotation annotation = ResolveAnnotation(source);

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
                // and leave SW's view/selection alone.
            }
        }

        // Never hands back a dangling dimension's own annotation - see
        // BalloonGridRow.IsDimensionResolved's remarks. Falls back to the
        // balloon note already placed for this characteristic instead,
        // found without cycling through every sheet - see
        // BalloonGridService.FindBalloonAnnotation.
        private IAnnotation ResolveAnnotation(BalloonGridRow source)
        {
            if (source.AnnotationSource != null && source.IsDimensionResolved)
                return source.AnnotationSource.GetAnnotation() as IAnnotation;

            return BalloonGridService.FindBalloonAnnotation(_model, source.Characteristic);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
