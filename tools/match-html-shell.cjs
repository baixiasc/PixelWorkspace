const fs=require('fs'),p=require('path');const file=p.join(__dirname,'App.xaml.cs');let s=fs.readFileSync(file,'utf8');let start=s.indexOf(' readonly ContentControl content'),end=s.indexOf(' public void VisitAllPages');s=s.slice(0,start)+` readonly ContentControl content=new();
 readonly TextBlock heading=new(){FontSize=22,FontWeight=FontWeights.Bold};
 readonly UserControl?[] pages=new UserControl?[3];
 readonly Button[] tabs=new Button[3];
 readonly string[] names=["图片编辑","图片混淆","去除元数据"];
 static Brush Color(string value)=>(Brush)new BrushConverter().ConvertFromString(value)!;
 public WorkspaceWindow()
 {
  Title="图片处理工具箱 · 像素工作台";Width=1380;Height=900;MinWidth=900;MinHeight=620;WindowStartupLocation=WindowStartupLocation.CenterScreen;
  Background=Color("#F3F5F1");Foreground=Color("#20343A");FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");UseLayoutRounding=true;SnapsToDevicePixels=true;
  var root=new Grid{Background=Background};var rail=new ColumnDefinition{Width=new GridLength(228)};root.ColumnDefinitions.Add(rail);root.ColumnDefinitions.Add(new ColumnDefinition());Content=root;
  var sidebar=new Border{Background=Color("#FFFDFA"),BorderBrush=Color("#D9E3DF"),BorderThickness=new Thickness(0,0,1,0),Padding=new Thickness(18,32,18,22)};root.Children.Add(sidebar);
  var dock=new DockPanel();sidebar.Child=dock;
  var brand=new StackPanel{Margin=new Thickness(10,0,0,32)};brand.Children.Add(new TextBlock{Text="像素工作台",FontSize=20,FontWeight=FontWeights.Bold});brand.Children.Add(new TextBlock{Text="P I X E L  W O R K S P A C E",FontSize=9,Foreground=Color("#8A9892"),Margin=new Thickness(0,10,0,0)});DockPanel.SetDock(brand,Dock.Top);dock.Children.Add(brand);
  var noteText=new StackPanel();noteText.Children.Add(new TextBlock{Text="你的图片，留在本地",FontSize=12,FontWeight=FontWeights.SemiBold,Foreground=Color("#287F7A"),Margin=new Thickness(0,0,0,6)});noteText.Children.Add(new TextBlock{Text="切换工具保留操作状态。\\n各工具独立加载图片。",FontSize=12,Foreground=Color("#718184"),LineHeight=23});
  var note=new Border{BorderBrush=Color("#D9E3DF"),BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(0,20,0,0),Margin=new Thickness(8,20,8,0),Child=noteText};DockPanel.SetDock(note,Dock.Bottom);dock.Children.Add(note);
  var nav=new StackPanel();dock.Children.Add(nav);
  for(int i=0;i<3;i++){int index=i;var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(new TextBlock{Text=$"0{i+1}",FontSize=11,Opacity=.55,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)});row.Children.Add(new TextBlock{Text=names[i],FontSize=14,FontWeight=FontWeights.SemiBold});var b=new Button{Content=row,HorizontalContentAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,15,12,15),Margin=new Thickness(0,0,0,9)};b.Click+=(_,_)=>Select(index);tabs[i]=b;nav.Children.Add(b);}
  var main=new Grid{Margin=new Thickness(28,28,28,20)};Grid.SetColumn(main,1);root.Children.Add(main);main.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});main.RowDefinitions.Add(new RowDefinition());
  var top=new DockPanel{Margin=new Thickness(0,0,0,20)};main.Children.Add(top);
  var badge=new Border{Background=Color("#FFF1DA"),BorderBrush=Color("#F0DBB5"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Padding=new Thickness(12,7,12,7),VerticalAlignment=VerticalAlignment.Center,Child=new TextBlock{Text="● 本地图片处理",FontSize=11,Foreground=Color("#8E6638")}};DockPanel.SetDock(badge,Dock.Right);top.Children.Add(badge);
  var titles=new StackPanel();titles.Children.Add(new TextBlock{Text="YOUR EVERYDAY IMAGE TOOLS",FontSize=10,Foreground=Color("#A07959"),Margin=new Thickness(0,0,0,6)});titles.Children.Add(heading);top.Children.Add(titles);
  var frame=new Border{Background=Color("#FFFEFA"),BorderBrush=Color("#D9E3DF"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Child=content};frame.SizeChanged+=(_,_)=>frame.Clip=new RectangleGeometry(new Rect(0,0,frame.ActualWidth,frame.ActualHeight),18,18);Grid.SetRow(frame,1);main.Children.Add(frame);
  SizeChanged+=(_,_)=>{bool narrow=ActualWidth<=1000;rail.Width=new GridLength(narrow?180:228);sidebar.Padding=narrow?new Thickness(12,24,12,22):new Thickness(18,32,18,22);main.Margin=narrow?new Thickness(14,20,14,20):new Thickness(28,28,28,20);};Select(0);
 }
 void Select(int index)
 {
  pages[index] ??= index switch {0=>new EditorPage(),1=>new ObfuscationPage(),_=>new MetadataPage()};content.Content=pages[index];heading.Text=names[index];
  for(int i=0;i<3;i++){tabs[i].Background=Color(i==index?"#DFF0EB":"#FFFDFA");tabs[i].Foreground=Color(i==index?"#17665F":"#687B7D");tabs[i].BorderBrush=Color(i==index?"#C5E1D9":"#FFFDFA");}
 }
`+s.slice(end);s=s.replace('new[]{1280,900}','new[]{1380,900}').replace('width==900?620:820','width==900?620:900');fs.writeFileSync(file,s);const x=p.join(__dirname,'App.xaml');let a=fs.readFileSync(x,'utf8');for(const [o,n] of Object.entries({'#5263D6':'#287F7A','#FFFFFF':'#FFFEFA','#505B6F':'#46666A','#E7EAF0':'#CBDAD5','#202938':'#20343A','CornerRadius="7"':'CornerRadius="10"'}))a=a.split(o).join(n);fs.writeFileSync(x,a);
