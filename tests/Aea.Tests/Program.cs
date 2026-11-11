using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aea.Core.Authentication;
using Aea.Core.Downloads;
using Aea.Core.Minecraft;
using Aea.Core.Releases;
using Aea.Core.Storage;

var tests = new List<(string Name, Func<Task> Run)>();
string root = Path.Combine(Path.GetTempPath(), "aea-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
string publicKey = key.ExportSubjectPublicKeyInfoPem();
string privateKey = key.ExportPkcs8PrivateKeyPem();
byte[] bytes = Encoding.UTF8.GetBytes("verified artifact");
Artifact Sample(string path = "file.jar") => new("https://cdn.modrinth.com/" + path, path, "SHA256", Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length);
PackRelease Pack(string version = "1.0.0") => new(1, "test-pack", version, "Test", "26.3",
    Sample("game.json") with { Url = "https://piston-meta.mojang.com/game.json" }, "0.20.1",
    Sample("fabric.json") with { Url = "https://meta.fabricmc.net/v2/versions/loader/26.3/0.20.1/profile/json" },
    [Sample("library.jar")], 25, Sample("java.zip"), [Sample()], null);
VerifiedRelease Verified(string version = "1.0.0") => ReleaseSecurity.Verify(ReleaseSecurity.Sign(Pack(version), privateKey), publicKey);
void Test(string name, Action action) => tests.Add((name, () => { action(); return Task.CompletedTask; }));
void AsyncTest(string name, Func<Task> action) => tests.Add((name, action));
string NewRoot() => Path.Combine(root, Guid.NewGuid().ToString("N"));

Test("signed release rejects modified payload, signature, foreign key and malformed version", () =>
{
    var signed = ReleaseSecurity.Sign(Pack(), privateKey);
    Check.Equal("1.0.0", ReleaseSecurity.Verify(signed, publicKey).Pack.Version);
    byte[] changed = Convert.FromBase64String(signed.Payload); changed[^2] ^= 1;
    Check.Throws<InvalidDataException>(() => ReleaseSecurity.Verify(signed with { Payload = Convert.ToBase64String(changed) }, publicKey));
    Check.Throws<InvalidDataException>(() => ReleaseSecurity.Verify(signed with { Signature = "AA==" }, publicKey));
    using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    Check.Throws<InvalidDataException>(() => ReleaseSecurity.Verify(signed, other.ExportSubjectPublicKeyInfoPem()));
    Check.Throws<InvalidDataException>(() => ReleaseSecurity.Sign(Pack("../1"), privateKey));
});

Test("path boundary rejects traversal, ADS, reserved names and Windows aliases", () =>
{
    foreach (string path in new[] { "../secret", "a/../../secret", "C:/secret", "//host/share", "x\\..\\secret", "a:stream", "a/CON.txt", "file. ", "a//file", "a/..", "a/file." })
        Check.Throws<InvalidDataException>(() => AppStorage.Inside(root, path));
    Check.True(AppStorage.Inside(root, "libraries/valid-1.0.jar").StartsWith(root, StringComparison.Ordinal));
});

Test("archive rejects zip slip and leaves sibling files intact", () =>
{
    string parent = NewRoot(); Directory.CreateDirectory(parent);
    string zip = Path.Combine(parent, "attack.zip");
    using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
    using (var writer = new StreamWriter(archive.CreateEntry("../outside.txt").Open())) writer.Write("attack");
    Check.Throws<InvalidDataException>(() => SafeArchive.Extract(zip, Path.Combine(parent, "target"), CancellationToken.None));
    Check.True(!File.Exists(Path.Combine(parent, "outside.txt")));
});

Test("archive rejects symbolic links and duplicate case-insensitive paths", () =>
{
    foreach (bool link in new[] { true, false })
    {
        string parent = NewRoot(); Directory.CreateDirectory(parent);
        string zip = Path.Combine(parent, "attack.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("data.txt");
            if (link) entry.ExternalAttributes = unchecked((int)0xA1FF0000);
            else archive.CreateEntry("DATA.TXT");
        }
        Check.Throws<InvalidDataException>(() => SafeArchive.Extract(zip, Path.Combine(parent, "target"), CancellationToken.None));
    }
});

Test("manifest rejects executable mod paths, invalid integrity and duplicate mods", () =>
{
    Check.Throws<InvalidDataException>(() => ReleaseSecurity.Validate(Pack() with { Mods = [Sample("../x.jar")] }));
    Check.Throws<InvalidDataException>(() => ReleaseSecurity.Validate(Pack() with { Mods = [Sample("tool.exe")] }));
    Check.Throws<InvalidDataException>(() => ReleaseSecurity.Validate(Pack() with { Mods = [Sample(), Sample()] }));
    Check.Throws<InvalidDataException>(() => ReleaseSecurity.Validate(Pack() with { JavaArchive = Sample() with { Hash = "bad" } }));
    Check.Throws<InvalidDataException>(() => ReleaseSecurity.Validate(Pack() with { Schema = 2 }));
});

AsyncTest("verified downloads refuse corrupt bytes without replacing the installed artifact", async () =>
{
    var storage = new AppStorage(NewRoot());
    string target = Path.Combine(storage.Root, "artifact.jar");
    await File.WriteAllTextAsync(target, "previous");
    using var downloads = new VerifiedDownloads(new FakeHttp(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[bytes.Length]) }));
    await Check.ThrowsAsync<InvalidDataException>(() => downloads.File(Sample(), target, CancellationToken.None));
    Check.Equal("previous", await File.ReadAllTextAsync(target));
    Check.Equal(0, Directory.GetFiles(storage.Root, "*.part").Length);
});

