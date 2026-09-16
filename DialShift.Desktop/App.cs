using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using DialShift.Core;

namespace DialShift.Desktop;

public sealed class App : Application
{
    public Settings Settings { get; private set; } = null!;
    public SettingsStore Store { get; private set; } = null!;
    public RadioController Radio { get; private set; } = null!;
    public MainWindow Window { get; private set; } = null!;
    public bool Quitting { get; private set; }
    private IClassicDesktopStyleApplicationLifetime desktop = null!;
    private TrayIcon? tray;
    private NativeMenuItem trayPlay = null!, trayStatus = null!;
    private DispatcherTimer? timer;
    private FileStream? instanceLock;
    private DateTime? saveAt;
    public static bool SmokeTest => Environment.GetCommandLineArgs().Contains("--smoke-test");
    public static string DataDirectory => SmokeTest ? Path.Combine(Path.GetTempPath(), "DialShift-MacPreview-Smoke", Environment.ProcessId.ToString())
        : OperatingSystem.IsMacOS() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "DialShift")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DialShift-Preview");

    public override void Initialize()
    {
        Name = "DialShift";
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        Styles.Add(new Style(x => x.OfType<Button>())
        {
            Setters = { new Setter(Button.PaddingProperty, new Thickness(14, 10)), new Setter(Button.MarginProperty, new Thickness(0, 0, 7, 0)), new Setter(Button.CornerRadiusProperty, new CornerRadius(7)) }
        });
        Styles.Add(new Style(x => x.OfType<TextBox>()) { Setters = { new Setter(TextBox.MarginProperty, new Thickness(0, 6, 0, 16)) } });
        Styles.Add(new Style(x => x.OfType<CheckBox>()) { Setters = { new Setter(CheckBox.MarginProperty, new Thickness(0, 7, 12, 7)) } });
        Styles.Add(new Style(x => x.OfType<ComboBox>()) { Setters = { new Setter(ComboBox.MarginProperty, new Thickness(0, 7, 0, 12)), new Setter(ComboBox.MinWidthProperty, 250d) } });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            desktop = lifetime;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Directory.CreateDirectory(DataDirectory);
            try { instanceLock = new FileStream(Path.Combine(DataDirectory, "running.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException)
            {
                File.WriteAllText(Path.Combine(DataDirectory, "show-window"), "show");
                desktop.Shutdown(); return;
            }
            Store = new SettingsStore(DataDirectory); Settings = Store.Load();
            Radio = new RadioController(Settings, AudioFactory.Open);
            Window = new MainWindow(this); desktop.MainWindow = Window;
            if (this.TryGetFeature<IActivatableLifetime>() is { } activation)
                activation.Activated += (_, e) => { if (e.Kind == ActivationKind.Reopen) ShowWindow(); };
            BuildTray();
            var menu = new NativeMenu();
            menu.Items.Add(Menu("Show DialShift", ShowWindow));
            menu.Items.Add(Menu("Play / Pause", () => { Radio.Toggle(); Save(); }));
            menu.Items.Add(Menu("Settings…", () => { ShowWindow(); Window.SelectPage(2); }));
            NativeMenu.SetMenu(this, menu);
            Radio.Changed += () =>
            {
                trayPlay.Header = Radio.IsActive ? "Pause" : "Play";
                trayStatus.Header = Radio.Current?.Name ?? "DialShift";
                if (tray != null) tray.ToolTipText = $"DialShift · {Radio.Current?.Name ?? "Ready"}";
            };
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) =>
            {
                Radio.Tick();
                if (saveAt != null && DateTime.UtcNow >= saveAt) Save();
                var signal = Path.Combine(DataDirectory, "show-window");
                if (File.Exists(signal)) { File.Delete(signal); ShowWindow(); }
            };
            timer.Start();
            desktop.ShutdownRequested += (_, _) => Quitting = true;
            desktop.Exit += (_, _) => { Quitting = true; timer.Stop(); Radio.Dispose(); Save(); tray?.Dispose(); instanceLock?.Dispose(); };
            var opened = false;
            Window.Opened += async (_, _) =>
            {
                if (opened) return;
                opened = true;
                if (SmokeTest) { await SmokeChecks.Run(this); return; }
                Radio.StartSchedule();
                if (Store.Warning != null) await Notify(Store.Warning);
                if (Settings.StartInTray || Environment.GetCommandLineArgs().Contains("--tray")) HideToTray();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    public static WindowIcon Icon() => new(AssetLoader.Open(new Uri("avares://DialShift/Assets/icon-512.png")));
    private static NativeMenuItem Menu(string label, Action action)
    {
        var item = new NativeMenuItem(label); item.Click += (_, _) => action(); return item;
    }
    private void BuildTray()
    {
        // Avalonia's macOS exporter binds its native proxy to this menu instance.
        // Replacing TrayIcon.Menu after initialization throws during a saved edit.
        var menu = tray?.Menu ?? new NativeMenu();
        menu.Items.Clear();
        trayStatus = new NativeMenuItem(Radio.Current?.Name ?? "DialShift") { IsEnabled = false }; menu.Items.Add(trayStatus);
        menu.Items.Add(Menu("Show DialShift", ShowWindow));
        menu.Items.Add(new NativeMenuItemSeparator());
        trayPlay = Menu(Radio.IsActive ? "Pause" : "Play", () => { Radio.Toggle(); Save(); }); menu.Items.Add(trayPlay);
        menu.Items.Add(Menu("Next station", () => { Radio.NextStation(); Save(); }));
        menu.Items.Add(Menu("Volume up", () => { Radio.SetVolume(Settings.Volume + 10); Save(); }));
        menu.Items.Add(Menu("Volume down", () => { Radio.SetVolume(Settings.Volume - 10); Save(); }));
        menu.Items.Add(Menu("Mute", () => { Radio.SetVolume(0); Save(); }));
        foreach (var station in Settings.Stations) menu.Items.Add(Menu(station.Name, () => { Radio.Play(station); Save(); }));
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Menu(Settings.ScheduleEnabled ? "Turn schedule off" : "Turn schedule on", () => { Settings.ScheduleEnabled = !Settings.ScheduleEnabled; Radio.RefreshSchedule(); Refresh(); }));
        menu.Items.Add(Menu("Quit DialShift", Quit));
        if (tray == null)
        {
            tray = new TrayIcon { Icon = Icon(), ToolTipText = "DialShift", IsVisible = true };
            if (OperatingSystem.IsMacOS())
            {
                tray.Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://DialShift/Assets/tray.png")));
                MacOSProperties.SetIsTemplateIcon(tray, true);
            }
            tray.Clicked += (_, _) => ShowWindow();
            tray.Menu = menu;
            TrayIcon.SetIcons(this, new TrayIcons { tray });
        }
    }
    public void ShowWindow() { Window.Show(); Window.WindowState = WindowState.Normal; Window.Activate(); }
    public void HideToTray() { if (tray?.IsVisible == true) Window.Hide(); }
    public void Quit() { Quitting = true; desktop.Shutdown(); }
    public void Refresh() { Save(); BuildTray(); Window.RefreshPage(); }
    public void SaveSoon() => saveAt = DateTime.UtcNow.AddMilliseconds(700);
    public void Save()
    {
        saveAt = null;
        try { Store.Save(Settings); }
        catch (Exception ex) { Log(ex); Window?.ShowNotice("Settings could not be saved: " + ex.Message); }
    }
    public void SetStartup(bool enabled)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Login startup is available in the Mac app.");
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents");
        var path = Path.Combine(folder, "com.dialshift.radio.plist");
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new IOException("Cannot locate DialShift.");
            var bundle = Directory.GetParent(executable)?.Parent?.Parent?.FullName;
            if (bundle == null || !bundle.EndsWith(".app", StringComparison.Ordinal)) throw new IOException("Move DialShift.app to Applications before enabling launch at login.");
            Directory.CreateDirectory(folder);
            var escaped = System.Security.SecurityElement.Escape(bundle);
            File.WriteAllText(path, $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\"><plist version=\"1.0\"><dict><key>Label</key><string>com.dialshift.radio</string><key>ProgramArguments</key><array><string>/usr/bin/open</string><string>-a</string><string>{escaped}</string><string>--args</string><string>--tray</string></array><key>RunAtLoad</key><true/></dict></plist>");
        }
        else if (File.Exists(path)) File.Delete(path);
        Settings.LaunchAtLogin = enabled; Save();
    }
    public void OpenSettingsFolder()
    {
        var info = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "/usr/bin/open" : "explorer.exe") { UseShellExecute = false };
        info.ArgumentList.Add(Store.DirectoryPath); Process.Start(info);
    }
    public Task Notify(string text) => MessageDialog.Show(Window, "DialShift", text);
    public static void Log(Exception ex)
    {
        try { Directory.CreateDirectory(DataDirectory); File.AppendAllText(Path.Combine(DataDirectory, "dialshift.log"), $"{DateTimeOffset.Now:O} {ex}\n"); } catch { }
    }

    public async Task ImportSettings()
    {
        var files = await Window.StorageProvider.OpenFilePickerAsync(new() { Title = "Import DialShift settings", AllowMultiple = false, FileTypeFilter = [new("DialShift settings") { Patterns = ["*.json"] }] });
        if (files.Count == 0) return;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            var incoming = await JsonSerializer.DeserializeAsync<Settings>(stream) ?? throw new IOException("Empty settings file.");
            ValidateImport(incoming);
            if (!await MessageDialog.Confirm(Window, "Import your radio", $"Replace your current stations and schedule with {incoming.Stations.Count} stations and {incoming.Schedule.Count} slots? A backup of your current settings will be kept.")) return;
            Store.Save(Settings);
            File.Copy(Store.FilePath, Store.FilePath + ".before-import-" + DateTime.Now.ToString("yyyyMMddHHmmssfff"));
            Radio.Pause();
            Settings.Stations = incoming.Stations; Settings.Schedule = incoming.Schedule;
            Settings.Volume = Math.Clamp(incoming.Volume, 0, 100); Settings.ScheduleEnabled = incoming.ScheduleEnabled;
            Settings.LastStationId = incoming.LastStationId; Settings.FallbackStationId = incoming.FallbackStationId;
            // Startup belongs to this installation, not to the machine that exported the file.
            Radio.SetVolume(Settings.Volume); Radio.RefreshSchedule(); Refresh();
            Window.ShowNotice("Imported your stations and schedule.");
        }
        catch (Exception ex) { await Notify("Could not import settings: " + ex.Message); }
    }
    internal static void ValidateImport(Settings settings)
    {
        if (settings.Version != 1 || settings.Stations == null || settings.Schedule == null ||
            settings.Stations.Any(s => s == null || s.Id == Guid.Empty || string.IsNullOrWhiteSpace(s.Name) || !SettingsStore.ValidUrl(s.Url)) ||
            settings.Stations.Select(s => s.Id).Distinct().Count() != settings.Stations.Count ||
            settings.Schedule.Any(e => e == null || e.Days == null || e.Days.Count == 0 || e.Days.Any(d => !Enum.IsDefined(d)) || !Scheduler.TryTime(e.Time, out _) || !settings.Stations.Any(s => s.Id == e.StationId)))
            throw new IOException("This is not a valid DialShift settings file.");
    }
    public async Task ExportSettings()
    {
        var file = await Window.StorageProvider.SaveFilePickerAsync(new() { Title = "Export DialShift settings", SuggestedFileName = "DialShift-settings.json", DefaultExtension = "json" });
        if (file == null) return;
        try { await using var stream = await file.OpenWriteAsync(); stream.SetLength(0); await JsonSerializer.SerializeAsync(stream, Settings, new JsonSerializerOptions { WriteIndented = true }); }
        catch (Exception ex) { await Notify("Could not export settings: " + ex.Message); }
    }
}
