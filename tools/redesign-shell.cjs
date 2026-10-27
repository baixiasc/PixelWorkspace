const fs=require('fs'),path=require('path');const p=path.join(__dirname,'App.xaml.cs');let s=fs.readFileSync(p,'utf8');s=s.slice(0,s.indexOf('public sealed class WorkspaceWindow'))+`public sealed class WorkspaceWindow : Window
{
 readonly ContentControl content=new();
 readonly UserControl?[] pages=new UserControl?[3];
 readonly Button[] tabs=new Button[3];
 readonly string[] names=["图片编辑","图片混淆","元数据清理"];
 static Brush Color(string value)=>(Brush)new BrushConverter().ConvertFromString(value)!;
 public WorkspaceWindow()
 {
  Title="像素工作台"; Width=1280; Height=820; MinWidth=900; MinHeight=620; WindowStartupLocation=WindowStartupLocation.CenterScreen; Background=Color("#F5F6F8"); FontFamily=new FontFamily("Microsoft YaHei UI");
  UseLayoutRounding=true; SnapsToDevicePixels=true;
  var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());Content=root;
  var top=new Border{Background=Brushes.White,BorderBrush=Color("#E7EAF0"),BorderThickness=new Thickness(0,0,0,1),Padding=new Thickness(26,12,26,12)};root.Children.Add(top);
  var bar=new DockPanel{LastChildFill=true};top.Child=bar;
  var brand=new TextBlock{Text="像素工作台",FontSize=17,FontWeight=FontWeights.SemiBold,Foreground=Color("#202938"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,32,0)};DockPanel.SetDock(brand,Dock.Left);bar.Children.Add(brand);
  var local=new TextBlock{Text="本地处理",FontSize=11,Foreground=Color("#7B8494"),VerticalAlignment=VerticalAlignment.Center,ToolTip="图片在本机处理，不上传服务器"};DockPanel.SetDock(local,Dock.Right);bar.Children.Add(local);
  var nav=new StackPanel{Orientation=Orientation.Horizontal};bar.Children.Add(nav);
  for(int i=0;i<3;i++){int index=i;var button=new Button{Content=names[i],Padding=new Thickness(18,9,18,9),Margin=new Thickness(3,0,3,0),BorderThickness=new Thickness(0)};button.Click+=(_,_)=>Select(index);tabs[i]=button;nav.Children.Add(button);}
  var frame=new Border{Margin=new Thickness(18),Background=Brushes.White,BorderBrush=Color("#E7EAF0"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),ClipToBounds=true,Child=content};Grid.SetRow(frame,1);root.Children.Add(frame);Select(0);
 }
 void Select(int index)
 {
  pages[index] ??= index switch {0=>new EditorPage(),1=>new ObfuscationPage(),_=>new MetadataPage()};content.Content=pages[index];
  for(int i=0;i<3;i++){tabs[i].Background=Color(i==index?"#EEF0FC":"#FFFFFF");tabs[i].Foreground=Color(i==index?"#5263D6":"#7B8494");tabs[i].FontWeight=i==index?FontWeights.SemiBold:FontWeights.Normal;}
 }
 public void VisitAllPages(){Select(1);Select(2);Select(0);}
}
`;fs.writeFileSync(p,s);
