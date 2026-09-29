using System.Collections.Generic;

namespace BINSPECTION.Models
{
    public enum HoleCalloutType
    {
        Drill = 0,
        Tapped = 1,
        Counterbore = 2,
        Countersink = 3,
        Spotface = 4,
    }

    // Everything the Add Hole Callout dialog captured for one BINSPECTION-
    // authored hole callout (see Core/HoleCalloutService.cs) - stored on the
    // callout's ANCHOR Characteristic only (Characteristic.HoleCallout), so
    // Edit Hole Callout can reopen the dialog exactly as it was left and
    // regenerate both the note text and every characteristic row from it.
    //
    // Every length is in the drawing's own display units (inches or mm, see
    // Units) as the user saw/typed it - not meters - so reopening the dialog
    // round-trips the literal values, and the report/limits never have to
    // re-derive a unit conversion. Nullable depth = THRU.
    public class HoleCalloutDefinition
    {
        public HoleCalloutType Type { get; set; }

        // What was picked/typed in the dialog's Size combo ("1/4-20 UNC",
        // "M6x1.0", "1/4", or free text) - informational, the individual
        // values below are what actually drive the note and rows.
        public string SizeName { get; set; }

        // "4X" prefix when greater than 1.
        public int Quantity { get; set; } = 1;

        // "in" or "mm" - the drawing's linear units when this was created.
        public string Units { get; set; }

        public int DecimalPlaces { get; set; } = 3;

        public double? DrillDiameter { get; set; }

        public double? DrillDepth { get; set; }

        // Tapped only: e.g. "1/4-20 UNC", "M6x1.0".
        public string ThreadDesignation { get; set; }

        public string ThreadClass { get; set; }

        public double? ThreadDepth { get; set; }

        public double? CounterboreDiameter { get; set; }

        public double? CounterboreDepth { get; set; }

        // Countersink type, or Drill/Tapped with AddCountersink.
        public bool AddCountersink { get; set; }

        public double? CountersinkDiameter { get; set; }

        public double? CountersinkAngle { get; set; }

        public double? SpotfaceDiameter { get; set; }

        public double? SpotfaceDepth { get; set; }

        // Per-value tolerance overrides, keyed by HoleCalloutValueKeys. A
        // value with no entry here has no tolerance of its own, so the
        // report's sheet tolerance fallback supplies its limits.
        public List<HoleCalloutTolerance> Tolerances { get; set; } = new List<HoleCalloutTolerance>();

        // The user's chosen order of the characteristic rows, by
        // HoleCalloutValueKeys (set with Move Up/Down in the dialog). The
        // first row is the anchor (bare balloon number), the rest follow as
        // N.1, N.2... Empty = the natural order. A value not listed (e.g. a
        // countersink added later) goes after the listed ones.
        public List<string> RowOrder { get; set; } = new List<string>();

        // Diameter of the picked circular edge, in display units, when the
        // callout was created from real geometry - shown back in the dialog
        // and used to suggest the closest table size. Null for a clicked
        // point.
        public double? MeasuredDiameter { get; set; }

        // Sheet-level sketch holding the cosmetic circle(s) + center marks
        // drawn for clicked points (no hole geometry in the model), and the
        // sheet-space points (meters) + view scale it was drawn at, so an
        // edit can redraw it at the new diameter. Null/empty when the
        // callout was attached to real edges.
        public string CosmeticSketchName { get; set; }

        public List<double[]> CosmeticPoints { get; set; } = new List<double[]>();

        public double CosmeticViewScale { get; set; } = 1.0;
    }

    public class HoleCalloutTolerance
    {
        public string Key { get; set; }

        public double Plus { get; set; }

        // Stored signed (normally <= 0), same convention as
        // ReportGenerator.ReadDimensionValues' minusTolerance.
        public double Minus { get; set; }
    }

    // Stable identities for each value a hole callout can produce - used to
    // key tolerance overrides and to carry Method/Classification across an
    // edit that regenerates the rows.
    public static class HoleCalloutValueKeys
    {
        public const string Thread = "Thread";
        public const string ThreadDepth = "ThreadDepth";
        public const string DrillDiameter = "DrillDiameter";
        public const string DrillDepth = "DrillDepth";
        public const string CounterboreDiameter = "CounterboreDiameter";
        public const string CounterboreDepth = "CounterboreDepth";
        public const string CountersinkDiameter = "CountersinkDiameter";
        public const string CountersinkAngle = "CountersinkAngle";
        public const string SpotfaceDiameter = "SpotfaceDiameter";
        public const string SpotfaceDepth = "SpotfaceDepth";
    }

    // A characteristic's own value, stored at creation time for a
    // characteristic with no live SolidWorks dimension behind it (today:
    // BINSPECTION hole callout rows). ReportGenerator prefers this over
    // re-resolving a dimension - see FillTemplateSheet. Nominal null = a
    // text-only characteristic (a thread designation), which gets no
    // numeric limits.
    public class CharacteristicValue
    {
        public string Key { get; set; }

        public double? Nominal { get; set; }

        public double PlusTolerance { get; set; }

        public double MinusTolerance { get; set; }

        public int DecimalPlaces { get; set; }

        public bool IsAngular { get; set; }
    }
}
