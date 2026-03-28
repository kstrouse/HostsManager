using System;
using System.Collections.Generic;
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

    public async Task ApplySourcesAsync(IEnumerable<HostProfile> sources, CancellationToken cancellationToken = default)
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

        await File.WriteAllTextAsync(hostsPath, output, new UTF8Encoding(false), cancellationToken);
    }

    public async Task RestoreBackupAsync(CancellationToken cancellationToken = default)
    {
        var hostsPath = GetHostsFilePath();
        var backupPath = GetBackupFilePath();

        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("No backup file exists yet.", backupPath);
        }

        var backupText = await File.ReadAllTextAsync(backupPath, cancellationToken);
        await File.WriteAllTextAsync(hostsPath, backupText, new UTF8Encoding(false), cancellationToken);
    }

    public async Task SaveRawHostsFileAsync(string content, CancellationToken cancellationToken = default)
    {
        var hostsPath = GetHostsFilePath();
        var backupPath = GetBackupFilePath();

        var original = await File.ReadAllTextAsync(hostsPath, cancellationToken);
        if (!File.Exists(backupPath))
        {
            await File.WriteAllTextAsync(backupPath, original, new UTF8Encoding(false), cancellationToken);
        }

        await File.WriteAllTextAsync(hostsPath, content ?? string.Empty, new UTF8Encoding(false), cancellationToken);
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
}
