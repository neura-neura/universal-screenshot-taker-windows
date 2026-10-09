using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Tesseract;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace UniversalCapture.Core;

public record Recognition(string Text,List<TextLine> Lines,bool KeepImage=false);
public static class OcrService
{
    private static readonly HttpClient http=new(){Timeout=TimeSpan.FromMinutes(15)};
    public static async Task<Recognition> RecognizeAsync(string path,Configuration c,CancellationToken ct)
    {
        if(c.Engine=="ollama")
        {
            if(!Uri.TryCreate(c.Endpoint,UriKind.Absolute,out var endpoint)||endpoint.Scheme is not ("http" or "https"))throw new ArgumentException("Dirección de Ollama no válida.");
            using var response=await http.PostAsJsonAsync(new Uri(endpoint.AbsoluteUri.TrimEnd('/')+"/api/generate"),new {model=c.Model,prompt=c.Prompt,images=new[]{Convert.ToBase64String(await File.ReadAllBytesAsync(path,ct))},stream=false},ct);
            response.EnsureSuccessStatusCode();var json=await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var text=json.GetProperty("response").GetString()??"";
            return new(text.Replace("[[KEEP_PAGE_IMAGE]]","").Trim(),[],text.Contains("[[KEEP_PAGE_IMAGE]]"));
        }
        if(c.Engine=="windows")
        {
            var engine=OcrEngine.TryCreateFromUserProfileLanguages()??throw new InvalidOperationException("Windows OCR no está disponible. Usa el OCR local incluido.");
            using var stream=await (await StorageFile.GetFileFromPathAsync(path)).OpenReadAsync().AsTask(ct);
            var decoder=await BitmapDecoder.CreateAsync(stream).AsTask(ct);
            double scale=Math.Min(1,(double)OcrEngine.MaxImageDimension/Math.Max(decoder.PixelWidth,decoder.PixelHeight));
            var transform=new BitmapTransform{ScaledWidth=(uint)(decoder.PixelWidth*scale),ScaledHeight=(uint)(decoder.PixelHeight*scale)};
            using var bitmap=await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,transform,ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage).AsTask(ct);
            var result=await engine.RecognizeAsync(bitmap).AsTask(ct);
            var lines=result.Lines.Select(line=>
            {
                var words=line.Words;double x=words.Min(w=>w.BoundingRect.X),y=words.Min(w=>w.BoundingRect.Y),r=words.Max(w=>w.BoundingRect.Right),b=words.Max(w=>w.BoundingRect.Bottom);
                return new TextLine(line.Text,x/bitmap.PixelWidth,y/bitmap.PixelHeight,(r-x)/bitmap.PixelWidth,(b-y)/bitmap.PixelHeight){Words=words.Select(word=>new TextWord(word.Text,word.BoundingRect.X/bitmap.PixelWidth,word.BoundingRect.Y/bitmap.PixelHeight,word.BoundingRect.Width/bitmap.PixelWidth,word.BoundingRect.Height/bitmap.PixelHeight)).ToList()};
            }).ToList();return new(string.Join('\n',lines.Select(x=>x.Text)),lines);
        }
        if(c.Engine=="tesseract")
        {
            string output=await RunAsync(string.IsNullOrWhiteSpace(c.ToolPath)?"tesseract":c.ToolPath,[path,"stdout","-l",c.Languages,"tsv"],ct);
            using var image=Image.FromFile(path);var groups=new Dictionary<string,(string Text,Rectangle Bounds,List<TextWord> Words)>();
            foreach(var row in output.Split('\n').Skip(1))
            {
                var v=row.TrimEnd('\r').Split('\t');if(v.Length<12||v[0]!="5"||string.IsNullOrWhiteSpace(v[11]))continue;
                var key=string.Join('-',v.Skip(1).Take(4));var rect=new Rectangle(int.Parse(v[6]),int.Parse(v[7]),int.Parse(v[8]),int.Parse(v[9]));
                var word=new TextWord(v[11],(double)rect.X/image.Width,(double)rect.Y/image.Height,(double)rect.Width/image.Width,(double)rect.Height/image.Height);
                if(groups.TryGetValue(key,out var old)){old.Words.Add(word);groups[key]=(old.Text+" "+v[11],Rectangle.Union(old.Bounds,rect),old.Words);}else groups[key]=(v[11],rect,[word]);
            }
            var lines=groups.Values.Select(x=>new TextLine(x.Text,(double)x.Bounds.X/image.Width,(double)x.Bounds.Y/image.Height,(double)x.Bounds.Width/image.Width,(double)x.Bounds.Height/image.Height){Words=x.Words}).ToList();
            return new(string.Join('\n',lines.Select(x=>x.Text)),lines);
        }
        if(c.Engine=="surya")
        {
            string dir=Path.Combine(Path.GetTempPath(),"UST-OCR-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try
            {
                await RunAsync(string.IsNullOrWhiteSpace(c.ToolPath)?"surya_ocr":c.ToolPath,[path,"--output_dir",dir],ct);
                string file=Directory.GetFiles(dir,"results.json",SearchOption.AllDirectories).FirstOrDefault()??throw new InvalidOperationException("Surya no generó results.json.");
                using var json=JsonDocument.Parse(await File.ReadAllTextAsync(file,ct));var lines=new List<TextLine>();
                foreach(var entry in json.RootElement.EnumerateObject())foreach(var page in entry.Value.EnumerateArray())
                {
                    var bounds=page.GetProperty("image_bbox").EnumerateArray().Select(x=>x.GetDouble()).ToArray();double w=bounds[2]-bounds[0],h=bounds[3]-bounds[1];
                    foreach(var line in page.GetProperty("text_lines").EnumerateArray()){var b=line.GetProperty("bbox").EnumerateArray().Select(x=>x.GetDouble()).ToArray();lines.Add(new(line.GetProperty("text").GetString()??"",b[0]/w,b[1]/h,(b[2]-b[0])/w,(b[3]-b[1])/h));}
                }return new(string.Join('\n',lines.Select(x=>x.Text)),lines);
            }finally{Directory.Delete(dir,true);}
        }
        return await Task.Run(()=>
        {
            ct.ThrowIfCancellationRequested();
            var data=Path.Combine(AppContext.BaseDirectory,"tessdata");
            using var engine=new TesseractEngine(data,c.Languages,EngineMode.LstmOnly);
            using var image=Pix.LoadFromFile(path);using var page=engine.Process(image);using var iterator=page.GetIterator();var lines=new List<TextLine>();
            TextLine? current=null;
            iterator.Begin();do {ct.ThrowIfCancellationRequested();
                if(iterator.IsAtBeginningOf(PageIteratorLevel.TextLine)){
                    string text=iterator.GetText(PageIteratorLevel.TextLine)?.Trim()??"";
                    current=null;if(text.Length>0&&iterator.TryGetBoundingBox(PageIteratorLevel.TextLine,out var b)){current=new(text,(double)b.X1/image.Width,(double)b.Y1/image.Height,(double)b.Width/image.Width,(double)b.Height/image.Height);lines.Add(current);}
                }
                string word=iterator.GetText(PageIteratorLevel.Word)?.Trim()??"";
                if(current is not null&&word.Length>0&&iterator.TryGetBoundingBox(PageIteratorLevel.Word,out var wb))current.Words.Add(new(word,(double)wb.X1/image.Width,(double)wb.Y1/image.Height,(double)wb.Width/image.Width,(double)wb.Height/image.Height));
            }while(iterator.Next(PageIteratorLevel.Word));
            return new Recognition(string.Join('\n',lines.Select(x=>x.Text)),lines);
        },ct);
    }
    public static async Task<string[]> OllamaModelsAsync(string endpoint,CancellationToken ct)
    {
        var data=await http.GetFromJsonAsync<JsonElement>(endpoint.TrimEnd('/')+"/api/tags",ct);
        return data.GetProperty("models").EnumerateArray().Select(x=>x.GetProperty("name").GetString()??"").ToArray();
    }
    public static async Task PullAsync(string endpoint,string model,CancellationToken ct)
    {
        using var response=await http.PostAsJsonAsync(endpoint.TrimEnd('/')+"/api/pull",new{name=model,stream=false},ct);response.EnsureSuccessStatusCode();
        var json=await response.Content.ReadFromJsonAsync<JsonElement>(ct);if(json.TryGetProperty("error",out var error))throw new InvalidOperationException(error.GetString());
    }
    private static async Task<string> RunAsync(string exe,string[] args,CancellationToken ct)
    {
        using var process=new Process{StartInfo=new(exe){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true}};
        foreach(var arg in args)process.StartInfo.ArgumentList.Add(arg);
        process.Start();using var registration=ct.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}});
        var stdout=process.StandardOutput.ReadToEndAsync(ct);var stderr=process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);string output=await stdout,error=await stderr;
        if(process.ExitCode!=0)throw new InvalidOperationException(error);return output;
    }
}
