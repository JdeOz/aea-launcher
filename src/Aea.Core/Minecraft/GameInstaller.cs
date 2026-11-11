using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Aea.Core.Downloads;
using Aea.Core.Releases;
using Aea.Core.Storage;

namespace Aea.Core.Minecraft;

public sealed record PreparedGame(VerifiedRelease Release, string JavaExecutable, string GameDirectory, string Slot,
    string AssetsDirectory, string LibrariesDirectory, string[] Classpath, JsonElement Game, JsonElement Loader);

public sealed class GameInstaller(AppStorage storage, VerifiedDownloads downloads)
{
    public async Task<PreparedGame> Prepare(VerifiedRelease release, IProgress<string>? progress, CancellationToken ct)
    {
        var pack = release.Pack;
        string slot = AppStorage.Inside(storage.Slots, release.Digest);
        Directory.CreateDirectory(slot);
        progress?.Report("Preparando Java…");
        string java = await Java(pack, progress, ct);

        progress?.Report("Verificando Minecraft…");
        string gameFile = Path.Combine(slot, "game.json");
        await downloads.File(pack.GameMetadata, gameFile, ct);
        using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(gameFile, ct));
        var game = document.RootElement.Clone();
        if (game.GetProperty("id").GetString() != pack.Minecraft || game.GetProperty("javaVersion").GetProperty("majorVersion").GetInt32() != pack.JavaMajor)
            throw new InvalidDataException("La versión del juego y Java no coinciden con el pack.");

        string loaderFile = Path.Combine(slot, "fabric.json");
        await downloads.File(pack.FabricMetadata, loaderFile, ct);
        using var loaderDocument = JsonDocument.Parse(await File.ReadAllBytesAsync(loaderFile, ct));
        var loader = loaderDocument.RootElement.Clone();
        if (loader.GetProperty("inheritsFrom").GetString() != pack.Minecraft || loader.GetProperty("mainClass").GetString() != "net.fabricmc.loader.impl.launch.knot.KnotClient")
            throw new InvalidDataException("El perfil de Fabric no corresponde a este pack.");

