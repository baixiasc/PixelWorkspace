using System;
using System.IO;

namespace PixelWorkspace;

internal static class SettingsStore
{
    private static readonly string DirectoryPath =
        Environment.GetEnvironmentVariable("PIXELWORKSPACE_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PixelWorkspace");
    private static readonly string DefaultFolderPath = Path.Combine(DirectoryPath, "default-image-folder.txt");

    public static string? LoadDefaultFolder()
    {
        foreach (var file in new[] { DefaultFolderPath, Path.Combine(AppContext.BaseDirectory, "default-image-folder.txt") })
        {
            try
            {
                if (!File.Exists(file)) continue;
                var folder = File.ReadAllText(file).Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder)) return folder;
            }
            catch { }
        }
        return null;
    }

    public static bool SaveDefaultFolder(string folder, out string error)
    {
        error = "";
        var value = (folder ?? "").Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(value)) { error = "路径为空。"; return false; }
        try
        {
            var fullPath = Path.GetFullPath(value);
            Directory.CreateDirectory(DirectoryPath);
            var temporary = DefaultFolderPath + ".tmp";
            File.WriteAllText(temporary, fullPath);
            File.Move(temporary, DefaultFolderPath, true);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            try
            {
                var fallback = Path.Combine(AppContext.BaseDirectory, "default-image-folder.txt");
                File.WriteAllText(fallback, Path.GetFullPath(value));
                return true;
            }
            catch (Exception fallbackError)
            {
                error = $"{ex.Message}；备用位置也失败：{fallbackError.Message}";
                return false;
            }
        }
    }

    public static void ClearDefaultFolder()
    {
        try
        {
            if (File.Exists(DefaultFolderPath)) File.Delete(DefaultFolderPath);
        }
        catch { }
    }
}
