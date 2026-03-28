using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HostsManager.Models;

namespace HostsManager.Services;

public class HostsFileService
{
    private const string BeginMarker = "# >>> Hosts Manager managed entries >>>";
    private const string EndMarker = "# <<< Hosts Manager managed entries <<<";

    private readonly string appDataDir;

    public HostsFileService()
    {
        appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HostsManager");
    }

    public string GetHostsFilePath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");
        }

        return "/etc/hosts";
    }

    public string GetBackupFilePath()
    {
        Directory.CreateDirectory(appDataDir);
        return Path.Combine(appDataDir, "hosts.backup");
    }

    public async Task ApplySourcesAsync(IEnumerable<HostProfile> sources, bool allowPrivilegePrompt = false, CancellationToken cancellationToken = default)
    {
        var hostsPath = GetHostsFilePath();
        var backupPath = GetBackupFilePath();

        var original = await File.ReadAllTextAsync(hostsPath, cancellationToken);

        if (!File.Exists(backupPath))
        {
            await File.WriteAllTextAsync(backupPath, original, new UTF8Encoding(false), cancellationToken);
        }

        var cleaned = RemoveManagedBlock(original);
        var managedBlock = BuildManagedBlock(sources);

        var output = string.IsNullOrWhiteSpace(managedBlock)
            ? cleaned.TrimEnd() + Environment.NewLine
            : cleaned.TrimEnd() + Environment.NewLine + Environment.NewLine + managedBlock + Environment.NewLine;

        await WriteHostsFileAsync(hostsPath, output, allowPrivilegePrompt, cancellationToken);
    }

    public async Task RestoreBackupAsync(bool allowPrivilegePrompt = false, CancellationToken cancellationToken = default)
    {
        var hostsPath = GetHostsFilePath();
        var backupPath = GetBackupFilePath();

        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("No backup file exists yet.", backupPath);
        }

        var backupText = await File.ReadAllTextAsync(backupPath, cancellationToken);
        await WriteHostsFileAsync(hostsPath, backupText, allowPrivilegePrompt, cancellationToken);
    }

    public async Task SaveRawHostsFileAsync(string content, bool allowPrivilegePrompt = false, CancellationToken cancellationToken = default)
    {
        var hostsPath = GetHostsFilePath();
        var backupPath = GetBackupFilePath();

        var original = await File.ReadAllTextAsync(hostsPath, cancellationToken);
        if (!File.Exists(backupPath))
        {
            await File.WriteAllTextAsync(backupPath, original, new UTF8Encoding(false), cancellationToken);
        }

        await WriteHostsFileAsync(hostsPath, content ?? string.Empty, allowPrivilegePrompt, cancellationToken);
    }

    public bool ManagedHostsMatch(IEnumerable<HostProfile> sources, string? currentHostsContent)
    {
        var expectedManagedBlock = BuildManagedBlock(sources);
        var actualManagedBlock = ExtractManagedBlock(currentHostsContent ?? string.Empty);
        return string.Equals(
            NormalizeForComparison(actualManagedBlock),
            NormalizeForComparison(expectedManagedBlock),
            StringComparison.Ordinal);
    }

    private static async Task WriteHostsFileAsync(string hostsPath, string content, bool allowPrivilegePrompt, CancellationToken cancellationToken)
    {
        try
        {
            await File.WriteAllTextAsync(hostsPath, content, new UTF8Encoding(false), cancellationToken);
        }
        catch (Exception ex) when (allowPrivilegePrompt && RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && NeedsMacPrivilegePrompt(ex))
        {
            await WriteHostsFileWithMacPrivilegesAsync(hostsPath, content, cancellationToken);
        }
    }

    private static bool NeedsMacPrivilegePrompt(Exception ex) =>
        ex is UnauthorizedAccessException ||
        ex is IOException ||
        ex is System.Security.SecurityException;

    private static async Task WriteHostsFileWithMacPrivilegesAsync(string hostsPath, string content, CancellationToken cancellationToken)
    {
        var tempFilePath = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(tempFilePath, content, new UTF8Encoding(false), cancellationToken);

            var command = $"/usr/bin/install -m 644 {ShellQuote(tempFilePath)} {ShellQuote(hostsPath)}";
            var appleScript = $"do shell script \"{EscapeAppleScriptString(command)}\" with administrator privileges";

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/osascript",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                }
            };

            process.StartInfo.ArgumentList.Add("-e");
            process.StartInfo.ArgumentList.Add(appleScript);

            process.Start();
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(error))
                {
                    error = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                }

                throw new UnauthorizedAccessException(string.IsNullOrWhiteSpace(error)
                    ? "Administrative authorization was canceled or failed."
                    : error.Trim());
            }
        }
        finally
        {
            try
            {
                File.Delete(tempFilePath);
            }
            catch
            {
            }
        }
    }

    private static string ShellQuote(string value)
    {
        return $"'{value.Replace("'", "'\"'\"'")}'";
    }

    private static string EscapeAppleScriptString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string RemoveManagedBlock(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var result = new List<string>();
        var inManaged = false;

        foreach (var line in lines)
        {
            if (!inManaged && line.Trim() == BeginMarker)
            {
                inManaged = true;
                continue;
            }

            if (inManaged && line.Trim() == EndMarker)
            {
                inManaged = false;
                continue;
            }

            if (!inManaged)
            {
                result.Add(line);
            }
        }

        return string.Join(Environment.NewLine, result).TrimEnd();
    }

    private static string BuildManagedBlock(IEnumerable<HostProfile> sources)
    {
        var builder = new StringBuilder();
        builder.AppendLine(BeginMarker);

        foreach (var source in sources.Where(source => source.IsEnabled && !source.IsReadOnly))
        {
            var lines = source.Entries
                .Replace("\r\n", "\n")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line));

            if (!lines.Any())
            {
                continue;
            }

            builder.AppendLine($"# source: {source.Name}");
            foreach (var line in lines)
            {
                builder.AppendLine(line);
            }

            builder.AppendLine();
        }

        builder.Append(EndMarker);
        var block = builder.ToString();

        return block.Contains("# source:", StringComparison.Ordinal)
            ? block
            : string.Empty;
    }

    private static string ExtractManagedBlock(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var result = new List<string>();
        var inManaged = false;

        foreach (var line in lines)
        {
            if (!inManaged && line.Trim() == BeginMarker)
            {
                inManaged = true;
            }

            if (inManaged)
            {
                result.Add(line);
            }

            if (inManaged && line.Trim() == EndMarker)
            {
                break;
            }
        }

        return string.Join(Environment.NewLine, result).Trim();
    }

    private static string NormalizeForComparison(string value)
    {
        return value.Replace("\r\n", "\n").Trim();
    }
}
