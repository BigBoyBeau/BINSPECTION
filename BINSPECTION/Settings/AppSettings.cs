namespace BINSPECTION.Settings
{
    // Global, cross-drawing user preferences - distinct from ProjectData,
    // which is per-drawing (saved in the .binspection.json sidecar next to
    // each drawing). A preference like SnapToSelectionEnabled is a personal
    // workflow choice, not something that should reset when opening a
    // different drawing, so it lives here instead.
    public class AppSettings
    {
        public bool SnapToSelectionEnabled { get; set; } = true;

        // Last folder a hole callout style was loaded from or saved to (see
        // Core/HoleCalloutStyle.cs) - the Hole Callout dialog opens its file
        // browsers here and lists the styles found here, so a shared
        // styles folder only has to be found once.
        public string HoleCalloutStyleFolder { get; set; }
    }
}
