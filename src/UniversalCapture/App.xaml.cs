using Microsoft.UI.Xaml;
namespace UniversalCapture;
public partial class App : Application
{
    private Window? window;
    private Mutex? mutex;
    public App()
    {
        InitializeComponent();
        UnhandledException+=(_,e)=>Log(e.Exception);
    }
    private static void Log(Exception exception)
    {
        try
        {
            string directory=Environment.GetEnvironmentVariable("UST_DATA_DIR")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"UniversalScreenshotTaker");
            Directory.CreateDirectory(directory);File.AppendAllText(Path.Combine(directory,"errors.log"),DateTimeOffset.Now+"\n"+exception+"\n");
        }catch{}
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        mutex=new Mutex(true,"Local\\NeuraUniversalScreenshotTaker",out bool first);
        if(!first){Exit();return;}
        try{window=new MainWindow();window.Activate();}catch(Exception ex){Log(ex);Exit();}
    }
}
