using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Desinstalleur.App.Services;

/// <summary>Barre de titre assortie au thème de l'application (Windows 10 20H1+ et Windows 11).</summary>
public static class WindowTheme
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Barre de titre sombre en thème sombre, claire en thème clair (nom gardé pour les fenêtres existantes).</summary>
    public static void ApplyDarkTitleBar(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            var dark = ThemeManager.IsLight ? 0 : 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

            // Couleur au format 0x00BBGGRR : même couleur que le fond des fenêtres. Ignoré avant Windows 11.
            var background = ThemeManager.WindowBackground;
            var caption = background.R | (background.G << 8) | (background.B << 16);
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
        }
        catch
        {
            // Purement cosmétique : on ignore les anciennes versions de Windows.
        }
    }
}
