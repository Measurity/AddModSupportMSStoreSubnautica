using System;
using System.Runtime.InteropServices;

namespace AddModSupportMSStoreSubnautica
{
    public static class ConsoleUtils
    {
        private const int HwndTopmost = -1;
        private const int SwpNoMove = 0x0002;
        private const int SwpNoSize = 0x0001;
        private const int SwpShowWindow = 0x0040;

        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();
        
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

        public static void SetTopMost()
        {
            SetWindowPos(GetConsoleWindow(), HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
        }
    }
}