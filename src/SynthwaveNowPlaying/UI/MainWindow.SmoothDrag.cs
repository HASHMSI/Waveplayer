using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Controls.Primitives;

namespace SynthwaveNowPlaying;

public partial class MainWindow
{
    private bool _smoothDragging;
    private IntPtr _dragHandle;
    private CursorPoint _dragOrigin;
    private NativeRect _dragRect;
    private int _dragX, _dragY;
    private Point? _pullDragOrigin;
    private bool _suppressPullClick;

    private void InitializeSmoothDrag()
    {
        PlaylistPullButton.PreviewMouseLeftButtonDown+=(_,e)=>
        {
            _pullDragOrigin=e.GetPosition(this);_suppressPullClick=false;
        };
        PlaylistPullButton.PreviewMouseMove+=(_,e)=>
        {
            if(_pullDragOrigin is not {} origin||e.LeftButton!=MouseButtonState.Pressed||_editMode)return;
            var delta=e.GetPosition(this)-origin;
            if(Math.Abs(delta.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(delta.Y)<SystemParameters.MinimumVerticalDragDistance)return;
            _pullDragOrigin=null;_suppressPullClick=true;
            // Pull-strip drag is an explicit move gesture, including while pinned.
            // A regular click still opens the library; dragging never opens it.
            if(_pinned)PinButton_Click(PinButton,new RoutedEventArgs());
            PlaylistPullButton.ReleaseMouseCapture();
            BeginSmoothDrag();e.Handled=true;
        };
        PlaylistPullButton.PreviewMouseLeftButtonUp+=(_,_)=>_pullDragOrigin=null;
        Shell.MouseLeftButtonUp += (_,e) => { if(_smoothDragging){EndSmoothDrag();e.Handled=true;} };
        Shell.LostMouseCapture += (_,_) => EndSmoothDrag();
        Deactivated += (_,_) => EndSmoothDrag();
        Closed += (_,_) => EndSmoothDrag();
    }

    private void BeginSmoothDrag()
    {
        _dragHandle=new WindowInteropHelper(this).Handle;
        if(!GetCursorPos(out _dragOrigin)||!GetWindowRect(_dragHandle,out _dragRect))return;
        if(!Shell.CaptureMouse())return;
        _smoothDragging=true;
        _dragX=_dragRect.Left; _dragY=_dragRect.Top;
        ClosePlaylistAnimated();
        // Keep WPF's normal input/animation dispatcher running while moving.
        // Only one native move is issued per composition frame, even with a
        // high polling-rate mouse. Coordinates stay in physical screen pixels.
        CompositionTarget.Rendering+=MoveSmoothDrag;
    }

    private void MoveSmoothDrag(object? sender,EventArgs e)
    {
        if(!_smoothDragging)return;
        if(Mouse.LeftButton!=MouseButtonState.Pressed||_pinned){EndSmoothDrag();return;}
        UpdateDragPosition();
    }
    private void UpdateDragPosition()
    {
        if(!GetCursorPos(out var pointer))return;
        int x=_dragRect.Left+pointer.X-_dragOrigin.X, y=_dragRect.Top+pointer.Y-_dragOrigin.Y;
        if(x==_dragX&&y==_dragY)return;
        if(SetWindowPos(_dragHandle,IntPtr.Zero,x,y,0,0,0x0415))
        { _dragX=x;_dragY=y;_dockToBottomRight=false; }
    }
    private void EndSmoothDrag()
    {
        if(!_smoothDragging)return;
        UpdateDragPosition();
        _smoothDragging=false;
        CompositionTarget.Rendering-=MoveSmoothDrag;
        if(Shell.IsMouseCaptured)Shell.ReleaseMouseCapture();
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X,Y; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
}
