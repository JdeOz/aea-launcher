using System.Security.Cryptography;
using System.Text.Json;
using Aea.Core.Downloads;
using Aea.Core.Minecraft;
using Aea.Core.Releases;
using Aea.Core.Storage;

return await Publisher.Run(args);

static class Publisher
{
    public static async Task<int> Run(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "keygen")
            {
                string path = Path.GetFullPath(args[1]);
                if (File.Exists(path)) throw new InvalidOperationException("La clave ya existe; no se reemplazará.");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                byte[] privateBytes = System.Text.Encoding.UTF8.GetBytes(key.ExportPkcs8PrivateKeyPem());
                try { AppStorage.AtomicWrite(path, ProtectedData.Protect(privateBytes, null, DataProtectionScope.CurrentUser)); }
                finally { CryptographicOperations.ZeroMemory(privateBytes); }
                File.WriteAllText(path + ".public.pem", key.ExportSubjectPublicKeyInfoPem());
                Console.WriteLine("Clave privada cifrada para tu usuario de Windows. Solo se distribuye .public.pem.");
                return 0;
            }
            if (args.Length >= 4 && args[0] == "pack")
            {
                using var downloads = new VerifiedDownloads();
                var configuration = JsonSerializer.Deserialize<PackConfig>(File.ReadAllBytes(args[1]), AppStorage.Json)!;
                PackRelease release = await Resolve(configuration, downloads, CancellationToken.None);
                byte[] keyBytes = ProtectedData.Unprotect(File.ReadAllBytes(args[2]), null, DataProtectionScope.CurrentUser);
                SignedRelease envelope;
                try { envelope = ReleaseSecurity.Sign(release, System.Text.Encoding.UTF8.GetString(keyBytes)); }
                finally { CryptographicOperations.ZeroMemory(keyBytes); }
                string output = Path.GetFullPath(args[3]);
                Directory.CreateDirectory(output);
                AppStorage.AtomicWrite(Path.Combine(output, "release.json"), JsonSerializer.SerializeToUtf8Bytes(envelope, AppStorage.Json));
                AppStorage.AtomicWrite(Path.Combine(output, "pack.lock.json"), JsonSerializer.SerializeToUtf8Bytes(release, AppStorage.Json));
                Console.WriteLine($"Pack {release.PackId} {release.Version}: {release.Mods.Length} mods. Exportación local firmada, sin publicación.");
                return 0;
            }
            if (args.Length == 4 && args[0] == "prepare")
            {
                using var downloads = new VerifiedDownloads();
                var storage = new AppStorage(args[3]);
                var envelope = JsonSerializer.Deserialize<SignedRelease>(File.ReadAllBytes(args[1]), AppStorage.Json)!;
                var release = ReleaseSecurity.Verify(envelope, File.ReadAllText(args[2]));
                var prepared = await new ReleaseManager(storage, downloads, File.ReadAllText(args[2]))
                    .Install(release, new Progress<string>(Console.WriteLine), CancellationToken.None);
                var start = GameLaunch.Build(prepared, new Aea.Core.Authentication.GameAccount("Validation", Guid.Empty.ToString("N"), "validation-placeholder", "", ""), 2048);
                start.FileName = Path.Combine(Path.GetDirectoryName(prepared.JavaExecutable)!, "java.exe");
                start.ArgumentList.Insert(0, "--dry-run");
                start.RedirectStandardError = true;
                start.RedirectStandardOutput = true;
                using var process = System.Diagnostics.Process.Start(start)!;
                Task<string> errors = process.StandardError.ReadToEndAsync();
                Task<string> outputText = process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                await Task.WhenAll(errors, outputText);
                if (process.ExitCode != 0) throw new InvalidOperationException("Java no pudo cargar la clase principal con los argumentos preparados (dry-run).");
                Console.WriteLine($"Preparación y Java dry-run verificados. Minecraft {prepared.Release.Pack.Minecraft}, {prepared.Classpath.Length} bibliotecas. No se ejecutó el juego ni una sesión autenticada.");
                return 0;
            }
            Console.Error.WriteLine("keygen <clave-cifrada> | pack <pack.json> <clave-cifrada> <salida> | prepare <release.json> <public.pem> <datos>");
            return 2;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"No se completó la operación: {error.GetType().Name}. {error.Message}");
            return 1;
        }
    }

    private static async Task<PackRelease> Resolve(PackConfig config, VerifiedDownloads downloads, CancellationToken ct)
    {
        AppStorage.Identifier(config.PackId);
        ReleaseSecurity.VersionNumber(config.Version);
        using var index = await downloads.Json("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", ct);
        string minecraft = config.Minecraft == "latest" ? index.RootElement.GetProperty("latest").GetProperty("release").GetString()! : config.Minecraft;
        var entry = index.RootElement.GetProperty("versions").EnumerateArray().Single(version => version.GetProperty("id").GetString() == minecraft);
        if (entry.GetProperty("type").GetString() != "release") throw new InvalidDataException("El primer pack solo admite versiones estables.");
        string metadataUrl = entry.GetProperty("url").GetString()!;
        byte[] metadata = await downloads.Metadata(metadataUrl, ct);
        string gameHash = Convert.ToHexStringLower(SHA1.HashData(metadata));
        if (gameHash != entry.GetProperty("sha1").GetString()) throw new InvalidDataException("El JSON oficial de Minecraft no coincide con su índice.");
        var gameMetadata = new Artifact(metadataUrl, "game.json", "SHA1", gameHash, metadata.Length);
        using var game = JsonDocument.Parse(metadata);
        int javaMajor = game.RootElement.GetProperty("javaVersion").GetProperty("majorVersion").GetInt32();
        using var loaders = await downloads.Json($"https://meta.fabricmc.net/v2/versions/loader/{minecraft}", ct);
        string fabric = config.Fabric == "latest" ? loaders.RootElement.EnumerateArray().First(item => item.GetProperty("loader").GetProperty("stable").GetBoolean()).GetProperty("loader").GetProperty("version").GetString()! : config.Fabric;
        string fabricUrl = $"https://meta.fabricmc.net/v2/versions/loader/{minecraft}/{fabric}/profile/json";
        byte[] fabricBytes = await downloads.Metadata(fabricUrl, ct);
        var fabricMetadata = new Artifact(fabricUrl, "fabric.json", "SHA256", Convert.ToHexStringLower(SHA256.HashData(fabricBytes)), fabricBytes.Length);
        using var loader = JsonDocument.Parse(fabricBytes);
        var fabricLibraries = new List<Artifact>();
        foreach (var library in loader.RootElement.GetProperty("libraries").EnumerateArray())
        {
            string relative = GameInstaller.MavenPath(library.GetProperty("name").GetString()!);
            string url = library.GetProperty("url").GetString()!.TrimEnd('/') + "/" + relative;
            if (library.TryGetProperty("sha256", out var hash) && library.TryGetProperty("size", out var size))
                fabricLibraries.Add(new(url, relative, "SHA256", hash.GetString()!, size.GetInt64()));
            else
            {
                byte[] bytes = await downloads.Metadata(url, ct, 32 * 1024 * 1024);
                string repositoryHash = System.Text.Encoding.UTF8.GetString(await downloads.Metadata(url + ".sha256", ct, 1024)).Trim().Split(' ')[0];
                string actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
                if (!actual.Equals(repositoryHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Checksum de biblioteca Fabric incorrecto.");
                fabricLibraries.Add(new(url, relative, "SHA256", actual, bytes.Length));
            }
        }
        using var runtimes = await downloads.Json($"https://api.adoptium.net/v3/assets/latest/{javaMajor}/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse", ct);
        var package = runtimes.RootElement[0].GetProperty("binary").GetProperty("package");
        var javaArchive = new Artifact(package.GetProperty("link").GetString()!, "java.zip", "SHA256", package.GetProperty("checksum").GetString()!, package.GetProperty("size").GetInt64());

        var resolved = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        async Task Visit(string project, string? fixedVersion = null)
        {
            JsonElement version;
            if (fixedVersion != null)
            {
                using var response = await downloads.Json("https://api.modrinth.com/v2/version/" + Uri.EscapeDataString(fixedVersion), ct);
                version = response.RootElement.Clone();
            }
            else
            {
                using var response = await downloads.Json("https://api.modrinth.com/v2/project/" + Uri.EscapeDataString(project)
                    + "/version?loaders=%5B%22fabric%22%5D&game_versions=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { minecraft })), ct);
                version = response.RootElement.EnumerateArray().FirstOrDefault(item => item.GetProperty("version_type").GetString() == "release").Clone();
                if (version.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"No hay una versión estable compatible de {project}.");
            }
            string id = version.GetProperty("project_id").GetString()!;
            string versionId = version.GetProperty("id").GetString()!;
            if (resolved.TryGetValue(id, out var previous))
            {
                if (fixedVersion != null && previous.GetProperty("id").GetString() != fixedVersion)
                    throw new InvalidDataException("Dos mods requieren versiones distintas de una dependencia.");
                return;
            }
            if (!version.GetProperty("game_versions").EnumerateArray().Any(item => item.GetString() == minecraft)
                || !version.GetProperty("loaders").EnumerateArray().Any(item => item.GetString() == "fabric"))
                throw new InvalidDataException("Una dependencia no es compatible con Minecraft/Fabric.");
            if (!visiting.Add(versionId)) return;
            resolved.Add(id, version);
            foreach (var dependency in version.GetProperty("dependencies").EnumerateArray().Where(item => item.GetProperty("dependency_type").GetString() == "required"))
            {
                string? dependencyProject = dependency.GetProperty("project_id").GetString();
                string? dependencyVersion = dependency.GetProperty("version_id").GetString();
                if (dependencyProject == null && dependencyVersion == null) throw new InvalidDataException("Dependencia requerida sin identificador.");
                await Visit(dependencyProject ?? "", dependencyVersion);
            }
        }
        foreach (string project in config.Mods) await Visit(project);
        var mods = resolved.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item =>
        {
            var files = item.Value.GetProperty("files").EnumerateArray().Where(file => file.GetProperty("filename").GetString()!.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)).ToArray();
            var file = files.FirstOrDefault(file => file.GetProperty("primary").GetBoolean());
            if (file.ValueKind != JsonValueKind.Object && files.Length == 1) file = files[0];
            if (file.ValueKind != JsonValueKind.Object) throw new InvalidDataException("El mod no tiene un JAR principal inequívoco.");
            return new Artifact(file.GetProperty("url").GetString()!, file.GetProperty("filename").GetString()!, "SHA512",
                file.GetProperty("hashes").GetProperty("sha512").GetString()!, file.GetProperty("size").GetInt64());
        }).ToArray();
        return new(1, config.PackId, config.Version, config.Name, minecraft, gameMetadata, fabric, fabricMetadata,
            fabricLibraries.ToArray(), javaMajor, javaArchive, mods, config.Server);
    }

    private sealed record PackConfig(string PackId, string Version, string Name, string Minecraft, string Fabric, string[] Mods, string? Server);
}
