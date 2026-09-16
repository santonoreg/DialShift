using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace DialShift.Desktop;

// These checks exercise actual editor windows, text fields and button handlers.
// They use the same isolated temporary profile as the playback smoke test.
internal static class EditorSmokeChecks
{
    public static async Task Run(App app, List<string> results, string output)
    {
        var tray = TrayIcon.GetIcons(app)!.Single();
        var originalMenu = tray.Menu;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            results.Add(message);
        }
        static TextBox Field(Window window, string label) => window.GetVisualDescendants().OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == label);
        static void Click(Window window, string content) => window.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, content)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        static async Task SaveRender(Window window, string path)
        {
            await Task.Delay(150);
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
            bitmap.Render(window); bitmap.Save(path, PngBitmapEncoderOptions.Default);
        }
        var stationCount = app.Settings.Stations.Count;
        var canceled = new StationDialog(app, null);
        var completion = canceled.ShowDialog<bool>(app.Window); await Task.Delay(150);
        Field(canceled, "Station name").Text = "Unsaved station";
        Click(canceled, "Cancel");
        Check(!await completion && app.Settings.Stations.Count == stationCount, "Cancel station leaves settings unchanged");

        var stationDialog = new StationDialog(app, null);
        completion = stationDialog.ShowDialog<bool>(app.Window); await Task.Delay(150);
        Click(stationDialog, "Save");
        Check(!completion.IsCompleted && app.Settings.Stations.Count == stationCount, "Station requires a name");
        Field(stationDialog, "Station name").Text = "Editor smoke station";
        Field(stationDialog, "Stream URL · https://…").Text = "not a URL";
        Click(stationDialog, "Save");
        Check(!completion.IsCompleted && app.Settings.Stations.Count == stationCount, "Station rejects invalid stream URL");
        Field(stationDialog, "Stream URL · https://…").Text = app.Settings.Stations[0].Url;
        await SaveRender(stationDialog, Path.Combine(output, "station-editor.png"));
        Click(stationDialog, "Save");
        Check(await completion && app.Settings.Stations.Count == stationCount + 1, "Add station saves through the editor");
        var station = app.Settings.Stations[^1];
        app.Refresh();
        Check(ReferenceEquals(tray.Menu, originalMenu), "Saving a station preserves native tray menu identity");
        Check(tray.Menu!.Items.OfType<NativeMenuItem>().Any(i => Equals(i.Header, station.Name)), "Saved station appears in tray menu");
        var edit = new StationDialog(app, station);
        completion = edit.ShowDialog<bool>(app.Window); await Task.Delay(150);
        Field(edit, "Station name").Text = "Edited station"; Click(edit, "Save");
        Check(await completion && station.Name == "Edited station", "Edit station updates its name");
        app.Refresh();
        Check(ReferenceEquals(tray.Menu, originalMenu) && tray.Menu!.Items.OfType<NativeMenuItem>().Any(i => Equals(i.Header, station.Name)), "Renaming a station updates the existing tray menu");

        var scheduleCount = app.Settings.Schedule.Count;
        var scheduleDialog = new ScheduleDialog(app, null, DayOfWeek.Monday);
        completion = scheduleDialog.ShowDialog<bool>(app.Window); await Task.Delay(150);
        Field(scheduleDialog, "Start time · 24-hour HH:mm").Text = "25:99"; Click(scheduleDialog, "Save");
        Check(!completion.IsCompleted && app.Settings.Schedule.Count == scheduleCount, "Schedule rejects invalid time");
        Field(scheduleDialog, "Start time · 24-hour HH:mm").Text = "08:35";
        scheduleDialog.GetVisualDescendants().OfType<ComboBox>().Single().SelectedItem = station;
        await SaveRender(scheduleDialog, Path.Combine(output, "schedule-editor.png"));
        Click(scheduleDialog, "Save");
        Check(await completion && app.Settings.Schedule.Count == scheduleCount + 1 && app.Settings.Schedule[^1].StationId == station.Id, "Add schedule saves station and time");
        app.Refresh();
        Check(ReferenceEquals(tray.Menu, originalMenu), "Saving a schedule preserves native tray menu identity");
        var slot = app.Settings.Schedule[^1];
        var conflict = new ScheduleDialog(app, null, DayOfWeek.Monday);
        completion = conflict.ShowDialog<bool>(app.Window); await Task.Delay(150);
        Field(conflict, "Start time · 24-hour HH:mm").Text = "08:35"; Click(conflict, "Save");
        Check(!completion.IsCompleted && app.Settings.Schedule.Count == scheduleCount + 1, "Schedule rejects conflicting slots");
        Click(conflict, "Cancel"); await completion;
        var editSlot = new ScheduleDialog(app, slot, DayOfWeek.Monday);
        completion = editSlot.ShowDialog<bool>(app.Window); await Task.Delay(150);
        Field(editSlot, "Start time · 24-hour HH:mm").Text = "09:15"; Click(editSlot, "Save");
        Check(await completion && app.Settings.Schedule[^1].Time == "09:15", "Edit schedule updates its time");
        app.Refresh();
        Check(ReferenceEquals(tray.Menu, originalMenu), "Editing a schedule preserves native tray menu identity");
        app.Save();
        var saved = app.Store.Load();
        Check(saved.Stations.Any(s => s.Id == station.Id) && saved.Schedule.Any(s => s.StationId == station.Id && s.Time == "09:15"), "Station and schedule edits persist to disk");

        var delete = new StationDialog(app, station);
        completion = delete.ShowDialog<bool>(app.Window); await Task.Delay(150);
        Click(delete, "Delete station"); await Task.Delay(150);
        var lifetime = (IClassicDesktopStyleApplicationLifetime)app.ApplicationLifetime!;
        var confirmation = lifetime.Windows.Single(w => w != app.Window && w != delete);
        Click(confirmation, "Confirm");
        Check(await completion && app.Settings.Stations.Count == stationCount && app.Settings.Schedule.Count == scheduleCount, "Delete confirmation removes station and associated schedule");
        app.Refresh();
        Check(ReferenceEquals(tray.Menu, originalMenu) && !tray.Menu!.Items.OfType<NativeMenuItem>().Any(i => Equals(i.Header, station.Name)), "Deleting a station updates the existing tray menu");
        var enabled = app.Settings.ScheduleEnabled;
        app.Settings.ScheduleEnabled = !enabled; app.Refresh();
        Check(ReferenceEquals(tray.Menu, originalMenu) && tray.Menu!.Items.OfType<NativeMenuItem>().Any(i => Equals(i.Header, !enabled ? "Turn schedule off" : "Turn schedule on")), "Schedule toggle updates the existing tray menu");
        app.Settings.ScheduleEnabled = enabled; app.Refresh();
    }
}
