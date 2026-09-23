using System;
using System.Collections.Generic;
using System.Linq;
using BINSPECTION.Models;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace BINSPECTION.Core
{
    // One row of BalloonManagerWindow's grid: either a live dimension found
    // by DimensionScanner (AnnotationSource set, so it can be ballooned via
    // Add), an already-persisted characteristic with no live dimension hit
    // this scan (a GD&T frame/note/surface finish balloon, or one whose
    // dimension failed to resolve - AnnotationSource null, so only
    // Delete/Ungroup/Un-Number/manual renumber apply), or both at once (a
    // live dimension that already has a balloon).
    public class BalloonGridRow
    {
        public string PersistentRefId { get; set; }

        public Characteristic Characteristic { get; set; }

        public DisplayDimension AnnotationSource { get; set; }

        // False when AnnotationSource is set but its value couldn't be read
        // this scan (GetDimensionDisplay's resolved out param) - almost
        // always means the dimension is dangling (its driving
        // feature/geometry was deleted upstream, but the DisplayDimension
        // itself is still enumerated on the drawing). Selecting a dangling
        // annotation has been observed to crash SolidWorks outright (a
        // native access violation, not a catchable .NET exception), so
        // BalloonManagerWindow.ResolveAnnotation skips straight to the
        // balloon-note fallback for a row flagged like this instead of ever
        // calling Select3 on it. Meaningless (left true) when
        // AnnotationSource is null.
        public bool IsDimensionResolved { get; set; } = true;

        public string SheetName { get; set; }

        public string DimensionDisplay { get; set; }

        public string DisplayNumber { get; set; }

        public bool IsUnnumbered { get; set; }

        public string LegacyBalloonNumber { get; set; }

        public bool HasBalloon { get; set; }

        // True when this row has a Characteristic record to attach
        // Method/Class to (ballooned or unnumbered-but-tracked) - a bare
        // unballooned dimension has nowhere to save attributes until it's
        // added/numbered, so the grid disables those cells.
        public bool HasCharacteristic => Characteristic != null;

        public string Method { get; set; }

        public string Class { get; set; }

        // True when this dimension's tolerance display is forced to
        // literal "BASIC" text - see Characteristic.IsBasic and
        // BalloonGridService.SetBasic. For a row with a live
        // AnnotationSource this always reflects the dimension's actual
        // current IDimensionTolerance.Type, not just whatever was last
        // saved.
        public bool IsBasic { get; set; }
    }

    // The selection-independent operational core behind BalloonManagerWindow:
    // everything Group Balloon/Ungroup Balloon/Add Balloon/Un-Number/
    // Re-Number used to do by reading the user's SolidWorks graphics-area
    // selection, now driven by which grid rows are checked
    // instead. Every method here applies its change immediately (live SW
    // annotation edits, same as the commands it replaces) and saves the
    // project file itself, rather than staging changes behind a dialog's
    // OK/Cancel - there is no way to "cancel" a balloon that has already
    // been renamed/created/deleted on the drawing.
    //
    // Every action returns a GridOperationResult instead of throwing: bulk
    // actions isolate failures per row (one bad row is reported, the rest
    // still apply), all-or-nothing actions (Group, Manual renumber) roll
    // back on failure, and a failed save is reported rather than silently
    // dropped - see Execute. Callers just show result.Message if non-null.
    public static class BalloonGridService
    {
        // Every dimension on the given sheet (or every sheet, if
        // sheetFilter is null), merged with whatever persisted
        // Characteristic already exists for it, plus any persisted
        // characteristic for that sheet with no live dimension hit here
        // (GD&T/note/surface-finish balloons, or one whose dimension
        // couldn't be resolved this run) so Delete/Ungroup/Un-Number/manual
        // renumber still work on it from the grid.
        public static List<BalloonGridRow> BuildRows(
            ModelDoc2 model,
            DrawingDoc drawing,
            ProjectData projectData,
            string sheetFilter)
        {
            List<BalloonGridRow> rows = new List<BalloonGridRow>();

            Dictionary<string, Characteristic> byPersistentRefId = projectData.Characteristics
                .Where(c => !string.IsNullOrEmpty(c.PersistentRefId))
                .GroupBy(c => c.PersistentRefId)
                .ToDictionary(g => g.Key, g => g.First());

            HashSet<Characteristic> consumed = new HashSet<Characteristic>();

            DimensionScanner scanner = new DimensionScanner();

            // Restrict the scan to just sheetFilter when one is given,
            // instead of activating every sheet in the drawing only to
            // filter almost all of it right back out below - see
            // DrawingSheetHelper.GetAllViewsBySheet's remarks. The grid is
            // always scoped to one sheet in practice (BalloonManagerWindow
            // always passes its current _selectedSheet), so this is the
            // common case, not an edge case.
            List<DrawingSheetHelper.ViewOnSheet> viewsBySheet =
                DrawingSheetHelper.GetAllViewsBySheet(
                    drawing,
                    sheetFilter != null ? new[] { sheetFilter } : null);

            List<DimensionHit> hits = scanner.GetAllDimensions(drawing, viewsBySheet: viewsBySheet, model: model);

            foreach (DimensionHit hit in hits)
            {
                string sheetName = hit.SheetName;

                if (sheetFilter != null && sheetName != sheetFilter)
                    continue;

                Dimension swDim = hit.Dimension.GetDimension2(0);

                if (swDim == null)
                    continue;

                IAnnotation dimAnnotation =
                    hit.Dimension.GetAnnotation() as IAnnotation;

                string persistentRefId =
                    PersistentReferenceHelper.GetPersistentId(model, dimAnnotation);

                if (string.IsNullOrEmpty(persistentRefId))
                    continue;

                Characteristic characteristic;
                byPersistentRefId.TryGetValue(persistentRefId, out characteristic);

                if (characteristic != null)
                    consumed.Add(characteristic);

                bool hasBalloon =
                    characteristic != null && !characteristic.IsUnnumbered && characteristic.Number > 0;

                // Live SolidWorks state is the source of truth for whether
                // this dimension currently reads as Basic - not whatever
                // was last saved on the Characteristic - so a dimension the
                // user (or an earlier drawing edit) already set to Basic
                // shows checked even if it was never toggled from this
                // grid. See Characteristic.IsBasic.
                bool isBasic =
                    swDim.Tolerance != null && swDim.Tolerance.Type == (int)swTolType_e.swTolBASIC;

                bool dimensionResolved;
                string dimensionDisplay =
                    GetDimensionDisplay(model, hit.Dimension, swDim, isBasic, out dimensionResolved);

                rows.Add(new BalloonGridRow
                {
                    PersistentRefId = persistentRefId,
                    Characteristic = characteristic,
                    AnnotationSource = hit.Dimension,
                    IsDimensionResolved = dimensionResolved,
                    SheetName = sheetName,
                    DimensionDisplay = dimensionDisplay,
                    DisplayNumber = hasBalloon ? characteristic.DisplayNumber : "",
                    IsUnnumbered = characteristic != null && characteristic.IsUnnumbered,
                    LegacyBalloonNumber = characteristic?.LegacyBalloonNumber,
                    HasBalloon = hasBalloon,
                    Method = characteristic?.Method,
                    Class = characteristic?.Class,
                    IsBasic = isBasic,
                });
            }

            foreach (Characteristic characteristic in projectData.Characteristics)
            {
                if (consumed.Contains(characteristic))
                    continue;

                // Characteristics with no SheetName recorded (created
                // before that field existed, or via an older path that
                // never threaded a View through - see
                // [[binspection_architecture]]) can't be matched against
                // sheetFilter at all - always show them rather than
                // silently hiding real balloon data on every sheet, same
                // graceful-degradation instinct used elsewhere in this
                // add-in.
                if (sheetFilter != null &&
                    !string.IsNullOrEmpty(characteristic.SheetName) &&
                    characteristic.SheetName != sheetFilter)
                    continue;

                bool hasBalloon = !characteristic.IsUnnumbered && characteristic.Number > 0;

                rows.Add(new BalloonGridRow
                {
                    PersistentRefId = characteristic.PersistentRefId,
                    Characteristic = characteristic,
                    AnnotationSource = null,
                    SheetName = characteristic.SheetName,
                    DimensionDisplay = characteristic.DimensionName,
                    DisplayNumber = hasBalloon ? characteristic.DisplayNumber : "",
                    IsUnnumbered = characteristic.IsUnnumbered,
                    LegacyBalloonNumber = characteristic.LegacyBalloonNumber,
                    HasBalloon = hasBalloon,
                    Method = characteristic.Method,
                    Class = characteristic.Class,
                    IsBasic = characteristic.IsBasic,
                });
            }

            return rows
                .OrderBy(r => r.HasBalloon ? 0 : 1)
                .ThenBy(r => r.Characteristic?.Number ?? int.MaxValue)
                .ThenBy(r => r.Characteristic?.SubNumber ?? 0)
                .ToList();
        }

        // Human-readable value as currently drawn (e.g. "38.15"), not the
        // raw internal dimension name (e.g. "RD2@Drawing View1") and NOT
        // decorated with any tolerance text - per an explicit user request,
        // the Dimension/Characteristic cell (both here in Balloon Manager's
        // own grid and in the exported report - see
        // ReportGenerator.WriteCharacteristicCell) shows the bare nominal
        // value only; tolerance belongs solely in the report's separate
        // Upper/Lower Limit columns (see ReportGenerator.
        // ResolveLimitCellValues), never repeated here. A combined
        // hole-callout value ("⌀20.32 X 100°") is read from SolidWorks' own
        // rendered callout text (see
        // HoleCalloutExtractor.GetCombinedDisplayText), and a combined
        // chamfer value ("12.7 X 45°") via
        // ChamferValueReader.GetCombinedDisplayText
        // (IDimension.GetSystemChamferValues) - both instead of the
        // single-value path below, since GetDimension2(0) alone has no
        // guarantee of landing on the "right" half of a combined value (and,
        // for a chamfer's angle, can read as the supplement of what's
        // actually displayed); neither of those is a toleranced single
        // dimension in the first place, so isBasic has no effect on them
        // either. isBasic is otherwise unused for text now (kept as a
        // parameter only because every call site already resolves it for
        // other purposes - see BalloonGridService.SetBasic) - a Basic
        // dimension's nominal shows the same as any other, no "BASIC" text.
        // Internal (not private) so CommandManagerHandler's plain-dimension
        // balloon-creation path (OnCreateBalloons) can stamp
        // Characteristic.DimensionName with this same readable
        // text instead of the raw swDim.FullName ("RD2@DrawingView3") they
        // used before - that raw internal name is what was leaking into the
        // exported report's characteristic cell (see
        // ReportGenerator.WriteCharacteristicCell's live-refresh fallback).
        internal static string GetDimensionDisplay(
            ModelDoc2 model, DisplayDimension dimension, Dimension swDim, bool isBasic)
        {
            bool resolved;
            return GetDimensionDisplay(model, dimension, swDim, isBasic, out resolved);
        }

        // Same as above, plus whether the dimension's value could actually
        // be read this call - false almost always means the dimension is
        // dangling (see BalloonGridRow.IsDimensionResolved). A combined
        // hole-callout/chamfer value is always "resolved" here since it
        // doesn't go through ReadDimensionValues at all.
        internal static string GetDimensionDisplay(
            ModelDoc2 model, DisplayDimension dimension, Dimension swDim, bool isBasic, out bool resolved)
        {
            string combined =
                HoleCalloutExtractor.GetCombinedDisplayText(model, dimension) ??
                ChamferValueReader.GetCombinedDisplayText(model, dimension);

            if (combined != null)
            {
                resolved = true;
                return combined;
            }

            double nominal, plusTolerance, minusTolerance;

            ReportGenerator.ReadDimensionValues(
                model, dimension, out nominal, out plusTolerance, out minusTolerance, out resolved);

            return resolved
                ? nominal.ToString("0.####")
                : swDim.FullName;
        }


        // Balloons every checked, currently-unballooned row that has a live
        // dimension to anchor to - same single-value-only behavior the
        // command this replaces (Add Balloon) had (a hole callout/chamfer's
        // extra values are only split out by Create Balloons' full rescan).
        // Numbers come from the row's sheet's reserved range first, same as
        // Create Balloons (see NextNumber). Adding back a previously
        // un-numbered multi-value balloon's main row brings every one of its
        // values back with it (see FollowAnchor).
        public static GridOperationResult AddBalloons(
            ModelDoc2 model,
            string dataFilePath,
            List<BalloonGridRow> checkedRows,
            ProjectData projectData)
        {
            return Execute(model, dataFilePath, projectData, (result, batch) =>
            {
                PrepareNumbering(projectData);
                BalloonUnits units = new BalloonUnits(projectData);

                foreach (BalloonGridRow row in checkedRows)
                {
                    if (row.HasBalloon || row.AnnotationSource == null)
                        continue;

                    TryRow(result, Describe(row), () =>
                    {
                        int nextNumber = NextNumber(row.SheetName, projectData);

                        Note note = batch.CreateBalloon(row, nextNumber.ToString(), result);

                        if (note == null)
                            return;

                        result.Changed = true;

                        Characteristic characteristic = EnsureCharacteristic(row, projectData);

                        AssignNumber(characteristic, nextNumber, null);
                        characteristic.BalloonPersistId = batch.GetPersistId(note);

                        FollowAnchor(characteristic, units, rejoinUnnumbered: true);
                    });
                }
            });
        }

        // Joins two or more checked, currently-ungrouped balloons into one
        // group under a single fresh number, shown on the drawing by ONE
        // balloon: the first member's note is renamed to the bare group
        // number ("10") and every later member ("10.1", "10.2") is
        // data-only - its own note is deleted and it's flagged
        // SharesGroupBalloon, so Restore/Refresh Balloons never redraws it.
        // Only the first member needs a balloon on the drawing to start
        // from. The renumbering is all-or-nothing (rolled back on failure);
        // the later members' old notes are deleted only once it has
        // committed, and a note that won't delete is reported, not undone.
        // Multi-value balloons (a hole callout's "6, 6.1, 6.2" sharing one
        // physical note - see BalloonUnits) are refused outright: their
        // decimal numbers are already spoken for by their own values.
        public static GridOperationResult GroupBalloons(
            ModelDoc2 model,
            string dataFilePath,
            List<BalloonGridRow> checkedRows,
            ProjectData projectData)
        {
            return Execute(model, dataFilePath, projectData, (result, batch) =>
            {
                BalloonUnits units = new BalloonUnits(projectData);

                List<Characteristic> members = checkedRows
                    .Where(r => r.HasBalloon && r.Characteristic != null)
                    .Select(r => r.Characteristic)
                    .Distinct()
                    .ToList();

                if (members.Count < 2)
                {
                    result.Errors.Add("Check two or more ungrouped balloons to join, then try again.");
                    return;
                }

                List<Characteristic> multiValue = members.Where(units.IsMultiValue).ToList();

                if (multiValue.Count > 0)
                {
                    result.Errors.Add(
                        "Balloon " + JoinNumbers(multiValue) + " belongs to a multi-value balloon (hole callout, " +
                        "chamfer, composite GD&T frame or multi-line note), which already uses its own decimal " +
                        "numbers and can't be joined into a group.");
                    return;
                }

                if (members.Any(units.IsUserGroupMember))
                {
                    result.Errors.Add("One or more checked balloons is already part of a group - ungroup it first.");
                    return;
                }

                // Resolve every source Note BEFORE changing any of them, same
                // as LegacyRenumberService.Apply - two balloons briefly sharing
                // the same displayed text mid-batch could otherwise make a
                // later lookup in this same call ambiguous.
                List<string> oldDisplayNumbers = members.Select(c => c.DisplayNumber).ToList();
                List<Note> notes = oldDisplayNumbers.Select(batch.FindBalloon).ToList();

                Characteristic anchor = members[0];

                if (notes[0] == null)
                {
                    result.Errors.Add(
                        "Balloon " + anchor.DisplayNumber + " (the group's first member, which keeps the group's " +
                        "balloon) couldn't be found on the drawing. Run Refresh Balloons to redraw it, then try again.");
                    return;
                }

                PrepareNumbering(projectData);

                int groupNumber = NextNumber(anchor.SheetName, projectData);

                List<NumberSnapshot> snapshots = members.Select(c => new NumberSnapshot(c)).ToList();
                int renameMark = batch.RenameCount;

                try
                {
                    batch.Rename(notes[0], oldDisplayNumbers[0], groupNumber.ToString());

                    for (int i = 0; i < members.Count; i++)
                    {
                        Characteristic characteristic = members[i];

                        characteristic.PreGroupNumber = characteristic.Number;
                        characteristic.Number = groupNumber;

                        // The group's first member is the bare whole number (e.g.
                        // "6"), never "6.1" - only members after it get a decimal
                        // sub-number, starting at .1.
                        characteristic.SubNumber = i == 0 ? (int?)null : i;

                        if (i > 0)
                        {
                            characteristic.SharesGroupBalloon = true;
                            characteristic.BalloonPersistId = anchor.BalloonPersistId;
                        }
                    }

                    result.Changed = true;
                }
                catch (Exception ex)
                {
                    RollBack(result, batch, snapshots, renameMark, "Grouping", ex);
                    return;
                }

                for (int i = 1; i < members.Count; i++)
                {
                    if (notes[i] != null && !batch.DeleteBalloon(notes[i], oldDisplayNumbers[i]))
                    {
                        result.Warnings.Add(
                            "Balloon " + oldDisplayNumbers[i] + " is now " + members[i].DisplayNumber + " in group " +
                            groupNumber + ", but its old balloon couldn't be removed from the drawing - delete it by hand.");
                    }
                }
            });
        }

        // Reverses grouping for each checked, currently-grouped balloon:
        // each one gets its pre-group number back (or the next free one if
        // that number has since been taken, or was never recorded), same as
        // Ungroup Balloon did. A member with its own note (the group's first
        // one) has it renamed; a data-only member (SharesGroupBalloon) gets
        // a new balloon of its own drawn at its restored number. Ungrouping
        // the first member removes the balloon that stood for the whole
        // group, so its data-only members are ungrouped along with it. A
        // multi-value balloon's own values aren't a group and are left
        // alone. Each member is independent, so one failing doesn't stop
        // the rest.
        public static GridOperationResult UngroupBalloons(
            ModelDoc2 model,
            string dataFilePath,
            List<BalloonGridRow> checkedRows,
            ProjectData projectData)
        {
            return Execute(model, dataFilePath, projectData, (result, batch) =>
            {
                BalloonUnits units = new BalloonUnits(projectData);

                List<BalloonGridRow> checkedBalloonRows = checkedRows
                    .Where(r => r.HasBalloon && r.Characteristic != null)
                    .GroupBy(r => r.Characteristic)
                    .Select(g => g.First())
                    .ToList();

                List<BalloonGridRow> memberRows = checkedBalloonRows
                    .Where(r => units.IsUserGroupMember(r.Characteristic))
                    .ToList();

                if (memberRows.Count == 0)
                {
                    result.Errors.Add(checkedBalloonRows.Any(r => units.IsMultiValue(r.Characteristic))
                        ? "The checked numbers are the values of one multi-value balloon (hole callout, chamfer, " +
                          "composite GD&T frame or multi-line note), not a group - there's nothing to ungroup."
                        : "Check one or more grouped balloons (e.g. 12 or 12.2) to ungroup, then try again.");
                    return;
                }

                // (characteristic, its live row source if the grid had one)
                List<KeyValuePair<Characteristic, object>> members = memberRows
                    .Select(r => new KeyValuePair<Characteristic, object>(r.Characteristic, r.AnnotationSource))
                    .ToList();

                foreach (BalloonGridRow row in memberRows.Where(r => !r.Characteristic.SubNumber.HasValue).ToList())
                {
                    List<Characteristic> carried = GroupMembersSharing(row.Characteristic, projectData)
                        .Where(m => members.All(p => p.Key != m))
                        .ToList();

                    if (carried.Count == 0)
                        continue;

                    members.AddRange(carried.Select(m => new KeyValuePair<Characteristic, object>(m, null)));

                    result.Warnings.Add(
                        "Balloon " + row.Characteristic.DisplayNumber + " was the balloon for its whole group, so " +
                        JoinNumbers(carried) + " were ungrouped with it.");
                }

                List<Note> notes = members
                    .Select(p => p.Key.SharesGroupBalloon ? null : batch.FindBalloon(p.Key.DisplayNumber))
                    .ToList();

                PrepareNumbering(projectData);

                for (int i = 0; i < members.Count; i++)
                {
                    Characteristic characteristic = members[i].Key;
                    object liveSource = members[i].Value;
                    Note note = notes[i];

                    TryRow(result, "Balloon " + characteristic.DisplayNumber, () =>
                    {
                        string oldDisplayNumber = characteristic.DisplayNumber;
                        string restoredNumber = RestorableNumber(characteristic, projectData).ToString();
                        string balloonPersistId = characteristic.BalloonPersistId;

                        // Drawing first, then data - if the balloon can't be
                        // renamed/drawn, the saved record still matches.
                        if (characteristic.SharesGroupBalloon)
                        {
                            object source = liveSource ?? ResolveSource(model, characteristic);
                            Note created = source != null
                                ? batch.CreateBalloon(source, characteristic.SheetName, restoredNumber, "Balloon " + oldDisplayNumber, result)
                                : null;

                            if (created != null)
                                balloonPersistId = batch.GetPersistId(created);
                            else if (source == null)
                                result.Warnings.Add(
                                    "Balloon " + oldDisplayNumber + " was ungrouped to " + restoredNumber + ", but its " +
                                    "source couldn't be found to draw a balloon on - run Refresh Balloons to draw it.");
                            else
                                return;
                        }
                        else if (note != null)
                        {
                            batch.Rename(note, oldDisplayNumber, restoredNumber);
                        }
                        else
                        {
                            result.Warnings.Add(NoteNotFoundWarning(oldDisplayNumber, restoredNumber));
                        }

                        AssignNumber(characteristic, int.Parse(restoredNumber), null);
                        characteristic.BalloonPersistId = balloonPersistId;

                        result.Changed = true;
                    });
                }
            });
        }

        // Marks each checked, not-yet-unnumbered row as excluded from
        // inspection, same as Un-Number did - a checked balloon gets its
        // note deleted (number freed for reuse), a checked bare dimension
        // gets a reserved Characteristic created for it. Reversed by
        // ReNumberBalloons.
        //
        // Multi-value balloons: un-numbering the main row deletes the one
        // shared note, so every one of its values is un-numbered with it
        // (otherwise "6.1"/"6.2" would stay numbered with no balloon on the
        // drawing). Un-numbering just one of its decimal values ("6.1")
        // only excludes that value - the shared note stays for the rest.
        public static GridOperationResult MarkUnnumbered(
            ModelDoc2 model,
            string dataFilePath,
            List<BalloonGridRow> checkedRows,
            ProjectData projectData)
        {
            return Execute(model, dataFilePath, projectData, (result, batch) =>
            {
                BalloonUnits units = new BalloonUnits(projectData);

                HashSet<Characteristic> checkedCharacteristics = new HashSet<Characteristic>(
                    checkedRows.Where(r => r.Characteristic != null).Select(r => r.Characteristic));

                foreach (BalloonGridRow row in checkedRows)
                {
                    if (row.Characteristic == null && row.AnnotationSource == null)
                        continue;

                    TryRow(result, Describe(row), () =>
                    {
                        Characteristic characteristic = row.Characteristic;

                        // Also catches a value already un-numbered earlier in
                        // this same batch along with its main row.
                        if (characteristic != null && characteristic.IsUnnumbered)
                            return;

                        bool isSibling = characteristic != null && units.IsSibling(characteristic);

                        // A group's first member's balloon stands for the
                        // whole group - deleting it alone would leave "10.1"
                        // etc. numbered with no balloon anywhere.
                        if (characteristic != null && !isSibling)
                        {
                            List<Characteristic> carried = GroupMembersSharing(characteristic, projectData)
                                .Where(m => !checkedCharacteristics.Contains(m))
                                .ToList();

                            if (carried.Count > 0)
                            {
                                result.Errors.Add(
                                    Describe(row) + " is the balloon for its whole group (" + JoinNumbers(carried) +
                                    ") - check the whole group to un-number it, or ungroup it first.");
                                return;
                            }
                        }

                        if (characteristic != null && characteristic.Number > 0 && !isSibling &&
                            !characteristic.SharesGroupBalloon)
                        {
                            string displayNumber = characteristic.DisplayNumber;
                            Note note = batch.FindBalloon(displayNumber);

                            // Not found = already gone from the drawing, which
                            // is the goal anyway; only a failed delete of a
                            // balloon that IS there leaves this row alone.
                            if (note != null && !batch.DeleteBalloon(note, displayNumber))
                            {
                                result.Errors.Add(
                                    Describe(row) + ": the balloon couldn't be deleted from the drawing, so it was left numbered.");
                                return;
                            }
                        }

                        result.Changed = true;

                        if (characteristic == null)
                            characteristic = EnsureCharacteristic(row, projectData);

                        ClearNumber(characteristic);

                        if (!isSibling)
                        {
                            foreach (Characteristic sibling in units.SiblingsOf(characteristic))
                                ClearNumber(sibling);
                        }
                    });
                }
            });
        }

        // Reverses MarkUnnumbered for each checked, currently-unnumbered
        // row: assigns it the next available balloon number and creates a
        // balloon note for it (same as Add Balloon), clearing the
        // unnumbered flag. A multi-value balloon's main row brings all its
        // values back with it; a single value ("6.1") on its own rejoins
        // its main row's balloon, which must already be numbered.
        public static GridOperationResult ReNumberBalloons(
            ModelDoc2 model,
            string dataFilePath,
            List<BalloonGridRow> checkedRows,
            ProjectData projectData)
        {
            return Execute(model, dataFilePath, projectData, (result, batch) =>
            {
                PrepareNumbering(projectData);
                BalloonUnits units = new BalloonUnits(projectData);

                // Main rows first, so a value checked alongside its own main
                // row is already rejoined by the time it's reached.
                List<BalloonGridRow> ordered = checkedRows
                    .OrderBy(r => r.Characteristic != null && units.IsSibling(r.Characteristic) ? 1 : 0)
                    .ToList();

                foreach (BalloonGridRow row in ordered)
                {
                    Characteristic characteristic = row.Characteristic;

                    if (characteristic == null || !characteristic.IsUnnumbered)
                        continue;

                    TryRow(result, Describe(row), () =>
                    {
                        if (!characteristic.IsUnnumbered)
                            return;

                        if (units.IsSibling(characteristic))
                        {
                            RejoinSibling(characteristic, units, result);
                            return;
                        }

                        int nextNumber = NextNumber(characteristic.SheetName ?? row.SheetName, projectData);
                        string balloonPersistId = null;

                        if (row.AnnotationSource != null)
                        {
                            Note note = batch.CreateBalloon(row, nextNumber.ToString(), result);

                            if (note == null)
                                return;

                            balloonPersistId = batch.GetPersistId(note);
                        }
                        else
                        {
                            result.Warnings.Add(
                                Describe(row) + " was numbered " + nextNumber + ", but it has no live dimension " +
                                "in this grid to place a balloon on - run Refresh Balloons to draw it.");
                        }

                        result.Changed = true;

                        AssignNumber(characteristic, nextNumber, null);

                        if (balloonPersistId != null)
                            characteristic.BalloonPersistId = balloonPersistId;

                        FollowAnchor(characteristic, units, rejoinUnnumbered: true);
                    });
                }
            });
        }

        // Manually assigns/moves one row to an explicitly-typed balloon
        // number (e.g. "5" or "5.2"), including a grouped one - the direct
        // way to build or edit a group without going through Group
        // Balloon's "checked rows get consecutive sub-numbers" flow.
        // Intentionally simpler than LegacyRenumberService's bystander-
        // shifting plan builder: a collision with another EXISTING balloon
        // is offered to the user as a straight two-way swap via
        // confirmSwap, rather than silently displacing a third balloon out
        // of the way. A declined swap returns an empty result (nothing
        // changed, nothing to report).
        //
        // Multi-value balloons move as a unit: renumbering the main row
        // moves every value with it, only to a whole number no other
        // balloon is using (no swap - the other side would have to absorb
        // several decimal numbers). One of its values ("6.1") can only be
        // given back its own place on its main row's balloon.
        //
        // User groups follow GroupBalloons' one-balloon rule: typing "N.k"
        // where balloon N already exists joins N's balloon (this row's own
        // note is deleted, it becomes data-only); typing a whole number, or
        // "N.k" with no N, on a data-only member gives it a balloon of its
        // own again; renumbering a group's balloon moves its data-only
        // members with it, to a whole number nothing else uses. Swaps
        // involving a grouped balloon are refused rather than guessed at.
        public static GridOperationResult TryManualRenumber(
            ModelDoc2 model,
            string dataFilePath,
            BalloonGridRow row,
            string newNumberText,
            ProjectData projectData,
            Func<string, bool> confirmSwap)
        {
            return Execute(model, dataFilePath, projectData, (result, batch) =>
            {
                int number;
                int? subNumber;
                string displayNumber;

                if (!LegacyRenumberService.TryParseDisplayNumber(
                    newNumberText, out number, out subNumber, out displayNumber))
                {
                    result.Errors.Add("\"" + newNumberText + "\" isn't a valid balloon number - use \"5\" or \"5.2\".");
                    return;
                }

                BalloonUnits units = new BalloonUnits(projectData);
                Characteristic characteristic = row.Characteristic;

                if (characteristic != null && units.IsSibling(characteristic))
                {
                    RenumberSibling(characteristic, displayNumber, units, result);

                    if (result.Changed)
                        SyncRowNumber(row);

                    return;
                }

                bool isNewCharacteristic = characteristic == null;

                if (isNewCharacteristic)
                {
                    if (row.AnnotationSource == null)
                    {
                        result.Errors.Add("This row has no dimension to balloon.");
                        return;
                    }

                    characteristic = NewCharacteristicFor(row);
                }

                bool hadBalloon = !isNewCharacteristic && !characteristic.IsUnnumbered && characteristic.Number > 0;
                string oldDisplayNumber = characteristic.DisplayNumber;

                if (hadBalloon && displayNumber == oldDisplayNumber)
                    return;

                IReadOnlyList<Characteristic> siblings = units.SiblingsOf(characteristic);
                bool isMultiValue = siblings.Count > 0;

                // Group bookkeeping (see Characteristic.SharesGroupBalloon):
                // whether this row is currently a data-only group member, the
                // data-only members its own balloon stands for, and - for a
                // typed "N.k" - the existing anchor "N" whose one balloon it
                // joins instead of keeping/getting a balloon of its own.
                bool isDataOnlyMember = hadBalloon && characteristic.SharesGroupBalloon;

                List<Characteristic> carriedMembers = hadBalloon
                    ? GroupMembersSharing(characteristic, projectData)
                    : new List<Characteristic>();

                Characteristic joinAnchor = subNumber.HasValue
                    ? projectData.Characteristics.FirstOrDefault(o =>
                        o != characteristic && !o.IsUnnumbered && o.Number == number &&
                        !o.SubNumber.HasValue && !o.SharesGroupBalloon && !units.IsMultiValue(o))
                    : null;

                Characteristic collidingWith = null;

                if (isMultiValue)
                {
                    if (subNumber.HasValue)
                    {
                        result.Errors.Add(
                            "Balloon " + oldDisplayNumber + " is a multi-value balloon (" +
                            JoinNumbers(new[] { characteristic }.Concat(siblings.Where(s => !s.IsUnnumbered))) +
                            ") - it can only be renumbered to a whole number.");
                        return;
                    }

                    Characteristic taken = projectData.Characteristics.FirstOrDefault(o =>
                        o != characteristic && !siblings.Contains(o) && !o.IsUnnumbered && o.Number == number);

                    if (taken != null)
                    {
                        result.Errors.Add(
                            "Balloon " + taken.DisplayNumber + " is already assigned to " + taken.DimensionName +
                            ". A multi-value balloon needs a whole number with no other balloons on it - pick an unused one.");
                        return;
                    }
                }
                else
                {
                    Characteristic multiValueOnTarget = projectData.Characteristics.FirstOrDefault(o =>
                        o != characteristic && !o.IsUnnumbered && o.Number == number && units.IsMultiValue(o));

                    if (multiValueOnTarget != null)
                    {
                        result.Errors.Add(
                            "Balloon " + number + " is a multi-value balloon (hole callout, chamfer, composite GD&T " +
                            "frame or multi-line note) - its numbers can't be shared or swapped. Pick a different number.");
                        return;
                    }

                    // A group's balloon takes its data-only members with it
                    // ("10" -> "12" makes "10.1" -> "12.1"), so it needs a
                    // whole number nothing else is using.
                    if (carriedMembers.Count > 0)
                    {
                        if (subNumber.HasValue)
                        {
                            result.Errors.Add(
                                "Balloon " + oldDisplayNumber + " is the balloon for its whole group (" +
                                JoinNumbers(carriedMembers) + ") - it can only be renumbered to a whole number. " +
                                "Ungroup it first to move it into another group.");
                            return;
                        }

                        Characteristic taken = projectData.Characteristics.FirstOrDefault(o =>
                            o != characteristic && !carriedMembers.Contains(o) && !o.IsUnnumbered && o.Number == number);

                        if (taken != null)
                        {
                            result.Errors.Add(
                                "Balloon " + taken.DisplayNumber + " is already assigned to " + taken.DimensionName +
                                ". A group's balloon needs a whole number with no other balloons on it - pick an unused one.");
                            return;
                        }
                    }

                    collidingWith = projectData.Characteristics
                        .FirstOrDefault(c => c != characteristic && !c.IsUnnumbered && c.DisplayNumber == displayNumber);

                    if (collidingWith != null)
                    {
                        if (!hadBalloon)
                        {
                            result.Errors.Add(
                                "Balloon " + displayNumber + " is already assigned to " + collidingWith.DimensionName +
                                " - delete it or pick a different number first.");
                            return;
                        }

                        if (isDataOnlyMember || joinAnchor != null || collidingWith.SharesGroupBalloon ||
                            GroupMembersSharing(collidingWith, projectData).Count > 0)
                        {
                            result.Errors.Add(
                                "Balloon " + displayNumber + " is already assigned to " + collidingWith.DimensionName +
                                ", and grouped balloons can't be swapped - pick an unused number.");
                            return;
                        }

                        if (confirmSwap == null || !confirmSwap(displayNumber))
                            return;
                    }
                }

                // Where this row's balloon ends up: sharing joinAnchor's one
                // balloon (no note of its own), keeping its own note under
                // the new number, or getting a new note because it had none.
                bool joinsGroup = joinAnchor != null;
                bool keepsOwnNote = hadBalloon && !isDataOnlyMember && !joinsGroup;
                bool needsNewNote = !joinsGroup && (!hadBalloon || isDataOnlyMember);

                Note ownNote = hadBalloon && !isDataOnlyMember ? batch.FindBalloon(oldDisplayNumber) : null;
                Note collidingNote = collidingWith != null ? batch.FindBalloon(collidingWith.DisplayNumber) : null;

                // Placing a brand-new balloon is the one step that can fail
                // without throwing - do it before touching any data so a
                // failure leaves nothing to undo.
                Note created = null;

                if (needsNewNote)
                {
                    object source = row.AnnotationSource ??
                        (isNewCharacteristic ? null : ResolveSource(model, characteristic));

                    if (source != null)
                    {
                        created = batch.CreateBalloon(
                            source, row.SheetName ?? characteristic.SheetName, displayNumber, Describe(row), result);

                        if (created == null)
                            return;
                    }
                    else
                    {
                        result.Warnings.Add(
                            Describe(row) + " was numbered " + displayNumber + ", but its source couldn't be " +
                            "found to place a balloon on - run Refresh Balloons to draw it.");
                    }
                }

                List<NumberSnapshot> snapshots = new[] { characteristic, collidingWith }
                    .Concat(siblings)
                    .Concat(carriedMembers)
                    .Where(c => c != null)
                    .Select(c => new NumberSnapshot(c))
                    .ToList();

                int renameMark = batch.RenameCount;

                try
                {
                    if (collidingWith != null)
                    {
                        // Swap: the balloon currently sitting on the target number
                        // takes this row's OLD number instead of being displaced to
                        // some arbitrary free one.
                        string collidingOldDisplayNumber = collidingWith.DisplayNumber;

                        collidingWith.Number = characteristic.Number;
                        collidingWith.SubNumber = characteristic.SubNumber;

                        if (collidingNote != null)
                            batch.Rename(collidingNote, collidingOldDisplayNumber, collidingWith.DisplayNumber);
                        else
                            result.Warnings.Add(NoteNotFoundWarning(collidingOldDisplayNumber, collidingWith.DisplayNumber));
                    }

                    AssignNumber(characteristic, number, subNumber);

                    if (joinsGroup)
                    {
                        characteristic.SharesGroupBalloon = true;
                        characteristic.BalloonPersistId = joinAnchor.BalloonPersistId;
                    }
                    else if (keepsOwnNote)
                    {
                        if (ownNote != null)
                            batch.Rename(ownNote, oldDisplayNumber, characteristic.DisplayNumber);
                        else
                            result.Warnings.Add(NoteNotFoundWarning(oldDisplayNumber, characteristic.DisplayNumber));
                    }
                    else if (created != null)
                    {
                        characteristic.BalloonPersistId = batch.GetPersistId(created);
                    }

                    foreach (Characteristic member in carriedMembers)
                        member.Number = number;

                    if (isNewCharacteristic)
                        projectData.Characteristics.Add(characteristic);

                    row.Characteristic = characteristic;

                    if (isMultiValue)
                        FollowAnchor(characteristic, units, rejoinUnnumbered: !hadBalloon);

                    SyncRowNumber(row);

                    result.Changed = true;
                }
                catch (Exception ex)
                {
                    if (isNewCharacteristic)
                    {
                        projectData.Characteristics.Remove(characteristic);
                        row.Characteristic = null;
                    }

                    RollBack(result, batch, snapshots, renameMark, "Renumbering", ex);
                    return;
                }

                // Joining a group's balloon: this row's own old note goes,
                // but only once the renumber has committed - a delete can't
                // be rolled back.
                if (joinsGroup && ownNote != null && !batch.DeleteBalloon(ownNote, oldDisplayNumber))
                {
                    result.Warnings.Add(
                        "Balloon " + oldDisplayNumber + " is now " + displayNumber + " in group " + number +
                        ", but its old balloon couldn't be removed from the drawing - delete it by hand.");
                }
            });
        }

        // Saves Inspection Method/Classification for one row, same fields
        // the retired single-balloon and bulk attribute editors used to
        // write - only meaningful for a row that already has a
        // Characteristic (HasCharacteristic), since there's nowhere to
        // persist attributes for a bare unballooned dimension yet.
        // Newly-typed Method/Class values are added to the project's
        // option lists for next time, same as those retired editors did.
        public static GridOperationResult UpdateAttributes(
            string dataFilePath,
            BalloonGridRow row,
            string method,
            string classification,
            ProjectData projectData)
        {
            return Execute(null, dataFilePath, projectData, (result, batch) =>
            {
                if (row.Characteristic == null)
                    return;

                row.Characteristic.Method = method;
                row.Characteristic.Class = classification;

                row.Method = method;
                row.Class = classification;

                AddIfNew(projectData.MethodOptions, method);
                AddIfNew(projectData.ClassOptions, classification);

                result.Changed = true;
            });
        }

        // Bulk form of UpdateAttributes - writes the same Method and/or
        // Classification to every checked row in one action instead of
        // retyping it into each row's combo box, then saves once instead of
        // once per row. A blank method/classification argument leaves that
        // field alone on every row (so the user can bulk-set just one of
        // the two), same null/blank-means-don't-touch convention as the
        // Legacy Conversion import. Rows without a Characteristic
        // (unballooned, nowhere to persist attributes yet) are silently
        // skipped, same as the single-row editor disables them.
        public static GridOperationResult UpdateAttributesForRows(
            string dataFilePath,
            IEnumerable<BalloonGridRow> rows,
            string method,
            string classification,
            ProjectData projectData)
        {
            return Execute(null, dataFilePath, projectData, (result, batch) =>
            {
                bool setMethod = !string.IsNullOrWhiteSpace(method);
                bool setClass = !string.IsNullOrWhiteSpace(classification);

                if (!setMethod && !setClass)
                    return;

                foreach (BalloonGridRow row in rows)
                {
                    if (row.Characteristic == null)
                        continue;

                    if (setMethod)
                    {
                        row.Characteristic.Method = method;
                        row.Method = method;
                    }

                    if (setClass)
                    {
                        row.Characteristic.Class = classification;
                        row.Class = classification;
                    }
                }

                if (setMethod)
                    AddIfNew(projectData.MethodOptions, method);

                if (setClass)
                    AddIfNew(projectData.ClassOptions, classification);

                result.Changed = true;
            });
        }

        // Toggles Balloon Manager's Basic checkbox for one row: sets the
        // live SolidWorks dimension's tolerance TYPE to Basic (no text of
        // any kind is forced into the Dimension cell here anymore - see
        // BalloonGridService.GetDimensionDisplay's remarks; only the report's
        // Upper/Lower Limit columns and its sheet-tolerance fallback care
        // about IsBasic now), and - for a row that still has a live dimension -
        // pushes the same change onto the real drawing by setting
        // IDimensionTolerance.Type to swTolBASIC, same live-apply
        // convention as every other action here. The dimension's prior
        // tolerance type is remembered (Characteristic.PreBasicToleranceType)
        // so un-checking restores it instead of always dropping to "no
        // tolerance" - a real tolerance's underlying min/max values are
        // never touched either way, only which type is currently displayed.
        // Always persists IsBasic on the Characteristic (creating one if
        // this row didn't have one yet) so the state survives even for a
        // row with no live AnnotationSource. Refused only if this row has
        // neither a live dimension nor an existing Characteristic to create
        // one against. The box sketch is cosmetic - failing to draw/remove
        // it is reported as a warning, never undoes the Basic change itself.
        public static GridOperationResult SetBasic(
            ModelDoc2 model,
            string dataFilePath,
            BalloonGridRow row,
            bool isBasic,
            ProjectData projectData)
        {
            return Execute(model, dataFilePath, projectData, (result, batch) =>
            {
                Characteristic characteristic = row.Characteristic;

                if (characteristic == null && row.AnnotationSource == null)
                {
                    result.Errors.Add("This row has no dimension to mark Basic.");
                    return;
                }

                if (row.AnnotationSource == null)
                {
                    characteristic.IsBasic = isBasic;
                    row.IsBasic = isBasic;
                    result.Changed = true;
                    return;
                }

                Dimension swDim = row.AnnotationSource.GetDimension2(0);
                IDimensionTolerance tolerance = swDim?.Tolerance;
                int? preBasicToleranceType = characteristic?.PreBasicToleranceType;

                if (tolerance != null)
                {
                    if (isBasic)
                    {
                        int currentType = tolerance.Type;

                        if (currentType != (int)swTolType_e.swTolBASIC)
                        {
                            tolerance.Type = (int)swTolType_e.swTolBASIC;
                            preBasicToleranceType = currentType;
                        }
                    }
                    else
                    {
                        tolerance.Type = preBasicToleranceType ?? (int)swTolType_e.swTolNONE;
                        preBasicToleranceType = null;
                    }

                    batch.AnyDrawingChange = true;
                }

                result.Changed = true;

                if (characteristic == null)
                    characteristic = EnsureCharacteristic(row, projectData);

                characteristic.PreBasicToleranceType = preBasicToleranceType;
                characteristic.IsBasic = isBasic;
                row.IsBasic = isBasic;

                characteristic.DimensionName =
                    GetDimensionDisplay(model, row.AnnotationSource, swDim, isBasic);

                string boxSheetName = row.SheetName ?? characteristic.SheetName;

                try
                {
                    if (isBasic)
                    {
                        if (string.IsNullOrEmpty(characteristic.BasicBoxSketchName))
                        {
                            characteristic.BasicBoxSketchName = DimensionBoxSketchService.AddBox(
                                model, model as DrawingDoc, row.AnnotationSource, boxSheetName);

                            if (characteristic.BasicBoxSketchName == null)
                                result.Warnings.Add("Marked Basic, but the box around the dimension couldn't be drawn.");
                        }
                    }
                    else if (!string.IsNullOrEmpty(characteristic.BasicBoxSketchName))
                    {
                        DimensionBoxSketchService.RemoveBox(
                            model, model as DrawingDoc, characteristic.BasicBoxSketchName, boxSheetName);
                        characteristic.BasicBoxSketchName = null;
                    }

                    batch.AnyDrawingChange = true;
                }
                catch (Exception ex)
                {
                    BinspectionLog.Error("BalloonGridService.SetBasic box sketch", ex);

                    result.Warnings.Add(
                        (isBasic ? "Marked Basic, but the box around the dimension couldn't be drawn" :
                                   "Basic removed, but the box around the dimension couldn't be deleted") +
                        " (" + ex.Message + ").");
                }
            });
        }

        // Moves a checked/selected balloon (and every sibling data-only
        // characteristic that shares its one physical balloon note - see
        // Characteristic.BalloonPersistId's remarks on multi-value/
        // composite GD&T groups) onto a different sheet, for when Create
        // Balloons (or the bug it used to have - see
        // [[binspection_balloon_wrong_sheet_placement_bug]]) got the sheet
        // wrong. Picking the move from one of a multi-value balloon's
        // decimal rows ("6.1") moves its main row's note, since that's the
        // only physical balloon there is. A no-op if the balloon is already
        // on targetSheetName. Refused if the row has no live balloon to
        // move, or the move itself fails (e.g. targetSheetName can't be
        // activated) - see BalloonManager.MoveBalloon.
        public static GridOperationResult MoveBalloonToSheet(
            ModelDoc2 model,
            string dataFilePath,
            BalloonGridRow row,
            string targetSheetName,
            ProjectData projectData)
        {
            return Execute(model, dataFilePath, projectData, (result, batch) =>
            {
                Characteristic characteristic = row.Characteristic;
                BalloonUnits units = new BalloonUnits(projectData);

                Characteristic owner = characteristic != null && units.IsSibling(characteristic)
                    ? units.AnchorOf(characteristic)
                    : characteristic;

                if (characteristic == null || characteristic.IsUnnumbered || characteristic.Number <= 0 ||
                    owner == null || owner.IsUnnumbered || owner.Number <= 0)
                {
                    result.Errors.Add("This row has no balloon on the drawing to move.");
                    return;
                }

                if (characteristic.SharesGroupBalloon)
                {
                    result.Errors.Add(
                        "Balloon " + characteristic.DisplayNumber + " has no balloon of its own - its group's balloon " +
                        "(" + characteristic.Number + ") stands for it. Move that one instead.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(targetSheetName))
                {
                    result.Errors.Add("Pick a target sheet first.");
                    return;
                }

                if (string.Equals(owner.SheetName, targetSheetName, StringComparison.OrdinalIgnoreCase))
                    return;

                Note movedNote = batch.MoveBalloon(owner.DisplayNumber, targetSheetName);

                if (movedNote == null)
                {
                    result.Errors.Add(
                        "Could not move balloon " + owner.DisplayNumber + " to sheet \"" + targetSheetName + "\"" +
                        (batch.Manager.LastFailureReason != null ? " (" + batch.Manager.LastFailureReason + ")" : "") +
                        ".");
                    return;
                }

                result.Changed = true;

                string newBalloonPersistId = batch.GetPersistId(movedNote);

                // Every sibling that shares this one physical balloon (a
                // multi-value hole-callout/chamfer or composite GD&T group)
                // needs the same updated SheetName/BalloonPersistId, or the
                // report's per-row "Sheet" column would disagree with itself
                // for members of the same group.
                string oldBalloonPersistId = owner.BalloonPersistId;

                IEnumerable<Characteristic> sharing = string.IsNullOrEmpty(oldBalloonPersistId)
                    ? Enumerable.Empty<Characteristic>()
                    : projectData.Characteristics.Where(c => c.BalloonPersistId == oldBalloonPersistId);

                foreach (Characteristic member in new[] { owner }.Concat(units.SiblingsOf(owner)).Concat(sharing).Distinct())
                {
                    // A user group's data-only members are their own
                    // features on their own sheets - only the balloon they
                    // share moved, not them.
                    if (!member.SharesGroupBalloon)
                        member.SheetName = targetSheetName;

                    member.BalloonPersistId = newBalloonPersistId;
                }

                foreach (Characteristic member in GroupMembersSharing(owner, projectData))
                    member.BalloonPersistId = newBalloonPersistId;

                row.SheetName = targetSheetName;
            });
        }

        // The balloon note standing for characteristic on the drawing, for
        // Balloon Manager's jump-to-row - found WITHOUT cycling through
        // every sheet the way a plain FindExistingBalloon walk does
        // (DrawingSheetHelper.GetAllViewsBySheet activates each sheet in
        // turn, which is slow and visibly flips through the whole drawing
        // on every row click). Cheapest first:
        //   1. resolve its saved BalloonPersistId directly - no walk at all;
        //   2. if that id is stale (e.g. Refresh Balloons recreated the
        //      note), walk only the characteristic's own sheet, which the
        //      caller has already activated;
        //   3. only if both miss (a balloon sitting on the wrong sheet),
        //      the full every-sheet walk.
        // A data-only group member or a multi-value balloon's decimal value
        // has no note of its own, so its shared whole-number balloon is
        // looked up instead ("10.1" -> "10"). Returns null if there's no
        // balloon for it.
        public static IAnnotation FindBalloonAnnotation(ModelDoc2 model, Characteristic characteristic)
        {
            if (model == null || characteristic == null || characteristic.IsUnnumbered || characteristic.Number <= 0)
                return null;

            bool usesSharedNote =
                characteristic.SharesGroupBalloon ||
                (characteristic.PersistentRefId != null && characteristic.PersistentRefId.IndexOf('#') > 0);

            string displayNumber = usesSharedNote
                ? characteristic.Number.ToString()
                : characteristic.DisplayNumber;

            IAnnotation byId = ResolveBalloonById(model, characteristic.BalloonPersistId, displayNumber);

            if (byId != null)
                return byId;

            BalloonManager balloonManager = new BalloonManager();
            DrawingDoc drawing = model as DrawingDoc;

            if (!string.IsNullOrEmpty(characteristic.SheetName))
            {
                Note onOwnSheet = balloonManager.FindExistingBalloon(
                    model, displayNumber, DrawingSheetHelper.GetAllViewsBySheet(drawing, new[] { characteristic.SheetName }));

                if (onOwnSheet != null)
                    return onOwnSheet.GetAnnotation() as IAnnotation;
            }

            return balloonManager.FindExistingBalloon(model, displayNumber)?.GetAnnotation() as IAnnotation;
        }

        // Step 1 of FindBalloonAnnotation. The id alone is trusted only if
        // it still points at a note reading displayNumber - a note deleted
        // and replaced by Refresh Balloons leaves the old id dangling or,
        // in principle, pointing somewhere else. Works on the interfaces
        // (IAnnotation/INote), not the coclasses: objects handed back by
        // the persistent-reference resolver don't reliably cast to the
        // coclass types (see ReconciliationService.Reconcile's remarks).
        private static IAnnotation ResolveBalloonById(ModelDoc2 model, string balloonPersistId, string displayNumber)
        {
            if (string.IsNullOrEmpty(balloonPersistId))
                return null;

            try
            {
                swPersistReferencedObjectStates_e state;
                object resolved = PersistentReferenceHelper.ResolvePersistentId(model, balloonPersistId, out state);

                if (resolved == null)
                    return null;

                INote note = resolved as INote;
                IAnnotation annotation = resolved as IAnnotation;

                if (note == null && annotation != null)
                    note = annotation.GetSpecificAnnotation() as INote;
                else if (annotation == null && note != null)
                    annotation = note.GetAnnotation() as IAnnotation;

                if (note == null || annotation == null)
                    return null;

                return BalloonManager.NormalizeNoteText(note.GetText()) == displayNumber ? annotation : null;
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("BalloonGridService.ResolveBalloonById", ex);
                return null;
            }
        }

        // ---- Shared plumbing ------------------------------------------

        // Runs one grid action with the same safety net every action gets:
        // an unexpected exception is reported instead of escaping into
        // SolidWorks' UI thread, the drawing is redrawn once if anything
        // on it changed, and the project file is saved whenever the data
        // changed - including after a partial failure, so what DID happen
        // on the drawing is never lost from the saved data. A failed save
        // is reported (SaveFailed) rather than ignored.
        private static GridOperationResult Execute(
            ModelDoc2 model,
            string dataFilePath,
            ProjectData projectData,
            Action<GridOperationResult, BalloonBatch> body)
        {
            GridOperationResult result = new GridOperationResult();
            BalloonBatch batch = new BalloonBatch(model);

            try
            {
                body(result, batch);
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("BalloonGridService", ex);
                result.Errors.Add("Unexpected error: " + ex.Message);
            }

            if (batch.AnyDrawingChange && model != null)
            {
                try
                {
                    model.GraphicsRedraw2();
                }
                catch (Exception ex)
                {
                    BinspectionLog.Error("BalloonGridService GraphicsRedraw2", ex);
                }
            }

            if (result.Changed && !PersistenceManager.SaveProject(dataFilePath, projectData))
                result.SaveFailed = true;

            return result;
        }

        // Per-row isolation for bulk actions: one row throwing is reported
        // against that row and the loop moves on to the next one.
        private static void TryRow(GridOperationResult result, string label, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("BalloonGridService row (" + label + ")", ex);
                result.Errors.Add(label + ": " + ex.Message);
            }
        }

        // Undoes an all-or-nothing action (Group, Manual renumber) that
        // threw partway: restores every snapshotted number, then renames
        // back whatever notes were already renamed. Any note that won't
        // rename back leaves the drawing out of step with the (restored)
        // data, which Refresh Balloons fixes by redrawing from the data.
        private static void RollBack(
            GridOperationResult result,
            BalloonBatch batch,
            List<NumberSnapshot> snapshots,
            int renameMark,
            string action,
            Exception ex)
        {
            BinspectionLog.Error("BalloonGridService " + action, ex);

            foreach (NumberSnapshot snapshot in snapshots)
                snapshot.Restore();

            bool notesRestored = batch.UndoRenamesSince(renameMark);

            result.Changed = false;
            result.Errors.Add(
                action + " failed (" + ex.Message + ") and was rolled back." +
                (notesRestored ? "" : " Some balloon notes couldn't be put back - run Refresh Balloons to redraw them from the saved data."));
        }

        // Clears and reloads the static CharacteristicManager tracker from
        // this drawing's data, so numbers tracked for a different drawing
        // earlier in the session can't leak into NextNumber.
        private static void PrepareNumbering(ProjectData projectData)
        {
            CharacteristicManager.Clear();
            CharacteristicManager.LoadFrom(projectData.Characteristics);
        }

        // Next balloon number for a row on sheetName - that sheet's
        // reserved range first (see Models/BalloonNumberRange.cs), same as
        // Create Balloons, then the drawing-wide next number. Every number
        // currently in projectData is passed as reserved, so a number
        // assigned earlier in the same action (or held by a record the
        // tracker skipped for having no PersistentRefId) is never reissued.
        private static int NextNumber(string sheetName, ProjectData projectData)
        {
            return CharacteristicManager.GetNextNumberForSheet(
                sheetName,
                projectData.NumberRanges,
                projectData.Characteristics
                    .Where(c => !c.IsUnnumbered && c.Number > 0)
                    .Select(c => c.Number)
                    .ToList());
        }

        // The number an ungrouped balloon goes back to: its pre-group
        // number if it recorded one and nothing else has claimed it since,
        // otherwise the next free number for its sheet.
        private static int RestorableNumber(Characteristic characteristic, ProjectData projectData)
        {
            int? preGroupNumber = characteristic.PreGroupNumber;

            if (preGroupNumber.HasValue && preGroupNumber.Value > 0 &&
                !projectData.Characteristics.Any(o =>
                    o != characteristic && !o.IsUnnumbered && o.Number == preGroupNumber.Value))
                return preGroupNumber.Value;

            return NextNumber(characteristic.SheetName, projectData);
        }

        // A fresh (not yet saved) Characteristic for a live-dimension row
        // with no record of its own yet.
        private static Characteristic NewCharacteristicFor(BalloonGridRow row)
        {
            return new Characteristic
            {
                PersistentRefId = row.PersistentRefId,
                DimensionName = row.DimensionDisplay,
                SheetName = row.SheetName,
                IsBasic = row.IsBasic,
            };
        }

        // The row's Characteristic, creating and recording one first if it
        // doesn't have one yet.
        private static Characteristic EnsureCharacteristic(BalloonGridRow row, ProjectData projectData)
        {
            if (row.Characteristic != null)
                return row.Characteristic;

            Characteristic characteristic = NewCharacteristicFor(row);

            projectData.Characteristics.Add(characteristic);
            row.Characteristic = characteristic;

            return characteristic;
        }

        private static void AssignNumber(Characteristic characteristic, int number, int? subNumber)
        {
            characteristic.Number = number;
            characteristic.SubNumber = subNumber;
            characteristic.PreGroupNumber = null;
            characteristic.IsUnnumbered = false;
            characteristic.SharesGroupBalloon = false;

            CharacteristicManager.AddCharacteristic(characteristic);
        }

        private static void ClearNumber(Characteristic characteristic)
        {
            characteristic.IsUnnumbered = true;
            characteristic.SubNumber = null;
            characteristic.PreGroupNumber = null;
            characteristic.SharesGroupBalloon = false;
            characteristic.Number = 0;
        }

        // The data-only members ("10.1", "10.2") a group's first member's
        // one balloon stands for - see Characteristic.SharesGroupBalloon.
        // Empty for anything that isn't a numbered group anchor.
        private static List<Characteristic> GroupMembersSharing(Characteristic anchor, ProjectData projectData)
        {
            if (anchor.IsUnnumbered || anchor.Number <= 0 || anchor.SubNumber.HasValue || anchor.SharesGroupBalloon)
                return new List<Characteristic>();

            return projectData.Characteristics
                .Where(c => c != anchor && !c.IsUnnumbered && c.SharesGroupBalloon && c.Number == anchor.Number)
                .OrderBy(c => c.SubNumber ?? 0)
                .ToList();
        }

        // The live dimension/GD&T frame/note/surface finish a saved
        // characteristic points at, for drawing a balloon on a record the
        // grid has no live row source for (a data-only group member).
        // Same resolution steps as ReconciliationService.Reconcile. Null if
        // it no longer resolves.
        private static object ResolveSource(ModelDoc2 model, Characteristic characteristic)
        {
            string refId = characteristic.PersistentRefId;

            if (model == null || string.IsNullOrEmpty(refId))
                return null;

            int syntheticMarker = refId.IndexOf('#');

            if (syntheticMarker >= 0)
                refId = refId.Substring(0, syntheticMarker);

            swPersistReferencedObjectStates_e state;
            object resolved = PersistentReferenceHelper.ResolvePersistentId(model, refId, out state);

            if (resolved == null)
                return null;

            object source =
                resolved as IDisplayDimension ??
                (object)(resolved as IGtol) ??
                (object)(resolved as INote) ??
                (object)(resolved as ISFSymbol);

            if (source == null)
            {
                object specific = (resolved as IAnnotation)?.GetSpecificAnnotation();

                source =
                    specific as IDisplayDimension ??
                    (object)(specific as IGtol) ??
                    (object)(specific as INote) ??
                    (object)(specific as ISFSymbol);
            }

            return source;
        }

        // After a multi-value balloon's main row gets a (new) number, its
        // values follow it onto that number at their own fixed decimal
        // places, sharing its one note. rejoinUnnumbered: whether values
        // the user individually un-numbered come back too - true when the
        // balloon itself is coming back from un-numbered (they all went
        // together), false when an already-ballooned main row is just
        // moving to a different number.
        private static void FollowAnchor(Characteristic anchor, BalloonUnits units, bool rejoinUnnumbered)
        {
            foreach (Characteristic sibling in units.SiblingsOf(anchor))
            {
                if (sibling.IsUnnumbered && !rejoinUnnumbered)
                    continue;

                AssignNumber(sibling, anchor.Number, BalloonUnits.SiblingSubNumber(sibling));
                sibling.BalloonPersistId = anchor.BalloonPersistId;
            }
        }

        // Gives an individually un-numbered value of a multi-value balloon
        // its place back on its main row's balloon.
        private static void RejoinSibling(Characteristic sibling, BalloonUnits units, GridOperationResult result)
        {
            Characteristic anchor = units.AnchorOf(sibling);

            if (anchor == null || anchor.IsUnnumbered || anchor.Number <= 0)
            {
                result.Errors.Add(
                    Describe(sibling) + " is one value of a multi-value balloon whose main row isn't numbered - " +
                    "number the main row instead, which brings every value back.");
                return;
            }

            result.Changed = true;

            AssignNumber(sibling, anchor.Number, BalloonUnits.SiblingSubNumber(sibling));
            sibling.BalloonPersistId = anchor.BalloonPersistId;
        }

        // Manual renumber typed into one of a multi-value balloon's
        // decimal rows: the only number it can take is its own fixed place
        // on its main row's balloon.
        private static void RenumberSibling(
            Characteristic sibling, string displayNumber, BalloonUnits units, GridOperationResult result)
        {
            Characteristic anchor = units.AnchorOf(sibling);

            if (anchor == null || anchor.IsUnnumbered || anchor.Number <= 0)
            {
                RejoinSibling(sibling, units, result);
                return;
            }

            string slot = anchor.Number + "." + BalloonUnits.SiblingSubNumber(sibling);

            if (!sibling.IsUnnumbered && sibling.DisplayNumber == displayNumber)
                return;

            if (displayNumber != slot)
            {
                result.Errors.Add(
                    Describe(sibling) + " is one value of multi-value balloon " + anchor.DisplayNumber +
                    " and can only be numbered " + slot + ". To move the whole balloon, renumber its main row (" +
                    anchor.DisplayNumber + ").");
                return;
            }

            RejoinSibling(sibling, units, result);
        }

        // Brings a row's own number columns in line with its Characteristic,
        // for a caller that keeps using the same row object afterwards
        // (RenumberBalloonsWindow) instead of rebuilding rows via BuildRows.
        private static void SyncRowNumber(BalloonGridRow row)
        {
            Characteristic characteristic = row.Characteristic;
            bool hasBalloon = characteristic != null && !characteristic.IsUnnumbered && characteristic.Number > 0;

            row.HasBalloon = hasBalloon;
            row.IsUnnumbered = characteristic != null && characteristic.IsUnnumbered;
            row.DisplayNumber = hasBalloon ? characteristic.DisplayNumber : "";
        }

        private static string NoteNotFoundWarning(string oldDisplayNumber, string newDisplayNumber)
        {
            return "Balloon " + oldDisplayNumber + " wasn't found on the drawing, so only the saved data was " +
                "renumbered to " + newDisplayNumber + " - run Refresh Balloons to redraw it.";
        }

        private static string Describe(BalloonGridRow row)
        {
            if (!string.IsNullOrEmpty(row.DisplayNumber))
                return "Balloon " + row.DisplayNumber;

            return string.IsNullOrEmpty(row.DimensionDisplay) ? "A row" : "\"" + row.DimensionDisplay + "\"";
        }

        private static string Describe(Characteristic characteristic)
        {
            if (!characteristic.IsUnnumbered && characteristic.Number > 0)
                return "Balloon " + characteristic.DisplayNumber;

            return string.IsNullOrEmpty(characteristic.DimensionName)
                ? "A row"
                : "\"" + characteristic.DimensionName + "\"";
        }

        private static string JoinNumbers(IEnumerable<Characteristic> characteristics)
        {
            return string.Join(", ", characteristics.Select(c => c.DisplayNumber).Distinct());
        }

        private static void AddIfNew(List<string> options, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            string trimmed = value.Trim();

            if (!options.Any(o => string.Equals(o, trimmed, StringComparison.OrdinalIgnoreCase)))
                options.Add(trimmed);
        }

        // Which characteristics share one physical balloon note. Create
        // Balloons gives a multi-value hole callout/chamfer, composite
        // GD&T frame or multi-line note ONE note ("6") for its first value
        // and data-only sibling records for the rest ("6.1", "6.2"), whose
        // PersistentRefId is the first value's id plus "#<n>" (n = 2, 3,
        // ...; see CommandManagerHandler.OnCreateBalloons). Those siblings
        // share a Number with their main row but are NOT a user-made group
        // (GroupBalloons, where every member has its own note) - telling
        // the two apart is what keeps Ungroup/Un-Number/Renumber from
        // splitting one callout's values off onto numbers with no balloon.
        // Built once per action from the data as it stood at the start.
        private sealed class BalloonUnits
        {
            private static readonly IReadOnlyList<Characteristic> None = new List<Characteristic>();

            private readonly ProjectData _projectData;

            private readonly Dictionary<string, Characteristic> _byRefId =
                new Dictionary<string, Characteristic>();

            private readonly Dictionary<string, List<Characteristic>> _siblingsByAnchorId =
                new Dictionary<string, List<Characteristic>>();

            public BalloonUnits(ProjectData projectData)
            {
                _projectData = projectData;

                foreach (Characteristic characteristic in projectData.Characteristics)
                {
                    string refId = characteristic.PersistentRefId;

                    if (string.IsNullOrEmpty(refId))
                        continue;

                    if (!_byRefId.ContainsKey(refId))
                        _byRefId[refId] = characteristic;

                    string anchorId;
                    int index;

                    if (!TrySplitSiblingRefId(refId, out anchorId, out index))
                        continue;

                    List<Characteristic> siblings;

                    if (!_siblingsByAnchorId.TryGetValue(anchorId, out siblings))
                    {
                        siblings = new List<Characteristic>();
                        _siblingsByAnchorId[anchorId] = siblings;
                    }

                    siblings.Add(characteristic);
                }

                foreach (List<Characteristic> siblings in _siblingsByAnchorId.Values)
                    siblings.Sort((a, b) => (SiblingSubNumber(a) ?? 0).CompareTo(SiblingSubNumber(b) ?? 0));
            }

            public bool IsSibling(Characteristic characteristic)
            {
                string anchorId;
                int index;

                return characteristic != null &&
                    TrySplitSiblingRefId(characteristic.PersistentRefId, out anchorId, out index);
            }

            // The main row a sibling belongs to (null if that record is
            // gone), or the characteristic itself if it isn't a sibling.
            public Characteristic AnchorOf(Characteristic characteristic)
            {
                string anchorId;
                int index;

                if (!TrySplitSiblingRefId(characteristic.PersistentRefId, out anchorId, out index))
                    return characteristic;

                Characteristic anchor;
                return _byRefId.TryGetValue(anchorId, out anchor) ? anchor : null;
            }

            // A main row's sibling values in order ("6.1", "6.2", ...);
            // empty for anything that isn't a multi-value main row.
            public IReadOnlyList<Characteristic> SiblingsOf(Characteristic anchor)
            {
                if (anchor == null || string.IsNullOrEmpty(anchor.PersistentRefId) || IsSibling(anchor))
                    return None;

                List<Characteristic> siblings;
                return _siblingsByAnchorId.TryGetValue(anchor.PersistentRefId, out siblings) ? siblings : None;
            }

            public bool IsMultiValue(Characteristic characteristic)
            {
                return IsSibling(characteristic) || SiblingsOf(characteristic).Count > 0;
            }

            // True if some characteristic from a DIFFERENT balloon shares
            // this one's Number - i.e. it's part of a user-made group,
            // whether as the group's bare anchor ("6", SubNumber null) or
            // one of its decimal members ("6.1"). A multi-value balloon's
            // own values sharing its number don't count.
            public bool IsUserGroupMember(Characteristic characteristic)
            {
                if (characteristic.IsUnnumbered || characteristic.Number <= 0)
                    return false;

                object unit = UnitKey(characteristic);

                return _projectData.Characteristics.Any(other =>
                    other != characteristic &&
                    !other.IsUnnumbered &&
                    other.Number == characteristic.Number &&
                    !UnitKey(other).Equals(unit));
            }

            // A sibling's fixed decimal place on its main row's balloon:
            // "#2" is .1, "#3" is .2, and so on.
            public static int? SiblingSubNumber(Characteristic sibling)
            {
                string anchorId;
                int index;

                return TrySplitSiblingRefId(sibling.PersistentRefId, out anchorId, out index)
                    ? index - 1
                    : (int?)null;
            }

            private object UnitKey(Characteristic characteristic)
            {
                string anchorId;
                int index;

                if (TrySplitSiblingRefId(characteristic.PersistentRefId, out anchorId, out index))
                    return anchorId;

                return string.IsNullOrEmpty(characteristic.PersistentRefId)
                    ? (object)characteristic
                    : characteristic.PersistentRefId;
            }

            // Real persistent ids are base64 (no '#'), so a trailing
            // "#<n>" with n >= 2 only ever marks a synthetic sibling id.
            private static bool TrySplitSiblingRefId(string refId, out string anchorId, out int index)
            {
                anchorId = null;
                index = 0;

                if (string.IsNullOrEmpty(refId))
                    return false;

                int hash = refId.LastIndexOf('#');

                if (hash <= 0 || hash == refId.Length - 1)
                    return false;

                if (!int.TryParse(
                        refId.Substring(hash + 1),
                        System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out index) ||
                    index < 2)
                    return false;

                anchorId = refId.Substring(0, hash);
                return true;
            }
        }

        // One action's shared view of the drawing's balloon notes: the
        // sheet/view walk and the display-number -> Note index are each
        // built at most once, on first use, instead of every lookup
        // re-walking every sheet (see BalloonManager.CreateBalloon's
        // remarks), and kept in sync as notes are created, renamed and
        // deleted. Also logs renames so an all-or-nothing action can undo
        // them.
        private sealed class BalloonBatch
        {
            private readonly ModelDoc2 _model;
            private readonly List<RenameRecord> _renames = new List<RenameRecord>();
            private List<DrawingSheetHelper.ViewOnSheet> _viewsBySheet;
            private Dictionary<string, Note> _index;

            public BalloonBatch(ModelDoc2 model)
            {
                _model = model;
            }

            public BalloonManager Manager { get; } = new BalloonManager();

            public bool AnyDrawingChange { get; set; }

            public int RenameCount => _renames.Count;

            private List<DrawingSheetHelper.ViewOnSheet> ViewsBySheet =>
                _viewsBySheet ?? (_viewsBySheet = DrawingSheetHelper.GetAllViewsBySheet(_model as DrawingDoc));

            private Dictionary<string, Note> Index =>
                _index ?? (_index = Manager.BuildExistingBalloonIndex(_model as DrawingDoc, ViewsBySheet));

            public Note FindBalloon(string displayNumber)
            {
                Note note;
                return Index.TryGetValue(displayNumber, out note) ? note : null;
            }

            // Places a balloon for row's live dimension; a failure is
            // recorded against the row (with BalloonManager's reason) and
            // returns null.
            public Note CreateBalloon(BalloonGridRow row, string displayNumber, GridOperationResult result)
            {
                return CreateBalloon(row.AnnotationSource, row.SheetName, displayNumber, Describe(row), result);
            }

            // Same, for any source CreateBalloon accepts (a dimension, GD&T
            // frame, note or surface finish); label names it in an error.
            public Note CreateBalloon(
                object source, string sheetName, string displayNumber, string label, GridOperationResult result)
            {
                Note note = Manager.CreateBalloon(
                    _model, source, displayNumber, sheetName,
                    viewsBySheet: ViewsBySheet, existingBalloonIndex: Index);

                if (note == null)
                {
                    result.Errors.Add(
                        label + ": couldn't place balloon " + displayNumber +
                        (Manager.LastFailureReason != null ? " (" + Manager.LastFailureReason + ")" : "") + ".");
                    return null;
                }

                AnyDrawingChange = true;
                return note;
            }

            public void Rename(Note note, string oldText, string newText)
            {
                note.SetText(newText);
                AnyDrawingChange = true;

                _renames.Add(new RenameRecord { Note = note, OldText = oldText, NewText = newText });
                Reindex(note, oldText, newText);
            }

            // Renames back, newest first, everything renamed since mark.
            // False if any note refused to rename back.
            public bool UndoRenamesSince(int mark)
            {
                bool allRestored = true;

                for (int i = _renames.Count - 1; i >= mark; i--)
                {
                    RenameRecord rename = _renames[i];

                    try
                    {
                        rename.Note.SetText(rename.OldText);
                        Reindex(rename.Note, rename.NewText, rename.OldText);
                    }
                    catch (Exception ex)
                    {
                        BinspectionLog.Error("BalloonGridService undo rename", ex);
                        allRestored = false;
                    }
                }

                _renames.RemoveRange(mark, _renames.Count - mark);

                return allRestored;
            }

            public bool DeleteBalloon(Note note, string displayNumber)
            {
                if (!Manager.DeleteBalloon(_model, note))
                    return false;

                AnyDrawingChange = true;

                Note indexed;

                if (_index != null && _index.TryGetValue(displayNumber, out indexed) && ReferenceEquals(indexed, note))
                    _index.Remove(displayNumber);

                return true;
            }

            // A move replaces the note (and may change sheets), so the
            // cached walk and index are dropped rather than patched.
            public Note MoveBalloon(string displayNumber, string targetSheetName)
            {
                Note moved = Manager.MoveBalloon(_model, displayNumber, targetSheetName);

                if (moved != null)
                {
                    AnyDrawingChange = true;
                    _index = null;
                    _viewsBySheet = null;
                }

                return moved;
            }

            public string GetPersistId(Note note)
            {
                return Manager.GetBalloonPersistId(_model, note);
            }

            // Only drop oldText's entry if it still points at this note -
            // mid-swap, another note may already have been renamed onto it.
            private void Reindex(Note note, string oldText, string newText)
            {
                if (_index == null)
                    return;

                Note indexed;

                if (_index.TryGetValue(oldText, out indexed) && ReferenceEquals(indexed, note))
                    _index.Remove(oldText);

                _index[newText] = note;
            }

            private sealed class RenameRecord
            {
                public Note Note;
                public string OldText;
                public string NewText;
            }
        }

        // Numbering fields of one characteristic as they were before an
        // all-or-nothing action started, for RollBack.
        private sealed class NumberSnapshot
        {
            private readonly Characteristic _characteristic;
            private readonly int _number;
            private readonly int? _subNumber;
            private readonly int? _preGroupNumber;
            private readonly bool _isUnnumbered;
            private readonly string _balloonPersistId;
            private readonly bool _sharesGroupBalloon;

            public NumberSnapshot(Characteristic characteristic)
            {
                _characteristic = characteristic;
                _number = characteristic.Number;
                _subNumber = characteristic.SubNumber;
                _preGroupNumber = characteristic.PreGroupNumber;
                _isUnnumbered = characteristic.IsUnnumbered;
                _balloonPersistId = characteristic.BalloonPersistId;
                _sharesGroupBalloon = characteristic.SharesGroupBalloon;
            }

            public void Restore()
            {
                _characteristic.Number = _number;
                _characteristic.SubNumber = _subNumber;
                _characteristic.PreGroupNumber = _preGroupNumber;
                _characteristic.IsUnnumbered = _isUnnumbered;
                _characteristic.BalloonPersistId = _balloonPersistId;
                _characteristic.SharesGroupBalloon = _sharesGroupBalloon;
            }
        }
    }

    // What one BalloonGridService action did, in one shape for every
    // action: whether it changed (and saved) anything, which rows failed
    // or were refused (Errors), what succeeded with a caveat the user
    // should know about (Warnings), and whether the save itself failed.
    // Message is the single user-facing summary, or null when there's
    // nothing to say.
    public class GridOperationResult
    {
        public bool Changed { get; internal set; }

        public bool SaveFailed { get; internal set; }

        public List<string> Errors { get; } = new List<string>();

        public List<string> Warnings { get; } = new List<string>();

        public bool HasErrors => SaveFailed || Errors.Count > 0;

        public string SaveFailedMessage =>
            "The changes couldn't be saved to the BINSPECTION data file (check that it isn't read-only or " +
            "open in another program). Until it saves, the drawing and the saved balloon data disagree - " +
            "repeat the action once the file is writable.";

        public string Message
        {
            get
            {
                // A plain refusal ("Check two or more balloons...") reads
                // best on its own, without a summary header.
                if (!Changed && !SaveFailed && Errors.Count == 1 && Warnings.Count == 0)
                    return Errors[0];

                List<string> parts = new List<string>();

                if (Errors.Count > 0)
                {
                    parts.Add(
                        (Changed ? "Some rows couldn't be updated:" : "Nothing was changed:") +
                        System.Environment.NewLine + Bullets(Errors));
                }

                if (Warnings.Count > 0)
                    parts.Add(Bullets(Warnings));

                if (SaveFailed)
                    parts.Add(SaveFailedMessage);

                return parts.Count == 0
                    ? null
                    : string.Join(System.Environment.NewLine + System.Environment.NewLine, parts);
            }
        }

        private static string Bullets(IEnumerable<string> lines)
        {
            return string.Join(System.Environment.NewLine, lines.Select(l => "• " + l));
        }
    }
}
