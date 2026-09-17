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

            // Legacy numbers that can't be parsed into a valid balloon
            // display number at all - reported so the caller can still
            // record them as a reference-only LegacyNumber.
            public List<string> InvalidFormat { get; } = new List<string>();

            // "Legacy #X and #Y both want to become (<target>) - resolve
            // which one should." One line per target two DIFFERENT legacy
            // numbers both requested this run - the only case this plan
            // can't resolve on its own.
            public List<string> Collisions { get; } = new List<string>();
        }

        // requestedLegacyNumbers should list every characteristic getting a
        // legacy number this run. allCharacteristics is every
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
            List<(Characteristic Characteristic, string LegacyNumber, int NewNumber, int? NewSubNumber, string NewDisplayNumber)> parsed =
                new List<(Characteristic, string, int, int?, string)>();

            foreach (KeyValuePair<Characteristic, string> request in requestedLegacyNumbers)
            {
                string legacyNumber = request.Value?.Trim();

                int newNumber;
                int? newSubNumber;
                string newDisplayNumber;

                if (!TryParseDisplayNumber(legacyNumber, out newNumber, out newSubNumber, out newDisplayNumber))
                {
                    plan.InvalidFormat.Add(legacyNumber);
                    continue;
                }

                if (newDisplayNumber == request.Key.DisplayNumber)
                    continue; // already correct - nothing to do

                parsed.Add((request.Key, legacyNumber, newNumber, newSubNumber, newDisplayNumber));
            }

            // A target two DIFFERENT characteristics both asked for this
            // run is a genuine conflict between two legitimate requests -
            // not something a bystander bump can fix. Both are dropped and
            // reported; everything else proceeds.
            HashSet<Characteristic> rejected = new HashSet<Characteristic>();

            foreach (var group in parsed.GroupBy(r => r.NewDisplayNumber, StringComparer.OrdinalIgnoreCase))
            {
                List<string> groupLegacyNumbers = group.Select(r => r.LegacyNumber).ToList();

                if (groupLegacyNumbers.Count > 1)
                {
                    plan.Collisions.Add(
                        "Legacy #" + string.Join(" and #", groupLegacyNumbers) +
                        " all want to become (" + group.Key + ") - resolve which one should.");

                    foreach (var entry in group)
                        rejected.Add(entry.Characteristic);
                }
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
            Dictionary<RenumberPlanEntry, Note> notesByEntry = new Dictionary<RenumberPlanEntry, Note>();

            foreach (RenumberPlanEntry entry in plan.ToApply)
            {
                notesByEntry[entry] = balloonManager.FindExistingBalloon(model, entry.OldDisplayNumber);
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
