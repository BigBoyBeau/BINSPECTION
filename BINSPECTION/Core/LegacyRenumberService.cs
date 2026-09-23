using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BINSPECTION.Models;
using SolidWorks.Interop.sldworks;

namespace BINSPECTION.Core
{
    // Turns a set of "this characteristic should become legacy number X"
    // requests (from LegacyNumberMatcher's automatic matches and the
    // user's typed exceptions) into an actual renumbering of the live
    // balloons on the drawing - not just a stored reference field. Always
    // update the balloon's live text via Note.SetText, then the
    // Characteristic's own Number/SubNumber, never the other way around.
    //
    // Converting a whole legacy project to Binspection's numbering
    // routinely means the two schemes overlap: some balloon already
    // legitimately has the exact number a DIFFERENT, unrelated balloon
    // needs to become. Rejecting that as an unresolvable collision would
    // make this feature fail on exactly the case it exists for - so a
    // target number blocked only by a "bystander" (a balloon with no
    // legacy number of its own in this run) gets that bystander
    // automatically bumped to a free number instead. A target blocked by
    // another characteristic that's ALSO getting a legacy number this run
    // is left alone (its own request will vacate the number). Only a
    // genuine collision - two DIFFERENT legacy numbers wanting the exact
    // same target - is left for the user to resolve by hand.
    public static class LegacyRenumberService
    {
        // Whole number, optionally ".subnumber" - the exact shape a
        // balloon's DisplayNumber/BalloonManager.BalloonTextPattern
        // requires. A legacy number that doesn't fit this (letters, extra
        // dots) cannot become a real balloon number at all.
        private static readonly Regex DisplayNumberPattern = new Regex(@"^(\d+)(?:\.(\d+))?$");

        // Splits a legacy number like "9.1" into Number=9/SubNumber=1 the
        // same way any other grouped balloon in this app is represented -
        // shared by BuildPlan and by CommandManagerHandler when it needs
        // to create a brand new Characteristic for a legacy number that
        // has no existing balloon at all, so both paths agree on exactly
        // what counts as a valid balloon number.
        public static bool TryParseDisplayNumber(
            string legacyNumber, out int number, out int? subNumber, out string displayNumber)
        {
            number = 0;
            subNumber = null;
            displayNumber = null;

            Match match = legacyNumber != null ? DisplayNumberPattern.Match(legacyNumber.Trim()) : Match.Empty;

            if (!match.Success)
                return false;

            number = int.Parse(match.Groups[1].Value);
            subNumber = match.Groups[2].Success ? (int?)int.Parse(match.Groups[2].Value) : null;
            displayNumber = subNumber.HasValue ? number + "." + subNumber.Value : number.ToString();

            return true;
        }

        public class RenumberPlanEntry
        {
            public Characteristic Characteristic { get; set; }
            public string OldDisplayNumber { get; set; }
            public int NewNumber { get; set; }
            public int? NewSubNumber { get; set; }
            public string NewDisplayNumber { get; set; }

            // True for an entry this plan added on its own to free up a
            // target number for someone else's legacy-driven rename - not
            // itself something the user asked to change.
            public bool IsDisplaced { get; set; }
        }

        public class RenumberPlan
        {
            public List<RenumberPlanEntry> ToApply { get; } = new List<RenumberPlanEntry>();

            // Characteristics whose live DisplayNumber ALREADY equals the
            // legacy balloon number matched to them this run - no rename
            // needed, but still a genuine successful match the caller
            // should record (LegacyBalloonNumber/Method/Class carried
            // over), same as anything in ToApply. Excludes a characteristic
            // that ALSO shows up in a rejected Collision below - being
            // "already correct" per ONE request doesn't cancel out a
            // conflicting SECOND request for the same characteristic.
            public List<Characteristic> AlreadyCorrect { get; } = new List<Characteristic>();

            // Legacy balloon numbers that can't be parsed into a valid
            // balloon display number at all - reported so the caller can
            // still record them as a reference-only LegacyBalloonNumber.
            public List<string> InvalidFormat { get; } = new List<string>();

