using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Windows;
using System.Net;
using System.Net.Http;
using System.IO;
using Microsoft.Win32;
using SynthwaveNowPlaying;
using SetupWindow = WavePlayerSetup.MainWindow;

internal static class Program
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "workflow-" + Guid.NewGuid().ToString("N")));
    private static readonly List<string> Results = [];
    private static readonly List<Process> Children = [];
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static string Baseline = "", Package = "";
    private static readonly Version Version = new("1.1.2");
    private static string SettingsFile => Path.Combine(Root, "prefs", "settings.json");

    private static void Check(bool result, string label)
    {
        if (!result) throw new Exception(label);
        Results.Add("PASS " + label);
        Console.WriteLine(Results[^1]);
    }

    private static void Install(string target)
    {
        try { typeof(SetupWindow).GetMethod("InstallPayload", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [target, false, new Progress<int>()]); }
        catch (TargetInvocationException ex) { throw ex.InnerException!; }
    }

    private static string OldInstall(string name)
    {
        var target = Path.Combine(Root, name);
        UpdatePackage.Stage(Baseline, target, target, new Version("1.1.1"), false);
        return target;
    }

    private static Process Start(string target, params string[] args)
    {
        var start = new ProcessStartInfo(Path.Combine(target, "WavePlayer.exe"))
        { UseShellExecute = false, WorkingDirectory = target, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        var process = Process.Start(start)!;
        Children.Add(process);
        return process;
    }

    private static void StopOwn(Process process)
    {
        if (process.HasExited) return;
        var path = process.MainModule!.FileName;
        if (!Path.GetFullPath(path).StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new Exception("Refusing to stop a non-test process");
        // Exercise the normal single-instance exit signal, without force-killing.
        using var exit = Process.Start(new ProcessStartInfo(path, "--exit")
        { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        if (!process.WaitForExit(10000)) throw new Exception("Test player did not exit normally");
    }

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Expected baseline ZIP and current ZIP.");
        Baseline = Path.GetFullPath(args[0]);
        Package = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(Path.Combine(Root, "prefs"));
        Directory.CreateDirectory(Path.Combine(Root, "music"));
        Environment.SetEnvironmentVariable("WAVEPLAYER_LAB_DATA_DIR", Path.Combine(Root, "prefs"));
        var before = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", "WavePlayer", null);
        File.WriteAllText(SettingsFile, new JsonObject
        {
            ["ThemeChoiceMade"] = true, ["UiStyle"] = "NeonDrive", ["Volume"] = 0d,
            ["StartWithWindows"] = false, ["ShuffleEnabled"] = true, ["WidgetWidth"] = 520d,
            ["LastPositionSeconds"] = 27d, ["MusicFolders"] = new JsonArray(Path.Combine(Root, "music")),
            ["PlaylistEdits"] = new JsonObject { ["local"] = new JsonObject
            { ["Removed"] = new JsonArray("keep-removed.mp3"), ["Order"] = new JsonArray("b.mp3", "a.mp3") } },
            ["UnknownFuturePreference"] = "preserve"
        }.ToJsonString());
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                var clean = Path.Combine(Root, "clean");
                Install(clean);
                Check(UpdatePackage.ReadInstalled(clean)!.Version == Version.ToString() && File.Exists(Path.Combine(clean, "coreclr.dll")),
                    "real setup clean install includes runtime and manifest");
                var wizard = new SetupWindow();
                var type = typeof(SetupWindow);
                type.GetField("_installed", Private)!.SetValue(wizard, true);
                type.GetField("_destination", Private)!.SetValue(wizard, clean);
                type.GetField("_uiStyle", Private)!.SetValue(wizard, "Modern80s");
                type.GetMethod("SavePreferences", Private)!.Invoke(wizard, null);
                var prefs = JsonNode.Parse(File.ReadAllText(SettingsFile))!;
                Check(prefs["UiStyle"]!.GetValue<string>() == "Modern80s" && prefs["ThemeChoiceMade"]!.GetValue<bool>(),
                    "setup applies newly selected UI for existing user");
                Check(prefs["UnknownFuturePreference"]!.GetValue<string>() == "preserve" && prefs["ShuffleEnabled"]!.GetValue<bool>() &&
                    prefs["WidgetWidth"]!.GetValue<double>() == 520 && prefs["PlaylistEdits"]!["local"]!["Removed"]![0]!.GetValue<string>() == "keep-removed.mp3",
                    "setup preserves shuffle, size, playlist edits and unknown preferences");
                Check(Equals(before, Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", "WavePlayer", null)),
                    "isolated setup leaves actual Windows startup unchanged");
                var running = Start(clean, "--startup");
                await Task.Delay(2500);
                try { Install(clean); throw new Exception("Setup accepted running player"); }
                catch (IOException ex) { Check(ex.Message.Contains("running", StringComparison.OrdinalIgnoreCase), "setup refuses running tray copy before changing files"); }
                Check(UpdatePackage.ReadInstalled(clean)!.Version == Version.ToString(), "rejected install leaves original version intact");
                StopOwn(running);
                var upgrade = OldInstall("upgrade");
                File.WriteAllText(Path.Combine(upgrade, "personal.txt"), "keep");
                Install(upgrade);
                Check(UpdatePackage.ReadInstalled(upgrade)!.Version == Version.ToString() && File.ReadAllText(Path.Combine(upgrade, "personal.txt")) == "keep",
                    "real setup upgrades old install and preserves personal files");

                // HTTP fixture carries the real ZIP bytes through the production
                // downloader, SHA-256 checks, file transaction and player restart.
                var target = OldInstall("auto-update");
                File.WriteAllText(Path.Combine(target, "personal.txt"), "keep");
                var asset = new UpdateAsset(new Uri("https://github.com/HASHMSI/Waveplayer/releases/download/v1.1.2/WavePlayerFiles.zip"),
                    new FileInfo(Package).Length, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Package))));
                var handler = new PackageHandler(Package);
                var window = new UpdaterWindow(Path.Combine(target, "WavePlayer.exe"), -1, resumeHidden: true)
                {
                    ReleaseProvider = () => Task.FromResult<ReleasePackage?>(new(Version, "v1.1.2", asset, null)),
                    DownloadHandler = handler
                };
                var completion = new TaskCompletionSource();
                window.Closed += (_, _) => completion.TrySetResult();
                app.MainWindow = window;
                window.Show();
                await completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
                Check(handler.Count == 1 && UpdatePackage.ReadInstalled(target)!.Version == Version.ToString(),
                    "automatic HTTP download, verification and install completed");
                Check(FileVersionInfo.GetVersionInfo(Path.Combine(target, "WavePlayer.exe")).FileVersion!.StartsWith("1.1.2") &&
                    File.ReadAllText(Path.Combine(target, "personal.txt")) == "keep", "updated executable version matches and personal files survive");
                var updated = Process.GetProcessesByName("WavePlayer").FirstOrDefault(process =>
                {
                    try { return process.MainModule!.FileName.Equals(Path.Combine(target, "WavePlayer.exe"), StringComparison.OrdinalIgnoreCase); }
                    catch { return false; }
                });
                Check(updated is not null && !updated.HasExited, "updated player restarted successfully in the tray");
                if (updated is not null) Children.Add(updated);
                Check(!JsonNode.Parse(File.ReadAllText(SettingsFile))!["StartWithWindows"]!.GetValue<bool>(), "startup opt-out survives setup and in-app update");
                File.WriteAllLines(Path.Combine(Root, "results.txt"), Results);
                Console.WriteLine("RESULTS " + Path.Combine(Root, "results.txt"));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                Environment.ExitCode = 1;
                File.WriteAllText(Path.Combine(Root, "results.txt"), string.Join("\n", Results) + "\nFAIL " + ex);
            }
            finally
            {
                foreach (var child in Children) try { StopOwn(child); } catch (Exception ex) { Console.Error.WriteLine(ex.Message); }
                app.Shutdown();
            }
        });
        app.Run();
    }

    private sealed class PackageHandler(string package) : HttpMessageHandler
    {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Count++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(File.OpenRead(package)) });
        }
    }
}
