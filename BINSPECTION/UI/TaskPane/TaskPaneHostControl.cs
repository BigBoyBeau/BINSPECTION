using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using BINSPECTION.Models;
using SolidWorks.Interop.sldworks;

namespace BINSPECTION.UI.TaskPane
{
    // Bridges SolidWorks' Task Pane (which hosts a COM ActiveX control,
    // activated by ProgID via ITaskpaneView.AddControl) to the real WPF
    // content in BalloonManagerPanel. This is the first WinForms control
    // and the first WPF UserControl in the project - every existing window
    // is a plain WPF Window shown directly (ShowDialog/Show), none of which
    // need to cross a COM activation boundary the way a Task Pane control
    // does.
    //
    // ClassInterfaceType.None + the small explicit ITaskPaneHostControl
    // interface (rather than relying on the implicit AutoDispatch default
    // BInspectionAddin uses for ISwAddin) is deliberate: UserControl's huge
    // inherited public surface (Control/Component's events, Site,
    // ContextMenuStrip, ...) is a much larger, less controlled COM contract
    // than the small purpose-built interface SolidWorks itself expects for
    // ISwAddin - None avoids exposing all of that to regasm.
    [ComVisible(true)]
    [Guid("AA892036-54B2-415A-9753-080E6A9A5C8F")]
    [ProgId("BINSPECTION.TaskPaneHostControl")]
    [ClassInterface(ClassInterfaceType.None)]
    public class TaskPaneHostControl : UserControl, ITaskPaneHostControl
    {
        private readonly BalloonManagerPanel _panel;

        // Public parameterless constructor is required for COM activation
        // by ProgID (ITaskpaneView.AddControl).
        public TaskPaneHostControl()
        {
            _panel = new BalloonManagerPanel();

            ElementHost elementHost = new ElementHost
            {
                Dock = DockStyle.Fill,
                Child = _panel,
            };

            Controls.Add(elementHost);
        }

        // The methods below are called directly through this concrete
        // class (BInspectionAddIn/CommandManagerHandler hold a
        // TaskPaneHostControl reference, cast straight from
        // ITaskpaneView.AddControl's return value) - never through COM/
        // IDispatch, so they aren't part of ITaskPaneHostControl and aren't
        // constrained to COM-visible parameter types (ProjectData isn't
        // COM-visible, and doesn't need to be).
        public void ShowBalloonManager(
            ModelDoc2 model,
            DrawingDoc drawing,
            string dataFilePath,
            ProjectData projectData)
        {
            _panel.LoadContext(model, drawing, dataFilePath, projectData);
        }

        public void ReloadFromDisk()
        {
            _panel.ReloadFromDisk();
        }

        public bool DataChanged => _panel.DataChanged;

        // Pass-through for the panel's "submenu" nav events - see
        // BalloonManagerPanel's remarks on OpenSheetTolerancesRequested
        // etc. Same not-part-of-ITaskPaneHostControl reasoning as the
        // methods above: only ever subscribed to directly, in-process, by
        // CommandManagerHandler.SetTaskPane.
        public event EventHandler OpenSheetTolerancesRequested
        {
            add { _panel.OpenSheetTolerancesRequested += value; }
            remove { _panel.OpenSheetTolerancesRequested -= value; }
        }

        public event EventHandler CreateBalloonsRequested
        {
            add { _panel.CreateBalloonsRequested += value; }
            remove { _panel.CreateBalloonsRequested -= value; }
        }

        public event EventHandler RemoveBalloonsRequested
        {
            add { _panel.RemoveBalloonsRequested += value; }
            remove { _panel.RemoveBalloonsRequested -= value; }
        }

        public event EventHandler DeleteAllBalloonsRequested
        {
            add { _panel.DeleteAllBalloonsRequested += value; }
            remove { _panel.DeleteAllBalloonsRequested -= value; }
        }

        public event EventHandler SavePositionRequested
        {
            add { _panel.SavePositionRequested += value; }
            remove { _panel.SavePositionRequested -= value; }
        }

        public event EventHandler RestorePositionRequested
        {
            add { _panel.RestorePositionRequested += value; }
            remove { _panel.RestorePositionRequested -= value; }
        }

        public event EventHandler GenerateReportRequested
        {
            add { _panel.GenerateReportRequested += value; }
            remove { _panel.GenerateReportRequested -= value; }
        }

        public event EventHandler ResetRequested
        {
            add { _panel.ResetRequested += value; }
            remove { _panel.ResetRequested -= value; }
        }
    }
}