            // "Legacy Balloon #X and #Y both want to become (<target>) -
            // resolve which one should." One line per target two DIFFERENT
            // legacy balloon numbers both requested this run - the only
            // case this plan can't resolve on its own.
            public List<string> Collisions { get; } = new List<string>();
        }

        // requestedLegacyNumbers should list every characteristic getting a
        // legacy balloon number this run. allCharacteristics is every
        // characteristic on the drawing, used both to seed which display
        // numbers are already taken and as the source of truth for what
        // still counts as "used" when picking a free number to bump a
        // bystander to.
        public static RenumberPlan BuildPlan(
            List<Characteristic> allCharacteristics,
            List<KeyValuePair<Characteristic, string>> requestedLegacyNumbers)
        {
            RenumberPlan plan = new RenumberPlan();

            List<Characteristic> numbered = allCharacteristics.Where(c => !c.IsUnnumbered).ToList();

            Dictionary<string, Characteristic> ownerOfDisplayNumber = numbered
                .ToDictionary(c => c.DisplayNumber, c => c);

            // Parse every request up front - format failures are reported
            // and dropped before any collision reasoning happens.
            List<(Characteristic Characteristic, string LegacyBalloonNumber, int NewNumber, int? NewSubNumber, string NewDisplayNumber)> parsed =
                new List<(Characteristic, string, int, int?, string)>();

            HashSet<Characteristic> alreadyCorrectCandidates = new HashSet<Characteristic>();

            foreach (KeyValuePair<Characteristic, string> request in requestedLegacyNumbers)
            {
                string legacyBalloonNumber = request.Value?.Trim();

                int newNumber;
                int? newSubNumber;
                string newDisplayNumber;

                if (!TryParseDisplayNumber(legacyBalloonNumber, out newNumber, out newSubNumber, out newDisplayNumber))
                {
                    plan.InvalidFormat.Add(legacyBalloonNumber);
                    continue;
                }

                if (newDisplayNumber == request.Key.DisplayNumber)
                {
                    // Already correct - nothing to RENAME, but still
                    // tentatively a successful match (see AlreadyCorrect's
                    // remarks) - confirmed once collision detection below
                    // has had a chance to reject it instead, in case some
                    // OTHER legacy row also matched this same characteristic
                    // wanting a genuinely different number.
                    alreadyCorrectCandidates.Add(request.Key);
                    continue;
                }

                parsed.Add((request.Key, legacyBalloonNumber, newNumber, newSubNumber, newDisplayNumber));
            }

            // Multiple legacy rows can legitimately target the exact same
            // characteristic + target number - e.g. several sample-
            // measurement rows recorded under one "OP 30 (2)" label (see
            // [[binspection_legacy_number_matching]]'s 2026-09-21 OP-
            // scoping note), or a user manually pointing two different
            // legacy rows at the same balloon via the "Assign" override.
            // Collapse those down to ONE request per (Characteristic,
            // NewDisplayNumber) pair before collision detection below - the
            // group-by-target-number check right after this only cares
            // about DIFFERENT legacy numbers fighting over one target, not
            // the same request arriving more than once.
            parsed = parsed
                .GroupBy(r => (r.Characteristic, r.NewDisplayNumber))
                .Select(g => g.First())
                .ToList();

            // A target two DIFFERENT characteristics both asked for this
            // run is a genuine conflict between two legitimate requests -
            // not something a bystander bump can fix. Both are dropped and
            // reported; everything else proceeds.
            HashSet<Characteristic> rejected = new HashSet<Characteristic>();

            foreach (var group in parsed.GroupBy(r => r.NewDisplayNumber, StringComparer.OrdinalIgnoreCase))
            {
                List<string> groupLegacyBalloonNumbers = group.Select(r => r.LegacyBalloonNumber).ToList();

                if (groupLegacyBalloonNumbers.Count > 1)
                {
                    plan.Collisions.Add(
                        "Legacy Balloon #" + string.Join(" and #", groupLegacyBalloonNumbers) +
                        " all want to become (" + group.Key + ") - resolve which one should.");

                    foreach (var entry in group)
                        rejected.Add(entry.Characteristic);
                }
            }

            // The REVERSE conflict: the SAME characteristic requested as
            // two (or more) DIFFERENT target numbers this run - e.g. two
            // distinct legacy rows both picking the same physical
            // dimension as their best match, a real risk now that the
            // matcher's tie-breaking signals are reduced (see
            // [[binspection_legacy_number_matching]]'s 2026-09-21 note).
            // Without this check, BOTH requests made it into ToApply below
            // with the SAME OldDisplayNumber (read from the characteristic
            // BEFORE anything mutates it), and whichever got applied LAST
            // silently overwrote the other's rename - the exact "matched,
            // but its number never actually changed" symptom this exists
            // to catch instead of silently mis-happening.
            foreach (var group in parsed.GroupBy(r => r.Characteristic))
            {
                List<string> distinctTargets = group.Select(r => r.NewDisplayNumber).Distinct().ToList();

                if (distinctTargets.Count > 1)
                {
                    plan.Collisions.Add(
                        "(" + group.Key.DisplayNumber + ") " + (group.Key.DimensionName ?? "") +
                        " was matched by more than one legacy row wanting different numbers (" +
                        string.Join(", ", group.Select(r => "#" + r.LegacyBalloonNumber + " -> (" + r.NewDisplayNumber + ")")) +
                        ") - resolve which one is correct.");

                    foreach (var entry in group)
                        rejected.Add(entry.Characteristic);
                }
            }

            foreach (Characteristic characteristic in alreadyCorrectCandidates)
            {
                if (!rejected.Contains(characteristic))
                    plan.AlreadyCorrect.Add(characteristic);
            }

            HashSet<Characteristic> requestedCharacteristics =
                new HashSet<Characteristic>(parsed.Select(r => r.Characteristic));

            int nextFreeCandidate = numbered.Count > 0 ? numbered.Max(c => c.Number) + 1 : 1;

            foreach (var request in parsed)
            {
                if (rejected.Contains(request.Characteristic))
                    continue;

                string oldDisplayNumber = request.Characteristic.DisplayNumber;

                Characteristic blocker;

                if (ownerOfDisplayNumber.TryGetValue(request.NewDisplayNumber, out blocker) &&
                    !ReferenceEquals(blocker, request.Characteristic))
                {
                    if (requestedCharacteristics.Contains(blocker) && !rejected.Contains(blocker))
                    {
                        // blocker is getting its own legacy-driven rename
                        // this run too - it will vacate this number on its
                        // own turn, so just let this request take it now.
                        ownerOfDisplayNumber.Remove(request.NewDisplayNumber);
                    }
                    else
                    {
                        // An unrelated balloon with no legacy number of its
                        // own is sitting on the number this request needs -
                        // move it out of the way rather than give up, since
                        // that overlap is the normal case when converting a
                        // whole project rather than a real problem.
                        string bumpedOldDisplayNumber = blocker.DisplayNumber;

                        while (ownerOfDisplayNumber.ContainsKey(nextFreeCandidate.ToString()))
                            nextFreeCandidate++;

                        string bumpedNewDisplayNumber = nextFreeCandidate.ToString();
                        nextFreeCandidate++;

                        ownerOfDisplayNumber.Remove(bumpedOldDisplayNumber);
                        ownerOfDisplayNumber[bumpedNewDisplayNumber] = blocker;

                        plan.ToApply.Add(new RenumberPlanEntry
                        {
                            Characteristic = blocker,
                            OldDisplayNumber = bumpedOldDisplayNumber,
                            NewNumber = int.Parse(bumpedNewDisplayNumber),
                            NewSubNumber = null,
                            NewDisplayNumber = bumpedNewDisplayNumber,
                            IsDisplaced = true,
                        });
                    }
                }

                ownerOfDisplayNumber.Remove(oldDisplayNumber);
                ownerOfDisplayNumber[request.NewDisplayNumber] = request.Characteristic;

                plan.ToApply.Add(new RenumberPlanEntry
                {
                    Characteristic = request.Characteristic,
                    OldDisplayNumber = oldDisplayNumber,
                    NewNumber = request.NewNumber,
                    NewSubNumber = request.NewSubNumber,
                    NewDisplayNumber = request.NewDisplayNumber,
                    IsDisplaced = false,
                });
            }

            return plan;
        }

