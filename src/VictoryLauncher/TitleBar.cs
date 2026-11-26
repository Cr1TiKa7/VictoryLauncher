using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VictoryLauncher;

internal static class TitleBar
{
    /// <summary>Paints the title bar in the colour of the evening sky (ignored where Windows does not offer it).</summary>
    public static void Evening(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        Set(handle, 20 /* dark title bar */, 1);
        Set(handle, 35 /* caption colour, 0x00BBGGRR */, 0x002E0A1B);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private static void Set(IntPtr window, int attribute, int value) =>
        _ = DwmSetWindowAttribute(window, attribute, ref value, sizeof(int));
}
