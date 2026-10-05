using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CanvasForge.Core;
using SkiaSharp;

namespace CanvasForge.App;
internal static class Images
{
    public static PixelImage Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Unsupported image format.");
        var info = codec.Info;
        if ((long)info.Width * info.Height > 100_000_000)
            throw new InvalidDataException("Image exceeds 100 million pixels.");
        var max = 2048;
        var scale = Math.Min(1, max / (double)Math.Max(info.Width, info.Height));
        var size = codec.GetScaledDimensions((float)scale);
        using var bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
        if (result is not SKCodecResult.Success and not SKCodecResult.IncompleteInput)
        {
            using var full = SKBitmap.Decode(path) ?? throw new InvalidDataException("Cannot decode image.");
            using var resized = full.Resize(bitmap.Info, SKFilterQuality.High) ?? throw new InvalidDataException("Cannot resize image.");
            return FromSkia(resized);
        }

        if (Math.Max(bitmap.Width, bitmap.Height) > max)
        {
            var factor = max / (double)Math.Max(bitmap.Width, bitmap.Height);
            using var resized = bitmap.Resize(new SKImageInfo(Math.Max(1, (int)(bitmap.Width * factor)), Math.Max(1, (int)(bitmap.Height * factor)), SKColorType.Rgba8888, SKAlphaType.Unpremul), SKFilterQuality.High) ?? throw new InvalidDataException("Cannot resize image.");
            return FromSkia(resized);
        }

        return FromSkia(bitmap);
    }

    private static PixelImage FromSkia(SKBitmap bitmap)
    {
        var bytes = new byte[bitmap.Width * bitmap.Height * 4];
        for (var y = 0; y < bitmap.Height; y++)
            Marshal.Copy(bitmap.GetPixels() + y * bitmap.RowBytes, bytes, y * bitmap.Width * 4, bitmap.Width * 4);
        return new(bitmap.Width, bitmap.Height, bytes);
    }

    // WPF has no byte RGBA PixelFormat. Convert to BGRA32 for low-memory preview.
    public static BitmapSource Bitmap(PixelImage image)
    {
        var bytes = (byte[])image.Rgba.Clone();
        for (var i = 0; i < bytes.Length; i += 4)
            (bytes[i], bytes[i + 2]) = (bytes[i + 2], bytes[i]);
        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, bytes, image.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    public static void Save(PixelImage image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Bitmap(image)));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    public static PixelImage MaterialPreview(PixelImage canvas, PaintPlan plan)
    {
        var result = canvas.Clone();
        for (var y = 0; y < canvas.Height; y++)
            for (var x = 0; x < canvas.Width; x++)
            {
                var gx = Math.Min(plan.Width - 1, x * plan.Width / canvas.Width);
                var gy = Math.Min(plan.Height - 1, y * plan.Height / canvas.Height);
                var idx = plan.Indices[gy * plan.Width + gx];
                if (idx < 0)
                    continue;
                result.Set(y * canvas.Width + x, plan.Palette[idx].Color);
            }

        return result;
    }
}

