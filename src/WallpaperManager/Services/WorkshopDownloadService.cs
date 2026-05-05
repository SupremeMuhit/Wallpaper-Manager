using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WallpaperManager.Services;

public sealed class WorkshopDownloadService
{
    private const string SecretKey = "wallpaper-engine-secret";
    private const string AppId = "431960";

    private static readonly List<(string Username, string EncryptedPassword)> EncryptedAccounts = new();

    static WorkshopDownloadService()
    {
        LoadAccountsFromEnv();
    }

    private static void LoadAccountsFromEnv()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var envPath = Path.Combine(baseDir, ".env");
            
            // Also check project dir for dev
            if (!File.Exists(envPath))
                envPath = Path.Combine(baseDir, "..", "..", "..", ".env");

            if (File.Exists(envPath))
            {
                var lines = File.ReadAllLines(envPath);
                foreach (var line in lines)
                {
                    if (line.StartsWith("STEAM_ACCOUNTS="))
                    {
                        var accountsStr = line.Substring("STEAM_ACCOUNTS=".Length);
                        var pairs = accountsStr.Split(';', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var pair in pairs)
                        {
                            var parts = pair.Split(',', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length == 2)
                            {
                                EncryptedAccounts.Add((parts[0], parts[1]));
                            }
                        }
                    }
                }
            }
        }
        catch { /* Fallback or ignore */ }
        
        // Fallback if env is empty/missing
        if (EncryptedAccounts.Count == 0)
        {
            EncryptedAccounts.Add(("ruiiixx", "JFdbKzI1Ml1BaVY3"));
            EncryptedAccounts.Add(("vAbuDy", "NQ4DAAFZBgwC"));
            EncryptedAccounts.Add(("adgjl1182", "JiQ4OT9YSVxLFA=="));
            EncryptedAccounts.Add(("gobjj16182", "DRQDDhkAH11AH1c="));
            EncryptedAccounts.Add(("787109690", "PxQPOQg4PTQbSlRb"));
            EncryptedAccounts.Add(("workshop01", "DRQDAw8WA19f"));
            EncryptedAccounts.Add(("workshop02", "DRQDAw8WA19c"));
            EncryptedAccounts.Add(("workshop03", "DRQDAw8WA19e"));
            EncryptedAccounts.Add(("premexilmenledgconis", "RBE0Djg7Ogk2Tw==")); // Move to end as requested
        }
    }

    private bool _skipCurrentRequested = false;
    public void SkipCurrentAccount()
    {
        _skipCurrentRequested = true;
    }

    public string? ExtractWorkshopId(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        // Try numeric ID first
        if (Regex.IsMatch(input.Trim(), @"^\d+$"))
        {
            return input.Trim();
        }

        // Try URL
        var match = Regex.Match(input, @"id=(\d+)");
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        return null;
    }

    public async Task<bool> DownloadAsync(string workshopId, string downloadDir, Action<double, string>? onProgress = null)
    {
        if (string.IsNullOrWhiteSpace(workshopId)) return false;

        // Ensure download directory exists
        var targetPath = Path.Combine(downloadDir, workshopId);
        Directory.CreateDirectory(targetPath);

        // Find DepotDownloaderMod.exe
        // We expect it to be in a "DepotDownloaderMod" folder next to the app
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var depotExe = Path.Combine(baseDir, "DepotDownloaderMod", "DepotDownloaderMod.exe");

        if (!File.Exists(depotExe))
        {
            // Fallback for development (checking if it was copied to the project folder)
            depotExe = Path.Combine(baseDir, "..", "..", "..", "DepotDownloaderMod", "DepotDownloaderMod.exe");
            if (!File.Exists(depotExe))
            {
                onProgress?.Invoke(0, "Error: DepotDownloaderMod.exe not found.");
                return false;
            }
        }

        _skipCurrentRequested = false;
        var accounts = EncryptedAccounts
            .Select(a => (a.Username, Password: Decrypt(a.EncryptedPassword)))
            .ToList();
        
        string lastError = "All accounts failed";

        foreach (var account in accounts)
        {
            _skipCurrentRequested = false;
            onProgress?.Invoke(0, $"Connecting to Steam as {account.Username}...");

            var startInfo = new ProcessStartInfo
            {
                FileName = depotExe,
                Arguments = $"-app {AppId} -pubfile {workshopId} -username {account.Username} -password {account.Password} -dir \"{targetPath}\" -max-servers 30 -max-downloads 10",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(depotExe)
            };

            using var process = new Process { StartInfo = startInfo };
            
            var tcs = new TaskCompletionSource<bool>();

            process.OutputDataReceived += (s, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                
                var progress = ParseProgress(e.Data);
                if (progress.HasValue)
                {
                    onProgress?.Invoke(progress.Value, "Downloading...");
                }
                else if (e.Data.Contains("Login Key Failed", StringComparison.OrdinalIgnoreCase) || 
                         e.Data.Contains("Invalid Password", StringComparison.OrdinalIgnoreCase) ||
                         e.Data.Contains("Steam Guard", StringComparison.OrdinalIgnoreCase) ||
                         e.Data.Contains("Two-factor", StringComparison.OrdinalIgnoreCase) ||
                         e.Data.Contains("Authenticator", StringComparison.OrdinalIgnoreCase) ||
                         e.Data.Contains("Captcha", StringComparison.OrdinalIgnoreCase))
                {
                    try { process.Kill(); } catch { }
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    lastError = e.Data;
                }
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                bool receivedAnyOutput = false;
                process.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) receivedAnyOutput = true; };

                // Initial connection timeout - if no output for 30s, it's probably stuck on a prompt
                var connectionTimeout = Task.Delay(TimeSpan.FromSeconds(30));
                var processTask = process.WaitForExitAsync();

                while (!processTask.IsCompleted)
                {
                    if (_skipCurrentRequested)
                    {
                        try { process.Kill(); } catch { }
                        onProgress?.Invoke(0, "Skipping current account...");
                        break;
                    }
                    
                    var completed = await Task.WhenAny(processTask, Task.Delay(500));
                    if (completed == processTask) break;

                    // Still check initial timeout
                    if (!receivedAnyOutput && DateTime.Now - process.StartTime > TimeSpan.FromSeconds(30))
                    {
                        try { process.Kill(); } catch { }
                        onProgress?.Invoke(0, $"Account {account.Username} unresponsive. Trying next...");
                        break;
                    }
                }

                if (_skipCurrentRequested) continue;

                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5));
                var completedTask = await Task.WhenAny(processTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    try { process.Kill(); } catch { }
                    onProgress?.Invoke(0, "Download timed out.");
                    continue;
                }

                if (process.ExitCode == 0)
                {
                    onProgress?.Invoke(100, "Download complete!");
                    return true;
                }
                else
                {
                    onProgress?.Invoke(0, $"Account {account.Username} failed. Trying next...");
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                continue;
            }
        }

        onProgress?.Invoke(0, $"Error: {lastError}");
        return false;
    }

    private static double? ParseProgress(string line)
    {
        var match = Regex.Match(line, @"[Pp]rogress[:\s]+(\d+\.?\d*)\s*%");
        if (match.Success && double.TryParse(match.Groups[1].Value, out var val))
        {
            return val;
        }

        match = Regex.Match(line, @"\b(\d+\.?\d*)\s*%");
        if (match.Success && double.TryParse(match.Groups[1].Value, out var val2))
        {
            if (val2 >= 0 && val2 <= 100) return val2;
        }

        return null;
    }

    private static string Decrypt(string encoded)
    {
        var data = Convert.FromBase64String(encoded);
        var secret = Encoding.UTF8.GetBytes(SecretKey);
        var result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            result[i] = (byte)(data[i] ^ secret[i % secret.Length]);
        }
        return Encoding.UTF8.GetString(result);
    }
}
