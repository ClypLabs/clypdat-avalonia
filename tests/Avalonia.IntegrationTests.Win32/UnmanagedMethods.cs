using System;
using System.Runtime.InteropServices;

namespace Avalonia.IntegrationTests.Win32;

internal static partial class UnmanagedMethods
{
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(IntPtr hwnd, out RECT lpRect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static partial IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "GetLayeredWindowAttributes", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetLayeredWindowAttributes(
        IntPtr hwnd,
        out uint colorKey,
        out byte alpha,
        out uint flags);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int nIndex);

    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    public const int SM_CMONITORS = 80;
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_LAYERED = 0x00080000;
    public const uint LWA_ALPHA = 0x00000002;
}
