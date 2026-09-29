using System;
using System.Collections.Generic;
using System.Linq;

namespace BINSPECTION.Core
{
    // Default values the Add Hole Callout dialog fills in when a size is
    // picked - e.g. pick "1/4-20 UNC" and the #7 (.201) tap drill fills in;
    // pick "1/4" for a counterbore and the .281 clearance drill plus the
    // .438 x .250 socket-head counterbore fill in. Every value is only a
    // starting point: the dialog leaves every field editable.
    //
    // Sources (common shop-chart values, not a single normative standard):
    // tap drills are the usual ~75% thread drills; inch clearance/
    // counterbore are the widely published socket head cap screw chart
    // (normal fit clearance, counterbore dia "A" / depth = screw dia);
    // inch countersink dia is the 82 degree flat head machine screw max
    // head dia; metric clearance is ISO 273 medium, counterbore DIN 974-1
    // for ISO 4762, countersink is the ISO 10642 90 degree head dia.
    // Spotface defaults to the counterbore diameter at a shallow depth.
    //
    // Values are in the size's OWN units (IsMetric: mm, else inches);
    // HoleCalloutBuilder converts them to the drawing's units.
    public class ScrewSize
    {
        public string Name { get; set; }
        public bool IsMetric { get; set; }
        public double Major { get; set; }
        public double CloseClearance { get; set; }
        public double NormalClearance { get; set; }
        public double CounterboreDiameter { get; set; }
        public double CounterboreDepth { get; set; }
        public double CountersinkDiameter { get; set; }
        public double CountersinkAngle { get; set; }
        public double SpotfaceDepth { get; set; }
    }

    public class ThreadSize
    {
        public string Designation { get; set; }
        public ScrewSize Screw { get; set; }
        public string TapDrillName { get; set; }
        public double TapDrill { get; set; }
        public string DefaultClass { get; set; }
    }

    public static class HoleSizeTable
    {
        public static readonly IReadOnlyList<ScrewSize> ScrewSizes;

        public static readonly IReadOnlyList<ThreadSize> ThreadSizes;

        static HoleSizeTable()
        {
            List<ScrewSize> screws = new List<ScrewSize>
            {
                //      name    major   close   normal  cbore   cdepth  csink
                Inch("#2",   .086, .094,  .1015, .188, .086, .172),
                Inch("#4",   .112, .120,  .1285, .219, .112, .225),
                Inch("#5",   .125, .1406, .1495, .250, .125, .252),
                Inch("#6",   .138, .154,  .1695, .281, .138, .279),
                Inch("#8",   .164, .180,  .1935, .344, .164, .332),
                Inch("#10",  .190, .2055, .221,  .406, .190, .385),
                Inch("1/4",  .250, .266,  .281,  .438, .250, .507),
                Inch("5/16", .3125, .328, .344,  .531, .3125, .635),
                Inch("3/8",  .375, .391,  .406,  .625, .375, .762),
                Inch("7/16", .4375, .453, .469,  .719, .4375, .812),
                Inch("1/2",  .500, .516,  .531,  .812, .500, .875),
                Inch("5/8",  .625, .641,  .656, 1.000, .625, 1.125),
                Inch("3/4",  .750, .766,  .781, 1.188, .750, 1.375),

                Metric("M2",   2.0,  2.2,  2.4,  4.4,  2.2,  4.4),
                Metric("M2.5", 2.5,  2.7,  2.9,  5.4,  2.7,  5.5),
                Metric("M3",   3.0,  3.2,  3.4,  6.5,  3.3,  6.7),
                Metric("M4",   4.0,  4.3,  4.5,  8.0,  4.4,  9.0),
                Metric("M5",   5.0,  5.3,  5.5, 10.0,  5.4, 11.2),
                Metric("M6",   6.0,  6.4,  6.6, 11.0,  6.5, 13.4),
                Metric("M8",   8.0,  8.4,  9.0, 15.0,  8.6, 17.9),
                Metric("M10", 10.0, 10.5, 11.0, 18.0, 10.8, 22.4),
                Metric("M12", 12.0, 13.0, 13.5, 20.0, 13.0, 26.9),
                Metric("M16", 16.0, 17.0, 17.5, 26.0, 17.5, 33.6),
                Metric("M20", 20.0, 21.0, 22.0, 33.0, 21.5, 40.3),
            };

            ScrewSizes = screws;

            ScrewSize S(string name) => screws.First(s => s.Name == name);

            ThreadSizes = new List<ThreadSize>
            {
                Thread("#2-56 UNC",   S("#2"),   "#50",  .0700),
                Thread("#2-64 UNF",   S("#2"),   "#50",  .0700),
                Thread("#4-40 UNC",   S("#4"),   "#43",  .0890),
                Thread("#4-48 UNF",   S("#4"),   "#42",  .0935),
                Thread("#5-40 UNC",   S("#5"),   "#38",  .1015),
                Thread("#6-32 UNC",   S("#6"),   "#36",  .1065),
                Thread("#6-40 UNF",   S("#6"),   "#33",  .1130),
                Thread("#8-32 UNC",   S("#8"),   "#29",  .1360),
                Thread("#8-36 UNF",   S("#8"),   "#29",  .1360),
                Thread("#10-24 UNC",  S("#10"),  "#25",  .1495),
                Thread("#10-32 UNF",  S("#10"),  "#21",  .1590),
                Thread("1/4-20 UNC",  S("1/4"),  "#7",   .2010),
                Thread("1/4-28 UNF",  S("1/4"),  "#3",   .2130),
                Thread("5/16-18 UNC", S("5/16"), "F",    .2570),
                Thread("5/16-24 UNF", S("5/16"), "I",    .2720),
                Thread("3/8-16 UNC",  S("3/8"),  "5/16", .3125),
                Thread("3/8-24 UNF",  S("3/8"),  "Q",    .3320),
                Thread("7/16-14 UNC", S("7/16"), "U",    .3680),
                Thread("7/16-20 UNF", S("7/16"), "25/64", .3906),
                Thread("1/2-13 UNC",  S("1/2"),  "27/64", .4219),
                Thread("1/2-20 UNF",  S("1/2"),  "29/64", .4531),
                Thread("5/8-11 UNC",  S("5/8"),  "17/32", .5312),
                Thread("5/8-18 UNF",  S("5/8"),  "37/64", .5781),
                Thread("3/4-10 UNC",  S("3/4"),  "21/32", .6562),
                Thread("3/4-16 UNF",  S("3/4"),  "11/16", .6875),

                Thread("M2x0.4",   S("M2"),   "1.6",  1.6),
                Thread("M2.5x0.45", S("M2.5"), "2.05", 2.05),
                Thread("M3x0.5",   S("M3"),   "2.5",  2.5),
                Thread("M4x0.7",   S("M4"),   "3.3",  3.3),
                Thread("M5x0.8",   S("M5"),   "4.2",  4.2),
                Thread("M6x1.0",   S("M6"),   "5.0",  5.0),
                Thread("M8x1.25",  S("M8"),   "6.8",  6.8),
                Thread("M10x1.5",  S("M10"),  "8.5",  8.5),
                Thread("M12x1.75", S("M12"),  "10.2", 10.2),
                Thread("M16x2.0",  S("M16"),  "14.0", 14.0),
                Thread("M20x2.5",  S("M20"),  "17.5", 17.5),
            };
        }

