namespace HostsManager.Desktop.Tests;

public sealed class HostsStateTrackerTests
{
    [Fact]
    public void NeedsConfigurationSave_TracksEditableSourcesAndSettings()
    {
        var tracker = new HostsStateTracker();
        var sources = CreateSources();

        Assert.True(tracker.NeedsConfigurationSave(false, false, sources));

        tracker.MarkConfigurationSaved(false, false, sources);

        Assert.False(tracker.NeedsConfigurationSave(false, false, sources));

        sources[0].Entries = "changed system";
        Assert.False(tracker.NeedsConfigurationSave(false, false, sources));

        sources[1].Entries = "changed remote";
        Assert.True(tracker.NeedsConfigurationSave(false, false, sources));
        Assert.True(tracker.NeedsConfigurationSave(true, false, sources));
    }

    [Fact]
    public void EvaluateManagedApply_SkipsApplyForSignatureAlreadyKnownToMatch()
    {
        var tracker = new HostsStateTracker();
        var sources = CreateSources();

        tracker.InitializeManagedState(sources, managedHostsMatch: true);

        var evaluation = tracker.EvaluateManagedApply(sources, managedHostsMatch: false);

        Assert.False(evaluation.ShouldApply);
        Assert.False(evaluation.HadAppliedChangeBefore);
    }

    [Fact]
    public void EvaluateManagedApply_FlagsReapplyAfterManagedSourceChanges()
    {
        var tracker = new HostsStateTracker();
        var sources = CreateSources();

        tracker.InitializeManagedState(sources, managedHostsMatch: true);
        sources[1].Entries = "10.0.0.5 changed.remote";

        var evaluation = tracker.EvaluateManagedApply(sources, managedHostsMatch: false);

        Assert.True(evaluation.ShouldApply);
        Assert.True(evaluation.HadAppliedChangeBefore);
    }

    [Fact]
    public void MarkManagedApplyAttempted_SuppressesDuplicateRetryForSameSignature()
    {
        var tracker = new HostsStateTracker();
        var sources = CreateSources();

        tracker.InitializeManagedState(sources, managedHostsMatch: true);
        sources[1].Entries = "10.0.0.5 changed.remote";

        var firstEvaluation = tracker.EvaluateManagedApply(sources, managedHostsMatch: false);
        tracker.MarkManagedApplyAttempted(sources);
        var secondEvaluation = tracker.EvaluateManagedApply(sources, managedHostsMatch: false);

        Assert.True(firstEvaluation.ShouldApply);
        Assert.False(secondEvaluation.ShouldApply);
    }

    private static List<HostProfile> CreateSources()
    {
        return
        [
            new HostProfile
            {
                Id = "system-hosts-source",
                Name = "System Hosts",
                SourceType = SourceType.System,
                LocalPath = "C:/Windows/System32/drivers/etc/hosts",
                Entries = "127.0.0.1 localhost",
                IsReadOnly = true
            },
            new HostProfile
            {
                Id = "remote-1",
                Name = "Remote One",
                SourceType = SourceType.Remote,
                RemoteTransport = RemoteTransport.Https,
                RemoteLocation = "https://example.test/hosts",
                Entries = "10.0.0.1 remote.test"
            }
        ];
    }
}
