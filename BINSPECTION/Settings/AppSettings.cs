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
    }
}
