using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CanvasForge.Core;

namespace CanvasForge.App;
internal static partial class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int L, T, R, B;
        public readonly ScreenRect ToScreen() => new(L, T, R, B);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public UIntPtr Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyInput
    {
        public ushort Vk, Scan;
        public uint Flags, Time;
        public UIntPtr Extra;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct Union
    {
        [FieldOffset(0)]
        public MouseInput Mouse;
        [FieldOffset(0)]
        public KeyInput Key;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public Union Union;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPels, YPels;
        public uint ClrUsed, ClrImportant;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] input, int size);
    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint period);
    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")]
    internal static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    internal static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")]
    internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint op);
    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BitmapInfo info, uint usage);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();
    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();
    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);
    public static ScreenRect VirtualScreen => new(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(76) + GetSystemMetrics(78), GetSystemMetrics(77) + GetSystemMetrics(79));

    public static ScreenPoint Cursor()
    {
        GetCursorPos(out var p);
        return new(p.X, p.Y);
    }

    public static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    private delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
    public static IntPtr FindRustAt(ScreenPoint point)
    {
        var hit = WindowAt(point);
        if (IsRust(hit)) return hit;
        var matches = new List<IntPtr>();
        EnumWindows((window, _) =>
        {
            if (IsWindowVisible(window) && IsRust(window) && GetWindowRect(window, out var r)
                && point.X >= r.L && point.X < r.R && point.Y >= r.T && point.Y < r.B)
                matches.Add(window);
            return true;
        }, IntPtr.Zero);
        // Never send input to an arbitrary window when several games overlap.
        return matches.Count == 1 ? matches[0] : IntPtr.Zero;
    }
    public static IntPtr WindowAt(ScreenPoint p) => GetAncestor(WindowFromPoint(new() { X = p.X, Y = p.Y }), 2);
    public static string Title(IntPtr window)
    {
        var s = new StringBuilder(512);
        GetWindowText(window, s, s.Capacity);
        return s.ToString();
    }

    public static uint ProcessIdOf(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var pid);
        return pid;
    }

    // Bind only to the game process. Browser/Steam titles mentioning Rust must
    // never qualify as input targets, even if the actual game is not running.
    public static bool IsRustProcess(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        try
        {
            var pid = ProcessIdOf(window);
            if (pid == 0) return false;
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName.Equals("RustClient", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (Win32Exception) { return false; }
    }

    public static bool IsRust(IntPtr window)
    {
        return IsRustProcess(window);
    }

    // Finds the single visible Rust window without relying on captured screen
    // coordinates (which go stale if the window moved since calibration).
    public static IntPtr FindRust()
    {
        IntPtr found = IntPtr.Zero;
        var count = 0;
        EnumWindows((window, _) =>
        {
            if (IsWindowVisible(window) && IsRust(window)) { found = window; count++; }
            return true;
        }, IntPtr.Zero);
        return count == 1 ? found : IntPtr.Zero;
    }

    // Screen coordinate of the window's client (0,0) and its device DPI. Used to
    // rebase captured calibration coordinates onto the window's current position.
    public static ScreenPoint ClientOrigin(IntPtr window)
    {
        var p = new Point { X = 0, Y = 0 };
        if (!ClientToScreen(window, ref p))
            throw new Win32Exception("Cannot locate the Rust client area.");
        return new(p.X, p.Y);
    }

    public static ScreenSize ClientSize(IntPtr window)
    {
        if (!GetClientRect(window, out var rect))
            throw new Win32Exception("Cannot read the Rust client size.");
        return new(rect.R - rect.L, rect.B - rect.T);
    }

    public static int DpiOf(IntPtr window)
    {
        var dpi = GetDpiForWindow(window);
        if (dpi == 0)
            throw new Win32Exception("Cannot read the Rust window DPI.");
        return checked((int)dpi);
    }

    private static void Send(params Input[] inputs)
    {
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected SendInput. Check privilege levels.");
    }

    public static bool BeginHighResolutionTimer() => timeBeginPeriod(1) == 0;
    public static void EndHighResolutionTimer() => timeEndPeriod(1);

    private static Input MouseButtonInputOf(bool up) => new()
    {
        Type = 0,
        Union = new() { Mouse = new() { Flags = up ? 4u : 2u } }
    };

    private static Input AbsoluteMoveInputOf(int x, int y)
    {
        var r = VirtualScreen;
        return new Input
        {
            Type = 0,
            Union = new()
            {
                Mouse = new()
                {
                    X = (int)Math.Round((x - r.Left) * 65535.0 / Math.Max(1, r.Width - 1)),
                    Y = (int)Math.Round((y - r.Top) * 65535.0 / Math.Max(1, r.Height - 1)),
                    Flags = 0x0001u | 0x2000u | 0x4000u | 0x8000u
                }
            }
        };
    }

    private static Input KeyInputOf(int key, bool up) => new()
    {
        Type = 1,
        Union = new()
        {
            Key = new()
            {
                Vk = (ushort)key,
                Flags = up ? 2u : 0
            }
        }
    };
    public static void Key(int key, bool up = false) => Send(KeyInputOf(key, up));
    public static void Press(int key)
    {
        Key(key);
        Key(key, true);
    }

    public static void Chord(int modifier, int key)
    {
        Key(modifier);
        try
        {
            Press(key);
        }
        finally
        {
            Key(modifier, true);
        }
    }

    public static void UnicodeKey(char ch, bool up) => Send(new Input
    {
        Type = 1, Union = new() { Key = new() { Scan = ch, Flags = up ? 6u : 4u } }
    });
    public static void Unicode(string text)
    {
        foreach (var ch in text)
            Send(new() { Type = 1, Union = new() { Key = new() { Scan = ch, Flags = 4 } } }, new() { Type = 1, Union = new() { Key = new() { Scan = ch, Flags = 6 } } });
    }

    public static void Mouse(bool up) => Send(MouseButtonInputOf(up));
    public static void MoveAndDown(int x, int y) => Send(AbsoluteMoveInputOf(x, y), MouseButtonInputOf(false));
    public static void ReleaseChecked()=>Send(MouseButtonInputOf(true), KeyInputOf(0x10, true), KeyInputOf(0x11, true), KeyInputOf(0x12, true));
    public static void Release()
    {
        try
        {
            ReleaseChecked();
        }
        catch
        {
        }
    }

    public static void MovePath(IReadOnlyList<ScreenPoint> points)
    {
        var r = VirtualScreen;
        var inputs = points.Select(p => new Input { Type = 0, Union = new() { Mouse = new() { X = (int)Math.Round((p.X - r.Left) * 65535.0 / Math.Max(1, r.Width - 1)), Y = (int)Math.Round((p.Y - r.Top) * 65535.0 / Math.Max(1, r.Height - 1)), Flags = 0x8000 | 0x4000 | 0x2000 | 1 } } }).ToArray();
        Send(inputs);
    }

    public static PixelImage Screenshot(ScreenRect r)
    {
        if (!r.Valid || (long)r.Right - r.Left > 16384 || (long)r.Bottom - r.Top > 16384
            || (long)r.Width * r.Height > 32_000_000)
            throw new ArgumentException("Invalid or oversized capture rectangle.");
        var dc = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(dc);
        var bmp = CreateCompatibleBitmap(dc, r.Width, r.Height);
        var old = SelectObject(mem, bmp);
        try
        {
            if (!BitBlt(mem, 0, 0, r.Width, r.Height, dc, r.Left, r.Top, 0x00CC0020 | 0x40000000))
                throw new Win32Exception();
            SelectObject(mem, old);
            var buffer = new byte[checked(r.Width * r.Height * 4)];
            var info = new BitmapInfo
            {
                Size = (uint)Marshal.SizeOf<BitmapInfo>(),
                Width = r.Width,
                Height = -r.Height,
                Planes = 1,
                BitCount = 32
            };
            if (GetDIBits(dc, bmp, 0, (uint)r.Height, buffer, ref info, 0) == 0)
                throw new Win32Exception();
            for (var i = 0; i < buffer.Length; i += 4)
            {
                (buffer[i], buffer[i + 2]) = (buffer[i + 2], buffer[i]);
                buffer[i + 3] = 255;
            }

            return new(r.Width, r.Height, buffer);
        }
        finally
        {
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, dc);
        }
    }

    public static Rgb Median(ScreenRect r)
    {
        var im = Screenshot(r);
        var list = new List<Rgb>();
        for (var y = im.Height / 5; y < Math.Max(im.Height / 5 + 1, im.Height * 4 / 5); y++)
            for (var x = im.Width / 5; x < Math.Max(im.Width / 5 + 1, im.Width * 4 / 5); x++)
                list.Add(im.Color(y * im.Width + x));
        return new(list.Select(c => c.R).OrderBy(x => x).ElementAt(list.Count / 2), list.Select(c => c.G).OrderBy(x => x).ElementAt(list.Count / 2), list.Select(c => c.B).OrderBy(x => x).ElementAt(list.Count / 2));
    }

    private static void ClipboardOpen(IntPtr owner=default)
    {
        for (var i = 0; i < 10; i++)
        {
            if (OpenClipboard(owner))
                return;
            Thread.Sleep(10);
        }

        throw new Win32Exception("Cannot open clipboard.");
    }

    public static string? ClipboardRead()=>ObserveClipboard().Text;
    internal static ClipboardObservation ObserveClipboard()
    {
        ClipboardOpen();
        try
        {
            string? ReadText()
            {
            var handle = GetClipboardData(13);
            if (handle == IntPtr.Zero)
                return null;
            var p = GlobalLock(handle);
            if (p == IntPtr.Zero)
                return null;
            try
            {
                return Marshal.PtrToStringUni(p);
            }
            finally
            {
                GlobalUnlock(handle);
            }
            }
            return new(ReadText(),ClipboardSequence(),ProcessIdOf(ClipboardOwner()));
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static uint ClipboardWrite(string text,uint? expectedSequence=null)
    {
        var bytes = Encoding.Unicode.GetBytes(text + '\0');
        var handle = GlobalAlloc(0x42, (UIntPtr)bytes.Length);
        if (handle == IntPtr.Zero)
            throw new OutOfMemoryException();
        var owned = true;
        try
        {
            var p = GlobalLock(handle);
            if (p == IntPtr.Zero)
                throw new Win32Exception();
            try
            {
                Marshal.Copy(bytes, 0, p, bytes.Length);
            }
            finally
            {
                GlobalUnlock(handle);
            }

            using var owner=new ClipboardWriteWindow();
            ClipboardOpen(owner.Handle);
            try
            {
                if(expectedSequence.HasValue&&ClipboardSequence()!=expectedSequence.Value)
                    throw new InvalidOperationException("Буфер обміну змінився під час вводу. Зупини стороннє копіювання та повтори.");
                if (!EmptyClipboard() || SetClipboardData(13, handle) == IntPtr.Zero)
                    throw new Win32Exception();
                owned = false;
                return ClipboardSequence();
            }
            finally
            {
                CloseClipboard();
            }
        }
        finally
        {
            if (owned)
                GlobalFree(handle);
        }
    }
}
