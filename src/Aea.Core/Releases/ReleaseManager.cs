using System.Text.Json;
using Aea.Core.Downloads;
using Aea.Core.Minecraft;
using Aea.Core.Storage;

namespace Aea.Core.Releases;

public sealed class ReleaseManager(AppStorage storage, VerifiedDownloads downloads, string publicKey)
{
    public VerifiedRelease Load(SignedRelease bundled)
    {
        var release = ReleaseSecurity.Verify(bundled, publicKey);
        string active = storage.ActiveFile(release.Pack.PackId);
        if (!File.Exists(active)) return release;
        AppStorage.RejectLinks(active);
        var installed = ReleaseSecurity.Verify(JsonSerializer.Deserialize<SignedRelease>(File.ReadAllBytes(active), AppStorage.Json)!, publicKey);
        if (installed.Pack.PackId != release.Pack.PackId) throw new InvalidDataException("El pack instalado no coincide con este launcher.");
        return ReleaseSecurity.VersionNumber(installed.Pack.Version) >= ReleaseSecurity.VersionNumber(release.Pack.Version) ? installed : release;
    }

    public async Task<VerifiedRelease?> Check(VerifiedRelease current, string updateUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(updateUrl)) return null;
        byte[] json = await downloads.Metadata(updateUrl, ct, 3 * 1024 * 1024);
        var candidate = ReleaseSecurity.Verify(JsonSerializer.Deserialize<SignedRelease>(json, AppStorage.Json)!, publicKey);
        if (candidate.Pack.PackId != current.Pack.PackId) throw new InvalidDataException("La actualización pertenece a otro pack.");
        var next = ReleaseSecurity.VersionNumber(candidate.Pack.Version);
        var previous = ReleaseSecurity.VersionNumber(current.Pack.Version);
        if (next == previous && candidate.Digest != current.Digest) throw new InvalidDataException("El contenido publicado cambió sin subir la versión.");
        return next > previous ? candidate : null;
    }

    public async Task<PreparedGame> Install(VerifiedRelease release, IProgress<string>? progress, CancellationToken ct)
    {
        var prepared = await new GameInstaller(storage, downloads).Prepare(release, progress, ct);
        Activate(prepared, ct);
        return prepared;
    }

    private void Activate(PreparedGame prepared, CancellationToken ct)
    {
        // Only the envelope pointer changes. Slots and personal instance files are never replaced.
        ct.ThrowIfCancellationRequested();
        AppStorage.AtomicWrite(storage.ActiveFile(prepared.Release.Pack.PackId), JsonSerializer.SerializeToUtf8Bytes(prepared.Release.Envelope, AppStorage.Json));
    }
}
