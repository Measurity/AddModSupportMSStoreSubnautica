using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

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
        ///     True if application is running without being extracted from a zip.
        /// </summary>
        /// <returns></returns>
        public static bool IsRunningInTemp()
        {
            return Directory.GetCurrentDirectory().StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        ///     Gets the drive by the given path. Returns null if path is invalid or does not have a known drive.
        /// </summary>
        /// <param name="path">Path that points to a directory on a drive.</param>
        /// <returns>The drive information or null if invalid.</returns>
        public static DriveInfo? GetDriveFromPath(ReadOnlySpan<char> path)
        {
            if (path.IsEmpty || path.IsWhiteSpace()) return null;
            path = path.Trim();
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (path.IndexOf(drive.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase) == 0) return drive;
            }
            return null;
        }

        public static T? ReadRegistry<T>(ReadOnlySpan<char> pathWithKey)
        {
            using RegistryKey? key = GetRegistryKey(pathWithKey, false);
            if (key == null) return default;
            var nameOfKey = pathWithKey[(pathWithKey.LastIndexOf(Path.DirectorySeparatorChar) + 1)..];
            return (T?)TypeDescriptor.GetConverter(typeof(T)).ConvertFrom(key.GetValue(nameOfKey.ToString()));
        }

        public static void WriteRegistry<T>(ReadOnlySpan<char> pathWithKey, T value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            using RegistryKey? key = GetRegistryKey(pathWithKey);
            if (key == null) return;
            RegistryValueKind kind = value switch
            {
                int => RegistryValueKind.DWord,
                long => RegistryValueKind.QWord,
                byte[] => RegistryValueKind.Binary,
                _ => RegistryValueKind.String
            };
            var nameOfKey = pathWithKey[(pathWithKey.LastIndexOf(Path.DirectorySeparatorChar) + 1)..];
            key.SetValue(nameOfKey.ToString(), value, kind);
        }

        public static async IAsyncEnumerable<string> ReadLinesFromFileAsync(string file)
        {
            using StreamReader stream = new(new FileStream(file, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite, 4096, FileOptions.Asynchronous));
            while (!stream.EndOfStream)
            {
                var line = await stream.ReadLineAsync();
                if (line == null) continue;
                yield return line;
            }
        }

        private static RegistryKey? GetRegistryKey(ReadOnlySpan<char> path, bool writable = true)
        {
            if (path.IsEmpty || path.IsWhiteSpace()) return null;
            path = path.Trim();
            if (path.IndexOf("Computer", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return null;
            }
            string[] parts = path.ToString().Split(Path.DirectorySeparatorChar);
            RegistryHive hive;
            switch (parts[1].ToLowerInvariant())
            {
                case "hkey_local_machine":
                    hive = RegistryHive.LocalMachine;
                    break;
                default:
                    hive = RegistryHive.CurrentUser;
                    break;
            }
            return RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(string.Join(Path.DirectorySeparatorChar, parts[2..^1]), writable);
        }
    }
}