AsyncTest("cancelled and failed downloads preserve active release and personal files", async () =>
{
    var storage = new AppStorage(NewRoot());
    var manager = new ReleaseManager(storage, new VerifiedDownloads(new FakeHttp(_ => new(HttpStatusCode.InternalServerError))), publicKey);
    var installed = Verified();
    AppStorage.AtomicWrite(storage.ActiveFile(installed.Pack.PackId), JsonSerializer.SerializeToUtf8Bytes(installed.Envelope, AppStorage.Json));
    string world = Path.Combine(storage.Instance(installed.Pack.PackId), "world.dat");
    await File.WriteAllTextAsync(world, "world");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    await Check.ThrowsAsync<OperationCanceledException>(() => manager.Install(Verified("1.1.0"), null, cancelled.Token));
    await Check.ThrowsAsync<LauncherException>(() => manager.Install(Verified("1.1.0"), null, CancellationToken.None));
    Check.Equal(installed.Digest, manager.Load(installed.Envelope).Digest);
    Check.Equal("world", await File.ReadAllTextAsync(world));
});

AsyncTest("feed rejects wrong pack, changed same version, downgrade and untrusted redirect", async () =>
{
    var storage = new AppStorage(NewRoot());
    SignedRelease candidate = ReleaseSecurity.Sign(Pack("1.1.0"), privateKey);
    using var downloads = new VerifiedDownloads(new FakeHttp(_ => new(HttpStatusCode.OK)
    { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(candidate, AppStorage.Json)) }));
    var manager = new ReleaseManager(storage, downloads, publicKey);
    var current = Verified();
    Check.Equal("1.1.0", (await manager.Check(current, "https://github.com/release.json", CancellationToken.None))!.Pack.Version);
    Check.Equal(current.Digest, manager.Load(current.Envelope).Digest);
    candidate = ReleaseSecurity.Sign(Pack("0.9.0"), privateKey);
    Check.True(await manager.Check(current, "https://github.com/release.json", CancellationToken.None) == null);
    candidate = ReleaseSecurity.Sign(Pack() with { Name = "changed" }, privateKey);
    await Check.ThrowsAsync<InvalidDataException>(() => manager.Check(current, "https://github.com/release.json", CancellationToken.None));
    candidate = ReleaseSecurity.Sign(Pack("2.0.0") with { PackId = "different" }, privateKey);
    await Check.ThrowsAsync<InvalidDataException>(() => manager.Check(current, "https://github.com/release.json", CancellationToken.None));
    using var redirect = new VerifiedDownloads(new FakeHttp(_ =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
        response.Headers.Location = new Uri("http://127.0.0.1/private"); return response;
    }));
    await Check.ThrowsAsync<InvalidDataException>(() => redirect.Metadata("https://github.com/release.json", CancellationToken.None));
});

