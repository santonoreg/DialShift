namespace DialShift.Desktop;

public enum AudioState { Buffering, Playing, Failed }

public readonly record struct AudioSnapshot(AudioState State, string? Title = null, string? Error = null);

// Sessions are owned and polled by the UI thread. Dispose must stop audible playback.
public interface IAudioSession : IDisposable
{
    AudioSnapshot Read();
    void SetVolume(int volume);
}

public static class AudioFactory
{
    public static IAudioSession Open(string url, int volume)
    {
        if (OperatingSystem.IsMacOS()) return new MacAudioSession(url, volume);
#if WINDOWS_PREVIEW
        if (OperatingSystem.IsWindows()) return new WindowsAudioSession(url, volume);
#endif
        throw new PlatformNotSupportedException("This build supports macOS. Use the original DialShift app on Windows.");
    }
}
