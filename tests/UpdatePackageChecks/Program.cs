using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SynthwaveNowPlaying;

var root=Path.Combine(Path.GetTempPath(),"WavePlayer-PackageChecks",Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var installed=Path.Combine(root,"installed");Directory.CreateDirectory(installed);
var baseline=new Dictionary<string,byte[]> { ["WavePlayer.exe"]=[1,2,3], ["WavePlayer.dll"]=[4,5,6], ["Assets/image.bin"]=[7,8,9] };
var next=baseline.ToDictionary(p=>p.Key,p=>p.Value);next["WavePlayer.dll"]=[11,12,13];next["new.bin"]=[21,22];
UpdateManifest Manifest(string v, Dictionary<string,byte[]> files,string? b=null,List<string>? c=null)=>new(2,v,b,files.Select(p=>new UpdateFile(p.Key,p.Value.Length,Convert.ToHexString(SHA256.HashData(p.Value)))).ToList(),c);
var old=Manifest("1.0.0",baseline);var full=Manifest("1.2.0",next);var patch=full with{BaseVersion="1.0.0",Changed=["WavePlayer.dll","new.bin"]};
foreach(var (name,bytes) in baseline){var p=Path.Combine(installed,name);Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.WriteAllBytes(p,bytes);}
File.WriteAllText(Path.Combine(installed,UpdatePackage.ManifestName),JsonSerializer.Serialize(old));
string Zip(string name,UpdateManifest? m, Dictionary<string,byte[]> entries){var path=Path.Combine(root,name+".zip");using var z=ZipFile.Open(path,ZipArchiveMode.Create);if(m is not null){using var w=new StreamWriter(z.CreateEntry(UpdatePackage.ManifestName).Open());w.Write(JsonSerializer.Serialize(m));}foreach(var (key,bytes)in entries){using var f=z.CreateEntry(key).Open();f.Write(bytes);}return path;}
int count=0;
void Verify(string label,Action check){check();Console.WriteLine("PASS "+label);count++;}
void Reject(string label,Action check)=>Verify(label,()=>{try{check();}catch(InvalidDataException){return;}throw new Exception("Invalid package accepted: "+label);});
Verify("full manifest, file hashes, extras ignored",()=>{var entries=next.ToDictionary(p=>p.Key,p=>p.Value);entries["unlisted.exe"]=[0];var stage=Path.Combine(root,"full");var m=UpdatePackage.Stage(Zip("full",full,entries),stage,installed,new Version("1.2.0"),false);if(m.Version!="1.2.0"||File.Exists(Path.Combine(stage,"unlisted.exe")))throw new Exception();});
Verify("file patch reuses verified unchanged files",()=>{var entries=next.Where(p=>patch.Changed!.Contains(p.Key)).ToDictionary();var stage=Path.Combine(root,"patch");var m=UpdatePackage.Stage(Zip("patch",patch,entries),stage,installed,new Version("1.2.0"),true);if(m.BaseVersion is not null||!File.ReadAllBytes(Path.Combine(stage,"Assets/image.bin")).SequenceEqual(baseline["Assets/image.bin"]))throw new Exception();});
Reject("wrong base rejected",()=>UpdatePackage.Stage(Zip("base",patch with{BaseVersion="0.8.0"},next),Path.Combine(root,"badbase"),installed,new Version("1.2.0"),true));
Reject("wrong hash rejected",()=>UpdatePackage.Stage(Zip("hash",full,next.ToDictionary(p=>p.Key,p=>new byte[p.Value.Length])),Path.Combine(root,"badhash"),installed,new Version("1.2.0"),false));
Reject("wrong release version rejected",()=>UpdatePackage.Stage(Zip("version",full,next),Path.Combine(root,"badversion"),installed,new Version("1.3.0"),false));
Reject("portable/setup-only ZIP rejected",()=>UpdatePackage.ReadArchive(Zip("legacy",null,baseline)));
foreach(var path in new[]{"../escape.bin","/absolute.bin","C:/target.bin","folder\\bad.bin","waveplayer-UPDATE.json"})Reject("unsafe path "+path,()=>UpdatePackage.Validate(full with{Files=[..full.Files,new UpdateFile(path,0,new string('0',64))]}));
Reject("corrupt reused baseline rejected",()=>{File.WriteAllBytes(Path.Combine(installed,"Assets/image.bin"),[4,4,4]);UpdatePackage.Stage(Zip("corruptbaseline",patch,next),Path.Combine(root,"badbaseline"),installed,new Version("1.2.0"),true);});
Console.WriteLine($"{count} package checks passed.");
var prepared=Path.Combine(root,"full");
var fresh=Path.Combine(root,"fresh-install");
Verify("clean install transaction",()=>{UpdateInstall.Apply(prepared,fresh,full,allowLegacyInstall:true);if(UpdatePackage.ReadInstalled(fresh)!.Version!="1.2.0")throw new Exception();});
var legacy=Path.Combine(root,"legacy-install");Directory.CreateDirectory(legacy);File.WriteAllText(Path.Combine(legacy,"WavePlayer.exe"),"old single-file player");File.WriteAllText(Path.Combine(legacy,"personal.txt"),"keep");
Verify("legacy upgrade preserves personal files",()=>{UpdateInstall.Apply(prepared,legacy,full,allowLegacyInstall:true);if(File.ReadAllText(Path.Combine(legacy,"personal.txt"))!="keep")throw new Exception();});
var upgrade=Path.Combine(root,"upgrade-install");Directory.CreateDirectory(upgrade);foreach(var (name,bytes)in baseline){var p=Path.Combine(upgrade,name);Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.WriteAllBytes(p,bytes);}File.WriteAllText(Path.Combine(upgrade,UpdatePackage.ManifestName),JsonSerializer.Serialize(old));
Verify("startup failure rolls back every previous file",()=>{try{UpdateInstall.Apply(prepared,upgrade,full,()=>throw new IOException("Synthetic startup failure"));throw new Exception("Failure ignored");}catch(IOException){}foreach(var (name,bytes)in baseline)if(!File.ReadAllBytes(Path.Combine(upgrade,name)).SequenceEqual(bytes))throw new Exception("Rollback mismatch");if(UpdatePackage.ReadInstalled(upgrade)!.Version!="1.0.0"||File.Exists(Path.Combine(upgrade,"new.bin")))throw new Exception("Rollback incomplete");});
Verify("locked file stops install before mutation",()=>{using var locked=new FileStream(Path.Combine(upgrade,"WavePlayer.dll"),FileMode.Open,FileAccess.Read,FileShare.None);try{UpdateInstall.Apply(prepared,upgrade,full);throw new Exception("Lock ignored");}catch(IOException){}if(!File.ReadAllBytes(Path.Combine(upgrade,"WavePlayer.exe")).SequenceEqual(baseline["WavePlayer.exe"]))throw new Exception("Changed before preflight completed");});
Verify("file-copy failure restores previous install",()=>{Directory.CreateDirectory(Path.Combine(upgrade,"new.bin"));try{UpdateInstall.Apply(prepared,upgrade,full);throw new Exception("Copy failure ignored");}catch(IOException){}foreach(var (name,bytes)in baseline)if(!File.ReadAllBytes(Path.Combine(upgrade,name)).SequenceEqual(bytes))throw new Exception("Copy rollback mismatch");Directory.Delete(Path.Combine(upgrade,"new.bin"));});
Verify("successful manifest upgrade commits new files",()=>{UpdateInstall.Apply(prepared,upgrade,full);if(UpdatePackage.ReadInstalled(upgrade)!.Version!="1.2.0"||!File.ReadAllBytes(Path.Combine(upgrade,"new.bin")).SequenceEqual(next["new.bin"]))throw new Exception();});
if(args.Length>0)
{
    var header=UpdatePackage.ReadArchive(args[0]);
    UpdatePackage.Stage(args[0],Path.Combine(root,"published-payload"),installed,Version.Parse(header.Version),header.BaseVersion is not null);
    Console.WriteLine($"PASS published setup payload: {header.Files.Count} SHA-256 verified files");
}
