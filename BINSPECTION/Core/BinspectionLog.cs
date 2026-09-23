using System;
using System.IO;

namespace BINSPECTION.Core
{
    // Persistent error/warning log for failures that would otherwise only
    // reach System.Diagnostics.Debug.WriteLine - which is invisible in a
    // normal installed-addin session (no debugger attached to SolidWorks).
    // That invisibility is what let several past bugs look "silent" (see
    // [[binspection_create_balloons_multi_sheet_silent_abort]]).
    //
    // Writes to %LOCALAPPDATA%\BINSPECTION\binspection.log, rolling over to
    // binspection.old.log once it passes MaxBytes so it can never grow
    // unbounded. Every member is guaranteed never to throw - a logging
    // failure must never turn into a second failure inside the catch block
    // that called it.
    public static class BinspectionLog
    {
        private const long MaxBytes = 2 * 1024 * 1024;

        private static readonly object Sync = new object();

        public static string LogFilePath { get; } = BuildLogFilePath();

        private static string BuildLogFilePath()
        {
            try
            {
                string folder = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    "BINSPECTION");

                return Path.Combine(folder, "binspection.log");
            }
            catch
            {
                return null;
            }
        }

        public static void Error(string context, Exception ex)
        {
            Write("ERROR", context, ex?.ToString() ?? "(no exception)");
        }

        public static void Error(string context, string message)
        {
            Write("ERROR", context, message);
        }

        public static void Warn(string context, string message)
        {
            Write("WARN ", context, message);
        }

        public static void Info(string context, string message)
        {
            Write("INFO ", context, message);
        }

        private static void Write(string level, string context, string message)
        {
            string line =
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + level + " [" + context + "] " + message;

            System.Diagnostics.Debug.WriteLine(line);

            if (LogFilePath == null)
                return;

            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath));

                    FileInfo info = new FileInfo(LogFilePath);

                    if (info.Exists && info.Length > MaxBytes)
                    {
                        string oldPath = Path.Combine(info.DirectoryName, "binspection.old.log");

                        if (File.Exists(oldPath))
                            File.Delete(oldPath);

                        File.Move(LogFilePath, oldPath);
                    }

                    File.AppendAllText(LogFilePath, line + System.Environment.NewLine);
                }
            }
            catch
            {
                // Logging must never throw - see class remarks.
            }
        }
    }
}
