using System.Collections.ObjectModel;

namespace BINSPECTION.Models
{
    // A named group of tolerance rules a sheet can be assigned to, e.g.
    // "Sheet Tolerance 1" covering that sheet's 1/2/3-decimal linear and
    // 1/2-decimal angular tolerances. Split out from Characteristic on
    // purpose: this is project/sheet-level setup data, not something that
    // belongs on individual balloons.
    //
    // Entries is an ObservableCollection (rather than a plain List) so the
    // Sheet Tolerance Selection window's entries grid updates live as rows
    // are added/removed - it still round-trips through JSON identically to
    // a List<T>.
    public class SheetToleranceSet
    {
        public string Name { get; set; }

        public ObservableCollection<ToleranceEntry> Entries { get; set; } = new ObservableCollection<ToleranceEntry>();

        public static SheetToleranceSet CreateDefault(string name)
        {
            return new SheetToleranceSet
            {
                Name = name,
                Entries = new ObservableCollection<ToleranceEntry>
                {
                    new ToleranceEntry { DecimalPlaces = 1, IsAngular = false, Value = 0.1 },
                    new ToleranceEntry { DecimalPlaces = 2, IsAngular = false, Value = 0.01 },
                    new ToleranceEntry { DecimalPlaces = 3, IsAngular = false, Value = 0.001 },
                    new ToleranceEntry { DecimalPlaces = 1, IsAngular = true, Value = 0.1 },
                    new ToleranceEntry { DecimalPlaces = 2, IsAngular = true, Value = 0.01 },
                }
            };
        }
    }

    // One row of a SheetToleranceSet, e.g. "2 Decimal Place x.xx - 0.01" or
    // "1 Decimal Angular x.x - 0.1 deg".
    public class ToleranceEntry
    {
        public int DecimalPlaces { get; set; }

        public bool IsAngular { get; set; }

        public double Value { get; set; }

        // Display-only label built from the other fields - not used for
        // lookups, just so the JSON and any UI reading it are easy to
        // follow (mirrors Characteristic.DisplayNumber's role).
        public string Label
        {
            get
            {
                string places = new string('x', DecimalPlaces - 1);
                string format = DecimalPlaces > 0
                    ? "x." + places + "x"
                    : "x";

                string kind = IsAngular ? "Decimal Angular" : "Decimal Place";
                string unit = IsAngular ? " deg" : "";

                return $"{DecimalPlaces} {kind} {format} - {Value}{unit}";
            }
        }
    }
}
