using DialShift.Core;
using DialShift.Desktop;

var passed = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS: " + label); passed++; }
var settings = Settings.Defaults();
var a = settings.Stations[0]; var b = settings.Stations[1]; var c = settings.Stations[2];
var time = new DateTimeOffset(new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Local));
var sessions = new List<FakeAudio>();
using var radio = new RadioController(settings, (url, volume) => { var audio = new FakeAudio(url, volume); sessions.Add(audio); return audio; }, () => time);
void Step(int seconds) { for (var i = 0; i < seconds; i++) { time = time.AddSeconds(1); radio.Tick(); } }
settings.Schedule = [new() { StationId = a.Id, Time = "09:00", Days = [DayOfWeek.Monday] }, new() { StationId = b.Id, Time = "11:00", Days = [DayOfWeek.Monday] }];
settings.ScheduleEnabled = true;
radio.StartSchedule();
Check(radio.Desired == a && radio.IsActive, "Startup catches current schedule");
Step(1);
Check(radio.IsPlaying, "Audio state becomes playing");
radio.Play(c); Step(1);
Check(radio.Desired == c, "Manual station remains until next slot");
Check(sessions[0].Disposed, "Switch disposes previous player");
radio.Pause(); var pausedCount = sessions.Count;
Step(5);
Check(!radio.IsActive && sessions.Count == pausedCount, "Pause holds the current occurrence");
time = time.AddMinutes(20); radio.Tick();
Check(!radio.IsActive && sessions.Count == pausedCount, "Wake keeps a manually paused stream paused");
time = new DateTimeOffset(new DateTime(2026, 9, 14, 11, 1, 0, DateTimeKind.Local)); radio.Tick();
Check(radio.IsActive && radio.Desired == b, "Wake catches a scheduled change while paused");
radio.Play(c); Step(1); var beforeWake = sessions.Count;
time = time.AddMinutes(2); radio.Tick();
Check(sessions.Count == beforeWake + 1 && radio.Desired == c, "Wake reconnects active manual selection");
radio.SetVolume(200); Check(settings.Volume == 100 && sessions[^1].Volume == 100, "Volume is clamped and forwarded");
radio.SetVolume(0); Check(sessions[^1].Volume == 0, "Mute is forwarded");
settings.ScheduleEnabled = false; settings.FallbackStationId = b.Id;
radio.Play(a); sessions[^1].State = AudioState.Failed; Step(1);
Check(sessions[^1].Disposed && !radio.IsPlaying && radio.IsActive, "Failure disposes stream and retains play intent");
var attempts = sessions.Count; Step(2); Check(sessions.Count == attempts, "Retry waits for backoff");
Step(1); Check(sessions.Count == attempts + 1 && radio.Current == a, "First retry opens requested station");
sessions[^1].State = AudioState.Failed; Step(1); Step(6);
sessions[^1].State = AudioState.Failed; Step(1); Step(30);
Check(radio.Current == b && radio.Desired == a, "Three failures select fallback without forgetting original");
Step(1); Step(120);
Check(radio.Current == a, "Successful fallback retries original after two minutes");
sessions[^1].State = AudioState.Failed; Step(1); radio.Pause(); attempts = sessions.Count; Step(40);
Check(sessions.Count == attempts && !radio.IsActive, "Pause cancels pending retries");
radio.Play(a); sessions[^1].State = AudioState.Buffering; Step(26);
Check(sessions[^1].Disposed && radio.Track.Contains("responding"), "Stalled stream enters recovery");
radio.Play(a); radio.ForgetStation(a.Id);
Check(radio.Desired == null && radio.Current == null && !radio.IsActive, "Deleting current station stops audio");
radio.Play(b); radio.Dispose(); attempts = sessions.Count; Step(40); radio.Play(c);
Check(sessions.Count == attempts && sessions[^1].Disposed, "Disposal prevents further playback");
Console.WriteLine($"{passed} desktop controller checks passed.");

sealed class FakeAudio(string url, int volume) : IAudioSession
{
    public string Url { get; } = url;
    public int Volume { get; private set; } = volume;
    public bool Disposed { get; private set; }
    public AudioState State { get; set; } = AudioState.Playing;
    public AudioSnapshot Read() => new(State);
    public void SetVolume(int value) => Volume = value;
    public void Dispose() => Disposed = true;
}
