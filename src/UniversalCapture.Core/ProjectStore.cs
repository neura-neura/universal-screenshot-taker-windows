using System.IO.Compression;
using System.Text.Json;

namespace UniversalCapture.Core;

public sealed class ProjectStore
{
    public string Root { get; }
    public string RecoveryDirectory => Path.Combine(Root,"Recovery");
    public string CurrentDirectory => Path.Combine(Root,"Current");
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true,WriteIndented=true };
    private readonly SemaphoreSlim gate=new(1,1);
    public ProjectStore(string? root = null)
    {
        Root=root??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"UniversalScreenshotTaker");
        Directory.CreateDirectory(Path.Combine(CurrentDirectory,"images"));Directory.CreateDirectory(RecoveryDirectory);
    }
    public string ImagePath(CapturePage page) => SafePath(CurrentDirectory,page.File);
    public static string SafePath(string root,string relative)
    {
        if(Path.IsPathRooted(relative))throw new InvalidDataException("El proyecto contiene una ruta absoluta.");
        var full=Path.GetFullPath(Path.Combine(root,relative));
        if(!full.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("El proyecto contiene una ruta fuera del archivo.");
        return full;
    }
    public Project Load()
    {
        string manifest=Path.Combine(CurrentDirectory,"project.json");
        if(!File.Exists(manifest))return new();
        var p=JsonSerializer.Deserialize<Project>(File.ReadAllText(manifest),Json)??throw new InvalidDataException("Proyecto vacío.");
        Validate(p,CurrentDirectory);return p;
    }
    private static void Validate(Project p,string root)
    {
        if(p.Version!=1)throw new InvalidDataException("Versión de proyecto no compatible.");
        if(p.Pages.Count>10000||p.Pages.Select(x=>x.Id).Distinct().Count()!=p.Pages.Count)throw new InvalidDataException("Lista de páginas no válida.");
        foreach(var page in p.Pages)
        {
            var file=SafePath(root,page.File);
            if(!File.Exists(file)||!Path.GetExtension(file).Equals(".png",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Falta una imagen del proyecto: "+page.File);
            using var image=Image.FromFile(file);
        }
    }
    public async Task<CapturePage> AddAsync(byte[] image,int? number,CancellationToken ct)
    {
        var page=new CapturePage{Number=number};page.File="images/"+page.Id.ToString("N")+".png";
        await File.WriteAllBytesAsync(ImagePath(page),image,ct);return page;
    }
    public async Task RetakeAsync(CapturePage page,byte[] image,CancellationToken ct)
    {
        // New filename prevents a cached WinUI thumbnail from showing the old image.
        var name="images/"+Guid.NewGuid().ToString("N")+".png";
        await File.WriteAllBytesAsync(SafePath(CurrentDirectory,name),image,ct);
        page.File=name;page.Text="";page.Lines=[];page.KeepImage=false;page.Changed(nameof(CapturePage.File));
    }
    public async Task SaveAsync(Project snapshot,CancellationToken ct=default)
    {
        var json=JsonSerializer.Serialize(snapshot,Json);
        await gate.WaitAsync(ct);
        try {await Atomic.WriteAsync(Path.Combine(CurrentDirectory,"project.json"),async temp=>await File.WriteAllTextAsync(temp,json,ct));}
        finally {gate.Release();}
    }
    public async Task ExportProjectAsync(Project snapshot,string destination,CancellationToken ct=default)
    {
        var json=JsonSerializer.Serialize(snapshot,Json);
        var files=snapshot.Pages.Select(x=>x.File).Distinct().ToArray();
        await gate.WaitAsync(ct);
        try
        {
            await Atomic.WriteAsync(destination, async temp =>
            {
                using var archive=ZipFile.Open(temp,ZipArchiveMode.Create);
                await using(var stream=archive.CreateEntry("project.json").Open()) await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(json),ct);
                foreach(var file in files){ct.ThrowIfCancellationRequested();archive.CreateEntryFromFile(SafePath(CurrentDirectory,file),file,CompressionLevel.Fastest);}
            });
        }finally{gate.Release();}
    }
    public async Task RecoverAsync(Project current)
    {
        if(current.Pages.Count>0)await ExportProjectAsync(current,Path.Combine(RecoveryDirectory,$"Recovery-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.ustproject"));
    }
    public async Task<Project> OpenAsync(string path,Project current,CancellationToken ct)
    {
        string staging=Path.Combine(Root,"Import-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
        try
        {
            await Task.Run(()=>
            {
                using var zip=ZipFile.OpenRead(path);long total=0;
                if(zip.Entries.Count>20001)throw new InvalidDataException("El proyecto tiene demasiados archivos.");
                var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var entry in zip.Entries)
                {
                    ct.ThrowIfCancellationRequested();total=checked(total+entry.Length);
                    if(total>4L*1024*1024*1024||entry.Length>512L*1024*1024)throw new InvalidDataException("El proyecto excede el límite de importación (4 GB).");
                    var output=SafePath(staging,entry.FullName);
                    if(!names.Add(output))throw new InvalidDataException("El proyecto contiene archivos duplicados.");
                    if(entry.FullName.EndsWith('/')){Directory.CreateDirectory(output);continue;}
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);entry.ExtractToFile(output);
                }
            },ct);
            var result=JsonSerializer.Deserialize<Project>(await File.ReadAllTextAsync(Path.Combine(staging,"project.json"),ct),Json)??throw new InvalidDataException("Proyecto vacío.");
            Validate(result,staging);await RecoverAsync(current);
            // Keep the old folder intact until validation and recovery have succeeded.
            string previous=Path.Combine(Root,"Previous-"+Guid.NewGuid().ToString("N"));
            await gate.WaitAsync(ct);
            try
            {
                Directory.Move(CurrentDirectory,previous);
                try {Directory.Move(staging,CurrentDirectory);}catch{Directory.Move(previous,CurrentDirectory);throw;}
                Directory.Delete(previous,true);
            }finally{gate.Release();}
            // Screen coordinates and HWNDs are not portable.
            result.Settings.WindowHandle=0;result.Settings.WindowBounds=null;result.Settings.Field=null;result.Settings.FieldElement=null;result.Settings.Area=null;result.Settings.BrowserSession=null;
            await SaveAsync(result,ct);return result;
        }finally{if(Directory.Exists(staging))Directory.Delete(staging,true);}
    }
}
public static class Atomic
{
    public static async Task WriteAsync(string destination,Func<string,Task> write)
    {
        destination=Path.GetFullPath(destination);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp=Path.Combine(Path.GetDirectoryName(destination)!,"."+Guid.NewGuid().ToString("N")+Path.GetExtension(destination));
        try {await write(temp);File.Move(temp,destination,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
