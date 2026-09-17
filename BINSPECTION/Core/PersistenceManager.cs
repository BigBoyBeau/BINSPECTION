using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BINSPECTION.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SolidWorks.Interop.sldworks;

namespace BINSPECTION.Core
{
    // Reads and writes the external "balloon data" file that lives next to
    // a drawing. This file - not anything embedded in the drawing itself -
    // is the source of truth for characteristic numbers. That's the whole
    // design goal: the drawing's metadata can corrupt, a balloon can get
    // bumped or deleted, and this file still knows what number belongs
    // where.
    public static class PersistenceManager
    {
        // Appended to the drawing's own path, e.g.
        // "C:\Drawings\Bracket.SLDDRW" -> "C:\Drawings\Bracket.SLDDRW.binspection.json"
        private const string FileSuffix = ".binspection.json";

        // Builds the sidecar file path for a drawing. Returns null if the
        // drawing has never been saved (there's no stable path yet to
        // anchor the sidecar file to).
        public static string GetDataFilePath(ModelDoc2 model)
        {
            if (model == null)
                return null;

            string drawingPath = model.GetPathName();

            if (string.IsNullOrEmpty(drawingPath))
                return null;

            return drawingPath + FileSuffix;
        }

        // Loads the saved characteristics for a drawing. Always returns a
        // list (never null) - a missing or corrupt sidecar file degrades
        // to "nothing saved yet" rather than crashing the add-in, since a
        // fragile data file would defeat the entire point of having one.
        //
        // Convenience wrapper around LoadProject for the many call sites
        // that only care about characteristics - it preserves whatever
        // ActiveSheets/ToleranceSets/SheetToleranceAssignments already
        // exist in the file, they just aren't touched here.
        public static List<Characteristic> Load(string path)
        {
            return LoadProject(path).Characteristics;
        }

        // Saves the given characteristics to disk, preserving any
        // project-level sheet/tolerance data already saved alongside them.
        public static bool Save(string path, List<Characteristic> characteristics)
        {
            ProjectData data = LoadProject(path);

            data.Characteristics = characteristics ?? new List<Characteristic>();

            return SaveProject(path, data);
        }

        // Loads the full project data (characteristics + sheet/tolerance
        // setup) for a drawing. Always returns a non-null ProjectData with
        // non-null collections - a missing or corrupt sidecar file
        // degrades to "nothing saved yet" rather than crashing the add-in.
        //
        // Transparently migrates the older file format, whose JSON root
        // was a bare array of Characteristics with no wrapper object.
        public static ProjectData LoadProject(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return new ProjectData();

            try
            {
                string json = File.ReadAllText(path);

                if (string.IsNullOrWhiteSpace(json))
                    return new ProjectData();

                JToken root = JToken.Parse(json);

                // ProjectData's collection properties (ClassOptions,
                // MethodOptions, etc.) have field-initializer defaults.
                // Json.NET's default ObjectCreationHandling.Auto reuses
                // whatever the getter already returns for a collection
                // property and *appends* the JSON's items onto it, instead
                // of replacing it - so without ObjectCreationHandling.
                // Replace here, every load would silently grow those lists
                // by the default entries again, compounding on every
                // load/save cycle. (Characteristics happens to dodge the
                // visible symptom because PersistenceManager.Save always
                // overwrites it with the caller's own list right after
                // loading - see below - but the underlying bug is the
                // same for every collection property.)
                JsonSerializer serializer = JsonSerializer.Create(
                    new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });

                ProjectData data;

                if (root.Type == JTokenType.Array)
                {
                    // Old format: the root itself was the characteristics
                    // list. Wrap it - nothing is known yet about sheets or
                    // tolerance sets for a file saved before those existed.
                    data = new ProjectData
                    {
                        Characteristics =
                            root.ToObject<List<Characteristic>>(serializer) ?? new List<Characteristic>()
                    };
                }
                else
                {
                    data = root.ToObject<ProjectData>(serializer) ?? new ProjectData();
                }

                if (data.ActiveSheets == null)
                    data.ActiveSheets = new List<string>();

                if (data.ToleranceSets == null)
                    data.ToleranceSets = new List<Models.SheetToleranceSet>();

                if (data.SheetToleranceAssignments == null)
                    data.SheetToleranceAssignments = new Dictionary<string, string>();

                if (data.Characteristics == null)
                    data.Characteristics = new List<Characteristic>();

                // Also strips out duplicate entries left behind in sidecar
                // files that were already saved before the fix above -
                // otherwise those files would keep showing the same
                // inflated option list forever even though it can no
                // longer grow further.
                data.ClassOptions = DeduplicateOptions(
                    data.ClassOptions, new List<string> { "Minor", "Major", "Critical", "Key" });

                data.MethodOptions = DeduplicateOptions(
                    data.MethodOptions, new List<string> { "CMM", "Calipers" });

                return data;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"PersistenceManager.LoadProject Error: {ex}");

                return new ProjectData();
            }
        }

        // Falls back to the given defaults if empty, otherwise strips
        // duplicate entries (case-insensitive, trimmed) while keeping the
        // first-seen casing and order - the Method/Classification combo
        // boxes bind directly to this list, so any duplicate here shows up
        // as a literal repeated entry in the dropdown.
        private static List<string> DeduplicateOptions(List<string> options, List<string> defaults)
        {
            if (options == null || options.Count == 0)
                return defaults;

            var result = new List<string>();

            foreach (string option in options)
            {
                if (string.IsNullOrWhiteSpace(option))
                    continue;

                string trimmed = option.Trim();

                if (!result.Any(o => string.Equals(o, trimmed, StringComparison.OrdinalIgnoreCase)))
                    result.Add(trimmed);
            }

            return result.Count > 0 ? result : defaults;
        }

        // Saves the full project data to disk. Writes to a temp file first
        // and then swaps it into place, so a crash, a file lock, or
        // SolidWorks closing mid-write can't leave behind a half-written,
        // corrupt data file.
        public static bool SaveProject(string path, ProjectData data)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            string tempPath = path + ".tmp";

            try
            {
                string json = JsonConvert.SerializeObject(
                    data,
                    Formatting.Indented);

                File.WriteAllText(tempPath, json);

                if (File.Exists(path))
                    File.Delete(path);

                File.Move(tempPath, path);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"PersistenceManager.SaveProject Error: {ex}");

                return false;
            }
        }
    }
}
