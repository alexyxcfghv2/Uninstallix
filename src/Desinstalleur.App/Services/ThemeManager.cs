using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Desinstalleur.App.Services;

/// <summary>
/// Thème (sombre / clair / celui de Windows) et couleur d'accent.
/// Les vues utilisent {StaticResource ...} : le thème est donc appliqué une seule fois au démarrage, avant la
/// création de toute fenêtre, en modifiant les pinceaux de Themes/Styles.xaml eux-mêmes (ou en remplaçant
/// l'entrée du dictionnaire quand l'objet est figé). Un changement de réglage s'applique au prochain démarrage.
/// </summary>
public static class ThemeManager
{
    public const string Dark = "sombre";
    public const string Light = "clair";
    public const string Auto = "auto";

    public const string DefaultAccent = "bleu";
    public const string WindowsAccent = "windows";

    /// <summary>Une couleur d'accent proposée : identifiant enregistré, nom affiché (français), couleur et couleur secondaire du dégradé.</summary>
    public sealed record AccentChoice(string Id, string Name, Color Main, Color Second);

    public static IReadOnlyList<AccentChoice> Accents { get; } = new[]
    {
        new AccentChoice(DefaultAccent, "Bleu", Hex("#1E8BFF"), Hex("#18D6FF")),
        new AccentChoice("violet", "Violet", Hex("#8B5CF6"), Hex("#C17BFF")),
        new AccentChoice("vert", "Vert", Hex("#12A150"), Hex("#2FD08A")),
        new AccentChoice("orange", "Orange", Hex("#F2700F"), Hex("#FFAE33")),
        new AccentChoice("rose", "Rose", Hex("#E5428F"), Hex("#FF7EB8")),
        new AccentChoice("rouge", "Rouge", Hex("#E0283E"), Hex("#FF6A5C")),
    };

    /// <summary>Vrai quand le thème clair a été appliqué à ce démarrage.</summary>
    public static bool IsLight { get; private set; }

    /// <summary>Couleur du fond des fenêtres (sert à la barre de titre).</summary>
    public static Color WindowBackground { get; private set; } = Hex("#070D1F");

    // Valeurs d'origine (thème sombre) des pinceaux et couleurs qui dépendent de l'accent.
    private static readonly Color DarkCard = Hex("#0E1630");

    // Palette claire : textes contrôlés au niveau WCAG AA (≥ 4,5:1) sur tous les fonds clairs.
    private static readonly Dictionary<string, Color> LightPalette = new()
    {
        ["WindowBackground"] = Hex("#F3F5FA"),
        ["CardBackground"] = Hex("#FFFFFF"),
        ["CardBackgroundRaised"] = Hex("#EEF2F9"),
        ["CardBorder"] = Hex("#DCE2EE"),
        ["InputBackground"] = Hex("#F6F8FC"),
        ["HoverFill"] = Hex("#E8EDF7"),
        ["TextPrimary"] = Hex("#111A2E"),
        ["TextSecondary"] = Hex("#4F5B76"),
        ["TextDisabled"] = Hex("#9AA3B8"),
        ["DangerBrush"] = Hex("#C42B40"),
        ["DangerSoft"] = Hex("#FDE7EA"),
        ["SuccessBrush"] = Hex("#127A45"),
        ["SuccessSoft"] = Hex("#E0F5EA"),
        ["WarningBrush"] = Hex("#9A5A00"),
        ["WarningSoft"] = Hex("#FFF1DB"),
        ["HoverBorder"] = Hex("#C2CBDD"),
        ["ControlBorder"] = Hex("#B9C2D6"),
        ["ControlBorderStrong"] = Hex("#8792AD"),
        ["DisabledFill"] = Hex("#E3E7EF"),
        ["TrackBackground"] = Hex("#E1E6F0"),
        ["ScrollThumbBrush"] = Hex("#C5CCDB"),
        ["ScrollThumbHoverBrush"] = Hex("#A6AFC3"),
        ["SidebarBackground"] = Hex("#FBFCFE"),
    };

