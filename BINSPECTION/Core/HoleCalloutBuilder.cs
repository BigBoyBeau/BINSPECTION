using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BINSPECTION.Models;

namespace BINSPECTION.Core
{
    // One inspectable value of a hole callout - becomes one Characteristic
    // row (the first is the anchor that owns the balloon, the rest are
    // "#n" siblings, same as a Create Balloons hole callout).
    public class HoleCalloutRow
    {
        public string Key { get; set; }

        // Grid/report text, e.g. "4X ⌀ .201 THRU" - same symbol convention
        // as HoleCalloutExtractor's split segments, so a BINSPECTION-authored
        // callout reads exactly like a native one that Create Balloons split.
        public string Text { get; set; }

        public CharacteristicValue Value { get; set; }
    }

    // Pure logic, no SolidWorks calls: turns a HoleCalloutDefinition into
    // (a) the note text, written with SolidWorks' own symbol tags so the
    // note renders like a native hole callout, and (b) the characteristic
    // rows. Both come from the same definition, which is what lets Edit
    // Hole Callout regenerate the note and the rows together.
    public static class HoleCalloutBuilder
    {
        // Same tags HoleCalloutExtractor.TagLabels reads back out of native
        // callouts (MOD-DIAM/HOLE-SINK/HOLE-DEPTH confirmed live there).
        private const string DiamTag = "<MOD-DIAM>";
        private const string DepthTag = "<HOLE-DEPTH>";
        private const string SinkTag = "<HOLE-SINK>";
        private const string CboreTag = "<HOLE-CBORE>";
        private const string SpotTag = "<HOLE-SPOT>";
        private const string DegTag = "<MOD-DEG>";
        private const string PlusMinusTag = "<MOD-PM>";

        public static bool IsInch(HoleCalloutDefinition def) =>
            !string.Equals(def?.Units, "mm", StringComparison.OrdinalIgnoreCase);

        public static bool HasCountersink(HoleCalloutDefinition def) =>
            def.Type == HoleCalloutType.Countersink ||
            (def.AddCountersink && (def.Type == HoleCalloutType.Drill || def.Type == HoleCalloutType.Tapped));

        // Missing/invalid required values for the definition's type - empty
        // when it can be created.
        public static List<string> Validate(HoleCalloutDefinition def)
        {
            List<string> errors = new List<string>();

            if (def.Quantity < 1)
                errors.Add("Quantity must be at least 1.");

            RequirePositive(errors, def.DrillDiameter, def.Type == HoleCalloutType.Tapped ? "Tap drill diameter" : "Hole diameter");
            RequirePositiveOrThru(errors, def.DrillDepth, "Hole depth");

            if (def.Type == HoleCalloutType.Tapped)
            {
                if (string.IsNullOrWhiteSpace(def.ThreadDesignation))
                    errors.Add("Thread designation is required.");

                RequirePositiveOrThru(errors, def.ThreadDepth, "Thread depth");
            }

            if (def.Type == HoleCalloutType.Counterbore)
            {
                RequirePositive(errors, def.CounterboreDiameter, "Counterbore diameter");
                RequirePositive(errors, def.CounterboreDepth, "Counterbore depth");
            }

            if (HasCountersink(def))
            {
                RequirePositive(errors, def.CountersinkDiameter, "Countersink diameter");
                RequirePositive(errors, def.CountersinkAngle, "Countersink angle");
            }

            if (def.Type == HoleCalloutType.Spotface)
            {
                RequirePositive(errors, def.SpotfaceDiameter, "Spotface diameter");
                RequirePositive(errors, def.SpotfaceDepth, "Spotface depth");
            }

            return errors;
        }

