using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PixelWorkspace;

/// <summary>
/// Local file browser and preview surface. The host can subscribe to the transfer events
/// without coupling this page to any of the processing pages.
/// </summary>
public sealed class BrowserPage : UserControl
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".wdp", ".webp" };
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".mp4", ".m4v", ".mov", ".avi", ".wmv", ".mkv", ".webm", ".mpeg", ".mpg" };

    private readonly TextBlock pathLabel = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 8), Foreground = ImageFiles.Muted, TextWrapping = TextWrapping.NoWrap };
    private readonly ListBox fileList = new();
    private readonly TextBox searchBox = new();
    private string searchText = "";
    private readonly Image imagePreview = new() { Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
    private readonly MediaElement videoPreview = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Stop, ScrubbingEnabled = true, Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
    private readonly Button videoPlayPause = new();
    private readonly Slider videoSeek = new();
    private readonly TextBlock videoTime = new();
    private readonly Grid videoTransport = new() { Visibility = Visibility.Collapsed };
    private readonly DispatcherTimer videoClock = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool playing;
    private bool scrubbing;
    private bool suppressSeek;
    private readonly TextBlock previewPlaceholder = ImageFiles.Label("选择左侧的图片或视频开始预览。", 16);
    private readonly TextBlock imageDetails = ImageFiles.Label("", 12);
    private int previewRequest;
    private int entriesRequest;
    private Button upButton = null!;
    private Button transferEditor = null!;
    private Button transferObfuscation = null!;
    private Button transferMetadata = null!;
    private string? currentFolder;
    private string? selectedPath;
    private readonly DispatcherTimer autoRefresh = new() { Interval = TimeSpan.FromSeconds(5) };
    private string? folderSignature;
    private bool checkingFolder;

    public event Action<string>? TransferToEditor;
    public event Action<string>? TransferToObfuscation;
    public event Action<string>? TransferToMetadata;

    public BrowserPage()
    {
        Background = ImageFiles.Paper;
        Foreground = ImageFiles.Ink;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        FontSize = 13;

        var root = new Grid { Margin = new Thickness(20) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 160 });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
        Content = root;

        var browser = BuildBrowser();
        Grid.SetColumn(browser, 0);
        root.Children.Add(browser);

        var preview = BuildPreview();
        Grid.SetColumn(preview, 2);
        root.Children.Add(preview);

        var details = BuildDetails();
        Grid.SetColumn(details, 4);
        root.Children.Add(details);

        foreach (var column in new[] { 1, 3 })
        {
            var splitter = new GridSplitter
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = Brushes.Transparent,
                ResizeDirection = GridResizeDirection.Columns,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                Cursor = Cursors.SizeWE,
                ShowsPreview = false,
                DragIncrement = 1,
                KeyboardIncrement = 10
            };
            var line = new FrameworkElementFactory(typeof(Border));
            line.SetValue(Border.WidthProperty, 1d);
            line.SetValue(Border.BackgroundProperty, ImageFiles.Line);
            line.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            var hitArea = new FrameworkElementFactory(typeof(Border));
            hitArea.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            hitArea.AppendChild(line);
            splitter.Template = new ControlTemplate(typeof(GridSplitter)) { VisualTree = hitArea };
            Grid.SetColumn(splitter, column);
            root.Children.Add(splitter);
        }

        fileList.MouseDoubleClick += (_, _) => ActivateSelected();
        autoRefresh.Tick += (_, _) => _ = CheckFolderAsync();
        videoPreview.MediaFailed += (_, args) => ShowMessage($"视频无法预览：{args.ErrorException?.Message ?? "格式不受支持"}");
        videoPreview.MediaOpened += (_, _) =>
        {
            videoSeek.Maximum = DurationSeconds();
            SyncSeek();
            videoClock.Start();
            SetPlayButton(true);
            videoPreview.Play();
        };
        videoPreview.MediaEnded += (_, _) =>
        {
            videoClock.Stop();
            SetPlayButton(false);
            suppressSeek = true;
            videoSeek.Value = videoSeek.Maximum;
            suppressSeek = false;
            videoTime.Text = $"{FormatTime(videoSeek.Maximum)} / {FormatTime(videoSeek.Maximum)}";
        };
        videoClock.Tick += (_, _) => SyncSeek();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (videoPreview.Visibility != Visibility.Visible) return;
        if (System.Windows.Input.Keyboard.FocusedElement is TextBox) return;
        switch (e.Key)
        {
            case System.Windows.Input.Key.Space:
                TogglePlayback();
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Left:
                Nudge(-5);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Right:
                Nudge(5);
                e.Handled = true;
                break;
        }
    }

    /// <summary>Moves the play head by the given number of seconds, clamped to the clip.</summary>
    private void Nudge(double seconds)
    {
        if (videoPreview.Source == null) return;
        var target = videoPreview.Position.TotalSeconds + seconds;
        target = Math.Clamp(target, 0, Math.Max(0, DurationSeconds()));
        videoPreview.Position = TimeSpan.FromSeconds(target);
        SyncSeek();
    }

    private void TogglePlayback()
    {
        if (videoPreview.Source == null) return;
        if (playing) videoPreview.Pause();
        else videoPreview.Play();
        SetPlayButton(!playing);
    }

    private void SetPlayButton(bool isPlaying)
    {
        playing = isPlaying;
        videoPlayPause.Content = isPlaying ? "⏸  暂停" : "▶  播放";
        System.Windows.Automation.AutomationProperties.SetName(videoPlayPause, isPlaying ? "暂停" : "播放");
        if (isPlaying) videoClock.Start();
    }

    private double DurationSeconds()
    {
        var duration = videoPreview.NaturalDuration;
        return duration.HasTimeSpan ? Math.Max(0, duration.TimeSpan.TotalSeconds) : 0;
    }

    /// <summary>Copies the player position into the progress bar and the time label.</summary>
    private void SyncSeek()
    {
        if (scrubbing || suppressSeek) return;
        var duration = DurationSeconds();
        if (duration > 0 && Math.Abs(videoSeek.Maximum - duration) > 0.5)
        {
            suppressSeek = true;
            videoSeek.Maximum = duration;
            suppressSeek = false;
        }
        var position = Math.Clamp(videoPreview.Position.TotalSeconds, 0, Math.Max(videoSeek.Maximum, 0));
        suppressSeek = true;
        videoSeek.Value = position;
        suppressSeek = false;
        videoTime.Text = $"{FormatTime(position)} / {FormatTime(duration)}";
    }

    private static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
    }

    /// <summary>Sets the browser folder, useful when the parent already has a known path.</summary>
    public void OpenFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            ShowMessage("文件夹不存在或无法访问。");
            return;
        }
        currentFolder = Path.GetFullPath(folder);
        pathLabel.Text = currentFolder;
        RefreshEntries();
    }

    /// <summary>Saves the current path text so it survives closing without pressing Open.</summary>
    /// <summary>Loads a file into the preview and details panel.</summary>
    public void OpenFile(string file)
    {
        if (File.Exists(file))
        {
            if (!string.Equals(currentFolder, Path.GetDirectoryName(file), StringComparison.OrdinalIgnoreCase))
                OpenFolder(Path.GetDirectoryName(file)!);
            _ = SelectPathAsync(Path.GetFullPath(file));
        }
    }

    private FrameworkElement BuildBrowser()
    {
        var panel = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
        var title = ImageFiles.Label("文件浏览", 17);
        title.FontWeight = FontWeights.SemiBold;
        DockPanel.SetDock(title, Dock.Top);
        panel.Children.Add(title);

        DockPanel.SetDock(pathLabel, Dock.Top);
        panel.Children.Add(pathLabel);

        var navRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        upButton = ImageFiles.Button("↑ 上一级", NavigateUp);
        navRow.Children.Add(upButton);
        navRow.Children.Add(ImageFiles.Button("刷新", RefreshEntries));
        Loaded += (_, _) =>
        {
            autoRefresh.Start();
            if (currentFolder != null) return;
            var start = SettingsStore.LoadDefaultFolder() ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (Directory.Exists(start)) OpenFolder(start);
        };
        Unloaded += (_, _) => autoRefresh.Stop();
        DockPanel.SetDock(navRow, Dock.Top);
        panel.Children.Add(navRow);

        var searchHost = BuildSearch();
        DockPanel.SetDock(searchHost, Dock.Top);
        panel.Children.Add(searchHost);

        fileList.BorderBrush = ImageFiles.Line;
        fileList.Background = ImageFiles.Soft;
        fileList.Padding = new Thickness(4);
        fileList.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var item = new FrameworkElementFactory(typeof(StackPanel));
        item.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        item.SetValue(StackPanel.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        item.SetValue(StackPanel.MarginProperty, new Thickness(2));
        var icon = new FrameworkElementFactory(typeof(Image));
        icon.SetValue(Image.WidthProperty, 42d);
        icon.SetValue(Image.HeightProperty, 42d);
        icon.SetValue(Image.StretchProperty, Stretch.UniformToFill);
        icon.SetValue(Image.MarginProperty, new Thickness(0, 0, 8, 0));
        icon.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(BrowserEntry.Thumbnail)));
        item.AppendChild(icon);
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        label.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        label.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(BrowserEntry.DisplayName)));
        item.AppendChild(label);
        fileList.ItemTemplate = new DataTemplate(typeof(BrowserEntry)) { VisualTree = item };
        panel.Children.Add(fileList);
        return panel;
    }

    /// <summary>Filter box under the navigation buttons; narrows the list by file or folder name.</summary>
    private FrameworkElement BuildSearch()
    {
        var searchHost = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        searchBox.Height = 34;
        searchBox.Padding = new Thickness(10, 0, 10, 0);
        searchBox.VerticalContentAlignment = VerticalAlignment.Center;
        searchBox.BorderBrush = ImageFiles.Line;
        searchBox.BorderThickness = new Thickness(1);
        searchBox.Background = ImageFiles.Paper;
        searchBox.Foreground = ImageFiles.Ink;
        searchBox.FontSize = 13;
        System.Windows.Automation.AutomationProperties.SetName(searchBox, "搜索文件名");
        var placeholder = new TextBlock
        {
            Text = "搜索文件名…",
            Foreground = ImageFiles.Muted,
            Margin = new Thickness(11, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        searchHost.Children.Add(searchBox);
        searchHost.Children.Add(placeholder);
        searchHost.Children.Add(ClearSearchButton(placeholder));
        searchBox.TextChanged += (_, _) =>
        {
            placeholder.Visibility = searchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var text = searchBox.Text.Trim();
            if (string.Equals(text, searchText, StringComparison.Ordinal)) return;
            searchText = text;
            ApplyEntries();
        };
        searchBox.Tag = placeholder;
        Grid.SetZIndex(placeholder, 1);
        return searchHost;
    }

    /// <summary>Small ✕ next to the filter box that clears the query.</summary>
    private FrameworkElement ClearSearchButton(TextBlock placeholder)
    {
        var clear = new Button
        {
            Content = "✕",
            Width = 24,
            Height = 24,
            Margin = new Thickness(0, 0, 6, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ImageFiles.Muted,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Visibility = Visibility.Collapsed
        };
        System.Windows.Automation.AutomationProperties.SetName(clear, "清除搜索");
        clear.Click += (_, _) =>
        {
            searchBox.Clear();
            searchBox.Focus();
        };
        searchBox.TextChanged += (_, _) => clear.Visibility = searchBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        return clear;
    }

    private FrameworkElement BuildPreview()
    {
        var panel = new Grid { Margin = new Thickness(12, 0, 12, 10) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = ImageFiles.Label("预览", 17);
        title.FontWeight = FontWeights.SemiBold;
        Grid.SetRow(title, 0);
        panel.Children.Add(title);
        var frame = new Border { Background = ImageFiles.Canvas, BorderBrush = ImageFiles.Line, BorderThickness = new Thickness(1), Padding = new Thickness(12), ClipToBounds = true };
        var surface = new Grid();
        surface.Children.Add(imagePreview);
        surface.Children.Add(videoPreview);
        previewPlaceholder.Foreground = ImageFiles.Muted;
        previewPlaceholder.HorizontalAlignment = HorizontalAlignment.Center;
        previewPlaceholder.VerticalAlignment = VerticalAlignment.Center;
        previewPlaceholder.TextAlignment = TextAlignment.Center;
        surface.Children.Add(previewPlaceholder);
        frame.Child = surface;
        BuildTransport();
        Grid.SetRow(frame, 1);
        panel.Children.Add(frame);
        Grid.SetRow(videoTransport, 2);
        panel.Children.Add(videoTransport);
        return panel;
    }

    /// <summary>Teal played portion and grey remainder, so the progress is readable at a glance.</summary>
    private const string SeekTemplate = @"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Slider'>
        <Grid Height='18' Background='Transparent'>
            <Track x:Name='PART_Track'>
                <Track.DecreaseRepeatButton>
                    <RepeatButton Command='Slider.DecreaseLarge' Focusable='False' IsTabStop='False'>
                        <RepeatButton.Template>
                            <ControlTemplate TargetType='RepeatButton'>
                                <Border Background='#287F7A' CornerRadius='3' Height='6' VerticalAlignment='Center'/>
                            </ControlTemplate>
                        </RepeatButton.Template>
                    </RepeatButton>
                </Track.DecreaseRepeatButton>
                <Track.IncreaseRepeatButton>
                    <RepeatButton Command='Slider.IncreaseLarge' Focusable='False' IsTabStop='False'>
                        <RepeatButton.Template>
                            <ControlTemplate TargetType='RepeatButton'>
                                <Border Background='#DCE5E2' CornerRadius='3' Height='6' VerticalAlignment='Center'/>
                            </ControlTemplate>
                        </RepeatButton.Template>
                    </RepeatButton>
                </Track.IncreaseRepeatButton>
                <Track.Thumb>
                    <Thumb Width='12' Height='12'>
                        <Thumb.Template>
                            <ControlTemplate TargetType='Thumb'>
                                <Grid>
                                    <Ellipse Fill='White' Stroke='#287F7A' StrokeThickness='2.5'/>
                                </Grid>
                            </ControlTemplate>
                        </Thumb.Template>
                    </Thumb>
                </Track.Thumb>
            </Track>
        </Grid>
    </ControlTemplate>";

    /// <summary>Playback strip: pause/resume, a draggable progress bar and elapsed/total time.</summary>
    private FrameworkElement BuildTransport()
    {
        videoPlayPause.Content = "⏸  暂停";
        videoPlayPause.Padding = new Thickness(12, 6, 12, 6);
        videoPlayPause.MinHeight = 32;
        videoPlayPause.Margin = new Thickness(0, 0, 10, 0);
        videoPlayPause.Background = ImageFiles.Paper;
        videoPlayPause.Foreground = ImageFiles.Ink;
        videoPlayPause.BorderBrush = ImageFiles.Line;
        videoPlayPause.BorderThickness = new Thickness(1);
        videoPlayPause.Cursor = System.Windows.Input.Cursors.Hand;
        System.Windows.Automation.AutomationProperties.SetName(videoPlayPause, "暂停");
        videoPlayPause.Click += (_, _) => TogglePlayback();

        videoSeek.Minimum = 0;
        videoSeek.Maximum = 1;
        videoSeek.SmallChange = 1;
        videoSeek.LargeChange = 10;
        videoSeek.IsMoveToPointEnabled = true;
        videoSeek.Height = 18;
        videoSeek.Background = Brushes.Transparent;
        videoSeek.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(SeekTemplate);
        videoSeek.VerticalAlignment = VerticalAlignment.Center;
        videoSeek.Margin = new Thickness(0, 0, 12, 0);
        videoSeek.ValueChanged += (_, _) =>
        {
            if (suppressSeek) return;
            var seconds = Math.Clamp(videoSeek.Value, 0, Math.Max(0, DurationSeconds()));
            videoTime.Text = $"{FormatTime(scrubbing ? videoSeek.Value : seconds)} / {FormatTime(DurationSeconds())}";
            if (scrubbing) return;
            if (Math.Abs(videoPreview.Position.TotalSeconds - videoSeek.Value) > 0.2) videoPreview.Position = TimeSpan.FromSeconds(videoSeek.Value);
        };
        videoSeek.PreviewMouseLeftButtonDown += (_, _) => scrubbing = true;
        videoSeek.PreviewMouseLeftButtonUp += (_, _) =>
        {
            scrubbing = false;
            videoPreview.Position = TimeSpan.FromSeconds(Math.Clamp(videoSeek.Value, 0, Math.Max(0, DurationSeconds())));
            SyncSeek();
        };
        videoSeek.LostMouseCapture += (_, _) => scrubbing = false;

        videoTime.Text = "0:00 / 0:00";
        videoTime.FontSize = 12;
        videoTime.Foreground = ImageFiles.Muted;
        videoTime.VerticalAlignment = VerticalAlignment.Center;
        videoTime.MinWidth = 88;
        videoTime.TextAlignment = TextAlignment.Right;

        videoTransport.Margin = new Thickness(0, 10, 0, 0);
        videoTransport.MinHeight = 36;
        videoTransport.HorizontalAlignment = HorizontalAlignment.Stretch;
        videoTransport.Background = Brushes.Transparent;
        videoTransport.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        videoTransport.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        videoTransport.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(videoPlayPause, 0);
        Grid.SetColumn(videoSeek, 1);
        Grid.SetColumn(videoTime, 2);
        videoTransport.Children.Add(videoPlayPause);
        videoTransport.Children.Add(videoSeek);
        videoTransport.Children.Add(videoTime);
        return videoTransport;
    }

    private FrameworkElement BuildDetails()
    {
        var panel = new DockPanel { Margin = new Thickness(12, 0, 0, 0) };
        var title = ImageFiles.Label("文件详情", 17);
        title.FontWeight = FontWeights.SemiBold;
        DockPanel.SetDock(title, Dock.Top);
        panel.Children.Add(title);

        var actionPanel = new StackPanel();
        imageDetails.Foreground = ImageFiles.Ink;
        imageDetails.LineHeight = 21;
        imageDetails.Margin = new Thickness(0, 8, 0, 14);
        actionPanel.Children.Add(imageDetails);
        transferEditor = ImageFiles.Button("转到图片编辑", () => Transfer(TransferToEditor), true);
        transferObfuscation = ImageFiles.Button("转到图片混淆", () => Transfer(TransferToObfuscation));
        transferMetadata = ImageFiles.Button("转到元数据清理", () => Transfer(TransferToMetadata));
        actionPanel.Children.Add(transferEditor);
        actionPanel.Children.Add(transferObfuscation);
        actionPanel.Children.Add(transferMetadata);
        panel.Children.Add(actionPanel);

        SetTransferEnabled(false);
        return panel;
    }

    private void RefreshEntries()
    {
        if (string.IsNullOrWhiteSpace(currentFolder) || !Directory.Exists(currentFolder))
        {
            ShowMessage("请输入有效文件夹路径。");
            return;
        }
        ApplyEntries();
    }

    /// <summary>Watches the folder every five seconds so freshly generated files appear without pressing 刷新.</summary>
    private async Task CheckFolderAsync()
    {
        if (checkingFolder) return;
        if (currentFolder is not { } folder || !Directory.Exists(folder)) return;
        checkingFolder = true;
        try
        {
            var signature = await Task.Run(() => FolderSignature(folder));
            if (signature == null) return;
            if (string.Equals(signature, folderSignature, StringComparison.Ordinal)) return;
            if (!string.Equals(currentFolder, folder, StringComparison.OrdinalIgnoreCase)) return;
            ApplyEntries();
        }
        catch { }
        finally { checkingFolder = false; }
    }

    private void ApplyEntries()
    {
        var folder = currentFolder;
        if (folder == null) return;
        try
        {
            var previousPath = (fileList.SelectedItem as BrowserEntry)?.Path ?? selectedPath;
            var previousOffset = FindScrollViewer(fileList)?.VerticalOffset ?? 0;
            var request = ++entriesRequest;
            var known = new Dictionary<string, BrowserEntry>(StringComparer.OrdinalIgnoreCase);
            if (fileList.ItemsSource is IEnumerable<BrowserEntry> existing)
                foreach (var entry in existing) known[entry.Path] = entry;

            var entries = new List<BrowserEntry>();
            var pending = new List<string>();
            var filter = searchText;
            bool Match(string name) => filter.Length == 0 || name.Contains(filter, StringComparison.OrdinalIgnoreCase);
            var folders = Directory.EnumerateDirectories(folder).Select(x => Path.GetFileName(x) ?? "").Where(Match).OrderBy(x => x).Select(x => Path.Combine(folder, x)).ToArray();
            var files = Directory.EnumerateFiles(folder).Where(IsSupported).Where(x => Match(Path.GetFileName(x))).OrderBy(Path.GetFileName).ToArray();
            foreach (var dir in folders)
            {
                if (known.Remove(dir, out var cached)) { entries.Add(cached); continue; }
                entries.Add(new BrowserEntry(dir, Path.GetFileName(dir), true, CreateFolderThumbnail()));
            }
            foreach (var file in files)
            {
                var stamp = StampOf(file);
                if (known.Remove(file, out var cached))
                {
                    if (cached.Stamp != stamp)
                    {
                        cached.Stamp = stamp;
                        cached.Thumbnail = IsVideo(file) ? CreateVideoThumbnail() : null;
                        pending.Add(file);
                    }
                    entries.Add(cached);
                    continue;
                }
                entries.Add(new BrowserEntry(file, Path.GetFileName(file), false, IsVideo(file) ? CreateVideoThumbnail() : null) { Stamp = stamp });
                pending.Add(file);
            }

            fileList.ItemsSource = entries;
            folderSignature = FolderSignature(folder);
            upButton.IsEnabled = Directory.GetParent(folder) != null;
            if (pending.Count > 0) _ = LoadThumbnailsAsync(pending.ToArray(), entries, request);
            if (previousPath != null || previousOffset > 0)
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => RestoreView(entries, previousPath, previousOffset)));
        }
        catch (Exception ex) { ShowMessage($"无法读取文件夹：{ex.Message}"); }
    }

    private void RestoreView(List<BrowserEntry> entries, string? path, double offset)
    {
        if (path != null)
        {
            var match = entries.FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
            if (match != null && !ReferenceEquals(fileList.SelectedItem, match)) fileList.SelectedItem = match;
        }
        if (offset > 0) FindScrollViewer(fileList)?.ScrollToVerticalOffset(offset);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (FindScrollViewer(child) is { } found) return found;
        }
        return null;
    }

    private static long StampOf(string path)
    {
        try { var info = new FileInfo(path); return info.Length ^ info.LastWriteTimeUtc.Ticks; }
        catch { return 0; }
    }

    private static string? FolderSignature(string folder)
    {
        try
        {
            var parts = new List<string>();
            foreach (var dir in Directory.EnumerateDirectories(folder)) parts.Add("D|" + Path.GetFileName(dir));
            foreach (var file in Directory.EnumerateFiles(folder).Where(IsSupported)) parts.Add($"F|{Path.GetFileName(file)}|{StampOf(file)}");
            parts.Sort(StringComparer.Ordinal);
            return string.Join("\n", parts);
        }
        catch { return null; }
    }

    private void NavigateUp()
    {
        if (currentFolder != null && Directory.GetParent(currentFolder) is DirectoryInfo parent) OpenFolder(parent.FullName);
    }

    private void ActivateSelected()
    {
        if (fileList.SelectedItem is not BrowserEntry entry) return;
        if (entry.IsDirectory) OpenFolder(entry.Path); else _ = SelectPathAsync(entry.Path);
    }

    private void SelectEntry(BrowserEntry? entry)
    {
        if (entry != null && !entry.IsDirectory) _ = SelectPathAsync(entry.Path);
    }

    private async Task SelectPathAsync(string path)
    {
        var request = ++previewRequest;
        selectedPath = path;
        if (IsVideo(path))
        {
            previewPlaceholder.Visibility = Visibility.Collapsed;
            imagePreview.Visibility = Visibility.Collapsed;
            videoPreview.Stop();
            videoClock.Stop();
            suppressSeek = true;
            videoSeek.Value = 0;
            videoSeek.Maximum = Math.Max(1, videoSeek.Maximum);
            suppressSeek = false;
            videoTime.Text = "0:00 / 0:00";
            SetPlayButton(false);
            videoPreview.Source = new Uri(path);
            videoPreview.Visibility = Visibility.Visible;
            videoTransport.Visibility = Visibility.Visible;
            imageDetails.Text = $"名称：{Path.GetFileName(path)}\n大小：{FormatSize(new FileInfo(path).Length)}\n尺寸：视频\n元数据：无";
            SetTransferEnabled(false);
            return;
        }
        if (!IsImage(path)) return;
        SetTransferEnabled(false);
        try
        {
            var result = await Task.Run(() =>
            {
                var source = LoadImage(path);
                var info = new FileInfo(path);
                var dimensions = GetImageDimensions(path);
                var hasMetadata = HasImageMetadata(path);
                return (source, info.Length, dimensions.Width, dimensions.Height, hasMetadata);
            });
            if (request != previewRequest || !string.Equals(selectedPath, path, StringComparison.OrdinalIgnoreCase)) return;
            previewPlaceholder.Visibility = Visibility.Collapsed;
            videoPreview.Stop(); videoPreview.Visibility = Visibility.Collapsed;
            videoClock.Stop(); SetPlayButton(false); videoTransport.Visibility = Visibility.Collapsed;
            imagePreview.Source = result.source; imagePreview.Visibility = Visibility.Visible;
            imageDetails.Text = $"名称：{Path.GetFileName(path)}\n大小：{FormatSize(result.Length)}\n尺寸：{result.Width} × {result.Height}\n元数据：{(result.hasMetadata ? "有" : "无")}";
            SetTransferEnabled(true);
        }
        catch (Exception ex) { if (request == previewRequest) ShowMessage($"无法预览此文件：{ex.Message}"); }
    }

    private void Transfer(Action<string>? callback)
    {
        if (selectedPath == null) return;
        callback?.Invoke(selectedPath);
    }

    private void ClearPreview()
    {
        videoPreview.Stop();
        videoClock.Stop();
        SetPlayButton(false);
        videoTransport.Visibility = Visibility.Collapsed;
        imagePreview.Source = null;
        imageDetails.Text = "";
        imagePreview.Visibility = Visibility.Collapsed;
        videoPreview.Visibility = Visibility.Collapsed;
        previewPlaceholder.Visibility = Visibility.Visible;
        SetTransferEnabled(false);
    }

    private void ShowMessage(string message)
    {
        if (selectedPath == null) ClearPreview();
    }

    private void SetTransferEnabled(bool enabled)
    {
        transferEditor.IsEnabled = enabled;
        transferObfuscation.IsEnabled = enabled;
        transferMetadata.IsEnabled = enabled;
    }

    private static BitmapImage LoadImage(string path)
    {
        var image = new BitmapImage();
        image.BeginInit(); image.UriSource = new Uri(path); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 1600; image.EndInit(); image.Freeze();
        return image;
    }

    private async Task LoadThumbnailsAsync(string[] files, List<BrowserEntry> entries, int request)
    {
        foreach (var file in files)
        {
            var isImage = IsImage(file);
            var thumbnail = await Task.Run(() => isImage ? (ImageSource?)LoadListThumbnail(file) : LoadVideoThumbnail(file));
            if (request != entriesRequest || !string.Equals(currentFolder, Path.GetDirectoryName(file), StringComparison.OrdinalIgnoreCase)) return;
            var entry = entries.FirstOrDefault(x => string.Equals(x.Path, file, StringComparison.OrdinalIgnoreCase));
            if (entry == null || thumbnail == null) continue;
            entry.Thumbnail = thumbnail;
        }
    }

    /// <summary>Asks the Windows shell for the poster frame so videos show a real picture instead of the play badge.</summary>
    private static ImageSource? LoadVideoThumbnail(string path)
    {
        var handle = IntPtr.Zero;
        try
        {
            var iid = ShellThumbnailFactoryId;
            ShellThumbnail.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
            if (factory is not IShellItemImageFactory shell) return null;
            shell.GetImage(new ShellSize { cx = 96, cy = 96 }, ShellThumbnailBiggerSizeOk | ShellThumbnailOnly, out handle);
            if (handle == IntPtr.Zero) return null;
            var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch { return null; }
        finally { if (handle != IntPtr.Zero) DeleteObject(handle); }
    }

    private static BitmapImage? LoadListThumbnail(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 56;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }

    private static bool IsSupported(string path) => IsImage(path) || IsVideo(path);
    private static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path));
    private static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path));
    private static string FormatSize(long bytes) => bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? $"{bytes / 1024d:0.0} KB" : $"{bytes / 1024d / 1024d:0.0} MB";

    private static (int Width, int Height) GetImageDimensions(string path)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnLoad);
        return (frame.PixelWidth, frame.PixelHeight);
    }

    private static bool HasImageMetadata(string path)
    {
        try { return !ImageGenerationMetadata.Parse(path).IsEmpty; }
        catch { return false; }
    }

    private static DrawingImage CreateFolderThumbnail()
    {
        var drawing = new System.Windows.Media.DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(ImageFiles.Canvas, new Pen(ImageFiles.Line, 1), new RectangleGeometry(new Rect(0, 0, 56, 56))));
        drawing.Children.Add(new GeometryDrawing(ImageFiles.Muted, null, Geometry.Parse("M 10,18 L 23,18 L 27,22 L 46,22 L 46,40 L 10,40 Z")));
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }

    private static DrawingImage CreateVideoThumbnail()
    {
        var drawing = new System.Windows.Media.DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(ImageFiles.Canvas, new Pen(ImageFiles.Line, 1), new RectangleGeometry(new Rect(0, 0, 56, 56))));
        drawing.Children.Add(new GeometryDrawing(ImageFiles.Muted, null, new EllipseGeometry(new Point(28, 25), 15, 15)));
        drawing.Children.Add(new GeometryDrawing(ImageFiles.Paper, null, Geometry.Parse("M 24,17 L 24,33 L 37,25 Z")));
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }

    private const int ShellThumbnailBiggerSizeOk = 0x1;
    private const int ShellThumbnailOnly = 0x8;
    private static readonly Guid ShellThumbnailFactoryId = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    [StructLayout(LayoutKind.Sequential)]
    private struct ShellSize
    {
        public int cx;
        public int cy;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(ShellSize size, int flags, out IntPtr bitmap);
    }

    private static class ShellThumbnail
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        internal static extern void SHCreateItemFromParsingName(string path, IntPtr bind, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object factory);
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    private sealed class BrowserEntry : INotifyPropertyChanged
    {
        public BrowserEntry(string path, string displayName, bool isDirectory, ImageSource? thumbnail)
        { Path = path; DisplayName = displayName; IsDirectory = isDirectory; Thumbnail = thumbnail; }
        public string Path { get; }
        public string DisplayName { get; }
        public bool IsDirectory { get; }
        public long Stamp { get; set; }
        private ImageSource? thumbnail;
        public ImageSource? Thumbnail
        {
            get => thumbnail;
            set { if (ReferenceEquals(thumbnail, value)) return; thumbnail = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
