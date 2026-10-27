using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace PixelWorkspace;

public sealed class EditorPage : UserControl
{
    // Match the HTML editor's final balanced-palette overrides, not its original purple theme.
    private static readonly Brush Teal = BrushOf("#287F7A"), Paper = BrushOf("#FFFEFA"), Pale = BrushOf("#E2F1EB"), Coral = BrushOf("#C9665B"), Ink = BrushOf("#20343A"), Muted = BrushOf("#687B7D"), Line = BrushOf("#D9E3DF"), Success = BrushOf("#3C9272"), Amber = BrushOf("#F7EAD9");
    private enum Tool { View, Crop, Mosaic, Mask, Sticker }
    private readonly List<EditorDocument> documents = new();
    private readonly List<EditorSticker> stickers = new();
    private readonly Dictionary<Tool, Button> modeButtons = new();
    private readonly List<Button> imageButtons = new();
    private readonly HashSet<long> painted = new();
    private EditorDocument? active;
    private EditorSticker? selected;
    private Tool mode;
    private readonly Canvas surface = new() { Background = Brushes.Transparent, ClipToBounds = true };
    private readonly Grid viewport = new() { ClipToBounds = true, Background = BrushOf("#F5F6F8"), MinHeight = 120 };
    private readonly Image image = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly TextBlock status = new() { Foreground = Muted, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock badge = new() { Foreground = Teal, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 16, 0) };
    private readonly TextBlock batchTitle = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(10) };
    private readonly StackPanel cards = new();
    private readonly Border batchPanel;
    private readonly StackPanel empty;
    private readonly WrapPanel mosaicParams = new(), maskParams = new(), stickerParams = new(), cropParams = new();
    private readonly ComboBox method = new() { Width = 115, Margin = new Thickness(6), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox colorInput = new() { Text = "#000000", Width = 85, Margin = new Thickness(6), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "输入 #RRGGBB 颜色" };
    private readonly TextBlock stickerStatus = new() { Margin = new Thickness(8), Foreground = Muted, VerticalAlignment = VerticalAlignment.Center };
    private readonly Slider size, strength, blur, opacity, stickerScale, stickerRotation;
    private readonly Button undoButton, redoButton, applyStickerButton, cancelStickerButton;
    private readonly Button resetMenu, copyMenu, exportAllButton, clearButton;
    private readonly UIElement strengthGroup, blurGroup;
    private bool down, panning, syncing;
    private Point start, last, dragOffset, panStart, panOrigin;
    private Point? hover;
    private Rect? crop;
    private double zoom = 1, fit = 1, panX, panY;
    private Color maskColor = Colors.Black;

    public EditorPage()
    {
        Background = Paper; Foreground = Ink; FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); FontSize = 13; Focusable = true; AllowDrop = true;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;
        var header = new WrapPanel { Margin = new Thickness(24, 22, 24, 19) };
        var heading = new StackPanel { Margin = new Thickness(0, 0, 24, 3), VerticalAlignment = VerticalAlignment.Center };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(14), Background = BrushOf("#E3F1ED"), Margin = new Thickness(0, 0, 10, 0), Child = new TextBlock { Text = "🖼️", FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        title.Children.Add(new TextBlock { Text = "图片编辑器", FontSize = 21, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        heading.Children.Add(title);
        heading.Children.Add(new TextBlock { Text = "裁剪、打码、遮罩、旋转贴纸都在本地完成", Foreground = Muted, FontSize = 13, Margin = new Thickness(0, 7, 0, 0) });
        header.Children.Add(heading);
        var actions = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) }; header.Children.Add(actions);
        resetMenu = Add(actions, "↺ 重置当前", Reset); resetMenu.Background = Amber; resetMenu.Foreground = BrushOf("#8A6238");
        var export = Add(actions, "↓ 下载当前", Export, Success, true); export.ToolTip = "导出 PNG · Ctrl+S";
        copyMenu = Add(actions, "📋 复制当前", () => Copy(active)); copyMenu.ToolTip = "Ctrl+C";
        exportAllButton = Add(actions, "↓ 批量下载", ExportAll, Teal); exportAllButton.ToolTip = "将全部图片导出为 ZIP";
        clearButton = Add(actions, "⌫ 清空", Clear);
        foreach (Button action in actions.Children) { action.FontSize = 12; action.Padding = new Thickness(10, 7, 10, 7); action.MinHeight = 34; }
        root.Children.Add(header);

        var toolbar = new StackPanel { Background = BrushOf("#F6F8F5") };
        var toolbarBorder = new Border { Child = toolbar, BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 1), Padding = new Thickness(21, 13, 21, 13), Background = BrushOf("#F6F8F5") }; Grid.SetRow(toolbarBorder, 1); root.Children.Add(toolbarBorder);
        var modes = new WrapPanel(); toolbar.Children.Add(modes);
        var choose = Add(modes, "📁 选择图片", () => Choose(false), Teal); choose.ToolTip = "选择图片 · Ctrl+O";
        modes.Children.Add(ToolDivider());
        foreach (var pair in new[] { (Tool.Crop, "✂ 裁剪"), (Tool.Mosaic, "▧ 打码"), (Tool.Mask, "🎭 遮罩"), (Tool.Sticker, "🧩 贴纸") })
        { var tool = pair.Item1; modeButtons[tool] = Add(modes, pair.Item2, () => SetMode(tool), required: true); }
        modes.Children.Add(ToolDivider());
        var history = new WrapPanel(); modes.Children.Add(history);
        undoButton = Add(history, "↶ 撤销", Undo); undoButton.ToolTip = "Ctrl+Z";
        redoButton = Add(history, "↷ 重做", Redo); redoButton.ToolTip = "Ctrl+Y";
        var viewTools = new WrapPanel(); modes.Children.Add(viewTools);
        modeButtons[Tool.View] = Add(viewTools, "查看", () => SetMode(Tool.View), required: true);
        Add(viewTools, "适应", Fit).ToolTip = "适应画布"; Add(viewTools, "100%", () => { zoom = 1 / Math.Max(.001, fit); panX = panY = 0; LayoutSurface(); RefreshStatus(); });
        foreach (Button action in history.Children) { action.FontSize = 12; action.Padding = new Thickness(8, 8, 8, 8); }
        foreach (Button action in viewTools.Children) { action.FontSize = 12; action.Padding = new Thickness(8, 8, 8, 8); }
        var parameters = new StackPanel(); toolbar.Children.Add(parameters);
        parameters.Children.Add(cropParams); parameters.Children.Add(mosaicParams); parameters.Children.Add(maskParams); parameters.Children.Add(stickerParams);
        Add(cropParams, "应用裁剪", ApplyCrop, Teal); Add(cropParams, "取消", Cancel);
        cropParams.Children.Add(new TextBlock { Text = "拖动框选", ToolTip = "Enter 应用 · Esc 取消", Margin = new Thickness(8), Foreground = Muted });
        method.Items.Add("马赛克"); method.Items.Add("高斯模糊"); method.SelectedIndex = 0; mosaicParams.Children.Add(method);
        mosaicParams.Children.Add(SliderGroup("范围", 4, 140, 36, 2, out size));
        strengthGroup = SliderGroup("强度 %", 10, 100, 100, 5, out strength); mosaicParams.Children.Add(strengthGroup);
        blurGroup = SliderGroup("模糊", .4, 14, 3, .2, out blur); mosaicParams.Children.Add(blurGroup);
        method.SelectionChanged += (_, _) => { UpdateControls(); RenderOverlay(); };
        maskParams.Children.Add(SliderGroup("透明度 %", 5, 80, 35, 1, out opacity));
        maskParams.Children.Add(new TextBlock { Text = "颜色", Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center }); maskParams.Children.Add(colorInput);
        foreach (string hex in new[] { "#000000", "#ffffff", "#287f7a", "#c9665b", "#f7ead9" })
        { string value = hex; var swatch = Add(maskParams, " ", () => colorInput.Text = value, BrushOf(hex)); swatch.MinWidth = 25; swatch.Width = 25; swatch.Padding = new Thickness(0); swatch.ToolTip = hex; }
        colorInput.TextChanged += (_, _) => { try { if (colorInput.Text.Length != 7 || !colorInput.Text.StartsWith('#')) throw new FormatException(); maskColor = (Color)ColorConverter.ConvertFromString(colorInput.Text); colorInput.BorderBrush = Line; } catch { colorInput.BorderBrush = Coral; } };
        Add(stickerParams, "上传贴纸", () => Choose(true), required: true);
        stickerParams.Children.Add(SliderGroup("缩放 %", 10, 100, 80, 1, out stickerScale));
        stickerParams.Children.Add(SliderGroup("旋转 °", 0, 360, 0, 1, out stickerRotation));
        applyStickerButton = Add(stickerParams, "粘贴全部", ApplyStickers, Teal); cancelStickerButton = Add(stickerParams, "取消选中", CancelSticker); stickerParams.Children.Add(stickerStatus);
        stickerScale.ValueChanged += (_, _) => { if (syncing || selected == null || active == null) return; double cx = selected.X + selected.Width / 2, cy = selected.Y + selected.Height / 2; selected.Scale = stickerScale.Value; selected.X = cx - selected.Width / 2; selected.Y = cy - selected.Height / 2; selected.Clamp(active.Current.Width, active.Current.Height); RenderOverlay(); };
        stickerRotation.ValueChanged += (_, _) => { if (syncing || selected == null) return; selected.Rotation = stickerRotation.Value; RenderOverlay(); };
        var hints = new WrapPanel { Margin = new Thickness(24, 8, 24, 8) };
        foreach (string text in new[] { "⚡ 拖拽上传", "拖动涂抹 / 裁剪", "滚轮缩放", "中键拖拽平移", "Esc 取消当前操作", "🔒 图片不离开本机" })
            hints.Children.Add(new TextBlock { Text = text, Foreground = BrushOf("#5D7771"), FontSize = 12, Margin = new Thickness(0, 3, 18, 3) });
        var hintStrip = new Border { Background = BrushOf("#EEF6F1"), BorderBrush = BrushOf("#D9E8DF"), BorderThickness = new Thickness(0, 0, 0, 1), Child = hints }; Grid.SetRow(hintStrip, 2); root.Children.Add(hintStrip);
        var main = new Grid { Margin = new Thickness(24, 20, 24, 20) }; main.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); main.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetRow(main, 3); root.Children.Add(main);
        var batch = new DockPanel(); DockPanel.SetDock(batchTitle, Dock.Top); batch.Children.Add(batchTitle); batch.Children.Add(new ScrollViewer { Content = cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        batchPanel = new Border { Width = 152, Background = BrushOf("#F3F7F4"), BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 16, 0), Padding = new Thickness(4), Child = batch }; main.Children.Add(batchPanel);
        viewport.Background = CanvasPattern();
        var canvasFrame = new Border { Background = BrushOf("#EDF3F0"), BorderBrush = BrushOf("#D2E0DB"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(8), Child = viewport };
        Grid.SetColumn(canvasFrame, 1); main.Children.Add(canvasFrame); viewport.Children.Add(surface); surface.Children.Add(image);
        viewport.ToolTip = "滚轮缩放 · 中键平移 · Ctrl+V 粘贴图片";
        empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 380, Margin = new Thickness(24) };
        empty.Children.Add(new TextBlock { Text = "🖼️", FontSize = 48, Opacity = .65, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) });
        empty.Children.Add(new TextBlock { Text = "拖拽图片开始编辑", FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = BrushOf("#3C5F5A"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) });
        empty.Children.Add(new TextBlock { Text = "支持 JPG / PNG / GIF 等图片，可一次选择或拖拽多张图片，也可 Ctrl+V 粘贴。所有处理都只在本机完成。", Foreground = Muted, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, LineHeight = 23 });
        var load = Button("📁 选择图片", () => Choose(false), Teal); load.HorizontalAlignment = HorizontalAlignment.Center; load.Margin = new Thickness(0, 20, 0, 0);
        load.ToolTip = "支持 PNG / JPG / BMP / TIFF / GIF 首帧 / ICO；WebP 取决于系统解码器。支持多选，长边超过 2500 像素自动缩小。";
        empty.Children.Add(load);
        // Scale the empty presentation down at the 900px minimum window size so its load action remains reachable.
        viewport.Children.Add(new Viewbox { Child = empty, Stretch = Stretch.Uniform, StretchDirection = System.Windows.Controls.StretchDirection.DownOnly, Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var footer = new DockPanel { Margin = new Thickness(24, 12, 24, 12) };
        var help = new TextBlock { Text = "快捷键", Foreground = Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Focusable = true,
            ToolTip = "Ctrl+O 打开 · Ctrl+S 导出 · Ctrl+C 复制 · Ctrl+V 粘贴\nCtrl+Z 撤销 · Ctrl+Y 重做 · Enter 应用 · Esc 取消\nDelete 移除选中贴纸 · 滚轮缩放 · 中键平移" };
        DockPanel.SetDock(help, Dock.Right); footer.Children.Add(help);
        footer.Children.Add(new TextBlock { Text = "状态", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        badge.Margin = new Thickness(0); badge.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(new Border { Child = badge, Background = Pale, CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 16, 0) });
        footer.Children.Add(status);
        var footerFrame = new Border { Child = footer, Background = Paper, BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0) }; Grid.SetRow(footerFrame, 4); root.Children.Add(footerFrame);
        viewport.SizeChanged += (_, _) => LayoutSurface();
        surface.MouseDown += PointerDown; surface.MouseMove += PointerMove; surface.MouseUp += PointerUp;
        surface.LostMouseCapture += (_, _) => { if (down || panning) EndGesture(); };
        surface.MouseLeave += (_, _) => { if (!down) { hover = null; RenderOverlay(); } };
        viewport.PreviewMouseWheel += (_, e) => { if (active == null) return; zoom = Math.Clamp(zoom * (e.Delta > 0 ? 1.12 : 1 / 1.12), .25, 4); LayoutSurface(); RefreshStatus(); e.Handled = true; };
        PreviewKeyDown += OnKey;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) LoadFiles(paths, false); e.Handled = true; };
        Refresh();
    }
    private static Brush BrushOf(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    private static Border ToolDivider() => new() { Width = 1, Height = 30, Background = BrushOf("#CBDAD5"), Margin = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center };
    private static Brush CanvasPattern()
    {
        var tile = new DrawingGroup();
        tile.Children.Add(new GeometryDrawing(BrushOf("#EDF3F0"), null, new RectangleGeometry(new Rect(0, 0, 18, 18))));
        tile.Children.Add(new GeometryDrawing(BrushOf("#BFD4CB"), null, new EllipseGeometry(new Point(9, 9), .8, .8)));
        var brush = new DrawingBrush(tile) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 18, 18), Stretch = Stretch.None };
        brush.Freeze(); return brush;
    }
    private Button Button(string text, Action action, Brush? accent = null)
    {
        var b = new Button { Content = text, Background = accent ?? Paper, Foreground = accent == null ? BrushOf("#46666A") : Brushes.White, BorderBrush = accent ?? BrushOf("#CBDAD5"), BorderThickness = new Thickness(1), Padding = new Thickness(13, 8, 13, 8), Margin = new Thickness(3), Cursor = Cursors.Hand, MinHeight = 38, FontWeight = FontWeights.SemiBold };
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12)); border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty)); border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty)); border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty)); content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(content); template.VisualTree = border;
        var disabled = new Trigger { Property = IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(OpacityProperty, .4)); template.Triggers.Add(disabled);
        var over = new Trigger { Property = IsMouseOverProperty, Value = true }; over.Setters.Add(new Setter(OpacityProperty, .82)); template.Triggers.Add(over);
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Control.BorderBrushProperty, Teal)); template.Triggers.Add(focus); b.Template = template;
        b.Click += (_, _) => Guard(action); return b;
    }
    private Button Add(Panel panel, string text, Action action, Brush? accent = null, bool required = false)
    { var b = Button(text, action, accent); panel.Children.Add(b); if (required) imageButtons.Add(b); return b; }
    private static UIElement SliderGroup(string label, double min, double max, double initial, double step, out Slider slider)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Foreground = Muted });
        slider = new Slider { Minimum = min, Maximum = max, Value = initial, TickFrequency = step, IsSnapToTickEnabled = true, Width = 90, VerticalAlignment = VerticalAlignment.Center, ToolTip = label };
        var value = new TextBlock { Text = initial.ToString("0.#"), Width = 34, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Teal };
        slider.ValueChanged += (_, e) => value.Text = e.NewValue.ToString("0.#"); panel.Children.Add(slider); panel.Children.Add(value); return panel;
    }
    private void Guard(Action action) { try { action(); } catch (Exception ex) { status.Text = "操作失败：" + ex.Message; MessageBox.Show(Window.GetWindow(this), ex.Message, "图片编辑器", MessageBoxButton.OK, MessageBoxImage.Warning); } }
    private bool Pending() { if (stickers.Count == 0) return false; status.Text = "请先粘贴或取消待处理贴纸。"; return true; }
    private void Choose(bool sticker)
    {
        if (sticker && active == null) return;
        var dialog = new OpenFileDialog { Title = sticker ? "选择贴纸（可多选）" : "选择图片（可多选）", Multiselect = true, Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.ico|所有文件|*.*" };
        var start = ImageFiles.StartingFolder(); if (start != null) dialog.InitialDirectory = start;
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) LoadFiles(dialog.FileNames, sticker);
    }
    public void ImportFile(string path)
    {
        if (System.IO.File.Exists(path)) LoadFiles(new[] { path }, false);
    }

    private void LoadFiles(IEnumerable<string> paths, bool asSticker)
    {
        int added = 0; var failures = new List<string>();
        foreach (string path in paths)
        {
            try
            {
                var bitmap = EditorEngine.LoadBitmap(path, asSticker ? 10000 : 2500);
                if (asSticker) AddSticker(bitmap, System.IO.Path.GetFileName(path));
                else { var doc = new EditorDocument(System.IO.Path.GetFileName(path), EditorPixels.FromBitmap(bitmap)); documents.Add(doc); active ??= doc; }
                added++;
            }
            catch (Exception ex) { failures.Add($"{System.IO.Path.GetFileName(path)}: {ex.Message}"); }
        }
        Refresh(); if (asSticker) SyncSelection();
        status.Text = $"已加载 {added} 张{(asSticker ? "贴纸" : "图片")}" + (failures.Count > 0 ? $"；{failures.Count} 个文件无法解码。" : "");
        if (failures.Count > 0) MessageBox.Show(Window.GetWindow(this), string.Join("\n", failures.Take(8)) + "\nWebP 需要可用的 Windows WIC 解码器。", "部分文件未加载", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void AddSticker(BitmapSource bitmap, string name)
    {
        if (active == null) return;
        double scale = Math.Clamp(Math.Round(Math.Min(1, Math.Min(active.Current.Width * .8 / bitmap.PixelWidth, active.Current.Height * .8 / bitmap.PixelHeight)) * 100), 10, 100);
        var s = new EditorSticker(bitmap, name) { Scale = scale }; s.X = (active.Current.Width - s.Width) / 2 + stickers.Count % 8 * 22; s.Y = (active.Current.Height - s.Height) / 2 + stickers.Count % 8 * 22; s.Clamp(active.Current.Width, active.Current.Height); stickers.Add(s); selected = s; mode = Tool.Sticker;
    }
    private void SetMode(Tool tool)
    {
        if (active == null || (tool != Tool.Sticker && Pending())) return;
        EndGesture(); crop = null; mode = mode == tool ? Tool.View : tool;
        if (stickers.Count > 0) mode = Tool.Sticker; UpdateControls(); RenderOverlay(); RefreshStatus();
    }
    private void UpdateControls()
    {
        foreach (var b in imageButtons) b.IsEnabled = active != null;
        undoButton.IsEnabled = active?.CanUndo == true && stickers.Count == 0; redoButton.IsEnabled = active?.CanRedo == true && stickers.Count == 0;
        exportAllButton.IsEnabled = clearButton.IsEnabled = documents.Count > 0;
        resetMenu.IsEnabled = copyMenu.IsEnabled = active != null;
        foreach (var entry in modeButtons) { entry.Value.Background = mode == entry.Key ? Pale : Paper; entry.Value.Foreground = mode == entry.Key ? Teal : Muted; entry.Value.BorderBrush = mode == entry.Key ? Teal : BrushOf("#CBDAD5"); }
        cropParams.Visibility = mode == Tool.Crop ? Visibility.Visible : Visibility.Collapsed;
        mosaicParams.Visibility = mode == Tool.Mosaic ? Visibility.Visible : Visibility.Collapsed;
        maskParams.Visibility = mode == Tool.Mask ? Visibility.Visible : Visibility.Collapsed;
        stickerParams.Visibility = mode == Tool.Sticker ? Visibility.Visible : Visibility.Collapsed;
        strengthGroup.Visibility = method.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed; blurGroup.Visibility = method.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        applyStickerButton.IsEnabled = cancelStickerButton.IsEnabled = stickers.Count > 0;
        stickerScale.IsEnabled = stickerRotation.IsEnabled = selected != null;
        stickerStatus.Text = stickers.Count == 0 ? "" : $"{stickers.Count} 张待粘贴";
        stickerStatus.ToolTip = selected?.Name;
        empty.Visibility = active == null ? Visibility.Visible : Visibility.Collapsed; batchPanel.Visibility = documents.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        surface.Cursor = mode == Tool.Crop ? Cursors.Cross : mode == Tool.Sticker ? Cursors.Hand : Cursors.Arrow;
    }
    private void Refresh()
    {
        image.Source = active?.Current.Bitmap(); image.Width = active?.Current.Width ?? 0; image.Height = active?.Current.Height ?? 0;
        LayoutSurface(); RenderOverlay(); RenderCards(); UpdateControls(); RefreshStatus();
    }
    private void RefreshPixels() { image.Source = active?.Current.Bitmap(); RenderOverlay(); }
    private void RefreshStatus()
    {
        badge.Text = mode switch { Tool.Crop => "裁剪", Tool.Mosaic => "打码", Tool.Mask => "遮罩", Tool.Sticker => "贴纸", _ => "查看" };
        status.Text = active == null ? "就绪 · 图片不会离开本机" : $"第 {documents.IndexOf(active) + 1}/{documents.Count} 张  ·  {active.Name}  ·  {active.Current.Width} × {active.Current.Height}  ·  显示 {fit * zoom:P0}";
    }
    private void RenderCards()
    {
        cards.Children.Clear(); batchTitle.Text = $"图片 · {documents.Count}";
        foreach (var doc in documents)
        {
            var body = new StackPanel();
            body.Children.Add(new Image { Source = doc.Current.Bitmap(), Width = 104, Height = 72, Stretch = Stretch.Uniform, Margin = new Thickness(0, 4, 0, 6) });
            body.Children.Add(new TextBlock { Text = doc.Name, TextTrimming = TextTrimming.CharacterEllipsis, Width = 104, FontWeight = FontWeights.Normal, FontSize = 12 });
            body.Children.Add(new TextBlock { Text = $"{doc.Current.Width} × {doc.Current.Height}" + (doc.Dirty ? " · 已编辑" : ""), Foreground = Muted, FontSize = 11, Width = 104, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 2) });
            var pick = Button("", () => Switch(doc)); pick.Content = body; pick.Background = doc == active ? Pale : Paper; pick.BorderBrush = doc == active ? Teal : Line; pick.Padding = new Thickness(6); pick.ToolTip = doc.Name;
            cards.Children.Add(pick);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            Add(row, "复制", () => Copy(doc)); Add(row, "移除", () => Remove(doc)); cards.Children.Add(row);
        }
    }
    private void Switch(EditorDocument doc) { if (doc == active || Pending()) return; EndGesture(); active = doc; crop = null; mode = Tool.View; zoom = 1; panX = panY = 0; Refresh(); }
    private void Remove(EditorDocument doc)
    {
        if (doc == active && Pending()) return;
        int index = documents.IndexOf(doc); documents.Remove(doc);
        if (doc == active) { active = documents.Count == 0 ? null : documents[Math.Min(index, documents.Count - 1)]; crop = null; zoom = 1; panX = panY = 0; }
        Refresh();
    }
    private void Clear()
    {
        if (Pending()) return;
        if (documents.Any(d => d.Dirty) && MessageBox.Show(Window.GetWindow(this), "清空所有图片及未导出的编辑结果？", "清空批量列表", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        documents.Clear(); active = null; crop = null; mode = Tool.View; Refresh();
    }
    private void Fit() { zoom = 1; panX = panY = 0; LayoutSurface(); RefreshStatus(); }
    private void LayoutSurface()
    {
        if (active == null) { surface.Visibility = Visibility.Collapsed; return; }
        surface.Visibility = Visibility.Visible; int w = active.Current.Width, h = active.Current.Height;
        fit = Math.Min(1, Math.Min(Math.Max(30, viewport.ActualWidth - 40) / w, Math.Max(30, viewport.ActualHeight - 40) / h));
        surface.Width = w; surface.Height = h; surface.HorizontalAlignment = HorizontalAlignment.Left; surface.VerticalAlignment = VerticalAlignment.Top;
        var transform = new TransformGroup(); transform.Children.Add(new ScaleTransform(fit * zoom, fit * zoom)); transform.Children.Add(new TranslateTransform((viewport.ActualWidth - w * fit * zoom) / 2 + panX, (viewport.ActualHeight - h * fit * zoom) / 2 + panY)); surface.RenderTransform = transform;
    }
    private void RenderOverlay()
    {
        while (surface.Children.Count > 1) surface.Children.RemoveAt(surface.Children.Count - 1);
        if (active == null) return;
        double stroke = 1.5 / Math.Max(.01, fit * zoom);
        foreach (var s in stickers)
        {
            var layer = new Grid { Width = s.Width, Height = s.Height, IsHitTestVisible = false, RenderTransform = new RotateTransform(s.Rotation, s.Width / 2, s.Height / 2) };
            layer.Children.Add(new Image { Source = s.Image, Stretch = Stretch.Fill });
            layer.Children.Add(new Rectangle { Stroke = s == selected ? Teal : Coral, StrokeThickness = stroke, StrokeDashArray = new DoubleCollection { 5, 3 } });
            Canvas.SetLeft(layer, s.X); Canvas.SetTop(layer, s.Y); surface.Children.Add(layer);
        }
        if (crop is Rect r && mode == Tool.Crop && r.Width > 0 && r.Height > 0)
        {
            var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, active.Current.Width, active.Current.Height)), new RectangleGeometry(r));
            surface.Children.Add(new System.Windows.Shapes.Path { Data = outside, Fill = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), IsHitTestVisible = false });
            var outline = new Rectangle { Width = r.Width, Height = r.Height, Stroke = Brushes.White, StrokeThickness = stroke, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false }; Canvas.SetLeft(outline, r.X); Canvas.SetTop(outline, r.Y); surface.Children.Add(outline);
            var label = new TextBlock { Text = $" {r.Width:0} × {r.Height:0} ", Background = Teal, Foreground = Brushes.White, FontSize = 13 / Math.Max(.1, fit * zoom), IsHitTestVisible = false }; Canvas.SetLeft(label, r.X); Canvas.SetTop(label, Math.Max(0, r.Y - 22 / Math.Max(.1, fit * zoom))); surface.Children.Add(label);
        }
        if (hover is Point p && (mode == Tool.Mosaic || mode == Tool.Mask))
        {
            double radius = BrushRadius(); var circle = new Ellipse { Width = radius * 2, Height = radius * 2, Stroke = Teal, StrokeThickness = stroke, Fill = new SolidColorBrush(Color.FromArgb(35, 40, 127, 122)), IsHitTestVisible = false }; Canvas.SetLeft(circle, p.X - radius); Canvas.SetTop(circle, p.Y - radius); surface.Children.Add(circle);
        }
    }
    private double BrushRadius() => mode == Tool.Mask ? 16 : method.SelectedIndex == 1 ? Math.Max(8, size.Value) : size.Value / 2 + 2;
    private Point Position(MouseEventArgs e)
    { var p = e.GetPosition(surface); return active == null ? p : new Point(Math.Clamp(p.X, 0, active.Current.Width - .001), Math.Clamp(p.Y, 0, active.Current.Height - .001)); }
    private void PointerDown(object sender, MouseButtonEventArgs e)
    {
        if (active == null) return; Focus();
        if (e.ChangedButton == MouseButton.Middle) { panning = true; panStart = e.GetPosition(viewport); panOrigin = new Point(panX, panY); surface.CaptureMouse(); e.Handled = true; return; }
        if (e.ChangedButton != MouseButton.Left) return;
        start = last = Position(e); down = true; surface.CaptureMouse();
        if (mode == Tool.Crop) crop = new Rect(start, start);
        else if (mode == Tool.Mask || mode == Tool.Mosaic) { active.Checkpoint(); painted.Clear(); Paint(last, last); }
        else if (mode == Tool.Sticker)
        {
            selected = stickers.LastOrDefault(s => s.Contains(start));
            if (selected != null) { stickers.Remove(selected); stickers.Add(selected); dragOffset = new Point(start.X - selected.X, start.Y - selected.Y); } SyncSelection();
        }
        RenderOverlay(); e.Handled = true;
    }
    private void PointerMove(object sender, MouseEventArgs e)
    {
        if (active == null) return;
        if (panning) { var p = e.GetPosition(viewport); panX = panOrigin.X + p.X - panStart.X; panY = panOrigin.Y + p.Y - panStart.Y; LayoutSurface(); return; }
        Point point = Position(e); hover = point;
        if (down)
        {
            if (mode == Tool.Crop) crop = new Rect(start, point);
            else if (mode == Tool.Mask || mode == Tool.Mosaic) Paint(last, point);
            else if (mode == Tool.Sticker && selected != null) { selected.X = point.X - dragOffset.X; selected.Y = point.Y - dragOffset.Y; selected.Clamp(active.Current.Width, active.Current.Height); }
            last = point;
        }
        RenderOverlay();
    }
    private void PointerUp(object sender, MouseButtonEventArgs e) { EndGesture(); e.Handled = true; }
    private void EndGesture()
    {
        bool edited = down && (mode == Tool.Mask || mode == Tool.Mosaic); down = false; panning = false; surface.ReleaseMouseCapture();
        if (edited) { RenderCards(); UpdateControls(); RefreshStatus(); } painted.Clear();
    }
    private void Paint(Point from, Point to)
    {
        if (active == null) return; double radius = BrushRadius(); int steps = Math.Max(1, (int)Math.Ceiling((to - from).Length / Math.Max(2, radius / 2)));
        for (int n = 1; n <= steps; n++)
        {
            Point p = from + (to - from) * ((double)n / steps);
            if (mode == Tool.Mask) EditorEngine.Mask(active.Current, p, 16, opacity.Value / 100, maskColor);
            else if (method.SelectedIndex == 1) EditorEngine.Gaussian(active.Current, p, radius, blur.Value);
            else EditorEngine.Mosaic(active.Current, p, radius, Math.Max(2, (int)Math.Round(2 + strength.Value / 100 * 34)), painted);
        }
        active.Dirty = true; RefreshPixels();
    }
    private void ApplyCrop()
    {
        if (active == null || crop == null || Pending()) return; var r = crop.Value;
        int x = Math.Clamp((int)Math.Round(r.X), 0, active.Current.Width - 1), y = Math.Clamp((int)Math.Round(r.Y), 0, active.Current.Height - 1);
        int w = Math.Min((int)Math.Round(r.Width), active.Current.Width - x), h = Math.Min((int)Math.Round(r.Height), active.Current.Height - y);
        if (w < 4 || h < 4) { status.Text = "裁剪区域至少为 4 × 4 像素。"; return; }
        active.Checkpoint(); active.Current = EditorEngine.Crop(active.Current, new Int32Rect(x, y, w, h)); active.Dirty = true; crop = null; mode = Tool.View; zoom = 1; panX = panY = 0; Refresh();
    }
    private void SyncSelection()
    { syncing = true; if (selected != null) { stickerScale.Value = selected.Scale; stickerRotation.Value = selected.Rotation; } syncing = false; UpdateControls(); RenderOverlay(); }
    private void ApplyStickers()
    {
        if (active == null || stickers.Count == 0) return; var result = EditorEngine.Composite(active.Current, stickers); active.Checkpoint(); active.Current = result; active.Dirty = true; stickers.Clear(); selected = null; mode = Tool.View; Refresh();
    }
    private void CancelSticker() { if (stickers.Count == 0) return; stickers.Remove(selected ?? stickers[^1]); selected = stickers.LastOrDefault(); if (stickers.Count == 0) mode = Tool.View; SyncSelection(); RefreshStatus(); }
    private void Cancel() { EndGesture(); if (stickers.Count > 0) { CancelSticker(); return; } crop = null; mode = Tool.View; UpdateControls(); RenderOverlay(); RefreshStatus(); }
    private void Reset()
    {
        if (active == null) return; EndGesture(); active.Reset(); stickers.Clear(); selected = null; crop = null; mode = Tool.View; zoom = 1; panX = panY = 0; Refresh();
    }
    private void Undo() { if (active == null || Pending()) return; EndGesture(); active.Undo(); crop = null; Refresh(); }
    private void Redo() { if (active == null || Pending()) return; EndGesture(); active.Redo(); crop = null; Refresh(); }
    private void Copy(EditorDocument? doc)
    {
        if (doc == null || (doc == active && Pending())) return;
        // Include PNG for transparency-aware consumers and standard bitmap for native applications.
        using var png = new MemoryStream(); EditorEngine.SavePng(doc.Current, png);
        var data = new DataObject(); data.SetImage(doc.Current.Bitmap()); data.SetData("PNG", new MemoryStream(png.ToArray())); Clipboard.SetDataObject(data, true); status.Text = "已复制：" + doc.Name;
    }
    private void Export()
    {
        if (active == null || Pending()) return;
        var dialog = new SaveFileDialog { Title = "导出当前图片", Filter = "PNG 图片|*.png", DefaultExt = ".png", FileName = EditorEngine.ExportName(active.Name, documents.IndexOf(active)) };
        var startFolder = ImageFiles.StartingFolder(); if (startFolder != null) dialog.InitialDirectory = startFolder;
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        using (var stream = File.Create(dialog.FileName)) EditorEngine.SavePng(active.Current, stream); status.Text = "已导出 PNG：" + System.IO.Path.GetFileName(dialog.FileName);
    }
    private void ExportAll()
    {
        if (documents.Count == 0 || Pending()) return;
        var dialog = new SaveFileDialog { Title = "批量导出 ZIP", Filter = "ZIP 压缩包|*.zip", DefaultExt = ".zip", FileName = $"edited_images_{documents.Count}.zip" };
        var startFolder = ImageFiles.StartingFolder(); if (startFolder != null) dialog.InitialDirectory = startFolder;
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        using (var file = File.Create(dialog.FileName)) using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            for (int i = 0; i < documents.Count; i++) { using var entry = zip.CreateEntry(EditorEngine.ExportName(documents[i].Name, i), CompressionLevel.Fastest).Open(); EditorEngine.SavePng(documents[i].Current, entry); }
        status.Text = $"已导出 {documents.Count} 张 PNG 至 ZIP。";
    }
    private void Paste()
    {
        if (!Clipboard.ContainsImage()) { status.Text = "剪贴板中没有图片。"; return; }
        var bitmap = Clipboard.GetImage(); if (bitmap == null) return;
        if (mode == Tool.Sticker && active != null) { AddSticker(bitmap, "剪贴板贴纸"); SyncSelection(); }
        else
        {
            double scale = Math.Min(1, 2500.0 / Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));
            BitmapSource source = scale < 1 ? new TransformedBitmap(bitmap, new ScaleTransform(scale, scale)) : bitmap;
            var doc = new EditorDocument("剪贴板.png", EditorPixels.FromBitmap(source)); documents.Add(doc); active ??= doc;
        }
        Refresh();
    }
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        Action? action = e.Key switch
        {
            Key.Escape => Cancel,
            Key.Enter when mode == Tool.Crop => ApplyCrop,
            Key.Enter when mode == Tool.Sticker => ApplyStickers,
            Key.Delete when mode == Tool.Sticker => CancelSticker,
            Key.Z when ctrl => Undo,
            Key.Y when ctrl => Redo,
            Key.O when ctrl => () => Choose(false),
            Key.S when ctrl => Export,
            Key.C when ctrl => () => Copy(active),
            Key.V when ctrl => Paste,
            _ => null
        };
        if (action != null) { Guard(action); e.Handled = true; }
    }
}
