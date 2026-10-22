using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace PowerPlugin.Core.Updates;

/// <summary>
/// Checks the update list before anything in it is used - the same scheme as Clavichat.
/// <para>
/// The list is a JSON envelope <c>{ "data": "...", "sig": "..." }</c>: <c>data</c> is the list
/// itself as a string, <c>sig</c> its Ed25519 signature in Base64. Only the publisher holds the
/// private key; wherever the files are stored only stores them. Whoever takes over that place can
/// therefore withhold updates but never smuggle in a program: whatever it serves has to carry a
/// valid signature, and the package has to match the size and SHA-256 named in the signed list.
/// </para>
/// </summary>
public static partial class UpdateManifestReader
{
    /// <summary>Name of the application in the list; a list of another program is refused.</summary>
    public const string AppName = "powerplugin";

    /// <summary>Upper bound for the list itself, so a hostile server cannot hand over megabytes.</summary>
    public const int MaxLength = 65536;

    private const int MaxNotes = 12;
    private const int MaxNoteLength = 400;
    private const long MaxPackageSize = 500L * 1024 * 1024;

    // DER prefix of an Ed25519 public key in SubjectPublicKeyInfo form (RFC 8410); the raw key follows.
    private static readonly byte[] SpkiPrefix = Convert.FromHexString("302a300506032b6570032100");

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,100}\.zip$")]
    private static partial Regex PackageName();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Hex();

    /// <summary>
    /// Verifies the signature with the publisher's key (SPKI/DER, Base64 - the form Clavichat's
    /// update-key.js prints), then every field. Throws <see cref="UpdateException"/> otherwise.
    /// </summary>
    public static UpdateManifest Verify(string text, string publicKeyBase64)
    {
        ArgumentNullException.ThrowIfNull(publicKeyBase64);

        if (string.IsNullOrEmpty(text) || text.Length > MaxLength)
        {
            throw new UpdateException("Die Update-Liste ist ungültig.");
        }

        (string data, string signature) = ReadEnvelope(text);

        if (!SignatureIsValid(data, signature, publicKeyBase64))
        {
            throw new UpdateException("Die Signatur der Update-Liste stimmt nicht - es wird nichts installiert.");
        }

        return ReadData(data);
    }

    private static (string Data, string Signature) ReadEnvelope(string text)
    {
        try
        {
            using JsonDocument envelope = JsonDocument.Parse(text);

            if (envelope.RootElement.ValueKind == JsonValueKind.Object &&
                envelope.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.String &&
                envelope.RootElement.TryGetProperty("sig", out JsonElement sig) && sig.ValueKind == JsonValueKind.String)
            {
                return (data.GetString()!, sig.GetString()!);
            }
        }
        catch (JsonException)
        {
        }

        throw new UpdateException("Die Update-Liste ist ungültig.");
    }

    private static bool SignatureIsValid(string data, string signatureBase64, string publicKeyBase64)
    {
        try
        {
            byte[] spki = Convert.FromBase64String(publicKeyBase64);
            byte[] signature = Convert.FromBase64String(signatureBase64);

            if (spki.Length != SpkiPrefix.Length + Ed25519PublicKeyParameters.KeySize ||
                !spki.AsSpan(0, SpkiPrefix.Length).SequenceEqual(SpkiPrefix) ||
                signature.Length != Ed25519PrivateKeyParameters.SignatureSize)
            {
                return false;
            }

            var key = new Ed25519PublicKeyParameters(spki, SpkiPrefix.Length);
            var verifier = new Ed25519Signer();
            verifier.Init(forSigning: false, key);

            byte[] message = Encoding.UTF8.GetBytes(data);
            verifier.BlockUpdate(message, 0, message.Length);
            return verifier.VerifySignature(signature);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static UpdateManifest ReadData(string data)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(data);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                String(root, "app") != AppName ||
                String(root, "version") is not { } version || !AppVersion.IsValid(version))
            {
                throw new UpdateException("Die Update-Liste gehört nicht zu PowerPlugin.");
            }

            if (!root.TryGetProperty("files", out JsonElement files) || files.ValueKind != JsonValueKind.Object ||
                !files.TryGetProperty("zip", out JsonElement zip) || zip.ValueKind != JsonValueKind.Object ||
                String(zip, "name") is not { } name || !PackageName().IsMatch(name) ||
                String(zip, "sha256") is not { } sha256 || !Sha256Hex().IsMatch(sha256) ||
                !zip.TryGetProperty("size", out JsonElement sizeElement) ||
                sizeElement.ValueKind != JsonValueKind.Number ||
                !sizeElement.TryGetInt64(out long size) || size <= 0 || size > MaxPackageSize)
            {
                throw new UpdateException("Für diese Fassung gibt es keine passende Datei.");
            }

            var notes = new List<string>();
            if (root.TryGetProperty("notes", out JsonElement notesElement) && notesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement note in notesElement.EnumerateArray())
                {
                    if (notes.Count == MaxNotes)
                    {
                        break;
                    }

                    if (note.ValueKind == JsonValueKind.String && note.GetString() is { Length: > 0 } text)
                    {
                        notes.Add(text.Length > MaxNoteLength ? text[..MaxNoteLength] : text);
                    }
                }
            }

            return new UpdateManifest(version, String(root, "date") ?? string.Empty, notes, new UpdateFile(name, size, sha256));
        }
        catch (JsonException exception)
        {
            throw new UpdateException("Die Update-Liste ist ungültig.", exception);
        }
    }

    private static string? String(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
