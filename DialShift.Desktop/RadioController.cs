using DialShift.Core;

namespace DialShift.Desktop;

public sealed class RadioController : IDisposable
{
    private readonly Settings settings;
    private readonly Func<string, int, IAudioSession> open;
    private readonly Func<DateTimeOffset> clock;
    private readonly ScheduleSession schedule = new();
    private IAudioSession? session;
    private DateTimeOffset lastTick, lastActivity, stableSince, retryAt;
    private int failures;
    private bool fallback, disposed;
    public Station? Desired { get; private set; }
    public Station? Current { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsPlaying { get; private set; }
    public string Status { get; private set; } = "Ready when you are";
    public string Track { get; private set; } = "Choose a station and make yourself at home.";
    public Occurrence? Next => Scheduler.Evaluate(settings, clock().LocalDateTime).Next;
    public event Action? Changed;

    public RadioController(Settings settings, Func<string, int, IAudioSession> open, Func<DateTimeOffset>? clock = null)
    {
        this.settings = settings; this.open = open; this.clock = clock ?? (() => DateTimeOffset.Now);
        lastTick = this.clock();
    }
    public void StartSchedule() => CheckSchedule(true);
    public void RefreshSchedule() { CheckSchedule(true); Changed?.Invoke(); }
    private void CheckSchedule(bool force = false)
    {
        var slot = schedule.TakeChange(settings, clock().LocalDateTime, force);
        if (slot != null && settings.Stations.FirstOrDefault(s => s.Id == slot.Entry.StationId) is { } station) Play(station, false);
    }
    public void Play(Station station, bool manual = true)
    {
        if (disposed) return;
        if (manual) schedule.HoldCurrent(settings, clock().LocalDateTime);
        Desired = station; settings.LastStationId = station.Id; failures = 0; fallback = false; IsActive = true;
        Open(station);
    }
    public void Toggle()
    {
        if (IsActive) Pause();
        else if ((Desired ?? settings.Stations.FirstOrDefault(s => s.Id == settings.LastStationId) ?? settings.Stations.FirstOrDefault()) is { } station) Play(station);
    }
    public void Pause()
    {
        schedule.HoldCurrent(settings, clock().LocalDateTime);
        IsActive = false; IsPlaying = false; retryAt = default; Retire();
        Status = settings.ScheduleEnabled ? "Paused · resumes at the next scheduled change" : "Paused";
        Track = "Press play to return to the live broadcast.";
        Changed?.Invoke();
    }
    public void NextStation()
    {
        if (settings.Stations.Count == 0) return;
        var index = settings.Stations.FindIndex(s => s.Id == (Desired?.Id ?? Current?.Id));
        Play(settings.Stations[(index + 1) % settings.Stations.Count]);
    }
    public void SetVolume(int volume) { settings.Volume = Math.Clamp(volume, 0, 100); session?.SetVolume(settings.Volume); }
    public void ForgetStation(Guid id)
    {
        if (Desired?.Id == id || Current?.Id == id) Pause();
        if (Desired?.Id == id) Desired = null;
        if (Current?.Id == id) Current = null;
        Changed?.Invoke();
    }
    public void ResumeFromSleep()
    {
        var before = session;
        CheckSchedule();
        if (IsActive && Desired != null && before == session) { failures = 0; fallback = false; Open(Desired); }
    }
    private void Open(Station station)
    {
        Retire(); Current = station; IsPlaying = false; retryAt = default; stableSince = default;
        lastActivity = clock(); Status = fallback ? "Connecting to fallback…" : "Connecting…"; Track = station.Tag;
        try { session = open(station.Url, settings.Volume); }
        catch (Exception ex) { Fail(ex.Message); }
        Changed?.Invoke();
    }
    private void Fail(string? error = null)
    {
        Retire(); IsPlaying = false; failures++;
        retryAt = clock().AddSeconds(failures < 3 ? failures * 3 : 30);
        Status = "Stream unavailable · retrying shortly";
        Track = error ?? "Your next scheduled change will still run.";
    }
    public void Tick()
    {
        if (disposed) return;
        var now = clock();
        if (now - lastTick > TimeSpan.FromSeconds(15)) ResumeFromSleep(); else CheckSchedule();
        lastTick = now;
        if (IsActive && session != null)
        {
            try
            {
                var snapshot = session.Read();
                if (snapshot.State == AudioState.Failed) Fail(snapshot.Error);
                else if (snapshot.State == AudioState.Playing)
                {
                    if (!IsPlaying) { stableSince = now; if (fallback) retryAt = now.AddMinutes(2); }
                    IsPlaying = true; lastActivity = now;
                    Status = fallback ? "Live · fallback station" : "Live broadcast";
                    Track = string.IsNullOrWhiteSpace(snapshot.Title) ? Current?.Tag ?? "Live radio" : snapshot.Title;
                    if (!fallback && now - stableSince >= TimeSpan.FromMinutes(1)) failures = 0;
                }
                else { IsPlaying = false; Status = "Buffering…"; if (now - lastActivity > TimeSpan.FromSeconds(25)) Fail("The stream stopped responding."); }
            }
            catch (Exception ex) { Fail(ex.Message); }
        }
        if (IsActive && retryAt != default && now >= retryAt && Desired != null)
        {
            var backup = settings.Stations.FirstOrDefault(s => s.Id == settings.FallbackStationId && s.Id != Desired.Id);
            fallback = failures >= 3 && backup != null && !fallback;
            Open(fallback ? backup! : Desired);
        }
        Changed?.Invoke();
    }
    private void Retire() { session?.Dispose(); session = null; }
    public void Dispose() { disposed = true; IsActive = false; IsPlaying = false; Retire(); }
}
