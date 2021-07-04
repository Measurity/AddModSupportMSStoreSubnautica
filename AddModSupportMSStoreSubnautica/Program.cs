using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace NitroxForMSStore
{
    internal class Program
    {
        private static readonly bool isQuitting = false;
        private static readonly ConsoleExitHandler ExitHandler = new();

        private static readonly string PlayerLogFile =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "..", "LocalLow",
                "Unknown Worlds", "Subnautica", "Player.log");

        public static async Task Main(string[] args)
        {
            ConsoleUtils.SetTopMost();
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                var errorMsg = e.ExceptionObject?.ToString() ??
                               $"An unknown unexpected error occurred:{Environment.NewLine}{Environment.StackTrace}";
                PrintColor(errorMsg, ConsoleColor.Red);
                Console.WriteLine("Press any key to continue . . .");
                Console.ReadKey(true);
                Environment.Exit(1);
            };

            if (RunCmd("if (Get-AppxPackage *Subnautica* | where IsDevelopmentMode -eq $False) { exit 0 } else { exit 1 }") != 0)
            {
                PrintColor("MS Store Subnautica is not installed", ConsoleColor.Red);
                goto end;
            }
            RequireAdmin();
            EnableDeveloperMode();

            PrintColor(@"Enter where you want your dump files to go. (Default: C:\Subnautica)", ConsoleColor.Cyan);
            var dir = AskAndCreateDirectory(@"C:\Subnautica");
            Console.WriteLine("your dump path is: " + dir);
            
            var subnauticaProc = await StartSubnauticaAsync();
            RunCmd($@"-p {subnauticaProc.Id} -d ""{dir}""", "UWPInjector.exe");
            KillProcessesByName("Subnautica");
            
            PrintColor("Uninstalling MS Store Subnautica to be replaced by dumped Subnautica...", ConsoleColor.Cyan);
            RunCmd("Get-AppxPackage *Subnautica* | Remove-AppxPackage");
            PrintColor("Registering dumped Subnautica into MS Store packages...", ConsoleColor.Cyan);
            RunCmd("Add-AppxPackage -Register AppxManifest.xml", "powershell.exe", dir);
            PrintColor("Organizing files in dumped Subnautica to allow server hosting (this can take a minute)...", ConsoleColor.Cyan);
            CopyContents(Path.Combine(dir, "AssetBundles"), Path.Combine(dir, "Subnautica_Data", "StreamingAssets", "AssetBundles"));
            CopyContents(Path.Combine(dir, "SNUnmanagedData"), Path.Combine(dir, "Subnautica_Data", "StreamingAssets", "SNUnmanagedData"));

            PrintColor(
                "Done! Start Nitrox Launcher and set the path in settings to the new dumped Subnautica folder before you play!",
                ConsoleColor.Yellow);
            end:
            Console.WriteLine("Press any key to continue . . .");
            Console.ReadKey(true);
        }

        private static void EnableDeveloperMode()
        {
            using var baseKey =
                Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock", true);
            baseKey?.SetValue("AllowDevelopmentWithoutDevLicense", 1, RegistryValueKind.DWord);
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
                File.Copy(file, Path.Combine(targetDirectory, Path.GetFileName(file)));
        }

        private static async Task<Process> StartSubnauticaAsync()
        {
            File.Delete(PlayerLogFile);
            var subnauticaProc = Process.Start("ms-xbl-38616e6e:\\");
            ExitHandler.AddCleanupTask(() =>
            {
                subnauticaProc?.Kill();
                subnauticaProc?.Close();
            });
            Console.WriteLine("Started Subnautica process Id = " + subnauticaProc?.Id);
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
                    using var stream = new StreamReader(new FileStream(PlayerLogFile, FileMode.Open, FileAccess.Read,
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

            if ((subnauticaProc?.MainModule?.FileName.Contains("WindowsApps") ?? false) == false)
            {
                PrintColor("Could not start Subnautica from MS Store. Make sure it's installed.", ConsoleColor.Red);
                Console.WriteLine("Press any key to continue . . .");
                Console.ReadKey(true);
            }

            return subnauticaProc;
        }

        private static string AskAndCreateDirectory(string defaultDir = null)
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
                    Directory.CreateDirectory(result);
                    return result;
                }
                catch (Exception)
                {
                    PrintColor($"Directory '{result}' does not exist. Enter a different directory:");
                }
            }
        }

        private static int RunCmd(string cmd, string shell = "powershell.exe", string workingDirectory = null)
        {
            var processStartInfo = new ProcessStartInfo
            {
                CreateNoWindow = false,
                UseShellExecute = false,
                Arguments = cmd,
                FileName = shell,
                WorkingDirectory = workingDirectory ?? ""
            };

            var process = new Process
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
            foreach (var process in Process.GetProcessesByName(name))
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

        private static void PrintColor(string message, ConsoleColor? color = null)
        {
            if (isQuitting) return;

            if (color == null)
            {
                Console.WriteLine(message);
            }
            else
            {
                var prev = Console.ForegroundColor;
                Console.ForegroundColor = color.Value;
                Console.WriteLine(message);
                Console.ForegroundColor = prev;
            }
        }

        private static void RequireAdmin()
        {
            static bool IsAdministrator()
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }

            if (!IsAdministrator())
            {
                using Process proc = new Process();
                proc.StartInfo = new ProcessStartInfo()
                {
                    FileName = Assembly.GetEntryAssembly().Location,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                proc.Start();
                Environment.Exit(0);
            }
        }
    }
}