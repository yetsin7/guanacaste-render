using System.IO;
using System.Runtime.InteropServices;
using Guanacaste3D.Core.Rendering;

namespace Guanacaste3D.Desktop;

/// <summary>
/// Writes the frames of a movie into an H.264 .mp4 file with the encoder Windows already has (Media Foundation), so a movie is a
/// file that plays anywhere, with no ffmpeg or editor to convert it. Width and height are rounded down to even numbers, as H.264
/// needs. If the file cannot be started, <see cref="TryCreate"/> says why instead of throwing, and the caller keeps the frames as images.
/// Media Foundation objects belong to the thread that made them, so every call to the encoder runs on one thread of the writer's own;
/// frames are queued to it (a few at a time), which also lets the next frame render while the last one is being encoded.
/// </summary>
internal sealed class Mp4VideoWriter : IFrameSink
{
    private IMFSinkWriter? _writer;
    private readonly int _width, _height, _fps;
    private long _frame;
    private bool _started;
    private readonly System.Collections.Concurrent.BlockingCollection<Action> _jobs = new(6);
    private Thread? _thread;
    private volatile bool _failed;
    private volatile string? _problem;
    private bool _disposed;

    public bool Failed => _failed;
    public string? Problem => _problem;
    public string Path { get; }

    private Mp4VideoWriter(string path, int width, int height, int fps)
    {
        Path = path;
        (_width, _height, _fps) = (width & ~1, height & ~1, Math.Clamp(fps, 1, 120));
    }

    /// <summary>Starts a video file of the given size; false (with the reason) when this computer cannot encode H.264.</summary>
    public static bool TryCreate(string path, int width, int height, int fps, out Mp4VideoWriter? writer, out string? problem)
    {
        writer = null;
        problem = null;
        if (width < 16 || height < 16 || !OperatingSystem.IsWindows()) { problem = "El tamaño o el sistema no admiten video."; return false; }
        var candidate = new Mp4VideoWriter(path, width, height, fps);
        try
        {
            candidate.StartThread();
            candidate.RunOnThread(candidate.Open);
            writer = candidate;
            return true;
        }
        catch (Exception exception) when (exception is COMException or DllNotFoundException or EntryPointNotFoundException or InvalidCastException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            candidate.Dispose();
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            problem = exception.Message;
            return false;
        }
    }

    private void StartThread()
    {
        _thread = new Thread(() =>
        {
            foreach (var job in _jobs.GetConsumingEnumerable()) job();
        }) { IsBackground = true, Name = "Video encoder" };
        _thread.Start();
    }

    /// <summary>Runs something on the encoder's thread and waits for it, passing on what it throws.</summary>
    private void RunOnThread(Action action)
    {
        var done = new TaskCompletionSource();
        _jobs.Add(() =>
        {
            try { action(); done.SetResult(); }
            catch (Exception exception) { done.SetException(exception); }
        });
        try { done.Task.GetAwaiter().GetResult(); }
        catch (AggregateException aggregate) { throw aggregate.GetBaseException(); }
    }

    private void Open()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        Check(MF.MFStartup(MF.Version, 0));
        _started = true;
        Check(MF.MFCreateSinkWriterFromURL(Path, IntPtr.Zero, IntPtr.Zero, out var writer));
        _writer = writer;
        var bitrate = (int)Math.Clamp((long)(_width * (long)_height * _fps * 0.22), 2_500_000, 40_000_000);

        Check(MF.MFCreateMediaType(out var output));
        output.SetGUID(MF.MajorType, MF.Video);
        output.SetGUID(MF.Subtype, MF.H264);
        output.SetUINT32(MF.AverageBitrate, bitrate);
        output.SetUINT32(MF.Mpeg2Profile, 100); // H.264 High profile: sharper at the same size than the default
        output.SetUINT32(MF.InterlaceMode, 2);
        output.SetUINT64(MF.FrameSize, Pack(_width, _height));
        output.SetUINT64(MF.FrameRate, Pack(_fps, 1));
        output.SetUINT64(MF.PixelAspectRatio, Pack(1, 1));
        writer.AddStream(output, out var stream);
        Marshal.ReleaseComObject(output);