        public static List<HoleCalloutRow> BuildRows(HoleCalloutDefinition def)
        {
            List<HoleCalloutRow> rows = new List<HoleCalloutRow>();
            string qty = QuantityPrefix(def);

            if (def.Type == HoleCalloutType.Tapped)
            {
                rows.Add(new HoleCalloutRow
                {
                    Key = HoleCalloutValueKeys.Thread,
                    Text = qty + ThreadText(def) + (def.ThreadDepth.HasValue ? "" : " THRU"),
                    Value = new CharacteristicValue { Key = HoleCalloutValueKeys.Thread, Nominal = null },
                });

                if (def.ThreadDepth.HasValue)
                    rows.Add(LengthRow(def, HoleCalloutValueKeys.ThreadDepth, "▽", def.ThreadDepth.Value, qty));
            }

            HoleCalloutRow drill = LengthRow(def, HoleCalloutValueKeys.DrillDiameter, "⌀", def.DrillDiameter ?? 0, qty);

            if (!def.DrillDepth.HasValue)
                drill.Text += " THRU";

            rows.Add(drill);

            if (def.DrillDepth.HasValue)
                rows.Add(LengthRow(def, HoleCalloutValueKeys.DrillDepth, "▽", def.DrillDepth.Value, qty));

            if (def.Type == HoleCalloutType.Counterbore)
            {
                rows.Add(LengthRow(def, HoleCalloutValueKeys.CounterboreDiameter, "⌴⌀", def.CounterboreDiameter ?? 0, qty));
                rows.Add(LengthRow(def, HoleCalloutValueKeys.CounterboreDepth, "▽", def.CounterboreDepth ?? 0, qty));
            }

            if (HasCountersink(def))
            {
                rows.Add(LengthRow(def, HoleCalloutValueKeys.CountersinkDiameter, "⌵⌀", def.CountersinkDiameter ?? 0, qty));

                double angle = def.CountersinkAngle ?? 0;
                int angleDecimals = AngleDecimals(angle);
                HoleCalloutTolerance angleTol = ToleranceFor(def, HoleCalloutValueKeys.CountersinkAngle);

                rows.Add(new HoleCalloutRow
                {
                    Key = HoleCalloutValueKeys.CountersinkAngle,
                    Text = qty + angle.ToString("F" + angleDecimals, CultureInfo.InvariantCulture) + "°",
                    Value = new CharacteristicValue
                    {
                        Key = HoleCalloutValueKeys.CountersinkAngle,
                        Nominal = angle,
                        PlusTolerance = angleTol?.Plus ?? 0,
                        MinusTolerance = angleTol?.Minus ?? 0,
                        DecimalPlaces = angleDecimals,
                        IsAngular = true,
                    },
                });
            }

            if (def.Type == HoleCalloutType.Spotface)
            {
                rows.Add(LengthRow(def, HoleCalloutValueKeys.SpotfaceDiameter, "SF⌀", def.SpotfaceDiameter ?? 0, qty));
                rows.Add(LengthRow(def, HoleCalloutValueKeys.SpotfaceDepth, "▽", def.SpotfaceDepth ?? 0, qty));
            }

            return ApplyRowOrder(rows, def.RowOrder);
        }

        // One value's piece of the note text, on one callout line.
        private class NoteSegment
        {
            public int Line;         // which callout line (thread, hole, cbore, csink, spot)
            public string Key;       // HoleCalloutValueKeys - drives the ordering
            public string Text;
            public string JoinBefore = " "; // separator when it isn't first on its line
        }

