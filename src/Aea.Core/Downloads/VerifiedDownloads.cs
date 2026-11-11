using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Aea.Core.Releases;
using Aea.Core.Storage;

namespace Aea.Core.Downloads;

public static class NetworkPolicy
{
    private static readonly HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "piston-meta.mojang.com", "piston-data.mojang.com", "launchermeta.mojang.com", "launcher.mojang.com",
        "libraries.minecraft.net", "resources.download.minecraft.net", "meta.fabricmc.net", "maven.fabricmc.net",
        "repo1.maven.org", "repo.maven.apache.org", "api.modrinth.com", "cdn.modrinth.com", "api.adoptium.net",
        "github.com", "api.github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com", "raw.githubusercontent.com"
    };
    public static Uri Validate(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || !Hosts.Contains(uri.IdnHost) || uri.Fragment.Length != 0)
            throw new InvalidDataException("Descarga fuera de los proveedores HTTPS permitidos.");
        return uri;
    }
}

public sealed class VerifiedDownloads : IDisposable
{
    private readonly HttpClient client;
    public VerifiedDownloads(HttpMessageHandler? handler = null)
    {
        client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None });
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AeaLauncher/1.0");
    }

    private async Task<HttpResponseMessage> Get(string url, CancellationToken ct)
    {
        Uri uri = NetworkPolicy.Validate(url);
        for (int count = 0; count < 8; count++)
        {
            var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                var next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                response.Dispose();
                uri = NetworkPolicy.Validate(next.AbsoluteUri);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                int status = (int)response.StatusCode;
                response.Dispose();
                throw new LauncherException("Descarga", $"El proveedor rechazó una descarga (HTTP {status}). Reinténtalo más tarde.", status);
            }
            return response;
        }
        throw new LauncherException("Descarga", "Demasiadas redirecciones al descargar.");
    }

    public async Task<byte[]> Metadata(string url, CancellationToken ct, int maximumBytes = 8 * 1024 * 1024)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            using var response = await Get(url, timeout.Token);
            using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var output = new MemoryStream();
            byte[] buffer = new byte[65536];
            int count;
            while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (output.Length + count > maximumBytes) throw new InvalidDataException("Respuesta de metadatos demasiado grande.");
                await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
            }
            return output.ToArray();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new LauncherException("Descarga", "El proveedor tardó demasiado en responder. Comprueba la conexión y reintenta."); }
    }

    public async Task<JsonDocument> Json(string url, CancellationToken ct) => JsonDocument.Parse(await Metadata(url, ct));

    public async Task File(Artifact artifact, string target, CancellationToken ct)
    {
        ReleaseSecurity.ValidateArtifact(artifact);
        AppStorage.RejectLinks(target);
        if (await IsValid(artifact, target, ct)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(15));
            using var response = await Get(artifact.Url, timeout.Token);
            if (response.Content.Headers.ContentLength is { } length && length != artifact.Size)
                throw new InvalidDataException("El tamaño anunciado de la descarga cambió.");
            using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var hash = IncrementalHash.CreateHash(new(artifact.Algorithm));
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                byte[] buffer = new byte[65536];
                long received = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    received += count;
                    if (received > artifact.Size) throw new InvalidDataException("La descarga supera el tamaño permitido.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                }
                if (received != artifact.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(artifact.Hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("El archivo descargado no superó la verificación de integridad.");
                await output.FlushAsync(timeout.Token);
            }
            ct.ThrowIfCancellationRequested();
            AppStorage.RejectLinks(target);
            System.IO.File.Move(temporary, target, true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new LauncherException("Descarga", "La descarga agotó su tiempo de espera. La versión anterior se conserva; puedes reintentar."); }
        finally { if (System.IO.File.Exists(temporary)) System.IO.File.Delete(temporary); }
    }

    public static async Task<bool> IsValid(Artifact artifact, string path, CancellationToken ct)
    {
        AppStorage.RejectLinks(path);
        if (!System.IO.File.Exists(path) || new FileInfo(path).Length != artifact.Size) return false;
        await using var input = System.IO.File.OpenRead(path);
        using var algorithm = IncrementalHash.CreateHash(new(artifact.Algorithm));
        byte[] buffer = new byte[65536];
        int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0) algorithm.AppendData(buffer, 0, count);
        return Convert.ToHexString(algorithm.GetHashAndReset()).Equals(artifact.Hash, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => client.Dispose();
}
