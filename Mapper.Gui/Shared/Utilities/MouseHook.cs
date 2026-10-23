using Mapper.Gui.Logic;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace Mapper.Gui
{
    // From: https://stackoverflow.com/questions/11607133/global-mouse-event-handler
    // Points are on the screen, as Windows gives them to this app: what a control's PointFromScreen takes. The hook's
    // own point is in physical pixels whatever the app's DPI awareness, which differ from those on a monitor whose
    // scale isn't the main one's (measurements landed far to the right there), so it's turned into the main window's
    // own. (GetCursorPos would do that too, but in the hook it still holds the point before the move.)
    public static partial class MouseHook
    {
        public static event EventHandler<Point> MouseDown = delegate { };
        public static event EventHandler<Point> MouseUp = delegate { };
        public static event EventHandler<Point> MouseMove = delegate { };

        // The window whose coordinates the points are given in (the main window)
        public static Window? Window { get; set; }

        public static void Start() => _hookID = SetHook(_proc);
        public static void Stop() => UnhookWindowsHookEx(_hookID);

        private static readonly LowLevelMouseProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;

        private static IntPtr SetHook(LowLevelMouseProc proc)
        {
            using Process curProcess = Process.GetCurrentProcess();
            using ProcessModule? curModule = curProcess.MainModule;
            if (curModule is null) return IntPtr.Zero;
            return SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                MSLLHOOKSTRUCT? hookStruct = (MSLLHOOKSTRUCT?)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                if (hookStruct is null) return IntPtr.Zero;

                POINT screen = hookStruct.Value.pt;
                IntPtr window = Window is null ? IntPtr.Zero : new System.Windows.Interop.WindowInteropHelper(Window).Handle;
                if (window != IntPtr.Zero) PhysicalToLogicalPointForPerMonitorDPI(window, ref screen);
                Point point = new(screen.x, screen.y);

                switch ((MouseMessages)wParam)
                {
                    case MouseMessages.WM_LBUTTONDOWN:
                        MouseDown?.Invoke(null, point);
                        break;
                    case MouseMessages.WM_MOUSEMOVE:
                        MouseMove?.Invoke(null, point);
                        break;
                    case MouseMessages.WM_LBUTTONUP:
                        MouseUp?.Invoke(null, point);
                        break;
                }
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private const int WH_MOUSE_LL = 14;

        private enum MouseMessages
        {
            WM_LBUTTONDOWN = 0x0201,
            WM_LBUTTONUP = 0x0202,
            WM_MOUSEMOVE = 0x0200,
            WM_MOUSEWHEEL = 0x020A,
            WM_RBUTTONDOWN = 0x0204,
            WM_RBUTTONUP = 0x0205
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData, flags, time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook,
              LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
          IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PhysicalToLogicalPointForPerMonitorDPI(IntPtr window, ref POINT point);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
