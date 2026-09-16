#if WINDOWS_PREVIEW
using LibVLCSharp.Shared;

namespace DialShift.Desktop;

// This backend only ships in the local Windows preview used to exercise the shared UI.
public sealed class WindowsAudioSession : IAudioSession
{
    private static readonly Lazy<LibVLC> Engine = new(() =>
    {
        LibVLCSharp.Shared.Core.Initialize();
        return new LibVLC("--no-video", "--no-osd", "--network-caching=1500", "--http-reconnect");
    });
    private readonly MediaPlayer player;
    private long lastTime = -1;
    private DateTime lastProgress = DateTime.UtcNow;
    public WindowsAudioSession(string url, int volume)
    {
        player = new MediaPlayer(Engine.Value);
        using var media = new Media(Engine.Value, new Uri(url));
        SetVolume(volume);
        if (!player.Play(media)) { player.Dispose(); throw new IOException("Stream could not be opened."); }
    }
    public AudioSnapshot Read()
    {
        if (player.Time != lastTime) { lastTime = player.Time; lastProgress = DateTime.UtcNow; }
        if (player.State is VLCState.Error or VLCState.Ended || DateTime.UtcNow - lastProgress > TimeSpan.FromSeconds(25))
            return new(AudioState.Failed);
        using var media = player.Media;
        return new(player.IsPlaying ? AudioState.Playing : AudioState.Buffering, media?.Meta(MetadataType.NowPlaying));
    }
    public void SetVolume(int volume) { player.Volume = volume; player.Mute = volume == 0; }
    public void Dispose() { player.Mute = true; _ = Task.Run(() => { player.Stop(); player.Dispose(); }); }
}
#endif
