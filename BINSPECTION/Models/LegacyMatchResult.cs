using System.Collections.Generic;

namespace BINSPECTION.Models
{
    // One row read from the uploaded legacy-system reference spreadsheet
    // (see Core/LegacyNumberImporter.cs) - the legacy balloon/item number
    // plus everything about its dimension the sheet actually gave us,
    // which is what Core/LegacyNumberMatcher.cs scores against this
    // drawing's live characteristics.
    public class LegacyBalloonRow
    {
        // The ACTUAL legacy balloon/item number for this row, read from
        // the spreadsheet's real ID column (e.g. "DIM #") - what
        // LegacyRenumberService turns into the live balloon's new
        // Number/SubNumber. Renamed from LegacyNumber (2026-09-21,
        // explicit user correction) - this used to be misread out of the
        // "OP <n> (<x>)" combined column instead (see OperationLabel/
        // OperationSheetNumber below), which is a COMPLETELY different
        // piece of data (operation + sheet-within-operation, not a balloon
        // number at all) and was producing wrong matches/renumbers.
        public string LegacyBalloonNumber { get; set; }

        // The operation this row belongs to (e.g. "OP 30"), when the
        // spreadsheet has a combined "OP <n> (<sheet>)" column (e.g.
        // header "OP    #") - see LegacyNumberImporter.TryParseOpItemCell.
        // Null for a spreadsheet with no such column. LegacyNumberMatcher
        // HARD-filters candidates by this - a row tagged with an operation
        // can only match a characteristic on the drawing sheet that
        // operation corresponds to, never falls back to another sheet -
        // see [[binspection_legacy_number_matching]]'s 2026-09-21 OP-
        // scoping note for why (a same-nominal dimension on a different
        // operation's sheet is a different physical feature, not a
        // near-miss).
        public string OperationLabel { get; set; }

        // The number in parentheses in that same combined column (e.g.
        // "3" in "OP 30 (3)") - per explicit user correction, this is the
        // SHEET NUMBER within OperationLabel's operation, NOT a balloon
        // number (LegacyBalloonNumber above is the real one, from a
        // completely different column). Captured for reference/debug
        // display only - nothing currently matches or filters on it.
        public string OperationSheetNumber { get; set; }

        public double Nominal { get; set; }

        // The raw text the nominal value came from, kept so the review
        // grid can show the user exactly what was in the spreadsheet
        // rather than a reformatted number, AND so LegacyNumberMatcher can
        // look for a leading diameter/radius/angle symbol in it (its own
        // best guess at "dimension type" from the legacy side - see
        // LegacyNumberMatcher.GuessLegacyFamily) - this is captured BEFORE
        // LegacyNumberImporter strips it down to a bare number, so any
        // such symbol survives here even though Nominal itself doesn't
        // carry it.
        public string RawNominalText { get; set; }

        // Upper/Lower Limit column values, read independently of which
        // column supplied Nominal (see LegacyNumberImporter.TryGetNominal)
        // so both can coexist - the tolerance RANGE these imply is its own
        // match signal (LegacyNumberMatcher), separate from Nominal itself.
        // Null when the spreadsheet has no such columns, or this row's
        // cells didn't parse.
        public double? UpperLimit { get; set; }

        public double? LowerLimit { get; set; }

        // Inspection method (e.g. "GAGE METHOD" column) and classification
        // (e.g. "CLASS" column), read alongside the nominal so a legacy
        // conversion can carry them onto the matched/created
        // Characteristic.Method/Class - see LegacyConversionWindow's
        // Apply_Click - AND so LegacyNumberMatcher can use agreement with a
        // candidate's EXISTING Method/Class as a tie-breaking match signal.
        // Null when the spreadsheet has no such column, or the cell for
        // this row is blank.
        public string Method { get; set; }

        public string Class { get; set; }
    }

    // One live characteristic considered as a possible match for a legacy
    // row, with the weighted score LegacyNumberMatcher gave it (0-100) and
    // a short human-readable reason - see LegacyNumberMatcher.ScoreCandidate.
    public class ScoredCandidate
    {
        public Characteristic Characteristic { get; set; }

        public double Score { get; set; }

        public string Reason { get; set; }

        // The candidate's own live nominal value, read straight off the
        // resolved SolidWorks dimension (LegacyNumberMatcher.CandidateInfo.
        // Nominal) - added 2026-09-21 (explicit user request) so the review
        // grid can show what the ACTUAL drawing dimension is, next to the
        // legacy spreadsheet's own nominal, instead of only the match
        // score/reason.
        public double Nominal { get; set; }
    }

    // One legacy row's full set of candidates, ranked best-first by Score -
    // produced by Core/LegacyNumberMatcher.cs. Replaces the old strict
    // exactly-one-hit/many-hits/no-hits split: EVERY row gets a ranked
    // candidate list (possibly empty), and it's up to the caller (
    // LegacyConversionWindow) to decide, from the score, how much scrutiny
    // to suggest the user give a particular row - not this class's job.
    public class LegacyMatchEntry
    {
        public LegacyBalloonRow LegacyRow { get; set; }

        public List<ScoredCandidate> Candidates { get; set; } = new List<ScoredCandidate>();

        // The single best-scored candidate, or null if nothing scored high
        // enough to even be considered a candidate (see
        // LegacyNumberMatcher.NominalAdmissionBand).
        public Characteristic BestMatch => Candidates.Count > 0 ? Candidates[0].Characteristic : null;

        public double? BestScore => Candidates.Count > 0 ? Candidates[0].Score : (double?)null;
    }

    // Everything that came out of comparing the uploaded legacy reference
    // rows against this drawing's live characteristics, produced by
    // Core/LegacyNumberMatcher.cs. Transient - never written to disk;
    // only the resulting Characteristic.LegacyBalloonNumber assignments
    // are persisted.
    public class LegacyMatchResult
    {
        // One entry per legacy row, in the same order LegacyNumberMatcher
        // was given them.
        public List<LegacyMatchEntry> Entries { get; set; } = new List<LegacyMatchEntry>();
    }
}
