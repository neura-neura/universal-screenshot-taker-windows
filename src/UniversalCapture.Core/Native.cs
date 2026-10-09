using System.Runtime.InteropServices;
using System.Text;

namespace UniversalCapture.Core;

public static class Native
{
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(nint hwnd,StringBuilder name,int maxCount);
    [DllImport("user32.dll")] public static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; public readonly Rectangle Rectangle => Rectangle.FromLTRB(Left, Top, Right, Bottom); }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint Type; public INPUTUNION Data; }
    [StructLayout(LayoutKind.Explicit)] private struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT Mouse; [FieldOffset(0)] public KEYBDINPUT Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int X,Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
    private static void Send(params INPUT[] inputs)
    {
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
            throw new InvalidOperationException("Windows bloqueó la entrada. Abre el navegador sin privilegios de administrador.");
    }
    public static void Key(ushort key, bool up = false) => Send(new INPUT { Type=1, Data=new() { Keyboard=new() { Key=key, Flags=up?2u:0 } } });
    public static void ReplaceText(string value)
    {
        // Never leave Ctrl pressed if an input operation fails.
        Key(0x11); try { Key(0x41); Key(0x41,true); } finally { Key(0x11,true); }
        foreach (char c in value) Send(new INPUT { Type=1, Data=new() { Keyboard=new() { Scan=c, Flags=4 } } },new INPUT { Type=1, Data=new() { Keyboard=new() { Scan=c, Flags=6 } } });
        Key(0x0D); Key(0x0D,true);
    }
    public static async Task WaitForModifiersAsync(CancellationToken ct)
    {
        for(int i=0;i<40;i++)
        {
            if(new[]{0x10,0x11,0x12,0x5B,0x5C}.All(key=>(GetAsyncKeyState(key)&0x8000)==0))return;
            await Task.Delay(50,ct);
        }
        throw new InvalidOperationException("Suelta las teclas modificadoras antes de automatizar el lector.");
    }
    public static Rectangle Bounds(nint window)
    {
        if (!GetWindowRect(window,out var r)) throw new InvalidOperationException("La ventana ya no está disponible.");
        return r.Rectangle;
    }
    public static void Validate(Configuration c, bool requireField = true)
    {
        var window=(nint)c.WindowHandle;
        if (!IsWindow(window)) throw new InvalidOperationException("Selecciona de nuevo el campo: la ventana del navegador se cerró.");
        GetWindowThreadProcessId(window, out var pid);
        if (pid != c.BrowserProcessId) throw new InvalidOperationException("La ventana cambió. Selecciona otra vez el campo y el área.");
        if (c.WindowBounds != Region.From(Bounds(window))) throw new InvalidOperationException("El navegador se movió o cambió de tamaño. Selecciona otra vez el campo y el área.");
        if (GetForegroundWindow()!=window) throw new InvalidOperationException("El navegador perdió el foco. Se detuvo la captura para evitar enviar teclas a otra aplicación.");
        if (requireField && c.Field is null) throw new InvalidOperationException("Selecciona el campo de página.");
        if (c.Area is not null && !Bounds(window).Contains(c.Area.Rectangle)) throw new InvalidOperationException("El área debe estar dentro de la ventana seleccionada.");
    }
    public static async Task ActivateAsync(Configuration c, CancellationToken ct)
    {
        nint window=(nint)c.WindowHandle;
        if (!IsWindow(window)) throw new InvalidOperationException("Selecciona primero el campo o el área del navegador.");
        if (IsIconic(window)) ShowWindow(window,9);
        SetForegroundWindow(window);
        for(int i=0;i<20 && GetForegroundWindow()!=window;i++) await Task.Delay(100,ct);
        Validate(c,false);
    }
}
