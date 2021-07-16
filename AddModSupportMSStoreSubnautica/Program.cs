using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using AddModSupportMSStoreSubnautica.TypeConverters;

namespace AddModSupportMSStoreSubnautica
{
    internal class Program
    {
        /// <summary>
        ///     The amount of free space required in bytes is about a Subnautica installation (~7.42GiB) + overhead = ~8GiB.
        /// </summary>
        private static readonly long SubnauticaInstallSize = (long) (8 * Math.Pow(1024, 3));

        private static readonly ConsoleExitHandler ExitHandler = new();

        private static readonly string PlayerLogFile =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "..", "LocalLow",
                "Unknown Worlds", "Subnautica", "Player.log");

        public static async Task Main(string[] args)
        {
            SetupProgram();
            if (!CanRun()) goto end;

            PrintColor(@"Enter a directory where you want Subnautica (with mod support) to be: (Default: C:\Subnautica)", ConsoleColor.Cyan);
            var dir = AskForDirectory(@"C:\Subnautica");
            if (!HasEnoughSpaceOnDrives(
                (SubnauticaInstallSize, Utils.ReadRegistry<string>(@"Computer\HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\PackageRoot")),
                (SubnauticaInstallSize, dir)
            )) return;

            Directory.CreateDirectory(dir);
            Process subnauticaProc = await StartSubnauticaAsync();
            if (RunCmd($@"-c -p {subnauticaProc.Id} -d ""{dir}""", @".\bin\UWPInjector.exe") != 0)
            {
                PrintColor("Failed to dump Subnautica.", ConsoleColor.Red);
                goto end;
            }
            KillProcessesByName("Subnautica");

            PrintColor("Uninstalling MS Store Subnautica to be replaced by dumped Subnautica...", ConsoleColor.Cyan);
#if !DEBUG
            RunCmd("Get-AppxPackage *Subnautica* | Remove-AppxPackage");
#endif
            PrintColor("Registering dumped Subnautica into MS Store packages...", ConsoleColor.Cyan);
#if !DEBUG
            RunCmd("Add-AppxPackage -Register AppxManifest.xml", "powershell.exe", dir);
#endif
            PrintColor("Organizing files in dumped Subnautica to allow server hosting (this can take a minute)...",
                ConsoleColor.Cyan);
            CopyContents(Path.Combine(dir, "AssetBundles"),
                Path.Combine(dir, "Subnautica_Data", "StreamingAssets", "AssetBundles"));
            CopyContents(Path.Combine(dir, "SNUnmanagedData"),
                Path.Combine(dir, "Subnautica_Data", "StreamingAssets", "SNUnmanagedData"));

            PrintColor(
                $"Done! Before you play: start Nitrox Launcher and set the path in settings to the new path:{Environment.NewLine}{dir}",
                ConsoleColor.Yellow);
            end:
            Console.WriteLine("Press any key to continue . . .");
            Console.ReadKey(true);
        }

