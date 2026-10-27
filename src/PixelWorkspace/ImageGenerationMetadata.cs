using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace PixelWorkspace;

/// <summary>Image-generation metadata collected from PNG, JPEG, EXIF, and XMP sources.</summary>
public sealed class ImageGenerationMetadata
{
    public string Parameters { get; init; } = "";
    public string Prompt { get; init; } = "";
    public string PositivePrompt { get; init; } = "";
    public string NegativePrompt { get; init; } = "";
    public string Workflow { get; init; } = "";
    public string ComfyPromptJson { get; init; } = "";
    public string Model { get; init; } = "";
    public string Checkpoint { get; init; } = "";
    public string Unet { get; init; } = "";
    public string Clip { get; init; } = "";
    public string Vae { get; init; } = "";
    public string Lora { get; init; } = "";
    public string Seed { get; init; } = "";
    public string Steps { get; init; } = "";
    public string Cfg { get; init; } = "";
    public string Sampler { get; init; } = "";
    public string Scheduler { get; init; } = "";
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public bool IsEmpty => Fields.Count == 0 && string.IsNullOrWhiteSpace(Parameters) && string.IsNullOrWhiteSpace(Prompt) && string.IsNullOrWhiteSpace(Workflow) && string.IsNullOrWhiteSpace(ComfyPromptJson);

    public static ImageGenerationMetadata Parse(string path) { try { return string.IsNullOrWhiteSpace(path) ? new() : Parse(File.ReadAllBytes(path)); } catch { return new(); } }
    public static ImageGenerationMetadata Parse(string path, int width, int height) => Parse(path);
    public string ToDisplay()
    {
        // Keep this surface intentionally narrow. ComfyUI workflows contain many node fields,
        // but only these normalized generation fields belong in the browser details panel.
        var positive = DisplayValue(!string.IsNullOrWhiteSpace(PositivePrompt) ? PositivePrompt : Prompt, 6000);
        var negative = DisplayValue(NegativePrompt, 6000);
        var model = JoinValues(Model, Checkpoint);
        var lines = new[]
        {
            $"UNet：{DisplayAsset(Unet)}",
            $"CLIP：{DisplayAsset(Clip)}",
            $"VAE：{DisplayAsset(Vae)}",
            $"LoRA：{DisplayAsset(Lora)}",
            $"模型 / Checkpoint：{DisplayAsset(model)}",
            $"正面提示词：{positive}",
            $"负面提示词：{negative}",
            $"种子：{DisplayValue(Seed)}",
            $"步数：{DisplayValue(Steps)}",
            $"CFG：{DisplayValue(Cfg)}",
            $"采样器：{DisplayValue(Sampler)}",
            $"调度器：{DisplayValue(Scheduler)}"
        };
        return string.Join("\n", lines);
    }

    private static string DisplayValue(string? value, int maxLength = 500)
    {
        if (string.IsNullOrWhiteSpace(value)) return "未记录";
        var text = value.Trim().Trim('"', '\'');
        if (text.StartsWith("[", StringComparison.Ordinal) && text.EndsWith("]", StringComparison.Ordinal)) text = text[1..^1];
        text = Regex.Replace(text, "\\s+", " ").Trim();
        return text.Length > maxLength ? text[..maxLength] + "…" : text;
    }

