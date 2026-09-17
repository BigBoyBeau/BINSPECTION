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

        public string LegacyNumber { get; set; }

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

            List<DimensionHit> hits = scanner.GetAllDimensions(drawing, viewsBySheet: viewsBySheet);

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
                    LegacyNumber = characteristic?.LegacyNumber,
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
                    LegacyNumber = characteristic.LegacyNumber,
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
        public static void AddBalloons(
            ModelDoc2 model,
            string dataFilePath,
            List<BalloonGridRow> checkedRows,
            ProjectData projectData)
        {
            CharacteristicManager.Clear();
            CharacteristicManager.LoadFrom(projectData.Characteristics);

            BalloonManager balloonManager = new BalloonManager();
            bool anyCreated = false;

            foreach (BalloonGridRow row in checkedRows)
            {
                if (row.HasBalloon || row.AnnotationSource == null)
                    continue;

                int nextNumber = CharacteristicManager.GetNextNumber();

                Note note = balloonManager.CreateBalloon(model, row.AnnotationSource, nextNumber, row.SheetName);

                if (note == null)
                    continue;

                Characteristic characteristic = row.Characteristic;

                if (characteristic == null)
                {
                    characteristic = new Characteristic
                    {
                        PersistentRefId = row.PersistentRefId,
                        DimensionName = row.DimensionDisplay,
                        SheetName = row.SheetName,
                        IsBasic = row.IsBasic,
                    };

                    projectData.Characteristics.Add(characteristic);
                    row.Characteristic = characteristic;
                }

                characteristic.Number = nextNumber;
                characteristic.IsUnnumbered = false;
                characteristic.SubNumber = null;
                characteristic.PreGroupNumber = null;
                characteristic.BalloonPersistId = balloonManager.GetBalloonPersistId(model, note);

                CharacteristicManager.AddCharacteristic(characteristic);

                anyCreated = true;
            }

            if (anyCreated)
                PersistenceManager.SaveProject(dataFilePath, projectData);
        }

        // Joins two or more checked, currently-ungrouped balloons into one
        // group under a single fresh number, same as Group Balloon did.
        // Returns an error message to show the user, or null on success.
        public static string GroupBalloons(
            ModelDoc2 model,
            string dataFilePath,
            List<BalloonGridRow> checkedRows,
            ProjectData projectData)
        {
            List<Characteristic> members = checkedRows
                .Where(r => r.HasBalloon && r.Characteristic != null)
                .Select(r => r.Characteristic)
                .ToList();

            if (members.Count < 2)
                return "Check two or more ungrouped balloons to join, then try again.";

            if (members.Any(c => IsGroupMember(c, projectData)))
                return "One or more checked balloons is already part of a group - ungroup it first.";

            BalloonManager balloonManager = new BalloonManager();

            // Resolve every source Note BEFORE changing any of them, same
            // as LegacyRenumberService.Apply - two balloons briefly sharing
            // the same displayed text mid-batch could otherwise make a
            // later lookup in this same call ambiguous.
            List<Note> notes = members
                .Select(c => balloonManager.FindExistingBalloon(model, c.DisplayNumber))
                .ToList();

            CharacteristicManager.Clear();
            CharacteristicManager.LoadFrom(projectData.Characteristics);

            int groupNumber = CharacteristicManager.GetNextNumber();
            bool anyNoteChanged = false;

            for (int i = 0; i < members.Count; i++)
            {
                Characteristic characteristic = members[i];

                characteristic.PreGroupNumber = characteristic.Number;
                characteristic.Number = groupNumber;

                // The group's first member is the bare whole number (e.g.
                // "6"), never "6.1" - only members after it get a decimal
                // sub-number, starting at .1.
                characteristic.SubNumber = i == 0 ? (int?)null : i;

                if (notes[i] != null)
                {
                    notes[i].SetText(characteristic.DisplayNumber);
                    anyNoteChanged = true;
                }
            }

            if (anyNoteChanged)
                model.GraphicsRedraw2();

            PersistenceManager.SaveProject(dataFilePath, projectData);

            return null;
        }

        // Reverses grouping for each checked, currently-grouped balloon:
        // each one gets its pre-group number back, same as Ungroup Balloon
        // did. Returns an error message to show the user, or null on
        // success.
        public static string UngroupBalloons(
            ModelDoc2 model,
            string dataFilePath,
            List<BalloonGridRow> checkedRows,
            ProjectData projectData)
        {
            List<Characteristic> members = checkedRows
                .Where(r => r.HasBalloon && r.Characteristic != null && IsGroupMember(r.Characteristic, projectData))
                .Select(r => r.Characteristic)
                .ToList();

            if (members.Count == 0)
                return "Check one or more grouped balloons (e.g. 12 or 12.2) to ungroup, then try again.";

            CharacteristicManager.Clear();
            CharacteristicManager.LoadFrom(projectData.Characteristics);

            BalloonManager balloonManager = new BalloonManager();

            List<Note> notes = members
                .Select(c => balloonManager.FindExistingBalloon(model, c.DisplayNumber))
                .ToList();

            bool anyNoteChanged = false;

            for (int i = 0; i < members.Count; i++)
            {
                Characteristic characteristic = members[i];

                int restoredNumber = characteristic.PreGroupNumber ?? CharacteristicManager.GetNextNumber();

                characteristic.Number = restoredNumber;
                characteristic.SubNumber = null;
                characteristic.PreGroupNumber = null;

                if (notes[i] != null)
                {
                    notes[i].SetText(characteristic.DisplayNumber);
                    anyNoteChanged = true;
                }
            }

            if (anyNoteChanged)
                model.GraphicsRedraw2();

            PersistenceManager.SaveProject(dataFilePath, projectData);

            return null;
        }

        // Marks each checked, not-yet-unnumbered row as excluded from
        // inspection, same as Un-Number did - a checked balloon gets its
        // note deleted (number freed for reuse), a checked bare dimension
        // gets a reserved Characteristic created for it. Reversed by
        // ReNumberBalloons.
        public static void MarkUnnumbered(
            ModelDoc2 model,
            string dataFilePath,
            List<BalloonGridRow> checkedRows,
            ProjectData projectData)
        {
            BalloonManager balloonManager = new BalloonManager();
            bool anyChanged = false;

            foreach (BalloonGridRow row in checkedRows)
            {
                Characteristic characteristic = row.Characteristic;

                if (characteristic != null && characteristic.IsUnnumbered)
                    continue;

                if (characteristic == null)
                {
                    if (row.AnnotationSource == null)
                        continue;

                    characteristic = new Characteristic
                    {
                        PersistentRefId = row.PersistentRefId,
                        DimensionName = row.DimensionDisplay,
                        SheetName = row.SheetName,
                        IsBasic = row.IsBasic,
                    };

                    projectData.Characteristics.Add(characteristic);
                    row.Characteristic = characteristic;
                }

                if (characteristic.Number > 0)
                    balloonManager.DeleteBalloon(model, characteristic.DisplayNumber);

                characteristic.IsUnnumbered = true;
                characteristic.SubNumber = null;
                characteristic.PreGroupNumber = null;
                characteristic.Number = 0;

                anyChanged = true;
            }

            if (anyChanged)
                PersistenceManager.SaveProject(dataFilePath, projectData);
        }

        // Reverses MarkUnnumbered for each checked, currently-unnumbered
        // row: assigns it the next available balloon number and creates a
        // balloon note for it (same as Add Balloon), clearing the
        // unnumbered flag.
        public static void ReNumberBalloons(
            ModelDoc2 model,
            string dataFilePath,
            List<BalloonGridRow> checkedRows,
            ProjectData projectData)
        {
            CharacteristicManager.Clear();
            CharacteristicManager.LoadFrom(projectData.Characteristics);

            BalloonManager balloonManager = new BalloonManager();
            bool anyChanged = false;

            foreach (BalloonGridRow row in checkedRows)
            {
                Characteristic characteristic = row.Characteristic;

                if (characteristic == null || !characteristic.IsUnnumbered)
                    continue;

                int nextNumber = CharacteristicManager.GetNextNumber();

                characteristic.IsUnnumbered = false;
                characteristic.Number = nextNumber;
                characteristic.SubNumber = null;
                characteristic.PreGroupNumber = null;

                if (row.AnnotationSource != null)
                {
                    Note note = balloonManager.CreateBalloon(model, row.AnnotationSource, nextNumber, row.SheetName);

                    if (note != null)
                        characteristic.BalloonPersistId = balloonManager.GetBalloonPersistId(model, note);
                }

                CharacteristicManager.AddCharacteristic(characteristic);

                anyChanged = true;
            }

            if (anyChanged)
                PersistenceManager.SaveProject(dataFilePath, projectData);
        }

        // Manually assigns/moves one row to an explicitly-typed balloon
        // number (e.g. "5" or "5.2"), including a grouped one - the direct
        // way to build or edit a group without going through Group
        // Balloon's "checked rows get consecutive sub-numbers" flow.
        // Intentionally simpler than LegacyRenumberService's bystander-
        // shifting plan builder: a collision with another EXISTING balloon
        // is offered to the user as a straight two-way swap via
        // confirmSwap, rather than silently displacing a third balloon out
        // of the way. Returns true on success (including a no-op); false
        // (with "error" set) if the input was invalid or the user declined
        // a swap.
        public static bool TryManualRenumber(
            ModelDoc2 model,
            string dataFilePath,
            BalloonGridRow row,
            string newNumberText,
            ProjectData projectData,
            Func<string, bool> confirmSwap,
            out string error)
        {
            error = null;

            int number;
            int? subNumber;
            string displayNumber;

            if (!LegacyRenumberService.TryParseDisplayNumber(
                newNumberText, out number, out subNumber, out displayNumber))
            {
                error = "\"" + newNumberText + "\" isn't a valid balloon number - use \"5\" or \"5.2\".";
                return false;
            }

            Characteristic characteristic = row.Characteristic;
            bool isNewCharacteristic = characteristic == null;

            if (isNewCharacteristic)
            {
                if (row.AnnotationSource == null)
                {
                    error = "This row has no dimension to balloon.";
                    return false;
                }

                characteristic = new Characteristic
                {
                    PersistentRefId = row.PersistentRefId,
                    DimensionName = row.DimensionDisplay,
                    SheetName = row.SheetName,
                    IsBasic = row.IsBasic,
                };
            }

            bool hadBalloon = !isNewCharacteristic && !characteristic.IsUnnumbered && characteristic.Number > 0;
            string oldDisplayNumber = characteristic.DisplayNumber;

            if (hadBalloon && displayNumber == oldDisplayNumber)
                return true;

            Characteristic collidingWith = projectData.Characteristics
                .FirstOrDefault(c => c != characteristic && !c.IsUnnumbered && c.DisplayNumber == displayNumber);

            if (collidingWith != null)
            {
                if (!hadBalloon)
                {
                    error = "Balloon " + displayNumber + " is already assigned to " + collidingWith.DimensionName +
                        " - delete it or pick a different number first.";
                    return false;
                }

                if (confirmSwap == null || !confirmSwap(displayNumber))
                    return false;
            }

            BalloonManager balloonManager = new BalloonManager();

            Note ownNote =
                hadBalloon ? balloonManager.FindExistingBalloon(model, oldDisplayNumber) : null;

            Note collidingNote =
                collidingWith != null ? balloonManager.FindExistingBalloon(model, collidingWith.DisplayNumber) : null;

            bool anyNoteChanged = false;

            if (collidingWith != null)
            {
                // Swap: the balloon currently sitting on the target number
                // takes this row's OLD number instead of being displaced to
                // some arbitrary free one.
                collidingWith.Number = characteristic.Number;
                collidingWith.SubNumber = characteristic.SubNumber;

                if (collidingNote != null)
                {
                    collidingNote.SetText(collidingWith.DisplayNumber);
                    anyNoteChanged = true;
                }
            }

            characteristic.Number = number;
            characteristic.SubNumber = subNumber;
            characteristic.PreGroupNumber = null;
            characteristic.IsUnnumbered = false;

            if (isNewCharacteristic)
                projectData.Characteristics.Add(characteristic);

            row.Characteristic = characteristic;

            if (hadBalloon)
            {
                if (ownNote != null)
                {
                    ownNote.SetText(characteristic.DisplayNumber);
                    anyNoteChanged = true;
                }
            }
            else if (row.AnnotationSource != null)
            {
                Note created = balloonManager.CreateBalloon(model, row.AnnotationSource, characteristic.DisplayNumber, row.SheetName);

                if (created != null)
                    characteristic.BalloonPersistId = balloonManager.GetBalloonPersistId(model, created);

                anyNoteChanged |= created != null;
            }

            if (anyNoteChanged)
                model.GraphicsRedraw2();

            PersistenceManager.SaveProject(dataFilePath, projectData);

            return true;
        }

        // Saves Inspection Method/Classification for one row, same fields
        // the retired single-balloon and bulk attribute editors used to
        // write - only meaningful for a row that already has a
        // Characteristic (HasCharacteristic), since there's nowhere to
        // persist attributes for a bare unballooned dimension yet.
        // Newly-typed Method/Class values are added to the project's
        // option lists for next time, same as those retired editors did.
        public static void UpdateAttributes(
            string dataFilePath,
            BalloonGridRow row,
            string method,
            string classification,
            ProjectData projectData)
        {
            if (row.Characteristic == null)
                return;

            row.Characteristic.Method = method;
            row.Characteristic.Class = classification;

            row.Method = method;
            row.Class = classification;

            AddIfNew(projectData.MethodOptions, method);
            AddIfNew(projectData.ClassOptions, classification);

            PersistenceManager.SaveProject(dataFilePath, projectData);
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
        public static void UpdateAttributesForRows(
            string dataFilePath,
            IEnumerable<BalloonGridRow> rows,
            string method,
            string classification,
            ProjectData projectData)
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

            PersistenceManager.SaveProject(dataFilePath, projectData);
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
        // row with no live AnnotationSource. Returns false (with error set)
        // only if this row has neither a live dimension nor an existing
        // Characteristic to create one against.
        public static bool SetBasic(
            ModelDoc2 model,
            string dataFilePath,
            BalloonGridRow row,
            bool isBasic,
            ProjectData projectData,
            out string error)
        {
            error = null;

            Characteristic characteristic = row.Characteristic;

            if (characteristic == null)
            {
                if (row.AnnotationSource == null)
                {
                    error = "This row has no dimension to mark Basic.";
                    return false;
                }

                characteristic = new Characteristic
                {
                    PersistentRefId = row.PersistentRefId,
                    DimensionName = row.DimensionDisplay,
                    SheetName = row.SheetName,
                };

                projectData.Characteristics.Add(characteristic);
                row.Characteristic = characteristic;
            }

            if (row.AnnotationSource != null)
            {
                Dimension swDim = row.AnnotationSource.GetDimension2(0);
                IDimensionTolerance tolerance = swDim?.Tolerance;

                if (tolerance != null)
                {
                    if (isBasic)
                    {
                        if (tolerance.Type != (int)swTolType_e.swTolBASIC)
                            characteristic.PreBasicToleranceType = tolerance.Type;

                        tolerance.Type = (int)swTolType_e.swTolBASIC;
                    }
                    else
                    {
                        tolerance.Type = characteristic.PreBasicToleranceType ?? (int)swTolType_e.swTolNONE;
                        characteristic.PreBasicToleranceType = null;
                    }
                }

                characteristic.DimensionName =
                    GetDimensionDisplay(model, row.AnnotationSource, swDim, isBasic);

                string boxSheetName = row.SheetName ?? characteristic.SheetName;

                if (isBasic)
                {
                    if (string.IsNullOrEmpty(characteristic.BasicBoxSketchName))
                    {
                        characteristic.BasicBoxSketchName = DimensionBoxSketchService.AddBox(
                            model, model as DrawingDoc, row.AnnotationSource, boxSheetName);
                    }
                }
                else if (!string.IsNullOrEmpty(characteristic.BasicBoxSketchName))
                {
                    DimensionBoxSketchService.RemoveBox(
                        model, model as DrawingDoc, characteristic.BasicBoxSketchName, boxSheetName);
                    characteristic.BasicBoxSketchName = null;
                }

                model.GraphicsRedraw2();
            }

            characteristic.IsBasic = isBasic;
            row.IsBasic = isBasic;

            PersistenceManager.SaveProject(dataFilePath, projectData);

            return true;
        }

        // Moves a checked/selected balloon (and every sibling data-only
        // characteristic that shares its one physical balloon note - see
        // Characteristic.BalloonPersistId's remarks on multi-value/
        // composite GD&T groups) onto a different sheet, for when Create
        // Balloons (or the bug it used to have - see
        // [[binspection_balloon_wrong_sheet_placement_bug]]) got the sheet
        // wrong. A no-op (returns true, nothing saved) if the row is
        // already on targetSheetName. Returns false (with error set) if the
        // row has no live balloon to move, or the move itself fails (e.g.
        // targetSheetName can't be activated) - see
        // BalloonManager.MoveBalloon.
        public static bool MoveBalloonToSheet(
            ModelDoc2 model,
            string dataFilePath,
            BalloonGridRow row,
            string targetSheetName,
            ProjectData projectData,
            out string error)
        {
            error = null;

            Characteristic characteristic = row.Characteristic;

            if (characteristic == null || characteristic.IsUnnumbered || characteristic.Number <= 0)
            {
                error = "This row has no balloon on the drawing to move.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(targetSheetName))
            {
                error = "Pick a target sheet first.";
                return false;
            }

            if (string.Equals(characteristic.SheetName, targetSheetName, StringComparison.OrdinalIgnoreCase))
                return true;

            BalloonManager balloonManager = new BalloonManager();

            Note movedNote =
                balloonManager.MoveBalloon(model, characteristic.DisplayNumber, targetSheetName);

            if (movedNote == null)
            {
                error = "Could not move balloon " + characteristic.DisplayNumber + " to sheet \"" +
                    targetSheetName + "\"" +
                    (balloonManager.LastFailureReason != null ? " (" + balloonManager.LastFailureReason + ")" : "") +
                    ".";
                return false;
            }

            string newBalloonPersistId = balloonManager.GetBalloonPersistId(model, movedNote);

            // Every sibling that shares this one physical balloon (a
            // multi-value hole-callout/chamfer or composite GD&T group)
            // needs the same updated SheetName/BalloonPersistId, or the
            // report's per-row "Sheet" column would disagree with itself
            // for members of the same group.
            string oldBalloonPersistId = characteristic.BalloonPersistId;

            List<Characteristic> group = string.IsNullOrEmpty(oldBalloonPersistId)
                ? new List<Characteristic> { characteristic }
                : projectData.Characteristics
                    .Where(c => c.BalloonPersistId == oldBalloonPersistId)
                    .ToList();

            foreach (Characteristic member in group)
            {
                member.SheetName = targetSheetName;
                member.BalloonPersistId = newBalloonPersistId;
            }

            row.SheetName = targetSheetName;

            model.GraphicsRedraw2();

            PersistenceManager.SaveProject(dataFilePath, projectData);

            return true;
        }

        // True if some OTHER tracked characteristic shares this one's
        // Number - i.e. it's part of a group, whether it's the group's
        // bare anchor ("6", SubNumber null) or one of its decimal members
        // ("6.1", SubNumber set). Needed because a group's first member no
        // longer carries a SubNumber of its own (see GroupBalloons), so
        // SubNumber.HasValue alone can no longer tell "grouped" from
        // "standalone".
        private static bool IsGroupMember(Characteristic characteristic, ProjectData projectData)
        {
            return projectData.Characteristics.Any(other =>
                other != characteristic &&
                !other.IsUnnumbered &&
                other.Number > 0 &&
                other.Number == characteristic.Number);
        }

        private static void AddIfNew(List<string> options, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            string trimmed = value.Trim();

            if (!options.Any(o => string.Equals(o, trimmed, StringComparison.OrdinalIgnoreCase)))
                options.Add(trimmed);
        }
    }
}
