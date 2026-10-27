using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PixelWorkspace;

internal sealed class ImageFileItem
{
    public string Name = "image.png";
    public byte[] Original = Array.Empty<byte>();
    public byte[]? Output;
    public BitmapSource Preview = null!;
    public bool IsPng;
    public bool Selected = true;
    public string State = "待处理";
    public Dictionary<string, string> Text = new();
    public BitmapMetadata? Exif;
}

internal static class ImageFiles
{
    internal static readonly Brush Teal = new SolidColorBrush(Color.FromRgb(40,127,122));
    internal static readonly Brush Paper = new SolidColorBrush(Color.FromRgb(255,254,250));
    internal static readonly Brush Success = new SolidColorBrush(Color.FromRgb(60,146,114));
    internal static readonly Brush Amber = new SolidColorBrush(Color.FromRgb(217,139,67));
    internal static readonly Brush Coral = new SolidColorBrush(Color.FromRgb(201,102,91));
    internal static readonly Brush Canvas = new SolidColorBrush(Color.FromRgb(237,243,240));
    internal static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(32,52,58));
    internal static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(104,123,125));
    internal static readonly Brush Line = new SolidColorBrush(Color.FromRgb(217,227,223));
    internal static readonly Brush Soft = new SolidColorBrush(Color.FromRgb(246,248,245));
    internal static readonly Brush Hint = new SolidColorBrush(Color.FromRgb(238,246,241));
    internal static readonly Brush Warm = new SolidColorBrush(Color.FromRgb(247,234,217));
    internal static Brush DottedCanvas()
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Canvas,null,new RectangleGeometry(new Rect(0,0,18,18))));
        drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(102,169,201,190)),null,new EllipseGeometry(new Point(9,9),.8,.8)));
        var brush = new DrawingBrush(drawing) { TileMode=TileMode.Tile, ViewportUnits=BrushMappingMode.Absolute, Viewport=new Rect(0,0,18,18), Stretch=Stretch.None };
        brush.Freeze(); return brush;
    }
    internal static Button ToneButton(string text, Action action, Brush background, Brush? foreground = null)
    {
        var button=Button(text,action);button.Background=background;button.BorderBrush=background;button.Foreground=foreground??Brushes.White;return button;
    }
    internal static Border Section(UIElement content, Brush background, Thickness padding) => new() { Child=content, Background=background, Padding=padding, BorderBrush=Line, BorderThickness=new Thickness(0,0,0,1) };
    internal static FrameworkElement HintStrip(params string[] hints)
    {
        var row=new WrapPanel();foreach(var text in hints){var label=Label(text,12);label.Foreground=Muted;label.Margin=new Thickness(0,3,24,3);row.Children.Add(label);}
        return Section(row,Hint,new Thickness(24,7,24,7));
    }
    internal static FrameworkElement Footer(TextBlock status, string badge, string note)
    {
        var row=new WrapPanel();
        var tag=Label(badge,12);tag.Margin=new Thickness(0);tag.Foreground=new SolidColorBrush(Color.FromRgb(138,98,56));
        row.Children.Add(new Border{Child=tag,Background=Warm,CornerRadius=new CornerRadius(8),Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,12,6)});
        status.Foreground=Muted;status.Margin=new Thickness(0,5,16,6);row.Children.Add(status);
        var label=Label(note,12);label.Foreground=Muted;label.Margin=new Thickness(0,5,0,6);row.Children.Add(label);
        return Section(row,Paper,new Thickness(24,12,24,6));
    }
    internal static void FitCards(WrapPanel cards, double available)
    {
        if(available<=0)return;
        int columns=Math.Max(1,(int)(available/236));double width=Math.Max(1,available/columns-16);
        foreach(FrameworkElement card in cards.Children)card.Width=width;
    }
    internal static Button Button(string text, Action action, bool primary = false)
    {
        var b = new Button { Content = text, Padding = new Thickness(14,8,14,8), Margin = new Thickness(0,0,8,8), MinHeight = 36, Background = primary ? Teal : Paper, Foreground = primary ? Brushes.White : Ink, BorderBrush = primary ? Teal : Line, BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand };
        System.Windows.Automation.AutomationProperties.SetName(b,text);
        b.Click += (_,_) => action(); return b;
    }
    internal static TextBlock Label(string text, double size = 13) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,10), Foreground = Ink };
    internal static MenuItem MenuAction(string text, Action action, bool enabled = true, bool danger = false)
    {
        var item = new MenuItem { Header = text, IsEnabled = enabled, Padding = new Thickness(12,8,12,8) };
        if(danger) item.Foreground = Coral;
        item.Click += (_,_) => action();
        return item;
    }
    internal static Button MenuButton(string text, bool primary, params MenuItem[] entries)
    {
        var menu = new ContextMenu { Background = Paper, Foreground = Ink, BorderBrush = Line, BorderThickness = new Thickness(1), Padding = new Thickness(4) };
        foreach(var entry in entries) menu.Items.Add(entry);
        var button = Button(text + " ▾", () => { }, primary);
        button.ContextMenu = menu;
        button.Click += (_,_) => { menu.PlacementTarget = button; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; menu.IsOpen = true; };
        button.IsEnabledChanged += (_,_) => { if(!button.IsEnabled) menu.IsOpen = false; };
        return button;
    }
    internal static FrameworkElement EmptyState(Action choose, string heading = "拖拽图片到此处", string description = "支持拖放、剪贴板粘贴、多文件选择\n点击选择图片，或按 Ctrl+V 粘贴")
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth=480 };
        var icon=Label("\uE91B",48);icon.FontFamily=new FontFamily("Segoe MDL2 Assets");icon.Foreground=Teal;icon.Opacity=.65;icon.TextAlignment=TextAlignment.Center;panel.Children.Add(icon);
        var title = Label(heading,20); title.FontWeight = FontWeights.SemiBold; title.TextAlignment = TextAlignment.Center; panel.Children.Add(title);
        var hint = Label(description); hint.Foreground = Muted; hint.TextAlignment = TextAlignment.Center; hint.LineHeight=24; panel.Children.Add(hint);
        var button = Button("选择图片",choose,true); button.HorizontalAlignment = HorizontalAlignment.Center; button.Margin = new Thickness(0,12,0,0); panel.Children.Add(button);
        var host = new Border { Child=panel, Padding=new Thickness(8), Background=Brushes.Transparent, HorizontalAlignment=HorizontalAlignment.Stretch, VerticalAlignment=VerticalAlignment.Stretch };
        host.SizeChanged+=(_,_)=>{bool compact=host.ActualHeight<240;icon.Visibility=compact?Visibility.Collapsed:Visibility.Visible;hint.Visibility=compact?Visibility.Collapsed:Visibility.Visible;title.FontSize=compact?16:20;title.Margin=new Thickness(0,0,0,compact?2:10);button.Margin=new Thickness(0,compact?2:12,0,0);};
        return host;
    }
    internal static string? StartingFolder(string? current = null)
    {
        if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current)) return current;
        var saved = SettingsStore.LoadDefaultFolder();
        if (!string.IsNullOrWhiteSpace(saved) && Directory.Exists(saved)) return saved;
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        return Directory.Exists(pictures) ? pictures : null;
    }
    internal static string[] Pick()
    {
        var d = new OpenFileDialog { Multiselect = true, Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.wdp;*.webp|所有文件|*.*" };
        var start = StartingFolder(); if (start != null) d.InitialDirectory = start;
        return d.ShowDialog() == true ? d.FileNames : Array.Empty<string>();
    }
    internal static ImageFileItem Load(string path) => Load(File.ReadAllBytes(path), Path.GetFileName(path));
    internal static ImageFileItem Load(byte[] bytes, string name)
    {
        var it = new ImageFileItem { Name = name, Original = bytes, IsPng = bytes.Length >= 8 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71 };
        it.Preview = Decode(bytes);
        if (it.IsPng) it.Text = ReadPngText(bytes);
        else
        {
            using var stream = new MemoryStream(bytes);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            if (frame.Metadata is BitmapMetadata m) it.Exif = ExifSubset(m);
        }
        return it;
    }
    internal static BitmapSource Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        ObfuscationEngine.ValidateSize(frame.PixelWidth, frame.PixelHeight);
        BitmapSource source = frame;
        // Browser image decoding honors EXIF orientation. Apply it to pixels, never carry the orientation tag forward.
        ushort o = 1;
        if (frame.Metadata is BitmapMetadata m)
            foreach (var q in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
                try { if (m.GetQuery(q) is object value) { o = Convert.ToUInt16(value); break; } } catch (Exception) { }
        Transform? transform = o switch {
            2 => new ScaleTransform(-1,1), 3 => new RotateTransform(180), 4 => new ScaleTransform(1,-1),
            5 => new MatrixTransform(0,1,1,0,0,0), 6 => new RotateTransform(90),
            7 => new MatrixTransform(0,-1,-1,0,0,0), 8 => new RotateTransform(270), _ => null };
        if (transform != null) source = new TransformedBitmap(source, transform);
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); converted.Freeze(); return converted;
    }
    internal static BitmapSource Raw(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = checked(converted.PixelWidth * 4);
        byte[] pixels = new byte[checked(stride * converted.PixelHeight)]; converted.CopyPixels(pixels, stride, 0);
        var result = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride); result.Freeze(); return result;
    }
    internal static byte[] Encode(BitmapSource source, bool png, BitmapMetadata? metadata = null)
    {
        source = Raw(source); // Never propagate metadata or decoder color contexts through BitmapFrame.
        if (!png)
        {
            int stride = source.PixelWidth * 4; byte[] p = new byte[stride * source.PixelHeight]; source.CopyPixels(p,stride,0);
            // Canvas JPEG composites transparent pixels onto black.
            for (int i=0; i<p.Length; i+=4) { int a=p[i+3]; for(int c=0;c<3;c++) p[i+c]=(byte)((p[i+c]*a+127)/255); p[i+3]=255; }
            source=BitmapSource.Create(source.PixelWidth,source.PixelHeight,96,96,PixelFormats.Bgra32,null,p,stride);
        }
        BitmapEncoder encoder = png ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 95 };
        encoder.Frames.Add(BitmapFrame.Create(source,null,metadata,null));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    internal static byte[] Obfuscate(ImageFileItem item, bool decrypt)
    {
        var source = Decode(item.Output ?? item.Original); int stride = checked(source.PixelWidth * 4);
        var pixels = new byte[checked(stride * source.PixelHeight)]; source.CopyPixels(pixels,stride,0);
        var moved = ObfuscationEngine.Transform(pixels,source.PixelWidth,source.PixelHeight,decrypt);
        var bitmap = BitmapSource.Create(source.PixelWidth,source.PixelHeight,96,96,PixelFormats.Bgra32,null,moved,stride);
        byte[] result = Encode(bitmap,item.IsPng,item.IsPng ? null : item.Exif);
        return item.IsPng ? InjectPngText(result,item.Text) : result;
    }
    private static BitmapMetadata ExifSubset(BitmapMetadata input)
    {
        var output = new BitmapMetadata("jpg");
        // Exactly the fields copied by injectExifToDataUrl in the original. ISO is omitted:
        // exif-js exposes ISOSpeedRatings, whereas the original checks the nonexistent ISO property.
        foreach (var pair in new[] { ("/app1/ifd/", new[]{271,272,305,306,315,33432}), ("/app1/ifd/exif/",new[]{36867,36868,33434,33437,37386,42036}), ("/app1/ifd/gps/",new[]{2,4}) })
            foreach (int id in pair.Item2)
            {
                string query = pair.Item1 + "{ushort=" + id + "}";
                try { var value = input.GetQuery(query); if (value != null) output.SetQuery(query,value); } catch (Exception) { }
            }
        output.Freeze(); return output;
    }
    internal static ImageFileItem? Paste()
    {
        if (Clipboard.ContainsImage()) { var image = Clipboard.GetImage(); if (image != null) return Load(Encode(image,true),"clipboard.png"); }
        return null;
    }
    internal static void Copy(byte[] bytes)
    {
        var data = new DataObject(); data.SetImage(Decode(bytes));
        using var png = new MemoryStream(Encode(Decode(bytes),true));
        data.SetData("PNG",png); Clipboard.SetDataObject(data,true);
    }
    internal static string CleanName(string value)
    {
        string s = Regex.Replace(Regex.Replace(string.IsNullOrEmpty(value) ? "image" : value, "[\\\\/:*?\"<>|]+", "_"), @"\s+", "_");
        s = s.Length > 100 ? s[..100] : s;
        return s is "." or ".." or "" ? "image" : s;
    }
    internal static string OutputName(ImageFileItem it, bool metadata) => CleanName(Path.GetFileNameWithoutExtension(it.Name)) + (metadata || it.IsPng ? ".png" : ".jpg");
    internal static void Save(ImageFileItem it, bool metadata)
    {
        if (it.Output == null) throw new InvalidOperationException("请先处理图片。");
        var d = new SaveFileDialog { FileName = OutputName(it,metadata), Filter = "图片|*.png;*.jpg" };
        var start = StartingFolder(); if (start != null) d.InitialDirectory = start;
        if (d.ShowDialog() == true) File.WriteAllBytes(d.FileName,it.Output);
    }
    internal static int Zip(IEnumerable<ImageFileItem> items, string folder, bool metadata)
    {
        var ready = items.Where(i=>i.Output != null).ToArray();
        if (ready.Length == 0) throw new InvalidOperationException("没有处理完成的图片，请先处理。");
        folder=CleanName(folder); var d = new SaveFileDialog { FileName=folder+".zip", Filter="ZIP 压缩文件|*.zip" };
        var startFolder = StartingFolder(); if (startFolder != null) d.InitialDirectory = startFolder;
        if (d.ShowDialog()!=true) return 0;
        using var stream=File.Create(d.FileName); using var zip=new ZipArchive(stream,ZipArchiveMode.Create);
        var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var it in ready)
        {
            string name=OutputName(it,metadata), unique=name; int n=2;
            while(!used.Add(unique)) unique=Path.GetFileNameWithoutExtension(name)+" ("+(n++)+")"+Path.GetExtension(name);
            using var entry=zip.CreateEntry(folder+"/"+unique,CompressionLevel.NoCompression).Open(); entry.Write(it.Output!);
        }
        return ready.Length;
    }
    private static int Big(byte[] b,int p) => checked((int)((uint)b[p]<<24 | (uint)b[p+1]<<16 | (uint)b[p+2]<<8 | b[p+3]));
    private static void BigWrite(Stream s,uint n) { s.WriteByte((byte)(n>>24));s.WriteByte((byte)(n>>16));s.WriteByte((byte)(n>>8));s.WriteByte((byte)n); }
    internal static Dictionary<string,string> ReadPngText(byte[] bytes)
    {
        var result=new Dictionary<string,string>();
        for(int p=8;p+12<=bytes.Length;)
        {
            int length=Big(bytes,p); if(length>bytes.Length-p-12) break;
            string type=Encoding.ASCII.GetString(bytes,p+4,4); int start=p+8,end=start+length;
            if(type is "tEXt" or "iTXt")
            {
                int i=start; while(i<end && bytes[i]!=0)i++;
                string key=Encoding.Latin1.GetString(bytes,start,i-start); i++;
                if(type=="iTXt") { i+=2; while(i<end&&bytes[i]!=0)i++;i++;while(i<end&&bytes[i]!=0)i++;i++; }
                if(i<=end)
                {
                    string text=type=="iTXt" ? Encoding.UTF8.GetString(bytes,i,end-i) : WebLatin1(bytes,i,end-i);
                    result[key]=text;
                }
            }
            p+=length+12; if(type=="IEND")break;
        }
        return result;
    }
    private static string WebLatin1(byte[] b,int start,int length)
    {
        const string cp="€\u0081‚ƒ„…†‡ˆ‰Š‹Œ\u008dŽ\u008f\u0090‘’“”•–—˜™š›œ\u009džŸ";
        var chars=new char[length];for(int i=0;i<length;i++){byte v=b[start+i];chars[i]=v>=128&&v<=159?cp[v-128]:(char)v;}return new string(chars);
    }
    internal static byte[] InjectPngText(byte[] png, Dictionary<string,string> text)
    {
        if(text.Count==0)return png;
        int p=8;while(p+12<=png.Length&&Encoding.ASCII.GetString(png,p+4,4)!="IDAT")p+=Big(png,p)+12;
        using var stream=new MemoryStream();stream.Write(png,0,p);
        foreach(var pair in text)
        {
            byte[] data=Encoding.UTF8.GetBytes("tEXt"+pair.Key+"\0"+pair.Value);
            BigWrite(stream,(uint)(data.Length-4));stream.Write(data);uint crc=0xffffffff;
            foreach(byte value in data){crc^=value;for(int j=0;j<8;j++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0);}
            BigWrite(stream,crc^0xffffffff);
        }
        stream.Write(png,p,png.Length-p);return stream.ToArray();
    }
}
