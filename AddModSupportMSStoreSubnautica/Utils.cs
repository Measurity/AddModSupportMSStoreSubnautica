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
    }
}