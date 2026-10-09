using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace UniversalCapture.Core;

public record Region(int X, int Y, int Width, int Height)
{
    [JsonIgnore] public Rectangle Rectangle => new(X, Y, Width, Height);
    public static Region From(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
}
public record TextWord(string Text, double X, double Y, double Width, double Height);
public record TextLine(string Text, double X, double Y, double Width, double Height)
{
    public List<TextWord> Words {get;init;}=[];
}
public record BrowserField(int[] RuntimeId,string AutomationId,string Name,int ControlType);
public class CapturePage : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string File { get; set; } = "";
    public int? Number { get; set; }
    private string text = "";
    public string Text { get => text; set { text = value; Changed(); Changed(nameof(Summary)); } }
    public List<TextLine> Lines { get; set; } = [];
    public bool KeepImage { get; set; }
    [JsonIgnore] public string ImagePath { get; set; } = "";
    [JsonIgnore] public string Label => Number is int n ? Locale.Format("Página {0}",n) : Locale.T("Página importada");
    [JsonIgnore] public string Summary => string.IsNullOrWhiteSpace(Text) ? Locale.T("Sin texto reconocido") : Locale.Format("{0:N0} caracteres",Text.Length);
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
public class Configuration
{
    public string Url { get; set; } = "";
    public Region? Field { get; set; }
    public BrowserField? FieldElement { get; set; }
    public Region? Area { get; set; }
    public long WindowHandle { get; set; }
    public Region? WindowBounds { get; set; }
    public int BrowserProcessId { get; set; }
    public int First { get; set; } = 1;
    public int Last { get; set; } = 10;
    public int Current { get; set; } = 1;
    public double WaitSeconds { get; set; } = 1.5;
    public bool WaitForStable { get; set; } = true;
    public string CaptureMode { get; set; } = "automatic";
    public string FieldMode { get; set; } = "dom";
    public string? BrowserSession { get; set; }
    public string Selector { get; set; } = "";
    public bool Outline { get; set; } = true;
    public bool TwoCorners { get; set; }
    public string Engine { get; set; } = "local";
    public string Languages { get; set; } = "spa+eng";
    public string ToolPath { get; set; } = "";
    public string Endpoint { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "glm-ocr";
    public string Prompt { get; set; } = "Extract only visible text in reading order. Preserve paragraphs, headings and tables as Markdown. Do not invent missing text. If an illustration must be preserved, put [[KEEP_PAGE_IMAGE]] on its own line. Return no explanations.";
    public bool PreserveImages { get; set; }
    public string Language { get; set; } = "system";
    public string OutputFormat { get; set; } = "pdf";
    public string BrowserExecutable {get;set;}="";
}
public class Project
{
    public int Version { get; set; } = 1;
    public string Title { get; set; } = "Capturas";
    public Configuration Settings { get; set; } = new();
    public List<CapturePage> Pages { get; set; } = [];
    public void BeginNextDocument()
    {
        Title="";Settings.Url="";Settings.First=Settings.Last=Settings.Current=0;Settings.BrowserSession=null;Pages.Clear();
    }
}
