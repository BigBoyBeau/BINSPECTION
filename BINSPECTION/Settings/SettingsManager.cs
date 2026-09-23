using System;
using System.IO;
using Newtonsoft.Json;

namespace BINSPECTION.Settings
{
    // Reads and writes AppSettings.json under %AppData%\BINSPECTION - the
    // one piece of BINSPECTION state that isn't per-drawing. Mirrors
    // Core.PersistenceManager.SaveProject's write-to-temp-then-swap pattern
    // and its "a missing or corrupt file degrades to defaults, never
    // crashes" behavior, for the same reason: a fragile settings file would
    // defeat the point of having one.
    public static class SettingsManager
    {
        private static string GetSettingsPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "BINSPECTION");

            Directory.CreateDirectory(folder);

            return Path.Combine(folder, "settings.json");
        }

        public static AppSettings Load()
        {
            string path = GetSettingsPath();

            if (!File.Exists(path))
                return new AppSettings();

            try
            {
                string json = File.ReadAllText(path);

                if (string.IsNullOrWhiteSpace(json))
                    return new AppSettings();

                return JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"SettingsManager.Load Error: {ex}");

                return new AppSettings();
            }
        }

        public static bool Save(AppSettings settings)
        {
            string path = GetSettingsPath();
            string tempPath = path + ".tmp";

            try
            {
                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);

                File.WriteAllText(tempPath, json);

                if (File.Exists(path))
                    File.Delete(path);

                File.Move(tempPath, path);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"SettingsManager.Save Error: {ex}");

                return false;
            }
        }
    }
}
