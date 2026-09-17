using System;
using System.Collections.Generic;
using System.Linq;
using BINSPECTION.Models;

namespace BINSPECTION.Core
{
    // Tracks which characteristic number is assigned to which dimension
    // for the CURRENTLY OPEN drawing, for the duration of one "Create
    // Balloons" run.
    //
    // This used to be an in-memory-only dictionary keyed by dimension
    // name, which is exactly why balloons renumbered every time the
    // drawing was closed and reopened: it started empty on every add-in
    // reload, so every dimension looked brand new again. It is now meant
    // to be repopulated at the start of every run from the external
    // per-drawing data file (see PersistenceManager + ReconciliationService)
    // instead of being trusted to have survived on its own.
    //
    // Keyed by SolidWorks persistent reference ID (see
    // PersistentReferenceHelper), not by dimension name, because names can
    // be changed or can collide across sheets/views, while the persistent
    // reference survives most renames and drawing edits.
    public static class CharacteristicManager
    {
        private static readonly Dictionary<string, Characteristic> Characteristics =
            new Dictionary<string, Characteristic>();

        // Wipes all in-memory tracking. Call this before loading data for
        // a (re)opened drawing, so tracking left over from a different
        // document open earlier in the same SolidWorks session can never
        // bleed into this one - CharacteristicManager is static, so
        // without this, numbers from drawing A could leak into drawing B.
        public static void Clear()
        {
            Characteristics.Clear();
        }

        public static bool HasCharacteristic(string persistentRefId)
        {
            if (string.IsNullOrEmpty(persistentRefId))
                return false;

            return Characteristics.ContainsKey(persistentRefId);
        }

        public static Characteristic GetCharacteristic(string persistentRefId)
        {
            Characteristic characteristic;

            bool found = Characteristics.TryGetValue(persistentRefId, out characteristic);

            if (found)
                return characteristic;

            return null;
        }

        public static void AddCharacteristic(Characteristic characteristic)
        {
            if (characteristic == null)
                return;

            if (string.IsNullOrEmpty(characteristic.PersistentRefId))
                return;

            if (!Characteristics.ContainsKey(characteristic.PersistentRefId))
            {
                Characteristics.Add(characteristic.PersistentRefId, characteristic);
            }
        }

        // Populates the tracker from a batch of already-resolved
        // characteristics (typically the "Matched" set coming out of
        // ReconciliationService). Does not clear existing entries first -
        // call Clear() explicitly when a clean slate is needed.
        public static void LoadFrom(IEnumerable<Characteristic> characteristics)
        {
            if (characteristics == null)
                return;

            foreach (Characteristic characteristic in characteristics)
            {
                AddCharacteristic(characteristic);
            }
        }

        // Every characteristic currently tracked, ready to be saved back
        // out to the external data file.
        public static List<Characteristic> ExportAll()
        {
            return Characteristics.Values.ToList();
        }

        // Returns the next available characteristic number.
        //
        // "reservedNumbers" should include every number that has EVER been
        // saved for this drawing - not just the ones currently tracked -
        // so a number belonging to a currently-unresolvable characteristic
        // (see ReconciliationResult.Unresolvable) can't get handed out to
        // a brand new dimension and then collide later if the original
        // dimension comes back (for example, via an Undo).
        public static int GetNextNumber(IEnumerable<int> reservedNumbers = null)
        {
            List<int> known = KnownNumbers(reservedNumbers);

            if (known.Count == 0)
                return 1;

            return known.Max() + 1;
        }

        // Same as GetNextNumber, except a sheet with a configured
        // BalloonNumberRange (see UI/CreateBalloonsSheetSelectionWindow)
        // fills that range first - lowest number in
        // [RangeStart, RangeEnd] not already used ANYWHERE in the drawing,
        // not just on this sheet, since balloon numbers are always unique
        // drawing-wide. Once every number in the range is taken, or the
        // sheet has no range configured at all, falls back to the normal
        // "highest known number + 1" behavior above.
        public static int GetNextNumberForSheet(
            string sheetName,
            IEnumerable<BalloonNumberRange> numberRanges,
            IEnumerable<int> reservedNumbers = null)
        {
            BalloonNumberRange range = numberRanges?.FirstOrDefault(
                r => r.SheetNames != null && r.SheetNames.Contains(sheetName));

            if (range != null)
            {
                HashSet<int> used = new HashSet<int>(KnownNumbers(reservedNumbers));

                for (int candidate = range.RangeStart; candidate <= range.RangeEnd; candidate++)
                {
                    if (!used.Contains(candidate))
                        return candidate;
                }

                // Range fully assigned - fall through to the normal
                // drawing-wide next number below.
            }

            return GetNextNumber(reservedNumbers);
        }

        private static List<int> KnownNumbers(IEnumerable<int> reservedNumbers)
        {
            List<int> known = Characteristics.Values
                .Select(c => c.Number)
                .ToList();

            if (reservedNumbers != null)
            {
                known.AddRange(reservedNumbers);
            }

            return known;
        }

    }
}
