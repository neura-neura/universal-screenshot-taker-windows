using System.IO.Compression;
using System.Security;
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace UniversalCapture.Core;

public static class ExportService
{
    public static readonly string[] Kinds=["pdf","searchablePDF","textPDF","zip","txt","md","html","epub","docx"];
    public static string Extension(string kind)=>kind is "searchablePDF" or "textPDF"?"pdf":kind;
    public static string FileName(string title)
    {
        var invalid=Path.GetInvalidFileNameChars();
        string name=new(title.Where(c=>!invalid.Contains(c)&&!char.IsControl(c)).ToArray());
        name=name.Trim().TrimEnd('.', ' ');
        if(name.Length>180){name=name[..180];if(char.IsHighSurrogate(name[^1]))name=name[..^1];name=name.TrimEnd('.', ' ');}
        if(name.Length==0)name="Document";
        string stem=name.Split('.')[0].TrimEnd(' ');
        if(System.Text.RegularExpressions.Regex.IsMatch(stem,@"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))name="_"+name;
        return name;
    }
    public static async Task PrepareSearchableAsync(Project p,ProjectStore store,CancellationToken ct,Action<int,int>? progress=null)
    {
        var missing=p.Pages.Where(x=>x.Lines.Count==0||x.Lines.Any(line=>line.Words.Count==0)).ToArray();
        for(int i=0;i<missing.Length;i++)
        {
            ct.ThrowIfCancellationRequested();progress?.Invoke(i+1,missing.Length);
            var result=await OcrService.RecognizeAsync(store.ImagePath(missing[i]),new Configuration{Engine="local",Languages=p.Settings.Languages},ct);
            ct.ThrowIfCancellationRequested();missing[i].Lines=result.Lines;
            if(string.IsNullOrWhiteSpace(missing[i].Text))missing[i].Text=result.Text;
        }
    }
    private static string Xml(string text)=>SecurityElement.Escape(new string(text.Where(c=>!char.IsControl(c)||c is '\n' or '\r' or '\t').ToArray()))??"";
    public static async Task ExportAsync(Project p,ProjectStore store,string kind,string destination,CancellationToken ct)
    {
        if(p.Pages.Count==0)throw new InvalidOperationException("No hay páginas para exportar.");
        if(!Kinds.Contains(kind))throw new ArgumentException("Formato desconocido.");
        if(kind=="searchablePDF")await PrepareSearchableAsync(p,store,ct);
        if(kind is not ("pdf" or "zip" or "searchablePDF") && p.Pages.Any(x=>string.IsNullOrWhiteSpace(x.Text)))throw new InvalidOperationException("Reconoce o escribe el texto de todas las páginas antes de exportar este formato.");
        await Atomic.WriteAsync(destination,temp=>Task.Run(()=>
        {
            ct.ThrowIfCancellationRequested();
            if(kind is "pdf" or "searchablePDF" or "textPDF"){Pdf(p,store,kind,temp,ct);return;}
            if(kind is "txt" or "md"){File.WriteAllText(temp,string.Join(kind=="md"?"\n\n---\n\n":"\n\n",p.Pages.Select(x=>x.Text)),new UTF8Encoding(false));return;}
            if(kind=="html"){File.WriteAllText(temp,"<!doctype html><html lang=\"es\"><meta charset=\"utf-8\"><title>"+Xml(p.Title)+"</title><style>body{max-width:800px;margin:40px auto;font:18px 'Segoe UI',sans-serif;padding:24px}pre{white-space:pre-wrap;font:inherit}img{max-width:100%}section{break-after:page;margin-bottom:3rem}</style><body>"+string.Join("",p.Pages.Select(x=>"<section>"+(p.Settings.PreserveImages||x.KeepImage?"<img alt=\""+Xml(x.Label)+"\" src=\"data:image/png;base64,"+Convert.ToBase64String(File.ReadAllBytes(store.ImagePath(x)))+"\"/>":"")+"<pre>"+Xml(x.Text)+"</pre></section>"))+"</body></html>");return;}
            using var zip=ZipFile.Open(temp,ZipArchiveMode.Create);
            if(kind=="zip"){for(int i=0;i<p.Pages.Count;i++){ct.ThrowIfCancellationRequested();zip.CreateEntryFromFile(store.ImagePath(p.Pages[i]),$"page-{i+1:D4}.png",CompressionLevel.Fastest);}return;}
            if(kind=="epub")Epub(zip,p,store,ct);else Docx(zip,p,store,ct);
        },ct));
    }
    private static void Write(ZipArchive zip,string name,string text,CompressionLevel level=CompressionLevel.Optimal)
    {
        using var stream=zip.CreateEntry(name,level).Open();using var writer=new StreamWriter(stream,new UTF8Encoding(false));writer.Write(text);
    }
    private static void Epub(ZipArchive zip,Project p,ProjectStore store,CancellationToken ct)
    {
        Write(zip,"mimetype","application/epub+zip",CompressionLevel.NoCompression);
        Write(zip,"META-INF/container.xml","<?xml version=\"1.0\"?><container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>");
        var manifest=new StringBuilder("<item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>");var spine=new StringBuilder();var nav=new StringBuilder();
        for(int i=0;i<p.Pages.Count;i++)
        {
            ct.ThrowIfCancellationRequested();int n=i+1;var page=p.Pages[i];string image="";
            if(p.Settings.PreserveImages||page.KeepImage)
            {
                zip.CreateEntryFromFile(store.ImagePath(page),$"OEBPS/image{n}.png");manifest.Append($"<item id=\"img{n}\" href=\"image{n}.png\" media-type=\"image/png\"/>");image=$"<img src=\"image{n}.png\" alt=\"{Xml(page.Label)}\" style=\"max-width:100%\"/>";
            }
            Write(zip,$"OEBPS/page{n}.xhtml",$"<?xml version=\"1.0\" encoding=\"UTF-8\"?><html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"es\"><head><title>{Xml(page.Label)}</title></head><body>{image}{string.Join("",page.Text.Split('\n').Select(x=>"<p>"+Xml(x)+"</p>"))}</body></html>");
            manifest.Append($"<item id=\"p{n}\" href=\"page{n}.xhtml\" media-type=\"application/xhtml+xml\"/>");spine.Append($"<itemref idref=\"p{n}\"/>");nav.Append($"<li><a href=\"page{n}.xhtml\">{Xml(page.Label)}</a></li>");
        }
        Write(zip,"OEBPS/nav.xhtml",$"<?xml version=\"1.0\"?><html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" lang=\"es\"><head><title>Contenido</title></head><body><nav epub:type=\"toc\"><h1>{Xml(p.Title)}</h1><ol>{nav}</ol></nav></body></html>");
        Write(zip,"OEBPS/content.opf",$"<?xml version=\"1.0\"?><package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"book\"><metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:identifier id=\"book\">urn:uuid:{Guid.NewGuid()}</dc:identifier><dc:title>{Xml(p.Title)}</dc:title><dc:language>es</dc:language><meta property=\"dcterms:modified\">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</meta></metadata><manifest>{manifest}</manifest><spine>{spine}</spine></package>");
    }
    private static void Docx(ZipArchive zip,Project p,ProjectStore store,CancellationToken ct)
    {
        Write(zip,"[Content_Types].xml","<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Default Extension=\"png\" ContentType=\"image/png\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
        Write(zip,"_rels/.rels","<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
        var body=new StringBuilder();var rels=new StringBuilder();
        for(int i=0;i<p.Pages.Count;i++)
        {
            ct.ThrowIfCancellationRequested();var page=p.Pages[i];int n=i+1;
            if(i>0)body.Append("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>");
            if(p.Settings.PreserveImages||page.KeepImage)
            {
                zip.CreateEntryFromFile(store.ImagePath(page),$"word/media/image{n}.png");using var image=Image.FromFile(store.ImagePath(page));
                double scale=Math.Min(5.8*914400/image.Width,8.7*914400/image.Height);long cx=(long)(image.Width*scale),cy=(long)(image.Height*scale);
                rels.Append($"<Relationship Id=\"img{n}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image{n}.png\"/>");
                body.Append($"<w:p><w:r><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"{cx}\" cy=\"{cy}\"/><wp:docPr id=\"{n}\" name=\"Página {n}\"/><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic><pic:nvPicPr><pic:cNvPr id=\"{n}\" name=\"image{n}.png\"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed=\"img{n}\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>");
            }
            foreach(var line in page.Text.Split('\n'))body.Append("<w:p><w:r><w:t xml:space=\"preserve\">"+Xml(line)+"</w:t></w:r></w:p>");
        }
        Write(zip,"word/document.xml",$"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><w:body>{body}<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\"/></w:sectPr></w:body></w:document>");
        Write(zip,"word/_rels/document.xml.rels",$"<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">{rels}</Relationships>");
    }
    private static readonly object fontLock=new();
    private static void Pdf(Project p,ProjectStore store,string kind,string destination,CancellationToken ct)
    {
        lock(fontLock){if(GlobalFontSettings.FontResolver is null)GlobalFontSettings.FontResolver=new SystemFontResolver();}
        using var document=new PdfDocument();document.Info.Title=p.Title;
        foreach(var item in p.Pages)
        {
            ct.ThrowIfCancellationRequested();using var image=XImage.FromFile(store.ImagePath(item));
            if(kind!="textPDF")
            {
                var page=document.AddPage();page.Width=XUnit.FromPoint(image.PixelWidth*.75);page.Height=XUnit.FromPoint(image.PixelHeight*.75);
                using(var g=XGraphics.FromPdfPage(page))
                {
                    g.DrawImage(image,0,0,page.Width.Point,page.Height.Point);
                    if(kind=="searchablePDF")
                    {
                        // Invisible text remains selectable and searchable in PDF viewers.
                        var font=new XFont("UST",12);
                        foreach(var word in item.Lines.SelectMany(line=>line.Words))
                        {
                            if(string.IsNullOrWhiteSpace(word.Text)||word.Width<=0||word.Height<=0)continue;
                            var measured=g.MeasureString(word.Text,font);
                            var state=g.Save();
                            g.TranslateTransform(word.X*page.Width.Point,word.Y*page.Height.Point);
                            g.ScaleTransform(word.Width*page.Width.Point/measured.Width,word.Height*page.Height.Point/measured.Height);
                            g.DrawString(word.Text+" ",font,new XSolidBrush(XColor.FromArgb(0,0,0,0)),new XRect(0,0,measured.Width+g.MeasureString(" ",font).Width,measured.Height),XStringFormats.TopLeft);
                            g.Restore(state);
                        }
                    }
                }
                continue;
            }
            if(p.Settings.PreserveImages||item.KeepImage)
            {
                var page=document.AddPage();page.Size=PdfSharp.PageSize.A4;using var g=XGraphics.FromPdfPage(page);
                double s=Math.Min((page.Width.Point-96)/image.PixelWidth,(page.Height.Point-96)/image.PixelHeight);
                g.DrawImage(image,(page.Width.Point-image.PixelWidth*s)/2,(page.Height.Point-image.PixelHeight*s)/2,image.PixelWidth*s,image.PixelHeight*s);
            }
            var fontText=new XFont("UST",12);PdfPage? textPage=null;XGraphics? graphics=null;double pos=48;
            try
            {
                foreach(var paragraph in item.Text.Split('\n'))
                {
                    string line="";
                    foreach(string word in paragraph.Split(' '))
                    {
                        if(graphics is null){textPage=document.AddPage();textPage.Size=PdfSharp.PageSize.A4;graphics=XGraphics.FromPdfPage(textPage);pos=48;}
                        // Split extremely long tokens as well as ordinary words.
                        foreach(var piece in Chunks(word,65))
                        {
                            string candidate=line.Length==0?piece:line+" "+piece;
                            if(graphics.MeasureString(candidate,fontText).Width>textPage!.Width.Point-96 && line.Length>0){Draw(line);line=piece;}else line=candidate;
                        }
                    }
                    if(graphics is null){textPage=document.AddPage();textPage.Size=PdfSharp.PageSize.A4;graphics=XGraphics.FromPdfPage(textPage);pos=48;}
                    Draw(line);pos+=6;
                }
                void Draw(string line)
                {
                    if(pos>textPage!.Height.Point-60){graphics!.Dispose();textPage=document.AddPage();textPage.Size=PdfSharp.PageSize.A4;graphics=XGraphics.FromPdfPage(textPage);pos=48;}
                    graphics!.DrawString(line,fontText,XBrushes.Black,new XPoint(48,pos+12));pos+=18;
                }
            }finally{graphics?.Dispose();}
        }
        document.Save(destination);
    }
    private static IEnumerable<string> Chunks(string text,int size){if(text.Length==0){yield return "";yield break;}for(int i=0;i<text.Length;i+=size)yield return text.Substring(i,Math.Min(size,text.Length-i));}
    private sealed class SystemFontResolver : IFontResolver
    {
        public FontResolverInfo ResolveTypeface(string familyName,bool bold,bool italic)=>new("segoe");
        public byte[] GetFont(string faceName)=>File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"segoeui.ttf"));
    }
}
