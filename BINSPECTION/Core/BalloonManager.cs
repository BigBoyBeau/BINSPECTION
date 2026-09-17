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
            View sourceView = null)
        {
            return CreateBalloon(model, displayDim, characteristicNumber.ToString(), sheetName, sourceView);
        }

        // Same as above, but takes the display string directly so a
        // grouped balloon ("12.2") can be created without a fake int
        // representation.
        public Note CreateBalloon(
            ModelDoc2 model,
            IDisplayDimension displayDim,
            string displayNumber,
            string sheetName = null,
            View sourceView = null)
        {
            return CreateBalloon(model, (object)displayDim, displayNumber, sheetName, sourceView);
        }

        // Balloons a GD&T feature control frame - same placement/styling as
        // a dimension balloon, just anchored to the frame's own position
        // instead of a dimension's.
        public Note CreateBalloon(
            ModelDoc2 model,
            IGtol gtol,
            string displayNumber,
            string sheetName = null,
            View sourceView = null)
        {
            return CreateBalloon(model, (object)gtol, displayNumber, sheetName, sourceView);
        }

        // Balloons a free-standing Note (a callout with a leader to real
        // geometry - see CommandManagerHandler.OnCreateBalloons for the
        // eligibility check) the same way as a dimension or GD&T frame.
        public Note CreateBalloon(
            ModelDoc2 model,
            INote sourceNote,
            string displayNumber,
            string sheetName = null,
            View sourceView = null)
        {
            return CreateBalloon(model, (object)sourceNote, displayNumber, sheetName, sourceView);
        }

        // Balloons a surface finish symbol - same placement/styling as a
        // dimension or GD&T balloon, anchored to the symbol's own position.
        public Note CreateBalloon(
            ModelDoc2 model,
            ISFSymbol surfaceFinish,
            string displayNumber,
            string sheetName = null,
            View sourceView = null)
        {
            return CreateBalloon(model, (object)surfaceFinish, displayNumber, sheetName, sourceView);
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
            View sourceView = null)
        {
            LastFailureReason = null;

            if (model == null || annotationSource == null)
            {
                LastFailureReason = "model or annotation source was null";
                return null;
            }

            DrawingSheetHelper.ActivateSheet(model as DrawingDoc, sheetName);

            // Prevent duplicate characteristic numbers
            Note existing =
                FindExistingBalloon(
                    model,
                    displayNumber);

            if (existing != null)
                return existing;

            try
            {
                Annotation sourceAnnotation =
                    GetSourceAnnotation(annotationSource);

                if (sourceAnnotation == null)
                {
                    LastFailureReason =
                        "GetSourceAnnotation returned null (unrecognized annotation type or GetAnnotation() failed)";
                    return null;
                }

                double[] pos =
                    (double[])sourceAnnotation.GetPosition();

                if (pos == null || pos.Length < 3)
                {
                    LastFailureReason =
                        "source annotation GetPosition() returned null/invalid on sheet '" + sheetName + "'";
                    return null;
                }

                string text =
                    displayNumber;

                // Selecting the source's own placed view before InsertNote
                // is what makes the new Note attach to THAT view instead of
                // landing as a floating, sheet-level annotation (SolidWorks
                // has no "which view" parameter on InsertNote itself - it
                // attaches to whatever is selected at call time, the same
                // way ActivateSheet above stands in for a "which sheet"
                // parameter InsertNote also lacks). Best-effort: a null
                // sourceView (caller didn't have one) or a failed select
                // just falls back to the old floating-note behavior rather
                // than failing the balloon.
                SelectViewForAttachment(model, sourceView);

                Note note =
                    (Note)model.InsertNote(text);

                if (note == null)
                {
                    LastFailureReason =
                        "InsertNote(\"" + text + "\") returned null on sheet '" + sheetName + "'";
                    return null;
                }

                Annotation balloonAnnotation =
                    (Annotation)note.GetAnnotation();

                if (balloonAnnotation == null)
                {
                    LastFailureReason = "new note's GetAnnotation() returned null";
                    return null;
                }

                double[] placedPos =
                    BalloonPlacementService.ComputePosition(
                        annotationSource, sourceAnnotation, pos, displayNumber);

                balloonAnnotation.SetPosition(
                    placedPos[0],
                    placedPos[1],
                    placedPos[2]);

                ApplyBalloonStyle(model, note, balloonAnnotation);

                return note;
            }
            catch (Exception ex)
            {
                LastFailureReason = ex.GetType().Name + ": " + ex.Message;

                System.Diagnostics.Debug.WriteLine(
                    $"CreateBalloon Error: {ex}");

                return null;
            }
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
        // gone - only once the new note exists is the old one removed.
        // Returns the new Note, or null (with LastFailureReason set) on
        // failure.
        public Note MoveBalloon(ModelDoc2 model, string displayNumber, string targetSheetName)
        {
            LastFailureReason = null;

            if (model == null || string.IsNullOrEmpty(targetSheetName))
            {
                LastFailureReason = "model or target sheet name was null";
                return null;
            }

            try
            {
                Note existingNote =
                    FindExistingBalloon(model, displayNumber);

                if (existingNote == null)
                {
                    LastFailureReason =
                        "could not find an existing balloon numbered \"" + displayNumber + "\"";
                    return null;
                }

                Annotation existingAnnotation =
                    (Annotation)existingNote.GetAnnotation();

                double[] pos =
                    existingAnnotation != null ? (double[])existingAnnotation.GetPosition() : null;

                if (!DrawingSheetHelper.ActivateSheet(model as DrawingDoc, targetSheetName))
                {
                    LastFailureReason = "could not activate sheet '" + targetSheetName + "'";
                    return null;
                }

                Note newNote =
                    (Note)model.InsertNote(displayNumber);

                if (newNote == null)
                {
                    LastFailureReason =
                        "InsertNote(\"" + displayNumber + "\") returned null on sheet '" + targetSheetName + "'";
                    return null;
                }

                Annotation newAnnotation =
                    (Annotation)newNote.GetAnnotation();

                if (newAnnotation == null)
                {
                    LastFailureReason = "new note's GetAnnotation() returned null";
                    return null;
                }

                if (pos != null && pos.Length >= 3)
                    newAnnotation.SetPosition(pos[0], pos[1], pos[2]);

                ApplyBalloonStyle(model, newNote, newAnnotation);

                try
                {
                    existingAnnotation.Select3(false, null);

                    model.Extension.DeleteSelection2(
                        (int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"MoveBalloon: failed to delete old note on the source sheet: {ex}");
                }

                return newNote;
            }
            catch (Exception ex)
            {
                LastFailureReason = ex.GetType().Name + ": " + ex.Message;

                System.Diagnostics.Debug.WriteLine(
                    $"MoveBalloon Error: {ex}");

                return null;
            }
        }

        // Selects sourceView by name so the immediately-following InsertNote
        // attaches its new Note to that view (SelectByID2 + "DRAWINGVIEW" is
        // the standard SolidWorks API technique for selecting a placed
        // drawing view by name - there's no InsertNote overload that takes a
        // view directly). Clears any prior selection first so a stale
        // selection from earlier in the same command can't get INSTEAD
        // attached to. Failures (view already deleted, name lookup throws,
        // etc.) are swallowed - the balloon still gets created, just as a
        // floating sheet-level note like before this feature existed.
        private static void SelectViewForAttachment(ModelDoc2 model, View sourceView)
        {
            if (sourceView == null)
                return;

            try
            {
                string viewName = sourceView.GetName2();

                if (string.IsNullOrEmpty(viewName))
                    return;

                model.ClearSelection2(true);

                model.Extension.SelectByID2(
                    viewName, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0);
            }
            catch
            {
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
                    return;

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
                }
                else if (layer.Color != colorRef)
                {
                    layer.Color = colorRef;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"EnsureBalloonLayer Error: {ex}");
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
        private static void ApplyBalloonStyle(ModelDoc2 model, Note note, Annotation annotation)
        {
            try
            {
                note.SetBalloon(
                    (int)swBalloonStyle_e.swBS_Inspection,
                    (int)swBalloonFit_e.swBF_Tightest);

                int red =
                    ColorTranslator.ToWin32(Color.Red);

                annotation.Color = red;

                EnsureBalloonLayer(model, red);

                annotation.Layer = BalloonLayerName;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ApplyBalloonStyle Error: {ex}");
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
        public Note FindExistingBalloon(
            ModelDoc2 model,
            string displayNumber)
        {
            DrawingDoc drawing = model as DrawingDoc;

            if (drawing == null)
                return null;

            Note found = null;

            AnnotationScanner.WalkAllAnnotations(drawing, (annotation, view) =>
            {
                if (found != null)
                    return;

                // Title-block/border content (e.g. a plain numeric drawing
                // zone reference marker) gets mixed into this same walk -
                // exclude it so it can never masquerade as "an existing
                // balloon" for whatever number it happens to look like. See
                // AnnotationScanner.IsOwnedBySheetFormat's remarks.
                if (AnnotationScanner.IsOwnedBySheetFormat(annotation))
                    return;

                Note note =
                    annotation.GetSpecificAnnotation()
                        as Note;

                if (note == null)
                    return;

                // Extract the number via the same regex used to detect
                // orphaned balloons, rather than comparing the whole string -
                // more forgiving of incidental formatting differences (extra
                // whitespace inside the parens, etc.) as long as the number
                // itself matches, and normalizes away any formatting markup
                // SolidWorks may have embedded in the text after a
                // save/reopen cycle.
                string normalizedText =
                    NormalizeNoteText(note.GetText());

                Match match =
                    BalloonTextPattern.Match(normalizedText);

                if (match.Success &&
                    match.Groups[1].Value == displayNumber)
                {
                    found = note;
                }
            });

            return found;
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
                Note note =
                    FindExistingBalloon(
                        model,
                        displayNumber);

                if (note == null)
                    return false;

                Annotation ann =
                    (Annotation)note.GetAnnotation();

                if (ann == null)
                    return false;

                ann.Select3(
                    false,
                    null);

                model.Extension.DeleteSelection2(
                    (int)swDeleteSelectionOptions_e.swDelete_Absorbed);

                return true;
            }
            catch
            {
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
        // safe. Returns how many balloons were deleted.
        //
        // Walks every view on every sheet (AnnotationScanner.WalkAllAnnotations)
        // rather than the old document-level GetFirstAnnotation2 walk - see
        // FindExistingBalloon's remarks for why that missed anything not on
        // the currently-active sheet. A sheet-level balloon note can surface
        // more than once this way (once per view on its sheet); duplicate
        // entries are harmless here since a second Select3/DeleteSelection2
        // on an already-deleted annotation just throws, which is caught
        // below same as any other per-item failure.
        public int DeleteAllBalloons(ModelDoc2 model)
        {
            DrawingDoc drawing = model as DrawingDoc;

            if (drawing == null)
                return 0;

            List<Annotation> balloons = new List<Annotation>();

            AnnotationScanner.WalkAllAnnotations(drawing, (annotation, view) =>
            {
                try
                {
                    if (string.Equals(
                        annotation.Layer,
                        BalloonLayerName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        balloons.Add(annotation);
                    }
                }
                catch
                {
                }
            });

            int deletedCount = 0;

            foreach (Annotation ann in balloons)
            {
                try
                {
                    ann.Select3(
                        false,
                        null);

                    model.Extension.DeleteSelection2(
                        (int)swDeleteSelectionOptions_e.swDelete_Absorbed);

                    deletedCount++;
                }
                catch
                {
                }
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
            catch
            {
            }

            foreach (string sheetName in sheetNames)
            {
                if (!DrawingSheetHelper.ActivateSheet(drawing, sheetName))
                    continue;

                Annotation annotation = model.IGetFirstAnnotation2();

                while (annotation != null)
                {
                    try
                    {
                        if (string.Equals(
                            annotation.Layer,
                            BalloonLayerName,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            balloons.Add(annotation);
                        }
                    }
                    catch
                    {
                    }

                    annotation = annotation.GetNext3();
                }
            }

            if (!string.IsNullOrEmpty(originalSheetName))
                DrawingSheetHelper.ActivateSheet(drawing, originalSheetName);

            int deletedCount = 0;

            foreach (Annotation ann in balloons)
            {
                try
                {
                    ann.Select3(
                        false,
                        null);

                    model.Extension.DeleteSelection2(
                        (int)swDeleteSelectionOptions_e.swDelete_Absorbed);

                    deletedCount++;
                }
                catch
                {
                }
            }

            return deletedCount;
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
                            System.Diagnostics.Debug.WriteLine(
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
                        System.Diagnostics.Debug.WriteLine(
                            $"RemoveNotesOnLayer item Error: {ex}");
                    }
                }

                if (selectedCount == 0)
                    return false;

                return model.Extension.DeleteSelection2(0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
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

            return PersistentReferenceHelper.GetPersistentId(
                model,
                note.GetAnnotation());
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
                                System.Diagnostics.Debug.WriteLine(
                                    "RemoveBalloonsByPersistId: already gone (Deleted) - " +
                                    DescribeId(base64Id));
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine(
                                    "RemoveBalloonsByPersistId: could not resolve (" + state + ") - " +
                                    DescribeId(base64Id));
                            }

                            continue;
                        }

                        IAnnotation annotation =
                            resolved as IAnnotation;

                        if (annotation == null)
                        {
                            System.Diagnostics.Debug.WriteLine(
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
                        System.Diagnostics.Debug.WriteLine(
                            $"RemoveBalloonsByPersistId item Error: {ex}");
                    }
                }

                if (selectedCount == 0)
                    return 0;

                bool deleted =
                    model.Extension.DeleteSelection2(0);

                if (!deleted)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "RemoveBalloonsByPersistId: DeleteSelection2 returned false with " +
                        selectedCount + " item(s) selected.");
                }

                return selectedCount;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
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