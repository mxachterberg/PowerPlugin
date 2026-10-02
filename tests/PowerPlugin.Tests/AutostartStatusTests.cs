using PowerPlugin.Core.Configuration;
using Xunit;

namespace PowerPlugin.Tests;

/// <summary>
/// The autostart has two mechanisms and two ways of being vetoed without anyone touching the
/// entry. These tests pin down which combination means what.
/// </summary>
public sealed class AutostartStatusTests
{
    [Fact]
    public void NothingRegisteredIsDisabled()
    {
        Assert.Equal(AutostartMode.Disabled, AutostartStatus.ResolveMode(AutostartFacts.None));
        Assert.Equal(AutostartState.Disabled, AutostartStatus.ResolveState(AutostartFacts.None));
    }

    [Fact]
    public void APlainRunEntryIsActive()
    {
        var facts = new AutostartFacts(HasRunEntry: true, HasScheduledTask: false,
            DisabledInTaskManager: false, RunAsAdminFlagSet: false);

        Assert.Equal(AutostartMode.Standard, AutostartStatus.ResolveMode(facts));
        Assert.Equal(AutostartState.Active, AutostartStatus.ResolveState(facts));
    }

    /// <summary>
    /// The case that started all this: the entry is present and correct, but the executable is
    /// marked "run as administrator" and Windows skips it at logon without a word.
    /// </summary>
    [Fact]
    public void TheElevationFlagSilentlyBreaksTheRunEntry()
    {
        var facts = new AutostartFacts(HasRunEntry: true, HasScheduledTask: false,
            DisabledInTaskManager: false, RunAsAdminFlagSet: true);

        Assert.Equal(AutostartMode.Standard, AutostartStatus.ResolveMode(facts));
        Assert.Equal(AutostartState.BlockedByElevationFlag, AutostartStatus.ResolveState(facts));

        Assert.Contains("als Administrator ausführen", AutostartStatus.Describe(facts), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryTurnedOffInTheTaskManagerIsReported()
    {
        var facts = new AutostartFacts(HasRunEntry: true, HasScheduledTask: false,
            DisabledInTaskManager: true, RunAsAdminFlagSet: false);

        Assert.Equal(AutostartState.BlockedByWindows, AutostartStatus.ResolveState(facts));
        Assert.Contains("Task-Manager", AutostartStatus.Describe(facts), StringComparison.Ordinal);
    }

    [Fact]
    public void BeingTurnedOffWinsOverTheElevationFlag()
    {
        // Both obstacles at once: removing only the flag would not help, so the message has to
        // name the one the user must clear first.
        var facts = new AutostartFacts(HasRunEntry: true, HasScheduledTask: false,
            DisabledInTaskManager: true, RunAsAdminFlagSet: true);

        Assert.Equal(AutostartState.BlockedByWindows, AutostartStatus.ResolveState(facts));
    }

    [Fact]
    public void AScheduledTaskIsImmuneToBothObstacles()
    {
        // The task starts the program elevated itself, so the compatibility flag is moot, and the
        // autostart list of the task manager does not govern scheduled tasks.
        var facts = new AutostartFacts(HasRunEntry: false, HasScheduledTask: true,
            DisabledInTaskManager: true, RunAsAdminFlagSet: true);

        Assert.Equal(AutostartMode.Elevated, AutostartStatus.ResolveMode(facts));
        Assert.Equal(AutostartState.Active, AutostartStatus.ResolveState(facts));
    }

    [Fact]
    public void BothMechanismsAtOnceAreFlaggedAsConflicting()
    {
        var facts = new AutostartFacts(HasRunEntry: true, HasScheduledTask: true,
            DisabledInTaskManager: false, RunAsAdminFlagSet: false);

        // The task is what actually starts the program, so the mode follows it.
        Assert.Equal(AutostartMode.Elevated, AutostartStatus.ResolveMode(facts));
        Assert.Equal(AutostartState.Conflicting, AutostartStatus.ResolveState(facts));
    }

    [Theory]
    [InlineData(false, false, AutostartMode.Disabled)]
    [InlineData(true, false, AutostartMode.Standard)]
    [InlineData(true, true, AutostartMode.Elevated)]
    public void TheTwoSwitchesMapToOneMode(bool enabled, bool elevated, AutostartMode expected)
    {
        var settings = new AppSettings { StartWithWindows = enabled, StartWithWindowsElevated = elevated };

        Assert.Equal(expected, settings.AutostartMode);
    }

    [Fact]
    public void TheElevatedSwitchDoesNothingWhileAutostartIsOff()
    {
        var settings = new AppSettings { StartWithWindows = false, StartWithWindowsElevated = true };

        Assert.Equal(AutostartMode.Disabled, settings.AutostartMode);
    }

    [Fact]
    public void BothSwitchesSurviveARoundTrip()
    {
        string file = Path.Combine(Path.GetTempPath(), $"powerplugin-autostart-{Guid.NewGuid():N}.json");

        try
        {
            var store = new SettingsStore(file);
            store.Save(new AppSettings { StartWithWindows = true, StartWithWindowsElevated = true });

            AppSettings read = store.Load();

            Assert.True(read.StartWithWindows);
            Assert.True(read.StartWithWindowsElevated);
            Assert.Equal(AutostartMode.Elevated, read.AutostartMode);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void EveryStateHasItsOwnExplanation()
    {
        AutostartFacts[] all =
        [
            AutostartFacts.None,
            new(true, false, false, false),
            new(true, false, false, true),
            new(true, false, true, false),
            new(false, true, false, false),
            new(true, true, false, false),
        ];

        string[] messages = all.Select(AutostartStatus.Describe).ToArray();

        Assert.All(messages, m => Assert.False(string.IsNullOrWhiteSpace(m)));
        Assert.Equal(messages.Length, messages.Distinct().Count());
    }
}
