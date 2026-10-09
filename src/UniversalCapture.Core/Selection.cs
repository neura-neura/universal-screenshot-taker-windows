namespace UniversalCapture.Core;

// A temporary Win32 overlay; the application itself uses WinUI 3.
public sealed class Selection : Form
{
    private readonly bool pointOnly, twoCorners;
    private Point? anchor;
    private Point cursor;
    private readonly nint browser;
    private List<InspectedField> fields=[];
    private InspectedField? hovered;
    private readonly System.Windows.Forms.Timer inspector=new(){Interval=600};
    private bool inspecting;
    public BrowserField? Element {get;private set;}
    public Region? Result { get; private set; }
    private Selection(bool pointOnly, bool twoCorners,nint browser=0)
    {
        this.pointOnly=pointOnly; this.twoCorners=twoCorners;
        this.browser=browser;
        Text=pointOnly?Locale.T("Universal Capture · seleccionar campo"):Locale.T("Universal Capture · seleccionar área");
        FormBorderStyle=FormBorderStyle.None; StartPosition=FormStartPosition.Manual;
        AutoScaleMode=AutoScaleMode.None;Bounds=Native.IsWindow(browser)?Native.Bounds(browser):Screen.FromPoint(System.Windows.Forms.Cursor.Position).Bounds;TopMost=true;ShowInTaskbar=false;
        BackColor=Color.Black; Opacity=.35; Cursor=Cursors.Cross; DoubleBuffered=true; KeyPreview=true;
        KeyDown += (_,e) => { if(e.KeyCode==Keys.Escape) Close(); };
        MouseDown += (_,e) => { if(e.Button!=MouseButtons.Left)return; Native.GetCursorPos(out cursor);if(pointOnly){UpdateHover();if(hovered is not null){Element=hovered.Identity;Complete(hovered.Bounds.Rectangle);}return;} if(anchor is null)anchor=cursor; else if(twoCorners)Complete(Rect()); };
        MouseMove += (_,e) => { Native.GetCursorPos(out cursor); Invalidate(); };
        MouseUp += (_,e) => { if(!pointOnly && !twoCorners && anchor is not null)Complete(Rect()); };
        if(pointOnly)
        {
            inspector.Tick+=async(_,_)=>
            {
                if(inspecting)return;inspecting=true;
                try{var snapshot=await Task.Run(()=>BrowserElements.Inspect(browser));if(IsDisposed)return;if(snapshot.Count>0)fields=snapshot;UpdateHover();Invalidate();}
                catch(Exception ex) when(ex is FlaUI.Core.Exceptions.FlaUIException or InvalidOperationException or System.Runtime.InteropServices.COMException){}
                finally{inspecting=false;}
            };
            Shown+=(_,_)=>{inspector.Start();};FormClosed+=(_,_)=>inspector.Stop();
        }
    }
    private void UpdateHover(){Native.GetCursorPos(out cursor);hovered=fields.Where(x=>x.Bounds.Rectangle.Contains(cursor)).OrderBy(x=>(long)x.Bounds.Width*x.Bounds.Height).FirstOrDefault();}
    private Rectangle Rect() => Rectangle.FromLTRB(Math.Min(anchor!.Value.X,cursor.X),Math.Min(anchor.Value.Y,cursor.Y),Math.Max(anchor.Value.X,cursor.X),Math.Max(anchor.Value.Y,cursor.Y));
    private void Complete(Rectangle r) { if(r.Width<1||r.Height<1)return; Result=UniversalCapture.Core.Region.From(r); Close(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var font=new Font("Segoe UI",18);
        var monitor=Native.IsWindow(browser)?Native.Bounds(browser):Bounds;
        var help=new Point(monitor.Left-Left+24,monitor.Top-Top+24);
        string instruction=pointOnly?fields.Count==0?Locale.T("Buscando campos accesibles · Esc para cancelar"):Locale.T("Inspector: pulsa un campo resaltado · Esc para cancelar"):twoCorners?Locale.T("Haz clic en las dos esquinas · Esc para cancelar"):Locale.T("Arrastra alrededor de la página · Esc para cancelar");
        e.Graphics.FillRectangle(Brushes.Black,help.X-8,help.Y-8,760,52);
        e.Graphics.DrawString(instruction,font,Brushes.White,help);
        if(pointOnly){UpdateHover();if(hovered is not null){var r=hovered.Bounds.Rectangle;r.Offset(-Left,-Top);using var pen=new Pen(Color.LimeGreen,3){DashStyle=System.Drawing.Drawing2D.DashStyle.Dash};using var fill=new SolidBrush(Color.FromArgb(70,Color.LimeGreen));e.Graphics.FillRectangle(fill,r);e.Graphics.DrawRectangle(pen,r);e.Graphics.DrawString(string.IsNullOrWhiteSpace(hovered.Identity.Name)?Locale.T("Campo editable"):hovered.Identity.Name,font,Brushes.White,r.Left,r.Bottom+8);}}
        if(anchor is not null) {using var pen=new Pen(Color.DeepSkyBlue,3){DashStyle=System.Drawing.Drawing2D.DashStyle.Dash};using var fill=new SolidBrush(Color.FromArgb(70,Color.DeepSkyBlue));var r=Rect();r.Offset(-Left,-Top);e.Graphics.FillRectangle(fill,r);e.Graphics.DrawRectangle(pen,r);}
    }
    public static Task<Region?> PickAsync(bool pointOnly, bool twoCorners = false,nint browser=0)
    {
        var tcs=new TaskCompletionSource<Region?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{try {using var form=new Selection(pointOnly,twoCorners,browser);System.Windows.Forms.Application.Run(form); tcs.SetResult(form.Result);}catch(Exception ex){tcs.SetException(ex);}});
        thread.SetApartmentState(ApartmentState.STA);thread.IsBackground=true;thread.Start();return tcs.Task;
    }
    public static Task<InspectedField?> PickFieldAsync(nint browser)
    {
        var tcs=new TaskCompletionSource<InspectedField?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{try{using var form=new Selection(true,false,browser);System.Windows.Forms.Application.Run(form);tcs.SetResult(form.Element is not null&&form.Result is not null?new(form.Element,form.Result):null);}catch(Exception ex){tcs.SetException(ex);}});
        thread.SetApartmentState(ApartmentState.STA);thread.IsBackground=true;thread.Start();return tcs.Task;
    }
}
public sealed class Outline : Form
{
    private readonly Color color;
    private bool affinityConfigured;
    public Outline(Region region,Color? color=null)
    {
        FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;Bounds=Rectangle.Inflate(region.Rectangle,3,3);
        this.color=color??Color.DeepSkyBlue;
        TopMost=true;ShowInTaskbar=false;Enabled=false;BackColor=this.color;Opacity=.22;
    }
    protected override bool ShowWithoutActivation=>true;
    // Form.Show can reactivate an owned WinForms popup despite NOACTIVATE.
    // Show the HWND directly with SW_SHOWNA so the browser keeps keyboard focus.
    public new bool Visible=>IsHandleCreated&&Native.IsWindowVisible(Handle);
    public new void Show()
    {
        var foreground=Native.GetForegroundWindow();
        if(!affinityConfigured){Native.SetWindowDisplayAffinity(Handle,0x11);affinityConfigured=true;}Native.ShowWindow(Handle,8);Invalidate();
        // Defensive restoration for providers/message loops that activate popups.
        if(Native.GetForegroundWindow()==Handle&&Native.IsWindow(foreground))Native.SetForegroundWindow(foreground);
    }
    public new void Hide(){if(IsHandleCreated)Native.ShowWindow(Handle,0);}
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x84){m.Result=-1;return;}
        if(m.Msg==0x21){m.Result=3;return;} // MA_NOACTIVATE
        if(m.Msg==0x18){DefWndProc(ref m);return;} // bypass Form's focus restoration on show
        base.WndProc(ref m);
    }
    protected override CreateParams CreateParams { get {var p=base.CreateParams;p.ExStyle|=0x08000020;return p;} }
    protected override void OnShown(EventArgs e) { base.OnShown(e); Native.SetWindowDisplayAffinity(Handle,0x11); }
    public void UpdateRegion(Region region){var bounds=Rectangle.Inflate(region.Rectangle,3,3);if(Bounds!=bounds){Bounds=bounds;Invalidate();}}
    protected override void OnPaint(PaintEventArgs e){using var pen=new Pen(Color.White,3){DashStyle=System.Drawing.Drawing2D.DashStyle.Dash};e.Graphics.DrawRectangle(pen,1,1,Width-3,Height-3);}
}
public sealed class Hotkeys : NativeWindow, IDisposable
{
    public event Action? Capture;
    public event Action? Stop;
    public bool Registered {get;}
    public Hotkeys()
    {
        CreateHandle(new CreateParams());
        var a=Native.RegisterHotKey(Handle,1,0x4006,0x43);var b=Native.RegisterHotKey(Handle,2,0x4006,0x7B);
        Registered=a&&b;
    }
    protected override void WndProc(ref Message m){if(m.Msg==0x312){if(m.WParam==1)Capture?.Invoke();if(m.WParam==2)Stop?.Invoke();}base.WndProc(ref m);}
    public void Dispose(){Native.UnregisterHotKey(Handle,1);Native.UnregisterHotKey(Handle,2);DestroyHandle();}
}
