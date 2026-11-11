using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aea.Core.Storage;

namespace Aea.Core.Releases;

public sealed record Artifact(string Url, string Path, string Algorithm, string Hash, long Size);
public sealed record PackRelease(int Schema, string PackId, string Version, string Name, string Minecraft,
    Artifact GameMetadata, string Fabric, Artifact FabricMetadata, Artifact[] FabricLibraries, int JavaMajor, Artifact JavaArchive, Artifact[] Mods, string? Server);
public sealed record SignedRelease(string Payload, string Signature);
public sealed record VerifiedRelease(PackRelease Pack, SignedRelease Envelope, string Digest);
public sealed record Brand(string Name, string MicrosoftClientId, string UpdateUrl, string PublicKey);

public static partial class ReleaseSecurity
{
    public static VerifiedRelease Verify(SignedRelease envelope, string publicKey)
    {
        try
        {
            byte[] payload = Convert.FromBase64String(envelope.Payload);
            if (payload.Length > 2 * 1024 * 1024) throw new InvalidDataException("Manifiesto demasiado grande.");
            using var key = ECDsa.Create();
            key.ImportFromPem(publicKey);
            if (!key.VerifyData(payload, Convert.FromBase64String(envelope.Signature), HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw new InvalidDataException("La actualización no tiene una firma válida.");
            var pack = JsonSerializer.Deserialize<PackRelease>(payload, AppStorage.Json)
                ?? throw new InvalidDataException("Falta el manifiesto del pack.");
            Validate(pack);
            return new(pack, envelope, Convert.ToHexStringLower(SHA256.HashData(payload)));
        }
        catch (Exception error) when (error is CryptographicException or FormatException or JsonException or ArgumentException)
        { throw new InvalidDataException("No se pudo verificar la firma del pack.", error); }
    }

    public static SignedRelease Sign(PackRelease pack, string privateKey)
    {
        Validate(pack);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(pack, AppStorage.Json);
        using var key = ECDsa.Create();
        key.ImportFromPem(privateKey);
        return new(Convert.ToBase64String(payload), Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
    }

    public static Version VersionNumber(string value) => VersionPattern().IsMatch(value) && Version.TryParse(value, out var version)
        ? version : throw new InvalidDataException("La versión del pack debe ser mayor.menor.parche.");

    public static void Validate(PackRelease pack)
    {
        if (pack.Schema != 1) throw new InvalidDataException("Formato de actualización no compatible.");
        AppStorage.Identifier(pack.PackId);
        VersionNumber(pack.Version);
        if (string.IsNullOrWhiteSpace(pack.Name) || pack.Name.Length > 80 || !SimpleVersion().IsMatch(pack.Minecraft) || !SimpleVersion().IsMatch(pack.Fabric)
            || pack.JavaMajor is < 17 or > 30 || pack.Mods == null || pack.Mods.Length > 500)
            throw new InvalidDataException("Configuración del pack inválida.");
        ValidateArtifact(pack.GameMetadata);
        ValidateArtifact(pack.FabricMetadata);
        ValidateArtifact(pack.JavaArchive);
        if (!pack.GameMetadata.Url.StartsWith("https://piston-meta.mojang.com/", StringComparison.Ordinal))
            throw new InvalidDataException("Los metadatos de Minecraft deben ser oficiales.");
        if (pack.FabricMetadata.Url != $"https://meta.fabricmc.net/v2/versions/loader/{pack.Minecraft}/{pack.Fabric}/profile/json")
            throw new InvalidDataException("Los metadatos de Fabric no corresponden al pack.");
        if (pack.FabricLibraries is not { Length: > 0 and <= 100 }) throw new InvalidDataException("Faltan bibliotecas de Fabric.");
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in pack.FabricLibraries)
        {
            ValidateArtifact(library);
            if (!libraries.Add(library.Path)) throw new InvalidDataException("Biblioteca Fabric duplicada.");
        }
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in pack.Mods)
        {
            ValidateArtifact(mod);
            if (!mod.Path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || mod.Path.Contains('/') || !paths.Add(mod.Path))
                throw new InvalidDataException("Los mods contienen nombres inválidos o duplicados.");
        }
        if (pack.Server != null && !ServerPattern().IsMatch(pack.Server)) throw new InvalidDataException("Dirección del servidor inválida.");
    }

    public static void ValidateArtifact(Artifact artifact)
    {
        Downloads.NetworkPolicy.Validate(artifact.Url);
        AppStorage.Inside(Path.GetTempPath(), artifact.Path);
        int length = artifact.Algorithm switch { "SHA1" => 40, "SHA256" => 64, "SHA512" => 128, _ => 0 };
        if (length == 0 || artifact.Hash.Length != length || !artifact.Hash.All(Uri.IsHexDigit) || artifact.Size is <= 0 or > 2L * 1024 * 1024 * 1024)
            throw new InvalidDataException("Falta la integridad de una descarga.");
    }

    [GeneratedRegex("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$")]
    private static partial Regex VersionPattern();
    [GeneratedRegex("^[0-9][a-zA-Z0-9.+_-]{0,60}$")]
    private static partial Regex SimpleVersion();
    [GeneratedRegex("^[a-zA-Z0-9.-]+(?::[0-9]{1,5})?$")]
    private static partial Regex ServerPattern();
}
