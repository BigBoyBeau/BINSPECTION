using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using BINSPECTION.Core;
using BINSPECTION.Models;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace BINSPECTION.CommandManager
{
    public class CommandManagerHandler

    {

        private readonly ISldWorks _swApp;
        private readonly int _addinId;

        private ICommandManager _cmdMgr;

        // Balloon Manager runs modeless (Show, not ShowDialog) so the user
        // can select dimensions/balloons in the graphics area while it's
        // open, the way SW Inspection's panel works. Tracked here so a
        // second invocation of the command just brings the existing window
        // forward instead of opening a duplicate.
        private BINSPECTION.UI.BalloonManagerWindow _balloonManagerWindow;

        public CommandManagerHandler(
            ISldWorks swApp,
            int addinId)
        {
            _swApp = swApp;
            _addinId = addinId;
        }

        public void CreateCommandManager()
        {
            try
            {
                _cmdMgr = _swApp.GetCommandManager(_addinId);

                try
                {
                    _cmdMgr.RemoveCommandGroup(1);
                }
                catch
                {
                    // Ignore if group doesn't exist
                }

                int errors = 0;

                CommandGroup cmdGroup =
                    _cmdMgr.CreateCommandGroup2(
                        1,
                        "BINSPECTION",
                        "Inspection Tools",
                        "",
                        -1,
                        true,
                        ref errors);

                // Letter-abbreviation icons generated at runtime (see
                // CommandIconGenerator) rather than shipped artwork - each
                // AddCommandItem2 call below passes an ImageListIndex that
                // must line up with CommandIconGenerator.CommandIconNames'
                // order.
                cmdGroup.IconList = CommandIconGenerator.BuildIconList();
                cmdGroup.MainIconList = CommandIconGenerator.BuildMainIconList();

                cmdGroup.AddCommandItem2(
                    "Create Balloons",
                    -1,
                    "Create inspection balloons",
                    "Create",
                    0,
                    nameof(OnCreateBalloons),
                    "",
                    0,
                    (int)swCommandItemType_e.swMenuItem |
                    (int)swCommandItemType_e.swToolbarItem);

                cmdGroup.AddCommandItem2(
                    "Generate Report",
                    -1,
                    "Generate inspection report",
                    "Report",
                    1,
                    nameof(OnGenerateReport),
                    "",
                    1,
                    (int)swCommandItemType_e.swMenuItem |
                    (int)swCommandItemType_e.swToolbarItem);

                // Restores balloons from the saved data file, WITHOUT
                // assigning any new numbers or ballooning brand-new
                // dimensions - that's what "Create Balloons" is for. Use
                // this to bring back a balloon that got deleted or lost
                // (e.g. after the drawing metadata moved something), and
                // optionally clean up balloons that don't match anything
                // saved (like duplicates from a numbering bug).
                cmdGroup.AddCommandItem2(
                    "Restore Balloons",
                    -1,
                    "Restore balloons from saved data",
                    "Restore",
                    2,
                    nameof(OnRestoreBalloons),
                    "",
                    2,
                    (int)swCommandItemType_e.swMenuItem |
                    (int)swCommandItemType_e.swToolbarItem);

                // Single home for reassigning balloon numbers AND editing
                // attributes: Group/Ungroup/Add/Un-Number/Re-Number, manual
                // number entry, Method/Classification editing, and Legacy
                // Conversion, all live in one WPF window driven by
                // checkboxes/cells in a grid rather than a SolidWorks
                // graphics-area selection. Replaces the former Group
                // Balloon/Ungroup Balloon/Toggle Unnumbered/Add Balloon/
                // Delete Balloon/Match Legacy Numbers/Edit Attributes/Edit
                // All Attributes commands - see OnOpenBalloonManager and
                // UI/BalloonManagerWindow.
                cmdGroup.AddCommandItem2(
                    "Balloon Manager",
                    -1,
                    "Reassign, group, add, delete, and edit balloon numbers and attributes",
                    "Balloon Manager",
                    3,
                    nameof(OnOpenBalloonManager),
                    "",
                    3,
                    (int)swCommandItemType_e.swMenuItem |
                    (int)swCommandItemType_e.swToolbarItem);

                // Sets up (or edits) what tolerance rules apply to each
                // sheet - see OnSheetToleranceSelection. Which sheets
                // actually get populated with balloons is chosen in the
                // Create Balloons picker instead, every time it runs.
                // Deliberately separate from Create Balloons so it can be
                // run any time (initial setup or a later edit), not just
                // once at project start.
                cmdGroup.AddCommandItem2(
                    "Sheet Tolerance Selection",
                    -1,
                    "Set up sheet tolerance groups",
                    "Sheet Tolerances",
                    4,
                    nameof(OnSheetToleranceSelection),
                    "",
                    4,
                    (int)swCommandItemType_e.swMenuItem |
                    (int)swCommandItemType_e.swToolbarItem);

                // Debugging tool: wipes every balloon on the sheet AND all
                // saved balloon data, so numbering can start clean from 1.
                // See OnDeleteAllBalloons.
                cmdGroup.AddCommandItem2(
                    "Delete All Balloons",
                    -1,
                    "Delete every balloon and all saved balloon data (debugging)",
                    "Delete All Balloons",
                    5,
                    nameof(OnDeleteAllBalloons),
                    "",
                    5,
                    (int)swCommandItemType_e.swMenuItem |
                    (int)swCommandItemType_e.swToolbarItem);

                // Removes every balloon's visual note from the sheet only -
                // the saved data file is untouched, so a later Restore
                // Balloons/Create Balloons run puts them all back with the
                // same numbers. See OnRemoveBalloons - deliberately a
                // separate command from Delete All Balloons, which also
                // wipes the saved data.
                cmdGroup.AddCommandItem2(
                    "Remove Balloons",
                    -1,
                    "Remove every balloon's visual from the sheet (saved balloon data is kept)",
                    "Remove Balloons",
                    6,
                    nameof(OnRemoveBalloons),
                    "",
                    6,
                    (int)swCommandItemType_e.swMenuItem |
                    (int)swCommandItemType_e.swToolbarItem);

                // Convenience command that runs Remove Balloons followed
                // immediately by Restore Balloons, with no message boxes in
                // between - use this to force every balloon to redraw
                // against current geometry (position, leader, etc.) without
                // clicking Remove then Restore separately. Saved balloon
                // data is untouched and numbers are unchanged, same
                // guarantees as Restore Balloons. See OnRefreshBalloons.
                cmdGroup.AddCommandItem2(
                    "Refresh Balloons",
                    -1,
                    "Remove and restore every balloon on the sheet in one step",
                    "Refresh Balloons",
                    7,
                    nameof(OnRefreshBalloons),
                    "",
                    7,
                    (int)swCommandItemType_e.swMenuItem |
                    (int)swCommandItemType_e.swToolbarItem);

                // Snapshots every balloon's current sheet-space position
                // into the saved data file (keyed by BalloonPersistId), so
                // it can be put back later with Restore Position - e.g.
                // before dragging balloons around to fit a print layout.
                // See OnSavePosition.
                cmdGroup.AddCommandItem2(
                    "Save Position",
                    -1,
                    "Save every balloon's current position",
                    "Save Position",
                    8,
                    nameof(OnSavePosition),
                    "",
                    8,
                    (int)swCommandItemType_e.swMenuItem |
                    (int)swCommandItemType_e.swToolbarItem);

                // Puts every balloon back at the position last captured by
                // Save Position. See OnRestorePosition.
                cmdGroup.AddCommandItem2(
                    "Restore Position",
                    -1,
                    "Restore every balloon to its last saved position",
                    "Restore Position",
                    9,
                    nameof(OnRestorePosition),
                    "",
                    9,
                    (int)swCommandItemType_e.swMenuItem |
                    (int)swCommandItemType_e.swToolbarItem);

                cmdGroup.HasToolbar = true;
                cmdGroup.HasMenu = true;

                cmdGroup.Activate();

                int cmd1 = cmdGroup.get_CommandID(0);
                int cmd2 = cmdGroup.get_CommandID(1);
                int cmd3 = cmdGroup.get_CommandID(2);
                int cmd4 = cmdGroup.get_CommandID(3);
                int cmd5 = cmdGroup.get_CommandID(4);
                int cmd6 = cmdGroup.get_CommandID(5);
                int cmd7 = cmdGroup.get_CommandID(6);
                int cmd8 = cmdGroup.get_CommandID(7);
                int cmd9 = cmdGroup.get_CommandID(8);
                int cmd10 = cmdGroup.get_CommandID(9);

                // Previously showed an OK popup ("BINSPECTION Commands
                // Loaded: ...") every time the command group registered -
                // same reasoning as the "BINSPECTION Loaded" popup removed
                // from BInspectionAddIn.ConnectToSW: this is routine startup
                // confirmation, not something that needs the user to click
                // through it. The command IDs are still written to the
                // debug output so a developer can confirm registration
                // succeeded without SolidWorks interrupting normal use.
                System.Diagnostics.Debug.WriteLine(
                    $"BINSPECTION Commands Loaded: {cmd1}, {cmd2}, {cmd3}, {cmd4}, {cmd5}, {cmd6}, {cmd7}, {cmd8}, {cmd9}, {cmd10}");
            }
            catch (Exception ex)
            {
                _swApp.SendMsgToUser2(
                    ex.ToString(),
                    (int)swMessageBoxIcon_e.swMbStop,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
        }

        public void RemoveCommandManager()
        {
            try
            {
                if (_cmdMgr != null)
                {
                    _cmdMgr.RemoveCommandGroup(1);
                }
            }
            catch
            {
            }
        }
        public void AttachCharacteristic(
    DisplayDimension displayDim,
    int characteristicNumber)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Characteristic {characteristicNumber} attached");
        }

        // OnCreateBalloons processes one sheet at a time (activate it,
        // then balloon everything on it, then move to the next) instead of
        // one flat pass over every hit on every sheet - see OnCreateBalloons
        // itself for why.
        //
        // Used to also treat an item whose sheet couldn't be determined
        // (DrawingSheetHelper.GetSheetName failed) as belonging to whichever
        // sheet was first in the run, on the theory that it still had to be
        // processed on SOME iteration or it would never get a balloon. That
        // fallback is exactly what let dimensions/notes on a sheet the user
        // never checked in the Create Balloons picker get ballooned onto
        // whatever sheet WAS checked - the picker's sheet selection is now
        // the sole owner of which sheets get populated (see
        // [[binspection_create_balloons_sheet_gating]] and the removed
        // Sheet Tolerance "Populated" checkbox), so an item has to match the
        // sheet actually being processed, full stop. DimensionHit/GtolHit/
        // SurfaceFinishHit/NoteHit.SheetName is now resolved once per scan
        // via DrawingSheetHelper.GetAllViewsBySheet (reliable - see its
        // remarks) rather than on demand later, so an item genuinely on the
        // current sheet essentially never fails to match it anymore; an
        // item that still doesn't match (including an unresolved name) is
        // simply skipped - it shows up in the "(unknown sheet)"/other-sheet
        // buckets of hitCountsBySheet below instead of being silently
        // mis-placed.
        private static bool BelongsOnSheet(string itemSheetName, string currentSheet)
        {
            return string.Equals(itemSheetName, currentSheet, StringComparison.Ordinal);
        }

        public void OnCreateBalloons()
        {
            try
            {
                ModelDoc2 model =
                    (ModelDoc2)_swApp.ActiveDoc;

                if (model == null)
                {
                    _swApp.SendMsgToUser2(
                        "No document is open.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                if (model.GetType() !=
                    (int)swDocumentTypes_e.swDocDRAWING)
                {
                    _swApp.SendMsgToUser2(
                        "Open a drawing first.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                DrawingDoc drawing =
                    model as DrawingDoc;

                if (drawing == null)
                {
                    _swApp.SendMsgToUser2(
                        "Unable to access drawing.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                // ---- Load + reconcile the saved balloon data BEFORE assigning any numbers ----
                //
                // This block is the actual fix for balloons renumbering on
                // reopen. Previously CharacteristicManager always started
                // empty, so every dimension looked "new." Now we read back
                // the external .binspection.json file for this drawing (if
                // one exists), resolve each saved characteristic to a live
                // dimension via its SolidWorks persistent reference, and
                // only treat dimensions that are genuinely new as new.
                string dataFilePath =
                    PersistenceManager.GetDataFilePath(model);

                bool persistenceAvailable =
                    !string.IsNullOrEmpty(dataFilePath);

                // Ask which sheets to populate THIS run, every run - this is
                // what makes running Create Balloons again on a different
                // sheet actually work (previously every sheet in the
                // drawing was scanned regardless of what the user meant to
                // touch, and once the STS-based gate below was added it
                // could still silently skip a sheet the user never
                // remembered to mark active there). Pre-checks whatever was
                // picked last time (ProjectData.ActiveSheets). This picker
                // is now the ONLY place sheet scope is set - Sheet Tolerance
                // Selection used to have its own "Populated" checkbox
                // writing this same field, but it never actually gated the
                // scan/balloon-placement below, just this picker's
                // pre-check, so it was removed; re-running for a new page is
                // just "uncheck the old one, check the new one" here.
                List<string> sheetNames =
                    DrawingSheetHelper.GetSheetNames(drawing);

                if (sheetNames.Count == 0)
                {
                    _swApp.SendMsgToUser2(
                        "No sheets were found in this drawing.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                ProjectData projectData =
                    persistenceAvailable
                        ? PersistenceManager.LoadProject(dataFilePath)
                        : null;

                List<string> previouslySelected =
                    projectData?.ActiveSheets != null && projectData.ActiveSheets.Count > 0
                        ? projectData.ActiveSheets
                        : sheetNames;

                BINSPECTION.UI.CreateBalloonsSheetSelectionWindow sheetPicker =
                    new BINSPECTION.UI.CreateBalloonsSheetSelectionWindow(
                        sheetNames,
                        previouslySelected,
                        projectData?.NumberRanges);

                bool? sheetsChosen = sheetPicker.ShowDialog();

                if (sheetsChosen != true || sheetPicker.SelectedSheets == null)
                    return;

                if (persistenceAvailable)
                {
                    projectData.ActiveSheets = sheetPicker.SelectedSheets;
                    projectData.NumberRanges = sheetPicker.NumberRanges;

                    PersistenceManager.SaveProject(dataFilePath, projectData);
                }

                // CharacteristicManager is static, so it can otherwise
                // carry stale entries over from a different drawing that
                // was open earlier in this SolidWorks session. Always
                // start this run clean.
                CharacteristicManager.Clear();

                // Every characteristic number this drawing has EVER had,
                // whether or not it currently resolves - used so a new
                // dimension never gets handed a number that still
                // "belongs" to an unresolvable/stale entry in the file.
                List<int> allKnownNumbers = new List<int>();

                if (persistenceAvailable)
                {
                    List<Characteristic> persisted =
                        PersistenceManager.Load(dataFilePath);

                    foreach (Characteristic saved in persisted)
                    {
                        allKnownNumbers.Add(saved.Number);
                    }

                    ReconciliationResult reconciliation =
                        ReconciliationService.Reconcile(model, persisted);

                    if (reconciliation.HasMismatches)
                    {
                        DialogResult choice =
                            ReconciliationPrompt.Show(reconciliation);

                        if (choice == DialogResult.Cancel)
                        {
                            // User backed out entirely - change nothing.
                            return;
                        }

                        if (choice == DialogResult.Yes)
                        {
                            ReconciliationService.RecreateMissingBalloons(
                                model,
                                reconciliation,
                                new BalloonManager());
                        }

                        // choice == DialogResult.No -> leave missing
                        // balloons alone for now, but still proceed with
                        // the rest of this run below.
                    }

                    // Use GetResolvedCharacteristics(), not Matched, so a
                    // characteristic whose balloon the user chose NOT to
                    // recreate still keeps its number reserved instead of
                    // being scanned as a brand-new dimension below.
                    CharacteristicManager.LoadFrom(
                        reconciliation.GetResolvedCharacteristics());
                }
                else
                {
                    // The drawing has never been saved, so there is no
                    // stable path to anchor a data file to yet. Numbering
                    // assigned in this run will NOT survive a close/reopen
                    // until the drawing is saved at least once.
                    _swApp.SendMsgToUser2(
                        "This drawing hasn't been saved yet, so balloon numbering can't be made permanent until you save it. Continuing for this session only.",
                        (int)swMessageBoxIcon_e.swMbWarning,
                        (int)swMessageBoxBtn_e.swMbOk);
                }

                // Computed ONCE and shared by every scan below - each scan
                // used to call DrawingSheetHelper.GetAllViewsBySheet itself,
                // which activates every sheet in the drawing to attribute
                // its views correctly (see that method's remarks). With
                // four scans (dimensions/GD&T/surface finishes/notes) that
                // meant visibly cycling through every sheet four times over
                // before any balloon existed. Computing it once here and
                // passing it into all four cuts that back to a single pass -
                // and restricted to just the sheets checked in the picker
                // above, since anything on a sheet the user didn't check
                // gets thrown away by BelongsOnSheet below anyway; no reason
                // to pay to activate sheets whose results are guaranteed to
                // be discarded.
                List<DrawingSheetHelper.ViewOnSheet> viewsBySheet =
                    DrawingSheetHelper.GetAllViewsBySheet(drawing, sheetPicker.SelectedSheets);

                DimensionScanner scanner =
                    new DimensionScanner();

                // TEMPORARY - see Core/ReferenceDimensionDiagnostics.cs.
                List<string> referenceDiagnostics = new List<string>();

                List<DimensionHit> dimensions =
                    scanner.GetAllDimensions(
                        drawing,
                        (dim, included) =>
                            referenceDiagnostics.Add(
                                ReferenceDimensionDiagnostics.Describe(dim, included)),
                        viewsBySheet);

                AnnotationScanner annotationScanner =
                    new AnnotationScanner();

                List<GtolHit> gtols =
                    annotationScanner.GetAllGtols(drawing, viewsBySheet);

                List<SurfaceFinishHit> surfaceFinishes =
                    annotationScanner.GetAllSurfaceFinishes(drawing, viewsBySheet);

                List<NoteHit> eligibleNotes =
                    annotationScanner.GetEligibleNotes(drawing, viewsBySheet);

                // Diagnostic only: how many of each hit type the scan
                // attributes to each sheet (via the same Hit.SheetName the
                // main loop uses for its BelongsOnSheet filter), taken
                // BEFORE any per-item skip/creation logic runs. If a sheet
                // shows 0 here even though it clearly has dimensions/GD&T/
                // etc. on it, the scan or GetAllViewsBySheet itself is the
                // problem; if it shows a nonzero count here but 0 balloons
                // end up created for that sheet, the problem is somewhere
                // in the per-item processing loop instead. Folded into the
                // final result message below so this doesn't require a
                // debugger to see.
                Dictionary<string, int> hitCountsBySheet =
                    new Dictionary<string, int>();

                void CountHit(string sheetName)
                {
                    string key = sheetName ?? "(unknown sheet)";

                    hitCountsBySheet[key] =
                        hitCountsBySheet.TryGetValue(key, out int existingCount)
                            ? existingCount + 1
                            : 1;
                }

                foreach (DimensionHit hit in dimensions)
                    CountHit(hit.SheetName);

                foreach (GtolHit hit in gtols)
                    CountHit(hit.SheetName);

                foreach (SurfaceFinishHit hit in surfaceFinishes)
                    CountHit(hit.SheetName);

                foreach (NoteHit hit in eligibleNotes)
                    CountHit(hit.SheetName);

                BalloonManager balloonManager =
                    new BalloonManager();

                int balloonsCreated = 0;
                int failedItems = 0;

                // Newly-appeared composite GD&T frames folded into an
                // already-existing balloon's report data (no new balloon
                // drawn) - see the GD&T loop's HasCharacteristic branch
                // below.
                int framesAddedToExisting = 0;

                // First handful of distinct BalloonManager.LastFailureReason
                // strings hit this run - surfaced in the final result
                // message so a "0 balloons created" run says WHY instead of
                // just how many, without needing a debugger attached to
                // SolidWorks (Debug.WriteLine alone is invisible in a
                // normal installed-addin session).
                List<string> failureReasons = new List<string>();

                // TEMPORARY - see Core/ChamferDiagnostics.cs.
                List<string> chamferDiagnostics = new List<string>();

                void RecordFailure(string reason)
                {
                    failedItems++;

                    if (string.IsNullOrEmpty(reason))
                        return;

                    if (failureReasons.Count < 5 && !failureReasons.Contains(reason))
                        failureReasons.Add(reason);
                }

                List<string> selectedSheets =
                    sheetPicker.SelectedSheets;

                // Every hit across the WHOLE drawing was already collected
                // above (the scanners themselves are sheet-agnostic reads) -
                // what happens here is processing them one sheet at a time,
                // fully, in the order the user picked, rather than one flat
                // pass over every hit on every sheet. That's what actually
                // guarantees the correct sheet is active in SolidWorks for
                // every balloon created on it: InsertNote (inside
                // BalloonManager.CreateBalloon) always lands on whichever
                // sheet SolidWorks currently has active, so activating each
                // sheet once, doing all of its work, then moving on is more
                // robust than relying on a per-item activate call sprinkled
                // through one long mixed-sheet pass. Numbering still runs
                // straight through regardless: CharacteristicManager.
                // GetNextNumber/allKnownNumbers are shared across every
                // sheet processed in this run.
                foreach (string currentSheet in selectedSheets)
                {
                    DrawingSheetHelper.ActivateSheet(drawing, currentSheet);

                    foreach (DimensionHit dimensionHit in dimensions)
                    {
                        // One bad item shouldn't kill the rest of this
                        // sheet's processing, let alone every sheet after
                        // it - mirrors AnnotationScanner.WalkAllAnnotations'
                        // per-annotation try/catch, which this loop
                        // previously lacked. Without this, an exception
                        // thrown while ballooning a single dimension/GD&T
                        // frame/etc. on sheet 2+ propagated all the way to
                        // OnCreateBalloons' outer catch, aborting every
                        // remaining sheet with no per-item indication of
                        // what failed - which read as "Create Balloons
                        // silently stops after the first sheet."
                        try
                        {
                        DisplayDimension dim = dimensionHit.Dimension;

                        // TEMPORARY - see Core/ChamferDiagnostics.cs.
                        chamferDiagnostics.Add(ChamferDiagnostics.Describe(model, dim));

                        Dimension swDim =
                            dim.GetDimension2(0);

                        if (swDim == null)
                            continue;

                        bool dimIsBasic =
                            swDim.Tolerance != null &&
                            swDim.Tolerance.Type == (int)swTolType_e.swTolBASIC;

                        // Human-readable value as currently drawn (e.g.
                        // "38.15 ±0.05"), not the raw internal dimension
                        // name ("RD2@DrawingView3") - see
                        // BalloonGridService.GetDimensionDisplay's remarks.
                        // Kept only as a human-readable label.
                        string dimensionName =
                            BalloonGridService.GetDimensionDisplay(model, dim, swDim, dimIsBasic);

                        // The dimension's persistent reference ID is the
                        // real identity we track by now.
                        // Cast to the IAnnotation interface rather than the
                        // Annotation coclass for consistency with
                        // ReconciliationService, which needs the interface
                        // cast to work reliably for objects resolved from a
                        // saved persistent reference.
                        IAnnotation dimAnnotation =
                            dim.GetAnnotation() as IAnnotation;

                        string persistentRefId =
                            PersistentReferenceHelper.GetPersistentId(
                                model,
                                dimAnnotation);

                        if (string.IsNullOrEmpty(persistentRefId))
                        {
                            // Couldn't get a stable ID for this dimension -
                            // skip it rather than balloon something we
                            // can't reliably find again next time. This
                            // check does not depend on whether the drawing
                            // has been saved yet - GetPersistReference3
                            // works on the live, in-memory model
                            // regardless.
                            continue;
                        }

                        // Already tracked - either resolved from the saved
                        // file above, or assigned earlier in this same
                        // loop. Don't double-balloon it.
                        if (CharacteristicManager.HasCharacteristic(persistentRefId))
                        {
                            continue;
                        }

                        string dimSheetName = dimensionHit.SheetName;

                        if (!BelongsOnSheet(dimSheetName, currentSheet))
                            continue;

                        // Two situations put more than one logical value
                        // under this ONE DisplayDimension/one on-sheet
                        // annotation:
                        //
                        // - A hole callout ("⌀ 17.46 THRU ALL" over "⌵⌀
                        //   20.32 X 100°, NEAR SIDE", etc. - IsHoleCallout()
                        //   true), which can combine more than one
                        //   independent feature value onto the same visual
                        //   line (a countersink diameter combined with its
                        //   angle and a trailing note, say).
                        //   HoleCalloutExtractor.GetSegments splits the
                        //   literal rendered text and translates
                        //   SolidWorks' own "<MOD-DIAM>"-style symbol tags.
                        // - A chamfer dimension shown as two combined
                        //   values ("1 X 45°", ".090 X .090", etc.) - Type2
                        //   == swChamferDimension. Unlike a hole callout, a
                        //   chamfer carries NO combined text at all
                        //   (confirmed 2026-09-15: every
                        //   swDimensionTextParts_e slot is empty for it),
                        //   and its two GetDimension2(0)/(1) sub-dimensions
                        //   have no reliable index-to-value mapping either
                        //   (confirmed same session: index 0 read as the
                        //   ANGLE, and as the wrong one of the two
                        //   supplementary readings a two-line vertex
                        //   admits). ChamferValueReader.GetSegments reads
                        //   the real distance+angle via
                        //   IDimension.GetSystemChamferValues instead,
                        //   which sidesteps both problems.
                        //
                        // Both stay grouped under one balloon on the sheet:
                        // one physical balloon on the first value (labeled
                        // with the bare group number, e.g. "6" - not "6.1"),
                        // every value after that tracked as its own
                        // data-only sibling characteristic ("6.1", "6.2",
                        // ...) under the same group number, using the
                        // synthetic "<realId>#<n>" PersistentRefId the
                        // multi-line note splitter above already uses (see
                        // ReconciliationService.Reconcile for how that's
                        // resolved back to the shared dimension).
                        List<string> multiValueLines =
                            HoleCalloutExtractor.GetSegments(model, dim) ??
                            ChamferValueReader.GetSegments(model, dim);

                        if (multiValueLines != null)
                        {
                            int groupNumber =
                                CharacteristicManager.GetNextNumberForSheet(
                                    currentSheet, sheetPicker.NumberRanges, allKnownNumbers);

                            // The group's first value is the bare whole
                            // number (e.g. "6"), never "6.1" - only values
                            // after it get a decimal sub-number, starting
                            // at .1.
                            string firstDisplayNumber =
                                groupNumber.ToString();

                            Note multiValueBalloon =
                                balloonManager.CreateBalloon(
                                    model,
                                    dim,
                                    firstDisplayNumber,
                                    dimSheetName,
                                    dimensionHit.View);

                            if (multiValueBalloon == null)
                            {
                                RecordFailure(balloonManager.LastFailureReason);
                                continue;
                            }

                            string multiValueBalloonPersistId =
                                balloonManager.GetBalloonPersistId(model, multiValueBalloon);

                            Characteristic firstCharacteristic = new Characteristic
                            {
                                Number = groupNumber,
                                SubNumber = null,
                                PersistentRefId = persistentRefId,
                                BalloonPersistId = multiValueBalloonPersistId,
                                DimensionName = multiValueLines[0],
                                SheetName = dimSheetName,
                            };

                            CharacteristicManager.AddCharacteristic(firstCharacteristic);

                            for (int i = 1; i < multiValueLines.Count; i++)
                            {
                                Characteristic siblingCharacteristic = new Characteristic
                                {
                                    Number = groupNumber,
                                    SubNumber = i,
                                    PersistentRefId = persistentRefId + "#" + (i + 1),
                                    BalloonPersistId = multiValueBalloonPersistId,
                                    DimensionName = multiValueLines[i],
                                    SheetName = dimSheetName,
                                };

                                CharacteristicManager.AddCharacteristic(siblingCharacteristic);
                            }

                            balloonsCreated++;

                            continue;
                        }

                        int nextNumber =
                            CharacteristicManager.GetNextNumberForSheet(
                                currentSheet, sheetPicker.NumberRanges, allKnownNumbers);

                        Note note =
                            balloonManager.CreateBalloon(
                                model,
                                dim,
                                nextNumber,
                                dimSheetName,
                                dimensionHit.View);

                        if (note == null)
                        {
                            RecordFailure(balloonManager.LastFailureReason);
                            continue;
                        }

                        Characteristic characteristic = new Characteristic
                        {
                            Number = nextNumber,
                            PersistentRefId = persistentRefId,
                            BalloonPersistId = balloonManager.GetBalloonPersistId(model, note),
                            DimensionName = dimensionName,
                            SheetName = dimSheetName,
                            IsBasic = dimIsBasic,
                        };

                        CharacteristicManager.AddCharacteristic(characteristic);

                        balloonsCreated++;
                        }
                        catch (Exception ex)
                        {
                            RecordFailure(ex.GetType().Name + ": " + ex.Message);

                            System.Diagnostics.Debug.WriteLine(
                                "OnCreateBalloons dimension item Error: " + ex);
                        }
                    }

                    // ---- GD&T feature control frames ----
                    foreach (GtolHit gtolHit in gtols)
                    {
                        try
                        {
                        IGtol gtol = gtolHit.Gtol;

                        IAnnotation gtolAnnotation =
                            gtol.GetAnnotation() as IAnnotation;

                        string persistentRefId =
                            PersistentReferenceHelper.GetPersistentId(
                                model,
                                gtolAnnotation);

                        if (string.IsNullOrEmpty(persistentRefId))
                            continue;

                        string gtolSheetName = gtolHit.SheetName;

                        if (!BelongsOnSheet(gtolSheetName, currentSheet))
                            continue;

                        if (CharacteristicManager.HasCharacteristic(persistentRefId))
                        {
                            // Already ballooned - but SolidWorks lets a user
                            // stack an ADDITIONAL frame under an existing
                            // composite FCF after the fact (the FCF dialog's
                            // own "add tolerance" row), which grows this
                            // SAME IGtol's GetFrameCount()/GtolTextFormatter.
                            // GetFrames() without changing its persistent ref
                            // ID. Without this recheck, that new frame was
                            // invisible forever: the very first thing this
                            // block used to do on an already-known ref ID
                            // was a bare `continue`, so nothing ever looked
                            // at whether MORE frames had shown up since the
                            // last run. Re-derive the live frame list and
                            // add any not-yet-recorded one as a data-only
                            // sibling under the SAME balloon/number, exactly
                            // like a brand-new composite frame's frames[1+]
                            // below - there's still only the one physical
                            // balloon, so nothing new is drawn on the sheet,
                            // but the new frame now gets its own report row.
                            Characteristic existingBase =
                                CharacteristicManager.GetCharacteristic(persistentRefId);

                            if (existingBase == null)
                                continue;

                            List<GtolTextFormatter.GdtFrameResult> liveFrames =
                                GtolTextFormatter.GetFrames(gtol);

                            for (int i = 1; i < liveFrames.Count; i++)
                            {
                                string siblingRefId = persistentRefId + "#" + (i + 1);

                                if (CharacteristicManager.HasCharacteristic(siblingRefId))
                                    continue;

                                Characteristic newFrameCharacteristic = new Characteristic
                                {
                                    Number = existingBase.Number,
                                    SubNumber = i,
                                    PersistentRefId = siblingRefId,
                                    BalloonPersistId = existingBase.BalloonPersistId,
                                    DimensionName = liveFrames[i].DisplayText,
                                    GdtBoxText = liveFrames[i].BoxText,
                                    GdtToleranceValue = liveFrames[i].ToleranceValue,
                                    SheetName = existingBase.SheetName ?? gtolSheetName,
                                };

                                CharacteristicManager.AddCharacteristic(newFrameCharacteristic);

                                framesAddedToExisting++;
                            }

                            continue;
                        }

                        int nextNumber =
                            CharacteristicManager.GetNextNumberForSheet(
                                currentSheet, sheetPicker.NumberRanges, allKnownNumbers);

                        Note note =
                            balloonManager.CreateBalloon(
                                model,
                                gtol,
                                nextNumber.ToString(),
                                gtolSheetName,
                                gtolHit.View);

                        if (note == null)
                        {
                            RecordFailure(balloonManager.LastFailureReason);
                            continue;
                        }

                        string gtolBalloonPersistId =
                            balloonManager.GetBalloonPersistId(model, note);

                        // GD&T frames don't have a simple plain-text label
                        // the way a dimension's FullName does -
                        // GtolTextFormatter reconstructs the frame's
                        // symbol/tolerance/datum content from the GTOL API
                        // (falls back to a generic descriptor if that fails
                        // for any reason). A composite callout ("Position
                        // 0.004 A B C" over "Position 0.001 A B") comes back
                        // as more than one element - there's still only the
                        // one physical balloon (numbered plain "N", never
                        // re-labeled), but each frame after the first is
                        // tracked as its own data-only sibling
                        // characteristic at N.1, N.2, ... using the same
                        // synthetic "<realId>#<n>" PersistentRefId pattern
                        // the multi-line note splitter below uses (see
                        // ReconciliationService.Reconcile for how that's
                        // resolved back to the shared Gtol on a later run).
                        List<GtolTextFormatter.GdtFrameResult> frames =
                            GtolTextFormatter.GetFrames(gtol);

                        Characteristic characteristic = new Characteristic
                        {
                            Number = nextNumber,
                            PersistentRefId = persistentRefId,
                            BalloonPersistId = gtolBalloonPersistId,
                            DimensionName = frames[0].DisplayText,
                            GdtBoxText = frames[0].BoxText,
                            GdtToleranceValue = frames[0].ToleranceValue,
                            SheetName = gtolSheetName,
                        };

                        CharacteristicManager.AddCharacteristic(characteristic);

                        for (int i = 1; i < frames.Count; i++)
                        {
                            Characteristic siblingCharacteristic = new Characteristic
                            {
                                Number = nextNumber,
                                SubNumber = i,
                                PersistentRefId = persistentRefId + "#" + (i + 1),
                                BalloonPersistId = gtolBalloonPersistId,
                                DimensionName = frames[i].DisplayText,
                                GdtBoxText = frames[i].BoxText,
                                GdtToleranceValue = frames[i].ToleranceValue,
                                SheetName = gtolSheetName,
                            };

                            CharacteristicManager.AddCharacteristic(siblingCharacteristic);
                        }

                        balloonsCreated++;
                        }
                        catch (Exception ex)
                        {
                            RecordFailure(ex.GetType().Name + ": " + ex.Message);

                            System.Diagnostics.Debug.WriteLine(
                                "OnCreateBalloons GD&T item Error: " + ex);
                        }
                    }

                    // ---- Surface finish symbols ----
                    foreach (SurfaceFinishHit surfaceFinishHit in surfaceFinishes)
                    {
                        try
                        {
                        ISFSymbol surfaceFinish = surfaceFinishHit.SurfaceFinish;

                        IAnnotation surfaceFinishAnnotation =
                            surfaceFinish.GetAnnotation() as IAnnotation;

                        string persistentRefId =
                            PersistentReferenceHelper.GetPersistentId(
                                model,
                                surfaceFinishAnnotation);

                        if (string.IsNullOrEmpty(persistentRefId))
                            continue;

                        if (CharacteristicManager.HasCharacteristic(persistentRefId))
                            continue;

                        string surfaceFinishSheetName = surfaceFinishHit.SheetName;

                        if (!BelongsOnSheet(surfaceFinishSheetName, currentSheet))
                            continue;

                        int nextNumber =
                            CharacteristicManager.GetNextNumberForSheet(
                                currentSheet, sheetPicker.NumberRanges, allKnownNumbers);

                        Note note =
                            balloonManager.CreateBalloon(
                                model,
                                surfaceFinish,
                                nextNumber.ToString(),
                                surfaceFinishSheetName,
                                surfaceFinishHit.View);

                        if (note == null)
                        {
                            RecordFailure(balloonManager.LastFailureReason);
                            continue;
                        }

                        Characteristic characteristic = new Characteristic
                        {
                            Number = nextNumber,
                            PersistentRefId = persistentRefId,
                            BalloonPersistId = balloonManager.GetBalloonPersistId(model, note),
                            DimensionName = BuildSurfaceFinishText(surfaceFinish),
                            SheetName = surfaceFinishSheetName,
                        };

                        CharacteristicManager.AddCharacteristic(characteristic);

                        balloonsCreated++;
                        }
                        catch (Exception ex)
                        {
                            RecordFailure(ex.GetType().Name + ": " + ex.Message);

                            System.Diagnostics.Debug.WriteLine(
                                "OnCreateBalloons surface finish item Error: " + ex);
                        }
                    }

                    // ---- Free-standing notes (leader-attached, not one of
                    // our own balloons - see AnnotationScanner.GetEligibleNotes) ----
                    foreach (NoteHit noteHit in eligibleNotes)
                    {
                        try
                        {
                        INote sourceNote = noteHit.Note;

                        IAnnotation noteAnnotation =
                            sourceNote.GetAnnotation() as IAnnotation;

                        string persistentRefId =
                            PersistentReferenceHelper.GetPersistentId(
                                model,
                                noteAnnotation);

                        if (string.IsNullOrEmpty(persistentRefId))
                            continue;

                        if (CharacteristicManager.HasCharacteristic(persistentRefId))
                            continue;

                        List<string> lines =
                            AnnotationScanner.SplitLines(((Note)sourceNote).GetText());

                        if (lines.Count == 0)
                            continue;

                        string noteSheetName = noteHit.SheetName;

                        if (!BelongsOnSheet(noteSheetName, currentSheet))
                            continue;

                        if (lines.Count == 1)
                        {
                            int nextNumber =
                                CharacteristicManager.GetNextNumberForSheet(
                                    currentSheet, sheetPicker.NumberRanges, allKnownNumbers);

                            Note note =
                                balloonManager.CreateBalloon(
                                    model,
                                    sourceNote,
                                    nextNumber.ToString(),
                                    noteSheetName,
                                    noteHit.View);

                            if (note == null)
                            {
                                RecordFailure(balloonManager.LastFailureReason);
                                continue;
                            }

                            Characteristic characteristic = new Characteristic
                            {
                                Number = nextNumber,
                                PersistentRefId = persistentRefId,
                                BalloonPersistId = balloonManager.GetBalloonPersistId(model, note),
                                DimensionName = lines[0],
                                SheetName = noteSheetName,
                            };

                            CharacteristicManager.AddCharacteristic(characteristic);

                            balloonsCreated++;
                        }
                        else
                        {
                            // Multi-line note: automatically split into one
                            // characteristic per line under a single group
                            // number (N.1, N.2, N.3...), matching what
                            // manually running Group Balloon on separate
                            // dimensions would produce - except there's
                            // only ONE physical balloon here (on line 1),
                            // since every line comes from the same one
                            // note, not a separate dimension each with its
                            // own balloon. Lines 2+ are data-only and get a
                            // synthetic "<realId>#<line>" id instead of the
                            // note's own persistent reference, purely so
                            // each has a distinct CharacteristicManager
                            // entry - see ReconciliationService.Reconcile
                            // for how that marker is recognized and safely
                            // resolved back to the shared note on a later
                            // run.
                            int groupNumber =
                                CharacteristicManager.GetNextNumberForSheet(
                                    currentSheet, sheetPicker.NumberRanges, allKnownNumbers);

                            // The group's first line is the bare whole
                            // number (e.g. "6"), never "6.1" - only lines
                            // after it get a decimal sub-number, starting
                            // at .1.
                            string firstDisplayNumber =
                                groupNumber.ToString();

                            Note note =
                                balloonManager.CreateBalloon(
                                    model,
                                    sourceNote,
                                    firstDisplayNumber,
                                    noteSheetName,
                                    noteHit.View);

                            if (note == null)
                            {
                                RecordFailure(balloonManager.LastFailureReason);
                                continue;
                            }

                            string multiLineNoteBalloonPersistId =
                                balloonManager.GetBalloonPersistId(model, note);

                            Characteristic firstCharacteristic = new Characteristic
                            {
                                Number = groupNumber,
                                SubNumber = null,
                                PersistentRefId = persistentRefId,
                                BalloonPersistId = multiLineNoteBalloonPersistId,
                                DimensionName = lines[0],
                                SheetName = noteSheetName,
                            };

                            CharacteristicManager.AddCharacteristic(firstCharacteristic);

                            for (int i = 1; i < lines.Count; i++)
                            {
                                Characteristic siblingCharacteristic = new Characteristic
                                {
                                    Number = groupNumber,
                                    SubNumber = i,
                                    PersistentRefId = persistentRefId + "#" + (i + 1),
                                    BalloonPersistId = multiLineNoteBalloonPersistId,
                                    DimensionName = lines[i],
                                    SheetName = noteSheetName,
                                };

                                CharacteristicManager.AddCharacteristic(siblingCharacteristic);
                            }

                            balloonsCreated++;
                        }
                        }
                        catch (Exception ex)
                        {
                            RecordFailure(ex.GetType().Name + ": " + ex.Message);

                            System.Diagnostics.Debug.WriteLine(
                                "OnCreateBalloons note item Error: " + ex);
                        }
                    }
                }

                if (persistenceAvailable)
                {
                    // Write the merged result (previously-matched +
                    // recreated + newly-created characteristics) back out,
                    // so the next time this drawing is opened, none of
                    // this has to be figured out from scratch again.
                    PersistenceManager.Save(
                        dataFilePath,
                        CharacteristicManager.ExportAll());
                }

                // TEMPORARY - see Core/ChamferDiagnostics.cs. Falls back to
                // the temp folder if the drawing was never saved (no
                // dataFilePath yet), same as other debug-dump features in
                // this add-in.
                string chamferDebugPath = null;

                if (chamferDiagnostics.Count > 0)
                {
                    try
                    {
                        chamferDebugPath =
                            persistenceAvailable
                                ? dataFilePath + ".chamfer-debug.txt"
                                : Path.Combine(Path.GetTempPath(), "binspection.chamfer-debug.txt");

                        File.WriteAllText(chamferDebugPath, string.Join(System.Environment.NewLine, chamferDiagnostics));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("ChamferDiagnostics write Error: " + ex);
                        chamferDebugPath = null;
                    }
                }

                // TEMPORARY - see Core/ReferenceDimensionDiagnostics.cs.
                string referenceDebugPath = null;

                if (referenceDiagnostics.Count > 0)
                {
                    try
                    {
                        referenceDebugPath =
                            persistenceAvailable
                                ? dataFilePath + ".reference-debug.txt"
                                : Path.Combine(Path.GetTempPath(), "binspection.reference-debug.txt");

                        File.WriteAllText(referenceDebugPath, string.Join(System.Environment.NewLine, referenceDiagnostics));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("ReferenceDimensionDiagnostics write Error: " + ex);
                        referenceDebugPath = null;
                    }
                }

                string resultMessage =
                    "Created " + balloonsCreated + " balloons.";

                if (framesAddedToExisting > 0)
                {
                    resultMessage +=
                        "\n\n" + framesAddedToExisting +
                        " new GD&T frame(s) on already-ballooned feature control frames were added to the report.";
                }

                if (chamferDebugPath != null)
                    resultMessage += "\n\nDiagnostic dump written to:\n" + chamferDebugPath;

                if (referenceDebugPath != null)
                    resultMessage += "\n\nReference-dimension diagnostic dump written to:\n" + referenceDebugPath;

                if (failedItems > 0)
                {
                    resultMessage +=
                        "\n\n" + failedItems +
                        " item(s) could not be ballooned and were skipped.";

                    if (failureReasons.Count > 0)
                    {
                        resultMessage +=
                            "\n\nReason(s):\n- " +
                            string.Join("\n- ", failureReasons);
                    }
                }

                // Always show the per-sheet scan breakdown when more than
                // one sheet was picked this run - see hitCountsBySheet's
                // remarks above for why this is the fastest way to tell
                // "the scan never found anything there" apart from "it
                // found items but the loop silently skipped them" without
                // attaching a debugger.
                if (selectedSheets.Count > 1)
                {
                    resultMessage += "\n\nItems found by sheet (before filtering):";

                    foreach (string sheet in selectedSheets)
                    {
                        int count =
                            hitCountsBySheet.TryGetValue(sheet, out int sheetCount)
                                ? sheetCount
                                : 0;

                        resultMessage += "\n- " + sheet + ": " + count;
                    }

                    int unknownCount =
                        hitCountsBySheet.TryGetValue("(unknown sheet)", out int unknown)
                            ? unknown
                            : 0;

                    if (unknownCount > 0)
                    {
                        resultMessage +=
                            "\n- (sheet name could not be determined): " + unknownCount;
                    }
                }

                _swApp.SendMsgToUser2(
                    resultMessage,
                    failedItems > 0
                        ? (int)swMessageBoxIcon_e.swMbWarning
                        : (int)swMessageBoxIcon_e.swMbInformation,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
            catch (Exception ex)
            {
                _swApp.SendMsgToUser2(
                    ex.ToString(),
                    (int)swMessageBoxIcon_e.swMbStop,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
        }

        public void OnGenerateReport()
        {
            try
            {
                ModelDoc2 model =
                    (ModelDoc2)_swApp.ActiveDoc;

                if (model == null)
                {
                    _swApp.SendMsgToUser2(
                        "No document is open.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                if (model.GetType() !=
                    (int)swDocumentTypes_e.swDocDRAWING)
                {
                    _swApp.SendMsgToUser2(
                        "Open a drawing first.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                string dataFilePath =
                    PersistenceManager.GetDataFilePath(model);

                if (string.IsNullOrEmpty(dataFilePath))
                {
                    _swApp.SendMsgToUser2(
                        "This drawing hasn't been saved yet, so there's no saved balloon data to report on.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                ProjectData projectData =
                    PersistenceManager.LoadProject(dataFilePath);

                if (projectData.Characteristics.Count == 0)
                {
                    _swApp.SendMsgToUser2(
                        "No saved balloon data was found for this drawing. Run Create Balloons first.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                // Report lives next to the drawing, alongside the
                // .binspection.json sidecar file - dropping the .SLDDRW
                // extension and adding our own suffix means re-running this
                // always overwrites the same report file instead of
                // accumulating one per run.
                string reportPath =
                    Path.ChangeExtension(model.GetPathName(), null) +
                    " - Inspection Report.xlsx";

                bool success =
                    ReportGenerator.BuildInspectionReport(
                        model, projectData.Characteristics, reportPath, projectData.ReportHeaderSettings,
                        projectData.ToleranceSets, projectData.SheetToleranceAssignments);

                if (success)
                {
                    _swApp.SendMsgToUser2(
                        "Inspection report saved to:\n" + reportPath,
                        (int)swMessageBoxIcon_e.swMbInformation,
                        (int)swMessageBoxBtn_e.swMbOk);
                }
                else
                {
                    _swApp.SendMsgToUser2(
                        "Failed to generate the inspection report.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);
                }
            }
            catch (Exception ex)
            {
                _swApp.SendMsgToUser2(
                    ex.ToString(),
                    (int)swMessageBoxIcon_e.swMbStop,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
        }

        // Restores balloons from the saved .binspection.json data file.
        //
        // Unlike OnCreateBalloons, this command NEVER assigns a new
        // number or balloons a dimension that isn't already in the saved
        // file - it only recreates balloons the file says should exist
        // but aren't currently on the sheet, and optionally cleans up
        // balloons that don't match anything saved. Use this to recover
        // from a balloon getting deleted, moved by a metadata glitch, or
        // duplicated by a bug, without touching anything else.
        public void OnRestoreBalloons()
        {
            try
            {
                ModelDoc2 model =
                    (ModelDoc2)_swApp.ActiveDoc;

                if (model == null)
                {
                    _swApp.SendMsgToUser2(
                        "No document is open.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                if (model.GetType() !=
                    (int)swDocumentTypes_e.swDocDRAWING)
                {
                    _swApp.SendMsgToUser2(
                        "Open a drawing first.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                string dataFilePath =
                    PersistenceManager.GetDataFilePath(model);

                if (string.IsNullOrEmpty(dataFilePath))
                {
                    _swApp.SendMsgToUser2(
                        "This drawing hasn't been saved yet, so there's no saved balloon data to restore from.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                List<Characteristic> persisted =
                    PersistenceManager.Load(dataFilePath);

                if (persisted.Count == 0)
                {
                    _swApp.SendMsgToUser2(
                        "No saved balloon data was found for this drawing.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                ReconciliationResult reconciliation =
                    ReconciliationService.Reconcile(model, persisted);

                if (!reconciliation.HasMismatches)
                {
                    // Genuinely nothing to do: every saved balloon is
                    // already on the sheet, nothing extra to clean up, and
                    // nothing unresolvable.
                    _swApp.SendMsgToUser2(
                        "All " + persisted.Count + " saved balloon(s) are already on the sheet. Nothing to restore.",
                        (int)swMessageBoxIcon_e.swMbInformation,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                BalloonManager balloonManager =
                    new BalloonManager();

                int restoredCount = 0;
                int deletedCount = 0;

                // Same "here's what doesn't match" summary Create Balloons
                // uses - this ALWAYS shows the full per-characteristic
                // detail (including exactly why anything is unresolvable),
                // even when there's nothing to recreate, so this command
                // never hides diagnostic information behind a bare count.
                DialogResult recreateChoice =
                    ReconciliationPrompt.Show(reconciliation);

                if (recreateChoice == DialogResult.Cancel)
                {
                    return;
                }

                if (recreateChoice == DialogResult.Yes)
                {
                    restoredCount = reconciliation.MissingBalloons.Count;

                    ReconciliationService.RecreateMissingBalloons(
                        model,
                        reconciliation,
                        balloonManager);
                }

                // Deleting something is a bigger decision than recreating
                // something that used to be there, so it gets its own,
                // separate confirmation rather than being folded into the
                // answer above.
                if (reconciliation.OrphanedBalloonNumbers.Count > 0)
                {
                    DialogResult deleteChoice =
                        ReconciliationPrompt.ShowDeleteOrphanedPrompt(
                            reconciliation.OrphanedBalloonNumbers);

                    if (deleteChoice == DialogResult.Yes)
                    {
                        deletedCount = reconciliation.OrphanedBalloonNumbers.Count;

                        ReconciliationService.DeleteOrphanedBalloons(
                            model,
                            reconciliation,
                            balloonManager);
                    }
                }

                // Keep the data file in sync with whatever the drawing
                // actually looks like now. GetResolvedCharacteristics(),
                // not Matched, so answering "No" to recreating a balloon
                // doesn't erase that characteristic from the saved file -
                // its dimension still resolves, it just doesn't have a
                // balloon drawn right now.
                CharacteristicManager.Clear();
                CharacteristicManager.LoadFrom(
                    reconciliation.GetResolvedCharacteristics());

                PersistenceManager.Save(
                    dataFilePath,
                    CharacteristicManager.ExportAll());

                string resultMessage =
                    "Restored " + restoredCount + " balloon(s)";

                if (deletedCount > 0)
                {
                    resultMessage += " and deleted " + deletedCount + " orphaned balloon(s).";
                }
                else
                {
                    resultMessage += ".";
                }

                _swApp.SendMsgToUser2(
                    resultMessage,
                    (int)swMessageBoxIcon_e.swMbInformation,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
            catch (Exception ex)
            {
                _swApp.SendMsgToUser2(
                    ex.ToString(),
                    (int)swMessageBoxIcon_e.swMbStop,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
        }

        // Single home for reassigning balloon numbers AND editing
        // attributes: Group/Ungroup/Add/Un-Number/Re-Number, manual number
        // entry, Method/Classification editing, and Legacy
        // Conversion, all live in one WPF window (see UI/BalloonManagerWindow)
        // driven by checkboxes/cells in a grid rather than a SolidWorks
        // graphics-area selection. Replaces the former Group Balloon/
        // Ungroup Balloon/Toggle Unnumbered/Add Balloon/Delete Balloon/
        // Match Legacy Numbers/Edit Attributes/Edit All Attributes
        // commands. Every action inside the window applies and saves
        // immediately (see Core/BalloonGridService), so this handler just
        // needs to refresh the in-memory CharacteristicManager afterward.
        public void OnOpenBalloonManager()
        {
            try
            {
                ModelDoc2 model =
                    (ModelDoc2)_swApp.ActiveDoc;

                if (model == null)
                {
                    _swApp.SendMsgToUser2(
                        "No document is open.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                if (model.GetType() !=
                    (int)swDocumentTypes_e.swDocDRAWING)
                {
                    _swApp.SendMsgToUser2(
                        "Open a drawing first.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                DrawingDoc drawing =
                    model as DrawingDoc;

                if (drawing == null)
                {
                    _swApp.SendMsgToUser2(
                        "Unable to access drawing.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                if (_balloonManagerWindow != null)
                {
                    _balloonManagerWindow.Activate();
                    return;
                }

                string dataFilePath =
                    PersistenceManager.GetDataFilePath(model);

                if (string.IsNullOrEmpty(dataFilePath))
                {
                    _swApp.SendMsgToUser2(
                        "This drawing hasn't been saved yet, so balloon numbers can't be managed until you save it.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                ProjectData projectData =
                    PersistenceManager.LoadProject(dataFilePath);

                BINSPECTION.UI.BalloonManagerWindow window =
                    new BINSPECTION.UI.BalloonManagerWindow(model, drawing, dataFilePath, projectData);

                window.Closed += (sender, args) =>
                {
                    _balloonManagerWindow = null;

                    if (window.DataChanged)
                    {
                        CharacteristicManager.Clear();
                        CharacteristicManager.LoadFrom(projectData.Characteristics);
                    }
                };

                _balloonManagerWindow = window;

                window.Show();
            }
            catch (Exception ex)
            {
                _swApp.SendMsgToUser2(
                    ex.ToString(),
                    (int)swMessageBoxIcon_e.swMbStop,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
        }

        // Debugging tool: deletes every balloon on the sheet AND wipes all
        // saved balloon data (Characteristics), so the next Create Balloons
        // run starts clean at 1. Project-level setup (ActiveSheets,
        // ToleranceSets, SheetToleranceAssignments) is left alone - that
        // isn't "balloon data," it's the sheet/tolerance configuration.
        // Always confirmed first since this is destructive and not
        // reversible by this add-in.
        public void OnDeleteAllBalloons()
        {
            try
            {
                ModelDoc2 model =
                    (ModelDoc2)_swApp.ActiveDoc;

                if (model == null)
                {
                    _swApp.SendMsgToUser2(
                        "No document is open.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                if (model.GetType() !=
                    (int)swDocumentTypes_e.swDocDRAWING)
                {
                    _swApp.SendMsgToUser2(
                        "Open a drawing first.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                DialogResult confirm =
                    MessageBox.Show(
                        "This will delete EVERY balloon on this drawing and permanently erase all saved " +
                        "balloon data (numbers, methods, classifications, comments)." +
                        System.Environment.NewLine + System.Environment.NewLine +
                        "This is intended for debugging and cannot be undone by this add-in." +
                        System.Environment.NewLine + System.Environment.NewLine +
                        "Delete ALL balloons and balloon data now?",
                        "BINSPECTION - Delete All Balloons",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2);

                if (confirm != DialogResult.Yes)
                    return;

                BalloonManager balloonManager =
                    new BalloonManager();

                int deletedCount =
                    balloonManager.DeleteAllBalloons(model);

                CharacteristicManager.Clear();

                string dataFilePath =
                    PersistenceManager.GetDataFilePath(model);

                if (!string.IsNullOrEmpty(dataFilePath))
                {
                    PersistenceManager.Save(
                        dataFilePath,
                        new List<Characteristic>());
                }

                _swApp.SendMsgToUser2(
                    "Deleted " + deletedCount + " balloon(s) and cleared all saved balloon data.",
                    (int)swMessageBoxIcon_e.swMbInformation,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
            catch (Exception ex)
            {
                _swApp.SendMsgToUser2(
                    ex.ToString(),
                    (int)swMessageBoxIcon_e.swMbStop,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
        }

        // Removes every balloon's visual note from the sheet, the same way
        // Delete Balloon does for one - the saved data file
        // (Characteristics, numbering) is left completely untouched,
        // unlike Delete All Balloons which wipes both. Use this to
        // declutter the sheet or reprint without balloons visible; a later
        // Restore Balloons/Create Balloons run will put them all back using
        // the numbers already on file. No confirmation prompt - unlike
        // Delete All Balloons, nothing here is permanent or destructive to
        // saved data.
        public void OnRemoveBalloons()
        {
            try
            {
                ModelDoc2 model =
                    (ModelDoc2)_swApp.ActiveDoc;

                if (model == null)
                {
                    _swApp.SendMsgToUser2(
                        "No document is open.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                if (model.GetType() !=
                    (int)swDocumentTypes_e.swDocDRAWING)
                {
                    _swApp.SendMsgToUser2(
                        "Open a drawing first.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                BalloonManager balloonManager =
                    new BalloonManager();

                int removedCount =
                    balloonManager.DeleteAllBalloons(model);

                _swApp.SendMsgToUser2(
                    "Removed " + removedCount + " balloon(s) from the sheet. Saved balloon data was not changed.",
                    (int)swMessageBoxIcon_e.swMbInformation,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
            catch (Exception ex)
            {
                _swApp.SendMsgToUser2(
                    ex.ToString(),
                    (int)swMessageBoxIcon_e.swMbStop,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
        }

        // Runs Remove Balloons then Restore Balloons back-to-back, with no
        // message box in between and no reconciliation prompt - since
        // every balloon on a chosen sheet was just removed by this same
        // call, every saved characteristic on that sheet is unconditionally
        // "missing" and gets recreated with its existing number, and there
        // can be no orphaned balloon numbers left to ask about deleting.
        // Use this to force every balloon to redraw against current
        // geometry (position, leader, etc.) in one click. Saved balloon
        // data is untouched.
        //
        // Asks which sheets to refresh THIS run (RefreshBalloonsSheetSelectionWindow),
        // same convention as Create Balloons' own sheet picker - a
        // multi-sheet drawing shouldn't have every sheet's balloons torn
        // down and redrawn just to fix one sheet. Only balloons on the
        // chosen sheets are deleted (BalloonManager.DeleteAllBalloons's
        // sheet-scoped overload); Reconcile still runs against the WHOLE
        // saved data file (not just the chosen sheets) so every other
        // sheet's characteristics resolve normally against their untouched
        // balloons and nothing gets dropped from the saved file.
        public void OnRefreshBalloons()
        {
            try
            {
                ModelDoc2 model =
                    (ModelDoc2)_swApp.ActiveDoc;

                if (model == null)
                {
                    _swApp.SendMsgToUser2(
                        "No document is open.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                if (model.GetType() !=
                    (int)swDocumentTypes_e.swDocDRAWING)
                {
                    _swApp.SendMsgToUser2(
                        "Open a drawing first.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                DrawingDoc drawing =
                    model as DrawingDoc;

                if (drawing == null)
                {
                    _swApp.SendMsgToUser2(
                        "Unable to access drawing.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                string dataFilePath =
                    PersistenceManager.GetDataFilePath(model);

                if (string.IsNullOrEmpty(dataFilePath))
                {
                    _swApp.SendMsgToUser2(
                        "This drawing hasn't been saved yet, so there's no saved balloon data to refresh from.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                List<Characteristic> persisted =
                    PersistenceManager.Load(dataFilePath);

                if (persisted.Count == 0)
                {
                    _swApp.SendMsgToUser2(
                        "No saved balloon data was found for this drawing.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                List<string> sheetNames =
                    DrawingSheetHelper.GetSheetNames(drawing);

                if (sheetNames.Count == 0)
                {
                    _swApp.SendMsgToUser2(
                        "No sheets were found in this drawing.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                ProjectData projectData =
                    PersistenceManager.LoadProject(dataFilePath);

                List<string> previouslySelected =
                    projectData?.ActiveSheets != null && projectData.ActiveSheets.Count > 0
                        ? projectData.ActiveSheets
                        : sheetNames;

                BINSPECTION.UI.RefreshBalloonsSheetSelectionWindow sheetPicker =
                    new BINSPECTION.UI.RefreshBalloonsSheetSelectionWindow(
                        sheetNames,
                        previouslySelected);

                bool? sheetsChosen = sheetPicker.ShowDialog();

                if (sheetsChosen != true || sheetPicker.SelectedSheets == null)
                    return;

                HashSet<string> chosenSheets =
                    new HashSet<string>(sheetPicker.SelectedSheets, StringComparer.OrdinalIgnoreCase);

                BalloonManager balloonManager =
                    new BalloonManager();

                int deletedCount =
                    balloonManager.DeleteAllBalloons(model, chosenSheets);

                ReconciliationResult reconciliation =
                    ReconciliationService.Reconcile(model, persisted);

                int restoredCount = reconciliation.MissingBalloons.Count;

                ReconciliationService.RecreateMissingBalloons(
                    model,
                    reconciliation,
                    balloonManager);

                // Same data-file sync as Restore Balloons, so numbering and
                // resolved-characteristic tracking stay consistent after
                // the refresh.
                CharacteristicManager.Clear();
                CharacteristicManager.LoadFrom(
                    reconciliation.GetResolvedCharacteristics());

                PersistenceManager.Save(
                    dataFilePath,
                    CharacteristicManager.ExportAll());

                // Temporary diagnostic breakdown (see
                // [[binspection_refresh_balloons_sheet_picker]] in project
                // memory) - "Refreshed 0 balloon(s)" alone doesn't say
                // whether nothing was deleted (sheet-scoping filter in
                // DeleteAllBalloons never matched, e.g. View.Sheet not
                // resolving for a sheet-level note's owning view) or
                // whether deletion worked but recreation didn't (resolution
                // failures). Remove once the real cause is confirmed.
                _swApp.SendMsgToUser2(
                    "Refreshed " + restoredCount + " balloon(s)." +
                        System.Environment.NewLine +
                        System.Environment.NewLine +
                        "Diagnostic: deleted=" + deletedCount +
                        ", matched=" + reconciliation.Matched.Count +
                        ", unresolvable=" + reconciliation.Unresolvable.Count +
                        ", orphaned=" + reconciliation.OrphanedBalloonNumbers.Count +
                        ", chosenSheets=[" + string.Join(", ", chosenSheets) + "]",
                    (int)swMessageBoxIcon_e.swMbInformation,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
            catch (Exception ex)
            {
                _swApp.SendMsgToUser2(
                    ex.ToString(),
                    (int)swMessageBoxIcon_e.swMbStop,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
        }

        // Resolves a balloon's own persistent reference id (
        // Characteristic.BalloonPersistId) back to its IAnnotation, for
        // reading/writing position. GetObjectByPersistReference3 hands a
        // balloon's persist id back as an INote (the coclass cast to the
        // interface it implements), not as IAnnotation directly - same
        // resolved-type ambiguity ReconciliationService.Reconcile already
        // works around for the ballooned-dimension side - so a plain
        // "resolved as IAnnotation" cast always came back null and made
        // Save/Restore Position treat every balloon as missing. Falls back
        // to INote.GetAnnotation() when the direct cast fails.
        private static IAnnotation ResolveBalloonAnnotation(
            ModelDoc2 model,
            string balloonPersistId)
        {
            swPersistReferencedObjectStates_e state;

            object resolved =
                PersistentReferenceHelper.ResolvePersistentId(
                    model,
                    balloonPersistId,
                    out state);

            if (resolved == null)
                return null;

            IAnnotation annotation =
                resolved as IAnnotation;

            if (annotation != null)
                return annotation;

            INote note =
                resolved as INote;

            if (note != null)
                return note.GetAnnotation() as IAnnotation;

            return null;
        }

        // Snapshots every balloon's current sheet-space position (see
        // IAnnotation.GetPosition) into the saved .binspection.json data
        // file, resolving each characteristic's own BalloonPersistId
        // directly rather than walking every annotation on every sheet -
        // same approach as BalloonManager.RemoveBalloonsByPersistId. Paired
        // with OnRestorePosition, which puts a balloon back at whatever was
        // captured here. Use this before manually dragging balloons around
        // to fit a print layout, so they can be put back later.
        public void OnSavePosition()
        {
            try
            {
                ModelDoc2 model =
                    (ModelDoc2)_swApp.ActiveDoc;

                if (model == null)
                {
                    _swApp.SendMsgToUser2(
                        "No document is open.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                if (model.GetType() !=
                    (int)swDocumentTypes_e.swDocDRAWING)
                {
                    _swApp.SendMsgToUser2(
                        "Open a drawing first.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                string dataFilePath =
                    PersistenceManager.GetDataFilePath(model);

                if (string.IsNullOrEmpty(dataFilePath))
                {
                    _swApp.SendMsgToUser2(
                        "This drawing hasn't been saved yet, so there's nowhere to save balloon positions.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                List<Characteristic> persisted =
                    PersistenceManager.Load(dataFilePath);

                int savedCount = 0;
                int missingCount = 0;

                foreach (Characteristic characteristic in persisted)
                {
                    if (string.IsNullOrEmpty(characteristic.BalloonPersistId))
                        continue;

                    IAnnotation annotation =
                        ResolveBalloonAnnotation(model, characteristic.BalloonPersistId);

                    if (annotation == null)
                    {
                        missingCount++;
                        continue;
                    }

                    double[] pos =
                        annotation.GetPosition() as double[];

                    if (pos == null || pos.Length < 3)
                    {
                        missingCount++;
                        continue;
                    }

                    characteristic.BalloonPositionX = pos[0];
                    characteristic.BalloonPositionY = pos[1];
                    characteristic.BalloonPositionZ = pos[2];

                    savedCount++;
                }

                PersistenceManager.Save(dataFilePath, persisted);

                CharacteristicManager.Clear();
                CharacteristicManager.LoadFrom(persisted);

                string message =
                    "Saved position for " + savedCount + " balloon(s).";

                if (missingCount > 0)
                {
                    message += " " + missingCount +
                        " balloon(s) could not be found on the drawing and were skipped.";
                }

                _swApp.SendMsgToUser2(
                    message,
                    (int)swMessageBoxIcon_e.swMbInformation,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
            catch (Exception ex)
            {
                _swApp.SendMsgToUser2(
                    ex.ToString(),
                    (int)swMessageBoxIcon_e.swMbStop,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
        }

        // Puts every balloon back at the sheet-space position last
        // captured by OnSavePosition. Balloons with no saved position
        // (Save Position has never been run for them) are left untouched.
        public void OnRestorePosition()
        {
            try
            {
                ModelDoc2 model =
                    (ModelDoc2)_swApp.ActiveDoc;

                if (model == null)
                {
                    _swApp.SendMsgToUser2(
                        "No document is open.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                if (model.GetType() !=
                    (int)swDocumentTypes_e.swDocDRAWING)
                {
                    _swApp.SendMsgToUser2(
                        "Open a drawing first.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                string dataFilePath =
                    PersistenceManager.GetDataFilePath(model);

                if (string.IsNullOrEmpty(dataFilePath))
                {
                    _swApp.SendMsgToUser2(
                        "This drawing hasn't been saved yet, so there's no saved balloon position data.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                List<Characteristic> persisted =
                    PersistenceManager.Load(dataFilePath);

                int restoredCount = 0;
                int missingCount = 0;
                int unsavedCount = 0;

                foreach (Characteristic characteristic in persisted)
                {
                    if (string.IsNullOrEmpty(characteristic.BalloonPersistId))
                        continue;

                    if (!characteristic.BalloonPositionX.HasValue ||
                        !characteristic.BalloonPositionY.HasValue ||
                        !characteristic.BalloonPositionZ.HasValue)
                    {
                        unsavedCount++;
                        continue;
                    }

                    IAnnotation annotation =
                        ResolveBalloonAnnotation(model, characteristic.BalloonPersistId);

                    if (annotation == null)
                    {
                        missingCount++;
                        continue;
                    }

                    annotation.SetPosition(
                        characteristic.BalloonPositionX.Value,
                        characteristic.BalloonPositionY.Value,
                        characteristic.BalloonPositionZ.Value);

                    restoredCount++;
                }

                string message;

                if (restoredCount == 0 && missingCount == 0 && unsavedCount == 0)
                {
                    message = "No balloons were found to restore.";
                }
                else if (restoredCount == 0 && unsavedCount > 0 && missingCount == 0)
                {
                    message = "No saved balloon positions were found. Run Save Position first.";
                }
                else
                {
                    message = "Restored position for " + restoredCount + " balloon(s).";

                    if (missingCount > 0)
                    {
                        message += " " + missingCount +
                            " balloon(s) could not be found on the drawing and were skipped.";
                    }

                    if (unsavedCount > 0)
                    {
                        message += " " + unsavedCount +
                            " balloon(s) have no saved position and were left unchanged.";
                    }
                }

                _swApp.SendMsgToUser2(
                    message,
                    (int)swMessageBoxIcon_e.swMbInformation,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
            catch (Exception ex)
            {
                _swApp.SendMsgToUser2(
                    ex.ToString(),
                    (int)swMessageBoxIcon_e.swMbStop,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
        }

        // Lets the user define/assign "Sheet Tolerance" sets (grouped
        // decimal-place and angular tolerance values) per sheet - see
        // UI/SheetToleranceSelectionWindow. Which sheets get populated with
        // balloons is chosen separately, in the Create Balloons picker, not
        // here. This is project/sheet-level setup data, saved separately
        // from individual balloon Characteristics in the same sidecar file
        // (see Models/ProjectData.cs). Can be run any time, whether to set this
        // up for the first time or to change it later.
        public void OnSheetToleranceSelection()
        {
            try
            {
                ModelDoc2 model =
                    (ModelDoc2)_swApp.ActiveDoc;

                if (model == null)
                {
                    _swApp.SendMsgToUser2(
                        "No document is open.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                if (model.GetType() !=
                    (int)swDocumentTypes_e.swDocDRAWING)
                {
                    _swApp.SendMsgToUser2(
                        "Open a drawing first.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                DrawingDoc drawing =
                    model as DrawingDoc;

                if (drawing == null)
                {
                    _swApp.SendMsgToUser2(
                        "Unable to access drawing.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                string dataFilePath =
                    PersistenceManager.GetDataFilePath(model);

                if (string.IsNullOrEmpty(dataFilePath))
                {
                    _swApp.SendMsgToUser2(
                        "This drawing hasn't been saved yet, so sheet/tolerance setup can't be saved until you save it.",
                        (int)swMessageBoxIcon_e.swMbStop,
                        (int)swMessageBoxBtn_e.swMbOk);

                    return;
                }

                ProjectData result =
                    OpenSheetToleranceSelection(model, drawing, dataFilePath);

                if (result == null)
                    return;

                _swApp.SendMsgToUser2(
                    "Saved sheet tolerance setup: " + result.ToleranceSets.Count +
                        " tolerance set(s) defined.",
                    (int)swMessageBoxIcon_e.swMbInformation,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
            catch (Exception ex)
            {
                _swApp.SendMsgToUser2(
                    ex.ToString(),
                    (int)swMessageBoxIcon_e.swMbStop,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
        }

        // Used by OnSheetToleranceSelection. Returns the saved ProjectData,
        // or null if there were no sheets to configure or the user
        // cancelled the window.
        private ProjectData OpenSheetToleranceSelection(ModelDoc2 model, DrawingDoc drawing, string dataFilePath)
        {
            List<string> sheetNames =
                DrawingSheetHelper.GetSheetNames(drawing);

            if (sheetNames.Count == 0)
            {
                _swApp.SendMsgToUser2(
                    "No sheets were found in this drawing.",
                    (int)swMessageBoxIcon_e.swMbStop,
                    (int)swMessageBoxBtn_e.swMbOk);

                return null;
            }

            ProjectData existing =
                PersistenceManager.LoadProject(dataFilePath);

            BINSPECTION.UI.SheetToleranceSelectionWindow window =
                new BINSPECTION.UI.SheetToleranceSelectionWindow(sheetNames, existing);

            bool? accepted = window.ShowDialog();

            if (accepted != true || window.Result == null)
                return null;

            PersistenceManager.SaveProject(dataFilePath, window.Result);

            return window.Result;
        }

        // A surface finish symbol's actual displayed text (roughness value,
        // process note, etc. - whatever ISFSymbol.GetTextAtIndex holds),
        // joined into one label the same way GtolTextFormatter builds a
        // GD&T frame's plain-English DimensionName. Falls back to a generic
        // descriptor only if the symbol has no readable text at all, rather
        // than always using that descriptor as before.
        private static string BuildSurfaceFinishText(ISFSymbol surfaceFinish)
        {
            try
            {
                List<string> parts = new List<string>();

                int textCount = surfaceFinish.GetTextCount();

                for (int i = 0; i < textCount; i++)
                {
                    string text = surfaceFinish.GetTextAtIndex(i);

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        parts.Add(text.Trim());
                    }
                }

                string combined =
                    string.Join(" ", parts);

                return string.IsNullOrWhiteSpace(combined)
                    ? "Surface Finish Symbol"
                    : combined;
            }
            catch
            {
                return "Surface Finish Symbol";
            }
        }
    }
}