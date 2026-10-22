using System.Runtime.InteropServices;

namespace Cadenza.UI.Tests.Infrastructure;

/// <summary>The few Win32 calls the UI tests need: placing the window off-screen and capturing it.</summary>
internal static partial class NativeMethods
{
    /// <summary>SW_SHOWNOACTIVATE: show the window without giving it focus.</summary>
    public const int ShowNoActivate = 4;

    /// <summary>PW_RENDERFULLCONTENT: capture DirectComposition content (WinUI) too.</summary>
    public const uint RenderFullContent = 2;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPlacement(IntPtr window, ref WindowPlacement placement);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr window, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCommand;
        public int MinX;
        public int MinY;
        public int MaxX;
        public int MaxY;
        public Rect Normal;
    }
}
