using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KolayKur.App.Icons;
using KolayKur.Core.Models;
using KolayKur.Core.Support;

namespace KolayKur.App.Views;

/// <summary>
/// 40 px avatar: önce harf (harf adtan, renk kimlikten, <see cref="PackageAvatar"/>); görünür olunca <see cref="AppIcons.Service"/>
/// uygulamanın gerçek simgesini bulursa simge gösterilir. Kimlik değişince (liste sanallaştırması) eski istek iptal edilir.
/// </summary>
public partial class PackageAvatarView : UserControl
{
    public static readonly DependencyProperty PackageIdProperty = DependencyProperty.Register(
        nameof(PackageId), typeof(string), typeof(PackageAvatarView),
        new PropertyMetadata(string.Empty, (d, _) => ((PackageAvatarView)d).Refresh()));

    public static readonly DependencyProperty PackageNameProperty = DependencyProperty.Register(
        nameof(PackageName), typeof(string), typeof(PackageAvatarView),
        new PropertyMetadata(string.Empty, (d, _) => ((PackageAvatarView)d).Refresh()));

    public static readonly DependencyProperty PackageSourceProperty = DependencyProperty.Register(
        nameof(PackageSource), typeof(string), typeof(PackageAvatarView),
        new PropertyMetadata(null, (d, _) => ((PackageAvatarView)d).Refresh()));

    public static readonly DependencyProperty PackageIsInstalledProperty = DependencyProperty.Register(
        nameof(PackageIsInstalled), typeof(bool), typeof(PackageAvatarView),
        new PropertyMetadata(false, (d, _) => ((PackageAvatarView)d).Refresh()));

    private CancellationTokenSource? loadCts;
    // IconService yalnızca "kurulu mu" bilgisini kullanır; sürüm metni anlamsızdır.
    private const string InstalledMarker = "installed";

    private bool loadQueued;
    private string iconFor = string.Empty;

    public PackageAvatarView()
    {
        InitializeComponent();

        Refresh();
        Loaded += (_, _) => QueueLoad();
        Unloaded += (_, _) => CancelLoad();
    }

    public string PackageId
    {
        get => (string)GetValue(PackageIdProperty);
        set => SetValue(PackageIdProperty, value);
    }

    public string PackageName
    {
        get => (string)GetValue(PackageNameProperty);
        set => SetValue(PackageNameProperty, value);
    }

    /// <summary>Paketin kaynağı (<see cref="PackageSources"/>); yoksa <c>null</c>.</summary>
    public string? PackageSource
    {
        get => (string?)GetValue(PackageSourceProperty);
        set => SetValue(PackageSourceProperty, value);
    }

    /// <summary>Paket kurulu mu: "simge yok" kaydı kurulu ve kurulu olmayan hâl için ayrı tutulur.</summary>
    public bool PackageIsInstalled
    {
        get => (bool)GetValue(PackageIsInstalledProperty);
        set => SetValue(PackageIsInstalledProperty, value);
    }

    private void Refresh()
    {
        var info = PackageAvatar.For(PackageId ?? string.Empty, PackageName ?? string.Empty);
        LetterText.Text = info.Letter;

        // DynamicResource gibi: tema değişince fırça sözlükten yeniden çözülür.
        Badge.SetResourceReference(Border.BackgroundProperty, $"AvatarBrush{info.ColorIndex}");
        if (IconLayer.Visibility == Visibility.Visible && string.Equals(iconFor, PackageId, StringComparison.Ordinal))
        {
            Badge.SetResourceReference(Border.BackgroundProperty, "IconPlateBrush");
        }

        // Başka bir pakete geçildiyse (kart geri dönüştürüldü) önceki paketin simgesi kalmasın.
        if (!string.Equals(iconFor, PackageId, StringComparison.Ordinal))
        {
            ShowLetter();
        }

        if (IsLoaded)
        {
            QueueLoad();
        }
    }

    private void ShowLetter()
    {
        IconLayer.Background = null;
        IconLayer.Visibility = Visibility.Collapsed;
        LetterText.Visibility = Visibility.Visible;
        iconFor = string.Empty;
    }

    // Kimlik, ad ve kaynak ardışık değişir; tek bir istek için isteği arka plan önceliğine erteleyip birleştirir.
    private void QueueLoad()
    {
        CancelLoad();
        if (loadQueued || AppIcons.Service is null || string.IsNullOrEmpty(PackageId))
        {
            return;
        }

        loadQueued = true;
        Dispatcher.BeginInvoke(StartLoad, System.Windows.Threading.DispatcherPriority.Background);
    }

    private async void StartLoad()
    {
        loadQueued = false;
        var service = AppIcons.Service;
        var id = PackageId;
        if (!IsLoaded || service is null || string.IsNullOrEmpty(id))
        {
            return;
        }

        var cts = loadCts = new CancellationTokenSource();
        var token = cts.Token;
        try
        {
            var package = new PackageInfo(id, PackageName ?? string.Empty, string.Empty, PackageIsInstalled ? InstalledMarker : null, null, PackageSource);
            var path = await service.GetIconPathAsync(package, token);
            if (path is null || token.IsCancellationRequested)
            {
                return;
            }

            // Dosya okuması ve çözme arka planda; donmuş görüntü arayüz iş parçacığına geçer.
            var image = await Task.Run(() => LoadFrozen(path), token);
            if (image is null)
            {
                // Başlığı geçerli ama gövdesi bozuk dosya: harf kalır, dosya silinir ve sonraki istekte yeniden bulunur.
                service.ReportBroken(path);
                return;
            }

            if (!token.IsCancellationRequested)
            {
                IconLayer.Background = new ImageBrush(image) { Stretch = Stretch.Uniform };
                Badge.SetResourceReference(Border.BackgroundProperty, "IconPlateBrush");
                LetterText.Visibility = Visibility.Collapsed;
                IconLayer.Visibility = Visibility.Visible;
                iconFor = id;
            }
        }
        catch (OperationCanceledException)
        {
            // Kart gizlendi ya da başka pakete geçti.
        }
        catch (Exception)
        {
            // async void: hiçbir hata arayüz iş parçacığına sızıp hata penceresini açmamalı; simge her zaman isteğe bağlıdır, harf kalır.
        }
    }

    private static BitmapImage? LoadFrozen(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 80;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            // FileFormatException, COMException, bozuk ya da kilitli dosya: hepsi "gösterilemiyor" demektir.
            return null;
        }
    }

    private void CancelLoad()
    {
        // Bağlantılı zamanlayıcısı olmayan kaynak atılmak zorunda değil; atılırsa süren isteğin belirteci kullanılamaz hâle gelirdi.
        loadCts?.Cancel();
        loadCts = null;
    }
}
