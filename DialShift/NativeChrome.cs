using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DialShift;

internal static class NativeChrome
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    public static void Apply(Window window) => window.SourceInitialized += (_, _) =>
    {
        var dark = 1;
        // Older Windows versions may ignore this cosmetic hint.
        _ = DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref dark, sizeof(int));
    };
}