        private static void SetupProgram()
        {
            #if !RELEASE
            Console.WriteLine(" -- RUNNING IN DEVELOPMENT MODE -- ");
            #endif
            ConsoleUtils.SetTopMost();
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                var errorMsg = e.ExceptionObject.ToString() ??
                               $"An unknown unexpected error occurred:{Environment.NewLine}{Environment.StackTrace}";
                PrintColor(errorMsg, ConsoleColor.Red);
                Console.WriteLine("Press any key to continue . . .");
                Console.ReadKey(true);
                Environment.Exit(1);
            };
            TypeDescriptor.AddAttributes(typeof(bool), new TypeConverterAttribute(typeof(BoolIntConverter)));
        }

        private static bool CanRun()
        {
            if (!OperatingSystem.IsWindows())
            {
                PrintColor("This tool only works on Windows.", ConsoleColor.Red);
                return false;
            }
            if (Utils.IsRunningInTemp())
            {
                PrintColor(@"Extract files from the zip before running the tool.
Anywhere is fine and can remove it when done.", ConsoleColor.Red);
                return false;
            }
            if (RunCmd("if (Get-AppxPackage *Subnautica* | where IsDevelopmentMode -eq $False) { exit 0 } else { exit 1 }") != 0)
            {
                PrintColor("MS Store Subnautica is not installed", ConsoleColor.Red);
                return false;
            }
#if !DEBUG
            RequireAdmin();            
#endif
            EnableDeveloperMode();
            return true;
        }

        private static bool HasEnoughSpaceOnDrives(params (long requiredSpaceInBytes, string? path)[] pathSpaceRequirements)
        {
            static bool HasEnoughSpaceAvailable(ReadOnlySpan<char> dir, long spaceRequirementInBytes)
            {
                DriveInfo? driveFromPath = Utils.GetDriveFromPath(dir);
                if (driveFromPath == null)
                {
                    // A bigger issue is going on than not having enough space, don't fail here.
                    return true;
                }

                while (driveFromPath.AvailableFreeSpace < spaceRequirementInBytes)
                {
                    var spaceRequirementMessage = $@"The available space on drive {Path.GetPathRoot(dir.ToString())} is low.
Recommended: {spaceRequirementInBytes / Math.Pow(1024, 3):##,###.##}GiB but actual space left is {driveFromPath.AvailableFreeSpace / Math.Pow(1024, 3):##,###.##}GiB";

                    PrintColor(spaceRequirementMessage, ConsoleColor.Yellow);
                    PrintColor("Press 'y' key to continue anyway (not recommended). Press 'n' key to close. Press enter to retry:");
                    switch (Console.ReadKey(true).KeyChar)
                    {
                        case 'n':
                            return false;
                        case 'y':
                            return true;
                        default:
                            continue;
                    }
                }

                return true;
            }

            Dictionary<string, long> drivesAggregate = new();
            // Group paths to drives and sum requirements.
            foreach ((long requiredSpaceInBytes, string? path) requirement in pathSpaceRequirements)
            {
                if (requirement.path == null) continue;
                var key = Directory.GetDirectoryRoot(requirement.path);
                drivesAggregate.TryAdd(key, 0);
                drivesAggregate[key] += requirement.requiredSpaceInBytes;
            }
            // Test drive space is there and confirm with user.
            foreach (var entry in drivesAggregate)
            {
                if (!HasEnoughSpaceAvailable(entry.Key, entry.Value)) return false;
            }
            return true;
        }

        [Conditional("RELEASE")]
        private static void EnableDeveloperMode()
        {
            Utils.WriteRegistry(@"Computer\HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock\AllowDevelopmentWithoutDevLicense", 1);
        }

        private static void CopyContents(string folder, string targetDirectory)
        {
            foreach (var directory in Directory.GetDirectories(folder))
            {
                var dirName = Path.GetFileName(directory);
                var curTargetFolder = Path.Combine(targetDirectory, dirName);
                Directory.CreateDirectory(curTargetFolder);
                CopyContents(directory, curTargetFolder);
            }

            Directory.CreateDirectory(targetDirectory);
            foreach (var file in Directory.GetFiles(folder))
            {
                File.Copy(file, Path.Combine(targetDirectory, Path.GetFileName(file)));
            }
        }

        private static async Task<Process> StartSubnauticaAsync()
        {
            File.Delete(PlayerLogFile);
            Process subnauticaProc = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = @"ms-xbl-38616e6e:\",
                    UseShellExecute = true
                }
            };
            subnauticaProc.Start();
            ExitHandler.AddCleanupTask(() =>
            {
                subnauticaProc.Kill();
                subnauticaProc.Close();
            });
            Console.WriteLine("Started Subnautica process Id = " + subnauticaProc.Id);
            // Wait for Subnautica to initialize a bit before running UWPInjector.
            const int timeToRetryInSeconds = 30;
            const int iterationDelayInMs = 250;
            PrintColor($"Waiting for Subnautica to get ready. Times out in {timeToRetryInSeconds} seconds...",
                ConsoleColor.Cyan);
            var retries = 1000 / iterationDelayInMs * timeToRetryInSeconds;
            while (retries-- >= 0)
            {
                try
                {
                    using StreamReader stream = new(new FileStream(PlayerLogFile, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite, 4096, FileOptions.Asynchronous));
                    if ((await stream.ReadToEndAsync()).Contains("SystemInfo: "))
                    {
                        break;
                    }
                }
                catch (Exception)
                {
                    // ignored
                }
                finally
                {
                    await Task.Delay(iterationDelayInMs);
                }
            }

            return subnauticaProc;
        }

        private static string AskForDirectory(string? defaultDir = null)
        {
            while (true)
            {
                var result = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(result))
                {
                    if (string.IsNullOrWhiteSpace(defaultDir)) continue;
                    result = Path.GetFullPath(defaultDir);
                }
                else
                {
                    result = Path.GetFullPath(result);
                }

                if (Directory.Exists(result))
                {
                    if (Directory.EnumerateFileSystemEntries(result).Any())
                    {
                        PrintColor(
                            $"Directory '{result}' is not empty. Remove the files in it or choose a different directory:");
                        continue;
                    }

                    return result;
                }

                try
                {
                    return result;
                }
                catch (Exception)
                {
                    PrintColor($"Directory '{result}' does not exist. Enter a different directory:");
                }
            }
        }

        private static int RunCmd(string cmd, string shell = "powershell.exe", string? workingDirectory = null)
        {
            ProcessStartInfo processStartInfo = new()
            {
                CreateNoWindow = false,
                UseShellExecute = false,
                Arguments = cmd,
                FileName = shell,
                WorkingDirectory = workingDirectory ?? ""
            };

            Process process = new()
            {
                StartInfo = processStartInfo,
                EnableRaisingEvents = true
            };
            process.Start();

            // Process output in performant way
            ExitHandler.AddCleanupTask(() =>
            {
                process.Kill();
                process.Dispose();
            });
            process.WaitForExit();
            return process.ExitCode;
        }

        private static void KillProcessesByName(string name)
        {
            foreach (Process process in Process.GetProcessesByName(name))
            {
                try
                {
                    process.Kill();
                    process.Dispose();
                }
                catch (Exception)
                {
                    PrintColor($"Failed to kill process: {process.ProcessName}", ConsoleColor.Yellow);
                }
            }
        }

        private static void PrintColor(string message, ConsoleColor? color = null)
        {
            if (color == null)
            {
                Console.WriteLine(message);
            }
            else
            {
                ConsoleColor prev = Console.ForegroundColor;
                Console.ForegroundColor = color.Value;
                Console.WriteLine(message);
                Console.ForegroundColor = prev;
            }
        }

        private static void RequireAdmin()
        {
            static bool IsAdministrator()
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }

            if (!IsAdministrator())
            {
                using Process proc = new()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = Utils.GetCurrentExecutableFileName(),
                        UseShellExecute = true,
                        Verb = "runas"
                    }
                };
                proc.Start();
                Environment.Exit(0);
            }
        }
    }
}