        Check(MF.MFCreateMediaType(out var input));
        input.SetGUID(MF.MajorType, MF.Video);
        input.SetGUID(MF.Subtype, MF.Rgb32);
        input.SetUINT32(MF.InterlaceMode, 2);
        input.SetUINT64(MF.FrameSize, Pack(_width, _height));
        input.SetUINT64(MF.FrameRate, Pack(_fps, 1));
        input.SetUINT64(MF.PixelAspectRatio, Pack(1, 1));
        input.SetUINT32(MF.DefaultStride, _width * 4);
        writer.SetInputMediaType(stream, input, IntPtr.Zero);
        Marshal.ReleaseComObject(input);
        writer.BeginWriting();
    }

    private static long Pack(int high, int low) => ((long)high << 32) | (uint)low;

    private static void Check(int result)
    {
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    /// <summary>Queues the next frame (BGRA, top row first); a frame larger than the video is cropped to it. The array must not change afterwards.</summary>
    public void Add(byte[] bgra, int width, int height)
    {
        if (_failed || _disposed || _thread is null) return;
        try { _jobs.Add(() => AddFrame(bgra, width, height)); }
        catch (InvalidOperationException) { /* closed while adding */ }
    }

    private void AddFrame(byte[] bgra, int width, int height)
    {
        if (_writer is null || _failed) return;
        try
        {
            if (width < _width || height < _height || bgra.Length < width * height * 4) throw new ArgumentException("The frame is smaller than the video.");
            Check(MF.MFCreateMemoryBuffer(_width * _height * 4, out var buffer));
            buffer.Lock(out var target, out _, out _);
            try
            {
                if (width == _width) Marshal.Copy(bgra, 0, target, _width * _height * 4);
                else
                    for (var row = 0; row < _height; row++) Marshal.Copy(bgra, row * width * 4, target + row * _width * 4, _width * 4);
            }
            finally { buffer.Unlock(); }
            buffer.SetCurrentLength(_width * _height * 4);
            Check(MF.MFCreateSample(out var sample));
            sample.AddBuffer(buffer);
            sample.SetSampleTime(_frame * 10_000_000L / _fps);
            sample.SetSampleDuration(10_000_000L / _fps);
            _writer.WriteSample(0, sample);
            Marshal.ReleaseComObject(sample);
            Marshal.ReleaseComObject(buffer);
            _frame++;
        }
        catch (Exception exception) when (exception is COMException or ArgumentException or InvalidCastException)
        {
            _failed = true;
            _problem = exception.Message;
        }
    }

    /// <summary>Closes the file: waits for the queued frames, writes the index so the video plays, and releases the encoder.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_thread is not null)
        {
            _jobs.Add(() =>
            {
                try { _writer?.FinalizeWriting(); }
                catch (Exception exception) { _failed = true; _problem ??= exception.Message; }
                try { if (_writer is not null) Marshal.ReleaseComObject(_writer); }
                catch (Exception exception) { _problem ??= exception.Message; }
                _writer = null;
                try { if (_started) MF.MFShutdown(); }
                catch (Exception exception) { _problem ??= exception.Message; }
                _started = false;
            });
            _jobs.CompleteAdding();
            _thread.Join();
        }
    }

    // ------------------------------------------------------------------ Media Foundation

    private static class MF
    {
        public const uint Version = 0x00020070;
        public static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        public static readonly Guid Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        public static readonly Guid Mpeg2Profile = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
        public static readonly Guid AverageBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
        public static readonly Guid InterlaceMode = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        public static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
        public static readonly Guid FrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        public static readonly Guid PixelAspectRatio = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
        public static readonly Guid DefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        public static readonly Guid Video = new("73646976-0000-0010-8000-00aa00389b71");
        public static readonly Guid H264 = new("34363248-0000-0010-8000-00aa00389b71");
        public static readonly Guid Rgb32 = new("00000016-0000-0010-8000-00aa00389b71");

        [DllImport("mfplat.dll", ExactSpelling = true)] public static extern int MFStartup(uint version, uint flags);
        [DllImport("mfplat.dll", ExactSpelling = true)] public static extern int MFShutdown();
        [DllImport("mfplat.dll", ExactSpelling = true)] public static extern int MFCreateMediaType(out IMFMediaType type);
        [DllImport("mfplat.dll", ExactSpelling = true)] public static extern int MFCreateSample(out IMFSample sample);
        [DllImport("mfplat.dll", ExactSpelling = true)] public static extern int MFCreateMemoryBuffer(int length, out IMFMediaBuffer buffer);
        [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        public static extern int MFCreateSinkWriterFromURL(string url, IntPtr byteStream, IntPtr attributes, out IMFSinkWriter writer);
    }

    // The interfaces repeat the 30 methods of IMFAttributes in order (COM layout); only the ones used have real signatures.
    [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaType
    {
        void A0(); void A1(); void A2(); void A3(); void A4(); void A5(); void A6(); void A7(); void A8(); void A9();
        void A10(); void A11(); void A12(); void A13(); void A14(); void A15(); void A16(); void A17();
        void SetUINT32([MarshalAs(UnmanagedType.LPStruct)] Guid key, int value);
        void SetUINT64([MarshalAs(UnmanagedType.LPStruct)] Guid key, long value);
        void A20();
        void SetGUID([MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPStruct)] Guid value);
        void A22(); void A23(); void A24(); void A25(); void A26(); void A27(); void A28(); void A29();
        void T0(); void T1(); void T2(); void T3(); void T4();
    }

    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSample
    {
        void A0(); void A1(); void A2(); void A3(); void A4(); void A5(); void A6(); void A7(); void A8(); void A9();
        void A10(); void A11(); void A12(); void A13(); void A14(); void A15(); void A16(); void A17(); void A18(); void A19();
        void A20(); void A21(); void A22(); void A23(); void A24(); void A25(); void A26(); void A27(); void A28(); void A29();
        void GetSampleFlags(); void SetSampleFlags(); void GetSampleTime();
        void SetSampleTime(long time);
        void GetSampleDuration();
        void SetSampleDuration(long duration);
        void GetBufferCount(); void GetBufferByIndex(); void ConvertToContiguousBuffer();
        void AddBuffer(IMFMediaBuffer buffer);
    }

    [ComImport, Guid("045fa593-8799-42b8-bc8d-8968c6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaBuffer
    {
        void Lock(out IntPtr buffer, out int maximumLength, out int currentLength);
        void Unlock();
        void GetCurrentLength();
        void SetCurrentLength(int length);
    }

    [ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSinkWriter
    {
        void AddStream(IMFMediaType type, out int streamIndex);
        void SetInputMediaType(int streamIndex, IMFMediaType type, IntPtr encodingParameters);
        void BeginWriting();
        void WriteSample(int streamIndex, IMFSample sample);
        void SendStreamTick(); void PlaceMarker(); void NotifyEndOfSegment(); void Flush();
        void FinalizeWriting();
    }
}
