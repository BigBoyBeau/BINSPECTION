using System.ComponentModel;
using BINSPECTION.Core;

namespace BINSPECTION.UI.TaskPane
{
    // UI-facing wrapper around a real BalloonGridService.BalloonGridRow,
    // same role BalloonManagerWindow's nested GridRow class played - Source
    // holds the real row every BalloonGridService call operates on, the
    // other properties mirror it for binding. Unlike GridRow, this
    // implements INotifyPropertyChanged: a plain ItemsControl (used here in
    // place of DataGrid, since the Task Pane is too narrow for a
    // multi-column grid) has no built-in edit-commit/refresh mechanics of
    // its own, so the checkbox/expand/combo bindings need real change
    // notification to update the UI.
    public class BalloonRowViewModel : INotifyPropertyChanged
    {
        public BalloonGridRow Source { get; }

        public BalloonRowViewModel(BalloonGridRow source)
        {
            Source = source;
            _method = source.Method;
            _class = source.Class;
            _isBasic = source.IsBasic;
            _sheetName = source.SheetName;
        }

        private bool _selected;
        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value)
                    return;

                _selected = value;
                OnPropertyChanged(nameof(Selected));
            }
        }

        private string _method;
        public string Method
        {
            get => _method;
            set
            {
                if (_method == value)
                    return;

                _method = value;
                OnPropertyChanged(nameof(Method));
            }
        }

        private string _class;
        public string Class
        {
            get => _class;
            set
            {
                if (_class == value)
                    return;

                _class = value;
                OnPropertyChanged(nameof(Class));
            }
        }

        private bool _isBasic;
        public bool IsBasic
        {
            get => _isBasic;
            set
            {
                if (_isBasic == value)
                    return;

                _isBasic = value;
                OnPropertyChanged(nameof(IsBasic));
            }
        }

        private string _sheetName;
        public string SheetName
        {
            get => _sheetName;
            set
            {
                if (_sheetName == value)
                    return;

                _sheetName = value;
                OnPropertyChanged(nameof(SheetName));
            }
        }

        public string DisplayNumber => Source.DisplayNumber;
        public string DimensionDisplay => Source.DimensionDisplay;
        public bool HasBalloon => Source.HasBalloon;
        public bool HasCharacteristic => Source.HasCharacteristic;
        public bool IsUnnumbered => Source.IsUnnumbered;
        public string LegacyBalloonNumber => Source.LegacyBalloonNumber;

        // A row with a live dimension that couldn't be resolved this scan -
        // almost always a dangling reference. Never select this
        // annotation directly (see BalloonManagerPanel.ResolveAnnotation);
        // shown to the user as a warning icon instead.
        public bool HasWarning => Source.AnnotationSource != null && !Source.IsDimensionResolved;

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
