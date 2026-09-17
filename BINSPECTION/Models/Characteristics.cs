namespace BINSPECTION.Models
{
    // One row of persisted balloon/characteristic data.
    //
    // This is exactly what gets written to and read back from the external
    // "<drawing>.binspection.json" file that lives next to the drawing
    // (see Core/PersistenceManager.cs). That external file - not anything
    // stored inside the drawing - is the source of truth for balloon
    // numbering, which is the whole point of this class existing: if the
    // drawing's own metadata ever gets corrupted, or a balloon note gets
    // nudged or deleted, this file still remembers the real answer.
    //
    // Keep this class plain data (no SolidWorks objects, no logic) so it
    // keeps round-tripping through JSON cleanly.
    public class Characteristic
    {
        // The inspection balloon number shown on the drawing, e.g. "(7)".
        public int Number { get; set; }

        // Base64-encoded SolidWorks "persistent reference" ID for the
        // dimension's annotation (see Core/PersistentReferenceHelper.cs).
        // This is the durable key used to re-find the exact same
        // dimension the next time the drawing is opened - it survives
        // most renames and drawing edits, unlike a dimension's name.
        public string PersistentRefId { get; set; }

        // Base64-encoded SolidWorks persistent reference ID for the
        // balloon Note's OWN annotation - distinct from PersistentRefId
        // above, which points at the ballooned dimension/GD&T frame/note
        // being annotated, not at the balloon marker itself. Captured at
        // balloon-creation time (see BalloonManager.GetBalloonPersistId)
        // so a balloon can be found and removed directly by identity
        // (Core/BalloonManager.cs RemoveBalloonsByPersistId) instead of by
        // matching its displayed number as text. A sibling characteristic
        // that shares another characteristic's one physical balloon (the
        // "#<n>" synthetic PersistentRefId siblings created in
        // CommandManagerHandler.OnCreateBalloons) carries the SAME
        // BalloonPersistId as the group's first characteristic, since
        // there is only one real Note between them - removing by any one
        // group member's id removes that shared balloon. Null only for a
        // characteristic that has never had a balloon created for it.
        public string BalloonPersistId { get; set; }

        // Human-readable label only (the dimension's FullName at the time
        // it was ballooned) - NOT used for lookups. Kept purely so the
        // JSON file is easy to read/debug, and so we have something to
        // show the user if PersistentRefId ever fails to resolve.
        public string DimensionName { get; set; }

        public string Method { get; set; }

        public string Class { get; set; }

        // True if this dimension is intentionally excluded from
        // inspection - Create Balloons will never balloon it (even on a
        // future rescan) and it never shows up in the report. Number is
        // meaningless while this is true (kept at 0).
        public bool IsUnnumbered { get; set; }

        // Set when this characteristic is a numbered (non-first) member of
        // a grouped balloon, e.g. "(12.2)" -> Number = 12, SubNumber = 2.
        // Null both for a standalone balloon AND for a group's own first/
        // anchor member, which is always displayed as the bare whole
        // number (e.g. "12", never "12.1") - see BalloonGridService.
        // IsGroupMember for how "grouped" is actually detected, since
        // SubNumber alone can't distinguish a group's anchor from a
        // standalone characteristic.
        public int? SubNumber { get; set; }

        // This characteristic's own whole number from before it was folded
        // into a group - lets Ungroup Balloon restore it exactly, rather
        // than handing out a fresh number.
        public int? PreGroupNumber { get; set; }

        // The balloon/item number this same characteristic carried in the
        // legacy inspection system, if it's been matched to one - see
        // Core/LegacyNumberMatcher.cs and UI/LegacyConversionWindow.xaml.cs.
        // Null until a legacy match run has assigned it. Kept as a string
        // since legacy numbering isn't guaranteed to be a plain integer.
        public string LegacyNumber { get; set; }

        // The literal SolidWorks drawing sheet name (e.g. "Sheet1") the
        // source dimension/annotation lived on when this characteristic was
        // created - captured from the IView it was found on (see
        // DimensionScanner/AnnotationScanner's *Hit wrapper types). Null for
        // characteristics created before this field existed.
        public string SheetName { get; set; }

        // True when this dimension is marked as a SolidWorks Basic
        // dimension - theoretically exact, no tolerance of its own. Pushed
        // onto the real drawing's IDimensionTolerance.Type for a row that
        // still has a live dimension - see Core/BalloonGridService.SetBasic.
        // Read by ReportGenerator.ApplySheetToleranceFallback (a Basic
        // dimension with no real tolerance still gets a numeric Upper/Lower
        // Limit from its sheet's default rule, per explicit user request -
        // no "BASIC" text is shown anywhere in the report or in Balloon
        // Manager's Dimension cell any more). Forced true on load whenever
        // the live dimension's own tolerance type already reads as Basic,
        // even if this flag was never explicitly set.
        public bool IsBasic { get; set; }

        // The live dimension's IDimensionTolerance.Type (swTolType_e) from
        // just before SetBasic switched it to Basic - lets un-checking
        // Basic restore the dimension's original tolerance display instead
        // of always dropping it to "no tolerance". Null if it was already
        // Basic (or toleranceless) when first checked.
        public int? PreBasicToleranceType { get; set; }

        // Name of the sheet-level sketch feature (e.g. "Sketch12") that
        // draws the thin rectangle around this dimension while IsBasic is
        // true - see Core/DimensionBoxSketchService.cs and
        // BalloonGridService.SetBasic. Null whenever IsBasic is false; the
        // stored name is how un-checking Basic finds and deletes the right
        // sketch again later.
        public string BasicBoxSketchName { get; set; }

        // For a GD&T characteristic only: the real "SolidWorks GDT" font's
        // boxed feature-control-frame string (see GtolTextFormatter -
        // BuildBoxText's remarks for the font mechanism), captured once at
        // balloon-creation time alongside DimensionName. Null for every
        // non-GD&T characteristic, and for a GD&T one whose symbol/frame
        // format couldn't be mapped to a confirmed glyph (Report
        // Generator falls back to translating DimensionName's plain text
        // instead - see ReportGenerator.WriteCharacteristicCell).
        public string GdtBoxText { get; set; }

        // This GD&T frame's own tolerance-zone value (e.g. 0.004), captured
        // at balloon-creation time alongside GdtBoxText - see
        // GtolTextFormatter.GdtFrameResult.ToleranceValue's remarks. Null
        // for every non-GD&T characteristic, and for a GD&T one with no
        // numeric tolerance to report (a BASIC box) or whose text didn't
        // parse. A feature-control frame's tolerance is a one-sided zone,
        // not a +/- range - ReportGenerator uses this as Upper Limit with
        // Lower Limit forced to 0, never combined with Nominal.
        public double? GdtToleranceValue { get; set; }

        // Sheet-space X/Y/Z of this characteristic's balloon Note (see
        // IAnnotation.GetPosition/SetPosition), captured by Save Position
        // (CommandManagerHandler.OnSavePosition) so Restore Position can
        // put the balloon back exactly where it was - e.g. after Optimize
        // Balloons nudges it, or after Refresh Balloons recreates it. Null
        // until Save Position has been run at least once for this balloon.
        public double? BalloonPositionX { get; set; }

        public double? BalloonPositionY { get; set; }

        public double? BalloonPositionZ { get; set; }

        // What actually gets stamped on the balloon and matched against
        // sheet text - "12" for a standalone characteristic OR a group's
        // bare first/anchor member, "12.2" for a group's later member.
        public string DisplayNumber =>
            SubNumber.HasValue ? Number + "." + SubNumber.Value : Number.ToString();
    }
}
