namespace PowerPlugin.Core.Updates;

/// <summary>One downloadable file of a release, as the signed update list announces it.</summary>
public sealed record UpdateFile(string Name, long Size, string Sha256);

/// <summary>
/// The newest release according to the signed update list: version, release notes and the
/// package. Only ever built by <see cref="UpdateManifestReader"/>, after the signature and every
/// field have been checked.
/// </summary>
public sealed record UpdateManifest(string Version, string Date, IReadOnlyList<string> Notes, UpdateFile Package);

/// <summary>A reason not to update, worded for the user.</summary>
public sealed class UpdateException : Exception
{
    public UpdateException(string message)
        : base(message)
    {
    }

    public UpdateException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
