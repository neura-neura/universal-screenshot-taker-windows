using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using UniversalCapture.Core;

namespace UniversalCapture;

internal static class Localization
{
    private sealed class Original {public Dictionary<string,string> Values {get;}=[];}
    private static readonly ConditionalWeakTable<DependencyObject,Original> originals=new();
    public static void Apply(DependencyObject node,string language)
    {
        Locale.Language=language;
        ApplyNode(node,language,[]);
    }
    private static void ApplyNode(DependencyObject node,string language,HashSet<DependencyObject> visited)
    {
        if(!visited.Add(node))return;
        // InfoBar owns bindings for its message. Translating its generated text
        // children replaces those bindings and freezes an earlier notification.
        if(node is InfoBar)return;
        string Translate(string property,string value)
        {
            string source=Locale.Source(value);
            if(!Locale.Contains(source)&&!originals.GetOrCreateValue(node).Values.ContainsKey(property))return value;
            var saved=originals.GetOrCreateValue(node).Values;if(!saved.ContainsKey(property))saved[property]=source;
            return Locale.T(saved[property],language);
        }
        if(node is TextBlock tb&&tb.DataContext is not CapturePage&&tb.Name is not ("StatusText" or "ExportCount" or "SelectionStatus" or "PreviewTitle" or "DefaultBrowserText" or "BrowserInstructions" or "BrowserPathText" or "BridgeStatus"))tb.Text=Translate("Text",tb.Text);
        if(node is ContentControl cc && cc.Content is string content)cc.Content=Translate("Content",content);
        if(node is AppBarButton ab)ab.Label=Translate("Label",ab.Label);
        if(node is TextBox box && box.Header is string bh)box.Header=Translate("Header",bh);
        if(node is NumberBox number && number.Header is string nh)number.Header=Translate("Header",nh);
        if(node is ComboBox combo){if(combo.Header is string ch)combo.Header=Translate("Header",ch);foreach(var item in combo.Items.OfType<DependencyObject>())ApplyNode(item,language,visited);}
        if(node is Expander exp && exp.Header is string eh)exp.Header=Translate("Header",eh);
        if(node is NavigationView nav){foreach(var item in nav.MenuItems.Concat(nav.FooterMenuItems).OfType<DependencyObject>())ApplyNode(item,language,visited);}
        // Collapsed screens have no realized visual tree yet, but their XAML children exist.
        if(node is Panel panel)foreach(var child in panel.Children)ApplyNode(child,language,visited);
        if(node is Border border&&border.Child is not null)ApplyNode(border.Child,language,visited);
        if(node is ContentControl control&&control.Content is DependencyObject contentNode)ApplyNode(contentNode,language,visited);
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)ApplyNode(VisualTreeHelper.GetChild(node,i),language,visited);
    }
}

