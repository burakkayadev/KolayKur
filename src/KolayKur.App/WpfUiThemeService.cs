using System.Windows;
using System.Windows.Media;
using KolayKur.Core.Support;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace KolayKur.App;

/// <summary>
/// <see cref="IThemeService"/>'in WPF-UI gerçekleştirmesi. Tema hangi yoldan değişirse değişsin (Ayarlar'dan ya da
/// Sistem modunda Windows'tan) <see cref="ApplicationThemeManager.Changed"/> tetiklenir; marka sözlüğü
/// (<c>Brand.Light</c>/<c>Brand.Dark</c>) ve WPF-UI vurgu rengi o olayda marka rengine ayarlanır.
/// Windows'un vurgu rengi hiçbir zaman kullanılmaz.
/// </summary>
public sealed class WpfUiThemeService : IThemeService
{
    private const WindowBackdropType Backdrop = WindowBackdropType.Mica;
    private const string LightDictionary = "Themes/Brand.Light.xaml";
    private const string DarkDictionary = "Themes/Brand.Dark.xaml";

    private static readonly Color LightAccent = Color.FromRgb(0x0E, 0x7C, 0x7B);
    private static readonly Color DarkAccent = Color.FromRgb(0x3D, 0xD6, 0xC6);

    private Window? window;
    private AppTheme requested = AppTheme.System;

    public WpfUiThemeService()
    {
        ApplicationThemeManager.Changed += OnThemeChanged;
    }

    /// <summary>
    /// Pencere oluşunca çağrılır: Mica'yı etkinleştirir ve Sistem modundaysa Windows tema değişimini izlemeye başlar.
    /// </summary>
    public void Attach(Window mainWindow)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        window = mainWindow;
        ApplyBackdrop(mainWindow);

        // SystemThemeWatcher yalnızca yüklenmiş pencereyle çalışır (UnWatch yüklenmemişte hata verir).
        mainWindow.Loaded += (_, _) => UpdateWatching();
        UpdateWatching();
    }

    public void Apply(AppTheme theme)
    {
        requested = theme;
        UpdateWatching();

        var target = theme switch
        {
            AppTheme.Light => ApplicationTheme.Light,
            AppTheme.Dark => ApplicationTheme.Dark,
            _ => ApplicationThemeManager.GetSystemTheme() is SystemTheme.Dark or SystemTheme.HCBlack
                ? ApplicationTheme.Dark
                : ApplicationTheme.Light,
        };

        // updateAccent: false — vurgu rengi Changed işleyicisinde marka rengi olarak ayarlanır, Windows vurgusu uygulanmaz.
        ApplicationThemeManager.Apply(target, Backdrop, updateAccent: false);

        // Tema zaten aynıysa Changed gelmeyebilir; marka sözlüğü her durumda doğru olmalı.
        ApplyBrand(target);
    }

    private void UpdateWatching()
    {
        if (window is not { IsLoaded: true })
        {
            return;
        }

        SystemThemeWatcher.UnWatch(window);
        if (requested == AppTheme.System)
        {
            SystemThemeWatcher.Watch(window, Backdrop, updateAccents: false);
        }
    }

    private static void ApplyBackdrop(Window target)
    {
        if (target is FluentWindow fluent)
        {
            fluent.WindowBackdropType = Backdrop;
        }
    }

    private static ApplicationTheme ToLightOrDark(ApplicationTheme theme) =>
        theme == ApplicationTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light;

    private void OnThemeChanged(ApplicationTheme theme, Color systemAccent) =>
        ApplyBrand(ToLightOrDark(theme));

    private static void ApplyBrand(ApplicationTheme theme)
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        var dark = theme == ApplicationTheme.Dark;
        SwapBrandDictionary(application.Resources.MergedDictionaries, dark);
        ApplicationAccentColorManager.Apply(dark ? DarkAccent : LightAccent, dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
    }

    private static void SwapBrandDictionary(System.Collections.ObjectModel.Collection<ResourceDictionary> dictionaries, bool dark)
    {
        var wanted = dark ? DarkDictionary : LightDictionary;
        var existing = dictionaries.FirstOrDefault(IsBrandDictionary);
        if (existing is not null && SourceEndsWith(existing, wanted))
        {
            return;
        }

        var replacement = new ResourceDictionary { Source = new Uri($"pack://application:,,,/KolayKur;component/{wanted}", UriKind.Absolute) };
        if (existing is null)
        {
            dictionaries.Add(replacement);
            return;
        }

        // Aynı sıraya koyulur: Brand sözlüğü WPF-UI sözlüklerinden sonra gelir ve ApplicationBackgroundBrush'ı geçersiz kılar.
        dictionaries[dictionaries.IndexOf(existing)] = replacement;
    }

    private static bool IsBrandDictionary(ResourceDictionary dictionary) =>
        SourceEndsWith(dictionary, LightDictionary) || SourceEndsWith(dictionary, DarkDictionary);

    private static bool SourceEndsWith(ResourceDictionary dictionary, string path) =>
        dictionary.Source?.OriginalString.EndsWith(path, StringComparison.OrdinalIgnoreCase) == true;
}
