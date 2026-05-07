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
    private const string PreferredFirstAccount = "adgjl1182";
    private const string PreferredLastAccount = "premexilmenledgconis";

    private static readonly List<(string Username, string EncryptedPassword)> EncryptedAccounts = new();

    static WorkshopDownloadService()
    {
        LoadAccountsFromEnv();
    }

    private static void LoadAccountsFromEnv()
    {
        try
        {
            EncryptedAccounts.Clear();
            foreach (var envPath in GetEnvCandidatePaths())
            {
                if (!File.Exists(envPath))
                {
                    continue;
                }

                foreach (var rawLine in File.ReadAllLines(envPath))
                {
                    var line = rawLine.Trim();
                    if (line.StartsWith("STEAM_ACCOUNTS=", StringComparison.OrdinalIgnoreCase))
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

                        return;
                    }
                }
            }
        }
        catch { /* Fallback or ignore */ }
        
    }

    private static IEnumerable<string> GetEnvCandidatePaths()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in new[] { AppDomain.CurrentDomain.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var path = Path.Combine(directory.FullName, ".env");
                if (seen.Add(path))
                {
                    yield return path;
                }

                directory = directory.Parent;
            }
        }
    }

    private bool _skipCurrentAccountRequested = false;
    public string LastFailureMessage { get; private set; } = string.Empty;

    public void SkipCurrentAccount()
    {
        _skipCurrentAccountRequested = true;
    }

    private bool _skipCurrentDownloadRequested = false;
    public void SkipCurrentDownload()
    {
        _skipCurrentDownloadRequested = true;
    }

    private bool _cancelCurrentDownloadRequested = false;
    public void CancelDownload()
    {
        _cancelCurrentDownloadRequested = true;
    }

    public void ResetCancellation()
    {
        _cancelCurrentDownloadRequested = false;
        _skipCurrentAccountRequested = false;
        _skipCurrentDownloadRequested = false;
    }

    public bool IsCancelled()
    {
        return _cancelCurrentDownloadRequested;
    }

    public bool IsCurrentDownloadSkipped()
    {
        return _skipCurrentDownloadRequested;
    }

    public List<string> GetAvailableAccounts()
    {
        return EncryptedAccounts.Select(a => a.Username).ToList();
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
        var match = Regex.Match(input, @"(?:id=|filedetails/)(\d+)");
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        return null;
    }

    public async Task<bool> DownloadAsync(string workshopId, string downloadDir, Action<double, string>? onProgress = null, string? forcedUsername = null)
    {
        LastFailureMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(workshopId)) return false;

        _skipCurrentAccountRequested = false;
        _skipCurrentDownloadRequested = false;

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
                LastFailureMessage = "DepotDownloaderMod.exe not found.";
                return false;
            }
        }

        var accounts = new List<(string Username, string Password)>();
        foreach (var account in EncryptedAccounts)
        {
            try
            {
                accounts.Add((account.Username, Decrypt(account.EncryptedPassword)));
            }
            catch
            {
                // Skip malformed account entries instead of disabling all downloads.
            }
        }

        if (accounts.Count == 0)
        {
            onProgress?.Invoke(0, "Error: No Steam accounts configured.");
            LastFailureMessage = "No Steam accounts configured. Check STEAM_ACCOUNTS in .env.";
            return false;
        }

        if (!string.IsNullOrEmpty(forcedUsername))
        {
            accounts = accounts.Where(a => string.Equals(a.Username, forcedUsername, StringComparison.OrdinalIgnoreCase)).ToList();
            if (accounts.Count == 0)
            {
                onProgress?.Invoke(0, $"Error: Account {forcedUsername} not found.");
                LastFailureMessage = $"Account {forcedUsername} not found.";
                return false;
            }
        }
        else
        {
            accounts = OrderAccountsForFallback(accounts);
        }
        
        string lastError = "All accounts failed";

        foreach (var account in accounts)
        {
            if (_cancelCurrentDownloadRequested) break;
            if (_skipCurrentDownloadRequested) break;
            _skipCurrentAccountRequested = false;
            onProgress?.Invoke(0, $"Connecting to Steam as {account.Username}...");

            var startInfo = new ProcessStartInfo
            {
                FileName = depotExe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(depotExe)
            };
            startInfo.ArgumentList.Add("-app");
            startInfo.ArgumentList.Add(AppId);
            startInfo.ArgumentList.Add("-pubfile");
            startInfo.ArgumentList.Add(workshopId);
            startInfo.ArgumentList.Add("-username");
            startInfo.ArgumentList.Add(account.Username);
            startInfo.ArgumentList.Add("-password");
            startInfo.ArgumentList.Add(account.Password);
            startInfo.ArgumentList.Add("-verify-all");
            startInfo.ArgumentList.Add("-dir");
            startInfo.ArgumentList.Add(targetPath);
            startInfo.ArgumentList.Add("-max-servers");
            startInfo.ArgumentList.Add("30");
            startInfo.ArgumentList.Add("-max-downloads");
            startInfo.ArgumentList.Add("10");

            using var process = new Process { StartInfo = startInfo };
            var outputLines = new List<string>();
            var lastOutputAt = DateTime.UtcNow;
            var receivedAnyOutput = false;

            void HandleOutputLine(string line, bool isError)
            {
                lock (outputLines)
                {
                    outputLines.Add(line);
                    if (outputLines.Count > 50)
                    {
                        outputLines.RemoveAt(0);
                    }
                }

                lastOutputAt = DateTime.UtcNow;
                receivedAnyOutput = true;
                lastError = line;

                var progress = ParseProgress(line);
                if (progress.HasValue)
                {
                    onProgress?.Invoke(Math.Min(progress.Value, 99), NormalizeStatus(line, account.Username));
                    return;
                }

                var normalizedStatus = NormalizeStatus(line, account.Username);
                if (!string.IsNullOrWhiteSpace(normalizedStatus))
                {
                    onProgress?.Invoke(0, normalizedStatus);
                }

                if (LooksLikeAuthFailure(line))
                {
                    lastError = line;
                    try { process.Kill(); } catch { }
                }
            }

            process.OutputDataReceived += (s, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                HandleOutputLine(e.Data, isError: false);
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    HandleOutputLine(e.Data, isError: true);
                }
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var processTask = process.WaitForExitAsync();
                var hardDeadline = DateTime.UtcNow + TimeSpan.FromMinutes(10);

                while (!processTask.IsCompleted)
                {
                    if (_skipCurrentAccountRequested || _skipCurrentDownloadRequested || _cancelCurrentDownloadRequested)
                    {
                        try { process.Kill(); } catch { }
                        var status = _cancelCurrentDownloadRequested
                            ? "Cancelling..."
                            : _skipCurrentDownloadRequested
                                ? "Skipping current download..."
                                : "Skipping current account...";
                        onProgress?.Invoke(0, status);
                        break;
                    }
                    
                    var completed = await Task.WhenAny(processTask, Task.Delay(500));
                    if (completed == processTask) break;

                    if (DateTime.UtcNow > hardDeadline)
                    {
                        try { process.Kill(); } catch { }
                        lastError = "Download timed out after 10 minutes.";
                        onProgress?.Invoke(0, lastError);
                        break;
                    }

                    if (!receivedAnyOutput && DateTime.UtcNow - lastOutputAt > TimeSpan.FromSeconds(45))
                    {
                        try { process.Kill(); } catch { }
                        lastError = "No response from DepotDownloader for 45 seconds.";
                        onProgress?.Invoke(0, $"{lastError} Trying next account...");
                        break;
                    }
                }

                if (_cancelCurrentDownloadRequested) break;
                if (_skipCurrentDownloadRequested) break;
                if (_skipCurrentAccountRequested) continue;

                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
                var completedTask = await Task.WhenAny(processTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    try { process.Kill(); } catch { }
                    onProgress?.Invoke(0, "DepotDownloader did not exit cleanly. Trying next account...");
                    continue;
                }

                if (process.ExitCode == 0)
                {
                    onProgress?.Invoke(100, "Download complete!");
                    return true;
                }
                else
                {
                    lock (outputLines)
                    {
                        lastError = outputLines.Count == 0 ? lastError : string.Join(Environment.NewLine, outputLines.TakeLast(5));
                    }

                    onProgress?.Invoke(0, $"Account {account.Username} failed. Trying next...");
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                continue;
            }
        }

        if (_cancelCurrentDownloadRequested)
        {
            onProgress?.Invoke(0, "Download cancelled.");
            return false;
        }

        if (_skipCurrentDownloadRequested)
        {
            onProgress?.Invoke(0, "Download skipped.");
            return false;
        }

        onProgress?.Invoke(0, $"Error: {lastError}");
        LastFailureMessage = lastError;
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

    private static string NormalizeStatus(string line, string username)
    {
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var lower = trimmed.ToLowerInvariant();
        if (lower.Contains("download") ||
            lower.Contains("depot") ||
            lower.Contains("verif") ||
            lower.Contains("connect") ||
            lower.Contains("login") ||
            lower.Contains("workshop") ||
            lower.Contains("manifest") ||
            lower.Contains("progress") ||
            lower.Contains('%'))
        {
            return $"{trimmed} ({username})";
        }

        return string.Empty;
    }

    private static bool LooksLikeAuthFailure(string line)
    {
        return line.Contains("Login Key Failed", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("Invalid Password", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("Steam Guard", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("Two-factor", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("Authenticator", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("Captcha", StringComparison.OrdinalIgnoreCase);
    }

    private static List<(string Username, string Password)> OrderAccountsForFallback(List<(string Username, string Password)> accounts)
    {
        return accounts
            .Select((account, index) => (account, index))
            .OrderBy(item => string.Equals(item.account.Username, PreferredFirstAccount, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => string.Equals(item.account.Username, PreferredLastAccount, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(item => item.index)
            .Select(item => item.account)
            .ToList();
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
