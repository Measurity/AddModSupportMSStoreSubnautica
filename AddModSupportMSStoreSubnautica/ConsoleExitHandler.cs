using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AddModSupportMSStoreSubnautica
{
    /// <summary>
    ///     Captures exit event of console and can do some cleanup.
    /// </summary>
    public class ConsoleExitHandler
    {
        private static readonly List<Action> CleanupTasks = new();

        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private readonly ConsoleEventDelegate handler; // Keeps it from getting garbage collected

        public ConsoleExitHandler()
        {
            handler = ConsoleEventCallback;
            SetConsoleCtrlHandler(handler, true);
        }

        public void AddCleanupTask(Action action)
        {
            lock (CleanupTasks)
            {
                CleanupTasks.Add(action);
            }
        }

        private bool ConsoleEventCallback(int eventType)
        {
            if (eventType != 2) return false;

            lock (CleanupTasks)
            {
                foreach (var task in CleanupTasks)
                    try
                    {
                        task();
                    }
                    catch (Exception)
                    {
                        // ignored
                    }

                CleanupTasks.Clear();
            }

            return false;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCtrlHandler(ConsoleEventDelegate callback, bool add);

        private delegate bool ConsoleEventDelegate(int eventType);
    }
}