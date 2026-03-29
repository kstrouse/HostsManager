using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HostsManager.Desktop.Models;

namespace HostsManager.Desktop.Services;

public sealed class ProfilePersistenceService : IProfilePersistenceService
{
    private readonly IProfileStore profileStore;
    private readonly IHostsStateTracker hostsStateTracker;

    public ProfilePersistenceService(IProfileStore profileStore, IHostsStateTracker hostsStateTracker)
    {
        this.profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        this.hostsStateTracker = hostsStateTracker ?? throw new ArgumentNullException(nameof(hostsStateTracker));
    }

    public Task SaveConfigurationAsync(
        bool minimizeToTrayOnClose,
        bool runAtStartup,
        IEnumerable<HostProfile> profiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        return profileStore.SaveAsync(
            BuildConfiguration(minimizeToTrayOnClose, runAtStartup, profiles),
            cancellationToken);
    }

    public async Task SaveConfigurationAndMarkSavedAsync(
        bool minimizeToTrayOnClose,
        bool runAtStartup,
        IEnumerable<HostProfile> profiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        await SaveConfigurationAsync(minimizeToTrayOnClose, runAtStartup, profiles, cancellationToken);
        hostsStateTracker.MarkConfigurationSaved(minimizeToTrayOnClose, runAtStartup, profiles);
    }

    private static AppConfig BuildConfiguration(
        bool minimizeToTrayOnClose,
        bool runAtStartup,
        IEnumerable<HostProfile> profiles)
    {
        return new AppConfig
        {
            MinimizeToTrayOnClose = minimizeToTrayOnClose,
            RunAtStartup = runAtStartup,
            Profiles = profiles.Where(source => !source.IsReadOnly).ToList()
        };
    }
}
