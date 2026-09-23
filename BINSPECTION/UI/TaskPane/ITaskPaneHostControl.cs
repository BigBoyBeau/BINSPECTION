using System.Runtime.InteropServices;

namespace BINSPECTION.UI.TaskPane
{
    // Deliberately empty. SolidWorks activates TaskPaneHostControl purely
    // by ProgID (ITaskpaneView.AddControl) and hosts it as an ActiveX
    // control - it never calls back into a custom interface method of
    // ours. This interface exists only so ClassInterfaceType.None on
    // TaskPaneHostControl gives regasm a small, deliberate COM contract
    // instead of auto-exposing UserControl's entire inherited public
    // surface (Site, ContextMenuStrip, every event, ...).
    //
    // The methods BInspectionAddIn/CommandManagerHandler actually call
    // (ShowBalloonManager, ReloadFromDisk) are plain public members on the
    // concrete TaskPaneHostControl class below, called through a direct
    // in-process .NET reference rather than through this interface - so
    // they aren't constrained to COM-visible parameter types (ProjectData
    // isn't COM-visible, and doesn't need to be).
    [ComVisible(true)]
    [Guid("87CD3B62-44A6-4240-B3AF-DBA44A2ACE76")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    public interface ITaskPaneHostControl
    {
    }
}
