using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace SynthwaveNowPlaying;

/// <summary>Native notification icon; keeps the player free of a WinForms runtime dependency.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const int Callback = 0x8001;
    private readonly HwndSource _source;
    private readonly ContextMenu _menu;
    private readonly Action _refresh, _restore;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private nint _icon;
    private bool _disposed;
    public bool Registered { get; private set; }

    public TrayIcon(ContextMenu menu, Action refresh, Action restore)
    {
        _menu = menu; _refresh = refresh; _restore = restore;
        // A hidden top-level window receives Explorer's TaskbarCreated broadcast.
        _source = new HwndSource(new HwndSourceParameters("WavePlayer tray messages")
        { Width = 0, Height = 0, WindowStyle = 0 });
        _source.AddHook(Hook);
        try
        {
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/WavePlayer;component/Assets/WavePlayer.ico"))!.Stream;
            using var reader = new BinaryReader(stream);
            reader.ReadUInt16(); reader.ReadUInt16(); int count = reader.ReadUInt16();
            var entries = new List<(int Width, uint Bytes, uint Offset)>();
            for (int i = 0; i < count; i++)
            {
                int width = reader.ReadByte(); reader.ReadBytes(7);
                entries.Add((width == 0 ? 256 : width, reader.ReadUInt32(), reader.ReadUInt32()));
            }
            var entry = entries.OrderBy(e => Math.Abs(e.Width - 32)).First();
            stream.Position = entry.Offset;
            byte[] bytes = reader.ReadBytes((int)entry.Bytes);
            _icon = CreateIconFromResourceEx(bytes, (uint)bytes.Length, true, 0x30000, 32, 32, 0);
            if (_icon == 0) throw new IOException("Unable to create notification icon.");
            Add();
        }
        catch { Dispose(); throw; }
    }

    private NotifyData Data() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyData>(), Window = _source.Handle, Id = 1,
        Flags = 1 | 2 | 4 | 0x80, Message = Callback, Icon = _icon,
        Tip = "WavePlayer · click for controls", Info = "", InfoTitle = "", Version = 4
    };

    private void Add()
    {
        var data = Data();
        Registered = Shell_NotifyIcon(0, ref data);
        if (Registered) Shell_NotifyIcon(4, ref data);
    }

    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (_disposed) return 0;
        if ((uint)message == _taskbarCreated) Add();
        if (message == Callback)
        {
            int action = (int)((long)lParam & 0xffff);
            if (action is 0x400 or 0x401 or 0x202 or 0x203)
            {
                _menu.IsOpen = false;
                _restore();
            }
            else if (action is 0x7b or 0x205)
            {
                if (!_menu.IsOpen)
                {
                    _refresh();
                    GetCursorPos(out var point);
                    var transform = _source.CompositionTarget.TransformFromDevice;
                    var position = transform.Transform(new Point(point.X, point.Y));
                    _menu.Placement = PlacementMode.AbsolutePoint;
                    _menu.HorizontalOffset = position.X;
                    _menu.VerticalOffset = position.Y;
                    SetForegroundWindow(_source.Handle);
                    _menu.IsOpen = true;
                }
            }
            handled = true;
        }
        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _menu.IsOpen = false;
        var data = Data(); Shell_NotifyIcon(2, ref data); Registered = false;
        if (_icon != 0) { DestroyIcon(_icon); _icon = 0; }
        _source.RemoveHook(Hook); _source.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyData
    {
        public uint Size; public nint Window; public uint Id, Flags, Message; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern nint CreateIconFromResourceEx(byte[] bytes, uint size, bool icon, uint version, int width, int height, uint flags);
}
