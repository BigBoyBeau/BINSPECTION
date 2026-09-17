using System.Collections.Generic;

namespace BINSPECTION.Models
{
    // The full contents of a drawing's "<drawing>.binspection.json" sidecar
    // file (see Core/PersistenceManager.cs). Wraps the per-balloon
    // Characteristics alongside project-level setup data - which sheets are
    // being ballooned and what tolerance rules apply to each - that has
    // nothing to do with any single balloon and shouldn't be duplicated
    // onto every Characteristic record.
    //
    // Older sidecar files predate this wrapper and have a bare JSON array
    // of Characteristics as their root; PersistenceManager detects that
    // shape and migrates it into this wrapper on load.
    public class ProjectData
    {
        // Sheet names (as SolidWorks reports them) that this project is
        // actively ballooning. Sheets in the drawing that aren't in this
        // list are being left alone (e.g. a cover/notes sheet). Set solely
        // by the Create Balloons sheet picker (see
        // UI/CreateBalloonsSheetSelectionWindow) - it owns which sheets get
        // scanned/populated each run; Sheet Tolerance Selection only reads
        // and carries this forward, it has no UI of its own to change it.
        public List<string> ActiveSheets { get; set; } = new List<string>();

        // Optional reserved balloon-number blocks per sheet, set up in the
        // Create Balloons sheet picker (see UI/CreateBalloonsSheetSelectionWindow
        // and Models/BalloonNumberRange.cs). Remembered the same way
        // ActiveSheets is, so re-running Create Balloons doesn't require
        // re-entering the same ranges every time.
        public List<BalloonNumberRange> NumberRanges { get; set; } = new List<BalloonNumberRange>();

        // The named tolerance rule sets available to assign to sheets
        // (e.g. "Sheet Tolerance 1", "Sheet Tolerance 2").
        public List<SheetToleranceSet> ToleranceSets { get; set; } = new List<SheetToleranceSet>();

        // Sheet name -> SheetToleranceSet.Name. A sheet with no entry here
        // has no tolerance set assigned yet.
        public Dictionary<string, string> SheetToleranceAssignments { get; set; } = new Dictionary<string, string>();

        public List<Characteristic> Characteristics { get; set; } = new List<Characteristic>();

        // Inspection-record header fields (Customer, Lot Size, Sample
        // Size, etc.) the report template has no SolidWorks-sourced value
        // for - editable in the Sheet Tolerance Selection window, per-
        // project, same as ToleranceSets above.
        public ReportHeaderSettings ReportHeaderSettings { get; set; } = new ReportHeaderSettings();

        // The Classification/Inspection Method choices offered in Balloon
        // Manager's Method/Classification combo boxes (see
        // UI/BalloonManagerWindow), seeded with BINSPECTION's defaults
        // the first time a project is loaded and grown from there whenever
        // the user types a new value - per-project, same as ToleranceSets,
        // rather than a single global list every project shares.
        public List<string> ClassOptions { get; set; } = new List<string> { "Minor", "Major", "Critical", "Key" };

        public List<string> MethodOptions { get; set; } = new List<string>
        {
            "Air Gage", "Bolt", "Bore Gage", "Calculator", "Calipers", "Calipers/Calculate",
            "CMM", "Comparator", "Comparator/Pin", "Con Tracer", "Federal", "Form Scan",
            "Function Gage", "Gage", "Gage Ball", "Gage Ball/Micro-Hite", "Gage Blocks",
            "Gage Blocks/Micro-Hite", "Go Gage", "Go Gage/Pin", "Go/NoGo Gage", "Groove Mic",
            "Indicator", "Manual CMM", "Micro-Hite", "Micro-Hite/Calculator", "Micrometers",
            "Micrometers/Pin", "Micrometers/Wire", "Optical Flat", "Overlay", "Overlay/Micro-Hite",
            "Overlay/Pin", "Overwires", "Pin", "Pin Micrometers", "Pin/Bore Gage", "Pin/Calipers",
            "Pin/Equation", "Pin/Gage Blocks/Micro-Hite", "Pin/Indicator", "Pin/Micro-Hite",
            "Plug Gage", "Profilometer", "Radius Gage", "Rail Gage", "Ring Gage", "Scale",
            "Shims", "Shop Made Plug Gage", "Specimen Plate", "Sunnen", "Sunnen/Indicator",
            "Surf Analyzer", "Thickness Gage", "Thread Gage", "Thread Gage/Calipers",
            "Torque Wrench", "Visual", "Visual/Calipers", "Visual/Pin"
        };
    }
}
