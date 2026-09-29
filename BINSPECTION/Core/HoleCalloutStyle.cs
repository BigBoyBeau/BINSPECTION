using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BINSPECTION.Models;
using Newtonsoft.Json;

namespace BINSPECTION.Core
{
    // A saved hole callout setup ("style"), one per .bhcstyle file - e.g.
    // "Acme\Tapped 1-4-20.bhcstyle". It is the full dialog state (type,
    // values, tolerances, decimals, thread class, row/note order) minus the
    // parts that belong to one particular drawing (quantity, measured
    // diameter, cosmetic geometry), so loading it into the Hole Callout
    // dialog reproduces the callout exactly. One file per style keeps them
    // easy to share, copy per customer, and organize in folders.
    public class HoleCalloutStyle
    {
        public const string FileExtension = ".bhcstyle";

        public const string FileFilter = "BINSPECTION hole callout style (*.bhcstyle)|*.bhcstyle";

        public int FormatVersion { get; set; } = 1;

        // Copied out of Definition so a folder listing can filter by type
        // without caring about the rest.
        public HoleCalloutType Type { get; set; }

        public HoleCalloutDefinition Definition { get; set; }
    }

    // One entry in the dialog's Style dropdown.
    public class HoleCalloutStyleEntry
    {
        public string Path { get; set; }

        // Relative to the styles folder, without extension - so a
        // customer subfolder shows as "Acme\Tapped 1-4-20".
        public string DisplayName { get; set; }

        public override string ToString() => DisplayName;
    }

    public static class HoleCalloutStyleService
    {
        private const int MaxListed = 500;

        public static void Save(string path, HoleCalloutDefinition current)
        {
            HoleCalloutDefinition definition = Clone(current);

            // Drawing-specific - never part of a reusable style.
            definition.Quantity = 1;
            definition.MeasuredDiameter = null;
            definition.CosmeticSketchName = null;
            definition.CosmeticPoints = new List<double[]>();
            definition.CosmeticViewScale = 1.0;

            HoleCalloutStyle style = new HoleCalloutStyle
            {
                Type = definition.Type,
                Definition = definition,
            };

            File.WriteAllText(path, JsonConvert.SerializeObject(style, Formatting.Indented));
        }

        public static HoleCalloutStyle Load(string path)
        {
            HoleCalloutStyle style = JsonConvert.DeserializeObject<HoleCalloutStyle>(
                File.ReadAllText(path),
                new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });

            if (style?.Definition == null)
                throw new InvalidDataException("The file doesn't contain a hole callout style.");

            return style;
        }

