using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

namespace WowQueueAgent.Capture;

public sealed class WindowFinder
{
    readonly HashSet<string> _processes;
    readonly HashSet<string> _titles;

    public WindowFinder(AgentConfig cfg)
    {
        _processes = new(cfg.ProcessNames, StringComparer.OrdinalIgnoreCase);
        _titles = new(cfg.WindowTitles, StringComparer.OrdinalIgnoreCase);
    }

    public IntPtr Find()
    {
        IntPtr best = IntPtr.Zero;
        long bestArea = 0;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;
            if (!Matches(hwnd)) return true;
            NativeMethods.GetClientRect(hwnd, out var r);
            long area = (long)r.Right * r.Bottom;
            if (area > bestArea) { best = hwnd; bestArea = area; }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    bool Matches(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(256);
        NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        if (_titles.Contains(sb.ToString())) return true;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return _processes.Contains(p.ProcessName);
        }
        catch { return false; }
    }
}

public sealed class WindowCapturer : IDisposable
{
    const DirectXPixelFormat Format = DirectXPixelFormat.B8G8R8A8UIntNormalized;

    IDirect3DDevice? _device;
    Direct3D11CaptureFramePool? _pool;
    GraphicsCaptureSession? _session;
    GraphicsCaptureItem? _item;
    SizeInt32 _size;
    SoftwareBitmap? _last;
    volatile bool _closed;

    public IntPtr Hwnd { get; private set; }
    public bool IsActive => _session != null && !_closed;
    public long FrameVersion { get; private set; }

    public bool Start(IntPtr hwnd)
    {
        Stop();
        _item = CaptureInterop.CreateItemForWindow(hwnd);
        if (_item == null) return false;
        _device ??= CaptureInterop.CreateDevice();
        _size = _item.Size;
        _closed = false;
        _item.Closed += (_, _) => _closed = true;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, Format, 2, _size);
        _session = _pool.CreateCaptureSession(_item);
        _session.IsCursorCaptureEnabled = false;
        _session.StartCapture();
        Hwnd = hwnd;
        return true;
    }

    public void Stop()
    {
        _session?.Dispose(); _session = null;
        _pool?.Dispose(); _pool = null;
        _item = null;
        _last?.Dispose(); _last = null;
        Hwnd = IntPtr.Zero;
        FrameVersion = 0;
    }

    // Returns the most recent frame; frames only arrive when the window content changes.
    public async Task<SoftwareBitmap?> GrabAsync()
    {
        if (_pool == null || _device == null) return null;

        Direct3D11CaptureFrame? newest = null;
        while (_pool.TryGetNextFrame() is { } f) { newest?.Dispose(); newest = f; }

        if (newest != null)
        {
            if (newest.ContentSize.Width != _size.Width || newest.ContentSize.Height != _size.Height)
            {
                _size = newest.ContentSize;
                _pool.Recreate(_device, Format, 2, _size);
            }
            _last?.Dispose();
            _last = await SoftwareBitmap.CreateCopyFromSurfaceAsync(newest.Surface);
            newest.Dispose();
            FrameVersion++;
        }
        return _last;
    }

    // Crops to the configured region. Returns null when the frame is entirely black
    // (e.g. exclusive fullscreen), so callers don't mistake it for "no dialog".
    public static SoftwareBitmap? Crop(SoftwareBitmap src, AgentConfig c, bool fullWindow = false)
    {
        int w = src.PixelWidth, h = src.PixelHeight;
        if (w < 16 || h < 16) return null;
        var buf = new byte[w * h * 4];
        src.CopyToBuffer(buf.AsBuffer());

        bool anyContent = false;
        for (int i = 0; i < buf.Length; i += 4 * 211)
            if (buf[i] != 0 || buf[i + 1] != 0 || buf[i + 2] != 0) { anyContent = true; break; }
        if (!anyContent) return null;

        int cx = fullWindow ? 0 : Math.Clamp((int)(w * c.CropX), 0, w - 1);
        int cy = fullWindow ? 0 : Math.Clamp((int)(h * c.CropY), 0, h - 1);
        int cw = fullWindow ? w : Math.Clamp((int)(w * c.CropWidth), 1, w - cx);
        int ch = fullWindow ? h : Math.Clamp((int)(h * c.CropHeight), 1, h - cy);

        var crop = new byte[cw * ch * 4];
        for (int row = 0; row < ch; row++)
            Buffer.BlockCopy(buf, ((cy + row) * w + cx) * 4, crop, row * cw * 4, cw * 4);

        AutoContrast(crop);

        return SoftwareBitmap.CreateCopyFromBuffer(crop.AsBuffer(), BitmapPixelFormat.Bgra8, cw, ch,
            BitmapAlphaMode.Premultiplied);
    }

    // Stretches luma contrast (clipping the darkest/brightest 1% of pixels) to make
    // low-contrast dialog text stand out more for the OCR engine. Cheap single pass
    // over a histogram plus a linear remap; skipped if the crop has no real contrast.
    static void AutoContrast(byte[] crop)
    {
        var hist = new int[256];
        int pixelCount = crop.Length / 4;
        for (int i = 0; i < crop.Length; i += 4)
        {
            int luma = (crop[i + 2] * 299 + crop[i + 1] * 587 + crop[i] * 114) / 1000;
            hist[luma]++;
        }

        int clip = pixelCount / 100; // 1% of pixels on each tail
        int lo = 0, acc = 0;
        while (lo < 255 && acc + hist[lo] <= clip) { acc += hist[lo]; lo++; }
        int hi = 255; acc = 0;
        while (hi > 0 && acc + hist[hi] <= clip) { acc += hist[hi]; hi--; }
        if (hi <= lo) return;

        double scale = 255.0 / (hi - lo);
        for (int i = 0; i < crop.Length; i += 4)
        {
            crop[i] = Stretch(crop[i], lo, scale);
            crop[i + 1] = Stretch(crop[i + 1], lo, scale);
            crop[i + 2] = Stretch(crop[i + 2], lo, scale);
        }
    }

    static byte Stretch(byte v, int lo, double scale) =>
        (byte)Math.Clamp((int)((v - lo) * scale), 0, 255);

    public void Dispose() { Stop(); }
}

