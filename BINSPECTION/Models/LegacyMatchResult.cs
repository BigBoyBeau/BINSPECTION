using System.Collections.Generic;

namespace BINSPECTION.Models
{
    // One row read from the uploaded legacy-system reference spreadsheet
    // (see Core/LegacyNumberImporter.cs) - the legacy balloon/item number
    // plus the nominal dimension value it identified, which is what
    // Core/LegacyNumberMatcher.cs matches against this drawing's live
    // characteristics.
    public class LegacyBalloonRow
    {
        public string LegacyNumber { get; set; }

        public double Nominal { get; set; }

        // The raw text the nominal value came from, kept only so the
        // exception picker dialog can show the user exactly what was in
        // the spreadsheet rather than a reformatted number.
        public string RawNominalText { get; set; }

        // Inspection method (e.g. "GAGE METHOD" column) and classification
        // (e.g. "CLASS" column), read alongside the nominal so a legacy
        // conversion can carry them onto the matched/created
        // Characteristic.Method/Class - see LegacyConversionWindow's
        // Apply_Click. Null when the spreadsheet has no such column, or
        // the cell for this row is blank.
        public string Method { get; set; }

        public string Class { get; set; }
    }

    // Everything that came out of comparing the uploaded legacy reference
    // rows against this drawing's live characteristics, produced by
    // Core/LegacyNumberMatcher.cs. Transient - never written to disk;
    // only the resulting Characteristic.LegacyNumber assignments are
    // persisted.
    public class LegacyMatchResult
    {
        // Legacy rows that matched exactly one live characteristic by
        // nominal dimension value - resolved automatically, no user input
        // needed.
        public List<LegacyMatchEntry> Matched { get; set; } = new List<LegacyMatchEntry>();

        // Legacy rows whose nominal value matched more than one live
        // characteristic (e.g. two holes with the same diameter) - the
        // user has to pick which one this legacy number actually belongs
        // to.
        public List<LegacyAmbiguousEntry> Ambiguous { get; set; } = new List<LegacyAmbiguousEntry>();

        // Legacy rows whose nominal value didn't match any live
        // characteristic within tolerance - the user can assign one by
        // hand or skip the row.
        public List<LegacyBalloonRow> Unmatched { get; set; } = new List<LegacyBalloonRow>();

        public bool HasExceptions => Ambiguous.Count > 0 || Unmatched.Count > 0;
    }

    public class LegacyMatchEntry
    {
        public LegacyBalloonRow LegacyRow { get; set; }

        public Characteristic Characteristic { get; set; }
    }

    public class LegacyAmbiguousEntry
    {
        public LegacyBalloonRow LegacyRow { get; set; }

        public List<Characteristic> Candidates { get; set; } = new List<Characteristic>();
    }
}
