using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Desinstalleur.Core;

namespace Desinstalleur.App.Services;

/// <summary>
/// Traduction de l'interface. Le texte source est le français ; en anglais, chaque texte est
/// cherché dans le dictionnaire <see cref="English"/> (L.En.cs). Un texte absent reste en français.
/// Les textes écrits en dur dans le XAML sont traduits automatiquement au chargement des éléments.
/// </summary>
public static partial class L
{
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);
    private static Dictionary<string, string>? _table;

    /// <summary>Vrai quand l'interface est en anglais.</summary>
    public static bool IsEnglish { get; private set; }

    /// <summary>Culture utilisée pour les nombres et les dates des textes formatés.</summary>
    public static CultureInfo Culture => IsEnglish ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Choisit la langue : « fr », « en », ou « auto » (français seulement si Windows est en français).</summary>
    public static void Init(string? preference)
    {
        IsEnglish = preference switch
        {
            "fr" => false,
            "en" => true,
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "fr",
        };
        ValueParsers.EnglishUnits = IsEnglish;
        if (!IsEnglish)
            return;

        _table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (fr, en) in English())
            _table[Normalize(fr)] = en;
        HookUi();
    }

    /// <summary>Traduit un texte français (inchangé en français ou s'il n'est pas dans le dictionnaire).</summary>
    public static string T(string fr)
    {
        if (_table is null || string.IsNullOrWhiteSpace(fr))
            return fr;
        if (_table.TryGetValue(fr, out var en))
            return en;
        var key = Normalize(fr);
        if (key.Length == fr.Length || !_table.TryGetValue(key, out en))
            return fr;
        // Garde les espaces autour du texte (morceaux de phrase dans des Run).
        var start = fr.Length - fr.TrimStart().Length;
        var end = fr.Length - fr.TrimEnd().Length;
        return (start > 0 ? " " : "") + en + (end > 0 ? " " : "");
    }

    /// <summary>Traduit un texte avec des {0}, {1}… puis le remplit.</summary>
    public static string F(string fr, params object?[] args) => string.Format(Culture, T(fr), args);

    private static string Normalize(string s) => Spaces.Replace(s, " ").Trim();

    // ---- Traduction automatique des textes du XAML ----

    private static void HookUi()
    {
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => { if (s is DependencyObject d) Translate(d); }), true);
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent,
            new RoutedEventHandler((s, _) => { if (s is ContextMenu m) TranslateMenu(m); }), true);
    }

    /// <summary>Traduit un élément de l'interface (appelé à chaque chargement, sans effet s'il est déjà traduit).</summary>
    public static void Translate(DependencyObject d)
    {
        switch (d)
        {
            case TextBlock tb:
                if (!IsFixed(tb, TextBlock.TextProperty))
                    break;
                foreach (var inline in tb.Inlines.ToList())
                    TranslateInline(inline);
                break;
            case HeaderedContentControl hc:
                TranslateProp(hc, HeaderedContentControl.HeaderProperty);
                TranslateProp(hc, ContentControl.ContentProperty);
                break;
            case HeaderedItemsControl hi:
                TranslateProp(hi, HeaderedItemsControl.HeaderProperty);
                break;
            case ContentControl cc:
                TranslateProp(cc, ContentControl.ContentProperty);
                if (cc is Window w)
                    TranslateProp(w, Window.TitleProperty);
                break;
            case TextBox box:
                TranslateProp(box, FrameworkElement.TagProperty);
                break;
            case DataGrid grid:
                foreach (var column in grid.Columns)
                    if (column.Header is string h)
                        column.Header = T(h);
                break;
        }

        TranslateProp(d, FrameworkElement.ToolTipProperty);
        if (d is FrameworkElement { ContextMenu: { } menu })
            TranslateMenu(menu);
    }

    private static void TranslateMenu(ContextMenu menu)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            TranslateProp(item, HeaderedItemsControl.HeaderProperty);
            TranslateProp(item, FrameworkElement.ToolTipProperty);
        }
    }

    private static void TranslateInline(Inline inline)
    {
        switch (inline)
        {
            case Run run:
                TranslateProp(run, Run.TextProperty);
                break;
            case Span span:
                foreach (var child in span.Inlines.ToList())
                    TranslateInline(child);
                break;
        }
    }

    private static void TranslateProp(DependencyObject d, DependencyProperty p)
    {
        if (d.GetValue(p) is not string text || text.Length == 0 || !IsFixed(d, p))
            return;
        var translated = T(text);
        if (!ReferenceEquals(translated, text) && translated != text)
            d.SetValue(p, translated);
    }

    /// <summary>Vrai si la valeur est écrite en dur (ni liaison, ni déclencheur de style).</summary>
    private static bool IsFixed(DependencyObject d, DependencyProperty p)
    {
        var source = DependencyPropertyHelper.GetValueSource(d, p);
        if (source.IsExpression || source.IsAnimated || source.IsCoerced)
            return false;
        return source.BaseValueSource is BaseValueSource.Local or BaseValueSource.ParentTemplate
            or BaseValueSource.Style or BaseValueSource.Default or BaseValueSource.Inherited
            or BaseValueSource.DefaultStyle or BaseValueSource.ImplicitStyleReference;
    }
}