        string libraries = Path.Combine(storage.Cache, "libraries");
        string assets = Path.Combine(storage.Cache, "assets");
        var classpath = new List<string>();
        var artifacts = new List<(Artifact File, string Target)>();
        var nativeArchives = new List<string>();
        foreach (var library in game.GetProperty("libraries").EnumerateArray().Where(GameRules.Allowed))
        {
            if (!library.TryGetProperty("downloads", out var libraryDownloads))
                throw new InvalidDataException("Biblioteca de Minecraft sin metadatos de descarga.");
            if (libraryDownloads.TryGetProperty("artifact", out var item))
            {
                Artifact artifact = FromMojang(item);
                string target = AppStorage.Inside(libraries, artifact.Hash.ToLowerInvariant() + "/" + artifact.Path);
                artifacts.Add((artifact, target));
                classpath.Add(target);
            }
            if (library.TryGetProperty("natives", out var nativeMap) && nativeMap.TryGetProperty("windows", out var classifier))
            {
                string name = classifier.GetString()!.Replace("${arch}", "64", StringComparison.Ordinal);
                Artifact artifact = FromMojang(libraryDownloads.GetProperty("classifiers").GetProperty(name));
                string target = AppStorage.Inside(libraries, artifact.Hash.ToLowerInvariant() + "/" + artifact.Path);
                artifacts.Add((artifact, target));
                nativeArchives.Add(target);
            }
        }
        foreach (var library in loader.GetProperty("libraries").EnumerateArray().Where(GameRules.Allowed))
        {
            string relative = MavenPath(library.GetProperty("name").GetString()!);
            string url = library.GetProperty("url").GetString()!.TrimEnd('/') + "/" + relative;
            var artifact = pack.FabricLibraries.SingleOrDefault(item => item.Path == relative && item.Url == url)
                ?? throw new InvalidDataException("Una biblioteca Fabric no está fijada por el publicador.");
            string target = AppStorage.Inside(libraries, artifact.Hash.ToLowerInvariant() + "/" + relative);
            artifacts.Add((artifact, target));
            classpath.Add(target);
        }
        if (loader.GetProperty("libraries").GetArrayLength() != pack.FabricLibraries.Length)
            throw new InvalidDataException("La lista fijada de bibliotecas Fabric no coincide con su perfil.");
        var client = FromMojang(game.GetProperty("downloads").GetProperty("client"), "client.jar");
        string clientPath = Path.Combine(slot, "client.jar");
        artifacts.Add((client, clientPath));
        classpath.Add(clientPath);
        if (game.TryGetProperty("logging", out var logging) && logging.TryGetProperty("client", out var clientLogging))
        {
            var config = FromMojang(clientLogging.GetProperty("file"), "logging.xml");
            artifacts.Add((config, Path.Combine(slot, "logging.xml")));
        }
        progress?.Report("Verificando bibliotecas…");
        await Parallel.ForEachAsync(artifacts, new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = ct },
            async (item, token) => await downloads.File(item.File, item.Target, token));
        foreach (string archive in nativeArchives) SafeArchive.Extract(archive, Path.Combine(slot, "natives", "java"), ct, nativeOnly: true);

        var indexArtifact = FromMojang(game.GetProperty("assetIndex"), "index.json");
        string indexId = game.GetProperty("assetIndex").GetProperty("id").GetString()!;
        AppStorage.Identifier(indexId.ToLowerInvariant());
        string indexPath = AppStorage.Inside(assets, "indexes/" + indexId + ".json");
        await downloads.File(indexArtifact, indexPath, ct);
        using var index = JsonDocument.Parse(await File.ReadAllBytesAsync(indexPath, ct));
        if (index.RootElement.TryGetProperty("virtual", out var virtualAssets) && virtualAssets.GetBoolean())
            throw new InvalidDataException("Esta versión antigua de recursos no está soportada.");
        int total = index.RootElement.GetProperty("objects").EnumerateObject().Count();
        int done = 0;
        await Parallel.ForEachAsync(index.RootElement.GetProperty("objects").EnumerateObject(),
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (item, token) =>
            {
                string hash = item.Value.GetProperty("hash").GetString()!;
                if (hash.Length != 40 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Hash de recurso inválido.");
                string relative = "objects/" + hash[..2] + "/" + hash;
                var artifact = new Artifact("https://resources.download.minecraft.net/" + hash[..2] + "/" + hash,
                    relative, "SHA1", hash, item.Value.GetProperty("size").GetInt64());
                await downloads.File(artifact, AppStorage.Inside(assets, relative), token);
                int completed = Interlocked.Increment(ref done);
                if (completed % 100 == 0 || completed == total) progress?.Report($"Preparando recursos: {completed}/{total}");
            });
        progress?.Report("Verificando mods…");
        await Parallel.ForEachAsync(pack.Mods, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            async (mod, token) => await downloads.File(mod, AppStorage.Inside(slot, "mods/" + mod.Path), token));
        ct.ThrowIfCancellationRequested();
        return new(release, java, storage.Instance(pack.PackId), slot, assets, libraries, classpath.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), game, loader);
    }

    private async Task<string> Java(PackRelease pack, IProgress<string>? progress, CancellationToken ct)
    {
        string archive = AppStorage.Inside(storage.Cache, "java/" + pack.JavaArchive.Hash.ToLowerInvariant() + ".zip");
        await downloads.File(pack.JavaArchive, archive, ct);
        string directory = AppStorage.Inside(storage.Cache, "java/" + pack.JavaArchive.Hash.ToLowerInvariant());
        string receipt = Path.Combine(directory, "complete");
        if (!File.Exists(receipt))
        {
            progress?.Report("Extrayendo Java…");
            await Task.Run(() => SafeArchive.Extract(archive, directory, ct), ct);
            AppStorage.AtomicWrite(receipt, System.Text.Encoding.UTF8.GetBytes(pack.JavaArchive.Hash));
        }
        var candidates = Directory.GetFiles(directory, "javaw.exe", SearchOption.AllDirectories);
        if (candidates.Length != 1) throw new InvalidDataException("No se encontró el ejecutable Java del pack.");
        AppStorage.RejectLinks(candidates[0]);
        return candidates[0];
    }

    public static Artifact FromMojang(JsonElement item, string? path = null) => new(item.GetProperty("url").GetString()!,
        path ?? item.GetProperty("path").GetString()!, "SHA1", item.GetProperty("sha1").GetString()!, item.GetProperty("size").GetInt64());

    public static string MavenPath(string coordinate)
    {
        string[] parts = coordinate.Split(':');
        if (parts.Length is < 3 or > 4 || parts.Any(p => !System.Text.RegularExpressions.Regex.IsMatch(p, "^[a-zA-Z0-9_.+-]+$") || p is "." or ".."))
            throw new InvalidDataException("Identificador Maven no compatible.");
        string relative = parts[0].Replace('.', '/') + "/" + parts[1] + "/" + parts[2] + "/" + parts[1] + "-" + parts[2] + (parts.Length == 4 ? "-" + parts[3] : "") + ".jar";
        AppStorage.Inside(Path.GetTempPath(), relative);
        return relative;
    }
}
