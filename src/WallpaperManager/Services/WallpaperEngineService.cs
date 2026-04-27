using System.Diagnostics;
using WallpaperManager.Models;

namespace WallpaperManager.Services;

public sealed class WallpaperEngineService
{
    public bool IsRunning(string executablePath)
    {
        var processName = GetProcessName(executablePath);
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        return Process.GetProcessesByName(processName).Length > 0;
    }

    public bool StartEngine(string executablePath)
    {
        if (!File.Exists(executablePath))
        {
            return false;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = true
        });

        return true;
    }

    public bool RunWallpaper(string executablePath, WallpaperItem wallpaper)
    {
        if (!File.Exists(executablePath) || string.IsNullOrWhiteSpace(wallpaper.LaunchPath))
        {
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("-control");
        startInfo.ArgumentList.Add("openWallpaper");
        startInfo.ArgumentList.Add("-file");
        startInfo.ArgumentList.Add(wallpaper.LaunchPath);

        Process.Start(startInfo);
        return true;
    }

    private static string GetProcessName(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return string.Empty;
        }

        return Path.GetFileNameWithoutExtension(executablePath);
    }
}
