using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Desinstalleur.App.Services;

/// <summary>Barre de titre sombre assortie à l'application (Windows 10 20H1+ et Windows 11).</summary>
public static class WindowTheme
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void ApplyDarkTitleBar(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            var enabled = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));

            // Couleur au format 0x00BBGGRR : même bleu nuit que le fond (#070D1F). Ignoré avant Windows 11.
            var caption = 0x001F0D07;
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
        }
        catch
        {
            // Purement cosmétique : on ignore les anciennes versions de Windows.
        }
    }
}