        // The note's text, one callout line per feature, following the
        // user's row order (Move Up/Down in the dialog): lines print in the
        // order their first value appears in that list, and the values on a
        // line print in that same order. With no custom order this is the
        // standard layout, e.g. for a tapped + countersunk hole:
        //   4X 1/4-20 UNC-2B <HOLE-DEPTH>.375
        //   <MOD-DIAM>.201 <HOLE-DEPTH>.500
        //   <HOLE-SINK><MOD-DIAM>.520 X 82<MOD-DEG>
        // A value only ever moves within its own line - a depth stays with
        // its diameter, the countersink angle with its diameter - so every
        // line still reads as a real callout. The quantity ("4X") goes on
        // whichever line prints first. THRU has no row of its own; it rides
        // on its diameter/thread segment.
        public static string BuildNoteText(HoleCalloutDefinition def)
        {
            const int threadLine = 0, holeLine = 1, cboreLine = 2, sinkLine = 3, spotLine = 4;

            List<NoteSegment> segments = new List<NoteSegment>();

            if (def.Type == HoleCalloutType.Tapped)
            {
                segments.Add(new NoteSegment
                {
                    Line = threadLine, Key = HoleCalloutValueKeys.Thread,
                    Text = ThreadText(def) + (def.ThreadDepth.HasValue ? "" : " THRU"),
                });

                if (def.ThreadDepth.HasValue)
                    segments.Add(DepthSegment(def, threadLine, HoleCalloutValueKeys.ThreadDepth, def.ThreadDepth.Value));
            }

            segments.Add(new NoteSegment
            {
                Line = holeLine, Key = HoleCalloutValueKeys.DrillDiameter,
                Text = DiamTag + Length(def, def.DrillDiameter ?? 0, HoleCalloutValueKeys.DrillDiameter) +
                       (def.DrillDepth.HasValue ? "" : " THRU"),
            });

            if (def.DrillDepth.HasValue)
                segments.Add(DepthSegment(def, holeLine, HoleCalloutValueKeys.DrillDepth, def.DrillDepth.Value));

            if (def.Type == HoleCalloutType.Counterbore)
            {
                segments.Add(new NoteSegment
                {
                    Line = cboreLine, Key = HoleCalloutValueKeys.CounterboreDiameter,
                    Text = CboreTag + DiamTag + Length(def, def.CounterboreDiameter ?? 0, HoleCalloutValueKeys.CounterboreDiameter),
                });
                segments.Add(DepthSegment(def, cboreLine, HoleCalloutValueKeys.CounterboreDepth, def.CounterboreDepth ?? 0));
            }

            if (HasCountersink(def))
            {
                double angle = def.CountersinkAngle ?? 0;

                segments.Add(new NoteSegment
                {
                    Line = sinkLine, Key = HoleCalloutValueKeys.CountersinkDiameter, JoinBefore = " X ",
                    Text = SinkTag + DiamTag + Length(def, def.CountersinkDiameter ?? 0, HoleCalloutValueKeys.CountersinkDiameter),
                });
                segments.Add(new NoteSegment
                {
                    Line = sinkLine, Key = HoleCalloutValueKeys.CountersinkAngle, JoinBefore = " X ",
                    Text = angle.ToString("F" + AngleDecimals(angle), CultureInfo.InvariantCulture) + DegTag +
                           ToleranceText(def, HoleCalloutValueKeys.CountersinkAngle, AngleDecimals(angle), true),
                });
            }

            if (def.Type == HoleCalloutType.Spotface)
            {
                segments.Add(new NoteSegment
                {
                    Line = spotLine, Key = HoleCalloutValueKeys.SpotfaceDiameter,
                    Text = SpotTag + DiamTag + Length(def, def.SpotfaceDiameter ?? 0, HoleCalloutValueKeys.SpotfaceDiameter),
                });
                segments.Add(DepthSegment(def, spotLine, HoleCalloutValueKeys.SpotfaceDepth, def.SpotfaceDepth ?? 0));
            }

            // Rank every value by where it sits in the (user-ordered) rows.
            List<string> rowOrder = BuildRows(def).Select(r => r.Key).ToList();

            int Rank(NoteSegment s)
            {
                int index = rowOrder.IndexOf(s.Key);
                return index < 0 ? int.MaxValue : index;
            }

            List<string> lines = segments
                .GroupBy(s => s.Line)
                .OrderBy(g => g.Min(Rank))
                .ThenBy(g => g.Key)
                .Select(g =>
                {
                    List<NoteSegment> ordered = g.OrderBy(Rank).ToList();
                    string line = ordered[0].Text;

                    for (int i = 1; i < ordered.Count; i++)
                        line += ordered[i].JoinBefore + ordered[i].Text;

                    return line;
                })
                .ToList();

            if (lines.Count > 0)
                lines[0] = QuantityPrefix(def) + lines[0];

            return string.Join(System.Environment.NewLine, lines);
        }

