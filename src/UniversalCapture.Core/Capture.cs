using System.Diagnostics;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace UniversalCapture.Core;

public static class Capture
{
    public static void OpenBrowser(string url)
    {
        if(!Uri.TryCreate(url.Contains(":")?url:"https://"+url,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https" or "file"))throw new ArgumentException("Escribe una dirección HTTP o HTTPS válida.");
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});
    }
    public static void BindWindow(Configuration c, Region region)
    {
        var p=new Point(region.X+region.Width/2,region.Y+region.Height/2);
        var hwnd=Native.GetAncestor(Native.WindowFromPoint(p),2);
        Native.GetWindowThreadProcessId(hwnd,out var pid);
        if(pid==Environment.ProcessId||hwnd==0)throw new InvalidOperationException("Selecciona un punto dentro del navegador.");
        var className=new System.Text.StringBuilder(256);Native.GetClassName(hwnd,className,className.Capacity);
        if(className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd")throw new InvalidOperationException("Elegiste el escritorio. Selecciona el campo o un área dentro del navegador.");
        c.WindowHandle=(long)hwnd;c.BrowserProcessId=(int)pid;c.WindowBounds=Region.From(Native.Bounds(hwnd));
    }
    public static async Task GoAsync(Configuration c,int number,CancellationToken ct)
    {
        if(c.FieldMode!="visual") {await BrowserBridge.Current.GoAsync(c,number,ct);return;}
        await Native.ActivateAsync(c,ct);
        await Native.WaitForModifiersAsync(ct);
        Native.Validate(c);
        await Task.Run(()=>BrowserElements.Focus(c),ct);
        await Task.Delay(140,ct);
        Native.Validate(c);ct.ThrowIfCancellationRequested();
        var value=number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if(await Task.Run(()=>BrowserElements.TrySetValue(c,value),ct))
        {
            Native.Validate(c);ct.ThrowIfCancellationRequested();Native.Key(0x0D);Native.Key(0x0D,true);
        }
        else Native.ReplaceText(value);
    }
    public static byte[] Screen(Region region)
    {
        if(region.Width<2||region.Height<2||region.Width>20000||region.Height>20000)throw new ArgumentException("El área de captura no es válida.");
        if(!SystemInformation.VirtualScreen.Contains(region.Rectangle))throw new InvalidOperationException("El área quedó fuera de las pantallas. Selecciónala otra vez.");
        using var bitmap=new Bitmap(region.Width,region.Height,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(bitmap)) g.CopyFromScreen(region.X,region.Y,0,0,bitmap.Size,CopyPixelOperation.SourceCopy);
        using var stream=new MemoryStream();bitmap.Save(stream,ImageFormat.Png);return stream.ToArray();
    }
    public static async Task<byte[]> ReadyAsync(Configuration c,CancellationToken ct)
    {
        if(c.Area is null)throw new InvalidOperationException("Selecciona el área de captura.");
        if(c.FieldMode!="visual")
        {
            async Task<byte[]> FromTab()
            {
                var result=await BrowserBridge.Current.CommandAsync(c,"capture",null,ct);
                if(result.TryGetProperty("rect",out var r))c.Area=new((int)Math.Round(r.GetProperty("x").GetDouble()),(int)Math.Round(r.GetProperty("y").GetDouble()),(int)Math.Round(r.GetProperty("width").GetDouble()),(int)Math.Round(r.GetProperty("height").GetDouble()));
                var data=result.GetProperty("image").GetString()??throw new InvalidDataException("El navegador no devolvió la captura.");
                if(!data.StartsWith("data:image/png;base64,",StringComparison.Ordinal))throw new InvalidDataException("Formato de captura no válido.");
                return Convert.FromBase64String(data[22..]);
            }
            return await FromTab();
        }
        await Task.Delay(TimeSpan.FromSeconds(c.WaitSeconds),ct);
        Native.Validate(c,false);
        byte[] image=await Task.Run(()=>Screen(c.Area),ct);
        if(!c.WaitForStable)return image;
        // Compare small, downsampled grayscale images rather than PNG bytes.
        byte[] previous=Signature(image);int stable=0;
        for(int i=0;i<32;i++)
        {
            await Task.Delay(250,ct);Native.Validate(c,false);
            image=await Task.Run(()=>Screen(c.Area),ct);
            var current=Signature(image);
            double delta=current.Zip(previous,(a,b)=>Math.Abs(a-b)).Average();
            stable=delta<.7?stable+1:0;
            if(stable>=3)return image;
            previous=current;
        }
        throw new InvalidOperationException("La página sigue cambiando. Aumenta la espera o desactiva «Esperar imagen estable» si tiene animaciones.");
    }
    private static byte[] Signature(byte[] png)
    {
        using var ms=new MemoryStream(png);using var source=Image.FromStream(ms);using var small=new Bitmap(64,64);
        using(var g=Graphics.FromImage(small))g.DrawImage(source,0,0,64,64);
        var data=new byte[4096];for(int y=0;y<64;y++)for(int x=0;x<64;x++){var p=small.GetPixel(x,y);data[y*64+x]=(byte)((p.R*30+p.G*59+p.B*11)/100);}return data;
    }
    public static async Task<List<byte[]>> ImportAsync(string path,CancellationToken ct)
    {
        if(Path.GetExtension(path).Equals(".pdf",StringComparison.OrdinalIgnoreCase))
        {
            var pdf=await PdfDocument.LoadFromFileAsync(await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path))).AsTask(ct);
            var pages=new List<byte[]>();
            for(uint i=0;i<pdf.PageCount;i++)
            {
                ct.ThrowIfCancellationRequested();using var page=pdf.GetPage(i);using var output=new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(output,new PdfPageRenderOptions{DestinationWidth=(uint)Math.Min(4000,page.Size.Width*2),BitmapEncoderId=Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId}).AsTask(ct);
                output.Seek(0);using var reader=new DataReader(output.GetInputStreamAt(0));await reader.LoadAsync((uint)output.Size).AsTask(ct);byte[] bytes=new byte[(int)output.Size];reader.ReadBytes(bytes);pages.Add(bytes);
            }return pages;
        }
        return await Task.Run(()=>{using var image=Image.FromFile(path);using var output=new MemoryStream();image.Save(output,ImageFormat.Png);return new List<byte[]>{output.ToArray()};},ct);
    }
}
