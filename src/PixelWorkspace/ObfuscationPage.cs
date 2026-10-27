using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelWorkspace;

public sealed class ObfuscationPage : UserControl
{
    private readonly List<ImageFileItem> items = new();
    private readonly WrapPanel cards = new();
    private readonly StackPanel root = new() { Margin = new Thickness(24) };
    private int autoMode;
    private readonly TextBlock status = ImageFiles.Label("请选择、拖入图片，或按 Ctrl+V 粘贴。");
    private bool busy;
    private readonly FrameworkElement controls;
    private readonly FrameworkElement emptyState;
    private readonly ScrollViewer cardScroll;
    private readonly Grid previewOverlay;
    private readonly Image previewImage;
    public ObfuscationPage()
    {
        Background = ImageFiles.Paper; AllowDrop = true;
        var layout=new DockPanel{LastChildFill=true,Margin=new Thickness(24)};
        var header=new WrapPanel{Margin=new Thickness(0,0,0,12)};
        var title=ImageFiles.Label("图片混淆",24);title.FontWeight=FontWeights.SemiBold;title.Margin=new Thickness(0,0,32,12);header.Children.Add(title);
        header.Children.Add(ImageFiles.Button("↓ 下载 ZIP",()=>Run(()=>{int n=ImageFiles.Zip(items,"obfuscated",false);status.Text=$"已导出 {n} 张。";return Task.CompletedTask;}),true));
        DockPanel.SetDock(header,Dock.Top);layout.Children.Add(header);
        var settingsContent=new StackPanel();var settingLabel=ImageFiles.Label("⚙ 自动处理设置",14);settingLabel.Foreground=ImageFiles.Teal;settingLabel.FontWeight=FontWeights.SemiBold;settingsContent.Children.Add(settingLabel);
        var options=new WrapPanel{Margin=new Thickness(0,4,0,0)};
        string[] names={"不处理","自动混淆","自动解混淆"};
        for(int i=0;i<names.Length;i++){int mode=i;var radio=new RadioButton{Content=names[i],GroupName="ObfuscationAuto",IsChecked=i==0,Margin=new Thickness(0,0,24,8),Foreground=ImageFiles.Ink,VerticalContentAlignment=VerticalAlignment.Center};radio.Checked+=(_,_)=>autoMode=mode;options.Children.Add(radio);}
        settingsContent.Children.Add(options);
        var settings=new Border{Child=settingsContent,Padding=new Thickness(18,14,18,8),Margin=new Thickness(0,0,0,16),CornerRadius=new CornerRadius(12),Background=ImageFiles.Soft,BorderBrush=ImageFiles.Line,BorderThickness=new Thickness(1)};
        DockPanel.SetDock(settings,Dock.Top);layout.Children.Add(settings);
        var batchInfo=ImageFiles.Label("支持批量上传多张图片，可单独或批量处理");batchInfo.Foreground=ImageFiles.Muted;DockPanel.SetDock(batchInfo,Dock.Top);layout.Children.Add(batchInfo);
        var tools = new WrapPanel{Margin=new Thickness(0,0,0,8)};
        tools.Children.Add(ImageFiles.Button("选择图片",()=>Run(()=>AddFiles(ImageFiles.Pick())),true));
        tools.Children.Add(ImageFiles.ToneButton("粘贴图片",Paste,ImageFiles.Hint,ImageFiles.Teal));
        tools.Children.Add(ImageFiles.Button("全部混淆",()=>Run(()=>Process(items.ToArray(),false)),true));
        tools.Children.Add(ImageFiles.ToneButton("全部解混淆",()=>Run(()=>Process(items.ToArray(),true)),ImageFiles.Hint,ImageFiles.Teal));
        tools.Children.Add(ImageFiles.ToneButton("↻ 全部还原",()=>Run(()=>Restore(items.ToArray())),ImageFiles.Warm,ImageFiles.Ink));
        tools.Children.Add(ImageFiles.ToneButton("清空图片",()=>{if(!busy){items.Clear();Render();status.Text="已清空。";}},ImageFiles.Warm,ImageFiles.Ink));
        DockPanel.SetDock(tools,Dock.Top);layout.Children.Add(tools);
        var help=ImageFiles.Label("PNG 保留文本元数据；其他格式输出 JPEG（质量 95），有损格式不保证逐像素还原。混淆不是安全加密。还原后会按自动设置重新处理。",11);help.Foreground=ImageFiles.Muted;help.Margin=new Thickness(0,8,0,0);DockPanel.SetDock(help,Dock.Bottom);layout.Children.Add(help);
        var footer=ImageFiles.Footer(status,"图片混淆","本地处理 · Ctrl+V 粘贴");DockPanel.SetDock(footer,Dock.Bottom);layout.Children.Add(footer);
        root.Margin=new Thickness(0);root.Children.Add(cards);
        cardScroll=new ScrollViewer { Content=root, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
        cardScroll.SizeChanged+=(_,_)=>ImageFiles.FitCards(cards,cardScroll.ViewportWidth);
        emptyState=ImageFiles.EmptyState(()=>Run(()=>AddFiles(ImageFiles.Pick())));
        var workspace=new Grid();workspace.Children.Add(cardScroll);workspace.Children.Add(emptyState);
        layout.Children.Add(new Border{Child=workspace,Padding=new Thickness(16),Background=ImageFiles.DottedCanvas(),BorderBrush=ImageFiles.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14)});
        previewOverlay = new Grid { Visibility=Visibility.Collapsed, Background=new SolidColorBrush(Color.FromArgb(235, 15, 24, 30)) }; Panel.SetZIndex(previewOverlay,50);
        var previewFrame = new Border { Margin=new Thickness(42), Background=ImageFiles.Paper, CornerRadius=new CornerRadius(14), Padding=new Thickness(18) };
        previewImage = new Image { Stretch=Stretch.Uniform, MaxWidth=1600, MaxHeight=1000 };
        previewFrame.Child=previewImage;
        previewOverlay.Children.Add(previewFrame);
        var closePreview = new Button { Content="×", FontSize=26, Width=44, Height=40, Padding=new Thickness(0), Margin=new Thickness(0,18,18,0), HorizontalAlignment=HorizontalAlignment.Right, VerticalAlignment=VerticalAlignment.Top, Background=ImageFiles.Paper, Foreground=ImageFiles.Ink, BorderBrush=ImageFiles.Line, ToolTip="关闭预览 (Esc)" };
        closePreview.Click+=(_,_)=>previewOverlay.Visibility=Visibility.Collapsed;
        previewOverlay.Children.Add(closePreview);
        var host=new Grid();host.Children.Add(layout);host.Children.Add(previewOverlay);Content=host;controls=layout;
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape&&previewOverlay.Visibility==Visibility.Visible){previewOverlay.Visibility=Visibility.Collapsed;e.Handled=true;}};
        DragOver+=(_,e)=>{e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};
        Drop+=(_,e)=>{if(e.Data.GetData(DataFormats.FileDrop) is string[] paths)Run(()=>AddFiles(paths));e.Handled=true;};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.V&&(Keyboard.Modifiers&ModifierKeys.Control)!=0&&Keyboard.FocusedElement is not TextBox){e.Handled=true;Paste();}};
        Render();
    }
    private async void Run(Func<Task> action)
    {
        if(busy)return;busy=true;controls.IsEnabled=false;
        try{await action();}catch(Exception ex){status.Text="操作失败："+ex.Message;}finally{busy=false;controls.IsEnabled=true;Render();}
    }
    private void Paste()=>Run(async()=>{
        if(Clipboard.ContainsFileDropList()){await AddFiles(Clipboard.GetFileDropList().Cast<string>().ToArray());return;}
        var item=ImageFiles.Paste();if(item==null){status.Text="剪贴板中没有图片。";return;}items.Add(item);await Auto(item);status.Text=$"已载入 {items.Count} 张图片。";
    });
    public void ImportFile(string path)
    {
        if (System.IO.File.Exists(path)) Run(() => AddFiles(new[] { path }));
    }

    private async Task AddFiles(string[] paths)
    {
        int errors=0;string last="";
        foreach(string path in paths)
        {
            try{status.Text="正在载入："+System.IO.Path.GetFileName(path);var item=await Task.Run(()=>ImageFiles.Load(path));items.Add(item);Render();await Auto(item);}
            catch(Exception ex){errors++;last=ex.Message;}
        }
        status.Text=$"已载入 {items.Count} 张图片。"+(errors>0?$" {errors} 张失败：{last}":"");
    }
    private Task Auto(ImageFileItem it)=>autoMode==0?Task.CompletedTask:Process(new[]{it},autoMode==2);
    private async Task Process(ImageFileItem[] list,bool decrypt)
    {
        if(list.Length==0){status.Text="请先添加图片。";return;}
        int done=0,failed=0;
        foreach(var it in list)
        {
            status.Text=$"正在{(decrypt?"解混淆":"混淆")}：{it.Name} ({done+failed+1}/{list.Length})";
            try{it.Output=await Task.Run(()=>ImageFiles.Obfuscate(it,decrypt));it.Preview=ImageFiles.Decode(it.Output);it.State=decrypt?"已解混淆":"已混淆";done++;}
            catch(Exception ex){it.State="失败："+ex.Message;failed++;}Render();
        }
        status.Text=$"完成 {done} 张，失败 {failed} 张。";
    }
    private async Task Restore(ImageFileItem[] list)
    {
        foreach(var it in list){it.Output=null;it.Preview=ImageFiles.Decode(it.Original);it.State="已还原";await Auto(it);}
        status.Text="已还原原始图片"+(autoMode==0?"。":"，并按自动设置处理。");
    }
    private void Render()
    {
        cards.Children.Clear();
        emptyState.Visibility=items.Count==0?Visibility.Visible:Visibility.Collapsed;
        cardScroll.Visibility=items.Count==0?Visibility.Collapsed:Visibility.Visible;
        if(items.Count==0)return;
        foreach(var it in items)
        {
            var panel=new StackPanel();
            var image = new Image { Source=it.Preview, Height=185, Stretch=System.Windows.Media.Stretch.Uniform, Cursor=Cursors.Hand, ToolTip="双击放大查看" };
            image.MouseLeftButtonDown += (_,e) => { if(e.ClickCount==2) { previewImage.Source=it.Preview; previewOverlay.Visibility=Visibility.Visible; e.Handled=true; } };
            panel.Children.Add(new Border{Background=ImageFiles.Canvas,CornerRadius=new CornerRadius(8),Margin=new Thickness(0,0,0,10),Child=image});
            panel.Children.Add(ImageFiles.Label(it.Name,14));var detail=ImageFiles.Label($"{it.Preview.PixelWidth} × {it.Preview.PixelHeight} · {it.State}",12);detail.Foreground=ImageFiles.Muted;panel.Children.Add(detail);
            var actions=new WrapPanel();actions.Children.Add(ImageFiles.ToneButton("混淆",()=>Run(()=>Process(new[]{it},false)),ImageFiles.Hint,ImageFiles.Teal));
            actions.Children.Add(ImageFiles.ToneButton("解混淆",()=>Run(()=>Process(new[]{it},true)),ImageFiles.Hint,ImageFiles.Teal));
            actions.Children.Add(ImageFiles.ToneButton("还原",()=>Run(()=>Restore(new[]{it})),ImageFiles.Warm,ImageFiles.Ink));
            actions.Children.Add(ImageFiles.Button("复制图片",()=>Run(()=>{ImageFiles.Copy(it.Output??it.Original);status.Text="已复制："+it.Name;return Task.CompletedTask;})));
            var save=ImageFiles.Button("保存处理图",()=>Run(()=>{ImageFiles.Save(it,false);return Task.CompletedTask;}));save.IsEnabled=it.Output!=null;actions.Children.Add(save);
            var remove=ImageFiles.Button("删除",()=>{if(!busy){items.Remove(it);Render();}});remove.Foreground=ImageFiles.Coral;actions.Children.Add(remove);
            panel.Children.Add(actions);cards.Children.Add(new Border{Child=panel,Width=236,Padding=new Thickness(12),Margin=new Thickness(0,0,16,16),BorderBrush=ImageFiles.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Background=ImageFiles.Paper});
        }
        ImageFiles.FitCards(cards,cardScroll.ViewportWidth);
    }
}
