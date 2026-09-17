using System;
using System.Collections.Generic;
using OfficeOpenXml.Style;

namespace BINSPECTION.Core
{
    // Maps a Characteristic's free-text Class value (see ProjectData.
    // ClassOptions) to a print/report severity: a sort rank (lower sorts
    // first) and a row-highlight color, matching the legacy inspection
    // macros' Major/Critical/100% treatment (see InspectionProcessor.Core.
    // Services.HighlightingService) without hardcoding today's exact
    // ClassOptions set. ClassOptions is a per-project, user-editable list -
    // the user plans to add "Incidental" and "100%" to it later - so any
    // class name not recognized here (including null/blank, and anything
    // typed in that isn't one of the known severities) just falls to the
    // lowest rank with no highlight, rather than throwing or guessing.
    public static class SeverityClassifier
    {
        // Lower sorts first. Matches the legacy sort order
        // (100%, Critical, Major, Minor, Incidental) with today's actual
        // ClassOptions ("Key" standing in for "Incidental" until the user
        // adds the real thing) - unrecognized/unset classes rank last.
        private static readonly Dictionary<string, int> RankByClass =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "100%", 0 },
                { "Critical", 1 },
                { "Major", 2 },
                { "Minor", 3 },
                { "Key", 4 },
                { "Incidental", 4 },
            };

        private const int UnrecognizedRank = 5;

        // Legacy highlight colors (HighlightingService.ApplyStaticHighlighting):
        // Major = yellow, Critical = light orange, 100% = light red. Minor/
        // Key/Incidental/unrecognized get no fill.
        private static readonly Dictionary<string, System.Drawing.Color> FillColorByClass =
            new Dictionary<string, System.Drawing.Color>(StringComparer.OrdinalIgnoreCase)
            {
                { "100%", System.Drawing.Color.FromArgb(255, 199, 179) },
                { "Critical", System.Drawing.Color.FromArgb(255, 214, 165) },
                { "Major", System.Drawing.Color.Yellow },
            };

        public static int RankOf(string characteristicClass)
        {
            if (string.IsNullOrWhiteSpace(characteristicClass))
                return UnrecognizedRank;

            int rank;

            return RankByClass.TryGetValue(characteristicClass.Trim(), out rank)
                ? rank
                : UnrecognizedRank;
        }

        // Returns true and sets fillColor when this class should highlight
        // its report row; false (fillColor left at default) when it
        // shouldn't - callers should leave the row's fill untouched in
        // that case rather than paint it white/none, so a report row's
        // default styling is never disturbed for an unclassified row.
        public static bool TryGetFillColor(string characteristicClass, out System.Drawing.Color fillColor)
        {
            fillColor = default(System.Drawing.Color);

            if (string.IsNullOrWhiteSpace(characteristicClass))
                return false;

            return FillColorByClass.TryGetValue(characteristicClass.Trim(), out fillColor);
        }

        // Convenience for ReportGenerator: applies the fill (if any) to a
        // row range's Style.Fill, leaving the range untouched when this
        // class has no associated highlight color.
        public static void ApplyRowFill(ExcelStyle style, string characteristicClass)
        {
            System.Drawing.Color fillColor;

            if (!TryGetFillColor(characteristicClass, out fillColor))
                return;

            style.Fill.PatternType = ExcelFillStyle.Solid;
            style.Fill.BackgroundColor.SetColor(fillColor);
        }
    }
}
