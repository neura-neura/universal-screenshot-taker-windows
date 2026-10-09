using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Net;

namespace UniversalCapture.Core;

public record BrowserApplication(string Name,string Executable,string Family,bool IsDefault=false)
{
    public bool Supported=>Family is "chromium" or "firefox";
    public string ExtensionPage=>Family=="firefox"?"about:debugging#/runtime/this-firefox":Path.GetFileName(Executable).Equals("msedge.exe",StringComparison.OrdinalIgnoreCase)?"edge://extensions/":"chrome://extensions/";
}

public static class BrowserDiscovery
{
    private static readonly string[] executables=["thorium.exe","chrome.exe","msedge.exe","firefox.exe","brave.exe","vivaldi.exe","opera.exe","chromium.exe","librewolf.exe","waterfox.exe","floorp.exe","zen.exe"];
    public static string? ExecutableFromCommand(string? command)
    {
        if(string.IsNullOrWhiteSpace(command))return null;
        command=Environment.ExpandEnvironmentVariables(command.Trim());
        if(command.StartsWith('"')){int end=command.IndexOf('"',1);return end>1?command[1..end]:null;}
        int exe=command.IndexOf(".exe",StringComparison.OrdinalIgnoreCase);return exe>=0?command[..(exe+4)]:null;
    }
    public static BrowserApplication FromExecutable(string path,string? name=null,bool isDefault=false)
    {
        path=Path.GetFullPath(path);
        string exe=Path.GetFileName(path).ToLowerInvariant();
        string family=exe is "firefox.exe" or "librewolf.exe" or "waterfox.exe" or "floorp.exe" or "zen.exe"?"firefox":executables.Contains(exe)?"chromium":"unknown";
        name=string.IsNullOrWhiteSpace(name)?FileVersionInfo.GetVersionInfo(path).ProductName:name;
        return new(string.IsNullOrWhiteSpace(name)?Path.GetFileNameWithoutExtension(path):name,path,family,isDefault);
    }
    public static IReadOnlyList<BrowserApplication> Detect(string? portable=null)
    {
        var found=new Dictionary<string,BrowserApplication>(StringComparer.OrdinalIgnoreCase);
        string? defaultExe=DefaultExecutable();
        void Add(string? path,string? name=null)
        {
            if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))return;
            try{var browser=FromExecutable(path,name,string.Equals(Path.GetFullPath(path),defaultExe,StringComparison.OrdinalIgnoreCase));
                if(!found.ContainsKey(browser.Executable))found[browser.Executable]=browser;
            }catch(Exception ex)when(ex is ArgumentException or IOException or UnauthorizedAccessException){}
        }
        foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
        {
            try{
                using var registry=RegistryKey.OpenBaseKey(hive,view);
                using var clients=registry.OpenSubKey(@"SOFTWARE\Clients\StartMenuInternet");
                foreach(string client in clients?.GetSubKeyNames()??[]){using var key=clients!.OpenSubKey(client);using var command=key?.OpenSubKey(@"shell\open\command");Add(ExecutableFromCommand(command?.GetValue("") as string),key?.GetValue("") as string);}
                foreach(string exe in executables){using var key=registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\"+exe);Add(ExecutableFromCommand(key?.GetValue("") as string));}
            }catch(Exception ex)when(ex is UnauthorizedAccessException or System.Security.SecurityException or IOException){}
        }
        string[] relative=[@"Google\Chrome\Application\chrome.exe",@"Microsoft\Edge\Application\msedge.exe",@"Thorium\Application\thorium.exe",@"BraveSoftware\Brave-Browser\Application\brave.exe",@"Vivaldi\Application\vivaldi.exe",@"Mozilla Firefox\firefox.exe",@"Programs\Opera\opera.exe",@"Programs\Thorium\Application\thorium.exe",@"Programs\Mozilla Firefox\firefox.exe",@"LibreWolf\librewolf.exe",@"Waterfox\waterfox.exe",@"Floorp\floorp.exe",@"Zen Browser\zen.exe"];
        foreach(string root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)})
            foreach(string sub in relative)Add(Path.Combine(root,sub));
        Add(defaultExe);Add(portable);
        return found.Values.OrderByDescending(x=>x.IsDefault).ThenBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
    public static string? DefaultExecutable()
    {
        uint count=0;AssocQueryString(0x1000,2,"https",null,null,ref count);
        if(count==0)return null;
        var value=new StringBuilder((int)count);
        return AssocQueryString(0x1000,2,"https",null,value,ref count)==0?Path.GetFullPath(value.ToString()):null;
    }
    public static BrowserApplication? SelectBrowser(IReadOnlyList<BrowserApplication> browsers,string? preferred=null)
        =>browsers.FirstOrDefault(x=>!string.IsNullOrWhiteSpace(preferred)&&string.Equals(x.Executable,preferred,StringComparison.OrdinalIgnoreCase))
            ??browsers.FirstOrDefault(x=>x.IsDefault)??browsers.FirstOrDefault(x=>x.Supported)??browsers.FirstOrDefault();
    public static void Open(BrowserApplication browser,string url)
    {
        if(!File.Exists(browser.Executable))throw new FileNotFoundException(Locale.T("El navegador seleccionado ya no está disponible."),browser.Executable);
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https" or "file"))throw new ArgumentException(Locale.T("Dirección del navegador no válida."));
        var start=new ProcessStartInfo(browser.Executable){UseShellExecute=false};start.ArgumentList.Add(url);Process.Start(start);
    }
    public static string InstallationGuide(BrowserApplication browser,string extensionDirectory,string? language=null,string? systemLanguage=null)
    {
        string resolved=Locale.Resolve(language??Locale.Language,systemLanguage);
        string Text(string value)=>WebUtility.HtmlEncode(Locale.T(value,resolved));
        string Field(string value)=>$"<input readonly aria-label='{Text("Selecciona y copia esta ruta")}' value='{WebUtility.HtmlEncode(value)}' onfocus='this.select()'><button onclick='copy(this)'>{Text("Copiar")}</button>";
        string source=browser.Family=="firefox"?Path.Combine(extensionDirectory,"manifest.json"):extensionDirectory;
        return $$"""
        <!doctype html><html lang="{{(resolved=="zh"?"zh-Hans":resolved)}}"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{Text("Instalar la extensión del navegador")}}</title>
        <style>:root{color-scheme:light dark;font:16px/1.6 'Segoe UI',sans-serif}body{max-width:800px;margin:40px auto;padding:24px}h1{font-size:28px}li{margin:24px 0}input{box-sizing:border-box;width:100%;padding:12px;font:14px monospace;border:1px solid #888;border-radius:6px}button{font:inherit;padding:6px 18px;margin-top:8px;border-radius:6px;border:1px solid #888;cursor:pointer}#status{min-height:2em}strong{font-weight:600}</style>
        <h1>{{Text("Instalar la extensión del navegador")}}</h1><p><strong>{{WebUtility.HtmlEncode(browser.Name)}}</strong></p>
        <p>{{Text("El navegador requiere que confirmes la instalación. Sigue estos pasos en tu perfil habitual.")}}</p>
        <ol><li>{{Text("Copia esta dirección, abre una pestaña y pégala en la barra de direcciones. Presiona Enter.")}}{{Field(browser.ExtensionPage)}}</li>
        <li>{{Text(browser.Family=="firefox"?"Elige Cargar complemento temporal y selecciona este archivo:":"Activa Modo desarrollador, elige Cargar descomprimida y selecciona esta carpeta:")}}{{Field(source)}}</li>
        <li>{{Text("Regresa a la pestaña del lector y pulsa el icono de Universal Capture. La conexión con la aplicación es automática.")}}</li></ol>
        <p>{{Text(browser.Family=="firefox"?"Firefox elimina las extensiones temporales al reiniciar. Una instalación permanente requiere un paquete firmado.":"Mantén esta carpeta en su ubicación mientras uses la extensión. No necesitas instalarla otra vez al abrir la aplicación.")}}</p>
        <p id="status" role="status"></p>
        <script>function copy(button){const input=button.previousElementSibling;input.focus();input.select();const copied=document.execCommand('copy');document.getElementById('status').textContent=copied?{{System.Text.Json.JsonSerializer.Serialize(Locale.T("Copiado.",resolved))}}:{{System.Text.Json.JsonSerializer.Serialize(Locale.T("Presiona Ctrl+C para copiar la ruta seleccionada.",resolved))}};}</script></html>
        """;
    }
    [DllImport("Shlwapi.dll",CharSet=CharSet.Unicode)]
    private static extern int AssocQueryString(uint flags,uint str,string assoc,string? extra,StringBuilder? output,ref uint length);
}
