using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HostsManager.Models;

namespace HostsManager.Services;

public class ProfileStore : IProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ProfileStore(string? configDirectory = null)
    {
        ConfigDirectory = configDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HostsManager");
    }

    public string ConfigDirectory { get; }

    public string ProfilesFilePath => Path.Combine(ConfigDirectory, "profiles.json");

    public async Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        EnsureDirectory();

        if (!File.Exists(ProfilesFilePath))
        {
            return new AppConfig();
        }

        var json = await File.ReadAllTextAsync(ProfilesFilePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new AppConfig();
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            var profiles = JsonSerializer.Deserialize<List<HostProfile>>(json, JsonOptions);
            return new AppConfig
            {
                Profiles = profiles ?? []
            };
        }

        var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
        return config ?? new AppConfig();
    }

    public async Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        EnsureDirectory();

        await using var stream = File.Create(ProfilesFilePath);
        await JsonSerializer.SerializeAsync(stream, config, JsonOptions, cancellationToken);
    }

    private void EnsureDirectory()
    {
        Directory.CreateDirectory(ConfigDirectory);
    }
}
