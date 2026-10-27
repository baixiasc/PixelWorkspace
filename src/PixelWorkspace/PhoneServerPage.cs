using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PixelWorkspace;

public sealed class PhoneServerPage : UserControl, IDisposable
{
    private readonly TextBox folderBox = new();
    private readonly TextBox portBox = new() { Text = "3088" };
    private readonly TextBlock status = ImageFiles.Label("服务尚未启动。", 13);
    private readonly TextBox address = new();
    private readonly TextBox password = new();
    private readonly TextBlock placeholder = ImageFiles.Label("", 13);
    private readonly TextBlock devices = ImageFiles.Label("已记住的手机：启动服务后显示。", 12);
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Button startButton;
    private readonly Button stopButton;
    private PhoneFileServer? server;

    public PhoneServerPage()
    {
        Background = ImageFiles.Paper; Foreground = ImageFiles.Ink;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        folderBox.Text = SettingsStore.LoadDefaultFolder() ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var root = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var layout = new StackPanel { Margin = new Thickness(34), MaxWidth = 820 };
        var title = ImageFiles.Label("手机查看", 22); title.FontWeight = FontWeights.SemiBold; layout.Children.Add(title);
        var subtitle = ImageFiles.Label("在同一局域网内用手机浏览电脑上的图片和视频。文件只在本机提供，不上传到互联网。", 13);
        subtitle.Foreground = ImageFiles.Muted; subtitle.Margin = new Thickness(0, 8, 0, 24); layout.Children.Add(subtitle);
        layout.Children.Add(ImageFiles.Label("共享文件夹", 15));
        var folderRow = new Grid { Margin = new Thickness(0, 8, 0, 18) };
        folderRow.ColumnDefinitions.Add(new ColumnDefinition()); folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        StyleTextBox(folderBox); folderRow.Children.Add(folderBox);
        var choose = ImageFiles.Button("选择文件夹", ChooseFolder, true); choose.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(choose, 1); folderRow.Children.Add(choose); layout.Children.Add(folderRow);
        var portRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18), VerticalAlignment = VerticalAlignment.Center };
        var portLabel = ImageFiles.Label("端口", 15); portLabel.Margin = new Thickness(0, 0, 16, 0); portLabel.VerticalAlignment = VerticalAlignment.Center; portRow.Children.Add(portLabel);
        StyleTextBox(portBox); portBox.Width = 110; portBox.MinHeight = 36; portBox.VerticalAlignment = VerticalAlignment.Center; portBox.VerticalContentAlignment = VerticalAlignment.Center; portRow.Children.Add(portBox); layout.Children.Add(portRow);
        var actions = new WrapPanel(); startButton = ImageFiles.ToneButton("启动手机查看", Start, ImageFiles.Teal); stopButton = ImageFiles.Button("停止服务", Stop); stopButton.IsEnabled = false; actions.Children.Add(startButton); actions.Children.Add(stopButton); layout.Children.Add(actions);
        var info = new Border { Margin = new Thickness(0, 18, 0, 0), Padding = new Thickness(18), Background = ImageFiles.Hint, BorderBrush = ImageFiles.Line, BorderThickness = new Thickness(1) };
        var infoStack = new StackPanel();
        var infoTitle = ImageFiles.Label("连接信息", 15); infoTitle.Margin = new Thickness(0, 0, 0, 10); infoStack.Children.Add(infoTitle);
        StyleSelectable(address, ImageFiles.Teal); address.Margin = new Thickness(0, 0, 0, 8); infoStack.Children.Add(address);
        StyleSelectable(password, ImageFiles.Amber); infoStack.Children.Add(password);
        SetInfo(address, ""); SetInfo(password, "");
        var placeholder = this.placeholder; placeholder.Text = "启动后这里会显示访问地址和访问密码，可直接选中复制。"; placeholder.Foreground = ImageFiles.Muted; placeholder.TextWrapping = TextWrapping.Wrap; placeholder.FontSize = 13; infoStack.Children.Add(placeholder);
        info.Child = infoStack; layout.Children.Add(info);
        var copyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var copyAddress = ImageFiles.Button("复制地址", () => Copy(address.Text)); copyAddress.Margin = new Thickness(0, 0, 8, 0);
        var copyPassword = ImageFiles.Button("复制密码", () => Copy(password.Text)); copyPassword.Margin = new Thickness(0);
        copyRow.Children.Add(copyAddress); copyRow.Children.Add(copyPassword); layout.Children.Add(copyRow);
        status.Foreground = ImageFiles.Muted; status.Margin = new Thickness(0, 14, 0, 0); layout.Children.Add(status);
        devices.Foreground = ImageFiles.Muted; devices.TextWrapping = TextWrapping.Wrap; devices.Margin = new Thickness(0, 8, 0, 0); layout.Children.Add(devices);
        var note = ImageFiles.Label("手机打开连接地址后输入密码即可访问。连接信息框里的地址和密码可以直接用鼠标选中复制（Ctrl+C），也可以点方框下面的“复制地址”“复制密码”。手机授权一次后会被记住 30 天，重启程序也不用再输密码；如果升级后第一次仍提示输密码，请再授权一次，之后就会记住。在手机上点“关闭服务”会直接停止这里的服务。", 12); note.Foreground = ImageFiles.Muted; note.Margin = new Thickness(0, 18, 0, 0); layout.Children.Add(note);
        root.Content = layout; Content = root;
    }

    private static void StyleTextBox(TextBox box) { box.Padding = new Thickness(9, 6, 9, 6); box.BorderBrush = ImageFiles.Line; box.Background = ImageFiles.Paper; }
    private static void StyleSelectable(TextBox box, Brush foreground)
    {
        box.IsReadOnly = true; box.AcceptsReturn = true; box.BorderThickness = new Thickness(0); box.BorderBrush = Brushes.Transparent;
        box.Background = Brushes.Transparent; box.Foreground = foreground; box.FontSize = 13; box.MinHeight = 0; box.MinWidth = 0;
        box.Padding = new Thickness(0); box.TextWrapping = TextWrapping.Wrap; box.Cursor = Cursors.IBeam; box.FocusVisualStyle = null;
    }
    private static void SetInfo(TextBox box, string text) { box.Text = text; box.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible; }
    private void Copy(string text)
    {
        var value = (text ?? "").Trim();
        if (value.Length == 0) { status.Text = "还没有可复制的内容，请先启动服务。"; return; }
        try { Clipboard.SetText(value); status.Text = "已复制到剪贴板。"; }
        catch (Exception ex) { status.Text = "复制失败：" + ex.Message; }
    }
    private void ShowDevices()
    {
        var active = server;
        if (active == null) return;
        var count = active.DeviceCount;
        var text = count == 0 ? "已记住的手机：0 台（手机授权后会出现在这里）。" : $"已记住的手机：{count} 台，30 天内无需再输密码。";
        text += "  记录文件：" + active.StoreInfo;
        if (active.StoreError is { Length: > 0 } error) text += "\n⚠ 授权记录保存失败：" + error;
        devices.Text = text;
    }
    internal void UseDefaultFolder(string path)
    {
        if (server != null || !Directory.Exists(path)) return;
        folderBox.Text = path;
    }
    private void ChooseFolder()
    {
        var dialog = new OpenFolderDialog { Title = "选择手机共享文件夹", InitialDirectory = Directory.Exists(folderBox.Text) ? folderBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) };
        if (dialog.ShowDialog() == true) folderBox.Text = dialog.FolderName;
    }
    private void Start()
    {
        Stop();
        if (!Directory.Exists(folderBox.Text)) { status.Text = "共享文件夹不存在。"; return; }
        if (!int.TryParse(portBox.Text, out var port) || port is < 1024 or > 65535) { status.Text = "端口必须是 1024 到 65535 之间的数字。"; return; }
        try
        {
            server = new PhoneFileServer(Path.GetFullPath(folderBox.Text), port); server.ShutdownRequested += RemoteShutdown; server.Start(); startButton.IsEnabled = false; stopButton.IsEnabled = true;
            var displayAddresses = server.Addresses.Count == 0 ? new[] { "127.0.0.1" } : server.Addresses;
            SetInfo(address, string.Join("\n", displayAddresses.Select(x => "http://" + x + ":" + port))); SetInfo(password, "访问密码：" + server.Password); placeholder.Visibility = Visibility.Collapsed; status.Text = "手机查看服务已启动。";
            refresh.Tick -= OnRefresh; refresh.Tick += OnRefresh; refresh.Start(); ShowDevices();
        }
        catch (Exception ex) { server?.Dispose(); server = null; status.Text = "启动失败：" + ex.Message; }
    }
    private void RemoteShutdown()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (server == null) return;
            Stop();
            status.Text = "服务已被手机端关闭。";
        });
    }
    private void OnRefresh(object? sender, EventArgs e) => ShowDevices();
    private void Stop()
    {
        refresh.Stop();
        server?.Dispose(); server = null; if (startButton != null) startButton.IsEnabled = true; if (stopButton != null) stopButton.IsEnabled = false; if (address != null) SetInfo(address, ""); if (password != null) SetInfo(password, ""); if (placeholder != null) placeholder.Visibility = Visibility.Visible; if (status != null) status.Text = "服务尚未启动。";
    }
    public void Dispose() => Stop();
}