        private static NoteSegment DepthSegment(HoleCalloutDefinition def, int line, string key, double depth) =>
            new NoteSegment { Line = line, Key = key, Text = DepthTag + Length(def, depth, key) };

        // Overwrites the definition's values with a thread's table
        // defaults: tap drill, class, and depths of 1.5x / 2x the major
        // diameter (a blind tapped hole's usual starting point).
        public static void ApplyThreadDefaults(HoleCalloutDefinition def, ThreadSize thread, double metersPerUnit)
        {
            double major = HoleSizeTable.ToUnits(thread.Screw.Major, thread.Screw.IsMetric, metersPerUnit);

            def.SizeName = thread.Designation;
            def.ThreadDesignation = thread.Designation;
            def.ThreadClass = thread.DefaultClass;
            def.DrillDiameter = Round(def, HoleSizeTable.ToUnits(thread.TapDrill, thread.Screw.IsMetric, metersPerUnit));
            def.ThreadDepth = Round(def, major * 1.5);
            def.DrillDepth = Round(def, major * 2.0);

            ApplyHeadDefaults(def, thread.Screw, metersPerUnit);
        }

        // Clearance-hole defaults for a screw size: normal-fit clearance
        // drill THRU, plus the counterbore/countersink/spotface for that
        // screw.
        public static void ApplyScrewDefaults(HoleCalloutDefinition def, ScrewSize screw, double metersPerUnit)
        {
            def.SizeName = screw.Name;
            def.DrillDiameter = Round(def, HoleSizeTable.ToUnits(screw.NormalClearance, screw.IsMetric, metersPerUnit));
            def.DrillDepth = null;

            ApplyHeadDefaults(def, screw, metersPerUnit);
        }

        private static void ApplyHeadDefaults(HoleCalloutDefinition def, ScrewSize screw, double metersPerUnit)
        {
            def.CounterboreDiameter = Round(def, HoleSizeTable.ToUnits(screw.CounterboreDiameter, screw.IsMetric, metersPerUnit));
            def.CounterboreDepth = Round(def, HoleSizeTable.ToUnits(screw.CounterboreDepth, screw.IsMetric, metersPerUnit));
            def.CountersinkDiameter = Round(def, HoleSizeTable.ToUnits(screw.CountersinkDiameter, screw.IsMetric, metersPerUnit));
            def.CountersinkAngle = screw.CountersinkAngle;
            def.SpotfaceDiameter = def.CounterboreDiameter;
            def.SpotfaceDepth = Round(def, HoleSizeTable.ToUnits(screw.SpotfaceDepth, screw.IsMetric, metersPerUnit));
        }

        // Formats a length the way the drawing prints it: fixed decimal
        // places, and inch values without the leading zero (".201"), which
        // is how this shop's drawings read (see HoleCalloutExtractor's live
        // "<MOD-DIAM> .1 <HOLE-DEPTH> .35" dump).
        public static string FormatLength(HoleCalloutDefinition def, double value)
        {
            string text = value.ToString("F" + Math.Max(0, def.DecimalPlaces), CultureInfo.InvariantCulture);

            if (IsInch(def) && text.StartsWith("0.", StringComparison.Ordinal))
                text = text.Substring(1);

            return text;
        }

        public static HoleCalloutTolerance ToleranceFor(HoleCalloutDefinition def, string key) =>
            def.Tolerances?.FirstOrDefault(t => t.Key == key && (t.Plus != 0 || t.Minus != 0));