static class CaptureInterop
{
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(IntPtr window, ref Guid iid);
        IntPtr CreateForMonitor(IntPtr monitor, ref Guid iid);
    }

    static readonly Guid IidInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    static readonly Guid IidItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    static readonly Guid IidDxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    [DllImport("combase.dll")]
    static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    static extern int WindowsCreateString(string src, int length, out IntPtr hstring);
    [DllImport("combase.dll")]
    static extern int WindowsDeleteString(IntPtr hstring);
    [DllImport("d3d11.dll")]
    static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags,
        IntPtr levels, uint levelCount, uint sdkVersion, out IntPtr device, out int level, out IntPtr context);
    [DllImport("d3d11.dll")]
    static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    public static GraphicsCaptureItem? CreateItemForWindow(IntPtr hwnd)
    {
        const string cls = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(cls, cls.Length, out var hs));
        try
        {
            var iid = IidInterop;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hs, ref iid, out var factory));
            try
            {
                var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory);
                var itemIid = IidItem;
                var ptr = interop.CreateForWindow(hwnd, ref itemIid);
                return ptr == IntPtr.Zero ? null : GraphicsCaptureItem.FromAbi(ptr);
            }
            finally { Marshal.Release(factory); }
        }
        finally { WindowsDeleteString(hs); }
    }

    public static IDirect3DDevice CreateDevice()
    {
        // driverType 1 = hardware, flags 0x20 = BGRA support, SDK version 7
        Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7,
            out var d3d, out _, out var ctx));
        Marshal.Release(ctx);
        var dxgiIid = IidDxgiDevice;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3d, ref dxgiIid, out var dxgi));
        try
        {
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var inspectable));
            return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally { Marshal.Release(dxgi); Marshal.Release(d3d); }
    }
}

static class NativeMethods
{
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
}
