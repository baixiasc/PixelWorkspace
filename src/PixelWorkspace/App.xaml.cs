using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PixelWorkspace;
public partial class App : Application
{
 protected override void OnStartup(StartupEventArgs e)
 {
  base.OnStartup(e);
  DispatcherUnhandledException += (_, args) => { MessageBox.Show(args.Exception.Message,"操作未完成",MessageBoxButton.OK,MessageBoxImage.Warning); args.Handled=true; };
  if(e.Args.Contains("--self-test")) { try { var fixture=System.IO.Path.Combine(AppContext.BaseDirectory,"compatibility-fixtures.json"); System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"self-test.txt"),NativeTests.Run(fixture)); Shutdown(0); } catch(Exception ex) { System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"self-test.txt"),ex.ToString()); Shutdown(1); } return; }
  var window=new WorkspaceWindow(); MainWindow=window;
  if(e.Args.Contains("--ui-preview")) { window.Show(); window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(async ()=> { try { await window.SavePreviews(AppContext.BaseDirectory); Shutdown(0); } catch(Exception ex) { System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"preview-error.txt"),ex.ToString()); Shutdown(1); } })); }
   else if(e.Args.Contains("--smoke-test")) { window.Show(); window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=> { try { window.VisitAllPages(); System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"smoke-test.txt"),"PASS: native WPF window and all six pages initialized."); Shutdown(0); } catch(Exception ex) { System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"smoke-test.txt"),ex.ToString()); Shutdown(1); } })); }
  else window.Show();
 }
}
public sealed class WorkspaceWindow : Window
{
 readonly ContentControl content=new();
 readonly TextBlock heading=new(){FontSize=22,FontWeight=FontWeights.Bold};
 readonly UserControl?[] pages=new UserControl?[6];
 readonly Button[] tabs=new Button[6];
 readonly string[] names=["图片浏览","图片编辑","图片混淆","去除元数据","手机查看","设置"];
 static Brush Color(string value)=>(Brush)new BrushConverter().ConvertFromString(value)!;
 public WorkspaceWindow()
 {
  Title="图片处理工具箱 · 像素工作台";Width=1380;Height=900;MinWidth=900;MinHeight=620;WindowStartupLocation=WindowStartupLocation.CenterScreen;Closing+=(_,_)=>{foreach(var page in pages){if(page is IDisposable disposable) disposable.Dispose();}};
  Background=Color("#F3F5F1");Foreground=Color("#20343A");FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");UseLayoutRounding=true;SnapsToDevicePixels=true;
  var root=new Grid{Background=Background};var rail=new ColumnDefinition{Width=new GridLength(228)};root.ColumnDefinitions.Add(rail);root.ColumnDefinitions.Add(new ColumnDefinition());Content=root;
  var sidebar=new Border{Background=Color("#FFFDFA"),BorderBrush=Color("#D9E3DF"),BorderThickness=new Thickness(0,0,1,0),Padding=new Thickness(18,32,18,22)};root.Children.Add(sidebar);
  var dock=new DockPanel();sidebar.Child=dock;
  var brand=new StackPanel{Margin=new Thickness(10,0,0,32)};brand.Children.Add(new TextBlock{Text="像素工作台",FontSize=20,FontWeight=FontWeights.Bold});brand.Children.Add(new TextBlock{Text="P I X E L  W O R K S P A C E",FontSize=9,Foreground=Color("#8A9892"),Margin=new Thickness(0,10,0,0)});DockPanel.SetDock(brand,Dock.Top);dock.Children.Add(brand);
  var noteText=new StackPanel();noteText.Children.Add(new TextBlock{Text="你的图片，留在本地",FontSize=12,FontWeight=FontWeights.SemiBold,Foreground=Color("#287F7A"),Margin=new Thickness(0,0,0,6)});noteText.Children.Add(new TextBlock{Text="切换工具保留操作状态。\n各工具独立加载图片。",FontSize=12,Foreground=Color("#718184"),LineHeight=23});
  var note=new Border{BorderBrush=Color("#D9E3DF"),BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(0,20,0,0),Margin=new Thickness(8,20,8,0),Child=noteText};DockPanel.SetDock(note,Dock.Bottom);dock.Children.Add(note);
  var nav=new StackPanel();dock.Children.Add(nav);
  for(int i=0;i<6;i++){int index=i;var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(new TextBlock{Text=$"0{i+1}",FontSize=11,Opacity=.55,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)});row.Children.Add(new TextBlock{Text=names[i],FontSize=14,FontWeight=FontWeights.SemiBold});var b=new Button{Content=row,HorizontalContentAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,15,12,15),Margin=new Thickness(0,0,0,9)};b.Click+=(_,_)=>Select(index);tabs[i]=b;nav.Children.Add(b);}
  var main=new Grid{Margin=new Thickness(28,28,28,20)};Grid.SetColumn(main,1);root.Children.Add(main);main.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});main.RowDefinitions.Add(new RowDefinition());
  var top=new DockPanel{Margin=new Thickness(0,0,0,20)};main.Children.Add(top);
  var badge=new Border{Background=Color("#FFF1DA"),BorderBrush=Color("#F0DBB5"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Padding=new Thickness(12,7,12,7),VerticalAlignment=VerticalAlignment.Center,Child=new TextBlock{Text="● 本地图片处理",FontSize=11,Foreground=Color("#8E6638")}};DockPanel.SetDock(badge,Dock.Right);top.Children.Add(badge);
  var titles=new StackPanel();titles.Children.Add(new TextBlock{Text="YOUR EVERYDAY IMAGE TOOLS",FontSize=10,Foreground=Color("#A07959"),Margin=new Thickness(0,0,0,6)});titles.Children.Add(heading);top.Children.Add(titles);
  var frame=new Border{Background=Color("#FFFEFA"),BorderBrush=Color("#D9E3DF"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Child=content};frame.SizeChanged+=(_,_)=>frame.Clip=new RectangleGeometry(new Rect(0,0,frame.ActualWidth,frame.ActualHeight),18,18);Grid.SetRow(frame,1);main.Children.Add(frame);
  SizeChanged+=(_,_)=>{bool narrow=ActualWidth<=1000;rail.Width=new GridLength(narrow?180:228);sidebar.Padding=narrow?new Thickness(12,24,12,22):new Thickness(18,32,18,22);main.Margin=narrow?new Thickness(14,20,14,20):new Thickness(28,28,28,20);};
   Select(0);
 }
 void Select(int index)
 {
  pages[index] ??= index switch {0=>BuildBrowser(),1=>new EditorPage(),2=>new ObfuscationPage(),3=>new MetadataPage(),4=>new PhoneServerPage(),_=>BuildSettings()};content.Content=pages[index];heading.Text=names[index];
  for(int i=0;i<6;i++){tabs[i].Background=Color(i==index?"#DFF0EB":"#FFFDFA");tabs[i].Foreground=Color(i==index?"#17665F":"#687B7D");tabs[i].BorderBrush=Color(i==index?"#C5E1D9":"#FFFDFA");}
 }
 SettingsPage BuildSettings()
   {
    var settings = new SettingsPage();
    settings.DefaultFolderChanged += path => { if (pages[0] is BrowserPage browser) browser.OpenFolder(path); if (pages[4] is PhoneServerPage phone) phone.UseDefaultFolder(path); };
    return settings;
   }
  BrowserPage BuildBrowser()
  {
   var browser=new BrowserPage();
   browser.TransferToEditor += path => { Select(1); ((EditorPage)pages[1]!).ImportFile(path); };
   browser.TransferToObfuscation += path => { Select(2); ((ObfuscationPage)pages[2]!).ImportFile(path); };
   browser.TransferToMetadata += path => { Select(3); ((MetadataPage)pages[3]!).ImportFile(path); };
   return browser;
  }
  public void VisitAllPages(){for(var i=0;i<6;i++) Select(i); Select(0);}
 public async System.Threading.Tasks.Task SavePreviews(string directory)
 {
  // Render the content at a fixed canvas size instead of resizing the window:
  // Windows shrinks a window that does not fit the screen, which would make the
  // preview size depend on the machine's DPI scaling.
  foreach(var size in new[]{new Size(1364,861),new Size(884,581)})
  {
   for(int i=0;i<6;i++)
   {
    Select(i);UpdateLayout();
    await System.Threading.Tasks.Task.Delay(size.Width>1000?1200:900);
    var visual=(FrameworkElement)Content;
    visual.Measure(size);visual.Arrange(new Rect(size));visual.UpdateLayout();
    var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
    var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
    using var stream=System.IO.File.Create(System.IO.Path.Combine(directory,$"ui-{(int)size.Width}-{i}.png"));encoder.Save(stream);
   }
  }
 }
}
