using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Desinstalleur.Core;

namespace Desinstalleur.App.Services;

/// <summary>
/// Petites animations de l'interface (apparitions, compteurs, barres).
/// Toutes se désactivent avec le réglage « Animations » : l'état final est alors appliqué directement.
/// </summary>
public static class Anim
{
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    public static bool Enabled => AppSettings.Current.EnableAnimations;

    /// <summary>Fondu + léger glissement vers le haut (pages, panneaux, messages).</summary>
    public static void FadeSlideIn(UIElement element, double fromY = 14, int durationMs = 320, int delayMs = 0)
    {
        if (!Enabled)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            return;
        }

        var translate = element.RenderTransform as TranslateTransform;
        if (translate is null || translate.IsFrozen)
        {
            translate = new TranslateTransform();
            element.RenderTransform = translate;
        }

        var duration = TimeSpan.FromMilliseconds(durationMs);
        var begin = TimeSpan.FromMilliseconds(delayMs);
        element.Opacity = 0;
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, duration) { BeginTime = begin, EasingFunction = EaseOut });
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(fromY, 0, duration) { BeginTime = begin, EasingFunction = EaseOut });
    }

    /// <summary>Glissement horizontal (menu latéral à l'ouverture).</summary>
    public static void SlideInFromLeft(UIElement element, double fromX = -30, int durationMs = 450)
    {
        if (!Enabled)
            return;
        var translate = new TranslateTransform(fromX, 0);
        element.RenderTransform = translate;
        var duration = TimeSpan.FromMilliseconds(durationMs);
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = EaseOut });
        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, duration) { EasingFunction = EaseOut });
    }

    /// <summary>Apparition « pop » avec rebond (coche de réussite).</summary>
    public static void Pop(UIElement element, int delayMs = 0)
    {
        if (!Enabled)
            return;
        var scale = new ScaleTransform(0.4, 0.4);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = scale;
        var animation = new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(520))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 4 },
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { BeginTime = TimeSpan.FromMilliseconds(delayMs) });
    }

    /// <summary>Change la largeur en douceur (barre de RAM).</summary>
    public static void Width(FrameworkElement element, double to, int durationMs = 600)
    {
        if (double.IsNaN(element.Width))
            element.Width = 0;
        if (!Enabled)
        {
            element.BeginAnimation(FrameworkElement.WidthProperty, null);
            element.Width = to;
            return;
        }
        element.BeginAnimation(FrameworkElement.WidthProperty,
            new DoubleAnimation(to, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = EaseOut });
    }

    // ----- Compteur de taille animé (ex. « 1,2 Go récupérables ») -----

    private static readonly DependencyProperty BytesProperty = DependencyProperty.RegisterAttached(
        "Bytes", typeof(double), typeof(Anim),
        new PropertyMetadata(0.0, (d, e) =>
        {
            if (d is TextBlock text)
                text.Text = ValueParsers.FormatSize((long)(double)e.NewValue);
        }));

    /// <summary>Fait défiler la taille affichée jusqu'à la nouvelle valeur.</summary>
    public static void CountBytes(TextBlock text, long to, int durationMs = 700)
    {
        if (!Enabled)
        {
            text.BeginAnimation(BytesProperty, null);
            text.SetValue(BytesProperty, (double)to);
            text.Text = ValueParsers.FormatSize(to);
            return;
        }
        text.BeginAnimation(BytesProperty,
            new DoubleAnimation(to, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = EaseOut });
    }

    // ----- Apparition en cascade des éléments d'une liste -----

    /// <summary>
    /// Propriété attachée pour les modèles de liste : &lt;Border services:Anim.EnterOnLoad="True"&gt;.
    /// Chaque élément apparaît avec un petit décalage selon sa position.
    /// </summary>
    public static readonly DependencyProperty EnterOnLoadProperty = DependencyProperty.RegisterAttached(
        "EnterOnLoad", typeof(bool), typeof(Anim), new PropertyMetadata(false, OnEnterOnLoadChanged));

    public static bool GetEnterOnLoad(DependencyObject d) => (bool)d.GetValue(EnterOnLoadProperty);
    public static void SetEnterOnLoad(DependencyObject d, bool value) => d.SetValue(EnterOnLoadProperty, value);

    private static void OnEnterOnLoadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && e.NewValue is true)
            element.Loaded += (_, _) => FadeSlideIn(element, 10, 300, Math.Min(IndexInList(element), 14) * 35);
    }

    private static int IndexInList(DependencyObject element)
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            var owner = ItemsControl.ItemsControlFromItemContainer(current);
            if (owner is not null)
                return Math.Max(0, owner.ItemContainerGenerator.IndexFromContainer(current));
            current = VisualTreeHelper.GetParent(current);
        }
        return 0;
    }
}
