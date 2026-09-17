using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace XinHaiHai;

public class GifAnimation
{
    public List<BitmapSource> Frames { get; set; } = new();
    public List<int> Delays { get; set; } = new();
}

public static class GifLoader
{
    public static GifAnimation LoadGif(string path, bool removeBlackBackground = true)
    {
        var anim = new GifAnimation();
        try
        {
            using var fs = File.OpenRead(path);
            var decoder = new GifBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            foreach (var frame in decoder.Frames)
            {
                BitmapSource source = frame;
                if (removeBlackBackground)
                {
                    source = ProcessBlackToTransparent(source);
                }
                source.Freeze();
                anim.Frames.Add(source);
                anim.Delays.Add(GetFrameDelay(frame));
            }
        }
        catch { }
        return anim;
    }

    static int GetFrameDelay(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata meta && meta.ContainsQuery("/grctdict/Delay"))
            {
                object delayObj = meta.GetQuery("/grctdict/Delay");
                if (delayObj is ushort delayUs)
                {
                    int ms = delayUs * 10;
                    return ms > 0 ? ms : 100;
                }
            }
        }
        catch { }
        return 100;
    }

    public static BitmapSource ProcessBlackToTransparent(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = converted.PixelWidth;
        int height = converted.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        converted.CopyPixels(pixels, stride, 0);

        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte b = pixels[i];
            byte g = pixels[i + 1];
            byte r = pixels[i + 2];
            // 默认黑色背景(#000000或极暗灰低于15)处理为透明背景
            if (r <= 15 && g <= 15 && b <= 15)
            {
                pixels[i + 3] = 0; // Alpha 设为透明
            }
        }

        var writeable = new WriteableBitmap(width, height, converted.DpiX, converted.DpiY, PixelFormats.Bgra32, null);
        writeable.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        return writeable;
    }
}
