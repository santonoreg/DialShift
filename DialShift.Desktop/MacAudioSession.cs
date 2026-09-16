using System.Runtime.InteropServices;

namespace DialShift.Desktop;

// AVPlayer belongs to macOS: no external player, native library download or codec installer.
// Keep Objective-C calls on the Avalonia main thread. Each poll has an autorelease pool.
public sealed class MacAudioSession : IAudioSession
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private static readonly Lazy<nint> Framework = new(() => NativeLibrary.Load("/System/Library/Frameworks/AVFoundation.framework/AVFoundation"));
    private nint player;

    public MacAudioSession(string url, int volume)
    {
        _ = Framework.Value; // Keep the framework loaded for the lifetime of the process.
        using var pool = new Pool();
        var text = StringMessage(Class("NSString"), Sel("stringWithUTF8String:"), url);
        var nsUrl = PointerMessage(Class("NSURL"), Sel("URLWithString:"), text);
        if (nsUrl == 0) throw new ArgumentException("macOS could not read this stream URL.");
        player = PointerMessage(Message(Class("AVPlayer"), Sel("alloc")), Sel("initWithURL:"), nsUrl);
        if (player == 0) throw new InvalidOperationException("macOS could not create the audio player.");
        SetVolume(volume);
        VoidMessage(player, Sel("play"));
    }

    public AudioSnapshot Read()
    {
        if (player == 0) return new(AudioState.Failed, Error: "Player is closed.");
        using var pool = new Pool();
        var item = Message(player, Sel("currentItem"));
        // AVPlayerStatus / AVPlayerItemStatus: unknown=0, ready=1, failed=2.
        if (Message(player, Sel("status")) == 2 || (item != 0 && Message(item, Sel("status")) == 2))
        {
            var error = Message(item, Sel("error"));
            if (error == 0) error = Message(player, Sel("error"));
            var description = Message(error, Sel("localizedDescription"));
            return new(AudioState.Failed, Error: Marshal.PtrToStringUTF8(Message(description, Sel("UTF8String"))) ?? "Stream unavailable.");
        }
        // AVPlayerTimeControlStatus: paused=0, waiting=1, playing=2.
        return new(Message(player, Sel("timeControlStatus")) == 2 ? AudioState.Playing : AudioState.Buffering);
    }

    public void SetVolume(int volume)
    {
        if (player != 0) FloatMessage(player, Sel("setVolume:"), Math.Clamp(volume, 0, 100) / 100f);
    }

    public void Dispose()
    {
        if (player == 0) return;
        using var pool = new Pool();
        VoidMessage(player, Sel("pause"));
        PointerMessage(player, Sel("replaceCurrentItemWithPlayerItem:"), 0);
        VoidMessage(player, Sel("release"));
        player = 0;
    }

    private sealed class Pool : IDisposable
    {
        private readonly nint pool = Message(Message(Class("NSAutoreleasePool"), Sel("alloc")), Sel("init"));
        public void Dispose() => VoidMessage(pool, Sel("drain"));
    }

    [DllImport(ObjC, EntryPoint = "objc_getClass")] private static extern nint Class([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(ObjC, EntryPoint = "sel_registerName")] private static extern nint Sel([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint Message(nint receiver, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint PointerMessage(nint receiver, nint selector, nint value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint StringMessage(nint receiver, nint selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void VoidMessage(nint receiver, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void FloatMessage(nint receiver, nint selector, float value);
}
