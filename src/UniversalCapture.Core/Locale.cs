using System.Globalization;
using System.Text.Json;

namespace UniversalCapture.Core;

public static class Locale
{
    private static readonly Dictionary<string,string[]> texts=JsonSerializer.Deserialize<Dictionary<string,string[]>>(typeof(Locale).Assembly.GetManifestResourceStream("UniversalCapture.Core.Translations.json")!)!;
    public static string Language {get;set;}="system";
    public static string Resolve(string language,string? systemLanguage=null)
    {
        string value=language=="system"?(systemLanguage??CultureInfo.CurrentUICulture.Name):language;
        return value.StartsWith("es",StringComparison.OrdinalIgnoreCase)?"es":value.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?"zh":"en";
    }
    public static bool Contains(string text)=>texts.ContainsKey(text);
    public static string Source(string text)
    {
        if(string.IsNullOrEmpty(text))return text??"";
        foreach(var pair in texts)
        {
            if(pair.Value.Contains(text))return pair.Key;
            if(pair.Key.EndsWith(": "))foreach(string value in pair.Value)
                if(text.StartsWith(value,StringComparison.Ordinal))return pair.Key+text[value.Length..];
        }
        return text;
    }
    public static string T(string text,string? language=null)
    {
        string resolved=Resolve(language??Language);
        if(resolved=="es")return text;
        if(texts.TryGetValue(text,out var values))return values[resolved=="zh"?1:0];
        // Prefixes contain user paths or service diagnostics which must be preserved.
        foreach(var pair in texts.Where(x=>x.Key.EndsWith(": ")).OrderByDescending(x=>x.Key.Length))
            if(text.StartsWith(pair.Key,StringComparison.Ordinal))return pair.Value[resolved=="zh"?1:0]+text[pair.Key.Length..];
        return text;
    }
    public static string Format(string text,params object[] values)=>string.Format(CultureInfo.CurrentCulture,T(text),values);
}
