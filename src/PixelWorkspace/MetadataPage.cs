using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PixelWorkspace;

public sealed class MetadataPage : UserControl
{
    private readonly List<ImageFileItem> items=new();
    private readonly StackPanel root=new(){Margin=new Thickness(24)};
    private readonly WrapPanel cards=new();
    private readonly TextBlock status=ImageFiles.Label("请选择或拖入图片。");
    private readonly TextBox folder=new(){Text="metadata-cleaned",MaxLength=80,Width=180,Margin=new Thickness(8,0,8,8),Padding=new Thickness(6)};
    private bool busy;
    private readonly FrameworkElement controls;
    private readonly FrameworkElement emptyState;
    private readonly ScrollViewer cardScroll;
    public MetadataPage()
    {
        Background=ImageFiles.Paper;AllowDrop=true;
        var layout=new DockPanel{LastChildFill=true};
        var header=new WrapPanel();
        var copy=new StackPanel{Margin=new Thickness(0,0,28,0)};
        var title=ImageFiles.Label("▧  图片元数据去除",21);title.FontWeight=FontWeights.SemiBold;copy.Children.Add(title);
        var subtitle=ImageFiles.Label("清理图片附带信息，生成干净的 PNG 副本");subtitle.Foreground=ImageFiles.Muted;copy.Children.Add(subtitle);header.Children.Add(copy);
        var headerActions=new WrapPanel{VerticalAlignment=VerticalAlignment.Center};
        var downloadSelected=ImageFiles.Button("↓ 下载所选",()=>Export(false));
        var downloadAll=ImageFiles.Button("↓ 批量下载",()=>Export(true),true);
        foreach(var button in new[]{downloadSelected,downloadAll}){button.MinHeight=34;button.Padding=new Thickness(10,7,10,7);button.VerticalAlignment=VerticalAlignment.Center;headerActions.Children.Add(button);}
        header.Children.Add(headerActions);
        var heading=ImageFiles.Section(header,ImageFiles.Paper,new Thickness(24,22,16,14));DockPanel.SetDock(heading,Dock.Top);layout.Children.Add(heading);
        var bar=new WrapPanel();
        bar.Children.Add(ImageFiles.Button("选择图片",()=>Run(()=>AddFiles(ImageFiles.Pick())),true));
        bar.Children.Add(ImageFiles.Button("粘贴图片",Paste));
        bar.Children.Add(ImageFiles.Button("全选 / 取消",()=>{if(busy)return;bool value=!items.All(i=>i.Selected);foreach(var it in items)it.Selected=value;Render();}));
        bar.Children.Add(ImageFiles.Button("清理所选",()=>Run(()=>Clean(Chosen(false)))));
        bar.Children.Add(ImageFiles.ToneButton("清理全部",()=>Run(()=>Clean(Chosen(true))),ImageFiles.Success));
        bar.Children.Add(ImageFiles.Button("复制所选",()=>Run(()=>Copy(Chosen(false)))));
        bar.Children.Add(ImageFiles.Button("复制全部",()=>Run(()=>Copy(Chosen(true)))));
        var settings=new WrapPanel{Margin=new Thickness(0,0,0,8)};
        var folderLabel=new Label{Content="打包文件夹",Target=folder,Foreground=ImageFiles.Muted,VerticalAlignment=VerticalAlignment.Center};settings.Children.Add(folderLabel);
        folder.Margin=new Thickness(8,0,0,0);folder.Background=ImageFiles.Paper;folder.Foreground=ImageFiles.Ink;folder.BorderBrush=ImageFiles.Line;
        System.Windows.Automation.AutomationProperties.SetName(folder,"ZIP 内文件夹名称");settings.Children.Add(folder);bar.Children.Add(settings);
        var toolbar=ImageFiles.Section(bar,ImageFiles.Soft,new Thickness(24,16,16,8));DockPanel.SetDock(toolbar,Dock.Top);layout.Children.Add(toolbar);
        var hints=ImageFiles.HintStrip("① 添加图片","② 单张、勾选或全部清理","③ 复制结果或下载 ZIP");DockPanel.SetDock(hints,Dock.Top);layout.Children.Add(hints);
        var help=ImageFiles.Label("PNG 输出保留透明度，但文件可能变大；动图仅保留第一帧。连续复制仅保留最后一张，多图保存建议使用批量下载。",11);help.Foreground=ImageFiles.Muted;help.Margin=new Thickness(24,0,24,16);DockPanel.SetDock(help,Dock.Bottom);layout.Children.Add(help);
        var footer=ImageFiles.Footer(status,"元数据清理","输出 PNG · 本地处理");DockPanel.SetDock(footer,Dock.Bottom);layout.Children.Add(footer);
        root.Margin=new Thickness(0);root.Children.Add(cards);
        cardScroll=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        cardScroll.SizeChanged+=(_,_)=>ImageFiles.FitCards(cards,cardScroll.ViewportWidth);
        emptyState=ImageFiles.EmptyState(()=>Run(()=>AddFiles(ImageFiles.Pick())),"拖拽图片开始清理","支持一次选择或拖拽多张图片。\n通过本地重新编码，移除原图中的 EXIF、GPS、XMP 等信息。");
        var workspace=new Grid();workspace.Children.Add(cardScroll);workspace.Children.Add(emptyState);
        layout.Children.Add(new Border{Child=workspace,Margin=new Thickness(24,20,24,12),Padding=new Thickness(16),Background=ImageFiles.DottedCanvas(),BorderBrush=ImageFiles.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14)});
        Content=layout;
        controls=layout;
        DragOver+=(_,e)=>{e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};
        Drop+=(_,e)=>{if(e.Data.GetData(DataFormats.FileDrop)is string[] paths)Run(()=>AddFiles(paths));e.Handled=true;};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.V&&(Keyboard.Modifiers&ModifierKeys.Control)!=0&&Keyboard.FocusedElement is not TextBox){e.Handled=true;Paste();}};
        Render();
    }
    private ImageFileItem[] Chosen(bool all)=>items.Where(i=>all||i.Selected).ToArray();
    private async void Run(Func<Task> action)
    {
        if(busy)return;busy=true;controls.IsEnabled=false;
        try{await action();}catch(Exception ex){status.Text="操作失败："+ex.Message;}finally{busy=false;controls.IsEnabled=true;Render();}
    }
    private void Paste()=>Run(async()=>{
        if(Clipboard.ContainsFileDropList()){await AddFiles(Clipboard.GetFileDropList().Cast<string>().ToArray());return;}
        var item=ImageFiles.Paste();if(item==null){status.Text="剪贴板中没有图片。";return;}items.Add(item);status.Text=$"已载入 {items.Count} 张图片。";
    });
    public void ImportFile(string path)
    {
        if (System.IO.File.Exists(path)) Run(() => AddFiles(new[] { path }));
    }

    private async Task AddFiles(string[] paths)
    {
        int failed=0;string last="";
        foreach(string path in paths)
        {
            try{status.Text="正在载入："+System.IO.Path.GetFileName(path);items.Add(await Task.Run(()=>ImageFiles.Load(path)));Render();}
            catch(Exception ex){failed++;last=ex.Message;}
        }
        status.Text=$"已载入 {items.Count} 张图片。"+(failed>0?$" {failed} 张失败：{last}":"");
    }
    private async Task Clean(ImageFileItem[] list)
    {
        if(list.Length==0){status.Text="请先添加或选择图片。";return;}
        int done=0,failed=0;
        foreach(var it in list)
        {
            status.Text=$"正在清理：{it.Name} ({done+failed+1}/{list.Length})";
            try{it.Output=await Task.Run(()=>ImageFiles.Encode(ImageFiles.Decode(it.Original),true));it.Preview=ImageFiles.Decode(it.Output);it.State=$"已清理 · PNG · {Math.Round(it.Output.Length/1024.0)} KB";done++;}
            catch(Exception ex){it.State="失败："+ex.Message;failed++;}Render();
        }
        status.Text=$"处理完成 {done} 张，失败 {failed} 张。";
    }
    private Task Copy(ImageFileItem[] list)
    {
        var ready=list.Where(i=>i.Output!=null).ToArray();
        if(ready.Length==0){status.Text="没有已处理的图片可复制。";return Task.CompletedTask;}
        int n=0;foreach(var it in ready){ImageFiles.Copy(it.Output!);n++;}
        status.Text=$"已复制 {n} 张；系统剪贴板仅保留最后一张。多图保存请使用 ZIP。";return Task.CompletedTask;
    }
    private void Export(bool all)=>Run(()=>{int n=ImageFiles.Zip(Chosen(all),folder.Text,true);status.Text=n>0?$"已打包 {n} 张图片。":"已取消导出。";return Task.CompletedTask;});
    private void Render()
    {
        cards.Children.Clear();
        emptyState.Visibility=items.Count==0?Visibility.Visible:Visibility.Collapsed;
        cardScroll.Visibility=items.Count==0?Visibility.Collapsed:Visibility.Visible;
        if(items.Count==0)return;
        foreach(var it in items)
        {
            var panel=new StackPanel();panel.Children.Add(new Border{Background=ImageFiles.Canvas,CornerRadius=new CornerRadius(8),Margin=new Thickness(0,0,0,10),Child=new Image{Source=it.Preview,Height=170,Stretch=System.Windows.Media.Stretch.Uniform}});
            var check=new CheckBox{Content=new TextBlock{Text=it.Name,TextWrapping=TextWrapping.Wrap},IsChecked=it.Selected,Margin=new Thickness(0,0,0,10)};
            check.Checked+=(_,_)=>it.Selected=true;check.Unchecked+=(_,_)=>it.Selected=false;panel.Children.Add(check);
            var detail=ImageFiles.Label($"{it.Preview.PixelWidth} × {it.Preview.PixelHeight} · {it.State}",12);detail.Foreground=ImageFiles.Muted;panel.Children.Add(detail);
            var actions=new WrapPanel();actions.Children.Add(ImageFiles.Button(it.Output==null?"清除元数据":"重新清理",()=>Run(()=>Clean(new[]{it})),true));
            var copy=ImageFiles.Button("复制处理图",()=>Run(()=>Copy(new[]{it})));copy.IsEnabled=it.Output!=null;actions.Children.Add(copy);
            var save=ImageFiles.Button("保存 PNG",()=>Run(()=>{ImageFiles.Save(it,true);return Task.CompletedTask;}));save.IsEnabled=it.Output!=null;actions.Children.Add(save);
            var remove=ImageFiles.Button("删除",()=>{if(!busy){items.Remove(it);Render();}});remove.Foreground=ImageFiles.Coral;actions.Children.Add(remove);
            panel.Children.Add(actions);
            cards.Children.Add(new Border{Child=panel,Width=236,Padding=new Thickness(12),Margin=new Thickness(0,0,16,16),BorderBrush=ImageFiles.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Background=ImageFiles.Paper});
        }
        ImageFiles.FitCards(cards,cardScroll.ViewportWidth);
    }
}
