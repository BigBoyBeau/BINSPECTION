using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

namespace BINSPECTION.Models
{
    // Everything that came out of comparing the saved balloon data (the
    // external .binspection.json file) against the drawing as it exists
    // right now, produced by Core/ReconciliationService.cs.
    //
    // Unlike Characteristic, this is a transient, in-session helper - it
    // holds live SolidWorks COM references and is never itself written to
    // disk.
    public class ReconciliationResult
    {
        // Persisted characteristics whose dimension still resolves AND
        // whose balloon note is present and correctly numbered. Nothing
        // needs to happen for these.
        public List<Characteristic> Matched { get; set; }

        // Persisted characteristics whose dimension still resolves, but
        // whose balloon note could not be found on the sheet. This is the
        // exact symptom that used to show up as "everything got
        // renumbered on reopen" - the dimension was always there, the
        // add-in just had no memory of its number.
        public List<MissingBalloonEntry> MissingBalloons { get; set; }

        // Persisted characteristics whose dimension could not be located
        // at all anymore. Kept in the saved data rather than silently
        // dropped, so the user can see what happened and so the number is
        // never handed out again. Each entry carries a Reason explaining
        // exactly which step failed (no reference saved / reference didn't
        // resolve / resolved object wasn't a dimension) - this is the
        // exact spot to look at if a dimension that's clearly still on the
        // drawing keeps ending up here instead of in Matched.
        public List<UnresolvableEntry> Unresolvable { get; set; }

        // Balloon-shaped notes ("(12)", "(12.2)", etc.) found on the sheet
        // whose display number doesn't appear anywhere in the saved data
        // file. Reported only - never touched automatically.
        public List<string> OrphanedBalloonNumbers { get; set; }

        public ReconciliationResult()
        {
            Matched = new List<Characteristic>();
            MissingBalloons = new List<MissingBalloonEntry>();
            Unresolvable = new List<UnresolvableEntry>();
            OrphanedBalloonNumbers = new List<string>();
        }

        // True if anything here needs the user's attention before the
        // add-in changes the drawing.
        public bool HasMismatches
        {
            get
            {
                return MissingBalloons.Count > 0 ||
                    Unresolvable.Count > 0 ||
                    OrphanedBalloonNumbers.Count > 0;
            }
        }

        // Every persisted characteristic whose dimension still resolves,
        // whether or not its balloon note is currently on the sheet.
        //
        // This is what CharacteristicManager should be loaded from - NOT
        // just Matched. A characteristic in MissingBalloons still "owns"
        // its number even if the user chooses not to recreate the balloon
        // right now (answering "No" in ReconciliationPrompt); if it were
        // left out here, the next scan would treat that dimension as
        // brand new and hand out a duplicate number for it, and saving
        // the data file afterward would silently drop it.
        public List<Characteristic> GetResolvedCharacteristics()
        {
            List<Characteristic> resolved = new List<Characteristic>(Matched);

            foreach (MissingBalloonEntry entry in MissingBalloons)
            {
                resolved.Add(entry.Characteristic);
            }

            return resolved;
        }
    }

    // A persisted characteristic whose source (dimension, GD&T frame, or
    // note) is still in the drawing but whose balloon note is missing from
    // the sheet.
    public class MissingBalloonEntry
    {
        public Characteristic Characteristic { get; set; }

        // The live object resolved from Characteristic.PersistentRefId,
        // ready to hand to BalloonManager.CreateBalloon if the user
        // chooses to recreate the balloon - an IDisplayDimension, IGtol, or
        // INote depending on what this characteristic was originally
        // ballooned from (see ReconciliationService.Reconcile). Typed as a
        // bare object rather than any one of those interfaces since there's
        // no common SolidWorks interface across all three;
        // BalloonManager.CreateBalloon's object-typed overload is what
        // sorts out which one it actually is.
        public object ResolvedAnnotationSource { get; set; }
    }

    // A persisted characteristic whose dimension could not be resolved
    // back to something on the drawing, plus WHY - see
    // ReconciliationService.Reconcile for where each Reason is set.
    public class UnresolvableEntry
    {
        public Characteristic Characteristic { get; set; }

        public string Reason { get; set; }
    }
}
