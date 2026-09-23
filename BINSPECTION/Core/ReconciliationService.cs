using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using BINSPECTION.Models;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace BINSPECTION.Core
{
    // Compares what the external data file says SHOULD be on the drawing
    // against what is ACTUALLY on the drawing right now, before any new
    // balloons get created or numbers get assigned.
    //
    // This is the piece that actually fixes the renumbering bug: instead
    // of always assuming every dimension is new (because the in-memory
    // tracker just came back empty after a reload), we resolve each saved
    // characteristic's persistent reference back to a live dimension in
    // the currently open drawing and recognize it as already-numbered.
    public static class ReconciliationService
    {
        public static ReconciliationResult Reconcile(
            ModelDoc2 model,
            List<Characteristic> persisted)
        {
            ReconciliationResult result = new ReconciliationResult();

            if (model == null || persisted == null)
                return result;

            BalloonManager balloonManager = new BalloonManager();

            // Computed ONCE and shared by every FindExistingBalloon call
            // below plus FindOrphanedBalloons - each of those used to call
            // DrawingSheetHelper.GetAllViewsBySheet (via
            // AnnotationScanner.WalkAllAnnotations) itself with no sheet
            // restriction, which activates every sheet in the drawing to
            // attribute its views correctly (see that method's remarks).
            // With one such call per PERSISTED CHARACTERISTIC, a reconcile
            // over 40 characteristics on a 5-sheet drawing meant cycling
            // through every sheet 40+ times before this. Computed once
            // here across the WHOLE drawing (not scoped to any sheet
            // selection - a characteristic can be resolved on any sheet)
            // and passed into every call below cuts that back to one pass.
            DrawingDoc drawing = model as DrawingDoc;

            List<DrawingSheetHelper.ViewOnSheet> viewsBySheet =
                DrawingSheetHelper.GetAllViewsBySheet(drawing);

            // Stashed on the result so RecreateMissingBalloons can reuse
            // this same cache instead of recomputing it once per balloon it
            // recreates.
            result.ViewsBySheet = viewsBySheet;

            // Every display number that has ever been saved for this
            // drawing, whether or not it resolves right now - used below to
            // figure out which on-sheet balloons are orphaned.
            HashSet<string> accountedForNumbers = new HashSet<string>();

            foreach (Characteristic characteristic in persisted)
            {
                // Intentionally excluded from inspection (see
                // Core/BalloonGridService.MarkUnnumbered) - there's no
                // balloon for these by design, so they never go through the
                // resolve/missing-balloon checks below. Still counted as
                // "resolved" so CharacteristicManager keeps tracking them
                // and Create Balloons never re-ballons the dimension.
                if (characteristic.IsUnnumbered)
                {
                    result.Matched.Add(characteristic);
                    continue;
                }

                accountedForNumbers.Add(characteristic.DisplayNumber);

                // Case 1: nothing was ever saved for this row (data file
                // predates the persistent-reference fix, or the reference
                // failed to generate when this characteristic was created).
                if (string.IsNullOrEmpty(characteristic.PersistentRefId))
                {
                    result.Unresolvable.Add(
                        new UnresolvableEntry
                        {
                            Characteristic = characteristic,
                            Reason = "No persistent reference was ever saved for this characteristic.",
                        });

                    continue;
                }

                // A characteristic auto-split from one multi-line note (see
                // CommandManagerHandler.OnCreateBalloons) carries a
                // synthetic "<realId>#<line>" PersistentRefId so each line
                // gets its own CharacteristicManager entry, even though
                // every line really does come from the same one note. Real
                // base64 persistent references never contain '#', so
                // stripping everything from the first one found and
                // resolving THAT is always safe and always correct for
                // these - there is no separate live object per line to
                // resolve, only the shared note.
                string idToResolve = characteristic.PersistentRefId;
                int syntheticMarker = idToResolve.IndexOf('#');

                if (syntheticMarker >= 0)
                    idToResolve = idToResolve.Substring(0, syntheticMarker);

                swPersistReferencedObjectStates_e state;

                object resolved = PersistentReferenceHelper.ResolvePersistentId(
                    model,
                    idToResolve,
                    out state);

                // Case 2: SolidWorks itself couldn't resolve the reference
                // back to a live object at all.
                if (resolved == null)
                {
                    result.Unresolvable.Add(
                        new UnresolvableEntry
                        {
                            Characteristic = characteristic,
                            Reason = "Persistent reference did not resolve (SolidWorks reported: " + state + ").",
                        });

                    continue;
                }

                // Try every source type Create Balloons can produce a
                // characteristic from - a dimension, a GD&T feature control
                // frame, a free-standing note, or a surface finish symbol -
                // in that order. Try the most direct interpretation first:
                // maybe the resolved object already IS one of these, with no
                // Annotation indirection needed at all.
                object resolvedSource =
                    resolved as IDisplayDimension ??
                    (object)(resolved as IGtol) ??
                    (object)(resolved as INote) ??
                    (object)(resolved as ISFSymbol);

                // Fall back to "it's an annotation wrapping one of the
                // above" - cast to the INTERFACE (IAnnotation), not the
                // coclass (Annotation), since objects handed back by a
                // generic resolver API like GetObjectByPersistReference3
                // come across as a bare COM object that only reliably casts
                // to the interface it implements, not the coclass type.
                if (resolvedSource == null)
                {
                    IAnnotation annotation = resolved as IAnnotation;

                    if (annotation != null)
                    {
                        object specific = annotation.GetSpecificAnnotation();

                        resolvedSource =
                            specific as IDisplayDimension ??
                            (object)(specific as IGtol) ??
                            (object)(specific as INote) ??
                            (object)(specific as ISFSymbol);
                    }
                }

                // Case 3: none of the four interpretations worked. Run
                // every plausible cast we can think of and report exactly
                // which ones (if any) succeed, plus the raw runtime type -
                // the fastest way to find the right one empirically instead
                // of guessing again.
                if (resolvedSource == null)
                {
                    result.Unresolvable.Add(
                        new UnresolvableEntry
                        {
                            Characteristic = characteristic,
                            Reason = "Could not get a dimension, GD&T frame, note, or surface finish symbol back from the saved reference (" +
                                DescribeResolvedObject(resolved) + ").",
                        });

                    continue;
                }

                Note existingBalloon = balloonManager.FindExistingBalloon(
                    model,
                    characteristic.DisplayNumber,
                    viewsBySheet);

                // Only the FIRST line of an auto-split multi-line note gets
                // a real physical balloon (showing its own "N.1" display
                // number, same as any characteristic) - lines 2+ (synthetic
                // marker present) are data-only and were never meant to
                // have a balloon of their own, so there's never a "missing
                // balloon" to offer recreating for one: it's either
                // resolvable (this branch, mirroring the shared note's
                // liveness) or not (handled above).
                // A grouped member after the first ("10.1") is likewise
                // data-only by design - its group anchor's "10" balloon
                // stands for it (Characteristic.SharesGroupBalloon).
                if (existingBalloon == null && syntheticMarker < 0 && !characteristic.SharesGroupBalloon)
                {
                    // The source is still there, but its balloon note is
                    // missing from the sheet - this is exactly the
                    // "reopened the drawing and it got renumbered/lost its
                    // balloon" symptom this whole change is meant to fix.
                    result.MissingBalloons.Add(
                        new MissingBalloonEntry
                        {
                            Characteristic = characteristic,
                            ResolvedAnnotationSource = resolvedSource,
                        });

                    continue;
                }

                // Source resolves fine and (for anything that has its own
                // balloon) that balloon is present and correctly numbered -
                // nothing to do for this one.
                result.Matched.Add(characteristic);
            }

            FindOrphanedBalloons(model, accountedForNumbers, result, viewsBySheet);

            return result;
        }

        // Diagnostic only: when a resolved persistent-reference object
        // doesn't match either interpretation this code knows about
        // (IAnnotation-wrapping-a-dimension, or a dimension directly),
        // this checks a broader set of plausible SolidWorks interfaces and
        // reports which ones (if any) the object actually implements.
        // This turns a failed guess into an empirical answer we can act on
        // directly, instead of a third round of blind interface guessing.
        private static string DescribeResolvedObject(object resolved)
        {
            if (resolved == null)
                return "resolved object is null";

            List<string> matches = new List<string>();

            if (resolved is IAnnotation)
                matches.Add("IAnnotation");

            if (resolved is IDisplayDimension)
                matches.Add("IDisplayDimension");

            if (resolved is IGtol)
                matches.Add("IGtol");

            if (resolved is IDimension)
                matches.Add("IDimension");

            if (resolved is INote)
                matches.Add("INote");

            if (resolved is ISFSymbol)
                matches.Add("ISFSymbol");

            if (resolved is IFeature)
                matches.Add("IFeature");

            if (resolved is IEntity)
                matches.Add("IEntity");

            if (resolved is Annotation)
                matches.Add("Annotation (coclass)");

            if (resolved is DisplayDimension)
                matches.Add("DisplayDimension (coclass)");

            if (resolved is Note)
                matches.Add("Note (coclass)");

            if (resolved is SFSymbol)
                matches.Add("SFSymbol (coclass)");

            string runtimeTypeName = resolved.GetType().FullName;

            if (matches.Count == 0)
            {
                return "runtime type " + runtimeTypeName +
                    "; did not match any of IAnnotation, IDisplayDimension, IGtol, IDimension, INote, ISFSymbol, IFeature, IEntity, or their coclasses";
            }

            return "runtime type " + runtimeTypeName +
                "; matched: " + string.Join(", ", matches.ToArray());
        }

        // Walks every VIEW on every sheet (AnnotationScanner.WalkAllAnnotations)
        // looking for balloon-shaped notes - text like "(12)" - whose number
        // isn't in the saved data file at all. These are only reported,
        // never auto-deleted.
        //
        // Was a document-level model.GetFirstAnnotation2() walk, which only
        // sees annotations on whichever sheet SolidWorks currently has
        // active - see BalloonManager.FindExistingBalloon's remarks (same
        // gap, same fix).
        private static void FindOrphanedBalloons(
            ModelDoc2 model,
            HashSet<string> accountedForNumbers,
            ReconciliationResult result,
            List<DrawingSheetHelper.ViewOnSheet> viewsBySheet = null)
        {
            DrawingDoc drawing = model as DrawingDoc;

            if (drawing == null)
                return;

            AnnotationScanner.WalkAllAnnotations(drawing, (annotation, view, sheetName) =>
            {
                // Same "is this a BINSPECTION balloon" test as
                // BalloonManager.FindExistingBalloon - excludes title-block
                // content AND any numeric note that isn't on the Binspection
                // layer (a user's own "12" note, a SOLIDWORKS Inspection
                // balloon), which would otherwise get reported as an
                // "orphaned balloon" and offered for deletion.
                Note note;
                string displayNumber;

                if (!BalloonManager.IsBinspectionBalloon(annotation, out note, out displayNumber))
                    return;

                // A sheet-level balloon note can be visited more than once
                // by WalkAllAnnotations (once per view on its sheet - see
                // AnnotationScanner's class remarks) - guard against adding
                // the same orphan number twice from that, not just against
                // ones already accounted for in the saved data file.
                if (!accountedForNumbers.Contains(displayNumber) &&
                    !result.OrphanedBalloonNumbers.Contains(displayNumber))
                {
                    result.OrphanedBalloonNumbers.Add(displayNumber);
                }
            }, viewsBySheet);
        }

        // Recreates a balloon for every entry in MissingBalloons, reusing
        // the characteristic number that was already assigned to it. This
        // is what restores the drawing to the numbering the data file
        // remembers, instead of letting a new number get handed out.
        //
        // Only called after the user has confirmed via ReconciliationPrompt
        // - this method itself does not ask for confirmation.
        public static void RecreateMissingBalloons(
            ModelDoc2 model,
            ReconciliationResult result,
            BalloonManager balloonManager)
        {
            // Built ONCE for however many balloons this reconciliation is
            // about to recreate, not re-walked per balloon - same reasoning
            // as CommandManagerHandler.OnCreateBalloons' existingBalloonIndex
            // (see CreateBalloon's remarks). A drawing with a lot of
            // missing balloons to recreate is exactly the "large" case
            // where the per-call walk's cost used to add up badly.
            Dictionary<string, Note> existingBalloonIndex =
                balloonManager.BuildExistingBalloonIndex(model as DrawingDoc, result.ViewsBySheet);

            // Every record a recreated balloon might stand for besides its
            // own - see RecordNewBalloonPersistId.
            List<Characteristic> everyone = new List<Characteristic>(result.Matched);

            foreach (MissingBalloonEntry entry in result.MissingBalloons)
                everyone.Add(entry.Characteristic);

            foreach (UnresolvableEntry entry in result.Unresolvable)
                everyone.Add(entry.Characteristic);

            foreach (MissingBalloonEntry entry in result.MissingBalloons)
            {
                Note note = balloonManager.CreateBalloon(
                    model,
                    entry.ResolvedAnnotationSource,
                    entry.Characteristic.DisplayNumber,
                    entry.Characteristic.SheetName,
                    sourceView: null,
                    viewsBySheet: result.ViewsBySheet,
                    existingBalloonIndex: existingBalloonIndex);

                if (note != null)
                {
                    RecordNewBalloonPersistId(
                        entry.Characteristic, balloonManager.GetBalloonPersistId(model, note), everyone);
                }

                result.Matched.Add(entry.Characteristic);
            }

            result.MissingBalloons.Clear();
        }

        // A recreated balloon is a brand-new note with a new persistent id,
        // so the old BalloonPersistId now points at a deleted note. Writing
        // the new id back lets later lookups by id (Balloon Manager's
        // jump-to-row - see BalloonGridService.FindBalloonAnnotation; Save/
        // Restore Position) hit directly instead of falling back to walking
        // sheets. The callers (Create/Restore/Refresh Balloons) save these
        // same Characteristic instances afterwards, so the update persists
        // with no extra save here. Records that share this one note get the
        // same new id: a multi-value balloon's "<id>#n" values, a group's
        // data-only members (SharesGroupBalloon), and anything still holding
        // the old id.
        private static void RecordNewBalloonPersistId(
            Characteristic owner,
            string newBalloonPersistId,
            List<Characteristic> everyone)
        {
            if (string.IsNullOrEmpty(newBalloonPersistId))
                return;

            string oldBalloonPersistId = owner.BalloonPersistId;
            string siblingPrefix = string.IsNullOrEmpty(owner.PersistentRefId) ? null : owner.PersistentRefId + "#";

            owner.BalloonPersistId = newBalloonPersistId;

            foreach (Characteristic other in everyone)
            {
                if (other == owner)
                    continue;

                bool sharesNote =
                    (!string.IsNullOrEmpty(oldBalloonPersistId) && other.BalloonPersistId == oldBalloonPersistId) ||
                    (siblingPrefix != null && other.PersistentRefId != null &&
                        other.PersistentRefId.StartsWith(siblingPrefix, System.StringComparison.Ordinal)) ||
                    (other.SharesGroupBalloon && !other.IsUnnumbered && !owner.SubNumber.HasValue &&
                        other.Number == owner.Number);

                if (sharesNote)
                    other.BalloonPersistId = newBalloonPersistId;
            }
        }

        // Deletes every balloon note whose number was found on the sheet
        // but isn't listed in the saved data file (see
        // OrphanedBalloonNumbers) - typically leftover duplicates from a
        // numbering bug, or a balloon-shaped note someone added by hand.
        //
        // Only called after the user has confirmed via
        // ReconciliationPrompt.ShowDeleteOrphanedPrompt - this method
        // itself does not ask for confirmation, and it never touches a
        // balloon whose number IS in the saved file.
        public static void DeleteOrphanedBalloons(
            ModelDoc2 model,
            ReconciliationResult result,
            BalloonManager balloonManager)
        {
            foreach (string number in result.OrphanedBalloonNumbers)
            {
                balloonManager.DeleteBalloon(model, number);
            }

            result.OrphanedBalloonNumbers.Clear();
        }
    }
}
