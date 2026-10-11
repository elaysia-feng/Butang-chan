using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ButangPet
{
    /// <summary>桌宠需要的 Win32 互操作。全部集中在这里，其他文件不直接 P/Invoke。</summary>
    internal static class Native
    {
        public const int WS_EX_LAYERED = 0x00080000;
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int key);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO { public uint Size, Tick; }
        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint Packet;
            public ushort Buttons;
            public byte LeftTrigger, RightTrigger;
            public short LeftX, LeftY, RightX, RightY;
        }
        [DllImport("xinput1_4.dll")]
        private static extern uint XInputGetState(uint index, out XINPUT_STATE state);

        /// <summary>只读取最近操作时间，不读取按键内容。</summary>
        public static uint LastInputTick()
        {
            LASTINPUTINFO info = new LASTINPUTINFO { Size = 8 };
            return GetLastInputInfo(ref info) ? info.Tick : 0;
        }

        /// <summary>读取四个 XInput 手柄的按钮；无手柄时正常返回零。</summary>
        public static ushort GamepadButtons()
        {
            ushort buttons = 0;
            try
            {
                for (uint i = 0; i < 4; i++)
                    if (XInputGetState(i, out XINPUT_STATE state) == 0) buttons |= state.Buttons;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            return buttons;
        }
        public static void ReadGamepad(out ushort buttons, out float horizontal)
        {
            buttons = 0; horizontal = 0;
            try
            {
                for (uint i = 0; i < 4; i++)
                    if (XInputGetState(i, out XINPUT_STATE state) == 0)
                    {
                        buttons |= state.Buttons;
                        if (Math.Abs((int)state.LeftX) > 10000) horizontal = state.LeftX / 32768f;
                    }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int Width, Height; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte Op, Flags, Alpha, Format; }
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public uint HeaderSize;
            public int Width, Height;
            public ushort Planes, BitCount;
            public uint Compression, ImageSize;
            public int XPels, YPels;
            public uint ColorsUsed, ColorsImportant;
            public uint Color;
        }
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dstDc, ref POINT dst,
            ref SIZE size, IntPtr srcDc, ref POINT src, uint color, ref BLENDFUNCTION blend, uint flags);
        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO info, uint usage,
            out IntPtr bits, IntPtr section, uint offset);

        /// <summary>复用透明画布和原生 DIB；GDI+ 直接绘入系统合成使用的内存。</summary>
        public sealed class LayeredSurface : IDisposable
        {
            private IntPtr _dc, _dib, _old;
            public System.Drawing.Bitmap Canvas { get; private set; }
            public void Ensure(int width, int height)
            {
                if (Canvas != null && Canvas.Width == width && Canvas.Height == height) return;
                Dispose();
                _dc = CreateCompatibleDC(IntPtr.Zero);
                BITMAPINFO info = new BITMAPINFO { HeaderSize = 40, Width = width, Height = -height,
                    Planes = 1, BitCount = 32 };
                _dib = CreateDIBSection(_dc, ref info, 0, out IntPtr bits, IntPtr.Zero, 0);
                if (_dc == IntPtr.Zero || _dib == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error(); Dispose();
                    throw new System.ComponentModel.Win32Exception(error);
                }
                _old = SelectObject(_dc, _dib);
                Canvas = new System.Drawing.Bitmap(width, height, width * 4,
                    System.Drawing.Imaging.PixelFormat.Format32bppPArgb, bits);
            }
            public void Present(IntPtr handle, System.Drawing.Point location, byte opacity)
            {
                POINT target = new POINT(location.X, location.Y), source = new POINT(0, 0);
                SIZE size = new SIZE { Width = Canvas.Width, Height = Canvas.Height };
                BLENDFUNCTION blend = new BLENDFUNCTION { Alpha = opacity, Format = 1 };
                if (!UpdateLayeredWindow(handle, IntPtr.Zero, ref target, ref size, _dc, ref source, 0, ref blend, 2))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            public void Dispose()
            {
                Canvas?.Dispose(); Canvas = null;
                if (_old != IntPtr.Zero) SelectObject(_dc, _old);
                if (_dib != IntPtr.Zero) DeleteObject(_dib);
                if (_dc != IntPtr.Zero) DeleteDC(_dc);
                _dc = _dib = _old = IntPtr.Zero;
            }
        }

        /// <summary>将预乘 Alpha 位图交给系统合成；透明像素直接透出桌面。</summary>
        public static void PresentLayeredBitmap(IntPtr hwnd, System.Drawing.Bitmap bitmap,
            System.Drawing.Point position, byte opacity)
        {
            // 1. 建立自顶向下的 32 位 DIB，保持 PNG 的透明边缘。
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            IntPtr dib = IntPtr.Zero, old = IntPtr.Zero;
            System.Drawing.Imaging.BitmapData data = null;
            try
            {
                BITMAPINFO info = new BITMAPINFO { HeaderSize = 40, Width = bitmap.Width,
                    Height = -bitmap.Height, Planes = 1, BitCount = 32 };
                dib = CreateDIBSection(dc, ref info, 0, out IntPtr bits, IntPtr.Zero, 0);
                if (dc == IntPtr.Zero || dib == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
                old = SelectObject(dc, dib);
                data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                byte[] row = new byte[bitmap.Width * 4];
                for (int y = 0; y < bitmap.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                    Marshal.Copy(row, 0, IntPtr.Add(bits, y * row.Length), row.Length);
                }
                // 2. 一次提交颜色与 Alpha，不调用会冲突的 SetLayeredWindowAttributes。
                POINT dst = new POINT(position.X, position.Y), src = new POINT(0, 0);
                SIZE size = new SIZE { Width = bitmap.Width, Height = bitmap.Height };
                BLENDFUNCTION blend = new BLENDFUNCTION { Alpha = opacity, Format = 1 };
                if (!UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, ref size, dc, ref src, 0, ref blend, 2))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                if (data != null) bitmap.UnlockBits(data);
                if (old != IntPtr.Zero) SelectObject(dc, old);
                if (dib != IntPtr.Zero) DeleteObject(dib);
                if (dc != IntPtr.Zero) DeleteDC(dc);
            }
        }
        // ── SetWindowPos ──
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        // ── 扩展窗口样式 ──
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;   // 整窗点击穿透
        public const int WS_EX_TOOLWINDOW = 0x00000080;     // 不在 Alt+Tab 里
        public const int WS_EX_NOACTIVATE = 0x08000000;    // 点击时不抢焦点
        public const int WS_EX_APPWINDOW = 0x00040000;

        // ── 区域 ──
        public const int ERROR = 0;
        public const int NULLREGION = 0;

        // 区域函数在 gdi32.dll，不在 user32.dll
        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom,
                                                       int ellipseWidth, int ellipseHeight);

        // 区域合并操作码。
        // 这些值是**在本机实测出来的**，不是照抄头文件：本机 gdi32 的 CombineRgn
        // 里 mode=1 得到的是交集，mode=2 才是并集。按头文件的 RGN_OR=1 去写，
        // 得到的会是越来越小的交集，最后桌宠整个消失。
        public const int RGN_AND_M = 1;
        public const int RGN_OR_M = 2;
        public const int RGN_OR_M_ALT = 3;

        /// <summary>
        /// 实测签名语义：**就地修改 hrgnResult**，返回非零表示成功（不是返回新句柄）。
        /// 按文档当成"返回新区域"来用，会拿到一个把返回值当句柄的错误结果。
        /// </summary>
        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CombineRgn(IntPtr hrgnResult, IntPtr hrgn1, IntPtr hrgn2, int iMode);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern bool GetRgnBox(IntPtr hRgn, out RECT lpRect);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateEllipticRgn(int left, int top, int right, int bottom);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        // ── 窗口 ──
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                               int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT point);
        public delegate IntPtr KeyboardHook(int code, IntPtr message, IntPtr data);
        public delegate IntPtr MouseHook(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int hook, KeyboardHook callback, IntPtr module, uint thread);
        [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
        public static extern IntPtr SetMouseHook(int hook, MouseHook callback, IntPtr module, uint thread);
        [DllImport("user32.dll")]
        public static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string name);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        // ── 全局热键 ──
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // ── 开机自启注册表 ──
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int RegOpenKeyEx(IntPtr hKey, string subKey, int ulOptions,
                                              int samDesired, out IntPtr phkResult);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int RegSetValueEx(IntPtr hKey, string lpValueName, int reserved,
                                               int dwType, byte[] lpData, int cbData);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int RegDeleteValue(IntPtr hKey, string lpValueName);

        [DllImport("advapi32.dll")]
        public static extern int RegCloseKey(IntPtr hKey);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int RegQueryValueEx(IntPtr hKey, string lpValueName, IntPtr lpReserved,
                                                 out uint lpType, byte[] lpData, ref uint lpcbData);

        public static readonly IntPtr HKEY_CURRENT_USER = new IntPtr(unchecked((int)0x80000001));
        public const int KEY_SET_VALUE = 0x0002;
        public const int KEY_QUERY_VALUE = 0x0001;
        public const int REG_SZ = 1;

        /// <summary>把窗口挂到「始终置顶」或取消。桌宠要压过全屏游戏窗口，所以用 SetWindowPos 而不是 TopMost 属性。</summary>
        public static void SetTopmost(IntPtr hwnd, bool on)
        {
            SetWindowPos(hwnd, on ? HWND_TOPMOST : HWND_NOTOPMOST,
                         0, 0, 0, 0,
                         SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        public static int GetExStyle(IntPtr hwnd)
        {
            return GetWindowLong(hwnd, GWL_EXSTYLE);
        }

        public static void SetExStyle(IntPtr hwnd, int style)
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, style);
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static int GetWindowLong(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(hWnd, nIndex).ToInt32()
                : GetWindowLong32(hWnd, nIndex);
        }

        private static void SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(hWnd, nIndex, new IntPtr(dwNewLong));
            else SetWindowLong32(hWnd, nIndex, dwNewLong);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        // ── 开机自启 ──
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "ButangPet";

        public static bool SetAutoStart(bool on, string exePath)
        {
            IntPtr key;
            if (RegOpenKeyEx(HKEY_CURRENT_USER, RunKey, 0, KEY_SET_VALUE, out key) != 0)
                return false;
            try
            {
                if (on)
                {
                    // 加引号：路径含空格时注册表启动项才不会断在中间
                    string cmd = "\"" + exePath + "\" --autostart";
                    byte[] data = Encoding.Unicode.GetBytes(cmd + "\0");
                    return RegSetValueEx(key, RunValue, 0, REG_SZ, data, data.Length) == 0;
                }
                return RegDeleteValue(key, RunValue) == 0;
            }
            finally { RegCloseKey(key); }
        }

        public static bool GetAutoStart(out string command)
        {
            command = null;
            IntPtr key;
            if (RegOpenKeyEx(HKEY_CURRENT_USER, RunKey, 0, KEY_QUERY_VALUE, out key) != 0)
                return false;
            try
            {
                uint size = 0;
                if (RegQueryValueEx(key, RunValue, IntPtr.Zero, out uint _, null, ref size) != 0)
                    return false;
                byte[] buf = new byte[size];
                if (RegQueryValueEx(key, RunValue, IntPtr.Zero, out uint _, buf, ref size) != 0)
                    return false;
                command = Encoding.Unicode.GetString(buf).TrimEnd('\0');
                return true;
            }
            finally { RegCloseKey(key); }
        }
    }
}
