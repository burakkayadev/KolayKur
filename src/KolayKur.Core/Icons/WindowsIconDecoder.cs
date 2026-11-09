using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace KolayKur.Core.Icons;

/// <summary>
/// Windows'un kendi resim çözücüsüyle (WIC; PNG, ICO, JPEG, BMP) baytları çözüp PNG'ye çevirir. ICO'da en büyük kare seçilir,
/// 256 pikselden büyük resimler küçültülür. Resim olmayan, bozuk ya da 16 pikselden küçük veri için <c>null</c>.
/// Kodlama/çözme işi iş parçacığı havuzunda yapılır; arayüz iş parçacığına dokunmaz.
/// </summary>
public sealed class WindowsIconDecoder : IIconDecoder
{
    public async Task<byte[]?> ToPngAsync(byte[] data, CancellationToken ct)
    {
        try
        {
            return await Task.Run(() => ConvertAsync(data, ct), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException or NotSupportedException or IOException)
        {
            // Çözülemeyen (resim olmayan ya da bozuk) veri "geçersiz" demektir.
            return null;
        }
    }

    private static async Task<byte[]?> ConvertAsync(byte[] data, CancellationToken ct)
    {
        using var input = new InMemoryRandomAccessStream();
        await input.WriteAsync(data.AsBuffer()).AsTask(ct).ConfigureAwait(false);
        input.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(input).AsTask(ct).ConfigureAwait(false);
        var frame = await LargestFrameAsync(decoder, ct).ConfigureAwait(false);
        var (width, height) = (frame.PixelWidth, frame.PixelHeight);
        if (width < IconLimits.MinPixels || height < IconLimits.MinPixels)
        {
            return null;
        }

        var scale = Math.Min(1.0, (double)IconLimits.MaxPixels / Math.Max(width, height));
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, Math.Round(width * scale)),
            ScaledHeight = (uint)Math.Max(1, Math.Round(height * scale)),
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        var pixels = await frame.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)
            .AsTask(ct).ConfigureAwait(false);

        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output).AsTask(ct).ConfigureAwait(false);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform.ScaledWidth, transform.ScaledHeight, 96, 96, pixels.DetachPixelData());
        await encoder.FlushAsync().AsTask(ct).ConfigureAwait(false);

        var png = new byte[output.Size];
        output.Seek(0);
        await output.ReadAsync(png.AsBuffer(), (uint)png.Length, InputStreamOptions.None).AsTask(ct).ConfigureAwait(false);
        return png;
    }

    private static async Task<BitmapFrame> LargestFrameAsync(BitmapDecoder decoder, CancellationToken ct)
    {
        BitmapFrame? best = null;
        for (uint i = 0; i < decoder.FrameCount; i++)
        {
            var frame = await decoder.GetFrameAsync(i).AsTask(ct).ConfigureAwait(false);
            if (best is null || (long)frame.PixelWidth * frame.PixelHeight > (long)best.PixelWidth * best.PixelHeight)
            {
                best = frame;
            }
        }

        return best ?? throw new InvalidOperationException("image has no frames");
    }
}
