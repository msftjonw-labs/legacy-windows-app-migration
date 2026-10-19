using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace LegacyOrders
{
    public static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetDiskFreeSpaceEx(string directory, out ulong available, out ulong total, out ulong free);

        public static void RequireFreeSpace(string directory, ulong bytes)
        {
            ulong available, total, free;
            if (!GetDiskFreeSpaceEx(directory, out available, out total, out free))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (available < bytes + 1024 * 1024) throw new IOException("Insufficient disk space for attachment.");
        }

        public static void Open(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("Document or directory was not found.", path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        public static void Reveal(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Document was not found.", path);
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + Path.GetFullPath(path) + "\"") { UseShellExecute = true });
        }
    }
}
