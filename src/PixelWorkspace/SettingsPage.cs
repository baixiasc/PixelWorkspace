using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelWorkspace;

public sealed class SettingsPage : UserControl
{
    public event Action<string>? DefaultFolderChanged;
    private readonly TextBox folder = new();
    private readonly TextBlock status = ImageFiles.Label("", 12);

    public SettingsPage()
    {
        Background = ImageFiles.Paper;
        Foreground = ImageFiles.Ink;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        FontSize = 13;

        var layout = new StackPanel { Margin = new Thickness(34), MaxWidth = 760 };
        var title = ImageFiles.Label("设置", 22);
        title.FontWeight = FontWeights.SemiBold;
        layout.Children.Add(title);
        var subtitle = ImageFiles.Label("图片浏览、图片编辑、图片混淆、去除元数据和手机查看都使用这个默认文件夹：打开文件、保存、导出 ZIP 时会从这里开始，手机查看也会默认共享这里。", 13);
        subtitle.Foreground = ImageFiles.Muted;
        subtitle.Margin = new Thickness(0, 8, 0, 24);
        layout.Children.Add(subtitle);

        layout.Children.Add(ImageFiles.Label("默认图片路径", 15));
        var row = new Grid { Margin = new Thickness(0, 8, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        folder.Text = SettingsStore.LoadDefaultFolder() ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        folder.Padding = new Thickness(9, 6, 9, 6);
        folder.BorderBrush = ImageFiles.Line;
        folder.Background = ImageFiles.Paper;
        row.Children.Add(folder);
        var browse = ImageFiles.Button("选择文件夹", ChooseFolder, true);
        browse.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(browse, 1);
        row.Children.Add(browse);
        layout.Children.Add(row);

        var actions = new WrapPanel();
        actions.Children.Add(ImageFiles.Button("保存默认路径", Save, true));
        var reset = ImageFiles.Button("恢复系统默认", Reset);
        reset.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(reset);
        layout.Children.Add(actions);
        status.Foreground = ImageFiles.Muted;
        status.Margin = new Thickness(0, 14, 0, 0);
        layout.Children.Add(status);
        Content = layout;
    }

    private void ChooseFolder()
    {
        var dialog = new Window
        {
            Title = "选择默认图片文件夹",
            Width = 560,
            Height = 430,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            Background = ImageFiles.Paper,
            ResizeMode = ResizeMode.CanResize
        };
        var folders = new ListBox { Margin = new Thickness(16), BorderBrush = ImageFiles.Line };
        var current = Directory.Exists(folder.Text) ? Path.GetFullPath(folder.Text) : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        void Load(string path)
        {
            if (!Directory.Exists(path)) return;
            current = path;
            folders.Items.Clear();
            foreach (var dir in Directory.EnumerateDirectories(path).OrderBy(Path.GetFileName)) folders.Items.Add(dir);
        }
        var up = ImageFiles.Button("↑ 上一级", () => { if (Directory.GetParent(current) is DirectoryInfo parent) Load(parent.FullName); });
        var open = ImageFiles.Button("选择此文件夹", () => { folder.Text = current; dialog.DialogResult = true; });
        var buttons = new DockPanel { Margin = new Thickness(16, 0, 16, 16) };
        DockPanel.SetDock(up, Dock.Left); buttons.Children.Add(up); DockPanel.SetDock(open, Dock.Right); buttons.Children.Add(open);
        var root = new DockPanel(); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons); root.Children.Add(folders); dialog.Content = root;
        folders.MouseDoubleClick += (_, _) => { if (folders.SelectedItem is string path) Load(path); };
        Load(current);
        dialog.ShowDialog();
    }

    private void Save()
    {
        if (!Directory.Exists(folder.Text))
        {
            status.Text = "请选择有效的文件夹路径。";
            return;
        }
        if (SettingsStore.SaveDefaultFolder(folder.Text, out var error))
        {
            folder.Text = Path.GetFullPath(folder.Text.Trim().Trim('"'));
            status.Text = "默认图片路径已保存。";
            DefaultFolderChanged?.Invoke(folder.Text);
        }
        else status.Text = $"路径保存失败：{error}";
    }

    private void Reset()
    {
        SettingsStore.ClearDefaultFolder();
        folder.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        status.Text = "已恢复系统默认路径。";
        DefaultFolderChanged?.Invoke(folder.Text);
    }
}