internal sealed class PhoneFileServer : IDisposable
{
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg", ".ico", ".avif" };
    private readonly string root; private readonly int port;
    private readonly PhoneTokenStore tokens = new();
    private readonly ConcurrentDictionary<string, LoginAttempts> attempts = new(StringComparer.Ordinal);
    private const int MaxLoginFailures = 10;
    private static readonly TimeSpan LoginLockDuration = TimeSpan.FromSeconds(60);
    private TcpListener? listener; private CancellationTokenSource? cancellation;
    // 8 随机字节 => 16 位十六进制、128 bit，局域网内暴力猜解不可行。
    public string Password { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
    public IReadOnlyList<string> Addresses { get; }
    public event Action? ShutdownRequested;
    public int DeviceCount => tokens.Count;
    public string StoreInfo => tokens.Location;
    public string? StoreError => tokens.LastError;
    public PhoneFileServer(string root, int port) { this.root = root; this.port = port; Addresses = GetLocalAddresses(); }

    public void Start()
    {
        var socket = new TcpListener(IPAddress.Any, port);
        socket.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.Start(128);
        listener = socket; cancellation = new CancellationTokenSource();
        _ = Task.Run(() => AcceptLoop(cancellation.Token));
    }

    private async Task AcceptLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener!.AcceptTcpClientAsync(token); }
            catch { break; }
            _ = Task.Run(() => ServeClient(client));
        }
    }

    private async Task ServeClient(TcpClient tcp)
    {
        using (tcp)
        {
            try
            {
                tcp.ReceiveTimeout = 30000; tcp.SendTimeout = 60000;
                await using var stream = tcp.GetStream();
                var header = await ReadHeader(stream);
                if (header == null) return;
                var lines = Encoding.Latin1.GetString(header).Split("\r\n");
                var requestLine = lines[0].Split(' ');
                if (requestLine.Length < 2) return;
                var request = new Client(stream) { Method = requestLine[0].ToUpperInvariant(), RemoteAddress = (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address?.ToString() ?? "" };
                var target = requestLine[1];
                var queryIndex = target.IndexOf('?');
                request.Path = Uri.UnescapeDataString(queryIndex < 0 ? target : target[..queryIndex]);
                request.Query = queryIndex < 0 ? "" : target[(queryIndex + 1)..];
                for (var i = 1; i < lines.Length; i++)
                {
                    var colon = lines[i].IndexOf(':');
                    if (colon <= 0) continue;
                    request.Headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
                }
                if (request.Headers.TryGetValue("Content-Length", out var text) && int.TryParse(text, out var length) && length > 0 && length <= 1_048_576)
                    request.Body = await ReadExactly(stream, length);
                await Handle(request);
            }
            catch { }
        }
    }

    private static async Task<byte[]?> ReadHeader(Stream stream)
    {
        var buffer = new byte[16384]; var length = 0; var matched = 0;
        while (length < buffer.Length)
        {
            if (await stream.ReadAsync(buffer.AsMemory(length, 1)) == 0) return null;
            length++;
            var value = buffer[length - 1];
            if (value == 13) matched = matched == 2 ? 3 : 1;
            else if (value == 10) matched = matched == 1 ? 2 : matched == 3 ? 4 : 0;
            else matched = 0;
            if (matched == 4) return buffer[..(length - 4)];
        }
        return null;
    }

    private static async Task<byte[]> ReadExactly(Stream stream, int count)
    {
        var buffer = new byte[count]; var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset));
            if (read == 0) break;
            offset += read;
        }
        return buffer;
    }

    private async Task Handle(Client client)
    {
        try
        {
            var path = client.Path;
            if (client.Method == "OPTIONS") { client.Send(204, "", Array.Empty<byte>()); return; }
            if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) { ServePublic(client, path); return; }
            if (path == "/api/login") { await Login(client); return; }
            if (path == "/api/verify") { Require(client, () => Json(client, 200, new { valid = true })); return; }
            if (path == "/api/logout") { var existing = TokenOf(client); if (existing != null) tokens.Remove(existing); client.Send(204, "", Array.Empty<byte>()); return; }
            if (path == "/api/shutdown") { Require(client, () => { Json(client, 200, new { message = "服务器正在关闭" }); _ = Task.Run(async () => { await Task.Delay(250); ShutdownRequested?.Invoke(); Dispose(); }); }); return; }
            if (path == "/api/browse") { Require(client, () => Browse(client)); return; }
            if (path == "/api/thumbnail") { Require(client, () => SendThumbnail(client)); return; }
            if (path == "/api/file") { Require(client, () => SendFile(client)); return; }
            if (path == "/api/info") { Require(client, () => Json(client, 200, new { sharedDirs = new[] { root }, port })); return; }
            NotFound(client);
        }
        catch (Exception ex) { try { Json(client, 500, new { error = ex.Message }); } catch { } }
    }

    private async Task Login(Client client)
    {
        var ip = client.RemoteAddress.Length == 0 ? "unknown" : client.RemoteAddress;
        var state = attempts.GetOrAdd(ip, _ => new LoginAttempts());
        if (state.IsLocked(DateTime.UtcNow))
        {
            // 同一来源在锁定期内直接拒绝，避免逐个口令穷举。
            var wait = Math.Max(1, (int)Math.Ceiling(state.LockedUntil.Subtract(DateTime.UtcNow).TotalSeconds));
            Json(client, 429, new { error = "尝试次数过多，请 " + wait + " 秒后再试" });
            return;
        }
        var body = Encoding.UTF8.GetString(client.Body);
        var password = ReadPassword(body);
        if (!string.Equals(password, Password, StringComparison.Ordinal))
        {
            state.RecordFailure(DateTime.UtcNow, MaxLoginFailures, LoginLockDuration);
            Json(client, 401, new { error = state.LockedUntil > DateTime.UtcNow ? "密码错误，已暂时锁定 60 秒" : "密码错误" });
            return;
        }
        state.RecordSuccess();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)); tokens.Add(token, DateTime.UtcNow.AddDays(30));
        Json(client, 200, new { token, message = "此设备已授权" }, $"Set-Cookie: token={token}; Path=/; Max-Age=2592000; HttpOnly; SameSite=Lax");
        await Task.CompletedTask;
    }

    private sealed class LoginAttempts
    {
        private int failures;
        public DateTime LockedUntil { get; private set; }
        public bool IsLocked(DateTime now) => LockedUntil > now;
        public void RecordFailure(DateTime now, int limit, TimeSpan duration)
        {
            failures++;
            if (failures >= limit) { LockedUntil = now.Add(duration); failures = 0; }
        }
        public void RecordSuccess() { failures = 0; LockedUntil = DateTime.MinValue; }
    }

    private static string? ReadPassword(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var property in document.RootElement.EnumerateObject())
                if (string.Equals(property.Name, "password", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                    return property.Value.GetString();
        }
        catch { }
        return null;
    }

    private void Require(Client client, Action action)
    {
        var token = TokenOf(client);
        if (token == null || !tokens.Valid(token)) { Json(client, 401, new { error = "未登录", needAuth = true }); return; }
        action();
    }

    private static string? TokenOf(Client client)
    {
        var token = client.Headers.TryGetValue("Cookie", out var cookie) ? ReadCookie(cookie) : null;
        if (token == null && client.Headers.TryGetValue("Authorization", out var authorization)) token = authorization.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase).Trim();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    private static string? ReadCookie(string cookie)
    {
        foreach (var part in cookie.Split(';'))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0].Trim() == "token") return pair[1].Trim();
        }
        return null;
    }

    private void Browse(Client client)
    {
        var requested = Query(client, "path"); var directory = string.IsNullOrWhiteSpace(requested) ? root : Resolve(requested);
        if (directory == null || !Directory.Exists(directory)) { Json(client, 404, new { error = "目录不存在" }); return; }
        var items = Directory.EnumerateFileSystemEntries(directory).Where(x => !Path.GetFileName(x).StartsWith('.') && !Path.GetFileName(x).StartsWith('$')).Select(x => { var info = new FileInfo(x); var isDir = Directory.Exists(x); var imageCount = isDir ? CountImages(x) : 0; return new { name = Path.GetFileName(x), path = x, isDirectory = isDir, size = isDir ? 0 : info.Length, modified = info.LastWriteTimeUtc.ToString("O"), mimeType = isDir ? null : Mime(Path.GetExtension(x)), imageCount }; }).OrderByDescending(x => x.isDirectory).ThenBy(x => x.name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var parent = string.Equals(directory, root, StringComparison.OrdinalIgnoreCase) ? null : Directory.GetParent(directory)?.FullName;
        Json(client, 200, new { path = directory, parent, isRoot = string.Equals(directory, root, StringComparison.OrdinalIgnoreCase), items });
    }

    private static readonly string[] AssetFolders = { "phone", "手机查看" };

    private static string? FindPublicRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        for (int level = 0; level < 3 && directory != null; level++, directory = directory.Parent)
        {
            foreach (var folder in AssetFolders)
            {
                var candidate = Path.Combine(directory.FullName, folder, "public");
                if (File.Exists(Path.Combine(candidate, "index.html"))) return candidate;
            }
        }
        return null;
    }

    private void ServePublic(Client client, string requestPath)
    {
        var publicRoot = FindPublicRoot();
        if (publicRoot == null) { NotFound(client); return; }
        var relative = requestPath == "/" ? "index.html" : requestPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var file = Path.GetFullPath(Path.Combine(publicRoot, relative));
        if (!file.StartsWith(publicRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(file)) { NotFound(client); return; }
        var extension = Path.GetExtension(file);
        var type = Mime(extension);
        if (extension.Equals(".html", StringComparison.OrdinalIgnoreCase) || extension.Equals(".js", StringComparison.OrdinalIgnoreCase) || extension.Equals(".css", StringComparison.OrdinalIgnoreCase) || extension.Equals(".json", StringComparison.OrdinalIgnoreCase)) type += "; charset=utf-8";
        SendBytes(client, 200, type, File.ReadAllBytes(file));
    }

    private void SendThumbnail(Client client)
    {
        var file = Resolve(Query(client, "path"));
        if (file == null || !File.Exists(file)) { Json(client, 404, new { error = "文件不存在" }); return; }
        if (!ImageExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) { Json(client, 415, new { error = "不是支持的图片文件" }); return; }
        SendBytes(client, 200, Mime(Path.GetExtension(file)), File.ReadAllBytes(file), "Cache-Control: private, max-age=86400");
    }

    private static int CountImages(string directory)
    {
        try { return Directory.EnumerateFiles(directory).Count(x => ImageExtensions.Contains(Path.GetExtension(x), StringComparer.OrdinalIgnoreCase)); }
        catch { return 0; }
    }

    private void SendFile(Client client)
    {
        var file = Resolve(Query(client, "path")); if (file == null || !File.Exists(file)) { Json(client, 404, new { error = "文件不存在" }); return; }
        var info = new FileInfo(file); long start = 0, end = info.Length - 1; var status = 200;
        if (client.Headers.TryGetValue("Range", out var range) && range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            var values = range[6..].Split('-');
            if (!long.TryParse(values[0], out start) || start < 0 || start >= info.Length) { client.Send(416, "", Array.Empty<byte>(), $"Content-Range: bytes */{info.Length}"); return; }
            if (values.Length > 1 && long.TryParse(values[1], out var requestedEnd)) end = requestedEnd;
            if (end < start) { client.Send(416, "", Array.Empty<byte>(), $"Content-Range: bytes */{info.Length}"); return; }
            if (end >= info.Length) end = info.Length - 1;
            status = 206;
        }
        using var stream = File.OpenRead(file); stream.Position = start;
        var headers = new List<string> { "Accept-Ranges: bytes" };
        if (status == 206) headers.Add($"Content-Range: bytes {start}-{end}/{info.Length}");
        client.Send(status, Mime(Path.GetExtension(file)), end - start + 1, client.Method == "HEAD" ? null : stream, headers.ToArray());
    }

    private string? Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return root;
        try { var full = Path.GetFullPath(path); return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || string.Equals(full, root, StringComparison.OrdinalIgnoreCase) ? full : null; }
        catch { return null; }
    }

    private static string? Query(Client client, string key)
    {
        foreach (var part in client.Query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && Uri.UnescapeDataString(pair[0]) == key) return Uri.UnescapeDataString(pair[1]);
        }
        return null;
    }

    private static string Mime(string ext) => ext.ToLowerInvariant() switch
    {
        ".html" => "text/html",
        ".js" => "text/javascript",
        ".css" => "text/css",
        ".json" => "application/json",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".svg" => "image/svg+xml",
        ".ico" => "image/x-icon",
        ".avif" => "image/avif",
        ".mp4" or ".m4v" => "video/mp4",
        ".webm" => "video/webm",
        ".mkv" => "video/x-matroska",
        ".mov" => "video/quicktime",
        ".avi" => "video/x-msvideo",
        ".ts" => "video/mp2t",
        ".txt" => "text/plain",
        _ => "application/octet-stream"
    };

    private static void SendBytes(Client client, int status, string contentType, byte[] bytes, params string[] headers) => client.Send(status, contentType, bytes.Length, new MemoryStream(bytes), headers);
    private static void Json(Client client, int status, object value, params string[] headers) => SendBytes(client, status, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(value), headers);
    private static void NotFound(Client client) => Json(client, 404, new { error = "API 不存在" });

    public void Dispose()
    {
        try { cancellation?.Cancel(); listener?.Stop(); } catch { }
        try { cancellation?.Dispose(); } catch { }
        listener = null; cancellation = null;
    }

    private static IReadOnlyList<string> GetLocalAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(x => x.GetIPProperties().UnicastAddresses)
        .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x.Address))
        .Select(x => x.Address.ToString()).Distinct().OrderBy(x => x).ToArray();

    private sealed class Client : IDisposable
    {
        private readonly Stream output;
        public Client(Stream output) { this.output = output; }
        public string Method { get; init; } = "GET";
        public string RemoteAddress { get; init; } = "";
        public string Path { get; set; } = "/";
        public string Query { get; set; } = "";
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public byte[] Body { get; set; } = Array.Empty<byte>();

        public void Send(int status, string contentType, long length, Stream? source, params string[] headers)
        {
            var head = new StringBuilder();
            head.Append("HTTP/1.1 ").Append(status).Append(' ').Append(Reason(status)).Append("\r\n");
            head.Append("Content-Length: ").Append(length).Append("\r\n");
            head.Append("Connection: close\r\n");
            head.Append("Cache-Control: no-store\r\n");
            if (contentType.Length > 0) head.Append("Content-Type: ").Append(contentType).Append("\r\n");
            foreach (var header in headers) head.Append(header).Append("\r\n");
            head.Append("\r\n");
            var bytes = Encoding.UTF8.GetBytes(head.ToString());
            output.Write(bytes, 0, bytes.Length);
            if (source != null)
            {
                var buffer = new byte[64 * 1024]; var remaining = length;
                while (remaining > 0)
                {
                    var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (read == 0) break;
                    output.Write(buffer, 0, read);
                    remaining -= read;
                }
            }
            try { output.Flush(); } catch { }
        }

        public void Send(int status, string contentType, byte[] bytes, params string[] headers) => Send(status, contentType, bytes.Length, new MemoryStream(bytes), headers);

        private static string Reason(int status) => status switch
        {
            200 => "OK", 204 => "No Content", 206 => "Partial Content", 400 => "Bad Request",
            401 => "Unauthorized", 404 => "Not Found", 415 => "Unsupported Media Type",
            416 => "Range Not Satisfiable", 500 => "Internal Server Error", _ => "OK"
        };

        public void Dispose() { try { output.Dispose(); } catch { } }
    }
}

