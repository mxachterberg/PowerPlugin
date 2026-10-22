using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using PowerPlugin.Core.Updates;
using Xunit;

namespace PowerPlugin.Tests;

public sealed class UpdateManifestTests
{
    // Signed with Node's crypto.sign - the same call Clavichat's publish-update.js uses - and a
    // throwaway key, so the reader is checked against the real signing side.
    private const string NodePublicKey = "MCowBQYDK2VwAyEALN6exuOvykECgqrp/6gthkCO1ImWkqxL3Rwp1x9y2yE=";
    private const string NodeEnvelope = """
        {
         "data": "{\"app\":\"powerplugin\",\"version\":\"1.0.1\",\"date\":\"2026-10-22\",\"notes\":[\"Erster Punkt\",\"Zweiter Punkt\"],\"files\":{\"zip\":{\"name\":\"PowerPlugin-1.0.1-win-x64.zip\",\"size\":1234567,\"sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}}}",
         "sig": "V1w6w82MxvttnfiyunbJWnfWl0WXy/jHt/Kbs1JLtiIfRN7prQ/TP7jwDDgewC1J2RGxc71dHZgV+4usfXpLBQ=="
        }
        """;

    [Fact]
    public void AListSignedByNodeIsAccepted()
    {
        UpdateManifest manifest = UpdateManifestReader.Verify(NodeEnvelope, NodePublicKey);

        Assert.Equal("1.0.1", manifest.Version);
        Assert.Equal(["Erster Punkt", "Zweiter Punkt"], manifest.Notes);
        Assert.Equal("PowerPlugin-1.0.1-win-x64.zip", manifest.Package.Name);
        Assert.Equal(1234567, manifest.Package.Size);
    }

    [Fact]
    public void AChangedListIsRefused()
    {
        string tampered = NodeEnvelope.Replace("1234567", "1234568", StringComparison.Ordinal);

        var error = Assert.Throws<UpdateException>(() => UpdateManifestReader.Verify(tampered, NodePublicKey));
        Assert.Contains("Signatur", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AListSignedWithAnotherKeyIsRefused()
    {
        (_, Func<string, string> sign) = NewKey();

        string envelope = sign(ValidData());
        Assert.Throws<UpdateException>(() => UpdateManifestReader.Verify(envelope, NodePublicKey));
    }

    [Fact]
    public void AListOfAnotherProgramIsRefused()
    {
        // Clavichat is signed with the same publisher key; its list must not pass for PowerPlugin's.
        (string publicKey, Func<string, string> sign) = NewKey();
        string data = ValidData().Replace("\"powerplugin\"", "\"clavichat\"", StringComparison.Ordinal);

        var error = Assert.Throws<UpdateException>(() => UpdateManifestReader.Verify(sign(data), publicKey));
        Assert.Contains("PowerPlugin", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"name\":\"PowerPlugin-1.0.1-win-x64.zip\"", "\"name\":\"..\\\\evil.zip\"")]
    [InlineData("\"name\":\"PowerPlugin-1.0.1-win-x64.zip\"", "\"name\":\"PowerPlugin.exe\"")]
    [InlineData("\"size\":1234567", "\"size\":-1")]
    [InlineData("\"sha256\":\"aaaa", "\"sha256\":\"AAAA")]
    [InlineData("\"version\":\"1.0.1\"", "\"version\":\"1.0\"")]
    public void SignedButImplausibleFieldsAreRefused(string original, string replacement)
    {
        (string publicKey, Func<string, string> sign) = NewKey();
        string data = ValidData().Replace(original, replacement, StringComparison.Ordinal);

        Assert.Throws<UpdateException>(() => UpdateManifestReader.Verify(sign(data), publicKey));
    }

    [Theory]
    [InlineData("")]
    [InlineData("kein json")]
    [InlineData("{\"data\":1,\"sig\":\"x\"}")]
    public void GarbageIsRefused(string text)
    {
        Assert.Throws<UpdateException>(() => UpdateManifestReader.Verify(text, NodePublicKey));
    }

    [Theory]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("1.10.0", "1.9.9", true)]
    [InlineData("2.0.0", "1.99.99", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.1", false)]
    public void VersionsCompareNumerically(string candidate, string current, bool newer)
    {
        Assert.Equal(newer, AppVersion.IsNewer(candidate, current));
    }

    [Fact]
    public void TheAssemblyVersionIsShortenedToThreeParts()
    {
        Assert.Equal("1.2.3", AppVersion.Of(new Version(1, 2, 3, 0)));
        Assert.Equal("1.2.0", AppVersion.Of(new Version(1, 2)));
    }

    private static string ValidData() =>
        "{\"app\":\"powerplugin\",\"version\":\"1.0.1\",\"date\":\"2026-10-22\",\"notes\":[\"Punkt\"]," +
        "\"files\":{\"zip\":{\"name\":\"PowerPlugin-1.0.1-win-x64.zip\",\"size\":1234567,\"sha256\":\"" +
        new string('a', 64) + "\"}}}";

    /// <summary>A fresh key pair: the public key in SPKI/Base64 and a function that signs a list.</summary>
    private static (string PublicKey, Func<string, string> Sign) NewKey()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var pair = generator.GenerateKeyPair();

        var publicKey = (Ed25519PublicKeyParameters)pair.Public;
        byte[] spki = [.. Convert.FromHexString("302a300506032b6570032100"), .. publicKey.GetEncoded()];

        string Sign(string data)
        {
            var signer = new Ed25519Signer();
            signer.Init(forSigning: true, pair.Private);
            byte[] message = Encoding.UTF8.GetBytes(data);
            signer.BlockUpdate(message, 0, message.Length);
            string sig = Convert.ToBase64String(signer.GenerateSignature());
            return JsonSerializer.Serialize(new { data, sig });
        }

        return (Convert.ToBase64String(spki), Sign);
    }
}
