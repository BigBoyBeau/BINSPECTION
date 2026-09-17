using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;

namespace BINSPECTION.Core
{
    // Builds the BINSPECTION toolbar icon strips from designed artwork
    // embedded as resources (Icons\*.png in the project, wildcard-included
    // in the .csproj) rather than drawing letter-abbreviation placeholders
    // at runtime. Still regenerated to disk on every add-in load -
    // ICommandGroup.IconList/MainIconList need file paths, not in-memory
    // images/streams - so updating an icon is just a matter of replacing
    // the embedded PNG and rebuilding, no separate install step.
    public static class CommandIconGenerator
    {
        // Order must exactly match the order commands are added via
        // AddCommandItem2 in CommandManagerHandler.CreateCommandManager -
        // each entry's index is that command's ImageListIndex. The string
        // here is the base filename (without "_<size>x<size>.png") of the
        // matching embedded icon under Icons\.
        public static readonly string[] CommandIconNames =
        {
            "create-balloons",             // Create Balloons
            "report-balloons",             // Generate Report
            "restore-balloons",            // Restore Balloons
            "balloon-manager",             // Balloon Manager
            "sheet-tolerances",            // Sheet Tolerance Selection
            "delete-all-balloons",         // Delete All Balloons
            "remove-balloons",             // Remove Balloons
            "refresh-balloons",            // Refresh Balloons
            "save-balloon-position",       // Save Position
            "restore-balloon-position",    // Restore Position
        };

        // Matches the exact per-icon PNG sizes shipped under Icons\, which
        // in turn match SolidWorks' own DPI-scaled CommandGroup icon sizes
        // (100/125/150/200/300/400%) - so each cell is drawn from its own
        // native-resolution source image rather than being upscaled from a
        // smaller one.
        private static readonly int[] IconSizes = { 20, 32, 40, 64, 96, 128 };

        // Whole-tab/group icon (ICommandGroup.MainIconList) - there's no
        // per-size artwork for this one, just a single 1024x1024 source
        // logo, so it's scaled down to each size instead of loaded at
        // exact size like the per-command icons.
        private const string MainLogoResourceSuffix = "mcr.png";

        // Builds (or rebuilds) the per-command icon strips and returns
        // their paths, one per size in IconSizes - for
        // ICommandGroup.IconList. Each strip is CommandIconNames.Length
        // icons wide, in the same order as CommandIconNames.
        public static string[] BuildIconList()
        {
            string folder = GetIconFolder();
            var paths = new List<string>();

            foreach (int size in IconSizes)
            {
                string path = Path.Combine(folder, $"binspection_commands_{size}.png");
                BuildCommandStrip(path, size);
                paths.Add(path);
            }

            return paths.ToArray();
        }

        // Builds the single "main" group icon (one per size) representing
        // the whole "BINSPECTION" toolbar/tab as a whole, not any
        // individual command - for ICommandGroup.MainIconList.
        public static string[] BuildMainIconList()
        {
            string folder = GetIconFolder();
            var paths = new List<string>();

            using (Image source = LoadEmbeddedIcon(MainLogoResourceSuffix))
            {
                foreach (int size in IconSizes)
                {
                    string path = Path.Combine(folder, $"binspection_main_{size}.png");
                    BuildMainIcon(path, size, source);
                    paths.Add(path);
                }
            }

            return paths.ToArray();
        }

        private static void BuildCommandStrip(string path, int cellSize)
        {
            int width = cellSize * CommandIconNames.Length;

            using (Bitmap bitmap = new Bitmap(width, cellSize, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Transparent);

                    for (int i = 0; i < CommandIconNames.Length; i++)
                    {
                        string suffix = $"{CommandIconNames[i]}_{cellSize}x{cellSize}.png";

                        using (Image icon = LoadEmbeddedIcon(suffix))
                        {
                            g.DrawImage(icon, i * cellSize, 0, cellSize, cellSize);
                        }
                    }
                }

                bitmap.Save(path, ImageFormat.Png);
            }
        }

        private static void BuildMainIcon(string path, int size, Image source)
        {
            using (Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);
                    g.DrawImage(source, 0, 0, size, size);
                }

                bitmap.Save(path, ImageFormat.Png);
            }
        }

        // Icons ship as embedded resources rather than loose files, so the
        // add-in doesn't depend on a folder existing next to the assembly.
        // Matched by filename suffix rather than a fully qualified
        // manifest name, since the exact generated prefix depends on
        // project/folder naming details that shouldn't matter here.
        private static Image LoadEmbeddedIcon(string fileNameSuffix)
        {
            Assembly assembly = typeof(CommandIconGenerator).Assembly;

            string resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name =>
                    name.EndsWith(fileNameSuffix, StringComparison.OrdinalIgnoreCase));

            if (resourceName == null)
            {
                throw new FileNotFoundException(
                    $"Embedded icon resource ending in '{fileNameSuffix}' was not found.");
            }

            byte[] bytes;

            using (Stream resourceStream = assembly.GetManifestResourceStream(resourceName))
            using (MemoryStream buffer = new MemoryStream())
            {
                resourceStream.CopyTo(buffer);
                bytes = buffer.ToArray();
            }

            // Image.FromStream requires its stream to stay open for the
            // life of the Image - wrapping a byte[] we already own (rather
            // than handing it the resource stream directly) sidesteps
            // having to keep that stream open for as long as each icon is
            // in use.
            return Image.FromStream(new MemoryStream(bytes));
        }

        private static string GetIconFolder()
        {
            // Written under ProgramData rather than next to the assembly -
            // a per-machine install (Program Files) isn't writable by
            // standard users, and these icons are regenerated on every load.
            string commonAppData =
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            string folder = Path.Combine(commonAppData, "BINSPECTION", "Icons");

            Directory.CreateDirectory(folder);

            return folder;
        }
    }
}
