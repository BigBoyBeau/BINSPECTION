using System.Collections.Generic;

namespace BINSPECTION.Models
{
    // An optional reserved block of balloon numbers for one sheet,
    // configured per-row in UI/CreateBalloonsSheetSelectionWindow. A
    // "range of sheets" is expressed as several sheets each carrying the
    // same RangeStart/RangeEnd, rather than as its own list/grouping
    // construct - simpler to edit and re-edit in a plain per-sheet grid
    // than a separate range-builder UI.
    //
    // When Create Balloons assigns a number to a new balloon on one of
    // these sheets, it fills this range first (lowest number in
    // [RangeStart, RangeEnd] not already used anywhere in the drawing)
    // before falling back to CharacteristicManager.GetNextNumber's normal
    // "highest known number + 1" once the range is exhausted - see
    // CharacteristicManager.GetNextNumberForSheet.
    public class BalloonNumberRange
    {
        public List<string> SheetNames { get; set; } = new List<string>();

        public int RangeStart { get; set; }

        public int RangeEnd { get; set; }
    }
}