Test("account secrets use Windows user encryption and never appear in diagnostics", () =>
{
    Check.True(!new GameAccount("PrivateName", Guid.Empty.ToString("N"), "sensitive-token", "private-xuid", "private-client").ToString().Contains("sensitive-token", StringComparison.Ordinal));
    byte[] clear = Encoding.UTF8.GetBytes("sensitive-token");
    byte[] encrypted = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser);
    Check.True(!Encoding.UTF8.GetString(encrypted).Contains("sensitive-token", StringComparison.Ordinal));
    Check.True(ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser).SequenceEqual(clear));
    var storage = new AppStorage(NewRoot());
    var logger = new Diagnostics(storage);
    logger.Failure(new LauncherException("Minecraft", "sensitive-token user@example.com https://secret", 403));
    string log = File.ReadAllText(logger.FilePath);
    Check.True(log.Contains("403", StringComparison.Ordinal) && !log.Contains("sensitive-token", StringComparison.Ordinal) && !log.Contains('@'));
});

AsyncTest("Microsoft access is blocked clearly until our own app is configured", async () =>
{
    using var account = new MicrosoftAccount(new AppStorage(NewRoot()), "");
    var error = await Check.ThrowsAsync<LauncherException>(() => account.SignIn(true, null, CancellationToken.None));
    Check.Equal("Configuración", error.Stage);
});

AsyncTest("Minecraft403 is actionable and does not create a local account", async () =>
{
    var storage = new AppStorage(NewRoot());
    using var account = new MicrosoftAccount(storage, "", new FakeHttp(request =>
    {
        string host = request.RequestUri!.Host;
        if (host == "user.auth.xboxlive.com") return JsonResponse("{\"Token\":\"xbl\"}");
        if (host == "xsts.auth.xboxlive.com") return JsonResponse("{\"Token\":\"xsts\",\"DisplayClaims\":{\"xui\":[{\"uhs\":\"hash\"}]}}");
        return new(HttpStatusCode.Forbidden);
    }));
    var error = await Check.ThrowsAsync<LauncherException>(() => account.ExchangeMinecraft("test-token", null, CancellationToken.None));
    Check.Equal(403, error.Status);
    Check.True(error.Message.Contains("aprobación", StringComparison.Ordinal));
    Check.Equal(0, Directory.GetFiles(storage.Auth).Length);
});

AsyncTest("valid Xbox and Minecraft profile yield session in memory; missing Java ownership is refused", async () =>
{
    bool owns = true;
    using var account = new MicrosoftAccount(new AppStorage(NewRoot()), "", new FakeHttp(request =>
    {
        string host = request.RequestUri!.Host;
        if (host == "user.auth.xboxlive.com") return JsonResponse("{\"Token\":\"xbl\"}");
        if (host == "xsts.auth.xboxlive.com") return JsonResponse("{\"Token\":\"xsts\",\"DisplayClaims\":{\"xui\":[{\"uhs\":\"hash\"}]}}");
        if (request.RequestUri.AbsolutePath.Contains("login_with_xbox", StringComparison.Ordinal)) return JsonResponse("{\"access_token\":\"minecraft-test-token\"}");
        return owns ? JsonResponse("{\"name\":\"Player\",\"id\":\"0123456789abcdef0123456789abcdef\"}") : new(HttpStatusCode.NotFound);
    }));
    Check.Equal("Player", (await account.ExchangeMinecraft("test", null, CancellationToken.None)).Name);
    owns = false;
    var error = await Check.ThrowsAsync<LauncherException>(() => account.ExchangeMinecraft("test", null, CancellationToken.None));
    Check.Equal(404, error.Status);
});

