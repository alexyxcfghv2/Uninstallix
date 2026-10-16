using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Desinstalleur.App.Services;

/// <summary>Extrait l'icône déclarée dans « DisplayIcon » (« chemin.exe,index »).</summary>
public static class IconLoader
{
    public static ImageSource? Load(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
            return null;

        var value = Environment.ExpandEnvironmentVariables(displayIcon.Trim());
        var index = 0;

        // « C:\App\app.exe,0 » ou « "C:\App\app.exe",-101 »
        var comma = value.LastIndexOf(',');
        if (comma > 0 && int.TryParse(value[(comma + 1)..].Trim(), out var parsed))
        {
            index = parsed;
            value = value[..comma];
        }
        value = value.Trim().Trim('"');

        if (!File.Exists(value))
            return null;

        var large = new IntPtr[1];
        try
        {
            if (NativeMethods.ExtractIconEx(value, index, large, null, 1) == 0 || large[0] == IntPtr.Zero)
                return null;

            var source = Imaging.CreateBitmapSourceFromHIcon(large[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (large[0] != IntPtr.Zero)
                NativeMethods.DestroyIcon(large[0]);
        }
    }
}