    /// <summary>
    /// Applique le thème et l'accent choisis. À appeler dans App.OnStartup, avant la création de toute fenêtre.
    /// Thème sombre + accent bleu = l'apparence d'origine : rien n'est modifié.
    /// </summary>
    public static void Apply(AppSettings settings)
    {
        try
        {
            IsLight = settings.Theme switch
            {
                Light => true,
                Auto => SystemUsesLightTheme(),
                _ => false,
            };
            var (accent, second, isDefault) = ResolveAccent(settings.Accent);
            if (!IsLight && isDefault)
                return;

            var resources = Application.Current?.Resources;
            if (resources is null)
                return;

            var card = IsLight ? LightPalette["CardBackground"] : DarkCard;

            // Les couleurs (structures) d'abord : les pinceaux qui en dépendent et qui ne sont pas encore créés les reprendront.
            SetColor(resources, "AccentColor", accent);
            SetColor(resources, "AccentCyanColor", second);
            SetColor(resources, "HaloColor", accent);
            SetColor(resources, "HaloEdgeColor", Color.FromArgb(0, accent.R, accent.G, accent.B));
            if (IsLight)
                SetColor(resources, "ShadowColor", Hex("#3A4766"));

            if (IsLight)
            {
                foreach (var (key, color) in LightPalette)
                    SetBrush(resources, key, color);
                WindowBackground = LightPalette["WindowBackground"];
            }

            SetBrush(resources, "AccentBrush", accent);
            SetBrush(resources, "AccentSoft", Mix(accent, card, IsLight ? 0.12 : 0.22));
            SetBrush(resources, "SelectedFill", Mix(accent, card, IsLight ? 0.16 : 0.24));
            SetBrush(resources, "LinkBrush", ReadableOn(accent, card, IsLight ? LightPalette["WindowBackground"] : Hex("#070D1F")));

            var start = isDefault ? Hex("#1670FF") : Mix(accent, Colors.Black, 0.10);
            var startHover = isDefault ? Hex("#2F82FF") : Mix(accent, Colors.White, 0.10);
            var endHover = isDefault ? Hex("#4BE0FF") : Mix(second, Colors.White, 0.18);
            SetGradient(resources, "AccentGradient", start, second);
            SetGradient(resources, "AccentGradientHover", startHover, endHover);

            Journal.Write($"Thème appliqué : {(IsLight ? "clair" : "sombre")}, accent « {settings.Accent} ».");
        }
        catch (Exception ex)
        {
            Journal.Write("Impossible d'appliquer le thème : " + ex.Message);
        }
    }