internal sealed class PhoneTokenStore
{
    private readonly ConcurrentDictionary<string, DateTime> entries = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private string? active;

    private static string PrimaryFolder => Environment.GetEnvironmentVariable("PIXELWORKSPACE_DATA_DIR") is { Length: > 0 } custom
        ? custom
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PixelWorkspace");
    private static string FallbackFolder => Path.Combine(AppContext.BaseDirectory, "数据");
    private static string FileName => "phone-view-devices.json";
    private static IEnumerable<string> Candidates => new[] { Path.Combine(PrimaryFolder, FileName), Path.Combine(FallbackFolder, FileName) };

    public PhoneTokenStore() => Load();

    public int Count => entries.Count;
    public string? LastError { get; private set; }
    public string Location => active ?? Path.Combine(PrimaryFolder, FileName);

    private void Load()
    {
        foreach (var candidate in Candidates)
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                var saved = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(candidate));
                if (saved == null) continue;
                var now = DateTime.UtcNow;
                foreach (var pair in saved) if (pair.Value > now) entries[pair.Key] = pair.Value;
                active = candidate;
                LastError = null;
                return;
            }
            catch (Exception ex) { LastError = candidate + " → " + ex.Message; }
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(entries.ToDictionary(x => x.Key, x => x.Value));
        foreach (var target in Order())
        {
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.WriteAllText(target, json);
                }
                active = target; LastError = null; return;
            }
            catch (Exception ex) { LastError = target + " → " + ex.Message; }
        }
    }

    private IEnumerable<string> Order()
    {
        if (active != null) yield return active;
        foreach (var candidate in Candidates) if (candidate != active) yield return candidate;
    }

    public void Add(string token, DateTime expiry) { entries[Hash(token)] = expiry; Save(); }

    public bool Valid(string token) => entries.TryGetValue(Hash(token), out var expiry) && expiry > DateTime.UtcNow;

    public void Remove(string token) { if (entries.TryRemove(Hash(token), out _)) Save(); }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