        // Renames each planned balloon's live text on the drawing, then
        // updates the matching Characteristic's Number/SubNumber. Resolves
        // every source Note BEFORE changing any of them (rather than
        // interleaving find-and-rename per entry), so two balloons briefly
        // sharing the same displayed text mid-batch can never make a later
        // lookup in this same call ambiguous. Caller is responsible for
        // persisting the characteristics afterward.
        public static void Apply(ModelDoc2 model, RenumberPlan plan, BalloonManager balloonManager)
        {
            // Computed ONCE for this whole batch and shared by every
            // FindExistingBalloon call below - that call used to have no
            // cache at all, so it fell back to an unscoped
            // DrawingSheetHelper.GetAllViewsBySheet walk (activating every
            // sheet in the drawing) once PER ENTRY. Converting a whole
            // legacy project routinely renumbers most of the balloons on a
            // drawing, so that meant cycling through every sheet dozens of
            // times over for one Apply call - same class of bug already
            // fixed for Create/Refresh/Restore Balloons.
            //
            // Scoped to just the sheets this plan's entries actually live
            // on (their own Characteristic.SheetName), not the whole
            // drawing - a legacy conversion run only ever touches balloons
            // already known to belong to specific sheets. Falls back to a
            // full unscoped walk (old behavior, still correct, just not
            // optimized) if ANY entry's sheet is unknown - an older
            // characteristic that predates SheetName tracking (see
            // Models/Characteristics.cs remarks) could live on any sheet,
            // and guessing wrong would silently leave its balloon's live
            // text stale while its JSON number still changed underneath it.
            DrawingDoc drawing = model as DrawingDoc;

            List<string> entrySheetNames = plan.ToApply
                .Select(entry => entry.Characteristic.SheetName)
                .ToList();

            bool everySheetKnown =
                entrySheetNames.All(name => !string.IsNullOrEmpty(name));

            List<DrawingSheetHelper.ViewOnSheet> viewsBySheet =
                everySheetKnown
                    ? DrawingSheetHelper.GetAllViewsBySheet(
                        drawing, entrySheetNames.Distinct(StringComparer.OrdinalIgnoreCase))
                    : DrawingSheetHelper.GetAllViewsBySheet(drawing);

            Dictionary<RenumberPlanEntry, Note> notesByEntry = new Dictionary<RenumberPlanEntry, Note>();

            foreach (RenumberPlanEntry entry in plan.ToApply)
            {
                notesByEntry[entry] =
                    balloonManager.FindExistingBalloon(model, entry.OldDisplayNumber, viewsBySheet);
            }

            bool anyNoteChanged = false;

            foreach (RenumberPlanEntry entry in plan.ToApply)
            {
                Note note = notesByEntry[entry];

                if (note != null)
                {
                    note.SetText(entry.NewDisplayNumber);
                    anyNoteChanged = true;
                }

                entry.Characteristic.Number = entry.NewNumber;
                entry.Characteristic.SubNumber = entry.NewSubNumber;
            }

            // The note here was found by walking the annotation list -
            // SolidWorks doesn't repaint an annotation whose text changed
            // via the API unless something tells it to, so without this the
            // balloon's on-screen text stays stale until the next
            // unrelated redraw (e.g. deleting and recreating the balloon,
            // which forces one as a side effect of the new geometry).
            if (anyNoteChanged)
                model.GraphicsRedraw2();
        }
    }
}