        // Every style under `folder` (subfolders included, for customer
        // folders) of the given type, sorted by name. A file that can't be
        // read is skipped, never fatal to the listing.
        public static List<HoleCalloutStyleEntry> List(string folder, HoleCalloutType type)
        {
            List<HoleCalloutStyleEntry> entries = new List<HoleCalloutStyleEntry>();

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return entries;

            IEnumerable<string> files;

            try
            {
                files = Directory.EnumerateFiles(folder, "*" + HoleCalloutStyle.FileExtension, SearchOption.AllDirectories).Take(MaxListed);
            }
            catch (Exception ex)
            {
                BinspectionLog.Error("HoleCalloutStyleService.List", ex);
                return entries;
            }

            string root = folder.TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar;

            foreach (string file in files)
            {
                try
                {
                    if (Load(file).Type != type)
                        continue;

                    string relative = file.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                        ? file.Substring(root.Length)
                        : System.IO.Path.GetFileName(file);

                    entries.Add(new HoleCalloutStyleEntry
                    {
                        Path = file,
                        DisplayName = relative.Substring(0, relative.Length - HoleCalloutStyle.FileExtension.Length),
                    });
                }
                catch (Exception ex)
                {
                    BinspectionLog.Warn("HoleCalloutStyleService.List", "skipped unreadable style '" + file + "': " + ex.Message);
                }
            }

            return entries.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // The style's definition, converted to the drawing's units and
        // carrying over the current callout's drawing-specific parts
        // (quantity, measured diameter, cosmetic geometry).
        public static HoleCalloutDefinition ApplyTo(HoleCalloutStyle style, HoleCalloutDefinition current)
        {
            HoleCalloutDefinition result = Clone(style.Definition);

            string styleUnits = HoleCalloutBuilder.IsInch(result) ? "in" : "mm";
            string drawingUnits = HoleCalloutBuilder.IsInch(current) ? "in" : "mm";

            if (styleUnits != drawingUnits)
                ConvertUnits(result, styleUnits == "in" ? 25.4 : 1 / 25.4, styleUnits == "in" ? -1 : +1);

            result.Units = drawingUnits;
            result.Quantity = current.Quantity;
            result.MeasuredDiameter = current.MeasuredDiameter;
            result.CosmeticSketchName = current.CosmeticSketchName;
            result.CosmeticPoints = current.CosmeticPoints ?? new List<double[]>();
            result.CosmeticViewScale = current.CosmeticViewScale;

            return result;
        }

        // Scales every length (not the countersink angle) and shifts the
        // decimal places to match - a 3-place inch style reads as 2-place mm.
        private static void ConvertUnits(HoleCalloutDefinition def, double factor, int decimalShift)
        {
            double? Scale(double? value) => value.HasValue ? value.Value * factor : (double?)null;

            def.DrillDiameter = Scale(def.DrillDiameter);
            def.DrillDepth = Scale(def.DrillDepth);
            def.ThreadDepth = Scale(def.ThreadDepth);
            def.CounterboreDiameter = Scale(def.CounterboreDiameter);
            def.CounterboreDepth = Scale(def.CounterboreDepth);
            def.CountersinkDiameter = Scale(def.CountersinkDiameter);
            def.SpotfaceDiameter = Scale(def.SpotfaceDiameter);
            def.SpotfaceDepth = Scale(def.SpotfaceDepth);

            foreach (HoleCalloutTolerance tol in def.Tolerances ?? new List<HoleCalloutTolerance>())
            {
                if (tol.Key == HoleCalloutValueKeys.CountersinkAngle)
                    continue;

                tol.Plus *= factor;
                tol.Minus *= factor;
            }

            def.DecimalPlaces = Math.Max(0, Math.Min(5, def.DecimalPlaces + decimalShift));

            int places = def.DecimalPlaces;
            double? Round(double? value) =>
                value.HasValue ? Math.Round(value.Value, places, MidpointRounding.AwayFromZero) : (double?)null;

            def.DrillDiameter = Round(def.DrillDiameter);
            def.DrillDepth = Round(def.DrillDepth);
            def.ThreadDepth = Round(def.ThreadDepth);
            def.CounterboreDiameter = Round(def.CounterboreDiameter);
            def.CounterboreDepth = Round(def.CounterboreDepth);
            def.CountersinkDiameter = Round(def.CountersinkDiameter);
            def.SpotfaceDiameter = Round(def.SpotfaceDiameter);
            def.SpotfaceDepth = Round(def.SpotfaceDepth);
        }

        // A suggested file name for Save Style: type + size, filesystem-safe
        // ("1/4-20 UNC" -> "Tapped 1-4-20 UNC").
        public static string SuggestFileName(HoleCalloutDefinition def)
        {
            string name = def.Type + (string.IsNullOrWhiteSpace(def.SizeName) ? "" : " " + def.SizeName.Trim());

            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '-');

            return name + HoleCalloutStyle.FileExtension;
        }

        private static HoleCalloutDefinition Clone(HoleCalloutDefinition def) =>
            JsonConvert.DeserializeObject<HoleCalloutDefinition>(
                JsonConvert.SerializeObject(def),
                new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });
    }
}
