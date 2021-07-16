using System;
using System.Diagnostics;
using System.IO;

namespace AddModSupportMSStoreSubnautica
{
    public static class Utils
    {
        public static string GetCurrentExecutableFileName()
        {
            using Process curProc = Process.GetCurrentProcess();
            return Path.Combine(AppContext.BaseDirectory, curProc.MainModule!.FileName!);
        }

        /// <summary>
        ///     True if applications is running from a zipped file.
        /// </summary>
        /// <returns></returns>
        public static bool IsRunningInTemp()
        {
            return Directory.GetCurrentDirectory().StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase);
        }
    }
}