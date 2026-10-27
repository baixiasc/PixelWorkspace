using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace PixelWorkspaceLauncher;

internal static class Program
{
    private const uint IconWarning = 0x00000030;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);

    [STAThread]
    private static void Main()
    {
        var home = ResolveHome();
        var programs = new[]
        {
            Path.Combine(home, "app", "PixelWorkspace.exe"),
            Path.Combine(home, "程序", "PixelWorkspace.exe"),
            Path.Combine(home, "app", "像素工作台.exe"),
            Path.Combine(home, "程序", "像素工作台.exe"),
        };

        foreach (var program in programs)
        {
            if (!File.Exists(program))
            {
                continue;
            }

            try
            {
                Process.Start(new ProcessStartInfo(program)
                {
                    WorkingDirectory = Path.GetDirectoryName(program)!,
                    UseShellExecute = false,
                });
                return;
            }
            catch (Exception first)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(program)
                    {
                        WorkingDirectory = Path.GetDirectoryName(program)!,
                        UseShellExecute = true,
                    });
                    return;
                }
                catch (Exception second)
                {
                    MessageBox(
                        IntPtr.Zero,
                        "程序启动失败：" + second.Message + "\n\n" + program + "\n\n" + first.Message,
                        "像素工作台",
                        IconWarning);
                    return;
                }
            }
        }

        MessageBox(
            IntPtr.Zero,
            "没有找到「程序」文件夹里的主程序。\n\n请把整个文件夹一起解压，不要单独移动或删除里面的文件。\n\n查找位置：" + home,
            "像素工作台",
            IconWarning);
    }

    private static string ResolveHome()
    {
        var process = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(process))
        {
            var folder = Path.GetDirectoryName(process);
            if (!string.IsNullOrEmpty(folder))
            {
                return folder;
            }
        }

        return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    }
}
