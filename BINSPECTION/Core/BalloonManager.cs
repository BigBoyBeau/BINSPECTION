using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace BINSPECTION.Core
{

    public class BalloonManager
    {
        // Shared with ReconciliationService.FindOrphanedBalloons, so both
        // "does this exact number have a balloon" and "what number does
        // this balloon-shaped note have" agree on what a balloon looks
        // like. Matches "12" and grouped balloons like "12.2" - group 1
        // captures the whole display string, not just an integer. No
        // surrounding parentheses - balloons show the bare number.
        public static readonly Regex BalloonTextPattern =
            new Regex(@"^(\d+(?:\.\d+)?)$");

        // Takes IDisplayDimension (the interface) rather than DisplayDimension
        // (the coclass) so this can also accept a dimension resolved from a
        // SolidWorks persistent reference (see ReconciliationService.cs),
        // where casting the resolved object to the coclass type fails even
        // though it genuinely is a dimension - the interface cast is the
        // reliable one for objects that come back through a generic
        // resolver API instead of a normal typed accessor. Any
        // DisplayDimension can still be passed here unchanged, since the
        // coclass implements this interface.
        public Note CreateBalloon(
            ModelDoc2 model,
            IDisplayDimension displayDim,
            int characteristicNumber,
            string sheetName = null,
            View sourceView = null,
            List<DrawingSheetHelper.ViewOnSheet> viewsBySheet = null,
            Dictionary<string, Note> existingBalloonIndex = null)
        {
            return CreateBalloon(model, displayDim, characteristicNumber.ToString(), sheetName, sourceView, viewsBySheet, existingBalloonIndex);
        }

        // Same as above, but takes the display string directly so a
        // grouped balloon ("12.2") can be created without a fake int
        // representation.
        public Note CreateBalloon(
            ModelDoc2 model,
            IDisplayDimension displayDim,
            string displayNumber,
            string sheetName = null,
            View sourceView = null,
            List<DrawingSheetHelper.ViewOnSheet> viewsBySheet = null,
            Dictionary<string, Note> existingBalloonIndex = null)
        {
            return CreateBalloon(model, (object)displayDim, displayNumber, sheetName, sourceView, viewsBySheet, existingBalloonIndex);
        }

        // Balloons a GD&T feature control frame - same placement/styling as
        // a dimension balloon, just anchored to the frame's own position
        // instead of a dimension's.
        public Note CreateBalloon(
            ModelDoc2 model,
            IGtol gtol,
            string displayNumber,
            string sheetName = null,
            View sourceView = null,
            List<DrawingSheetHelper.ViewOnSheet> viewsBySheet = null,
            Dictionary<string, Note> existingBalloonIndex = null)
        {
            return CreateBalloon(model, (object)gtol, displayNumber, sheetName, sourceView, viewsBySheet, existingBalloonIndex);
        }

        // Balloons a free-standing Note (a callout with a leader to real
        // geometry - see CommandManagerHandler.OnCreateBalloons for the
        // eligibility check) the same way as a dimension or GD&T frame.
        public Note CreateBalloon(
            ModelDoc2 model,
            INote sourceNote,
            string displayNumber,
            string sheetName = null,
            View sourceView = null,
            List<DrawingSheetHelper.ViewOnSheet> viewsBySheet = null,
            Dictionary<string, Note> existingBalloonIndex = null)
        {
            return CreateBalloon(model, (object)sourceNote, displayNumber, sheetName, sourceView, viewsBySheet, existingBalloonIndex);
        }

        // Balloons a surface finish symbol - same placement/styling as a
        // dimension or GD&T balloon, anchored to the symbol's own position.
        public Note CreateBalloon(
            ModelDoc2 model,
            ISFSymbol surfaceFinish,
            string displayNumber,
            string sheetName = null,
            View sourceView = null,
            List<DrawingSheetHelper.ViewOnSheet> viewsBySheet = null,
            Dictionary<string, Note> existingBalloonIndex = null)
        {
            return CreateBalloon(model, (object)surfaceFinish, displayNumber, sheetName, sourceView, viewsBySheet, existingBalloonIndex);
        }

        // Common implementation shared by every CreateBalloon overload
        // above (and by ReconciliationService.RecreateMissingBalloons,
        // which only has the resolved source as a bare `object` to begin
        // with). annotationSource must be an IDisplayDimension, IGtol,
        // INote, or ISFSymbol - anything else returns null. All four expose
        // the same GetAnnotation() shape, just not through a shared
        // SolidWorks interface, so the cast has to be tried one type at a
        // time.
        //
        // sheetName - the sheet the source annotation actually lives on
        // (every call site already knows this, from a DimensionHit/GtolHit/
        // etc.'s View or a saved Characteristic.SheetName). Passing it is
        // what fixes balloons bleeding onto the wrong sheet: InsertNote
        // below always creates on whichever sheet SolidWorks currently has
        // active, with NO parameter of its own to say otherwise, so unless
        // the correct sheet is activated first, a balloon for a dimension
        // on Sheet 1 ends up physically created on Sheet 4 if that's what
        // the user (or a previous command) happened to leave active -
        // right position, wrong sheet, which reads as "balloons from other
        // sheets overlapping this one." Null/unresolvable sheetName just
        // skips activation (old behavior) rather than failing the balloon.
        // Set on every null return from CreateBalloon below (cleared to
        // null at the start of each call) - Debug.WriteLine alone is
        // invisible in a normal installed-addin session (no attached
        // debugger), which is exactly what made balloon creation look
        // "silently" dead on sheets after the first one (see
        // [[binspection_create_balloons_multi_sheet_silent_abort]]).
        // CommandManagerHandler.OnCreateBalloons reads this immediately
        // after a null-returning call so it can surface WHICH of the
        // several possible reasons actually fired, in the final result
        // message, without needing a debugger attached to SolidWorks.
        public string LastFailureReason { get; private set; }

        public Note CreateBalloon(
            ModelDoc2 model,
            object annotationSource,
            string displayNumber,
            string sheetName = null,
            View sourceView = null,
            List<DrawingSheetHelper.ViewOnSheet> viewsBySheet = null,
            Dictionary<string, Note> existingBalloonIndex = null)
        {
            LastFailureReason = null;

            if (model == null || annotationSource == null)
                return Fail("CreateBalloon", "model or annotation source was null", model, null);

            DrawingDoc drawing = model as DrawingDoc;

            if (drawing == null)
                return Fail("CreateBalloon", "the document is not a drawing", model, null);

            // Set once InsertNote succeeds. Any failure AFTER that point
            // (GetAnnotation, placement, layer/styling, or an exception)
            // deletes it again via Fail's rollback - otherwise a half-built
            // note is left on the drawing with no Characteristic pointing at
            // it, which the next run can mistake for "an existing balloon"
            // with that number.
            Note note = null;

            try
            {
                if (!string.IsNullOrEmpty(sheetName) && !DrawingSheetHelper.ActivateSheet(drawing, sheetName))
                {
                    BinspectionLog.Warn("CreateBalloon",
                        "could not activate sheet '" + sheetName + "' for balloon \"" + displayNumber +
                        "\" - it will be created on whichever sheet is currently active");
                }

                // Prevent duplicate characteristic numbers. existingBalloonIndex
                // (optional): a lookup BuildExistingBalloonIndex already built
                // once for the whole run, so a caller placing MANY balloons in
                // one pass (OnCreateBalloons, ReconciliationService.
                // RecreateMissingBalloons) gets an O(1) check per balloon
                // instead of FindExistingBalloon's full walk of every
                // annotation on every sheet, EVERY TIME - which is what made a
                // large Create Balloons run's total duplicate-checking cost
                // grow with the SQUARE of how many balloons it placed, slow
                // enough on a big drawing to look like the add-in had hung.
                //
                // Inside the try (it used to run before it): the per-call
                // walk makes a lot of COM calls, and an exception from it
                // escaped CreateBalloon entirely instead of coming back as a
                // null + LastFailureReason like every other failure.
                Note existing;

                if (existingBalloonIndex != null)
                {
                    existingBalloonIndex.TryGetValue(displayNumber, out existing);
                }
                else
                {
                    existing = FindExistingBalloon(model, displayNumber, viewsBySheet);
                }

                if (existing != null)
                {
                    // Not a failure, but it IS drawing/JSON drift worth
                    // seeing - the caller is about to treat this pre-existing
                    // balloon as the one it just asked for.
                    BinspectionLog.Warn("CreateBalloon",
                        "balloon \"" + displayNumber + "\" already exists on the drawing - reused it instead of creating a new one");

                    return existing;
                }

                Annotation sourceAnnotation =
                    GetSourceAnnotation(annotationSource);

                if (sourceAnnotation == null)
                {
                    return Fail("CreateBalloon",
                        "GetSourceAnnotation returned null (unrecognized annotation type or GetAnnotation() failed)",
                        model, null);
                }

                double[] pos =
                    (double[])sourceAnnotation.GetPosition();

                if (pos == null || pos.Length < 3)
                {
                    return Fail("CreateBalloon",
                        "source annotation GetPosition() returned null/invalid on sheet '" + sheetName + "'",
                        model, null);
                }

                // Selecting the source's own placed view before InsertNote
                // is what makes the new Note attach to THAT view instead of
                // landing as a floating, sheet-level annotation (SolidWorks
                // has no "which view" parameter on InsertNote itself - it
                // attaches to whatever is selected at call time, the same
                // way ActivateSheet above stands in for a "which sheet"
                // parameter InsertNote also lacks). Best-effort: a null
                // sourceView (caller didn't have one) or a failed select
                // just falls back to the old floating-note behavior rather
                // than failing the balloon. Always clears the selection
                // first, even with no view - see SelectViewForAttachment.
                SelectViewForAttachment(model, sourceView);

                note =
                    (Note)model.InsertNote(displayNumber);

                ClearSelection(model);

                if (note == null)
                {
                    return Fail("CreateBalloon",
                        "InsertNote(\"" + displayNumber + "\") returned null on sheet '" + sheetName + "'",
                        model, null);
                }

                Annotation balloonAnnotation =
                    (Annotation)note.GetAnnotation();

                if (balloonAnnotation == null)
                    return Fail("CreateBalloon", "new note's GetAnnotation() returned null", model, note);

                double[] placedPos =
                    BalloonPlacementService.ComputePosition(
                        annotationSource, sourceAnnotation, pos, displayNumber);

                if (placedPos == null || placedPos.Length < 3)
                    return Fail("CreateBalloon", "BalloonPlacementService returned no position", model, note);

                balloonAnnotation.SetPosition(
                    placedPos[0],
                    placedPos[1],
                    placedPos[2]);

                // The Binspection layer is what makes this note findable as
                // a balloon at all (see IsBinspectionBalloon) - a note that
                // didn't land on it would be invisible to every later
                // find/delete, so it's rolled back rather than kept.
                if (!ApplyBalloonStyle(model, note, balloonAnnotation))
                {
                    return Fail("CreateBalloon",
                        "could not place balloon \"" + displayNumber + "\" on the '" + BalloonLayerName + "' layer",
                        model, note);
                }

                // Keep the shared index in sync as balloons are created, so
                // the NEXT item in this same run sees this one too - not
                // just whatever already existed before the run started.
                if (existingBalloonIndex != null)
                    existingBalloonIndex[displayNumber] = note;

                return note;
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("CreateBalloon(\"" + displayNumber + "\", sheet '" + sheetName + "')", ex);

                return Fail("CreateBalloon", ex.GetType().Name + ": " + ex.Message, model, note);
            }
        }

        // Records a failure for the caller (LastFailureReason), writes it to
        // the persistent log, and - if a note was already inserted before
        // the failure - deletes that note again so a failed call never
        // leaves an orphaned, half-styled note behind. Always returns null
        // so call sites can `return Fail(...)` directly.
        private Note Fail(string context, string reason, ModelDoc2 model, Note createdNote)
        {
            if (createdNote != null)
            {
                bool rolledBack = false;

                try
                {
                    rolledBack = DeleteAnnotation(model, createdNote.GetAnnotation() as Annotation, context + " rollback");
                }
                catch (Exception ex)
                {
                    BinspectionLog.Error(context + " rollback", ex);
                }

                if (!rolledBack)
                    reason += " (and the partially-created note could NOT be removed - check the drawing for a stray note)";
            }

            LastFailureReason = reason;

            BinspectionLog.Warn(context, reason);

            return null;
        }

        // Moves an already-placed balloon note from whichever sheet it's
        // currently on to a different target sheet - a manual recovery
        // action for when Create Balloons (or the multi-sheet placement bug
        // it used to have - see
        // [[binspection_balloon_wrong_sheet_placement_bug]]) puts a
        // balloon's note on the wrong sheet. Preserves the note's own
        // sheet-space X/Y/Z position exactly rather than re-deriving a
        // position from the source dimension/annotation - most BINSPECTION
        // sheets share the same paper-space template layout, so the same
        // coordinates read sensibly on the target sheet too, and a move
        // should land where the user expects (roughly where it already
        // was), not jump to some new default offset position.
        //
        // The new note is created (and styled/layered the same as any other
        // balloon - see ApplyBalloonStyle) BEFORE the old one is deleted, so
        // a failure partway through never leaves the balloon completely
        // gone - only once the new note exists is the old one removed. If
        // the old note then can't be deleted, the NEW one is rolled back
        // instead and the move reports failure - it used to log that to
        // Debug only and return success, leaving two balloons with the same
        // number on two different sheets.
        // Returns the new Note, or null (with LastFailureReason set) on
        // failure.
        public Note MoveBalloon(ModelDoc2 model, string displayNumber, string targetSheetName)
        {
            LastFailureReason = null;

            DrawingDoc drawing = model as DrawingDoc;

            if (drawing == null || string.IsNullOrEmpty(targetSheetName))
                return Fail("MoveBalloon", "model was not a drawing, or target sheet name was null", model, null);

            Note newNote = null;

            try
            {
                string sourceSheetName;
                int matchCount;

                Note existingNote =
                    FindBalloon(drawing, displayNumber, null, out sourceSheetName, out matchCount);

                if (existingNote == null)
                {
                    return Fail("MoveBalloon",
                        "could not find an existing balloon numbered \"" + displayNumber + "\"", model, null);
                }

                if (matchCount > 1)
                {
                    BinspectionLog.Warn("MoveBalloon",
                        matchCount + " balloons are numbered \"" + displayNumber + "\" - moving the one on sheet '" +
                        sourceSheetName + "'");
                }

                Annotation existingAnnotation =
                    (Annotation)existingNote.GetAnnotation();

                if (existingAnnotation == null)
                    return Fail("MoveBalloon", "existing balloon's GetAnnotation() returned null", model, null);

                double[] pos =
                    (double[])existingAnnotation.GetPosition();

                if (!DrawingSheetHelper.ActivateSheet(drawing, targetSheetName))
                    return Fail("MoveBalloon", "could not activate sheet '" + targetSheetName + "'", model, null);

                ClearSelection(model);

                newNote =
                    (Note)model.InsertNote(displayNumber);

                ClearSelection(model);

                if (newNote == null)
                {
                    return Fail("MoveBalloon",
                        "InsertNote(\"" + displayNumber + "\") returned null on sheet '" + targetSheetName + "'",
                        model, null);
                }

                Annotation newAnnotation =
                    (Annotation)newNote.GetAnnotation();

                if (newAnnotation == null)
                    return Fail("MoveBalloon", "new note's GetAnnotation() returned null", model, newNote);

                if (pos != null && pos.Length >= 3)
                    newAnnotation.SetPosition(pos[0], pos[1], pos[2]);

                if (!ApplyBalloonStyle(model, newNote, newAnnotation))
                {
                    return Fail("MoveBalloon",
                        "could not place the moved balloon on the '" + BalloonLayerName + "' layer", model, newNote);
                }

                // Delete the old note with its own sheet active - the same
                // "only trust the active sheet" rule every other sheet-
                // sensitive call in this add-in follows.
                if (!string.IsNullOrEmpty(sourceSheetName))
                    DrawingSheetHelper.ActivateSheet(drawing, sourceSheetName);

                bool oldDeleted = DeleteAnnotation(model, existingAnnotation, "MoveBalloon (old note)");

                DrawingSheetHelper.ActivateSheet(drawing, targetSheetName);

                if (!oldDeleted)
                {
                    return Fail("MoveBalloon",
                        "the original balloon on sheet '" + sourceSheetName + "' could not be deleted, so the move was cancelled",
                        model, newNote);
                }

                return newNote;
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("MoveBalloon(\"" + displayNumber + "\" -> '" + targetSheetName + "')", ex);

                return Fail("MoveBalloon", ex.GetType().Name + ": " + ex.Message, model, newNote);
            }
        }

        // Selects sourceView by name so the immediately-following InsertNote
        // attaches its new Note to that view (SelectByID2 + "DRAWINGVIEW" is
        // the standard SolidWorks API technique for selecting a placed
        // drawing view by name - there's no InsertNote overload that takes a
        // view directly). ALWAYS clears any prior selection first - even
        // with no view to select - since InsertNote attaches its new note to
        // whatever is selected at call time: a dimension/edge the user had
        // selected (every Balloon Manager path passes no view) would
        // otherwise get the balloon attached to IT. Select failures (view
        // already deleted, name lookup throws, etc.) are logged, not fatal -
        // the balloon still gets created, just as a floating sheet-level
        // note like before this feature existed.
        private static void SelectViewForAttachment(ModelDoc2 model, View sourceView)
        {
            ClearSelection(model);

            if (sourceView == null)
                return;

            try
            {
                string viewName = sourceView.GetName2();

                if (string.IsNullOrEmpty(viewName))
                    return;

                if (!model.Extension.SelectByID2(
                    viewName, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0))
                {
                    BinspectionLog.Warn("SelectViewForAttachment",
                        "could not select view '" + viewName + "' - balloon will be a floating sheet-level note");
                }
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("SelectViewForAttachment", ex);
            }
        }

        private static void ClearSelection(ModelDoc2 model)
        {
            try
            {
                model?.ClearSelection2(true);
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("ClearSelection", ex);
            }
        }

        private static Annotation GetSourceAnnotation(object annotationSource)
        {
            IDisplayDimension dimension = annotationSource as IDisplayDimension;

            if (dimension != null)
                return dimension.GetAnnotation() as Annotation;

            IGtol gtol = annotationSource as IGtol;

            if (gtol != null)
                return gtol.GetAnnotation() as Annotation;

            INote note = annotationSource as INote;

            if (note != null)
                return note.GetAnnotation() as Annotation;

            ISFSymbol surfaceFinish = annotationSource as ISFSymbol;

            if (surfaceFinish != null)
                return surfaceFinish.GetAnnotation() as Annotation;

            return null;
        }

        // Every BINSPECTION balloon lives on its own drawing layer, so
        // they can be hidden/frozen/printed as a group independently of
        // whatever layer the underlying dimension/annotation is on -
        // created on first use (per drawing) with the same red used on
        // the balloon itself, so a layer-colored view of the sheet still
        // reads as "these are the inspection balloons." Public so other
        // code can tell a balloon apart from a plain note the same way -
        // both are Note annotations, so the layer is the only reliable
        // signal.
        public const string BalloonLayerName = "Binspection";

        // The ONE definition of "this annotation is a BINSPECTION balloon",
        // shared by every find/index/orphan-scan (FindExistingBalloon,
        // BuildExistingBalloonIndex, ReconciliationService.FindOrphanedBalloons).
        // Those used to match on note TEXT alone, while every delete matched
        // on LAYER alone - so a user's own plain note reading "12" (or a
        // SOLIDWORKS Inspection add-in balloon) counted as "balloon 12
        // already exists" and silently blocked the real one from being
        // created, and Reconcile would even offer to delete it as an
        // "orphan". Requires: not title-block/sheet-format content, a Note,
        // on the Binspection layer, and text matching BalloonTextPattern.
        // displayNumber is the matched number ("12" / "12.2").
        public static bool IsBinspectionBalloon(Annotation annotation, out Note note, out string displayNumber)
        {
            note = null;
            displayNumber = null;

            if (annotation == null)
                return false;

            if (AnnotationScanner.IsOwnedBySheetFormat(annotation))
                return false;

            if (!IsOnBalloonLayer(annotation))
                return false;

            Note candidate =
                annotation.GetSpecificAnnotation() as Note;

            if (candidate == null)
                return false;

            // Extract the number via the regex rather than comparing the
            // whole string - more forgiving of incidental formatting
            // differences, and normalizes away any formatting markup
            // SolidWorks may have embedded in the text after a save/reopen
            // cycle (see NormalizeNoteText).
            Match match =
                BalloonTextPattern.Match(NormalizeNoteText(candidate.GetText()));

            if (!match.Success)
                return false;

            note = candidate;
            displayNumber = match.Groups[1].Value;

            return true;
        }

        // Layer-only half of IsBinspectionBalloon - used on its own by the
        // bulk deletes (DeleteAllBalloons), which deliberately also remove a
        // balloon whose text was hand-edited into something that no longer
        // looks like a number.
        public static bool IsOnBalloonLayer(IAnnotation annotation)
        {
            return annotation != null &&
                string.Equals(annotation.Layer, BalloonLayerName, StringComparison.OrdinalIgnoreCase);
        }

        // Gets (or creates) the shared "Binspection" layer on this
        // drawing, keeping its color in sync with the balloon color below.
        // ILayerMgr comes off IModelDoc2 (not IDrawingDoc/IModelDocExtension -
        // confirmed via reflection on the installed interop assembly), and
        // has no "does this layer exist" check of its own - IGetLayer
        // returning null is that check. AddLayer's style/width parameters
        // are left at 0 (SolidWorks' own "default") since only the color
        // matters here.
        private static void EnsureBalloonLayer(ModelDoc2 model, int colorRef)
        {
            try
            {
                ILayerMgr layerMgr =
                    ((IModelDoc2)model).IGetLayerManager();

                if (layerMgr == null)
                {
                    BinspectionLog.Warn("EnsureBalloonLayer", "IGetLayerManager returned null");
                    return;
                }

                Layer layer =
                    layerMgr.IGetLayer(BalloonLayerName);

                if (layer == null)
                {
                    layerMgr.AddLayer(
                        BalloonLayerName,
                        "Binspection",
                        colorRef,
                        0,
                        0);

                    if (layerMgr.IGetLayer(BalloonLayerName) == null)
                    {
                        BinspectionLog.Warn("EnsureBalloonLayer", "AddLayer('" + BalloonLayerName + "') failed");
                    }
                }
                else if (layer.Color != colorRef)
                {
                    layer.Color = colorRef;
                }
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("EnsureBalloonLayer", ex);
            }
        }

        // A plain circle sized to just fit the balloon's own text, in
        // red - IAnnotation.Color takes a COLORREF (0x00BBGGRR), not
        // ARGB, so ColorTranslator.ToWin32 (which produces exactly that
        // layout) is used rather than Color.ToArgb(). There's no separate
        // leader-line color on INote/IAnnotation - this also colors the
        // leader, which is the SolidWorks default behavior for a note's
        // single Color property. Every balloon is also placed on the
        // shared "Binspection" layer (see EnsureBalloonLayer), created
        // with the same red color if it doesn't already exist.
        //
        // Each step has its own try, so a failed SetBalloon (cosmetic) can
        // no longer skip the layer assignment (essential). Returns whether
        // the note actually ended up on the Binspection layer - the one
        // step callers must treat as fatal, since IsBinspectionBalloon
        // can't find a balloon that isn't on it.
        private static bool ApplyBalloonStyle(ModelDoc2 model, Note note, Annotation annotation)
        {
            int red =
                ColorTranslator.ToWin32(Color.Red);

            try
            {
                note.SetBalloon(
                    (int)swBalloonStyle_e.swBS_Inspection,
                    (int)swBalloonFit_e.swBF_Tightest);
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("ApplyBalloonStyle: SetBalloon", ex);
            }

            try
            {
                annotation.Color = red;
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("ApplyBalloonStyle: Color", ex);
            }

            EnsureBalloonLayer(model, red);

            try
            {
                annotation.Layer = BalloonLayerName;

                return IsOnBalloonLayer(annotation);
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("ApplyBalloonStyle: Layer", ex);

                return false;
            }
        }

        public Note FindExistingBalloon(
            ModelDoc2 model,
            int characteristicNumber)
        {
            return FindExistingBalloon(model, characteristicNumber.ToString());
        }

        // Walks every VIEW on every sheet (AnnotationScanner.WalkAllAnnotations)
        // rather than the document-level IModelDoc2::GetFirstAnnotation2 this
        // used before - GetFirstAnnotation2 only sees annotations on
        // whichever sheet SolidWorks itself currently has active, which is
        // independent of whatever sheet is selected in Balloon Manager's own
        // dropdown. Acting on a row from a non-active sheet would silently
        // find nothing here (a swallowed no-op two/three levels up, e.g.
        // Un-Number's caller never checks DeleteBalloon's bool result),
        // leaving the JSON record "gone" while the balloon note stayed on
        // the drawing. See AnnotationScanner's class remarks - the same gap
        // it fixed for the Create Balloons scan.
        // viewsBySheet (optional): a list already computed by
        // DrawingSheetHelper.GetAllViewsBySheet, so a caller checking many
        // display numbers in one run (CreateBalloon's own duplicate check,
        // called once per balloon placed; ReconciliationService.Reconcile,
        // called once per saved characteristic) can compute it ONCE and
        // share it, rather than every call re-activating every sheet in the
        // drawing all over again - same reasoning as
        // AnnotationScanner.WalkAllAnnotations' matching parameter.
        public Note FindExistingBalloon(
            ModelDoc2 model,
            string displayNumber,
            List<DrawingSheetHelper.ViewOnSheet> viewsBySheet = null)
        {
            DrawingDoc drawing = model as DrawingDoc;

            if (drawing == null)
                return null;

            string sheetName;
            int matchCount;

            Note found = FindBalloon(drawing, displayNumber, viewsBySheet, out sheetName, out matchCount);

            if (matchCount > 1)
            {
                BinspectionLog.Warn("FindExistingBalloon",
                    matchCount + " separate balloons are numbered \"" + displayNumber +
                    "\" - using the first one found (sheet '" + sheetName + "')");
            }

            return found;
        }

        // FindExistingBalloon's walk, also reporting which sheet the first
        // match was found on and how many DISTINCT balloons carry that
        // number (a duplicate number on the drawing is a data-integrity
        // problem the caller may want to report). A sheet-level note is
        // visited once per view on its sheet, so matches are de-duplicated
        // by sheet + position rather than just counted.
        private static Note FindBalloon(
            DrawingDoc drawing,
            string displayNumber,
            List<DrawingSheetHelper.ViewOnSheet> viewsBySheet,
            out string foundSheetName,
            out int matchCount)
        {
            Note found = null;
            string foundSheet = null;
            HashSet<string> distinctMatches = new HashSet<string>();

            AnnotationScanner.WalkAllAnnotations(drawing, (annotation, view, sheetName) =>
            {
                Note note;
                string number;

                if (!IsBinspectionBalloon(annotation, out note, out number) || number != displayNumber)
                    return;

                distinctMatches.Add(BalloonIdentityKey(annotation, sheetName));

                if (found == null)
                {
                    found = note;
                    foundSheet = sheetName;
                }
            }, viewsBySheet);

            foundSheetName = foundSheet;
            matchCount = distinctMatches.Count;

            return found;
        }

        // Same walk as FindExistingBalloon, but ONE pass that indexes every
        // balloon on the drawing by its display number, instead of a fresh
        // walk per number looked up. Build this ONCE per run and pass it as
        // CreateBalloon's existingBalloonIndex for any caller about to place
        // more than a handful of balloons in a row (OnCreateBalloons,
        // ReconciliationService.RecreateMissingBalloons) - see
        // CreateBalloon's remarks for why the per-call walk doesn't scale to
        // a large run. Two distinct balloons sharing one number used to be
        // silently collapsed to whichever was visited last; the first one
        // found is now kept and the duplicate is logged.
        public Dictionary<string, Note> BuildExistingBalloonIndex(
            DrawingDoc drawing,
            List<DrawingSheetHelper.ViewOnSheet> viewsBySheet = null)
        {
            Dictionary<string, Note> index =
                new Dictionary<string, Note>(StringComparer.OrdinalIgnoreCase);

            if (drawing == null)
                return index;

            Dictionary<string, string> identityByNumber =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            HashSet<string> duplicateNumbers = new HashSet<string>();

            AnnotationScanner.WalkAllAnnotations(drawing, (annotation, view, sheetName) =>
            {
                Note note;
                string number;

                if (!IsBinspectionBalloon(annotation, out note, out number))
                    return;

                string identity = BalloonIdentityKey(annotation, sheetName);
                string firstIdentity;

                if (!identityByNumber.TryGetValue(number, out firstIdentity))
                {
                    identityByNumber[number] = identity;
                    index[number] = note;
                }
                else if (firstIdentity != identity)
                {
                    duplicateNumbers.Add(number);
                }
            }, viewsBySheet);

            if (duplicateNumbers.Count > 0)
            {
                BinspectionLog.Warn("BuildExistingBalloonIndex",
                    "more than one balloon on the drawing carries each of these numbers: " +
                    string.Join(", ", duplicateNumbers));
            }

            return index;
        }

        // Identifies one physical balloon across repeated per-view visits of
        // the same sheet-level note: its sheet plus its rounded sheet-space
        // position. (COM wrapper identity isn't reliable for this - SolidWorks
        // can hand back a fresh wrapper for the same annotation.)
        private static string BalloonIdentityKey(Annotation annotation, string sheetName)
        {
            try
            {
                double[] pos = annotation.GetPosition() as double[];

                if (pos != null && pos.Length >= 2)
                    return sheetName + "|" + Math.Round(pos[0], 6) + "|" + Math.Round(pos[1], 6);
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("BalloonIdentityKey", ex);
            }

            return sheetName + "|?";
        }

        // SolidWorks can embed formatting markup (things like "<A:1>" or
        // font/style tags) directly into what Note.GetText() returns, and
        // this tends to show up specifically AFTER a document has been
        // saved and reopened - text read back immediately after
        // InsertNote() in the same live session is usually still clean.
        // Comparing raw GetText() output against a plain "(1)" then makes
        // an existing, correctly-numbered balloon look "missing" purely
        // because of formatting noise, which is what was causing Restore
        // Balloons to recreate balloons that were already there. Strip
        // anything in angle brackets and trim before comparing.
        public static string NormalizeNoteText(string rawText)
        {
            if (string.IsNullOrEmpty(rawText))
                return string.Empty;

            string stripped = Regex.Replace(rawText, "<[^>]*>", string.Empty);

            return stripped.Trim();
        }

        public bool DeleteBalloon(
            ModelDoc2 model,
            int characteristicNumber)
        {
            return DeleteBalloon(model, characteristicNumber.ToString());
        }

        public bool DeleteBalloon(
            ModelDoc2 model,
            string displayNumber)
        {
            try
            {
                Note note = FindExistingBalloon(model, displayNumber);

                if (note == null)
                {
                    BinspectionLog.Warn("DeleteBalloon",
                        "no balloon numbered \"" + displayNumber + "\" was found on the drawing - nothing deleted");
                    return false;
                }

                return DeleteBalloon(model, note);
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("DeleteBalloon(\"" + displayNumber + "\")", ex);
                return false;
            }
        }

        // Same as above for a caller that already resolved the Note (e.g.
        // from a BuildExistingBalloonIndex lookup), skipping the per-call
        // FindExistingBalloon walk. Returns true only if SolidWorks actually
        // reported the delete as done (it used to return true regardless).
        public bool DeleteBalloon(
            ModelDoc2 model,
            Note note)
        {
            try
            {
                if (model == null || note == null)
                    return false;

                return DeleteAnnotation(model, (Annotation)note.GetAnnotation(), "DeleteBalloon");
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("DeleteBalloon(note)", ex);
                return false;
            }
        }

        // The single select-then-delete used by every delete path in this
        // class, checking BOTH SolidWorks return values - Select3 (did the
        // annotation actually get selected?) and DeleteSelection2 (did the
        // delete actually happen?). Every caller used to ignore both and
        // report success anyway. Logs the reason on any failure.
        private static bool DeleteAnnotation(ModelDoc2 model, Annotation annotation, string context)
        {
            if (model == null || annotation == null)
            {
                BinspectionLog.Warn(context, "nothing to delete (model or annotation was null)");
                return false;
            }

            try
            {
                if (!annotation.Select3(false, null))
                {
                    BinspectionLog.Warn(context, "Select3 returned false - annotation could not be selected for deletion");
                    return false;
                }

                if (!model.Extension.DeleteSelection2(
                    (int)swDeleteSelectionOptions_e.swDelete_Absorbed))
                {
                    BinspectionLog.Warn(context, "DeleteSelection2 returned false - annotation was selected but not deleted");
                    ClearSelection(model);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                BinspectionLog.Error(context, ex);
                ClearSelection(model);
                return false;
            }
        }

        // Deletes every annotation on the shared "Binspection" layer (see
        // EnsureBalloonLayer/ApplyBalloonStyle - every balloon this add-in
        // creates is placed on that layer), regardless of what the saved
        // data file says - used by "Delete All Balloons" (a debugging tool)
        // to guarantee a clean slate even if the JSON and the drawing have
        // drifted out of sync. Selecting by layer instead of matching the
        // note's text against BalloonTextPattern means this can't miss
        // balloons whose text got edited/reformatted, and can't be tripped
        // up by non-Note annotations. Collects matches into a list before
        // deleting any of them, since deleting an annotation while still
        // walking the live annotation chain (annotation.GetNext3()) is not
        // safe. Returns how many balloons were ACTUALLY deleted.
        //
        // Walks every view on every sheet (AnnotationScanner.WalkAllAnnotations)
        // rather than the old document-level GetFirstAnnotation2 walk - see
        // FindExistingBalloon's remarks for why that missed anything not on
        // the currently-active sheet. A sheet-level balloon note can surface
        // more than once this way (once per view on its sheet) - those
        // repeat visits are collapsed by BalloonIdentityKey before deleting,
        // so the returned count is no longer inflated by them and a failed
        // second delete of an already-deleted note isn't logged as an error.
        public int DeleteAllBalloons(ModelDoc2 model)
        {
            DrawingDoc drawing = model as DrawingDoc;

            if (drawing == null)
                return 0;

            Dictionary<string, Annotation> balloons = new Dictionary<string, Annotation>();

            AnnotationScanner.WalkAllAnnotations(drawing, (annotation, view, sheetName) =>
            {
                if (!IsOnBalloonLayer(annotation))
                    return;

                string key = BalloonIdentityKey(annotation, sheetName);

                if (!balloons.ContainsKey(key))
                    balloons[key] = annotation;
            });

            return DeleteCollected(model, balloons.Values, "DeleteAllBalloons");
        }

        // Deletes each collected annotation individually (one bad item
        // never stops the rest) and logs one summary line if any failed.
        private static int DeleteCollected(ModelDoc2 model, ICollection<Annotation> annotations, string context)
        {
            int deletedCount = 0;

            foreach (Annotation ann in annotations)
            {
                if (DeleteAnnotation(model, ann, context))
                    deletedCount++;
            }

            if (deletedCount < annotations.Count)
            {
                BinspectionLog.Warn(context,
                    (annotations.Count - deletedCount) + " of " + annotations.Count +
                    " balloon(s) on the '" + BalloonLayerName + "' layer could not be deleted");
            }

            return deletedCount;
        }

        // Sheet-scoped variant used by Refresh Balloons (see
        // CommandManagerHandler.OnRefreshBalloons) so a refresh can be
        // limited to only the sheet(s) the user picked, leaving every
        // other sheet's balloons untouched. sheetNames == null means "every
        // sheet" - the original unscoped behavior every other caller
        // (OnRemoveBalloons, OnRefreshBalloons's own all-sheets case) still
        // gets via the overload above.
        //
        // Does NOT reuse AnnotationScanner.WalkAllAnnotations +
        // DrawingSheetHelper.GetSheetName(view) to decide which sheet a
        // balloon note belongs to - confirmed live (2026-09-15, diagnostic
        // counts added to OnRefreshBalloons: every persisted characteristic
        // came back "matched" and deleted/restored both read 0) that this
        // silently excludes every balloon note from the scoped delete. A
        // balloon Note created via InsertNote is a floating, sheet-level
        // annotation, not attached to a specific placed drawing view - the
        // "view" WalkAllAnnotations pairs it with is the sheet's OWN view
        // object (the first entry in IDrawingDoc.GetViews()' per-sheet
        // array), and IView.Sheet apparently does not resolve back to that
        // same sheet when called ON that object, unlike calling it on a
        // real placed view (which IS confirmed reliable - that's how
        // Characteristic.SheetName gets populated at Create Balloons time).
        //
        // Fixed by activating each chosen sheet in turn and using the
        // document-level IModelDoc2.GetFirstAnnotation2/GetNext3 walk
        // instead - this is exactly the walk AnnotationScanner moved away
        // from for whole-document scans (see its class remarks) because it
        // only ever sees annotations on whichever sheet is CURRENTLY
        // ACTIVE. That's a bug for a scan meant to cover every sheet at
        // once, but it's precisely the behavior wanted here: activate one
        // sheet, and everything this walk returns is guaranteed to belong
        // to that sheet, no per-annotation sheet lookup required at all.
        public int DeleteAllBalloons(ModelDoc2 model, HashSet<string> sheetNames)
        {
            DrawingDoc drawing = model as DrawingDoc;

            if (drawing == null)
                return 0;

            if (sheetNames == null)
                return DeleteAllBalloons(model);

            List<Annotation> balloons = new List<Annotation>();

            string originalSheetName = null;

            try
            {
                originalSheetName = drawing.IGetCurrentSheet()?.GetName();
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("DeleteAllBalloons(sheets): reading current sheet", ex);
            }

            try
            {
                foreach (string sheetName in sheetNames)
                {
                    if (!DrawingSheetHelper.ActivateSheet(drawing, sheetName))
                    {
                        BinspectionLog.Warn("DeleteAllBalloons(sheets)",
                            "could not activate sheet '" + sheetName + "' - its balloons were NOT deleted");
                        continue;
                    }

                    // Per-sheet guard: a chain that throws partway keeps
                    // what it already collected and moves to the next sheet.
                    try
                    {
                        Annotation annotation = model.IGetFirstAnnotation2();

                        while (annotation != null)
                        {
                            try
                            {
                                if (IsOnBalloonLayer(annotation))
                                    balloons.Add(annotation);
                            }
                            catch (Exception ex)
                            {
                                BinspectionLog.Error("DeleteAllBalloons(sheets): reading an annotation on sheet '" + sheetName + "'", ex);
                            }

                            annotation = annotation.GetNext3();
                        }
                    }
                    catch (Exception ex)
                    {
                        BinspectionLog.Error("DeleteAllBalloons(sheets): walking sheet '" + sheetName + "'", ex);
                    }
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(originalSheetName))
                    DrawingSheetHelper.ActivateSheet(drawing, originalSheetName);
            }

            return DeleteCollected(model, balloons, "DeleteAllBalloons(sheets)");
        }

        // Generic layer-cleanup helper: removes every note-type annotation
        // sitting on the given drawing layer and returns whether anything
        // was deleted. The one thing this build had to confirm rather than
        // assume: IModelDoc2 has no ".LayerMgr" property (correctly called
        // out as unverified) - the real accessor, already used by
        // EnsureBalloonLayer above, is IGetLayerManager().
        //
        // Note on annotation.GetType(): this is IAnnotation's own method
        // (returns swAnnotationType_e as an int), not System.Object's -
        // it only resolves to the SolidWorks one when the variable's
        // static type is the IAnnotation *interface* itself. Casting the
        // raw GetItems() element to the Annotation coclass instead (as
        // used elsewhere in this file for Select3/Layer) would silently
        // shadow it with System.Object.GetType() (returns System.Type, not
        // int) and fail to compile against swAnnotationType_e - so this
        // method deliberately keeps everything typed as IAnnotation.
        public bool RemoveNotesOnLayer(IModelDoc2 model, string layerName)
        {
            if (model == null || string.IsNullOrEmpty(layerName))
                return false;

            try
            {
                LayerMgr layerMgr =
                    model.IGetLayerManager();

                if (layerMgr == null)
                    return false;

                Layer layer =
                    layerMgr.GetLayer(layerName) as Layer;

                if (layer == null)
                    return false;

                object rawItems =
                    layer.GetItems(
                        (int)swLayerItemsOption_e.swLayerItemsOption_Annotations);

                object[] items = rawItems as object[];

                if (items == null || items.Length == 0)
                    return false;

                ISelectionMgr selectionMgr =
                    (ISelectionMgr)model.SelectionManager;

                int selectedCount = 0;

                foreach (object item in items)
                {
                    try
                    {
                        IAnnotation annotation =
                            item as IAnnotation;

                        if (annotation == null)
                        {
                            BinspectionLog.Warn("BalloonManager",
                                "RemoveNotesOnLayer: layer item was not an IAnnotation - actual type " +
                                (item?.GetType().FullName ?? "null"));

                            continue;
                        }

                        if (!string.Equals(
                            annotation.Layer,
                            layerName,
                            StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (annotation.GetType() !=
                            (int)swAnnotationType_e.swNote)
                            continue;

                        SelectData selectData =
                            selectionMgr.CreateSelectData();

                        if (annotation.Select3(true, selectData))
                            selectedCount++;
                    }
                    catch (Exception ex)
                    {
                        BinspectionLog.Warn("BalloonManager",
                            $"RemoveNotesOnLayer item Error: {ex}");
                    }
                }

                if (selectedCount == 0)
                    return false;

                return model.Extension.DeleteSelection2(0);
            }
            catch (Exception ex)
            {
                BinspectionLog.Warn("BalloonManager",
                    $"RemoveNotesOnLayer Error: {ex}");

                return false;
            }
        }

        // Captures a balloon's OWN persistent reference id at creation
        // time, for Characteristic.BalloonPersistId - reuses
        // PersistentReferenceHelper.GetPersistentId (the confirmed-safe
        // byte[]/base64 wrapper already used for the ballooned dimension's
        // own PersistentRefId) rather than duplicating that marshaling.
        // Deliberately distinct from that dimension-side id: this one
        // points at the balloon Note's own annotation, so it can be
        // resolved and deleted directly later (RemoveBalloonsByPersistId)
        // without ever touching the dimension/GD&T frame/note it was
        // ballooning.
        public string GetBalloonPersistId(ModelDoc2 model, Note note)
        {
            if (model == null || note == null)
                return null;

            try
            {
                string id = PersistentReferenceHelper.GetPersistentId(
                    model,
                    note.GetAnnotation());

                if (id == null)
                    BinspectionLog.Warn("GetBalloonPersistId", "no persistent id could be generated for a balloon note");

                return id;
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("GetBalloonPersistId", ex);
                return null;
            }
        }

        // Removes balloons directly by their own stored persistent
        // reference ids (Characteristic.BalloonPersistId) rather than by
        // matching displayed number text - built for callers (e.g. a
        // future "remove these specific rows' balloons" action) that
        // already know exactly which balloon Notes they mean and don't
        // want a text-pattern match to risk catching the wrong one.
        //
        // Resolves every id in the batch before deleting anything, then
        // makes ONE DeleteSelection2 call for everything that resolved -
        // one bad id partway through shouldn't block deletion of the
        // others that already succeeded (see the caller-facing design
        // note this was built from). A "Deleted" resolution (the object
        // this id pointed to is already gone) is treated as success
        // already achieved, not a failure - it's only logged, never
        // counted against the caller. Returns how many balloons were
        // actually selected for deletion.
        public int RemoveBalloonsByPersistId(
            IModelDoc2 model,
            IEnumerable<string> base64PersistIds)
        {
            if (model == null || base64PersistIds == null)
                return 0;

            try
            {
                ModelDoc2 modelDoc =
                    (ModelDoc2)model;

                ISelectionMgr selectionMgr =
                    (ISelectionMgr)model.SelectionManager;

                int selectedCount = 0;

                foreach (string base64Id in base64PersistIds)
                {
                    if (string.IsNullOrEmpty(base64Id))
                        continue;

                    try
                    {
                        swPersistReferencedObjectStates_e state;

                        object resolved =
                            PersistentReferenceHelper.ResolvePersistentId(
                                modelDoc,
                                base64Id,
                                out state);

                        if (resolved == null)
                        {
                            if ((state & swPersistReferencedObjectStates_e.swPersistReferencedObject_Deleted) != 0)
                            {
                                BinspectionLog.Warn("BalloonManager",
                                    "RemoveBalloonsByPersistId: already gone (Deleted) - " +
                                    DescribeId(base64Id));
                            }
                            else
                            {
                                BinspectionLog.Warn("BalloonManager",
                                    "RemoveBalloonsByPersistId: could not resolve (" + state + ") - " +
                                    DescribeId(base64Id));
                            }

                            continue;
                        }

                        IAnnotation annotation =
                            resolved as IAnnotation;

                        if (annotation == null)
                        {
                            BinspectionLog.Warn("BalloonManager",
                                "RemoveBalloonsByPersistId: resolved object was not an IAnnotation - actual type " +
                                resolved.GetType().FullName + " - " + DescribeId(base64Id));

                            continue;
                        }

                        SelectData selectData =
                            selectionMgr.CreateSelectData();

                        if (annotation.Select3(true, selectData))
                            selectedCount++;
                    }
                    catch (Exception ex)
                    {
                        BinspectionLog.Warn("BalloonManager",
                            $"RemoveBalloonsByPersistId item Error: {ex}");
                    }
                }

                if (selectedCount == 0)
                    return 0;

                bool deleted =
                    model.Extension.DeleteSelection2(0);

                if (!deleted)
                {
                    BinspectionLog.Warn("BalloonManager",
                        "RemoveBalloonsByPersistId: DeleteSelection2 returned false with " +
                        selectedCount + " item(s) selected.");
                }

                return selectedCount;
            }
            catch (Exception ex)
            {
                BinspectionLog.Warn("BalloonManager",
                    $"RemoveBalloonsByPersistId Error: {ex}");

                return 0;
            }
        }

        // Shortens a base64 persist id for log readability without
        // dumping the whole (often 20-30+ char) string every time.
        private static string DescribeId(string base64Id)
        {
            return base64Id.Length <= 12
                ? base64Id
                : base64Id.Substring(0, 12) + "...";
        }
    }
}