Test("game rule evaluation excludes other platforms, demo and unsupported feature flags", () =>
{
    using var document = JsonDocument.Parse("[{\"rules\":[{\"action\":\"allow\",\"os\":{\"name\":\"osx\"}}]},{\"rules\":[{\"action\":\"allow\",\"features\":{\"is_demo_user\":true}}]},{\"rules\":[{\"action\":\"allow\",\"os\":{\"name\":\"windows\"}}]}]");
    Check.True(!GameRules.Allowed(document.RootElement[0]));
    Check.True(!GameRules.Allowed(document.RootElement[1]));
    Check.True(GameRules.Allowed(document.RootElement[2]));
    Check.Throws<LauncherException>(() => GameRules.Replace("${unsupported}", new Dictionary<string, string>()));
    Check.Equal("a b", GameRules.Replace("${path}", new Dictionary<string, string> { ["path"] = "a b" }));
});

Test("RAM persists and invalid values never overwrite valid preferences", () =>
{
    var storage = new AppStorage(NewRoot());
    new Preferences(3072).Save(storage);
    Check.Equal(3072, Preferences.Load(storage).MaximumRamMb);
    Check.Throws<InvalidDataException>(() => new Preferences(1).Save(storage));
    Check.Equal(3072, Preferences.Load(storage).MaximumRamMb);
});

AsyncTest("corrupt encrypted session can be signed out without decrypting it", async () =>
{
    var storage = new AppStorage(NewRoot());
    await File.WriteAllTextAsync(Path.Combine(storage.Auth, "microsoft.bin"), "damaged-cache");
    using var account = new MicrosoftAccount(storage, "01234567-89ab-cdef-0123-456789abcdef");
    Check.True(account.HasSavedSession);
    await account.SignOut();
    Check.True(!account.HasSavedSession);
    Check.True(!await account.HasSavedAccount());
});