    /// <summary>Mode des applications de Windows : vrai si Windows est réglé en mode clair.</summary>
    public static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch (Exception ex)
        {
            Journal.Write("Mode clair/sombre de Windows illisible : " + ex.Message);
            return false;
        }
    }

    /// <summary>Couleur d'accent de Windows (DWM, valeur ABGR), ou null si elle n'est pas lisible.</summary>
    public static Color? WindowsAccentColor()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is not int raw)
                return null;
            var value = unchecked((uint)raw);
            return Color.FromRgb((byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF));
        }
        catch (Exception ex)
        {
            Journal.Write("Couleur d'accent de Windows illisible : " + ex.Message);
            return null;
        }
    }

    /// <summary>Couleur principale et secondaire d'un identifiant d'accent ; isDefault quand c'est le bleu d'origine.</summary>
    public static (Color Main, Color Second, bool IsDefault) ResolveAccent(string? id)
    {
        if (id == WindowsAccent && WindowsAccentColor() is { } windows)
            return (windows, SecondFor(windows), false);
        var choice = Accents.FirstOrDefault(a => a.Id == id) ?? Accents[0];
        return (choice.Main, choice.Second, choice.Id == DefaultAccent);
    }

    // ----- Ressources -----

    /// <summary>Le dictionnaire (lui-même ou un dictionnaire fusionné) qui définit la clé ; le dernier fusionné l'emporte.</summary>
    private static ResourceDictionary? FindOwner(ResourceDictionary dictionary, string key)
    {
        if (dictionary.Contains(key))
            return dictionary;
        for (var i = dictionary.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            if (FindOwner(dictionary.MergedDictionaries[i], key) is { } owner)
                return owner;
        }
        return null;
    }

    private static void SetColor(ResourceDictionary resources, string key, Color color) =>
        (FindOwner(resources, key) ?? resources)[key] = color;

    // Les ressources de l'application sont figées (Freeze) dès qu'on les lit : en général l'entrée est donc remplacée,
    // ce qui suffit puisque aucune fenêtre n'a encore résolu ses StaticResource. Un objet non figé est modifié sur place.
    private static void SetBrush(ResourceDictionary resources, string key, Color color)
    {
        var owner = FindOwner(resources, key);
        if (owner?[key] is SolidColorBrush { IsFrozen: false } brush)
        {
            brush.Color = color;
            return;
        }
        (owner ?? resources)[key] = new SolidColorBrush(color);
    }

    private static void SetGradient(ResourceDictionary resources, string key, Color start, Color end)
    {
        var owner = FindOwner(resources, key);
        if (owner?[key] is LinearGradientBrush { IsFrozen: false } brush && brush.GradientStops.Count >= 2 &&
            !brush.GradientStops.IsFrozen && brush.GradientStops.All(s => !s.IsFrozen))
        {
            brush.GradientStops[0].Color = start;
            brush.GradientStops[^1].Color = end;
            return;
        }
        var replacement = owner?[key] is LinearGradientBrush existing
            ? existing.CloneCurrentValue()
            : new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        if (replacement.GradientStops.Count < 2)
        {
            replacement.GradientStops.Clear();
            replacement.GradientStops.Add(new GradientStop(start, 0));
            replacement.GradientStops.Add(new GradientStop(end, 1));
        }
        else
        {
            replacement.GradientStops[0].Color = start;
            replacement.GradientStops[^1].Color = end;
        }
        (owner ?? resources)[key] = replacement;
    }

    // ----- Couleurs -----

    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// <summary>Mélange : amount = part de a (0 → b, 1 → a).</summary>
    public static Color Mix(Color a, Color b, double amount) => Color.FromRgb(
        (byte)Math.Round(a.R * amount + b.R * (1 - amount)),
        (byte)Math.Round(a.G * amount + b.G * (1 - amount)),
        (byte)Math.Round(a.B * amount + b.B * (1 - amount)));

    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var x = v / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    /// <summary>Rapport de contraste WCAG entre deux couleurs.</summary>
    public static double Contrast(Color a, Color b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Variante de l'accent lisible comme texte (≥ 4,5:1) sur les deux fonds : assombrie en clair, éclaircie en sombre.</summary>
    private static Color ReadableOn(Color accent, Color background1, Color background2)
    {
        var towards = Luminance(background1) > 0.5 ? Colors.Black : Colors.White;
        var color = Luminance(background1) > 0.5 ? accent : Mix(accent, Colors.White, 0.7);
        for (var i = 0; i < 20 && (Contrast(color, background1) < 4.6 || Contrast(color, background2) < 4.6); i++)
            color = Mix(color, towards, 0.92);
        return color;
    }

    /// <summary>Couleur secondaire du dégradé pour une couleur quelconque : teinte décalée et plus lumineuse.</summary>
    private static Color SecondFor(Color c)
    {
        var (h, s, l) = ToHsl(c);
        return FromHsl((h + 22) % 360, Math.Min(1, s * 1.05), Math.Min(0.72, l + 0.14));
    }

    private static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max - min < 1e-9)
            return (0, 0, l);
        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1 / 2.0) return q;
            if (t < 2 / 3.0) return p + (q - p) * (2 / 3.0 - t) * 6;
            return p;
        }
        if (s < 1e-9)
        {
            var v = (byte)Math.Round(l * 255);
            return Color.FromRgb(v, v, v);
        }
        var q2 = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p2 = 2 * l - q2;
        var hh = h / 360;
        return Color.FromRgb(
            (byte)Math.Round(Hue(p2, q2, hh + 1 / 3.0) * 255),
            (byte)Math.Round(Hue(p2, q2, hh) * 255),
            (byte)Math.Round(Hue(p2, q2, hh - 1 / 3.0) * 255));
    }
}
