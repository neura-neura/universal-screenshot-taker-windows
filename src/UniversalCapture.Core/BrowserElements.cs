using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace UniversalCapture.Core;

public record InspectedField(BrowserField Identity,Region Bounds);

// UIA3 talks to the browser's actual accessibility provider (including web frames).
// Coordinates are only transient hit testing/drawing data, never the locator.
public static class BrowserElements
{
    private static AutomationElement[] Fields(UIA3Automation automation,nint window)=>automation.FromHandle(window).FindAllDescendants(
        automation.ConditionFactory.ByControlType(ControlType.Edit).Or(automation.ConditionFactory.ByControlType(ControlType.Spinner)));
    private static bool InDocument(UIA3Automation automation,AutomationElement element)
    {
        if(element.Properties.ProcessId.ValueOrDefault==Environment.ProcessId)return true;
        var walker=automation.TreeWalkerFactory.GetControlViewWalker();
        for(var parent=element;parent is not null;parent=walker.GetParent(parent))
            if(parent.Properties.ControlType.ValueOrDefault==ControlType.Document)return true;
        return false;
    }
    private static Region? Bounds(AutomationElement element)
    {
        var r=element.BoundingRectangle;
        return r.Width<2||r.Height<2||element.IsOffscreen||!element.IsEnabled?null:Region.From(r);
    }
    // Chromium exposes anonymous HTML inputs without an AutomationId property.
    // A missing optional label must not discard a valid RuntimeId/component.
    private static BrowserField Identity(AutomationElement element)=>new(element.Properties.RuntimeId.Value,
        element.Properties.AutomationId.ValueOrDefault??"",element.Properties.Name.ValueOrDefault??"",(int)element.ControlType);
    public static List<InspectedField> Inspect(nint window)
    {
        var result=new List<InspectedField>();if(!Native.IsWindow(window))return result;
        using var automation=new UIA3Automation();
        foreach(var element in Fields(automation,window))
        {
            try{var bounds=Bounds(element);if(bounds is not null&&InDocument(automation,element))result.Add(new(Identity(element),bounds));}
            catch(Exception ex) when(ex is FlaUI.Core.Exceptions.ElementNotAvailableException or FlaUI.Core.Exceptions.PropertyNotSupportedException){}
        }
        return result;
    }
    private static AutomationElement Resolve(UIA3Automation automation,Configuration c)
    {
        var identity=c.FieldElement??throw new InvalidOperationException("Selecciona otra vez el campo con el inspector; el punto guardado no identifica un componente.");
        var candidates=new List<AutomationElement>();
        foreach(var element in Fields(automation,(nint)c.WindowHandle))
        {
            try
            {
                if(!InDocument(automation,element)||Bounds(element) is null)continue;
                if(element.Properties.RuntimeId.Value.SequenceEqual(identity.RuntimeId))return element;
                if((identity.ControlType>=50000||(int)element.ControlType==identity.ControlType)&&
                   (identity.AutomationId.Length>0?element.Properties.AutomationId.ValueOrDefault==identity.AutomationId:
                    identity.Name.Length>0&&element.Properties.Name.ValueOrDefault==identity.Name))candidates.Add(element);
            }catch(Exception ex) when(ex is FlaUI.Core.Exceptions.ElementNotAvailableException or FlaUI.Core.Exceptions.PropertyNotSupportedException){}
        }
        if(candidates.Count==1)return candidates[0];
        throw new InvalidOperationException("El componente seleccionado desapareció o ya no es único. Selecciona de nuevo el campo.");
    }
    public static Region Refresh(Configuration c)
    {
        using var automation=new UIA3Automation();return Bounds(Resolve(automation,c))??throw new InvalidOperationException("El campo ya no está visible.");
    }
    public static bool TrySetValue(Configuration c,string value)
    {
        using var automation=new UIA3Automation();var element=Resolve(automation,c);
        if(!element.Patterns.Value.TryGetPattern(out var pattern)||pattern.IsReadOnly.Value)return false;
        Native.Validate(c);pattern.SetValue(value);
        for(int attempt=0;attempt<20&&pattern.Value.Value!=value;attempt++)Thread.Sleep(50);
        if(pattern.Value.Value!=value)throw new InvalidOperationException("El navegador no aceptó el número de página solicitado.");
        return true;
    }
    private static bool IsWithin(UIA3Automation automation,AutomationElement? node,AutomationElement target)
    {
        var id=target.Properties.RuntimeId.Value;var walker=automation.TreeWalkerFactory.GetRawViewWalker();
        for(int depth=0;node is not null&&depth<32;depth++,node=walker.GetParent(node))
            if(node.Properties.RuntimeId.Value.SequenceEqual(id))return true;
        return false;
    }
    public static void Focus(Configuration c)
    {
        using var automation=new UIA3Automation();var element=Resolve(automation,c);
        // Compatibility mode uses the accessibility provider only; never move
        // the system pointer. The DOM bridge is preferred for web readers.
        try{element.FrameworkAutomationElement.SetFocus();}catch(Exception ex) when(ex is InvalidOperationException or System.Runtime.InteropServices.COMException or FlaUI.Core.Exceptions.FlaUIException){}
        if(!IsWithin(automation,automation.FocusedElement(),element))
            throw new InvalidOperationException("El navegador no permitió enfocar el componente seleccionado. No se enviaron teclas.");
        c.FieldElement=Identity(element);c.Field=Bounds(element);
    }
}
