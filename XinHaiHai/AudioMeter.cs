using System.Runtime.InteropServices;

namespace XinHaiHai;

/// <summary>
/// 通过 Windows Core Audio 读取默认输出设备的峰值电平,判断“此刻是否有声音在播放”。
/// 配合“已知听歌软件在运行”即可判定使用者正在听歌 → 切换听歌模式。
/// </summary>
public static class AudioMeter
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        int NotImpl1();
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        int GetPeakValue(out float pfPeak);
    }

    static readonly Guid IID_IAudioMeterInformation = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");

    /// <summary>返回当前系统输出峰值 0~1;取不到时返回 -1。</summary>
    public static float Peak()
    {
        IMMDeviceEnumerator en = null;
        try
        {
            en = (IMMDeviceEnumerator)(new MMDeviceEnumerator());
            if (en.GetDefaultAudioEndpoint(0 /*eRender*/, 0 /*eConsole*/, out var dev) != 0 || dev == null)
                return -1;
            var iid = IID_IAudioMeterInformation;
            if (dev.Activate(ref iid, 1 /*CLSCTX_INPROC_SERVER*/, IntPtr.Zero, out object o) != 0 || o == null)
                return -1;
            var meter = (IAudioMeterInformation)o;
            return meter.GetPeakValue(out float peak) == 0 ? peak : -1;
        }
        catch { return -1; }
        finally { if (en != null) Marshal.ReleaseComObject(en); }
    }
}
