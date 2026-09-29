using System;
using System.Linq;
using BINSPECTION.Core;
using BINSPECTION.Models;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace BINSPECTION.CommandManager
{
    // Add Hole Callout / Edit Hole Callout - see Core/HoleCalloutService.cs
    // for what they actually do to the drawing and the sidecar JSON.
    public partial class CommandManagerHandler
    {
        // Label of the note right-click menu item registered in
        // BInspectionAddin.ConnectToSW - shared so the add and remove calls
        // can never drift apart.
        public const string EditHoleCalloutPopupLabel = "Edit Hole Callout (BINSPECTION)";

        // Ribbon/menu command: callout for whatever hole geometry is
        // currently selected. Selection is read live, same as every
        // selection-driven command in this add-in. If the selection is
        // instead an existing BINSPECTION callout (or its balloon), this
        // opens it for editing rather than refusing.
        public void OnAddHoleCallout()
        {
            try
            {
                if (!TryGetSavedDrawing(out ModelDoc2 model, out DrawingDoc drawing, out string dataFilePath))
                    return;

                HoleCalloutService service = new HoleCalloutService(_swApp);

                HoleCalloutSelection selection = service.ReadSelection(model, drawing);

                // Exactly one note selected, and it's one of our callouts
                // (or its balloon): edit it rather than attach a new callout
                // to it.
                if (selection.Picks.Count == 1)
                {
                    INote selectedNote = FirstSelectedNote(model);

                    if (selectedNote != null)
                    {
                        ProjectData projectData = PersistenceManager.LoadProject(dataFilePath);
                        Characteristic anchor = service.FindAnchorForNote(model, projectData, selectedNote);

                        if (anchor != null)
                        {
                            EditHoleCallout(model, drawing, dataFilePath, anchor);
                            return;
                        }
                    }
                }

                if (selection.Picks.Count == 0)
                {
                    _swApp.SendMsgToUser2(
                        "Select what the callout points at first, then run Add Hole Callout:\n\n" +
                        "- any object on the drawing - an edge, face, vertex, sketch entity, dimension, note... " +
                        "(Ctrl-click several for a 4X-style callout; the leader goes to the first one), or\n" +
                        "- a click on an empty spot inside a view, where there's nothing to point at - " +
                        "a cosmetic circle and center mark are drawn there.",
                        (int)swMessageBoxIcon_e.swMbInformation,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                double metersPerUnit =
                    HoleCalloutService.GetMetersPerUnit(model, out string units, out int decimals);

                double? measured = selection.Picks
                    .Where(p => p.DiameterMeters.HasValue)
                    .Select(p => (double?)(p.DiameterMeters.Value / metersPerUnit))
                    .FirstOrDefault();

                HoleCalloutDefinition initial = new HoleCalloutDefinition
                {
                    Type = HoleCalloutType.Drill,
                    Quantity = selection.Picks.Count,
                    Units = units,
                    DecimalPlaces = decimals,
                    MeasuredDiameter = measured,
                    DrillDiameter = measured.HasValue
                        ? Math.Round(measured.Value, decimals, MidpointRounding.AwayFromZero)
                        : (double?)null,
                    DrillDepth = null,
                    CountersinkAngle = units == "mm" ? 90 : 82,
                };

                string header = selection.Describe(metersPerUnit, initial);

                if (selection.IgnoredCount > 0)
                    header += "\n" + selection.IgnoredCount + " other selected item(s) had no location to point at and were ignored.";

                header += "\nThe callout is created without a balloon - balloon it later with Create Balloons or Balloon Manager's Add.";

                BINSPECTION.UI.HoleCalloutWindow window =
                    new BINSPECTION.UI.HoleCalloutWindow(initial, false, header, metersPerUnit);

                if (window.ShowDialog() != true || window.Result == null)
                    return;

                GridOperationResult result =
                    service.Create(model, drawing, dataFilePath, window.Result, selection, metersPerUnit);

                ShowHoleCalloutResult(result);

                if (result.Changed)
                    _taskPaneHostControl?.ReloadFromDisk();
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("OnAddHoleCallout", ex);

                _swApp.SendMsgToUser2(
                    ex.ToString(),
                    (int)swMessageBoxIcon_e.swMbStop,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
        }

        // Edit Hole Callout - the ribbon command AND the note right-click
        // item. Works on whatever is selected on the drawing: the callout
        // note itself or its balloon.
        public void OnEditHoleCallout()
        {
            try
            {
                if (!TryGetSavedDrawing(out ModelDoc2 model, out DrawingDoc drawing, out string dataFilePath))
                    return;

                INote selectedNote = FirstSelectedNote(model);

                if (selectedNote == null)
                {
                    _swApp.SendMsgToUser2(
                        "Select the hole callout on the drawing first (click its note or its balloon), then run Edit Hole Callout.",
                        (int)swMessageBoxIcon_e.swMbInformation,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                ProjectData projectData = PersistenceManager.LoadProject(dataFilePath);

                Characteristic anchor =
                    new HoleCalloutService(_swApp).FindAnchorForNote(model, projectData, selectedNote);

                if (anchor == null)
                {
                    _swApp.SendMsgToUser2(
                        "That note isn't a BINSPECTION hole callout. Only callouts created with Add Hole Callout " +
                        "(or their balloons) can be edited this way.",
                        (int)swMessageBoxIcon_e.swMbInformation,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                EditHoleCallout(model, drawing, dataFilePath, anchor);
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("OnEditHoleCallout", ex);

                _swApp.SendMsgToUser2(
                    ex.ToString(),
                    (int)swMessageBoxIcon_e.swMbStop,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
        }

        private void EditHoleCallout(ModelDoc2 model, DrawingDoc drawing, string dataFilePath, Characteristic anchor)
        {
            double metersPerUnit = HoleCalloutService.GetMetersPerUnit(model, out string _, out int _);

            string header =
                "Editing hole callout " +
                (anchor.IsUnnumbered ? "(un-numbered)"
                    : HoleCalloutService.IsAwaitingBalloon(anchor) ? "(not ballooned yet)"
                    : anchor.DisplayNumber) +
                (string.IsNullOrEmpty(anchor.SheetName) ? "" : " on " + anchor.SheetName) +
                ". Saving rewrites the note and regenerates its characteristic rows - Method/Classification are kept per value.";

            BINSPECTION.UI.HoleCalloutWindow window =
                new BINSPECTION.UI.HoleCalloutWindow(anchor.HoleCallout, true, header, metersPerUnit);

            if (window.ShowDialog() != true || window.Result == null)
                return;

            GridOperationResult result = new HoleCalloutService(_swApp).Edit(
                model, drawing, dataFilePath, anchor.PersistentRefId, window.Result, metersPerUnit);

            ShowHoleCalloutResult(result);

            if (result.Changed)
                _taskPaneHostControl?.ReloadFromDisk();
        }

        private void ShowHoleCalloutResult(GridOperationResult result)
        {
            string message = result.Message;

            if (string.IsNullOrEmpty(message))
                return;

            _swApp.SendMsgToUser2(
                message,
                result.HasErrors
                    ? (int)swMessageBoxIcon_e.swMbWarning
                    : (int)swMessageBoxIcon_e.swMbInformation,
                (int)swMessageBoxBtn_e.swMbOk);
        }

        // Same active-drawing checks every command does, plus a saved
        // drawing - a callout's rows live in the sidecar JSON, which has no
        // path until the drawing is saved (same rule as Balloon Manager).
        private bool TryGetSavedDrawing(out ModelDoc2 model, out DrawingDoc drawing, out string dataFilePath)
        {
            model = _swApp.ActiveDoc as ModelDoc2;
            drawing = model as DrawingDoc;
            dataFilePath = null;

            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocDRAWING || drawing == null)
            {
                _swApp.SendMsgToUser2(
                    "Open a drawing first.",
                    (int)swMessageBoxIcon_e.swMbStop,
                    (int)swMessageBoxBtn_e.swMbOk);

                return false;
            }

            dataFilePath = PersistenceManager.GetDataFilePath(model);

            if (string.IsNullOrEmpty(dataFilePath))
            {
                _swApp.SendMsgToUser2(
                    "This drawing hasn't been saved yet - save it first so the hole callout's inspection data has somewhere to live.",
                    (int)swMessageBoxIcon_e.swMbStop,
                    (int)swMessageBoxBtn_e.swMbOk);

                return false;
            }

            return true;
        }

        private static INote FirstSelectedNote(ModelDoc2 model)
        {
            SelectionMgr selMgr = model.SelectionManager as SelectionMgr;

            if (selMgr == null)
                return null;

            int count = selMgr.GetSelectedObjectCount2(-1);

            for (int i = 1; i <= count; i++)
            {
                if (selMgr.GetSelectedObjectType3(i, -1) != (int)swSelectType_e.swSelNOTES)
                    continue;

                INote note = selMgr.GetSelectedObject6(i, -1) as INote;

                if (note != null)
                    return note;
            }

            return null;
        }
    }
}
