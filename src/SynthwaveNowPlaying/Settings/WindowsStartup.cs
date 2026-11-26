using Microsoft.Win32;
using System.IO;

namespace SynthwaveNowPlaying;

internal static class WindowsStartup
{
    private const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static void Sync(bool enabled)
    {
        // Isolated review windows must never register themselves at logon.
        if(LabData.Isolated)return;
        var exe=Environment.ProcessPath;
        if(exe is null||!Path.GetFileName(exe).Equals("WavePlayer.exe",StringComparison.OrdinalIgnoreCase))return;
        if(!File.Exists(Path.Combine(Path.GetDirectoryName(exe)!,UpdatePackage.ManifestName)))return;
        using var run=Registry.CurrentUser.CreateSubKey(RunKey,true);
        if(enabled)run.SetValue("WavePlayer",$"\"{exe}\" --startup",RegistryValueKind.String);
        else run.DeleteValue("WavePlayer",false);
    }
}
