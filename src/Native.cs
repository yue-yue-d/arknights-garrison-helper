using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace WeishuHelper
{
    /// <summary>
    /// 只用最普通的窗口 API：找游戏窗口、读它的位置、把悬浮窗摆过去。
    /// 不打开游戏进程、不读写游戏内存、不注入任何东西。
    /// </summary>
    public static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int L, T, R, B; public int W => R - L; public int H => B - T; }
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        delegate bool EnumProc(IntPtr h, IntPtr p);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr p);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);

        public static readonly IntPtr HWND_TOPMOST = new(-1);
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
        const int GWL_EXSTYLE = -20;
        const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_APPWINDOW = 0x40000;
        public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_NOREPEAT = 0x4000;
        public const int WM_HOTKEY = 0x0312;

        static readonly uint MyPid = (uint)Environment.ProcessId;

        /// <summary>找明日方舟的游戏窗口（Arknights.exe 的可见主窗口），找不到返回 0。</summary>
        public static IntPtr FindGame()
        {
            var pids = new HashSet<uint>();
            foreach (var p in Process.GetProcessesByName("Arknights"))
            {
                pids.Add((uint)p.Id);
                p.Dispose();
            }
            if (pids.Count == 0) return IntPtr.Zero;
            IntPtr best = IntPtr.Zero;
            long bestArea = 0;
            EnumWindows((h, _) =>
            {
                if (!IsWindowVisible(h)) return true;
                GetWindowThreadProcessId(h, out var pid);
                if (!pids.Contains(pid)) return true;
                GetWindowRect(h, out var r);
                long a = (long)r.W * r.H;
                var sb = new StringBuilder(64);
                GetClassName(h, sb, 64);
                if (sb.ToString() == "UnityWndClass") a *= 4;   // 优先 Unity 渲染窗口
                if (a > bestArea) { bestArea = a; best = h; }
                return true;
            }, IntPtr.Zero);
            return best;
        }

        /// <summary>游戏画面（客户区）在屏幕上的位置，物理像素。</summary>
        public static RECT ClientOnScreen(IntPtr h)
        {
            GetClientRect(h, out var c);
            var p = new POINT();
            ClientToScreen(h, ref p);
            return new RECT { L = p.X, T = p.Y, R = p.X + c.W, B = p.Y + c.H };
        }

        public static bool IsOwnWindow(IntPtr h)
        {
            if (h == IntPtr.Zero) return false;
            GetWindowThreadProcessId(h, out var pid);
            return pid == MyPid;
        }

        public static void SetToolWindow(IntPtr h)
        {
            long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
            ex = (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
            SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex));
        }

        public static void SetClickThrough(IntPtr h, bool on)
        {
            long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
            ex = on ? ex | WS_EX_TRANSPARENT | WS_EX_LAYERED : ex & ~WS_EX_TRANSPARENT;
            SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex));
        }
    }
}