        public static ThreadSize FindThread(string designation) =>
            ThreadSizes.FirstOrDefault(t => string.Equals(t.Designation, designation, StringComparison.OrdinalIgnoreCase));

        public static ScrewSize FindScrew(string name) =>
            ScrewSizes.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

        // The thread whose tap drill is closest to a measured hole diameter
        // (inches or mm per `metersPerUnit`), within 3% - a STEP import's
        // plain hole is most often modeled at the tap drill size. Null when
        // nothing is close enough to be a believable guess.
        public static ThreadSize ClosestThreadByTapDrill(double measured, double metersPerUnit) =>
            Closest(ThreadSizes, t => ToUnits(t.TapDrill, t.Screw.IsMetric, metersPerUnit), measured);

        // Same idea for a clearance hole: the screw whose close or normal
        // clearance drill is nearest the measured diameter.
        public static ScrewSize ClosestScrewByClearance(double measured, double metersPerUnit) =>
            Closest(ScrewSizes, s => ToUnits(s.NormalClearance, s.IsMetric, metersPerUnit), measured) ??
            Closest(ScrewSizes, s => ToUnits(s.CloseClearance, s.IsMetric, metersPerUnit), measured);

        // Converts a table value (inches, or mm when isMetric) to the
        // drawing's display units.
        public static double ToUnits(double value, bool isMetric, double metersPerUnit)
        {
            double meters = isMetric ? value * 0.001 : value * 0.0254;
            return meters / metersPerUnit;
        }

        private static T Closest<T>(IEnumerable<T> items, Func<T, double> diameterOf, double measured) where T : class
        {
            if (measured <= 0)
                return null;

            T best = null;
            double bestError = double.MaxValue;

            foreach (T item in items)
            {
                double error = Math.Abs(diameterOf(item) - measured) / measured;

                if (error < bestError)
                {
                    bestError = error;
                    best = item;
                }
            }

            return bestError <= 0.03 ? best : null;
        }

        private static ScrewSize Inch(string name, double major, double close, double normal,
            double cbore, double cboreDepth, double csink) =>
            new ScrewSize
            {
                Name = name, IsMetric = false, Major = major,
                CloseClearance = close, NormalClearance = normal,
                CounterboreDiameter = cbore, CounterboreDepth = cboreDepth,
                CountersinkDiameter = csink, CountersinkAngle = 82, SpotfaceDepth = 0.02,
            };

        private static ScrewSize Metric(string name, double major, double close, double normal,
            double cbore, double cboreDepth, double csink) =>
            new ScrewSize
            {
                Name = name, IsMetric = true, Major = major,
                CloseClearance = close, NormalClearance = normal,
                CounterboreDiameter = cbore, CounterboreDepth = cboreDepth,
                CountersinkDiameter = csink, CountersinkAngle = 90, SpotfaceDepth = 0.5,
            };

        private static ThreadSize Thread(string designation, ScrewSize screw, string tapDrillName, double tapDrill) =>
            new ThreadSize
            {
                Designation = designation, Screw = screw,
                TapDrillName = tapDrillName, TapDrill = tapDrill,
                DefaultClass = screw.IsMetric ? "6H" : "2B",
            };
    }
}