AsyncTest("successful update activates only after verification, preserves worlds and isolates changed binaries", async () =>
{
    var storage = new AppStorage(NewRoot());
    var responses = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    Artifact Put(string url, string path, byte[] data)
    {
        responses[url] = data;
        return new(url, path, "SHA256", Convert.ToHexStringLower(SHA256.HashData(data)), data.Length);
    }
    Artifact Official(string url, string path, byte[] data)
    {
        responses[url] = data;
        return new(url, path, "SHA1", Convert.ToHexStringLower(SHA1.HashData(data)), data.Length);
    }
    using var javaBytes = new MemoryStream();
    using (var zip = new ZipArchive(javaBytes, ZipArchiveMode.Create, true))
    using (var writer = new StreamWriter(zip.CreateEntry("runtime/bin/javaw.exe").Open())) writer.Write("fixture only, never executed");
    var java = Put("https://github.com/fixture/java.zip", "java.zip", javaBytes.ToArray());
    var library = Put("https://maven.fabricmc.net/net/fabricmc/fabric-loader/0.20.1/fabric-loader-0.20.1.jar",
        "net/fabricmc/fabric-loader/0.20.1/fabric-loader-0.20.1.jar", Encoding.UTF8.GetBytes("library-one"));
    var fabric = Put("https://meta.fabricmc.net/v2/versions/loader/26.3/0.20.1/profile/json", "fabric.json", JsonSerializer.SerializeToUtf8Bytes(new
    {
        inheritsFrom = "26.3",
        mainClass = "net.fabricmc.loader.impl.launch.knot.KnotClient",
        libraries = new[] { new { name = "net.fabricmc:fabric-loader:0.20.1", url = "https://maven.fabricmc.net/" } }
    }));
    var clientArtifact = Official("https://piston-data.mojang.com/client.jar", "client.jar", Encoding.UTF8.GetBytes("client"));
    var indexArtifact = Official("https://piston-meta.mojang.com/assets.json", "index.json", Encoding.UTF8.GetBytes("{\"objects\":{}}"));
    var game = Put("https://piston-meta.mojang.com/game.json", "game.json", JsonSerializer.SerializeToUtf8Bytes(new
    {
        id = "26.3",
        javaVersion = new { majorVersion = 25 },
        libraries = Array.Empty<object>(),
        downloads = new { client = new { url = clientArtifact.Url, sha1 = clientArtifact.Hash, size = clientArtifact.Size } },
        assetIndex = new { id = "26.3", url = indexArtifact.Url, sha1 = indexArtifact.Hash, size = indexArtifact.Size },
        arguments = new { jvm = new[] { "-cp", "${classpath}" }, game = new[] { "--username", "${auth_player_name}", "--accessToken", "${auth_access_token}" } }
    }));
    var mod = Put("https://cdn.modrinth.com/mod.jar", "mod.jar", Encoding.UTF8.GetBytes("mod"));
    var pack = new PackRelease(1, "test-pack", "1.0.0", "Test", "26.3", game, "0.20.1", fabric, [library], 25, java, [mod], null);
    VerifiedRelease Release(PackRelease data) => ReleaseSecurity.Verify(ReleaseSecurity.Sign(data, privateKey), publicKey);
    using var downloads = new VerifiedDownloads(new FakeHttp(request => responses.TryGetValue(request.RequestUri!.AbsoluteUri, out var data)
        ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) } : new(HttpStatusCode.NotFound)));
    var manager = new ReleaseManager(storage, downloads, publicKey);
    var original = Release(pack);
    var installed = await manager.Install(original, null, CancellationToken.None);
    string oldLibrary = installed.Classpath[0];
    string world = Path.Combine(installed.GameDirectory, "world.dat");
    await File.WriteAllTextAsync(world, "world");
    string options = Path.Combine(installed.GameDirectory, "options.txt");
    await File.WriteAllTextAsync(options, "personal options");
    var changed = Put(library.Url, library.Path, Encoding.UTF8.GetBytes("library-two"));
    var next = Release(pack with { Version = "1.1.0", FabricLibraries = [changed], Mods = [] });
    var updated = await manager.Install(next, null, CancellationToken.None);
    Check.Equal("1.1.0", manager.Load(original.Envelope).Pack.Version);
    Check.Equal("library-one", await File.ReadAllTextAsync(oldLibrary));
    Check.Equal("library-two", await File.ReadAllTextAsync(updated.Classpath[0]));
    Check.Equal("world", await File.ReadAllTextAsync(world));
    Check.Equal("personal options", await File.ReadAllTextAsync(options));
    var launch = GameLaunch.Build(updated, new("Player", Guid.Empty.ToString("N"), "fixture-token", "", ""), 2048);
    Check.True(!launch.ArgumentList.Any(value => value.Contains("fabric.addMods", StringComparison.Ordinal)));
    Check.Equal("fixture-token", launch.ArgumentList[^1]);
    Check.Throws<LauncherException>(() => GameLaunch.Build(updated, new("Player", Guid.Empty.ToString("N"), "", "", ""), 2048));
});

int failures = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception error) { failures++; Console.WriteLine("FAIL " + test.Name + " — " + error.GetType().Name + ": " + error.Message); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} passed. Real authentication/game not tested by fixtures.");
if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()) + "aea-tests-", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Unexpected test cleanup root.");
Directory.Delete(root, recursive: true);
return failures == 0 ? 0 : 1;

static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(response(request)); }
}
static class Check
{
    public static void True(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    public static void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception($"Expected {expected}, received {actual}."); }
    public static T Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T error) { return error; } throw new Exception("Expected " + typeof(T).Name); }
    public static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T error) { return error; } throw new Exception("Expected " + typeof(T).Name); }
}
