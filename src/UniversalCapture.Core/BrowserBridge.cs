using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UniversalCapture.Core;

// Optional selector bridge. Visual capture never requires an extension.
// Bound to loopback, random per-launch secret, limited request sizes, no arbitrary code commands.
public sealed class BrowserBridge : IDisposable
{
    public static BrowserBridge Current {get;}=new();
    public string Token {get;}=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    public int Port {get;private set;}
    private TcpListener? listener;
    private readonly CancellationTokenSource stop=new();
    private readonly ConcurrentQueue<(string Id,string? Session,object Payload)> commands=new();
    private readonly ConcurrentDictionary<string,TaskCompletionSource<JsonElement>> waiting=new();
    public string Status {get;private set;}="Sin conectar";
    private readonly ConcurrentDictionary<string,DateTime> polls=new();
    public bool HasConnection(string? session=null)=>polls.Any(x=>(session is null||x.Key==session)&&DateTime.UtcNow-x.Value<TimeSpan.FromSeconds(5));
    public void Start()
    {
        if(listener is not null)return;
        for(int port=38473;port<=38477;port++)
        {
            var candidate=new TcpListener(IPAddress.Loopback,port);
            try{candidate.Start();listener=candidate;Port=port;break;}catch(SocketException){candidate.Stop();}
        }
        if(listener is null)throw new InvalidOperationException("No se pudo iniciar la conexión local del navegador.");
        _ = AcceptAsync();
    }
    private async Task AcceptAsync()
    {
        try {while(!stop.IsCancellationRequested){var client=await listener!.AcceptTcpClientAsync(stop.Token);_ = HandleAsync(client);}}
        catch(OperationCanceledException){}catch(SocketException) when(stop.IsCancellationRequested){}
    }
    private async Task HandleAsync(TcpClient client)
    {
        using(client)
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(5));var ct=timeout.Token;
            try
            {
                var stream=client.GetStream();var header=new List<byte>();byte[] one=new byte[1];
                while(header.Count<16384){if(await stream.ReadAsync(one,ct)==0)return;header.Add(one[0]);if(header.Count>=4&&header[^4]==13&&header[^3]==10&&header[^2]==13&&header[^1]==10)break;}
                if(header.Count>=16384)return;
                string[] lines=Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");var request=lines[0].Split(' ');
                if(request.Length!=3)return;
                var headers=lines.Skip(1).Where(x=>x.Contains(':')).ToDictionary(x=>x[..x.IndexOf(':')].Trim(),x=>x[(x.IndexOf(':')+1)..].Trim(),StringComparer.OrdinalIgnoreCase);
                string origin=headers.GetValueOrDefault("Origin","");
                if(origin!=""&&!origin.StartsWith("chrome-extension://",StringComparison.Ordinal)&&!origin.StartsWith("moz-extension://",StringComparison.Ordinal)){await Respond("403 Forbidden","{}","");return;}
                if(request[0]=="OPTIONS"){await Respond("204 No Content","",origin);return;}
                if(request[0]=="GET"&&request[1]=="/connection")
                {
                    if(!origin.StartsWith("chrome-extension://",StringComparison.Ordinal)&&!origin.StartsWith("moz-extension://",StringComparison.Ordinal)&&!(origin==""&&headers.GetValueOrDefault("X-UST-Discovery")=="1")){await Respond("403 Forbidden","{}",origin);return;}
                    await Respond("200 OK",JsonSerializer.Serialize(new{app="UniversalCapture",token=Token,language=Locale.Resolve(Locale.Language)}),origin);return;
                }
                if(request[0]=="GET" && request[1] is "/reader" or "/iframe")
                {
                    string html=request[1]=="/reader"?await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Fixtures","reader.html"),ct):$"<!doctype html><html><meta charset='utf-8'><title>Universal Capture · iframe de prueba</title><body style='margin:0'><iframe title='Lector de otro origen' src='http://localhost:{Port}/reader' style='border:0;width:100%;height:95vh'></iframe></body></html>";
                    await Respond("200 OK",html,origin,"text/html; charset=utf-8");return;
                }
                string auth=headers.GetValueOrDefault("Authorization","");
                if(!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(auth),Encoding.UTF8.GetBytes("Bearer "+Token))){await Respond("401 Unauthorized","{}",origin);return;}
                if(request[0]=="GET"&&request[1]=="/command")
                {
                    Status="Conectado";object? payload=null;
                    string session=headers.GetValueOrDefault("X-UST-Session","");
                    polls[session]=DateTime.UtcNow;
                    var until=DateTime.UtcNow.AddSeconds(2);
                    do
                    {
                        for(int remaining=commands.Count;remaining>0&&commands.TryDequeue(out var command);remaining--)
                        {
                            if(!waiting.ContainsKey(command.Id))continue;
                            if(command.Session is null||command.Session==session){payload=command.Payload;break;}
                            commands.Enqueue(command);
                        }
                        if(payload is not null)break;
                        await Task.Delay(25,ct);
                    }while(DateTime.UtcNow<until);
                    polls[session]=DateTime.UtcNow;
                    await Respond("200 OK",payload is null?"{}":JsonSerializer.Serialize(payload,new JsonSerializerOptions(JsonSerializerDefaults.Web)),origin);return;
                }
                if(request[0]=="POST"&&request[1]=="/result")
                {
                    if(!int.TryParse(headers.GetValueOrDefault("Content-Length"),out int length)||length<0||length>32*1024*1024){await Respond("413 Content Too Large","{}",origin);return;}
                    byte[] body=new byte[length];await stream.ReadExactlyAsync(body,ct);using var json=JsonDocument.Parse(body);
                    if(json.RootElement.TryGetProperty("id",out var id)&&waiting.TryRemove(id.GetString()??"",out var tcs))tcs.TrySetResult(json.RootElement.Clone());
                    await Respond("200 OK","{}",origin);return;
                }
                if(request[0]=="GET"&&request[1].StartsWith("/pending?id=",StringComparison.Ordinal))
                {
                    var id=Uri.UnescapeDataString(request[1][12..]);
                    await Respond("200 OK",JsonSerializer.Serialize(new{pending=waiting.ContainsKey(id)}),origin);return;
                }
                await Respond("404 Not Found","{}",origin);
                async Task Respond(string status,string body,string allowed,string contentType="application/json")
                {
                    byte[] data=Encoding.UTF8.GetBytes(body);
                    string response=$"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {data.Length}\r\nConnection: close\r\nAccess-Control-Allow-Methods: GET, POST, OPTIONS\r\nAccess-Control-Allow-Headers: Authorization, Content-Type, X-UST-Session, X-UST-Discovery\r\n"+(allowed.Length>0?$"Access-Control-Allow-Origin: {allowed}\r\n":"")+"\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response),ct);await stream.WriteAsync(data,ct);
                }
            }catch(Exception ex) when(ex is IOException or OperationCanceledException or JsonException or ArgumentException){}
        }
    }
    public async Task<JsonElement> CommandAsync(Configuration c,string action,int? page,CancellationToken ct)
    {
        Start();string id=Guid.NewGuid().ToString("N");var tcs=new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);waiting[id]=tcs;
        var targetSession=(action is "go" or "restore"||action.StartsWith("pick-"))&&!HasConnection(c.BrowserSession)?null:c.BrowserSession;
        commands.Enqueue((id,targetSession,new{id,action,url=c.Url,mode=c.FieldMode,selector=c.Selector,page,outline=c.Outline,twoCorners=c.TwoCorners,area=c.Area,waitSeconds=c.WaitSeconds,stable=c.WaitForStable,language=Locale.Resolve(c.Language)}));
        try
        {
            var result=await tcs.Task.WaitAsync(action.StartsWith("pick-")?TimeSpan.FromMinutes(2):action is "ready" or "capture"?TimeSpan.FromSeconds(c.WaitSeconds+15):TimeSpan.FromSeconds(12),ct);
            if(result.TryGetProperty("error",out var error))throw new InvalidOperationException(error.GetString());return result;
        }catch(TimeoutException){throw new InvalidOperationException("El complemento no respondió. Abre Ayuda, conecta la pestaña del lector y vuelve a intentarlo.");}
        finally{waiting.TryRemove(id,out _);}
    }
    public async Task GoAsync(Configuration c,int page,CancellationToken ct)
    {
        var result=await CommandAsync(c,"go",page,ct);
        if(result.TryGetProperty("session",out var session))c.BrowserSession=session.GetString();
        if(result.TryGetProperty("url",out var url))c.Url=url.GetString()??c.Url;
    }
    public void Dispose(){stop.Cancel();listener?.Stop();foreach(var tcs in waiting.Values)tcs.TrySetCanceled();stop.Dispose();}
}
