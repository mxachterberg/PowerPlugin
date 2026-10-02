namespace PowerPlugin.Core.Configuration;

/// <summary>How the program is started together with Windows.</summary>
public enum AutostartMode
{
    /// <summary>Not started automatically.</summary>
    Disabled,

    /// <summary>
    /// Through the per user Run key. Needs no administrator rights to set up, but the program
    /// then runs unelevated and the CPU power sensors stay unavailable.
    /// </summary>
    Standard,

    /// <summary>
    /// Through a scheduled task that runs with the highest available privileges. Setting it up
    /// needs administrator rights once; afterwards the program starts elevated without a prompt,
    /// which is the only way to have autostart and CPU sensors at the same time.
    /// </summary>
    Elevated,
}

/// <summary>Whether the configured autostart will actually run, and if not, why.</summary>
public enum AutostartState
{
    /// <summary>Nothing is registered.</summary>
    Disabled,

    /// <summary>Registered and nothing stands in the way.</summary>
    Active,

    /// <summary>
    /// The Run entry exists but Windows has switched it off, through the autostart tab of the
    /// task manager or the startup page of the settings app.
    /// </summary>
    BlockedByWindows,

    /// <summary>
    /// The executable carries the "run as administrator" compatibility flag. Explorer processes
    /// the Run key unelevated and silently skips entries that would need elevation, so the
    /// program never starts.
    /// </summary>
    BlockedByElevationFlag,

    /// <summary>Both mechanisms are registered, usually after setting one up by hand.</summary>
    Conflicting,
}

/// <summary>
/// What the system actually looks like. Gathered by the Windows layer, interpreted here.
/// </summary>
/// <param name="HasRunEntry">A value for the program exists under the per user Run key.</param>
/// <param name="HasScheduledTask">A logon task for the program is registered.</param>
/// <param name="DisabledInTaskManager">Windows marked the Run entry as switched off.</param>
/// <param name="RunAsAdminFlagSet">The executable carries the elevation compatibility flag.</param>
public sealed record AutostartFacts(
    bool HasRunEntry,
    bool HasScheduledTask,
    bool DisabledInTaskManager,
    bool RunAsAdminFlagSet)
{
    public static AutostartFacts None { get; } = new(false, false, false, false);
}

/// <summary>
/// Turns the observed facts into a mode and a verdict.
/// <para>
/// Worth spelling out because the interactions are not obvious: a scheduled task is immune to
/// both obstacles - it starts the program elevated itself, so the compatibility flag is moot, and
/// the task manager's autostart list does not govern it. The Run key is subject to both.
/// </para>
/// </summary>
public static class AutostartStatus
{
    public static AutostartMode ResolveMode(AutostartFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        // The task takes precedence: when both exist it is the one that actually starts the
        // program, elevated, while the Run entry is skipped.
        if (facts.HasScheduledTask)
        {
            return AutostartMode.Elevated;
        }

        return facts.HasRunEntry ? AutostartMode.Standard : AutostartMode.Disabled;
    }

    public static AutostartState ResolveState(AutostartFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (facts.HasScheduledTask)
        {
            return facts.HasRunEntry ? AutostartState.Conflicting : AutostartState.Active;
        }

        if (!facts.HasRunEntry)
        {
            return AutostartState.Disabled;
        }

        // Windows switching the entry off beats the compatibility flag: even without the flag
        // the program would stay down.
        if (facts.DisabledInTaskManager)
        {
            return AutostartState.BlockedByWindows;
        }

        return facts.RunAsAdminFlagSet ? AutostartState.BlockedByElevationFlag : AutostartState.Active;
    }

    /// <summary>A sentence for the settings page, naming the obstacle where there is one.</summary>
    public static string Describe(AutostartFacts facts) => ResolveState(facts) switch
    {
        AutostartState.Disabled =>
            "PowerPlugin startet nicht automatisch mit Windows.",

        AutostartState.Active when facts.HasScheduledTask =>
            "PowerPlugin startet über eine geplante Aufgabe mit der Anmeldung - mit Administratorrechten " +
            "und ohne Rückfrage. Die CPU-Sensoren stehen damit auch nach einem Neustart zur Verfügung.",

        AutostartState.Active =>
            "PowerPlugin startet mit der Anmeldung, ohne Administratorrechte. Die CPU-Leistung wird " +
            "dabei geschätzt.",

        AutostartState.BlockedByWindows =>
            "Der Autostart-Eintrag ist vorhanden, aber Windows hat ihn abgeschaltet - im Task-Manager " +
            "unter \"Autostart-Apps\" oder in den Einstellungen unter \"Apps → Autostart\". Dort wieder " +
            "aktivieren, sonst startet PowerPlugin nicht mit.",

        AutostartState.BlockedByElevationFlag =>
            "Der Autostart-Eintrag ist vorhanden, läuft aber ins Leere: Die Programmdatei ist als " +
            "\"als Administrator ausführen\" markiert, und solche Einträge überspringt Windows beim " +
            "Anmelden stillschweigend. Abhilfe: oben \"mit Administratorrechten starten\" wählen, dann " +
            "übernimmt eine geplante Aufgabe den Start.",

        _ =>
            "Autostart ist doppelt eingerichtet - über eine geplante Aufgabe und zusätzlich über den " +
            "Registry-Eintrag. Gestartet wird über die Aufgabe; der Registry-Eintrag kann weg.",
    };
}
