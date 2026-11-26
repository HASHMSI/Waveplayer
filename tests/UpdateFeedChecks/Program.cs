using System.Reflection;
using System.Text.Json;
using SynthwaveNowPlaying;

var type=typeof(MainWindow).Assembly.GetType("SynthwaveNowPlaying.UpdateClient")!;
var parse=type.GetMethod("FindNewestManifest",BindingFlags.NonPublic|BindingFlags.Static)!;
object Release(string version,string asset="WavePlayerFiles.zip",bool draft=false,bool prerelease=false,bool latest=false,string? hash=null)=>new {
    tag_name="v"+version,draft,prerelease,make_latest=latest,assets=new[]{new {
        name=asset,state="uploaded",browser_download_url=$"https://github.com/HASHMSI/Waveplayer/releases/download/v{version}/{asset}",
        size=4096,digest="sha256:"+(hash??new string('a',64))}}
};
object? Find(object[] page,object? previous=null){using var json=JsonDocument.Parse(JsonSerializer.Serialize(page));return parse.Invoke(null,[json.RootElement,previous]);}
string VersionOf(object? value)=>value?.GetType().GetProperty("Version")!.GetValue(value)!.ToString()??"none";
int count=0;
void Expect(string label,object? actual,string expected){var got=VersionOf(actual);if(got!=expected)throw new Exception($"{label}: expected {expected}, got {got}");Console.WriteLine("PASS "+label);count++;}
Expect("latest setup does not mask a newer manifest",Find([Release("1.0.1","WavePlayerSetup.exe",latest:true),Release("1.3.0"),Release("1.2.0")]),"1.3.0");
Expect("highest version wins over publication order",Find([Release("1.3.0"),Release("1.8.0"),Release("1.1.0")]),"1.8.0");
Expect("drafts and prereleases ignored",Find([Release("9.0.0",draft:true),Release("8.0.0",prerelease:true),Release("1.4.0")]),"1.4.0");
Expect("setup-only and old portable releases ignored",Find([Release("5.0.0","WavePlayerSetup.exe"),Release("4.0.0","WavePlayerPortable.zip"),Release("1.5.0")]),"1.5.0");
Expect("invalid SHA digest ignored",Find([Release("9.0.0",hash:"bad"),Release("1.6.0")]),"1.6.0");
Expect("newer manifest found on later page",Find([Release("1.9.0")],Find([Release("1.2.0")])) ,"1.9.0");
Expect("later older page cannot downgrade choice",Find([Release("1.1.0")],Find([Release("1.9.0")])) ,"1.9.0");
Expect("no manifest means no executable fallback",Find([Release("9.0.0","WavePlayerSetup.exe")]),"none");
Expect("existing asset filename casing accepted",Find([Release("1.0.4","WaveplayerFiles.zip")]),"1.0.4");
Console.WriteLine($"{count} update-feed checks passed.");
if(args.Contains("--live")){
    var task=(Task)type.GetMethod("FetchLatestAsync",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[CancellationToken.None])!;
    await task;
    Console.WriteLine("LIVE newest stable manifest: "+VersionOf(task.GetType().GetProperty("Result")!.GetValue(task)));
}