    private static string DisplayAsset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "未记录";
        var text = value.Trim().Trim('"', '\'');
        text = Regex.Replace(text, @"[\\/]", "/");
        text = text[(text.LastIndexOf('/') + 1)..];
        return DisplayValue(text, 300);
    }

    private static string JoinValues(params string[] values)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return string.Join("；", values.Select(DisplayAsset).Where(v => v != "未记录" && seen.Add(v)));
    }
    public static ImageGenerationMetadata Parse(BitmapFrame frame)
    {
        if (frame == null) return new(); var values = NewMap();
        if (frame.Metadata is BitmapMetadata m)
        {
            foreach (var q in new[] { "/Text/Description", "/Text/Comment", "/Text/Title", "/Text/Software", "/app1/ifd/{ushort=270}", "/app1/ifd/exif/{ushort=37510}" }) try { Add(values, q, m.GetQuery(q)?.ToString()); } catch { }
        }
        return Build(values);
    }
    public static ImageGenerationMetadata Parse(ReadOnlySpan<byte> bytes) => Parse(bytes.ToArray());
    public static ImageGenerationMetadata Parse(byte[]? bytes)
    {
        var values = NewMap(); if (bytes == null || bytes.Length == 0) return new();
        try { if (IsPng(bytes)) ParsePng(bytes, values); else if (IsJpeg(bytes)) ParseJpeg(bytes, values); else Add(values, "Text", Decode(bytes, 0, Math.Min(bytes.Length, 1024 * 1024))); } catch { }
        return Build(values);
    }
    private static Dictionary<string, string> NewMap() => new(StringComparer.OrdinalIgnoreCase);
    private static bool IsPng(byte[] b) => b.Length >= 8 && b.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
    private static bool IsJpeg(byte[] b) => b.Length >= 2 && b[0] == 255 && b[1] == 216;
    private static void Add(Dictionary<string, string> d, string key, string? value) { value = Clean(value); if (!string.IsNullOrWhiteSpace(value)) d[key] = value; }
    private static string Clean(string? s) => (s ?? "").Trim('\0', ' ', '\t', '\r', '\n');
    private static void ParsePng(byte[] b, Dictionary<string, string> d)
    {
        int p = 8; while (p + 12 <= b.Length) { uint len = U32BE(b, p); if (len > int.MaxValue || p > b.Length - 12 - (int)len) break; string type = Encoding.ASCII.GetString(b, p + 4, 4); int start = p + 8, end = start + (int)len;
            if (type == "tEXt") { int z = Array.IndexOf(b, (byte)0, start, (int)len); if (z >= start) Add(d, Encoding.Latin1.GetString(b, start, z - start), Encoding.Latin1.GetString(b, z + 1, end - z - 1)); }
            else if (type == "iTXt") ParseITxt(b, start, end, d); p = end + 4; if (type == "IEND") break; }
    }
    private static void ParseITxt(byte[] b, int start, int end, Dictionary<string, string> d)
    {
        int p = start, z = Array.IndexOf(b, (byte)0, p, end - p); if (z < 0) return; string key = Encoding.UTF8.GetString(b, p, z - p); p = z + 1; if (p + 2 > end) return; bool compressed = b[p++] != 0; p++;
        z = Array.IndexOf(b, (byte)0, p, end - p); if (z < 0) return; p = z + 1; z = Array.IndexOf(b, (byte)0, p, end - p); if (z < 0) return; p++; byte[] payload = b[p..end];
        if (compressed) try { using var input = new MemoryStream(payload); using var zip = new ZLibStream(input, CompressionMode.Decompress); using var output = new MemoryStream(); zip.CopyTo(output); payload = output.ToArray(); } catch { return; }
        Add(d, key, Encoding.UTF8.GetString(payload));
    }
    private static void ParseJpeg(byte[] b, Dictionary<string, string> d)
    {
        int p = 2; while (p + 4 <= b.Length && b[p] == 255) { while (p < b.Length && b[p] == 255) p++; if (p >= b.Length || b[p] is 218 or 217) break; byte marker = b[p++]; if (marker is >= 208 and <= 215) continue; if (p + 2 > b.Length) break; int len = (b[p] << 8) | b[p + 1]; if (len < 2 || p > b.Length - len) break; int start = p + 2, count = len - 2; if (marker == 225) ParseApp1(b, start, count, d); else if (marker == 254) Add(d, "Comment", Decode(b, start, count)); p += len; }
    }
    private static void ParseApp1(byte[] b, int start, int count, Dictionary<string, string> d)
    {
        if (count >= 6 && Encoding.ASCII.GetString(b, start, 6) == "Exif\0\0") ParseExif(b, start + 6, count - 6, d);
        else if (count >= 29 && Encoding.ASCII.GetString(b, start, 29) == "http://ns.adobe.com/xap/1.0/\0") Add(d, "XMP", Decode(b, start + 29, count - 29)); else Add(d, "APP1", Decode(b, start, count));
    }
    private static void ParseExif(byte[] b, int start, int count, Dictionary<string, string> d)
    {
        if (count < 8) return; bool little = b[start] == 73 && b[start + 1] == 73; if (!little && !(b[start] == 77 && b[start + 1] == 77)) return; ParseIfd(b, start, count, start + (int)U32(b, start + 4, little), little, d, 0);
    }
    private static void ParseIfd(byte[] b, int baseOffset, int count, int offset, bool little, Dictionary<string, string> d, int depth)
    {
        if (depth > 4 || offset < baseOffset || offset > baseOffset + count - 2) return; ushort n = U16(b, offset, little); int pos = offset + 2;
        for (int i = 0; i < n && pos + 12 <= baseOffset + count; i++, pos += 12) { ushort tag = U16(b, pos, little), type = U16(b, pos + 2, little); uint units = Size(type) * U32(b, pos + 4, little); if (units == 0 || units > int.MaxValue) continue; int data = units <= 4 ? pos + 8 : baseOffset + (int)U32(b, pos + 8, little); if (data < baseOffset || data > baseOffset + count - Math.Min((int)units, count)) continue; string text = type is 2 or 7 ? Decode(b, data, (int)Math.Min(units, (uint)(baseOffset + count - data))) : ""; if (!string.IsNullOrWhiteSpace(text)) Add(d, "EXIF:" + tag, text); if (tag is 0x8769 or 0x8825 && units >= 4) ParseIfd(b, baseOffset, count, baseOffset + (int)U32(b, pos + 8, little), little, d, depth + 1); }
    }
    private static uint Size(ushort t) => t switch { 1 or 2 or 6 or 7 => 1, 3 or 8 => 2, 4 or 9 => 4, 5 or 10 => 8, _ => 0 };
    private static string Decode(byte[] b, int p, int n) { string s; try { s = Encoding.UTF8.GetString(b, p, n); } catch { s = Encoding.Latin1.GetString(b, p, n); } return s.Contains('\ufffd') ? Clean(Encoding.Latin1.GetString(b, p, n)) : Clean(s); }
    private static uint U32BE(byte[] b, int p) => ((uint)b[p] << 24) | ((uint)b[p + 1] << 16) | ((uint)b[p + 2] << 8) | b[p + 3];
    private static uint U32(byte[] b, int p, bool l) => l ? (uint)(b[p] | b[p + 1] << 8 | b[p + 2] << 16 | b[p + 3] << 24) : U32BE(b, p);
    private static ushort U16(byte[] b, int p, bool l) => l ? (ushort)(b[p] | b[p + 1] << 8) : (ushort)(b[p] << 8 | b[p + 1]);
    private static ImageGenerationMetadata Build(Dictionary<string, string> source)
    {
        var all = new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase); foreach (var x in source.ToArray()) ExtractXmp(x.Value, all); string parameters = Find(all, "parameters", "comment", "description", "text"); ParseParameters(parameters, all); string comfy = Find(all, "comfy prompt", "comfy_prompt", "prompt_json", "prompt"); string workflow = Find(all, "workflow", "workflow_json"); if (string.IsNullOrWhiteSpace(comfy)) comfy = all.Values.FirstOrDefault(v => v.TrimStart().StartsWith("{") && v.Contains('"')) ?? ""; ParseComfy(comfy, all);
        return new() { Parameters = parameters, Prompt = Find(all, "prompt", "description"), PositivePrompt = Find(all, "positive prompt", "positive"), NegativePrompt = Find(all, "negative prompt", "negative", "negative_prompt"), Workflow = workflow, ComfyPromptJson = comfy, Model = Find(all, "model"), Checkpoint = Find(all, "checkpoint", "ckpt_name", "model_name"), Unet = Find(all, "unet"), Clip = Find(all, "clip", "clip_name"), Vae = Find(all, "vae", "vae_name"), Lora = Find(all, "lora", "loras"), Seed = Find(all, "seed"), Steps = Find(all, "steps"), Cfg = Find(all, "cfg", "cfg scale", "cfg_scale"), Sampler = Find(all, "sampler", "sampler_name"), Scheduler = Find(all, "scheduler"), Fields = all };
    }
    private static void ParseParameters(string s, Dictionary<string, string> d) { foreach (Match m in Regex.Matches(s ?? "", @"(?i)(negative prompt|positive prompt|prompt|seed|steps|cfg(?: scale)?|sampler(?: name)?|scheduler|model|checkpoint|clip|vae|lora)\s*:\s*(.*?)(?=,\s*(?:negative prompt|positive prompt|prompt|seed|steps|cfg(?: scale)?|sampler(?: name)?|scheduler|model|checkpoint|clip|vae|lora)\s*:|$)")) Add(d, m.Groups[1].Value, m.Groups[2].Value); }
    private static string Find(Dictionary<string, string> d, params string[] keys) => keys.Select(k => d.FirstOrDefault(x => Norm(x.Key) == Norm(k)).Value).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
    private static string Norm(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]", "");
    private static void ExtractXmp(string s, Dictionary<string, string> d) { if (!s.Contains('<')) return; foreach (Match m in Regex.Matches(s, @"(?is)<(?:\w+:)?([\w.-]+)[^>]*>(.*?)</(?:\w+:)?\1>")) Add(d, m.Groups[1].Value, Regex.Replace(m.Groups[2].Value, "<[^>]+>", "")); }
    private static void ParseComfy(string json, Dictionary<string, string> d) { try { using var doc = JsonDocument.Parse(json); Walk(doc.RootElement, d); } catch { } }
    private static void Walk(JsonElement e, Dictionary<string, string> d) { if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject()) { if (p.Value.ValueKind == JsonValueKind.String) Add(d, p.Name, p.Value.GetString()); else Walk(p.Value, d); } else if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) Walk(x, d); }
}
