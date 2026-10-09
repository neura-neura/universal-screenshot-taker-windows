using System.Drawing.Imaging;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using PdfSharp.Pdf.IO;
using UniversalCapture.Core;
using Region=UniversalCapture.Core.Region;

internal static class Program
{
    private static int assertions;
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);assertions++;Console.WriteLine("PASS "+message);}
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        string root=Path.Combine(Path.GetTempPath(),"UST-Tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            if(args.Contains("--list-browsers")){Console.WriteLine(JsonSerializer.Serialize(BrowserDiscovery.Detect(),ProjectStore.Json));return 0;}
            if(args.Length==3&&args[0]=="--export-searchable")
            {
                var existingStore=new ProjectStore(args[1]);var existing=existingStore.Load();
                await ExportService.ExportAsync(existing,existingStore,"searchablePDF",args[2],default);
                await existingStore.SaveAsync(existing);Console.WriteLine("Exported aligned searchable PDF: "+args[2]);return 0;
            }
            if(args.Length==2&&args[0]=="--inspect-browser")
            {
                using var automation=new FlaUI.UIA3.UIA3Automation();
                var window=automation.FromHandle((nint)long.Parse(args[1]));
                var edits=window.FindAllDescendants(automation.ConditionFactory.ByControlType(FlaUI.Core.Definitions.ControlType.Edit));
                Console.WriteLine("Edits: "+edits.Length);
                foreach(var edit in edits)
                {
                    Console.WriteLine($"Edit name={edit.Properties.Name.ValueOrDefault}, id={edit.Properties.AutomationId.ValueOrDefault}, bounds={edit.BoundingRectangle}, offscreen={edit.IsOffscreen}, enabled={edit.IsEnabled}");
                    if(edit.IsOffscreen)continue;
                    var walker=automation.TreeWalkerFactory.GetControlViewWalker();
                    for(var parent=edit;parent is not null;parent=walker.GetParent(parent))Console.WriteLine("  ancestor: "+parent.Properties.ControlType.ValueOrDefault);
                }
                Console.WriteLine("Inspected: "+JsonSerializer.Serialize(BrowserElements.Inspect((nint)long.Parse(args[1]))));return 0;
            }
            if(args.Length==2&&args[0]=="--browser")
            {
                var sample=JsonSerializer.Deserialize<Project>(await File.ReadAllTextAsync(args[1]),ProjectStore.Json)!;
                byte[]? previous=null;
                Native.GetCursorPos(out var originalPointer);
                for(int i=1;i<=3;i++)
                {
                    await Capture.GoAsync(sample.Settings,i,default);
                    var browserImage=await Capture.ReadyAsync(sample.Settings,default);
                    using var automation=new FlaUI.UIA3.UIA3Automation();
                    var field=automation.FromHandle((nint)sample.Settings.WindowHandle).FindAllDescendants(
                        automation.ConditionFactory.ByControlType(FlaUI.Core.Definitions.ControlType.Edit))
                        .Single(x=>x.Properties.RuntimeId.Value.SequenceEqual(sample.Settings.FieldElement!.RuntimeId));
                    Check(field.Patterns.Value.Pattern.Value.Value==i.ToString(),"browser component value confirms page "+i);
                    if(sample.Settings.Url.Contains("elibro.net",StringComparison.OrdinalIgnoreCase))
                    {
                        var labels=automation.FromHandle((nint)sample.Settings.WindowHandle).FindAllDescendants(
                            automation.ConditionFactory.ByControlType(FlaUI.Core.Definitions.ControlType.Text));
                        Check(labels.Any(x=>System.Text.RegularExpressions.Regex.IsMatch(x.Properties.Name.ValueOrDefault??"",$"^{i} / [0-9]+$")),"eLibro page counter confirms navigation to "+i);
                    }
                    Check(previous is null||!browserImage.SequenceEqual(previous),"browser rendered content changes for page "+i);
                    previous=browserImage;
                }
                await Capture.GoAsync(sample.Settings,1,default);
                Native.GetCursorPos(out var finalPointer);Check(originalPointer==finalPointer,"browser automation does not move the system pointer");
                return 0;
            }
            if(args.Contains("--interactive")){await Interactive(root);return 0;}
            Check(BrowserDiscovery.ExecutableFromCommand("\"C:\\Browser Folder\\chrome.exe\" --profile-directory=Default \"%1\"")==@"C:\Browser Folder\chrome.exe","browser discovery parses quoted executable paths without importing profile arguments");
            Check(BrowserDiscovery.ExecutableFromCommand(@"C:\Browser Folder\chrome.exe --single-argument %1")==@"C:\Browser Folder\chrome.exe","browser discovery parses unquoted registered paths with spaces");
            Check(BrowserDiscovery.ExecutableFromCommand(" ") is null,"empty browser registrations are ignored");
            Check(new BrowserApplication("Firefox","firefox.exe","firefox").ExtensionPage=="about:debugging#/runtime/this-firefox","Firefox assisted setup uses temporary add-on installation");
            Check(new BrowserApplication("Edge","msedge.exe","chromium").ExtensionPage=="edge://extensions/","Edge assisted setup supplies its own extension manager address");
            string guide=BrowserDiscovery.InstallationGuide(new BrowserApplication("<script>alert(1)</script>","chrome.exe","chromium"),"C:\\Extension & files");
            Check(guide.Contains("&lt;script&gt;alert(1)&lt;/script&gt;")&&!guide.Contains("<script>alert(1)</script>")&&guide.Contains("C:\\Extension &amp; files"),"installation guide safely escapes browser names and paths");
            Check(BrowserDiscovery.InstallationGuide(new BrowserApplication("Firefox","firefox.exe","firefox"),root).Contains(WebUtility.HtmlEncode(Path.Combine(root,"manifest.json"))),"Firefox installation guide selects manifest.json rather than the Chromium folder");
            var chrome=new BrowserApplication("Chrome","chrome.exe","chromium");var thorium=new BrowserApplication("Thorium","thorium.exe","chromium");
            foreach(var browser in new[]{chrome,new BrowserApplication("Edge","msedge.exe","chromium"),new BrowserApplication("Firefox","firefox.exe","firefox"),new BrowserApplication("Brave","brave.exe","chromium")})
                Check(BrowserDiscovery.SelectBrowser([thorium,browser with{IsDefault=true}])?.Name==browser.Name,"fresh configuration selects the actual Windows default: "+browser.Name);
            Check(BrowserDiscovery.SelectBrowser([thorium,chrome with{IsDefault=true}],"removed-browser.exe")?.Name=="Chrome","a missing browser from an imported project falls back to the local default");
            Check(BrowserDiscovery.SelectBrowser([thorium,chrome with{IsDefault=true}],"thorium.exe")?.Name=="Thorium","an explicit non-default browser remains selected");
            Check(BrowserDiscovery.SelectBrowser([]) is null,"a machine without detected browsers does not assume Thorium");
            string previousLanguage=Locale.Language;Locale.Language="es";
            string englishGuide=BrowserDiscovery.InstallationGuide(chrome,root,"en");
            Check(englishGuide.Contains("<html lang=\"en\"")&&englishGuide.Contains("<title>Install the browser extension</title>")&&englishGuide.Contains(">Copy</button>"),"guide uses the explicit app language even when global locale differs");
            Check(BrowserDiscovery.InstallationGuide(chrome,root,"es").Contains(WebUtility.HtmlEncode("Instalar la extensión del navegador")),"Spanish installation guide is translated");
            Check(BrowserDiscovery.InstallationGuide(chrome,root,"zh").Contains("<html lang=\"zh-Hans\"")&&BrowserDiscovery.InstallationGuide(chrome,root,"zh").Contains(WebUtility.HtmlEncode("复制")),"Chinese installation guide has translated buttons and document language");
            Check(BrowserDiscovery.InstallationGuide(chrome,root,"system","fr-FR").Contains("<title>Install the browser extension</title>"),"guide falls back to English for an unsupported system language");
            Check(BrowserDiscovery.InstallationGuide(chrome,root,"system","es-MX").Contains("<html lang=\"es\""),"guide resolves the supported system language");Locale.Language=previousLanguage;
            var store=new ProjectStore(Path.Combine(root,"data"));
            using var bitmap=new Bitmap(1000,480);using(var g=Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);using var font=new Font("Segoe UI",32);g.DrawString("Universal Capture page 1",font,Brushes.Black,40,50);g.DrawString("Texto de prueba en español",font,Brushes.Black,40,150);g.DrawString("Modern Windows capture",font,Brushes.Black,40,250);
            }
            using var ms=new MemoryStream();bitmap.Save(ms,ImageFormat.Png);var image=ms.ToArray();
            var page=await store.AddAsync(image,1,default);var p=new Project{Title="Prueba & <Windows>",Pages=[page]};
            var result=await OcrService.RecognizeAsync(store.ImagePath(page),p.Settings,default);
            Check(result.Text.Contains("Universal",StringComparison.OrdinalIgnoreCase),"real bundled OCR recognizes image text");
            Check(result.Lines.Count>=3&&result.Lines.All(x=>x.X>=0&&x.Y>=0&&x.Width>0&&x.Height>0&&x.X+x.Width<=1.01&&x.Y+x.Height<=1.01),"OCR returns normalized text coordinates");
            page.Text=result.Text;page.Lines=result.Lines;p.Settings.PreserveImages=true;
            Check(ExportService.FileName("Mi libro: 中文 / ¿tema? *\"<>|\\")=="Mi libro 中文  ¿tema","filename removes forbidden characters and preserves Unicode");
            Check(ExportService.FileName("CON.txt")=="_CON.txt"&&ExportService.FileName("LPT¹")=="_LPT¹","reserved Windows device filenames are safe");
            Check(ExportService.FileName("... ")=="Document"&&ExportService.FileName("Title. ")=="Title","empty and trailing-dot filenames are safe");
            Check(ExportService.FileName(new string('a',179)+"😀").Length==179,"long filename does not split a surrogate pair");
            Check(Locale.Resolve("system","es-MX")=="es"&&Locale.Resolve("system","zh-TW")=="zh"&&Locale.Resolve("system","fr-FR")=="en","system language resolves Spanish and Chinese with English fallback");
            Check(Locale.T("Eliminar todas","en")=="Delete all"&&Locale.T("Eliminar todas","zh")=="全部删除","interface translations are available in English and Chinese");
            p.Settings.OutputFormat="searchablePDF";p.Settings.Language="zh";p.Settings.Engine="ollama";p.Settings.WaitSeconds=0.7;p.Settings.BrowserExecutable=@"C:\Browser Folder\chrome.exe";
            await store.SaveAsync(p);var persisted=store.Load();
            Check(persisted.Settings.OutputFormat=="searchablePDF"&&persisted.Settings.Language=="zh"&&persisted.Settings.Engine=="ollama"&&persisted.Settings.WaitSeconds==0.7,"export format, language and OCR preferences survive reopening");
            Check(persisted.Settings.BrowserExecutable==p.Settings.BrowserExecutable,"the selected browser survives reopening without changing Windows defaults");
            string preservedText="User OCR text must be preserved";page.Text=preservedText;page.Lines=[];
            string positioned=Path.Combine(root,"fallback-searchable.pdf");await ExportService.ExportAsync(p,store,"searchablePDF",positioned,default);
            using(var fallback=PdfReader.Open(positioned,PdfDocumentOpenMode.Import))Check(fallback.PageCount==1&&page.Lines.Count>=3,"searchable PDF automatically obtains real local OCR positions for text-only engines");
            Check(page.Text==preservedText,"positional OCR does not overwrite existing recognized or edited text");
            Check(page.Lines.All(line=>line.Words.Count>0)&&page.Lines.SelectMany(line=>line.Words).All(word=>word.X>=0&&word.Y>=0&&word.Width>0&&word.Height>0),"local OCR supplies individual word positions for aligned PDF selection");
            page.Text=result.Text;
            await store.SaveAsync(p);Check(store.Load().Pages[0].Text==page.Text,"session autosave round trip");
            var nextStore=new ProjectStore(Path.Combine(root,"next-document"));var nextPage=await nextStore.AddAsync(image,12,default);
            var next=new Project{Title="Keep title",Pages=[nextPage],Settings=new(){Url="https://reader.example/book",First=12,Last=30,Current=20,Field=new(10,20,30,40),Area=new(50,60,700,800),FieldElement=new([1,2],"page","Page",50004),Selector="#page",BrowserSession="same-reader",WaitSeconds=2.4,Language="zh"}};
            await nextStore.RecoverAsync(next);next.BeginNextDocument();await nextStore.SaveAsync(next);var reopenedNext=nextStore.Load();
            Check(reopenedNext.Pages.Count==0&&reopenedNext.Settings.Url==""&&reopenedNext.Settings.First==0&&reopenedNext.Settings.Last==0&&reopenedNext.Settings.Current==0,"starting another document persists an empty gallery, URL and all three page numbers");
            Check(reopenedNext.Settings.Field==new Region(10,20,30,40)&&reopenedNext.Settings.Area==new Region(50,60,700,800)&&reopenedNext.Settings.FieldElement!.RuntimeId.SequenceEqual([1,2])&&reopenedNext.Settings.Selector=="#page"&&reopenedNext.Settings.BrowserSession is null,"starting another document preserves field identity, capture region while releasing the old browser binding");
            Check(reopenedNext.Title==""&&reopenedNext.Settings.WaitSeconds==2.4&&reopenedNext.Settings.Language=="zh","starting another document clears the saved title and preserves unrelated preferences");
            using(var recovery=ZipFile.OpenRead(Directory.GetFiles(nextStore.RecoveryDirectory,"*.ustproject").Single()))Check(recovery.Entries.Any(x=>x.FullName==nextPage.File),"the cleared gallery remains available in a recovery archive");
            foreach(string kind in ExportService.Kinds)
            {
                string file=Path.Combine(root,kind+"."+ExportService.Extension(kind));
                await ExportService.ExportAsync(p,store,kind,file,default);Check(new FileInfo(file).Length>20,kind+" export is generated");
                if(kind is "pdf" or "searchablePDF" or "textPDF")
                {
                    using var pdf=PdfReader.Open(file,PdfDocumentOpenMode.Import);Check(pdf.PageCount>=1,kind+" has readable pages");
                    var imported=await Capture.ImportAsync(file,default);Check(imported.Count==pdf.PageCount,kind+" renders through Windows PDF import");
                }
                if(kind=="docx")
                {
                    using var doc=WordprocessingDocument.Open(file,false);var errors=new OpenXmlValidator().Validate(doc).ToArray();
                    Check(errors.Length==0,"DOCX validates against Open XML schema: "+string.Join("; ",errors.Select(x=>x.Description)));
                    Check(doc.MainDocumentPart!.Document.InnerText.Contains("Universal"),"DOCX preserves recognized text");
                }
                if(kind=="epub")
                {
                    using var zip=ZipFile.OpenRead(file);Check(zip.Entries[0].FullName=="mimetype"&&zip.Entries[0].CompressedLength==zip.Entries[0].Length,"EPUB starts with uncompressed mimetype");
                    foreach(var entry in zip.Entries.Where(x=>x.FullName.EndsWith(".xml")||x.FullName.EndsWith(".opf")||x.FullName.EndsWith(".xhtml"))){using var stream=entry.Open();XDocument.Load(stream);}
                    Check(zip.GetEntry("OEBPS/image1.png") is not null,"EPUB XML and preserved image are present");
                }
            }
            page.Text=string.Join('\n',Enumerable.Repeat("A long paragraph that must flow across several printed pages. "+new string('W',240),100));
            string longPdf=Path.Combine(root,"long.pdf");await ExportService.ExportAsync(p,store,"textPDF",longPdf,default);
            using(var pdf=PdfReader.Open(longPdf,PdfDocumentOpenMode.Import))Check(pdf.PageCount>3,"long text PDF paginates without truncation");
            page.Text=result.Text;
            string projectPath=Path.Combine(root,"portable.ustproject");await store.ExportProjectAsync(p,projectPath);
            var restored=await store.OpenAsync(projectPath,p,default);Check(restored.Pages[0].Text==page.Text&&File.Exists(store.ImagePath(restored.Pages[0])),"portable project restores images and text");
            Check(Directory.GetFiles(store.RecoveryDirectory,"*.ustproject").Length==1,"opening project preserves previous recovery");
            restored.Settings.WindowHandle=123;restored.Settings.Field=new(1,1,1,1);await store.ExportProjectAsync(restored,projectPath);
            var fresh=await store.OpenAsync(projectPath,restored,default);Check(fresh.Settings.WindowHandle==0&&fresh.Settings.Field is null,"portable project clears stale screen bindings");
            string malicious=Path.Combine(root,"malicious.ustproject");using(var zip=ZipFile.Open(malicious,ZipArchiveMode.Create)){using var writer=new StreamWriter(zip.CreateEntry("../outside.txt").Open());writer.Write("bad");}
            bool rejected=false;try{await store.OpenAsync(malicious,fresh,default);}catch(InvalidDataException){rejected=true;}Check(rejected&&!File.Exists(Path.Combine(root,"data","outside.txt")),"zip traversal is rejected without touching current session");
            Check(store.Load().Pages.Count==1,"invalid import leaves saved session intact");
            var sentinel=Path.Combine(root,"sentinel.txt");await File.WriteAllTextAsync(sentinel,"original");
            try{await Atomic.WriteAsync(sentinel,async file=>{await File.WriteAllTextAsync(file,"partial");throw new IOException("simulated failure");});}catch(IOException){}
            Check(await File.ReadAllTextAsync(sentinel)=="original","failed export preserves existing destination");
            using var canceled=new CancellationTokenSource();canceled.Cancel();bool wasCanceled=false;try{await ExportService.ExportAsync(p,store,"docx",Path.Combine(root,"cancel.docx"),canceled.Token);}catch(OperationCanceledException){wasCanceled=true;}Check(wasCanceled&&!File.Exists(Path.Combine(root,"cancel.docx")),"canceled export leaves no partial file");
            await BridgeTests();
            // Save artifacts for external rendering and text extraction checks.
            string artifacts=Path.GetFullPath("TestResults/artifacts");Directory.CreateDirectory(artifacts);
            foreach(var file in Directory.GetFiles(root))if(Path.GetExtension(file) is ".pdf" or ".docx" or ".epub" or ".html")File.Copy(file,Path.Combine(artifacts,Path.GetFileName(file)),true);
            Console.WriteLine($"All {assertions} checks passed.");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{Directory.Delete(root,true);}
    }
    private static async Task BridgeTests()
    {
        var bridge=BrowserBridge.Current;bridge.Start();using var client=new HttpClient{BaseAddress=new($"http://127.0.0.1:{bridge.Port}")};
        Check((await client.GetAsync("/command")).StatusCode==HttpStatusCode.Unauthorized,"selector bridge rejects missing authentication");
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",bridge.Token);
        client.DefaultRequestHeaders.Add("Origin","https://evil.example");Check((await client.GetAsync("/command")).StatusCode==HttpStatusCode.Forbidden,"selector bridge rejects website origin");
        client.DefaultRequestHeaders.Remove("Origin");client.DefaultRequestHeaders.Add("Origin","chrome-extension://test");
        using(var discovery=JsonDocument.Parse(await client.GetStringAsync("/connection")))Check(discovery.RootElement.GetProperty("token").GetString()==bridge.Token,"extension discovers the local authenticated connection automatically");
        client.DefaultRequestHeaders.Remove("Origin");client.DefaultRequestHeaders.Add("X-UST-Discovery","1");
        Check((await client.GetAsync("/connection")).IsSuccessStatusCode,"privileged extension fetch can discover without an Origin header");
        client.DefaultRequestHeaders.Add("Origin","https://evil.example");Check((await client.GetAsync("/connection")).StatusCode==HttpStatusCode.Forbidden,"website cannot discover the browser connection key");
        client.DefaultRequestHeaders.Remove("Origin");client.DefaultRequestHeaders.Remove("X-UST-Discovery");client.DefaultRequestHeaders.Add("Origin","chrome-extension://test");
        var task=bridge.CommandAsync(new Configuration{FieldMode="css",Selector="#page"},"go",3,default);
        var command=JsonDocument.Parse(await client.GetStringAsync("/command"));Check(command.RootElement.GetProperty("page").GetInt32()==3,"bridge queues selector page command");
        await client.PostAsync("/result",new StringContent(JsonSerializer.Serialize(new{id=command.RootElement.GetProperty("id").GetString(),ok=true}),System.Text.Encoding.UTF8,"application/json"));
        Check((await task).GetProperty("ok").GetBoolean(),"bridge pairs response to pending command");
        var targeted=bridge.CommandAsync(new Configuration{BrowserSession="reader-a",Area=new Region(100,120,400,280)},"outline",null,default);
        client.DefaultRequestHeaders.Add("X-UST-Session","reader-b");
        using(var wrong=JsonDocument.Parse(await client.GetStringAsync("/command")))Check(!wrong.RootElement.EnumerateObject().Any(),"different browser session cannot consume a bound command");
        client.DefaultRequestHeaders.Remove("X-UST-Session");client.DefaultRequestHeaders.Add("X-UST-Session","reader-a");
        using(var right=JsonDocument.Parse(await client.GetStringAsync("/command")))
        {
            var area=right.RootElement.GetProperty("area");
            Check(area.GetProperty("x").GetInt32()==100&&area.GetProperty("y").GetInt32()==120&&area.GetProperty("width").GetInt32()==400&&area.GetProperty("height").GetInt32()==280,"bridge sends DOM crop coordinates in browser JSON format");
            var id=right.RootElement.GetProperty("id").GetString();
            using(var pending=JsonDocument.Parse(await client.GetStringAsync("/pending?id="+id)))Check(pending.RootElement.GetProperty("pending").GetBoolean(),"DOM picker can check that its command is still authorized");
            await client.PostAsync("/result",new StringContent(JsonSerializer.Serialize(new{id,ok=true}),System.Text.Encoding.UTF8,"application/json"));
        }
        Check((await targeted).GetProperty("ok").GetBoolean(),"bound browser session completes its command");
        var repick=bridge.CommandAsync(new Configuration{BrowserSession="closed-reader"},"pick-field",null,default);
        using(var rebound=JsonDocument.Parse(await client.GetStringAsync("/command")))
        {
            Check(rebound.RootElement.GetProperty("action").GetString()=="pick-field","a reloaded reader can select again after its old session disconnects");
            await client.PostAsync("/result",new StringContent(JsonSerializer.Serialize(new{id=rebound.RootElement.GetProperty("id").GetString(),found=true,session="reader-a"}),System.Text.Encoding.UTF8,"application/json"));
        }
        Check((await repick).GetProperty("session").GetString()=="reader-a","reselection returns the new browser session");
        client.DefaultRequestHeaders.Remove("X-UST-Session");
        using var cancellation=new CancellationTokenSource();
        var stale=bridge.CommandAsync(new Configuration(),"go",99,cancellation.Token);cancellation.Cancel();
        try{await stale;}catch(OperationCanceledException){}
        using(var empty=JsonDocument.Parse(await client.GetStringAsync("/command")))Check(!empty.RootElement.EnumerateObject().Any(),"canceled selector command cannot execute later");
        bridge.Dispose();
    }
    private static Task Interactive(string root)
    {
        var tcs=new TaskCompletionSource();var thread=new Thread(()=>
        {
            using var form=new Form{Text="UST automation fixture",Width=900,Height=700,StartPosition=FormStartPosition.CenterScreen};
            var field=new TextBox{Left=20,Top=20,Width=100,Text="1"};var label=new Label{Left=30,Top=120,Width=800,Height=400,Font=new Font("Segoe UI",36),Text="Page 1"};form.Controls.AddRange([field,label]);
            field.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter)label.Text="Page "+field.Text;};
            form.Shown+=async(_,_)=>
            {
                try
                {
                    Console.WriteLine("Activate the UST automation fixture to begin the interactive test."); await Task.Delay(500); for(int i=0;i<300 && Native.GetForegroundWindow()!=form.Handle;i++)await Task.Delay(100);var c=new Configuration{WindowHandle=(long)form.Handle,WindowBounds=Region.From(Native.Bounds(form.Handle)),BrowserProcessId=Environment.ProcessId,Field=Region.From(new Rectangle(field.PointToScreen(Point.Empty),field.Size)),Area=Region.From(new Rectangle(label.PointToScreen(Point.Empty),label.Size)),WaitSeconds=.2,WaitForStable=true};
                    c.FieldMode="visual";var inspected=await Task.Run(()=>BrowserElements.Inspect(form.Handle)); Check(inspected.Count==1,"inspector identifies actual editable component"); c.FieldElement=inspected[0].Identity; c.Field=new(0,0,1,1); field.Left+=180; var unmarked=Capture.Screen(c.Area!); using var border=new Outline(c.Area!); border.Show(); await Task.Delay(100); Check(Capture.Screen(c.Area!).SequenceEqual(unmarked),"translucent overlay is excluded from captured pixels"); Check(border.Visible&&Native.GetForegroundWindow()==form.Handle,"selection border is visible without taking focus"); for(int page=1;page<=3;page++){await Capture.GoAsync(c,page,default);var image=await Capture.ReadyAsync(c,default);Check(field.Text==page.ToString()&&label.Text=="Page "+page,"native number input and Enter page "+page);Check(image.Length>500,"visible screenshot page "+page);}
                    Check(border.Visible,"selection border remains visible between captures"); using var cancel=new CancellationTokenSource();cancel.Cancel();bool stopped=false;try{await Capture.ReadyAsync(c,cancel.Token);}catch(OperationCanceledException){stopped=true;}Check(stopped,"capture honors cancellation");
                    form.Left+=20;bool moved=false;try{Native.Validate(c);}catch(InvalidOperationException){moved=true;}Check(moved,"moved window stops capture");
                    Console.WriteLine($"Interactive: {assertions} checks passed.");tcs.TrySetResult();
                }catch(Exception ex){tcs.TrySetException(ex);}finally{form.Close();}
            };
            System.Windows.Forms.Application.Run(form);
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();return tcs.Task;
    }
}








