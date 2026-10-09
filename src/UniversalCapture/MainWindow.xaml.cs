using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using UniversalCapture.Core;
using Windows.Storage.Pickers;
using WinRT.Interop;
using CaptureEngine=UniversalCapture.Core.Capture;

namespace UniversalCapture;

public sealed partial class MainWindow : Window
{
    private readonly ProjectStore store=new(Environment.GetEnvironmentVariable("UST_DATA_DIR"));
    private Project project=new();
    private readonly ObservableCollection<CapturePage> pages=[];
    private readonly DispatcherTimer autosave=new(){Interval=TimeSpan.FromMilliseconds(650)};
    private readonly Hotkeys keys;
    private CancellationTokenSource? job;
    private CancellationTokenSource? selectionCancel;
    private bool? webOutline;
    private bool initializing=true,editing,closing,selectionBusy;
    private Outline? outline;
    private Outline? fieldOutline;
    private readonly DispatcherTimer selectionTimer=new(){Interval=TimeSpan.FromMilliseconds(600)};
    private bool updatingOutlines;
    private string? lastExportPath;
    private bool refreshingBrowsers;
    private sealed record BrowserChoice(BrowserApplication Browser,BitmapImage? Icon)
    {
        public string Label=>Browser.Name+(Browser.IsDefault?" · "+Locale.T("Predeterminado"):"");
    }
    private Configuration C=>project.Settings;
    private CapturePage? Selected=>PagesList.SelectedItem as CapturePage;
    public MainWindow()
    {
        // WinUI owns this thread's DispatcherQueueSynchronizationContext. WinForms
        // helper HWNDs must not replace it with their own synchronization context.
        System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall=false;
        InitializeComponent();
        Notice.Visibility=Visibility.Collapsed;
        Notice.Closed+=(_,_)=>Notice.Visibility=Visibility.Collapsed;
        ExtendsContentIntoTitleBar=true;SetTitleBar(AppTitleBar);AppWindow.Resize(new(1180,900));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory,"Assets","AppIcon.ico"));
        string? loadError=null;
        try{project=store.Load();}catch(Exception ex){loadError=ex.Message;}
        C.BrowserSession=null;
        PagesList.ItemsSource=pages;LoadControls();Nav.SelectedItem=Nav.MenuItems[0];
        autosave.Tick+=async(_,_)=>{autosave.Stop();try{await Persist();}catch(Exception ex){Error(ex);}};
        keys=new();keys.Capture+=()=>DispatcherQueue.TryEnqueue(async()=>{if(job is null&&!selectionBusy)await Run(SingleAsync);});keys.Stop+=()=>{job?.Cancel();selectionCancel?.Cancel();};
        BrowserBridge.Current.Start();
        AppWindow.Closing+=async(_,e)=>
        {
            if(closing)return;e.Cancel=true;
            if(selectionBusy){selectionCancel?.Cancel();return;}
            if(job is not null){job.Cancel();Message(Locale.T("Deteniendo la operación. Cierra la aplicación cuando termine."));return;}
            try{ReadControls();autosave.Stop();selectionTimer.Stop();await Persist();closing=true;
                if(C.FieldMode!="visual"&&BrowserBridge.Current.HasConnection(C.BrowserSession))
                    try{using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(2));await BrowserBridge.Current.CommandAsync(C,"suspend",null,cleanup.Token);}catch(OperationCanceledException){}catch(InvalidOperationException){}
                keys.Dispose();outline?.Dispose();fieldOutline?.Dispose();BrowserBridge.Current.Dispose();Close();}catch(Exception ex){closing=false;Error(ex);}
        };
        Root.Loaded+=(_,_)=>{Root.Language=Locale.Resolve(C.Language)=="zh"?"zh-Hans":Locale.Resolve(C.Language);StatusText.Text=Locale.T("Listo para capturar");Localization.Apply(Root,C.Language);if(loadError is not null)Message(Locale.T("No se pudo recuperar la sesión: ")+loadError+Locale.T(". Las imágenes siguen en la carpeta Current."),InfoBarSeverity.Warning);else if(!keys.Registered)Message(Locale.T("Un atajo global ya está en uso. Puedes capturar y detener desde los botones."),InfoBarSeverity.Warning);};
        initializing=false;Counts();UpdateSelection();
        _=RefreshBrowserList();
        selectionTimer.Tick+=async(_,_)=>await RefreshOutlines();selectionTimer.Start();
    }
    private static string Tag(ComboBox box)=>((box.SelectedItem as ComboBoxItem)?.Tag as string)??"";
    private static void SelectTag(ComboBox box,string value){box.SelectedItem=box.Items.OfType<ComboBoxItem>().FirstOrDefault(x=>Equals(x.Tag,value))??box.Items[0];}
    private void ReadControls()
    {
        if(initializing)return;
        C.Url=UrlBox.Text.Trim();C.First=Number(FirstBox,0);C.Last=Number(LastBox,0);C.Current=Number(CurrentBox,0);
        C.WaitSeconds=double.IsFinite(WaitBox.Value)?Math.Clamp(WaitBox.Value,.2,120):1.5;
        C.CaptureMode=Tag(ModeBox);
        var mode=Tag(FieldModeBox);
        if((mode=="visual")!=(C.FieldMode=="visual")){C.Field=null;C.FieldElement=null;C.Area=null;C.BrowserSession=null;webOutline=null;}
        C.FieldMode=mode;C.Selector=SelectorBox.Text;C.Outline=OutlineBox.IsChecked==true;C.TwoCorners=CornersBox.IsChecked==true;C.WaitForStable=StableBox.IsChecked==true;
        C.Engine=Tag(EngineBox);C.Languages=Tag(OcrLanguagesBox);C.Endpoint=EndpointBox.Text.Trim();C.Model=ModelBox.Text.Trim();C.Prompt=PromptBox.Text;C.ToolPath=ToolBox.Text.Trim();C.PreserveImages=PreserveBox.IsChecked==true;C.Language=Tag(LanguageBox);
        project.Title=TitleBox.Text;C.OutputFormat=Tag(OutputBox);
    }
    private static int Number(NumberBox box,int fallback)=>double.IsFinite(box.Value)?(int)Math.Clamp(Math.Round(box.Value),1,100000):fallback;
    private void LoadControls()
    {
        bool before=initializing;initializing=true;
        UrlBox.Text=C.Url;FirstBox.Value=C.First>0?C.First:double.NaN;LastBox.Value=C.Last>0?C.Last:double.NaN;CurrentBox.Value=C.Current>0?C.Current:double.NaN;WaitBox.Value=C.WaitSeconds;
        SelectTag(ModeBox,C.CaptureMode);SelectTag(FieldModeBox,C.FieldMode);SelectorBox.Text=C.Selector;OutlineBox.IsChecked=C.Outline;CornersBox.IsChecked=C.TwoCorners;StableBox.IsChecked=C.WaitForStable;
        SelectTag(EngineBox,C.Engine);SelectTag(OcrLanguagesBox,C.Languages);EndpointBox.Text=C.Endpoint;ModelBox.Text=C.Model;PromptBox.Text=C.Prompt;ToolBox.Text=C.ToolPath;PreserveBox.IsChecked=C.PreserveImages;TitleBox.Text=project.Title;SelectTag(LanguageBox,C.Language);
        SelectTag(OutputBox,C.OutputFormat);
        pages.Clear();foreach(var page in project.Pages){page.ImagePath=store.ImagePath(page);pages.Add(page);}
        Locale.Language=C.Language;initializing=before;UpdateSelection();Counts();if(!before)_=RefreshBrowserList();
    }
    private Project Snapshot()
    {
        project.Pages=pages.ToList();return JsonSerializer.Deserialize<Project>(JsonSerializer.Serialize(project,ProjectStore.Json),ProjectStore.Json)!;
    }
    private Task Persist()=>store.SaveAsync(Snapshot());
    private void QueueSave(){if(initializing)return;autosave.Stop();autosave.Start();}
    private void SettingChanged(object sender,object e){if(initializing||job is not null)return;ReadControls();QueueSave();}
    private void Counts(){ExportCount.Text=Locale.Format("{0:N0} páginas disponibles",pages.Count);}
    private void UpdateSelection()
    {
        if(SelectionStatus is null)return;
        string field=C.Field is null?Locale.T("pendiente"):C.FieldElement is not null?Locale.Format("{0} · componente seleccionado",string.IsNullOrWhiteSpace(C.FieldElement.Name)?Locale.T("Campo editable"):C.FieldElement.Name):C.FieldMode=="dom"?Locale.T("elemento seleccionado en el navegador"):C.FieldMode=="visual"?Locale.T("selecciona con el inspector"):C.Selector;
        string area=C.Area is null?Locale.T("pendiente"):$"{C.Area.Width} × {C.Area.Height} "+(C.FieldMode=="visual"?"px":Locale.T("px de la página"));
        SelectionStatus.Text=Locale.Format("Campo: {0}   ·   Área: {1}",field,area);
    }
    private void Navigate(NavigationView sender,NavigationViewSelectionChangedEventArgs args)
    {
        if(CaptureView is null)return;
        string tag=(args.SelectedItem as NavigationViewItem)?.Tag as string??"capture";
        var views=new Dictionary<string,UIElement>{{"capture",CaptureView},{"gallery",GalleryView},{"ocr",OcrView},{"export",ExportView},{"projects",ProjectsView},{"help",HelpView}};
        foreach(var pair in views)pair.Value.Visibility=pair.Key==tag?Visibility.Visible:Visibility.Collapsed;
        Counts();DispatcherQueue.TryEnqueue(()=>Localization.Apply(Root,C.Language));
    }
    private void Message(string text,InfoBarSeverity severity=InfoBarSeverity.Informational){Notice.Message=Locale.T(text);Notice.Severity=severity;Notice.Visibility=Visibility.Visible;Notice.IsOpen=true;}
    private void Error(Exception ex){StatusText.Text=Locale.T("La operación no se completó");Message(ex.Message,InfoBarSeverity.Error);}
    private void SetBusy(bool value)
    {
        StopButton.IsEnabled=value;CaptureView.IsEnabled=!value;GalleryView.IsHitTestVisible=!value;PagesList.CanReorderItems=!value;BrowserBox.IsEnabled=!value;BrowseBrowserButton.IsEnabled=!value;RefreshBrowserButton.IsEnabled=!value;InstallBrowserButton.IsEnabled=!value&&(BrowserBox.SelectedItem as BrowserChoice)?.Browser.Supported==true;
        OpenExportFolderButton.IsEnabled=!value;
        BeginNextDocumentButton.IsEnabled=!value;
        OcrView.IsEnabled=!value;ExportView.IsEnabled=!value;ProjectsView.IsEnabled=!value;
        PagesList.IsEnabled=!value;TextEditor.IsEnabled=!value;KeepImageBox.IsEnabled=!value;
    }
    private async Task Run(Func<CancellationToken,Task> action)
    {
        if(job is not null||selectionBusy)return;ReadControls();autosave.Stop();job=new();SetBusy(true);Notice.IsOpen=false;Notice.Visibility=Visibility.Collapsed;Progress.Value=0;
        try{await action(job.Token);await Persist();StatusText.Text=Locale.Format("Listo · {0:N0} páginas guardadas",pages.Count);}
        catch(OperationCanceledException){StatusText.Text=Locale.T("Detenido · las páginas capturadas se conservaron");try{await Persist();}catch(Exception ex){Error(ex);}}
        catch(Exception ex){Error(ex);try{await Persist();}catch(Exception saveError){Message(ex.Message+"\nError al guardar: "+saveError.Message,InfoBarSeverity.Error);}}
        finally{job.Dispose();job=null;SetBusy(false);Counts();CurrentBox.Value=C.Current>0?C.Current:double.NaN;await RefreshOutlines();}
    }
    private void Stop(object sender,RoutedEventArgs e){job?.Cancel();selectionCancel?.Cancel();}
    private void OpenReader(string url){if(BrowserBox.SelectedItem is BrowserChoice choice)BrowserDiscovery.Open(choice.Browser,url);else CaptureEngine.OpenBrowser(url);}
    private void OpenBrowser(object sender,RoutedEventArgs e){try{ReadControls();OpenReader(C.Url);}catch(Exception ex){Error(ex);}}
    private void OpenFixture(object sender,RoutedEventArgs e){try{OpenReader($"http://127.0.0.1:{BrowserBridge.Current.Port}/reader");}catch(Exception ex){Error(ex);}}
    private void OpenIframeFixture(object sender,RoutedEventArgs e){try{OpenReader($"http://127.0.0.1:{BrowserBridge.Current.Port}/iframe");}catch(Exception ex){Error(ex);}}
    private async Task Pick(bool field)
    {
        if(job is not null||selectionBusy)return;ReadControls();selectionBusy=true;outline?.Hide();fieldOutline?.Hide();
        try
        {
            StatusText.Text=field?Locale.T("Elige el campo en el navegador"):Locale.T("Encuadra la página del navegador");
            if(C.FieldMode!="visual")
            {
                if(!BrowserBridge.Current.HasConnection())throw new InvalidOperationException(Locale.T("Abre el complemento en la pestaña del lector. Se conectará automáticamente; puedes minimizar su panel."));
                selectionCancel=new();StopButton.IsEnabled=true;
                Native.ShowWindow(WindowNative.GetWindowHandle(this),6);
                var selected=await BrowserBridge.Current.CommandAsync(C,field?"pick-field":"pick-area",null,selectionCancel.Token);
                if(selected.TryGetProperty("cancelled",out var canceled)&&canceled.GetBoolean())return;
                var session=selected.GetProperty("session").GetString();
                if(C.BrowserSession is not null&&C.BrowserSession!=session){C.Field=null;C.Area=null;}
                C.BrowserSession=session;C.FieldElement=null;webOutline=null;
                if(selected.TryGetProperty("url",out var readerUrl)){C.Url=readerUrl.GetString()??C.Url;UrlBox.Text=C.Url;}
                var r=selected.GetProperty("rect");
                var region=new Region((int)Math.Round(r.GetProperty("x").GetDouble()),(int)Math.Round(r.GetProperty("y").GetDouble()),(int)Math.Round(r.GetProperty("width").GetDouble()),(int)Math.Round(r.GetProperty("height").GetDouble()));
                if(field){C.Field=region;C.FieldMode="dom";SelectTag(FieldModeBox,"dom");C.Selector=selected.GetProperty("selector").GetString()??"";SelectorBox.Text=C.Selector;}
                else C.Area=region;
                UpdateSelection();await Persist();StatusText.Text=Locale.T("Selección guardada en el navegador");return;
            }
            // Let the user bring their existing browser window to the foreground.
            Native.ShowWindow(WindowNative.GetWindowHandle(this),6);await Task.Delay(400);
            InspectedField? inspected=null;Region? result;
            if(field){inspected=await Selection.PickFieldAsync(Native.GetForegroundWindow());result=inspected?.Bounds;}
            else result=await Selection.PickAsync(false,C.TwoCorners,Native.GetForegroundWindow());
            await Task.Delay(120);
            if(result is null)return;
            long oldWindow=C.WindowHandle;var oldBounds=C.WindowBounds;
            CaptureEngine.BindWindow(C,result);
            if(oldWindow!=C.WindowHandle||oldBounds!=C.WindowBounds){C.Field=null;C.FieldElement=null;C.Area=null;}
            if(field){C.Field=result;C.FieldElement=inspected!.Identity;C.FieldMode="visual";SelectTag(FieldModeBox,"visual");}else C.Area=result;
            UpdateSelection();await Persist();StatusText.Text=Locale.T("Selección guardada");
        }catch(OperationCanceledException){StatusText.Text=Locale.T("Selección cancelada");}catch(Exception ex){Error(ex);}finally{selectionCancel?.Dispose();selectionCancel=null;StopButton.IsEnabled=false;selectionBusy=false;Native.ShowWindow(WindowNative.GetWindowHandle(this),9);Activate();await RefreshOutlines();}
    }
    private async void PickField(object sender,RoutedEventArgs e)=>await Pick(true);
    private async void PickArea(object sender,RoutedEventArgs e)=>await Pick(false);
    private void ShowOutline(){if(C.FieldMode=="visual"&&C.Outline&&C.Area is not null){outline??=new(C.Area);outline.UpdateRegion(C.Area);if(!outline.Visible)outline.Show();}}
    private async Task RefreshOutlines()
    {
        UpdateBridgeStatus();
        if(updatingOutlines||selectionBusy||closing||job is not null)return;updatingOutlines=true;
        try
        {
            if(C.FieldMode!="visual")
            {
                outline?.Hide();fieldOutline?.Hide();
                if(C.FieldMode=="dom"&&C.BrowserSession is null&&C.Field is not null&&BrowserBridge.Current.HasConnection())
                {
                    var restored=await BrowserBridge.Current.CommandAsync(C,"restore",null,default);
                    if(restored.TryGetProperty("found",out var found)&&found.GetBoolean())
                    {
                        C.BrowserSession=restored.GetProperty("session").GetString();
                        UpdateSelection();QueueSave();
                    }
                }
                if(C.BrowserSession is not null&&BrowserBridge.Current.HasConnection(C.BrowserSession))
                {
                    var result=await BrowserBridge.Current.CommandAsync(C,"outline",null,default);webOutline=C.Outline;
                    if(job is null&&result.TryGetProperty("rect",out var r)&&r.ValueKind==JsonValueKind.Object)
                    {
                        var area=new Region((int)Math.Round(r.GetProperty("x").GetDouble()),(int)Math.Round(r.GetProperty("y").GetDouble()),(int)Math.Round(r.GetProperty("width").GetDouble()),(int)Math.Round(r.GetProperty("height").GetDouble()));
                        if(C.Area!=area){C.Area=area;UpdateSelection();QueueSave();}
                    }
                }
                return;
            }
            nint browser=(nint)C.WindowHandle;var foreground=Native.GetForegroundWindow();
            if(!C.Outline||!Native.IsWindow(browser)||Native.IsIconic(browser)||
               (foreground!=browser&&foreground!=WindowNative.GetWindowHandle(this))||C.WindowBounds!=Region.From(Native.Bounds(browser)))
            {outline?.Hide();fieldOutline?.Hide();return;}
            ShowOutline();if(C.Area is null)outline?.Hide();
            if(C.FieldElement is not null&&C.FieldMode=="visual")
            {
                var settings=C;var bounds=await Task.Run(()=>BrowserElements.Refresh(settings));
                if(settings!=C||selectionBusy||closing)return;
                C.Field=bounds;fieldOutline??=new(bounds,System.Drawing.Color.LimeGreen);fieldOutline.UpdateRegion(bounds);if(!fieldOutline.Visible)fieldOutline.Show();
            }else fieldOutline?.Hide();
        }
        catch(Exception ex) when(ex is InvalidOperationException or FlaUI.Core.Exceptions.FlaUIException or System.Runtime.InteropServices.COMException){fieldOutline?.Hide();}
        finally{updatingOutlines=false;}
    }
    private async void GoToPage(object sender,RoutedEventArgs e)=>await Run(async ct=>{if(C.Current<1)throw new InvalidOperationException(Locale.T("Introduce el número de la página actual."));await CaptureEngine.GoAsync(C,C.Current,ct);await Task.Delay(TimeSpan.FromSeconds(C.WaitSeconds),ct);StatusText.Text=Locale.T("Página solicitada");});
    private async Task AddCapture(int? number,CancellationToken ct)
    {
        var bytes=await CaptureEngine.ReadyAsync(C,ct);ct.ThrowIfCancellationRequested();var page=await store.AddAsync(bytes,number,ct);page.ImagePath=store.ImagePath(page);pages.Add(page);await Persist();Counts();
    }
    private async void StartCapture(object sender,RoutedEventArgs e)=>await Run(async ct=>
    {
        if(C.CaptureMode!="automatic"){await SingleAsync(ct);return;}
        if(C.First<1||C.Last<1)throw new InvalidOperationException(Locale.T("Introduce la primera y la última página."));
        if(C.Last<C.First||C.Last-C.First>9999)throw new InvalidOperationException("El rango debe estar ordenado y no exceder 10 000 páginas.");
        if(C.Area is null)throw new InvalidOperationException(Locale.T("Selecciona el área de captura."));
        if(C.FieldMode=="visual")await Native.ActivateAsync(C,ct);ShowOutline();int count=C.Last-C.First+1;
        for(int number=C.First;number<=C.Last;number++)
        {
            ct.ThrowIfCancellationRequested();C.Current=number;StatusText.Text=Locale.Format("Capturando página {0} · {1} de {2}",number,number-C.First+1,count);
            await CaptureEngine.GoAsync(C,number,ct);await AddCapture(number,ct);Progress.Value=(number-C.First+1)*100.0/count;
        }
    });
    private async Task SingleAsync(CancellationToken ct)
    {
        if(C.Current<1)throw new InvalidOperationException(Locale.T("Introduce el número de la página actual."));
        if(C.Area is null)throw new InvalidOperationException(Locale.T("Selecciona el área de captura."));
        if(C.FieldMode=="visual")await Native.ActivateAsync(C,ct);ShowOutline();StatusText.Text=Locale.Format("Capturando página {0}",C.Current);
        await AddCapture(C.Current,ct);
        if(C.CaptureMode=="semi"){C.Current++;await CaptureEngine.GoAsync(C,C.Current,ct);await Task.Delay(TimeSpan.FromSeconds(C.WaitSeconds),ct);}
        Progress.Value=100;
    }
    private async void SingleCapture(object sender,RoutedEventArgs e)=>await Run(SingleAsync);
    private void SelectPage(object sender,SelectionChangedEventArgs e)=>UpdatePreview();
    private void UpdatePreview()
    {
        editing=true;
        if(Selected is CapturePage page){PreviewTitle.Text=page.Label;Preview.Source=new BitmapImage(new Uri(store.ImagePath(page)));TextEditor.Text=page.Text;KeepImageBox.IsChecked=page.KeepImage;}
        else{PreviewTitle.Text=Locale.T("Selecciona una página");Preview.Source=null;TextEditor.Text="";KeepImageBox.IsChecked=false;}
        editing=false;
    }
    private void EditText(object sender,TextChangedEventArgs e){if(editing||job is not null||Selected is not CapturePage page)return;if(page.Text!=TextEditor.Text){page.Text=TextEditor.Text;page.Lines=[];QueueSave();}}
    private void KeepImageChanged(object sender,RoutedEventArgs e){if(editing||Selected is not CapturePage page)return;page.KeepImage=KeepImageBox.IsChecked==true;QueueSave();}
    private void DeletePage(object sender,RoutedEventArgs e){if(job is not null||Selected is not CapturePage page)return;pages.Remove(page);Counts();QueueSave();}
    private async void DeleteAllPages(object sender,RoutedEventArgs e)
    {
        if(job is not null||selectionBusy||pages.Count==0)return;
        var dialog=new ContentDialog{XamlRoot=Root.XamlRoot,Title=Locale.T("Eliminar todas las páginas"),Content=Locale.Format("Se eliminarán las {0} páginas de esta galería. Se guardará una copia recuperable del proyecto.",pages.Count),PrimaryButtonText=Locale.T("Eliminar todas"),CloseButtonText=Locale.T("Cancelar"),DefaultButton=ContentDialogButton.Close};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        await Run(async ct=>{await store.RecoverAsync(Snapshot());pages.Clear();PagesList.SelectedItem=null;UpdatePreview();await Persist();});
    }
    private void Move(int direction){if(job is not null)return;int index=PagesList.SelectedIndex;if(index<0||index+direction<0||index+direction>=pages.Count)return;pages.Move(index,index+direction);PagesList.SelectedIndex=index+direction;QueueSave();}
    private void MoveUp(object sender,RoutedEventArgs e)=>Move(-1);
    private void MoveDown(object sender,RoutedEventArgs e)=>Move(1);
    private void Reordered(ListViewBase sender,DragItemsCompletedEventArgs e)=>QueueSave();
    private async void Retake(object sender,RoutedEventArgs e)
    {
        var page=Selected;if(page is null)return;
        await Run(async ct=>{if(page.Number is int n)await CaptureEngine.GoAsync(C,n,ct);else if(C.FieldMode=="visual")await Native.ActivateAsync(C,ct);ShowOutline();await store.RetakeAsync(page,await CaptureEngine.ReadyAsync(C,ct),ct);page.ImagePath=store.ImagePath(page);int index=pages.IndexOf(page);pages[index]=page;PagesList.SelectedItem=page;UpdatePreview();});
    }
    private FileOpenPicker Picker(params string[] extensions)
    {
        var picker=new FileOpenPicker();InitializeWithWindow.Initialize(picker,WindowNative.GetWindowHandle(this));foreach(var ext in extensions)picker.FileTypeFilter.Add(ext);return picker;
    }
    private async Task<string?> Destination(string ext,string name)
    {
        var picker=new FileSavePicker{SuggestedFileName=ExportService.FileName(name)};InitializeWithWindow.Initialize(picker,WindowNative.GetWindowHandle(this));picker.FileTypeChoices.Add(ext.ToUpperInvariant(),new List<string>{"."+ext});return (await picker.PickSaveFileAsync())?.Path;
    }
    private async void ImportPages(object sender,RoutedEventArgs e)
    {
        if(job is not null)return;
        try
        {
            var files=await Picker(".png",".jpg",".jpeg",".bmp",".tif",".tiff",".pdf").PickMultipleFilesAsync();if(files.Count==0)return;
            await Run(async ct=>{int count=0;foreach(var file in files){StatusText.Text=Locale.T("Importando ")+file.Name;foreach(var image in await CaptureEngine.ImportAsync(file.Path,ct)){var page=await store.AddAsync(image,null,ct);page.ImagePath=store.ImagePath(page);pages.Add(page);await Persist();}Progress.Value=++count*100.0/files.Count;}if(pages.Count>0)PagesList.SelectedIndex=pages.Count-1;});
        }catch(Exception ex){Error(ex);}
    }
    private async Task Recognize(CapturePage[] targets,CancellationToken ct)
    {
        if(targets.Length==0)throw new InvalidOperationException(Locale.T("Importa o captura páginas primero."));
        for(int i=0;i<targets.Length;i++)
        {
            var page=targets[i];StatusText.Text=Locale.Format("Reconociendo {0} de {1}",i+1,targets.Length);
            var result=await OcrService.RecognizeAsync(store.ImagePath(page),C,ct);ct.ThrowIfCancellationRequested();page.Text=result.Text;page.Lines=result.Lines;page.KeepImage=result.KeepImage;
            await Persist();Progress.Value=(i+1)*100.0/targets.Length;
        }UpdatePreview();
    }
    private async void RecognizeAll(object sender,RoutedEventArgs e)=>await Run(ct=>Recognize(pages.ToArray(),ct));
    private async void RecognizeSelected(object sender,RoutedEventArgs e){if(Selected is null){Message(Locale.T("Selecciona una página en la galería."));return;}await Run(ct=>Recognize([Selected!],ct));}
    private async void ListModels(object sender,RoutedEventArgs e)=>await Run(async ct=>Message(string.Join("\n",await OcrService.OllamaModelsAsync(C.Endpoint,ct))));
    private async void PullModel(object sender,RoutedEventArgs e)=>await Run(async ct=>{StatusText.Text=Locale.T("Descargando modelo de Ollama…");Progress.IsIndeterminate=true;try{await OcrService.PullAsync(C.Endpoint,C.Model,ct);Message(Locale.T("Modelo descargado."));}finally{Progress.IsIndeterminate=false;}});
    private async void PickTool(object sender,RoutedEventArgs e){try{var file=await Picker(".exe").PickSingleFileAsync();if(file is not null)ToolBox.Text=file.Path;}catch(Exception ex){Error(ex);}}
    private async void ExportDocument(object sender,RoutedEventArgs e)
    {
        if(job is not null)return;ReadControls();string kind=Tag(OutputBox);
        try{string? path=await Destination(ExportService.Extension(kind),project.Title);if(path is null)return;lastExportPath=null;OpenExportFolderButton.Visibility=Visibility.Collapsed;await Run(async ct=>{if(kind=="searchablePDF"){project.Pages=pages.ToList();await ExportService.PrepareSearchableAsync(project,store,ct,(i,n)=>{StatusText.Text=Locale.Format("Preparando PDF buscable {0} de {1}",i,n);Progress.Value=i*90.0/n;});await Persist();}StatusText.Text=Locale.T("Exportando documento…");await ExportService.ExportAsync(Snapshot(),store,kind,path,ct);lastExportPath=path;OpenExportFolderButton.Visibility=Visibility.Visible;Progress.Value=100;Message(Locale.T("Documento exportado: ")+path);});}catch(Exception ex){Error(ex);}
    }
    private async void SaveProject(object sender,RoutedEventArgs e)
    {
        if(job is not null)return;ReadControls();try{string? path=await Destination("ustproject",project.Title);if(path is null)return;await Run(ct=>store.ExportProjectAsync(Snapshot(),path,ct));Message(Locale.T("Proyecto guardado: ")+path);}catch(Exception ex){Error(ex);}
    }
    private async void OpenProject(object sender,RoutedEventArgs e)
    {
        if(job is not null)return;
        try{var file=await Picker(".ustproject").PickSingleFileAsync();if(file is null)return;await Run(async ct=>{project=await store.OpenAsync(file.Path,Snapshot(),ct);LoadControls();Message(Locale.T("Proyecto abierto. Selecciona campo y área otra vez."));});}catch(Exception ex){Error(ex);}
    }
    private async void NewProject(object sender,RoutedEventArgs e)=>await Run(async ct=>{await store.RecoverAsync(Snapshot());project=new();LoadControls();await Persist();});
    private async void BeginNextDocument(object sender,RoutedEventArgs e)
    {
        if(job is not null||selectionBusy)return;
        bool completed=false;
        await Run(async ct=>{await store.RecoverAsync(Snapshot());ct.ThrowIfCancellationRequested();project.BeginNextDocument();pages.Clear();
            bool before=initializing;initializing=true;try{TitleBox.Text="";UrlBox.Text="";FirstBox.Value=LastBox.Value=CurrentBox.Value=double.NaN;}finally{initializing=before;}
            PagesList.SelectedItem=null;UpdatePreview();Progress.Value=0;await Persist();completed=true;});
        if(completed){webOutline=null;Notice.Message="";Notice.IsOpen=false;Notice.Visibility=Visibility.Collapsed;Nav.SelectedItem=Nav.MenuItems[0];StatusText.Text=Locale.T("Listo para otro documento");}
    }
    private static void OpenPath(string path)=>Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
    private void OpenExportFolder(object sender,RoutedEventArgs e)
    {
        if(lastExportPath is null)return;
        try{OpenPath(Path.GetDirectoryName(lastExportPath)!);}catch(Exception ex){Error(ex);}
    }
    private void OpenRecovery(object sender,RoutedEventArgs e)=>OpenPath(store.RecoveryDirectory);
    private string ExtensionDirectory=>Path.Combine(AppContext.BaseDirectory,"BrowserExtension",BrowserBox.SelectedItem is BrowserChoice choice&&choice.Browser.Family=="firefox"?"firefox":"");
    private void OpenExtension(object sender,RoutedEventArgs e){try{OpenPath(ExtensionDirectory);}catch(Exception ex){Error(ex);}}
    private BitmapImage? BrowserIcon(string path)
    {
        try{string dir=Path.Combine(store.Root,"BrowserIcons");Directory.CreateDirectory(dir);string file=Path.Combine(dir,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path)))+".png");
            if(!File.Exists(file)){using var icon=System.Drawing.Icon.ExtractAssociatedIcon(path);using var bitmap=icon?.ToBitmap();bitmap?.Save(file,System.Drawing.Imaging.ImageFormat.Png);}return File.Exists(file)?new BitmapImage(new Uri(file)):null;
        }catch{return null;}
    }
    private async Task RefreshBrowserList(string? portable=null)
    {
        if(refreshingBrowsers)return;refreshingBrowsers=true;
        try{string selected=portable??C.BrowserExecutable;var browsers=await Task.Run(()=>BrowserDiscovery.Detect(selected));
            BrowserBox.ItemsSource=browsers.Select(browser=>new BrowserChoice(browser,BrowserIcon(browser.Executable))).ToArray();
            var chosen=BrowserDiscovery.SelectBrowser(browsers,selected);BrowserBox.SelectedItem=BrowserBox.Items.OfType<BrowserChoice>().FirstOrDefault(x=>x.Browser==chosen);
            if(chosen?.IsDefault==true&&!string.IsNullOrEmpty(C.BrowserExecutable)){C.BrowserExecutable="";QueueSave();}
            var defaultBrowser=browsers.FirstOrDefault(x=>x.IsDefault);DefaultBrowserText.Text=Locale.Format("Predeterminado de Windows: {0}",defaultBrowser?.Name??Locale.T("No identificado"));UpdateBrowserInstructions();WriteInstallationGuide();
        }catch(Exception ex){Error(ex);}finally{refreshingBrowsers=false;}
    }
    private async void RefreshBrowsers(object sender,RoutedEventArgs e)=>await RefreshBrowserList();
    private async void BrowseBrowser(object sender,RoutedEventArgs e)
    {
        try{var file=await Picker(".exe").PickSingleFileAsync();if(file is null)return;C.BrowserExecutable=file.Path;await RefreshBrowserList(file.Path);QueueSave();}catch(Exception ex){Error(ex);}
    }
    private void BrowserSelected(object sender,SelectionChangedEventArgs e)
    {
        UpdateBrowserInstructions();if(initializing||refreshingBrowsers)return;if(BrowserBox.SelectedItem is BrowserChoice choice){C.BrowserExecutable=choice.Browser.IsDefault?"":choice.Browser.Executable;WriteInstallationGuide();QueueSave();}
    }
    private void UpdateBrowserInstructions()
    {
        var browser=(BrowserBox.SelectedItem as BrowserChoice)?.Browser;InstallBrowserButton.IsEnabled=browser?.Supported==true;
        BrowserPathText.Text=browser?.Executable??"";
        BrowserInstructions.Text=Locale.T(browser is null?"No se encontraron navegadores. Usa Buscar otro navegador para seleccionar su archivo .exe.":!browser.Supported?"Este navegador no se reconoce como Chromium o Firefox. Selecciona el ejecutable principal de un navegador compatible.":browser.Family=="firefox"?"Instalación temporal en Firefox: pulsa Instalar extensión, elige Cargar complemento temporal y selecciona manifest.json en la carpeta indicada. Firefox lo elimina al reiniciar; una instalación permanente requiere un paquete firmado.":"Instalación asistida: pulsa Instalar extensión, activa Modo desarrollador y elige Cargar descomprimida. Pega la ruta de la extensión para seleccionar su carpeta. El navegador requiere que confirmes estos pasos.");
    }
    private void CopyExtensionPath(object sender,RoutedEventArgs e)
    {
        var data=new Windows.ApplicationModel.DataTransfer.DataPackage();data.SetText(ExtensionDirectory);Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);Message(Locale.T("Ruta de la extensión copiada."));
    }
    private void InstallBrowserExtension(object sender,RoutedEventArgs e)
    {
        if(BrowserBox.SelectedItem is not BrowserChoice choice||!choice.Browser.Supported)return;
        try{ReadControls();string? guide=WriteInstallationGuide();if(guide is null)return;BrowserDiscovery.Open(choice.Browser,new Uri(guide).AbsoluteUri+"?v="+DateTime.UtcNow.Ticks);Message(Locale.T("Completa la instalación en el navegador y luego pulsa el icono de la extensión desde tu lector."));}catch(Exception ex){Error(ex);}
    }
    private string? WriteInstallationGuide()
    {
        if(BrowserBox.SelectedItem is not BrowserChoice choice||!choice.Browser.Supported)return null;
        try{string language=Locale.Resolve(C.Language);string directory=Path.Combine(store.Root,"BrowserSetup",language);Directory.CreateDirectory(directory);
            string guide=Path.Combine(directory,"browser-install.html");string html=BrowserDiscovery.InstallationGuide(choice.Browser,ExtensionDirectory,C.Language);
            File.WriteAllText(guide,html);File.WriteAllText(Path.Combine(store.Root,"browser-install.html"),html);return guide;
        }catch(Exception ex){Error(ex);return null;}
    }
    private void OpenGuide(object sender,RoutedEventArgs e)=>OpenPath(Path.Combine(AppContext.BaseDirectory,"README.md"));
    private void CheckBridge(object sender,RoutedEventArgs e)
    {
        bool connected=BrowserBridge.Current.HasConnection();
        string status=connected?Locale.T("Navegador conectado. Puedes seleccionar el campo y el área."):Locale.T("Navegador desconectado. Abre el complemento en la pestaña del lector; se conectará automáticamente.");
        BridgeStatus.Text=status;Message(status,connected?InfoBarSeverity.Success:InfoBarSeverity.Warning);
    }
    private void UpdateBridgeStatus(){BridgeStatus.Text=Locale.T(BrowserBridge.Current.HasConnection()?"Lector conectado. La extensión está lista para seleccionar y capturar.":"Lector no conectado. Carga la extensión y pulsa su icono desde la pestaña del lector.");}
    private async Task LocateWeb(bool area,CancellationToken ct)
    {
        if(C.FieldMode=="visual")throw new InvalidOperationException(Locale.T("Elige CSS, HTML, campo enfocado o eLibro para localizar un elemento web."));
        var result=await BrowserBridge.Current.CommandAsync(C,area?"area":"locate",null,ct);
        C.BrowserSession=result.GetProperty("session").GetString();
        var r=result.GetProperty("rect");var region=new Region((int)Math.Round(r.GetProperty("x").GetDouble()),(int)Math.Round(r.GetProperty("y").GetDouble()),(int)Math.Round(r.GetProperty("width").GetDouble()),(int)Math.Round(r.GetProperty("height").GetDouble()));
        if(area)C.Area=region;else{C.Field=region;C.FieldElement=null;}UpdateSelection();await RefreshOutlines();
    }
    private async void LocateWebField(object sender,RoutedEventArgs e)=>await Run(ct=>LocateWeb(false,ct));
    private async void LocateWebArea(object sender,RoutedEventArgs e)=>await Run(ct=>LocateWeb(true,ct));
    private void ChangeLanguage(object sender,SelectionChangedEventArgs e){if(initializing)return;ReadControls();Localization.Apply(Root,C.Language);Root.Language=Locale.Resolve(C.Language)=="zh"?"zh-Hans":Locale.Resolve(C.Language);foreach(var page in pages){page.Changed(nameof(CapturePage.Label));page.Changed(nameof(CapturePage.Summary));}Notice.Message=Locale.T(Locale.Source(Notice.Message));StatusText.Text=Locale.Format("Listo · {0:N0} páginas guardadas",pages.Count);UpdatePreview();UpdateSelection();Counts();UpdateBridgeStatus();_=RefreshBrowserList();QueueSave();}
}
