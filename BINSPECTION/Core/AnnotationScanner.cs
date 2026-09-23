using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace BINSPECTION.Core
{
    // Finds the non-dimension annotations Create Balloons should also
    // balloon: GD&T feature control frames, surface finish symbols, and
    // free-standing Notes that are genuinely pointing at a feature (not a
    // title-block field or a general note - see GetEligibleNotes).
    //
    // All three walk every drawing VIEW via DrawingSheetHelper.GetAllViews
    // (the same walk DimensionScanner uses) and call IView::GetFirstAnnotation3
    // / IAnnotation::GetNext3 per view, rather than the document-level
    // IModelDoc2::GetFirstAnnotation2 - GetFirstAnnotation2 only sees
    // annotations on the CURRENTLY ACTIVE sheet, so a multi-sheet drawing
    // silently lost GD&T frames, notes, and surface finishes living on any
    // sheet that wasn't active when Create Balloons was run. GetAllViews
    // itself used to be a plain drawing.GetFirstView()/view.GetNextView()
    // walk here, on the assumption that walk already spanned every sheet -
    // live diagnostic data (2026-09-15) proved that assumption wrong: it
    // ONLY ever returned views on whichever sheet was currently active, the
    // exact same bug this class was written to work around for
    // GetFirstAnnotation2, just one level down. See GetAllViews' remarks in
    // DrawingSheetHelper.cs and
    // [[binspection_create_balloons_multi_sheet_silent_abort]]. A source
    // annotation may get visited more than once this way (a sheet-level
    // annotation can surface from more than one view's traversal) -
    // harmless, since CommandManagerHandler.OnCreateBalloons already skips
    // anything whose PersistentRefId already has a Characteristic before
    // creating a balloon.
    //
    // Every scan also excludes annotations owned by the sheet FORMAT
    // (IAnnotation.OwnerType == swAnnotationOwner_DrawingTemplate) - that is
    // SolidWorks' term for the title block, and IAnnotation::GetNext3's own
    // remarks note that the sheet format's annotations get mixed in with the
    // sheet's own when the sheet format is visible. Without this filter,
    // title-block fields (a note living in the title block, a surface finish
    // callout in the title block, etc.) would get auto-ballooned right along
    // with real inspection characteristics.
    // A GD&T frame found by GetAllGtols, paired with the drawing View it was
    // found on - see DimensionHit for why the View is needed.
    public class GtolHit
    {
        public IGtol Gtol { get; set; }

        public View View { get; set; }

        public string SheetName { get; set; }
    }

    // A surface finish symbol found by GetAllSurfaceFinishes, paired with
    // the drawing View it was found on - see DimensionHit.
    public class SurfaceFinishHit
    {
        public ISFSymbol SurfaceFinish { get; set; }

        public View View { get; set; }

        public string SheetName { get; set; }
    }

    // A free-standing note found by GetEligibleNotes, paired with the
    // drawing View it was found on - see DimensionHit.
    public class NoteHit
    {
        public INote Note { get; set; }

        public View View { get; set; }

        public string SheetName { get; set; }
    }

    public class AnnotationScanner
    {
        // Public (not private) so BalloonManager/ReconciliationService can
        // apply the same exclusion when matching a note's TEXT against
        // BalloonTextPattern (FindExistingBalloon, FindOrphanedBalloons) -
        // without it, a plain numeric title-block/border field (e.g. a
        // drawing zone reference marker like "1", "2", "3"...) matches the
        // same regex a real balloon's display number would, and gets
        // treated as "an existing balloon with that number" - silently
        // blocking the real balloon for that number from ever being
        // created. Confirmed via live diagnostic data (2026-09-14): every
        // characteristic whose display number collided with a low integer
        // (1-5, 7, 10) got silently short-circuited to reuse an existing
        // note instead of creating a new one, while ones with a decimal
        // display number ("6.1", "11.1") or ones that simply didn't collide
        // with title-block content ("8", "9") were unaffected - see
        // [[binspection_balloon_active_sheet_scoping_bug]].
        public static bool IsOwnedBySheetFormat(IAnnotation annotation)
        {
            return annotation.OwnerType ==
                (int)swAnnotationOwner_e.swAnnotationOwner_DrawingTemplate;
        }

        // Walks every view on every sheet, invoking visit(annotation, view)
        // for each annotation found. Shared by every scan below so the
        // multi-sheet view walk only has to be written once. Public so
        // BalloonManager/ReconciliationService can reuse the exact same
        // walk for finding balloon-shaped notes - IModelDoc2.GetFirstAnnotation2
        // (the walk they used before) is document-level and, per SolidWorks'
        // own behavior, only sees annotations on the CURRENTLY ACTIVE sheet
        // (see remarks above); walking every view avoids that gap
        // everywhere, not just during Create Balloons' own scan.
        public static void WalkAllAnnotations(
            DrawingDoc drawing,
            System.Action<Annotation, View> visit)
        {
            WalkAllAnnotations(drawing, (annotation, view, sheetName) => visit(annotation, view));
        }

        // Same walk, but also hands back the sheet name
        // DrawingSheetHelper.GetAllViewsBySheet resolved for the view each
        // annotation was found on - used by the three hit-collecting scans
        // below so their Hit types carry a reliably-resolved SheetName
        // instead of callers re-deriving it later from the View alone (see
        // GetAllViewsBySheet's remarks for why that per-view re-derivation
        // was unreliable).
        // viewsBySheet (optional): a list already computed by
        // DrawingSheetHelper.GetAllViewsBySheet, so a caller running more
        // than one scan (OnCreateBalloons runs three via this overload -
        // GD&T, surface finishes, notes) can compute it ONCE and share it,
        // rather than each scan re-activating every sheet in the drawing
        // all over again - see DimensionScanner.GetAllDimensions' matching
        // parameter for the same reasoning.
        public static void WalkAllAnnotations(
            DrawingDoc drawing,
            System.Action<Annotation, View, string> visit,
            List<DrawingSheetHelper.ViewOnSheet> viewsBySheet = null)
        {
            foreach (DrawingSheetHelper.ViewOnSheet entry in viewsBySheet ?? DrawingSheetHelper.GetAllViewsBySheet(drawing))
            {
                // Per-view guard around the chain walk itself (not just the
                // visit callback): a view deleted since viewsBySheet was
                // built, or a GetNext3 that throws mid-chain, used to escape
                // this method entirely and abort whatever caller was
                // scanning - including CreateBalloon's duplicate check.
                try
                {
                    Annotation annotation =
                        (Annotation)entry.View.GetFirstAnnotation3();

                    while (annotation != null)
                    {
                        try
                        {
                            visit(annotation, entry.View, entry.SheetName);
                        }
                        catch (System.Exception ex)
                        {
                            // One unreadable annotation shouldn't stop the
                            // whole scan.
                            BinspectionLog.Error("AnnotationScanner.WalkAllAnnotations: visiting annotation on sheet '" + entry.SheetName + "'", ex);
                        }

                        annotation =
                            annotation.GetNext3();
                    }
                }
                catch (System.Exception ex)
                {
                    BinspectionLog.Error("AnnotationScanner.WalkAllAnnotations: walking a view on sheet '" + entry.SheetName + "'", ex);
                }
            }
        }

        // GD&T frames, found by walking every annotation on every sheet and
        // keeping the ones typed as swGTol - see class remarks for why this
        // walks per-view rather than the old document-wide
        // IModelDoc2::GetFirstAnnotation2 walk (or the older per-view
        // IView::GetGTols() walk, which silently omits a feature control
        // frame that's combined with a dimension - only picking up
        // standalone/composite ones).
        public List<GtolHit> GetAllGtols(DrawingDoc drawing, List<DrawingSheetHelper.ViewOnSheet> viewsBySheet = null)
        {
            List<GtolHit> gtols = new List<GtolHit>();

            WalkAllAnnotations(drawing, (annotation, view, sheetName) =>
            {
                if (annotation.GetType() != (int)swAnnotationType_e.swGTol)
                    return;

                if (IsOwnedBySheetFormat(annotation))
                    return;

                IGtol gtol =
                    annotation.GetSpecificAnnotation() as IGtol;

                if (gtol != null)
                {
                    gtols.Add(new GtolHit { Gtol = gtol, View = view, SheetName = sheetName });
                }
            }, viewsBySheet);

            return gtols;
        }

        // Surface finish symbols, found the same way as GD&T frames above -
        // previously not scanned for at all, so every surface finish symbol
        // on a drawing was silently skipped by Create Balloons.
        public List<SurfaceFinishHit> GetAllSurfaceFinishes(DrawingDoc drawing, List<DrawingSheetHelper.ViewOnSheet> viewsBySheet = null)
        {
            List<SurfaceFinishHit> surfaceFinishes = new List<SurfaceFinishHit>();

            WalkAllAnnotations(drawing, (annotation, view, sheetName) =>
            {
                if (annotation.GetType() != (int)swAnnotationType_e.swSFSymbol)
                    return;

                if (IsOwnedBySheetFormat(annotation))
                    return;

                ISFSymbol surfaceFinish =
                    annotation.GetSpecificAnnotation() as ISFSymbol;

                if (surfaceFinish != null)
                {
                    surfaceFinishes.Add(new SurfaceFinishHit { SurfaceFinish = surfaceFinish, View = view, SheetName = sheetName });
                }
            }, viewsBySheet);

            return surfaceFinishes;
        }

        // Free-standing notes eligible to be auto-ballooned: real Note
        // annotations (not GD&T/dimensions/etc.), not owned by the sheet
        // format (the title block - see class remarks), and not already one
        // of BINSPECTION's own balloons (a plain number like "12" or "12.2"
        // - skipping these is what stops a rescan from trying to balloon its
        // own balloons).
        //
        // Previously also required a visible leader (GetLeaderCount() > 0)
        // or a formal SW attachment to geometry (GetAttachedEntityCount3()
        // > 0), on the theory that a note with neither signal was probably a
        // genuinely floating general note (a "NOTES:" block, disclaimers,
        // etc.) rather than a real inspection callout. A real-drawing
        // diagnostic dump (2026-09-14) of every Note found on a sheet -
        // title block, existing balloons, and everything else - showed this
        // theory doesn't hold: the only Note left over after excluding
        // sheet-format ownership and BalloonTextPattern matches (both
        // already handled below) was a genuine multi-line inspection note
        // with no leader and no formal attachment at all - just placed
        // directly on its feature. There was no separate "floating general
        // note" case in the data that still needed protecting against, so
        // the leader-or-attached gate was removed entirely.
        public List<NoteHit> GetEligibleNotes(DrawingDoc drawing, List<DrawingSheetHelper.ViewOnSheet> viewsBySheet = null)
        {
            List<NoteHit> notes = new List<NoteHit>();

            WalkAllAnnotations(drawing, (annotation, view, sheetName) =>
            {
                if (annotation.GetType() != (int)swAnnotationType_e.swNote)
                    return;

                if (IsOwnedBySheetFormat(annotation))
                    return;

                Note note =
                    annotation.GetSpecificAnnotation() as Note;

                if (note == null)
                    return;

                string normalizedText =
                    BalloonManager.NormalizeNoteText(note.GetText());

                if (!BalloonManager.BalloonTextPattern.IsMatch(normalizedText))
                {
                    notes.Add(new NoteHit { Note = note, View = view, SheetName = sheetName });
                }
            }, viewsBySheet);

            return notes;
        }

        // Splits a note's text into its non-blank lines, normalizing away
        // SolidWorks formatting markup first (same normalization balloon
        // text itself gets - see BalloonManager.NormalizeNoteText).
        // SolidWorks notes use "\r\n" internally, but every common line
        // ending is handled here in case that ever varies.
        public static List<string> SplitLines(string noteText)
        {
            string normalized =
                BalloonManager.NormalizeNoteText(noteText);

            string[] rawLines =
                normalized.Split(
                    new[] { "\r\n", "\r", "\n" },
                    System.StringSplitOptions.None);

            List<string> lines = new List<string>();

            foreach (string rawLine in rawLines)
            {
                string trimmed = rawLine.Trim();

                if (trimmed.Length > 0)
                {
                    lines.Add(trimmed);
                }
            }

            return lines;
        }
    }
}
