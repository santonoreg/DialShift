using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;

namespace DialShift.Desktop;

internal static class SmokeChecks
{
    public static async Task Run(App app)
    {
        var args = Environment.GetCommandLineArgs();
        var outputIndex = Array.IndexOf(args, "--output");
        var output = outputIndex >= 0 ? args[outputIndex + 1] : Path.Combine(App.DataDirectory, "verification");
        Directory.CreateDirectory(output);
        var results = new List<string>();
        try
        {
            await EditorSmokeChecks.Run(app, results, output);
            app.Radio.SetVolume(0);
            foreach (var station in app.Settings.Stations)
            {
                app.Radio.Play(station);
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (!app.Radio.IsPlaying && DateTime.UtcNow < deadline) await Task.Delay(250);
                if (!app.Radio.IsPlaying) throw new Exception($"Playback failed: {station.Name}: {app.Radio.Track}");
                results.Add("Live stream: " + station.Name);
            }
            app.Radio.Pause();
            if (app.Radio.IsActive || app.Radio.IsPlaying) throw new Exception("Pause failed.");
            results.Add("Pause releases playback");
            for (var page = 0; page < 3; page++)
            {
                app.Window.SelectPage(page); await Task.Delay(250);
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)app.Window.Bounds.Width, (int)app.Window.Bounds.Height));
                bitmap.Render(app.Window); bitmap.Save(Path.Combine(output, $"page-{page}.png"), PngBitmapEncoderOptions.Default);
            }
            results.Add("All three pages rendered");
            app.HideToTray(); await Task.Delay(200); app.ShowWindow();
            results.Add("Hide and restore window");
            app.Save(); var loaded = app.Store.Load();
            if (loaded.Stations.Count != 3 || loaded.Volume != 0) throw new Exception("Settings round-trip failed.");
            results.Add("Settings round-trip");
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { Passed = true, Results = results }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { Passed = false, Error = ex.ToString(), Results = results }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { app.Quit(); }
    }
}
