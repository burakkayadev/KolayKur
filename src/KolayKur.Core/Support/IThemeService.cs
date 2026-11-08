namespace KolayKur.Core.Support;

/// <summary>Seçilen temayı arayüze uygular. Çekirdek WPF'ten bağımsız kalsın diye uygulaması WPF katmanındadır.</summary>
public interface IThemeService
{
    void Apply(AppTheme theme);
}
