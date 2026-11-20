using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SynthwaveNowPlaying.UI;

/// <summary>Looping video decoded on a worker thread from bundled JPEG frames;
/// the UI uploads at most one small bitmap per source frame.</summary>
public sealed class LoopVideoView : FrameworkElement
{
    private const int WidthPixels=640, HeightPixels=218, Stride=WidthPixels*4;
    private const int FrameBytes=Stride*HeightPixels;
    private byte[] _readPixels=new byte[FrameBytes], _readyPixels=new byte[FrameBytes], _displayPixels=new byte[FrameBytes];
    private readonly object _frameGate=new();
    private readonly AutoResetEvent _wake=new(false);
    private readonly Stopwatch _time=new();
    private Thread? _reader;
    private volatile bool _running, _playing;
    private bool _rendering;
    private MemoryMappedFile? _mapping;
    private MemoryMappedViewAccessor? _frames;
    private byte[][]? _jpegFrames;
    private WriteableBitmap? _bitmap;
    private int _count, _last=-1, _readyFrame=-1;
    public LoopVideoView()
    {
        IsHitTestVisible=false;
        IsVisibleChanged+=(_,_)=>UpdatePlayback();
        Unloaded+=(_,_)=>Release();
    }
    public bool Open(string path)
    {
        Release();
        if(!File.Exists(path))return false;
        var length=new FileInfo(path).Length;
        if(length<FrameBytes||length%FrameBytes!=0)return false;
        _count=(int)(length/FrameBytes);
        _mapping=MemoryMappedFile.CreateFromFile(path,FileMode.Open,null,0,MemoryMappedFileAccess.Read);
        _frames=_mapping.CreateViewAccessor(0,0,MemoryMappedFileAccess.Read);
        _bitmap=new WriteableBitmap(WidthPixels,HeightPixels,96,96,PixelFormats.Bgra32,null);
        _running=true;
        _reader=new Thread(ReadFrames) {IsBackground=true,Name="WavePlayer video cache",Priority=ThreadPriority.BelowNormal};
        _reader.Start();
        UpdatePlayback(); return true;
    }
    public bool OpenCompressed(string path)
    {
        Release();
        if (!File.Exists(path)) return false;
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var entries = archive.Entries.Where(entry => entry.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.Name, StringComparer.Ordinal).ToArray();
            if (entries.Length == 0) return false;
            var frames = new byte[entries.Length][];
            for (var i = 0; i < entries.Length; i++)
            {
                using var source = entries[i].Open();
                using var bytes = new MemoryStream();
                source.CopyTo(bytes);
                frames[i] = bytes.ToArray();
            }
            _jpegFrames = frames;
            _count = frames.Length;
            _bitmap = new WriteableBitmap(WidthPixels, HeightPixels, 96, 96, PixelFormats.Bgra32, null);
            _running = true;
            _reader = new Thread(ReadFrames) { IsBackground = true, Name = "WavePlayer video frames", Priority = ThreadPriority.BelowNormal };
            _reader.SetApartmentState(ApartmentState.STA);
            _reader.Start();
            UpdatePlayback();
            return true;
        }
        catch (InvalidDataException) { Release(); return false; }
        catch (IOException) { Release(); return false; }
    }
    private void UpdatePlayback()
    {
        bool play=IsVisible&&(_frames is not null || _jpegFrames is not null);
        _playing=play;
        if(play)
        {
            _time.Start();
            if(!_rendering){CompositionTarget.Rendering+=Present;_rendering=true;}
        }
        else
        {
            _time.Stop();
            if(_rendering){CompositionTarget.Rendering-=Present;_rendering=false;}
        }
        _wake.Set();
    }
    private void ReadFrames()
    {
        int lastRead=-1;
        while(_running)
        {
            if(!_playing){_wake.WaitOne();continue;}
            double position=_time.Elapsed.TotalSeconds*30;
            int frame=(int)position%_count;
            if(frame!=lastRead)
            {
                // Page faults and storage reads never run on the input/render thread.
                try
                {
                    if (_jpegFrames is { } jpeg)
                    {
                        using var source = new MemoryStream(jpeg[frame], writable: false);
                        var decoded = new JpegBitmapDecoder(source, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                        if (decoded.Frames[0].PixelWidth != WidthPixels || decoded.Frames[0].PixelHeight != HeightPixels)
                            throw new InvalidDataException("Video frame size changed.");
                        new FormatConvertedBitmap(decoded.Frames[0], PixelFormats.Bgra32, null, 0)
                            .CopyPixels(_readPixels, Stride, 0);
                    }
                    else _frames!.ReadArray((long)frame*FrameBytes,_readPixels,0,FrameBytes);
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
                {
                    _playing = false;
                    return;
                }
                lock(_frameGate)
                {
                    (_readPixels,_readyPixels)=(_readyPixels,_readPixels);
                    _readyFrame=frame;
                }
                lastRead=frame;
            }
            _wake.WaitOne(Math.Clamp((int)Math.Ceiling((Math.Floor(position)+1-position)*1000/30),1,34));
        }
    }
    private void Present(object? sender,EventArgs e)
    {
        if(_bitmap is null)return;
        lock(_frameGate)
        {
            if(_readyFrame<0||_readyFrame==_last)return;
            (_readyPixels,_displayPixels)=(_displayPixels,_readyPixels);
            _last=_readyFrame;
            _readyFrame=-1;
        }
        // No allocation or file IO; the latest frame wins if the UI was busy.
        _bitmap.WritePixels(new Int32Rect(0,0,WidthPixels,HeightPixels),_displayPixels,Stride,0);
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        if(_bitmap is not null&&ActualWidth>0&&ActualHeight>0)
            dc.DrawImage(_bitmap,new Rect(0,0,ActualWidth,ActualHeight));
    }
    private void Release()
    {
        _playing=false;_running=false;_wake.Set();
        _reader?.Join();_reader=null;
        if(_rendering){CompositionTarget.Rendering-=Present;_rendering=false;}
        _time.Reset();_last=_readyFrame=-1;
        _frames?.Dispose();_mapping?.Dispose();_frames=null;_mapping=null;_jpegFrames=null;
    }
}
