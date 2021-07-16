using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AddModSupportMSStoreSubnautica
{
    public static class NativeUtils
    {
        private const int HwndTopmost = -1;
        private const int SwpNoMove = 0x0002;
        private const int SwpNoSize = 0x0001;
        private const int SwpShowWindow = 0x0040;
        private const int SwHide = 0;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            int hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            int uFlags);

        public static void SetConsoleTopMost()
        {
            SetWindowPos(GetConsoleWindow(), HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
        }

        public static void HideProcessWindow(Process process)
        {
            ShowWindow(process.MainWindowHandle, SwHide);
        }
    }
}