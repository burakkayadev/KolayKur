using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Kullanım: IconGen <Logo.xaml> <KolayKur.ico>
// Logo geometrisi yalnızca Logo.xaml'dadır; burada kopyalanmaz.
return Run(args);

static int Run(string[] args)
{
    if (args.Length != 2)
    {
        Console.Error.WriteLine("Kullanim: IconGen <Logo.xaml> <cikti.ico>");
        return 2;
    }

    int exitCode = 0;
    var thread = new Thread(() =>
    {
        try
        {
            Generate(args[0], args[1]);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            exitCode = 1;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    return exitCode;
}

static void Generate(string xamlPath, string icoPath)
{
    ResourceDictionary dictionary;
    using (var stream = File.OpenRead(xamlPath))
    {
        dictionary = (ResourceDictionary)XamlReader.Load(stream);
    }

    var full = (ImageSource)dictionary["LogoImage"];
    var small = (ImageSource)dictionary["LogoSmallImage"];

    // 24 px ve altı rozetsiz küçük logo, üstü tam logo.
    (int Size, ImageSource Source)[] frames =
    [
        (16, small), (20, small), (24, small),
        (32, full), (40, full), (48, full), (64, full), (256, full),
    ];

    var pngs = frames.Select(f => (f.Size, Png: RenderPng(f.Source, f.Size))).ToList();

    var directory = Path.GetDirectoryName(Path.GetFullPath(icoPath));
    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
    WriteIco(icoPath, pngs);
    Console.WriteLine($"{icoPath}: {pngs.Count} kare yazildi.");
}

static byte[] RenderPng(ImageSource source, int size)
{
    var visual = new DrawingVisual();
    RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
    using (var context = visual.RenderOpen())
    {
        context.DrawImage(source, new Rect(0, 0, size, size));
    }

    var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(visual);

    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var memory = new MemoryStream();
    encoder.Save(memory);
    return memory.ToArray();
}

static void WriteIco(string path, List<(int Size, byte[] Png)> images)
{
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);

    // ICONDIR
    writer.Write((ushort)0);
    writer.Write((ushort)1);
    writer.Write((ushort)images.Count);

    var offset = 6 + 16 * images.Count;
    foreach (var (size, png) in images)
    {
        // ICONDIRENTRY: 256 px için genişlik/yükseklik baytı 0'dır.
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)0);   // renk sayısı
        writer.Write((byte)0);   // ayrılmış
        writer.Write((ushort)1); // renk düzlemi
        writer.Write((ushort)32); // bit/piksel
        writer.Write((uint)png.Length);
        writer.Write((uint)offset);
        offset += png.Length;
    }

    foreach (var (_, png) in images) writer.Write(png);
}
