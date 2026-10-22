using System.Text.RegularExpressions;

namespace PowerPlugin.Core.Updates;

/// <summary>Version numbers of the form major.minor.patch, as the update list uses them.</summary>
public static partial class AppVersion
{
    [GeneratedRegex(@"^\d{1,4}\.\d{1,4}\.\d{1,6}$")]
    private static partial Regex Pattern();

    public static bool IsValid(string? version) => version is not null && Pattern().IsMatch(version);

    /// <summary>True when <paramref name="candidate"/> is a later version than <paramref name="current"/>.</summary>
    public static bool IsNewer(string candidate, string current)
    {
        int[] a = Parts(candidate);
        int[] b = Parts(current);

        for (int i = 0; i < 3; i++)
        {
            if (a[i] != b[i])
            {
                return a[i] > b[i];
            }
        }

        return false;
    }

    /// <summary>The version of a running assembly in update list form (major.minor.patch).</summary>
    public static string Of(Version? version) =>
        version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";

    private static int[] Parts(string version)
    {
        var parts = new int[3];
        string[] pieces = version.Split('.');

        for (int i = 0; i < parts.Length && i < pieces.Length; i++)
        {
            parts[i] = int.TryParse(pieces[i], out int value) ? value : 0;
        }

        return parts;
    }
}