        // Largest diameter shown on the sheet - what a cosmetic circle is
        // drawn at (the counterbore/countersink/spotface outline if any,
        // plus the hole itself).
        public static List<double> CosmeticDiameters(HoleCalloutDefinition def)
        {
            List<double> diameters = new List<double>();

            if (def.DrillDiameter > 0)
                diameters.Add(def.DrillDiameter.Value);

            if (def.Type == HoleCalloutType.Counterbore && def.CounterboreDiameter > 0)
                diameters.Add(def.CounterboreDiameter.Value);

            if (HasCountersink(def) && def.CountersinkDiameter > 0)
                diameters.Add(def.CountersinkDiameter.Value);

            if (def.Type == HoleCalloutType.Spotface && def.SpotfaceDiameter > 0)
                diameters.Add(def.SpotfaceDiameter.Value);

            return diameters;
        }

        // Sorts rows by the user's saved order; rows it doesn't mention keep
        // their natural relative order after the listed ones (stable sort).
        private static List<HoleCalloutRow> ApplyRowOrder(List<HoleCalloutRow> rows, List<string> order)
        {
            if (order == null || order.Count == 0)
                return rows;

            return rows
                .Select((row, naturalIndex) => new { row, naturalIndex, rank = order.IndexOf(row.Key) })
                .OrderBy(x => x.rank < 0 ? order.Count + x.naturalIndex : x.rank)
                .Select(x => x.row)
                .ToList();
        }

        private static HoleCalloutRow LengthRow(HoleCalloutDefinition def, string key, string symbol, double value, string qty)
        {
            HoleCalloutTolerance tol = ToleranceFor(def, key);

            return new HoleCalloutRow
            {
                Key = key,
                Text = qty + symbol + " " + FormatLength(def, value),
                Value = new CharacteristicValue
                {
                    Key = key,
                    Nominal = value,
                    PlusTolerance = tol?.Plus ?? 0,
                    MinusTolerance = tol?.Minus ?? 0,
                    DecimalPlaces = def.DecimalPlaces,
                    IsAngular = false,
                },
            };
        }

        private static string ThreadText(HoleCalloutDefinition def)
        {
            string thread = (def.ThreadDesignation ?? "").Trim();
            string threadClass = (def.ThreadClass ?? "").Trim();

            return threadClass.Length > 0 ? thread + "-" + threadClass : thread;
        }

        private static string Length(HoleCalloutDefinition def, double value, string key) =>
            FormatLength(def, value) + ToleranceText(def, key, def.DecimalPlaces, false);

        // Only a value with its own override prints a tolerance on the
        // note - everything else is covered by the sheet tolerance block,
        // exactly like an untoleranced dimension.
        private static string ToleranceText(HoleCalloutDefinition def, string key, int decimals, bool angular)
        {
            HoleCalloutTolerance tol = ToleranceFor(def, key);

            if (tol == null)
                return "";

            string Fmt(double v)
            {
                if (angular)
                    return v.ToString("F" + decimals, CultureInfo.InvariantCulture) + DegTag;

                string text = v.ToString("F" + Math.Max(0, decimals), CultureInfo.InvariantCulture);
                return IsInch(def) && text.StartsWith("0.", StringComparison.Ordinal) ? text.Substring(1) : text;
            }

            if (Math.Abs(tol.Plus + tol.Minus) < 1e-12)
                return PlusMinusTag + Fmt(tol.Plus);

            return " +" + Fmt(tol.Plus) + "/-" + Fmt(Math.Abs(tol.Minus));
        }

        private static string QuantityPrefix(HoleCalloutDefinition def) =>
            def.Quantity > 1 ? def.Quantity + "X " : "";

        private static int AngleDecimals(double angle) =>
            Math.Abs(angle - Math.Round(angle)) < 1e-9 ? 0 : 1;

        private static double Round(HoleCalloutDefinition def, double value) =>
            Math.Round(value, Math.Max(0, def.DecimalPlaces), MidpointRounding.AwayFromZero);

        private static void RequirePositive(List<string> errors, double? value, string name)
        {
            if (!value.HasValue || value.Value <= 0)
                errors.Add(name + " must be a positive number.");
        }

        private static void RequirePositiveOrThru(List<string> errors, double? value, string name)
        {
            if (value.HasValue && value.Value <= 0)
                errors.Add(name + " must be a positive number (or check THRU).");
        }
    }
}
