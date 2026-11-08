using System.Windows;
using System.Windows.Controls;
using KolayKur.Core.Support;

namespace KolayKur.App.Views;

/// <summary>40 px harf avatarı: harf adtan, renk kimlikten (<see cref="PackageAvatar"/>).</summary>
public partial class PackageAvatarView : UserControl
{
    public static readonly DependencyProperty PackageIdProperty = DependencyProperty.Register(
        nameof(PackageId), typeof(string), typeof(PackageAvatarView),
        new PropertyMetadata(string.Empty, (d, _) => ((PackageAvatarView)d).Refresh()));

    public static readonly DependencyProperty PackageNameProperty = DependencyProperty.Register(
        nameof(PackageName), typeof(string), typeof(PackageAvatarView),
        new PropertyMetadata(string.Empty, (d, _) => ((PackageAvatarView)d).Refresh()));

    public PackageAvatarView()
    {
        InitializeComponent();

        Refresh();
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

    private void Refresh()
    {
        var info = PackageAvatar.For(PackageId ?? string.Empty, PackageName ?? string.Empty);
        LetterText.Text = info.Letter;

        // DynamicResource gibi: tema değişince fırça sözlükten yeniden çözülür.
        Badge.SetResourceReference(Border.BackgroundProperty, $"AvatarBrush{info.ColorIndex}");
    }
}
