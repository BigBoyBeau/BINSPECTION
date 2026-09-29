using System;
using System.Collections.Generic;
using System.Linq;
using BINSPECTION.Models;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace BINSPECTION.Core
{
    public enum HoleCalloutPickKind
    {
        // Anything real the user selected - an edge, face, vertex, sketch
        // entity, dimension, note, center mark... The callout doesn't have
        // to be on a hole; the leader attaches to whatever it is.
        Object,

        // A click on empty space inside a drawing view - there's nothing to
        // attach to, so a cosmetic circle + center mark is drawn here and
        // the leader attaches to that.
        Point,
    }

    public class HoleCalloutPick
    {
        public HoleCalloutPickKind Kind { get; set; }

        // swSelectType_e of the selected item (Object picks).
        public int SelectType { get; set; }

        // Sheet-space selection point (meters) from
        // ISelectionMgr.GetSelectionPoint2 - where the user clicked.
        public double[] SheetPoint { get; set; }

        // Circular model edges only: true diameter in meters.
        public double? DiameterMeters { get; set; }
    }

    // What the user had selected when Add Hole Callout ran.
    public class HoleCalloutSelection
    {
        public List<HoleCalloutPick> Picks { get; } = new List<HoleCalloutPick>();

        public View View { get; set; }

        public string SheetName { get; set; }

        public int IgnoredCount { get; set; }

        public string Describe(double metersPerUnit, HoleCalloutDefinition def)
        {
            int objects = Picks.Count(p => p.Kind == HoleCalloutPickKind.Object);
            int points = Picks.Count(p => p.Kind == HoleCalloutPickKind.Point);

            List<string> parts = new List<string>();

            if (objects > 0) parts.Add(objects + (objects == 1 ? " object" : " objects"));
            if (points > 0) parts.Add(points + (points == 1 ? " point (cosmetic circle)" : " points (cosmetic circles)"));

            string text = "Picked " + string.Join(", ", parts);

            string viewName = null;
            try { viewName = View?.GetName2(); } catch { }

            if (!string.IsNullOrEmpty(viewName))
                text += " in " + viewName;

            if (!string.IsNullOrEmpty(SheetName))
                text += " (" + SheetName + ")";

            HoleCalloutPick measured = Picks.FirstOrDefault(p => p.DiameterMeters.HasValue);

            if (measured != null)
                text += " - measured ⌀ " + HoleCalloutBuilder.FormatLength(def, measured.DiameterMeters.Value / metersPerUnit);

            return text;
        }
    }

    // Everything Add Hole Callout / Edit Hole Callout does to the live
    // drawing and the sidecar JSON. The callout note is the ANCHOR
    // characteristic's source (its annotation's persistent reference is
    // the anchor's PersistentRefId); every value after the first is a "#n"
    // sibling sharing the one balloon - the exact shape Create Balloons
    // gives a native hole callout, so Restore/Refresh Balloons, the grid,
    // and the report handle it with no special cases. Because the anchor's
    // PersistentRefId is the note's, Create Balloons' own HasCharacteristic
    // check skips the note on every later run instead of ballooning it a
    // second time as a free-standing note.
    public class HoleCalloutService
    {
        // Note placed up and to the right of the picked point, sheet meters.
        private const double NoteOffsetX = 0.015;
        private const double NoteOffsetY = 0.012;

        private const double ThinLineWidthMeters = 0.0001;

        private readonly ISldWorks _swApp;

        public HoleCalloutService(ISldWorks swApp)
        {
            _swApp = swApp;
        }

        // ---- Units ----

        // The drawing's linear display units as meters-per-unit, plus the
        // "in"/"mm" tag stored on the definition. Anything that isn't an
        // imperial unit is treated as metric for formatting purposes.
        public static double GetMetersPerUnit(ModelDoc2 model, out string unitsTag, out int defaultDecimals)
        {
            int unit = (int)swLengthUnit_e.swINCHES;
            int decimals = -1;

            try
            {
                unit = model.Extension.GetUserPreferenceInteger(
                    (int)swUserPreferenceIntegerValue_e.swUnitsLinear,
                    (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified);

                decimals = model.Extension.GetUserPreferenceInteger(
                    (int)swUserPreferenceIntegerValue_e.swUnitsLinearDecimalPlaces,
                    (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified);
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("HoleCalloutService.GetMetersPerUnit", ex);
            }

            double metersPerUnit;
            bool imperial;

            switch ((swLengthUnit_e)unit)
            {
                case swLengthUnit_e.swMM: metersPerUnit = 0.001; imperial = false; break;
                case swLengthUnit_e.swCM: metersPerUnit = 0.01; imperial = false; break;
                case swLengthUnit_e.swMETER: metersPerUnit = 1.0; imperial = false; break;
                case swLengthUnit_e.swMICRON: metersPerUnit = 0.000001; imperial = false; break;
                case swLengthUnit_e.swNANOMETER: metersPerUnit = 0.000000001; imperial = false; break;
                case swLengthUnit_e.swANGSTROM: metersPerUnit = 0.0000000001; imperial = false; break;
                case swLengthUnit_e.swFEET:
                case swLengthUnit_e.swFEETINCHES: metersPerUnit = 0.3048; imperial = true; break;
                case swLengthUnit_e.swMIL: metersPerUnit = 0.0000254; imperial = true; break;
                case swLengthUnit_e.swUIN: metersPerUnit = 0.0000000254; imperial = true; break;
                default: metersPerUnit = 0.0254; imperial = true; break;
            }

            unitsTag = imperial ? "in" : "mm";
            defaultDecimals = decimals >= 0 && decimals <= 6 ? decimals : (imperial ? 3 : 2);

            return metersPerUnit;
        }

        // ---- Selection ----

        public HoleCalloutSelection ReadSelection(ModelDoc2 model, DrawingDoc drawing)
        {
            HoleCalloutSelection selection = new HoleCalloutSelection();

            try
            {
                selection.SheetName = (drawing.GetCurrentSheet() as Sheet)?.GetName();
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("HoleCalloutService.ReadSelection (sheet)", ex);
            }

            SelectionMgr selMgr = model.SelectionManager as SelectionMgr;

            if (selMgr == null)
                return selection;

            int count = selMgr.GetSelectedObjectCount2(-1);

            for (int i = 1; i <= count; i++)
            {
                try
                {
                    swSelectType_e type = (swSelectType_e)selMgr.GetSelectedObjectType3(i, -1);
                    object selected = selMgr.GetSelectedObject6(i, -1);
                    double[] point = selMgr.GetSelectionPoint2(i, -1) as double[];

                    if (point == null || point.Length < 3)
                    {
                        selection.IgnoredCount++;
                        continue;
                    }

                    View view = type == swSelectType_e.swSelDRAWINGVIEWS
                        ? selected as View
                        : selMgr.GetSelectedObjectsDrawingView2(i, -1);

                    // Only a click on a view's empty space (which selects the
                    // view itself) and a click on the bare sheet have
                    // nothing to attach to; everything else is a real
                    // object the leader can point at.
                    if (type == swSelectType_e.swSelSHEETS)
                    {
                        selection.IgnoredCount++;
                        continue;
                    }

                    HoleCalloutPick pick = new HoleCalloutPick
                    {
                        SheetPoint = point,
                        SelectType = (int)type,
                        Kind = type == swSelectType_e.swSelDRAWINGVIEWS
                            ? HoleCalloutPickKind.Point
                            : HoleCalloutPickKind.Object,
                        DiameterMeters = type == swSelectType_e.swSelEDGES
                            ? CircularEdgeDiameter(selected as Edge)
                            : null,
                    };

                    if (selection.View == null && view != null)
                        selection.View = view;

                    selection.Picks.Add(pick);
                }
                catch (Exception ex)
                {
                    BinspectionLog.Error("HoleCalloutService.ReadSelection (item " + i + ")", ex);
                    selection.IgnoredCount++;
                }
            }

            return selection;
        }

        private static double? CircularEdgeDiameter(Edge edge)
        {
            try
            {
                Curve curve = edge?.GetCurve() as Curve;

                if (curve == null || !curve.IsCircle())
                    return null;

                double[] circle = curve.CircleParams as double[];

                // [center xyz, axis xyz, radius] - model space meters, true size.
                if (circle == null || circle.Length < 7 || circle[6] <= 0)
                    return null;

                return circle[6] * 2.0;
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("HoleCalloutService.CircularEdgeDiameter", ex);
                return null;
            }
        }

        // ---- Create ----

        public GridOperationResult Create(
            ModelDoc2 model,
            DrawingDoc drawing,
            string dataFilePath,
            HoleCalloutDefinition def,
            HoleCalloutSelection selection,
            double metersPerUnit)
        {
            GridOperationResult result = new GridOperationResult();

            if (selection.Picks.Count == 0)
            {
                result.Errors.Add("Nothing usable was selected.");
                return result;
            }

            ProjectData projectData = PersistenceManager.LoadProject(dataFilePath);
            string sheetName = selection.SheetName;

            if (!string.IsNullOrEmpty(sheetName))
                DrawingSheetHelper.ActivateSheet(drawing, sheetName);

            HoleCalloutPick leaderPick = selection.Picks[0];

            double viewScale = 1.0;

            try
            {
                if (selection.View != null && selection.View.ScaleDecimal > 0)
                    viewScale = selection.View.ScaleDecimal;
            }
            catch
            {
            }

            def.CosmeticPoints = selection.Picks
                .Where(p => p.Kind == HoleCalloutPickKind.Point)
                .Select(p => new[] { p.SheetPoint[0], p.SheetPoint[1], 0.0 })
                .ToList();
            def.CosmeticViewScale = viewScale;
            def.CosmeticSketchName = null;

            Note note;

            if (leaderPick.Kind == HoleCalloutPickKind.Object)
            {
                // The leader attaches to whatever is selected when
                // InsertNote runs, so insert while the user's own selection
                // is still live (any object type works - no need to know how
                // to re-find it), trimmed to just the first pick. Cosmetic
                // circles for any empty-spot clicks come afterwards, since
                // sketching clears the selection.
                bool leaderTargetSelected = KeepOnlyFirstSelected(model, leaderPick);

                note = InsertCalloutNote(model, def, leaderPick, leaderTargetSelected, result);

                if (note != null && def.CosmeticPoints.Count > 0)
                    DrawCosmeticOrWarn(model, drawing, def, metersPerUnit, result);
            }
            else
            {
                // Empty-spot click first: draw the cosmetic circle(s), then
                // point the leader at the first one.
                DrawCosmeticOrWarn(model, drawing, def, metersPerUnit, result);

                model.ClearSelection2(true);

                bool leaderTargetSelected =
                    def.CosmeticSketchName != null && SelectCosmeticRim(model, def, leaderPick, metersPerUnit);

                note = InsertCalloutNote(model, def, leaderPick, leaderTargetSelected, result);
            }

            if (note == null)
            {
                RemoveSketch(model, drawing, def.CosmeticSketchName, sheetName);
                return result;
            }

            string noteRefId = PersistentReferenceHelper.GetPersistentId(model, note.GetAnnotation());

            if (string.IsNullOrEmpty(noteRefId))
            {
                DeleteNote(model, note);
                RemoveSketch(model, drawing, def.CosmeticSketchName, sheetName);
                result.Errors.Add("SolidWorks couldn't give the new callout note a persistent id, so it couldn't be tracked. Nothing was added.");
                return result;
            }

            // No balloon on create (per user request) - the rows are saved
            // at Number 0, "tracked, not ballooned yet" (see
            // IsAwaitingBalloon). Create Balloons or Balloon Manager's Add
            // balloons them later (see BalloonPendingCallout).
            List<Characteristic> characteristics =
                BuildCharacteristics(def, noteRefId, 0, null, sheetName);

            projectData.Characteristics.AddRange(characteristics);

            result.Changed = true;

            if (!PersistenceManager.SaveProject(dataFilePath, projectData))
                result.SaveFailed = true;

            model.GraphicsRedraw2();

            result.Warnings.Insert(0,
                "Created the callout with " + characteristics.Count +
                (characteristics.Count == 1 ? " characteristic" : " characteristics") +
                ", not ballooned. Balloon it later with Create Balloons or Balloon Manager's Add.");

            return result;
        }

        // A callout row that exists in the data but has never been
        // ballooned: Number 0 without being deliberately un-numbered.
        // Reconciliation skips these (no balloon is missing), and the
        // report leaves them out until they have a number.
        public static bool IsAwaitingBalloon(Characteristic characteristic) =>
            characteristic != null &&
            characteristic.CalloutValue != null &&
            !characteristic.IsUnnumbered &&
            characteristic.Number <= 0;

        // Balloons a not-yet-ballooned callout on its note and numbers
        // every row of it (anchor bare, siblings N.1, N.2...), found in
        // allRows (the caller's live collection - mutated in place; the
        // caller saves). Returns the new number,
        // or 0 with `error` set.
        public static int BalloonPendingCallout(
            ModelDoc2 model,
            IEnumerable<Characteristic> allRows,
            Characteristic anchor,
            int number,
            BalloonManager balloonManager,
            List<DrawingSheetHelper.ViewOnSheet> viewsBySheet,
            Dictionary<string, Note> existingBalloonIndex,
            out string error)
        {
            error = null;

            INote note = ResolveNote(model, anchor.PersistentRefId);

            if (note == null)
            {
                error = "the callout note is no longer on the drawing";
                return 0;
            }

            Note balloon = balloonManager.CreateBalloon(
                model, note, number.ToString(), anchor.SheetName,
                viewsBySheet: viewsBySheet, existingBalloonIndex: existingBalloonIndex);

            if (balloon == null)
            {
                error = balloonManager.LastFailureReason ?? "the balloon couldn't be placed";
                return 0;
            }

            AssignCalloutNumber(allRows, anchor, number, balloonManager.GetBalloonPersistId(model, balloon));

            return number;
        }

        // Numbers every row of a callout under one balloon: the anchor bare,
        // each "#n" sibling as N.(n-1) - the same sub-numbering Create
        // Balloons gives a native multi-value callout.
        public static void AssignCalloutNumber(
            IEnumerable<Characteristic> allRows, Characteristic anchor, int number, string balloonPersistId)
        {
            foreach (Characteristic row in RowsOf(allRows, anchor))
            {
                row.Number = number;
                row.IsUnnumbered = false;
                row.BalloonPersistId = balloonPersistId;

                int marker = row.PersistentRefId.IndexOf('#');

                row.SubNumber = marker < 0
                    ? (int?)null
                    : int.Parse(row.PersistentRefId.Substring(marker + 1)) - 1;

                CharacteristicManager.AddCharacteristic(row);
            }
        }

        private static List<Characteristic> RowsOf(IEnumerable<Characteristic> allRows, Characteristic anchor)
        {
            string anchorId = anchor.PersistentRefId;

            return allRows
                .Where(c => c.PersistentRefId == anchorId ||
                            (c.PersistentRefId ?? "").StartsWith(anchorId + "#", StringComparison.Ordinal))
                .ToList();
        }

        // ---- Edit ----

        // Finds the Add Hole Callout anchor behind whatever note the user
        // selected - the callout note itself, or its balloon. Null when the
        // note isn't a BINSPECTION-authored callout.
        public Characteristic FindAnchorForNote(ModelDoc2 model, ProjectData projectData, INote selectedNote)
        {
            if (selectedNote == null)
                return null;

            object selectedAnnotation = selectedNote.GetAnnotation();
            string selectedId = PersistentReferenceHelper.GetPersistentId(model, selectedAnnotation);

            List<Characteristic> anchors = projectData.Characteristics.Where(c => c.HoleCallout != null).ToList();

            // 1. Same persistent id string - the check Create Balloons'
            //    own dedupe relies on, cheapest and usually enough.
            if (!string.IsNullOrEmpty(selectedId))
            {
                Characteristic byId = anchors.FirstOrDefault(
                    a => a.PersistentRefId == selectedId || a.BalloonPersistId == selectedId);

                if (byId != null)
                    return byId;
            }

            // 2. Resolve each stored id and ask SolidWorks whether it's the
            //    same object.
            foreach (Characteristic anchor in anchors)
            {
                if (IsSameAnnotation(model, anchor.PersistentRefId, selectedAnnotation) ||
                    IsSameAnnotation(model, anchor.BalloonPersistId, selectedAnnotation))
                    return anchor;
            }

            // 3. Last resort: the resolved callout note has the same text at
            //    the same sheet position as the selected one.
            foreach (Characteristic anchor in anchors)
            {
                if (IsSameNoteByContent(ResolveNote(model, anchor.PersistentRefId), selectedNote))
                    return anchor;
            }

            return null;
        }

        private static bool IsSameNoteByContent(INote candidate, INote selected)
        {
            try
            {
                if (candidate == null || selected == null ||
                    !string.Equals(candidate.GetText(), selected.GetText(), StringComparison.Ordinal))
                    return false;

                double[] a = (candidate.GetAnnotation() as IAnnotation)?.GetPosition() as double[];
                double[] b = (selected.GetAnnotation() as IAnnotation)?.GetPosition() as double[];

                return a != null && b != null && a.Length >= 2 && b.Length >= 2 &&
                       Math.Abs(a[0] - b[0]) < 1e-6 && Math.Abs(a[1] - b[1]) < 1e-6;
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("HoleCalloutService.IsSameNoteByContent", ex);
                return false;
            }
        }

        // The anchor for any row of a callout (anchor or "#n" sibling).
        public static Characteristic FindAnchor(ProjectData projectData, Characteristic row)
        {
            if (row == null || string.IsNullOrEmpty(row.PersistentRefId))
                return null;

            string baseId = row.PersistentRefId;
            int marker = baseId.IndexOf('#');

            if (marker >= 0)
                baseId = baseId.Substring(0, marker);

            return projectData.Characteristics.FirstOrDefault(
                c => c.HoleCallout != null && c.PersistentRefId == baseId);
        }

        public GridOperationResult Edit(
            ModelDoc2 model,
            DrawingDoc drawing,
            string dataFilePath,
            string anchorRefId,
            HoleCalloutDefinition def,
            double metersPerUnit)
        {
            GridOperationResult result = new GridOperationResult();

            ProjectData projectData = PersistenceManager.LoadProject(dataFilePath);

            Characteristic anchor = projectData.Characteristics.FirstOrDefault(
                c => c.HoleCallout != null && c.PersistentRefId == anchorRefId);

            if (anchor == null)
            {
                result.Errors.Add("This hole callout's saved data couldn't be found.");
                return result;
            }

            if (!string.IsNullOrEmpty(anchor.SheetName))
                DrawingSheetHelper.ActivateSheet(drawing, anchor.SheetName);

            INote note = ResolveNote(model, anchorRefId);

            if (note == null)
            {
                result.Errors.Add("The callout note is no longer on the drawing, so it can't be edited. " +
                    "Use Balloon Manager's De-# to retire its rows, then add it again.");
                return result;
            }

            HoleCalloutDefinition previous = anchor.HoleCallout;

            if (!note.SetText(HoleCalloutBuilder.BuildNoteText(def)))
            {
                result.Errors.Add("SolidWorks refused the updated callout text. Nothing was changed.");
                return result;
            }

            // Cosmetic geometry follows the new size. Redrawing replaces the
            // sketch the leader was attached to, so only do it when the
            // drawn diameters actually changed.
            def.CosmeticPoints = previous.CosmeticPoints ?? new List<double[]>();
            def.CosmeticViewScale = previous.CosmeticViewScale;
            def.CosmeticSketchName = previous.CosmeticSketchName;
            def.MeasuredDiameter = previous.MeasuredDiameter;

            if (def.CosmeticPoints.Count > 0 &&
                !HoleCalloutBuilder.CosmeticDiameters(def).SequenceEqual(HoleCalloutBuilder.CosmeticDiameters(previous)))
            {
                RemoveSketch(model, drawing, previous.CosmeticSketchName, anchor.SheetName);
                def.CosmeticSketchName = DrawCosmetic(model, drawing, def, metersPerUnit);

                result.Warnings.Add("The cosmetic circle was redrawn at the new size - check that the callout's leader is still attached.");
            }

            RegenerateRows(projectData, anchor, def);

            result.Changed = true;

            if (!PersistenceManager.SaveProject(dataFilePath, projectData))
                result.SaveFailed = true;

            model.GraphicsRedraw2();

            result.Warnings.Insert(0, "Updated hole callout " + anchor.DisplayNumber + ".");

            return result;
        }

        // Replaces every row of the callout (anchor + "#n" siblings) with
        // freshly built ones, in the same place in the list. Numbering,
        // balloon identity, legacy number, position, and exclusion state
        // carry over from the old anchor; Method/Classification carry over
        // per value (matched by CalloutValue.Key), so editing a depth
        // doesn't wipe what was already assigned to the diameter.
        private static void RegenerateRows(ProjectData projectData, Characteristic anchor, HoleCalloutDefinition def)
        {
            string anchorId = anchor.PersistentRefId;

            List<Characteristic> oldRows = projectData.Characteristics
                .Where(c => c.PersistentRefId == anchorId || (c.PersistentRefId ?? "").StartsWith(anchorId + "#", StringComparison.Ordinal))
                .ToList();

            Dictionary<string, Characteristic> oldByKey = oldRows
                .Where(c => c.CalloutValue?.Key != null)
                .GroupBy(c => c.CalloutValue.Key)
                .ToDictionary(g => g.Key, g => g.First());

            int insertAt = projectData.Characteristics.IndexOf(anchor);

            foreach (Characteristic old in oldRows)
                projectData.Characteristics.Remove(old);

            List<Characteristic> fresh =
                BuildCharacteristics(def, anchorId, anchor.Number, anchor.BalloonPersistId, anchor.SheetName);

            Characteristic newAnchor = fresh[0];
            newAnchor.SubNumber = anchor.SubNumber;
            newAnchor.PreGroupNumber = anchor.PreGroupNumber;
            newAnchor.SharesGroupBalloon = anchor.SharesGroupBalloon;
            newAnchor.LegacyBalloonNumber = anchor.LegacyBalloonNumber;
            newAnchor.BalloonPositionX = anchor.BalloonPositionX;
            newAnchor.BalloonPositionY = anchor.BalloonPositionY;
            newAnchor.BalloonPositionZ = anchor.BalloonPositionZ;

            foreach (Characteristic row in fresh)
            {
                row.IsUnnumbered = anchor.IsUnnumbered;

                if (row.IsUnnumbered)
                    row.Number = 0;

                Characteristic previous;

                if (oldByKey.TryGetValue(row.CalloutValue.Key, out previous))
                {
                    row.Method = previous.Method;
                    row.Class = previous.Class;
                }
                else if (row == newAnchor)
                {
                    row.Method = anchor.Method;
                    row.Class = anchor.Class;
                }
            }

            if (insertAt < 0 || insertAt > projectData.Characteristics.Count)
                insertAt = projectData.Characteristics.Count;

            projectData.Characteristics.InsertRange(insertAt, fresh);
        }

        private static List<Characteristic> BuildCharacteristics(
            HoleCalloutDefinition def, string noteRefId, int number, string balloonPersistId, string sheetName)
        {
            List<HoleCalloutRow> rows = HoleCalloutBuilder.BuildRows(def);
            List<Characteristic> characteristics = new List<Characteristic>();

            for (int i = 0; i < rows.Count; i++)
            {
                characteristics.Add(new Characteristic
                {
                    Number = number,
                    SubNumber = i == 0 ? (int?)null : i,
                    PersistentRefId = i == 0 ? noteRefId : noteRefId + "#" + (i + 1),
                    BalloonPersistId = balloonPersistId,
                    DimensionName = rows[i].Text,
                    SheetName = sheetName,
                    HoleCallout = i == 0 ? def : null,
                    CalloutValue = rows[i].Value,
                });
            }

            return characteristics;
        }

        // ---- SolidWorks helpers ----

        // Inserts the note with whatever is currently selected as its
        // leader target (the caller has already arranged that), then moves
        // the text up and to the right of the picked point.
        private static Note InsertCalloutNote(
            ModelDoc2 model,
            HoleCalloutDefinition def,
            HoleCalloutPick leaderPick,
            bool leaderTargetSelected,
            GridOperationResult result)
        {
            if (!leaderTargetSelected)
            {
                model.ClearSelection2(true);
                result.Warnings.Add("The leader target couldn't be selected, so the callout was placed without a leader - drag one on from the note's handle if needed.");
            }

            Note note = null;

            try
            {
                note = model.InsertNote(HoleCalloutBuilder.BuildNoteText(def)) as Note;
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("HoleCalloutService.InsertCalloutNote", ex);
            }

            model.ClearSelection2(true);

            if (note == null)
            {
                result.Errors.Add("SolidWorks didn't create the callout note. Nothing was added.");
                return null;
            }

            try
            {
                Annotation annotation = note.GetAnnotation() as Annotation;

                annotation?.SetPosition(
                    leaderPick.SheetPoint[0] + NoteOffsetX,
                    leaderPick.SheetPoint[1] + NoteOffsetY,
                    0);
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("HoleCalloutService.InsertCalloutNote (position)", ex);
            }

            return note;
        }

        // Trims the user's live selection down to just the first object
        // pick, so the note gets one leader to it. Works for any selectable
        // type because nothing is re-selected - the rest is only deselected
        // (highest index first, so earlier indices don't shift). Falls back
        // to re-selecting by clicked position if the selection was lost
        // (e.g. the user clicked the drawing while the dialog was open).
        private static bool KeepOnlyFirstSelected(ModelDoc2 model, HoleCalloutPick pick)
        {
            try
            {
                SelectionMgr selMgr = model.SelectionManager as SelectionMgr;
                int count = selMgr?.GetSelectedObjectCount2(-1) ?? 0;

                if (count > 0 && selMgr.GetSelectedObjectType3(1, -1) == pick.SelectType)
                {
                    for (int i = count; i >= 2; i--)
                        selMgr.DeSelect2(i, -1);

                    return selMgr.GetSelectedObjectCount2(-1) == 1;
                }

                string typeName;

                if (!SelectTypeNames.TryGetValue((swSelectType_e)pick.SelectType, out typeName))
                    return false;

                model.ClearSelection2(true);

                return model.Extension.SelectByID2(
                    "", typeName, pick.SheetPoint[0], pick.SheetPoint[1], 0, false, 0, null, 0);
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("HoleCalloutService.KeepOnlyFirstSelected", ex);
                return false;
            }
        }

        // SelectByID2 type strings for the fallback above - only the common
        // leader targets; anything else just gets no leader.
        private static readonly Dictionary<swSelectType_e, string> SelectTypeNames =
            new Dictionary<swSelectType_e, string>
            {
                { swSelectType_e.swSelEDGES, "EDGE" },
                { swSelectType_e.swSelFACES, "FACE" },
                { swSelectType_e.swSelVERTICES, "VERTEX" },
                { swSelectType_e.swSelSILHOUETTES, "SILHOUETTE" },
                { swSelectType_e.swSelSKETCHSEGS, "SKETCHSEGMENT" },
                { swSelectType_e.swSelSKETCHPOINTS, "SKETCHPOINT" },
                { swSelectType_e.swSelEXTSKETCHSEGS, "EXTSKETCHSEGMENT" },
                { swSelectType_e.swSelEXTSKETCHPOINTS, "EXTSKETCHPOINT" },
                { swSelectType_e.swSelNOTES, "NOTE" },
                { swSelectType_e.swSelDIMENSIONS, "DIMENSION" },
            };

        // Selects the rim of the (smallest) cosmetic circle drawn at the
        // leader pick's point.
        private static bool SelectCosmeticRim(ModelDoc2 model, HoleCalloutDefinition def, HoleCalloutPick pick, double metersPerUnit)
        {
            try
            {
                double diameter = HoleCalloutBuilder.CosmeticDiameters(def).DefaultIfEmpty(0).Min();
                double radius = diameter * metersPerUnit * def.CosmeticViewScale / 2.0;

                if (radius <= 0)
                    return false;

                return model.Extension.SelectByID2(
                    "", "SKETCHSEGMENT", pick.SheetPoint[0] + radius, pick.SheetPoint[1], 0, false, 0, null, 0);
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("HoleCalloutService.SelectCosmeticRim", ex);
                return false;
            }
        }

        private static void DrawCosmeticOrWarn(ModelDoc2 model, DrawingDoc drawing, HoleCalloutDefinition def, double metersPerUnit, GridOperationResult result)
        {
            def.CosmeticSketchName = DrawCosmetic(model, drawing, def, metersPerUnit);

            if (def.CosmeticSketchName == null)
                result.Warnings.Add("The cosmetic circle couldn't be drawn - the callout was still created.");
        }

        // Draws a circle per shown diameter plus a center mark (two thin
        // crossing lines slightly past the outer circle) at every clicked
        // point, all in one sheet-level sketch - same "enter a sketch with
        // nothing selected" technique as DimensionBoxSketchService. Returns
        // the sketch feature's name, or null if nothing could be drawn.
        private static string DrawCosmetic(ModelDoc2 model, DrawingDoc drawing, HoleCalloutDefinition def, double metersPerUnit)
        {
            List<double> diameters = HoleCalloutBuilder.CosmeticDiameters(def);

            if (diameters.Count == 0 || def.CosmeticPoints == null || def.CosmeticPoints.Count == 0)
                return null;

            try
            {
                double scale = metersPerUnit * def.CosmeticViewScale;
                double outerRadius = diameters.Max() * scale / 2.0;
                double markHalfLength = outerRadius * 1.25;

                model.ClearSelection2(true);

                SketchManager sketchMgr = model.SketchManager;
                List<SketchSegment> segments = new List<SketchSegment>();

                sketchMgr.InsertSketch(true);

                foreach (double[] point in def.CosmeticPoints)
                {
                    double x = point[0];
                    double y = point[1];

                    foreach (double diameter in diameters.Distinct())
                    {
                        SketchSegment circle = sketchMgr.CreateCircleByRadius(x, y, 0, diameter * scale / 2.0);

                        if (circle != null)
                            segments.Add(circle);
                    }

                    SketchSegment horizontal = sketchMgr.CreateLine(x - markHalfLength, y, 0, x + markHalfLength, y, 0);
                    SketchSegment vertical = sketchMgr.CreateLine(x, y - markHalfLength, 0, x, y + markHalfLength, 0);

                    if (horizontal != null) segments.Add(horizontal);
                    if (vertical != null) segments.Add(vertical);
                }

                sketchMgr.InsertSketch(true);

                Feature sketchFeature = model.FeatureByPositionReverse(0) as Feature;

                if (sketchFeature == null || segments.Count == 0)
                    return null;

                bool append = false;

                foreach (SketchSegment segment in segments)
                {
                    segment.Select4(append, null);
                    append = true;
                }

                drawing.SetLineWidthCustom(ThinLineWidthMeters);
                model.ClearSelection2(true);

                return sketchFeature.Name;
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("HoleCalloutService.DrawCosmetic", ex);
                return null;
            }
        }

        private static void RemoveSketch(ModelDoc2 model, DrawingDoc drawing, string sketchName, string sheetName)
        {
            if (string.IsNullOrEmpty(sketchName))
                return;

            try
            {
                DimensionBoxSketchService.RemoveBox(model, drawing, sketchName, sheetName);
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("HoleCalloutService.RemoveSketch", ex);
            }
        }

        private static void DeleteNote(ModelDoc2 model, Note note)
        {
            try
            {
                model.ClearSelection2(true);

                Annotation annotation = note.GetAnnotation() as Annotation;

                if (annotation != null && annotation.Select3(false, null))
                    model.EditDelete();

                model.ClearSelection2(true);
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("HoleCalloutService.DeleteNote", ex);
            }
        }

        public static INote ResolveNote(ModelDoc2 model, string refId)
        {
            swPersistReferencedObjectStates_e state;
            object resolved = PersistentReferenceHelper.ResolvePersistentId(model, refId, out state);

            INote note = resolved as INote;

            if (note != null)
                return note;

            IAnnotation annotation = resolved as IAnnotation;

            return annotation?.GetSpecificAnnotation() as INote;
        }

        // Resolves the stored id and compares live objects rather than
        // comparing a freshly generated id string - the codebase never
        // relies on GetPersistReference3 returning byte-identical ids for
        // the same object twice (see BalloonManager.RemoveBalloonsByPersistId).
        private bool IsSameAnnotation(ModelDoc2 model, string storedId, object liveAnnotation)
        {
            if (string.IsNullOrEmpty(storedId) || liveAnnotation == null)
                return false;

            try
            {
                swPersistReferencedObjectStates_e state;
                object resolved = PersistentReferenceHelper.ResolvePersistentId(model, storedId, out state);

                if (resolved == null)
                    return false;

                IAnnotation resolvedAnnotation =
                    resolved as IAnnotation ?? (resolved as INote)?.GetAnnotation() as IAnnotation;

                if (resolvedAnnotation == null)
                    return false;

                return _swApp.IsSame(resolvedAnnotation, liveAnnotation) == (int)swObjectEquality.swObjectSame;
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("HoleCalloutService.IsSameAnnotation", ex);
                return false;
            }
        }
    }
}
