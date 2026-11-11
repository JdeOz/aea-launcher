using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Aea.Core.Storage;
using Microsoft.Identity.Client;

namespace Aea.Core.Authentication;

public sealed record GameAccount(string Name, string Uuid, string AccessToken, string Xuid, string ClientId)
{
    public override string ToString() => "GameAccount [credentials redacted]";
}

public sealed class MicrosoftAccount : IDisposable
{
    private IPublicClientApplication? application;
    private readonly HttpClient client;
    private readonly string clientId;
    private static readonly string[] Scopes = ["XboxLive.signin"];
    private readonly string cacheFile;

    public MicrosoftAccount(AppStorage storage, string clientId, HttpMessageHandler? handler = null)
    {
        this.clientId = clientId;
        client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };
        cacheFile = Path.Combine(storage.Auth, "microsoft.bin");
        application = CreateApplication();
    }

    private IPublicClientApplication? CreateApplication()
    {
        if (!Guid.TryParse(clientId, out _)) return null;
        var result = PublicClientApplicationBuilder.Create(clientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, "consumers")
            .WithRedirectUri("http://localhost")
            .Build();
        result.UserTokenCache.SetBeforeAccess(args =>
        {
            if (!File.Exists(cacheFile)) return;
            AppStorage.RejectLinks(cacheFile);
            try
            {
                byte[] encrypted = File.ReadAllBytes(cacheFile);
                byte[] clear = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                try { args.TokenCache.DeserializeMsalV3(clear); }
                finally { CryptographicOperations.ZeroMemory(clear); }
            }
            catch (CryptographicException)
            { throw new LauncherException("Cuenta", "Windows no puede abrir la sesión guardada. Cierra la sesión e inicia de nuevo."); }
        });
        result.UserTokenCache.SetAfterAccess(args =>
        {
            if (!args.HasStateChanged) return;
            byte[] clear = args.TokenCache.SerializeMsalV3();
            try { AppStorage.AtomicWrite(cacheFile, ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser)); }
            finally { CryptographicOperations.ZeroMemory(clear); }
        });
        return result;
    }

    public bool HasSavedSession => File.Exists(cacheFile);
    public async Task<bool> HasSavedAccount() => application != null && (await application.GetAccountsAsync()).Any();

    public async Task<GameAccount> SignIn(bool interactive, IProgress<string>? progress, CancellationToken ct)
    {
        if (application == null)
            throw new LauncherException("Configuración", "Falta registrar la aplicación Microsoft propia. El administrador debe configurar su Client ID y autorizar http://localhost.");
        try
        {
            progress?.Report("Validando la cuenta Microsoft…");
            AuthenticationResult result;
            var account = (await application.GetAccountsAsync()).FirstOrDefault();
            try { result = await application.AcquireTokenSilent(Scopes, account).ExecuteAsync(ct); }
            catch (MsalUiRequiredException)
            {
                if (!interactive) throw new LauncherException("Cuenta", "Tu sesión necesita renovarse. Pulsa Iniciar sesión.");
                progress?.Report("Continúa en el navegador de Microsoft…");
                result = await application.AcquireTokenInteractive(Scopes).WithUseEmbeddedWebView(false)
                    .WithPrompt(Prompt.SelectAccount).ExecuteAsync(ct);
            }
            return await ExchangeMinecraft(result.AccessToken, progress, ct);
        }
        catch (MsalClientException error) when (error.ErrorCode == "authentication_canceled")
        { throw new OperationCanceledException("Inicio de sesión cancelado.", ct); }
        catch (MsalException)
        { throw new LauncherException("Microsoft", "Microsoft no pudo completar el acceso. Comprueba el registro y la redirección de la aplicación e inténtalo de nuevo."); }
        catch (HttpRequestException)
        { throw new LauncherException("Cuenta", "No se pudo contactar con Microsoft o Minecraft. Revisa la conexión y reintenta; la sesión guardada se conserva."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new LauncherException("Cuenta", "El servicio tardó demasiado en responder. Reintenta; la sesión guardada se conserva."); }
    }

    internal async Task<GameAccount> ExchangeMinecraft(string accessToken, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("Validando Xbox…");
        using var xbl = await Post("https://user.auth.xboxlive.com/user/authenticate", new
        {
            Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + accessToken },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        }, "Xbox", ct);
        using var xsts = await Post("https://xsts.auth.xboxlive.com/xsts/authorize", new
        {
            Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xbl.RootElement.GetProperty("Token").GetString()! } },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        }, "Xbox", ct);
        var claims = xsts.RootElement.GetProperty("DisplayClaims").GetProperty("xui")[0];
        string uhs = claims.GetProperty("uhs").GetString()!;
        string xuid = claims.TryGetProperty("xid", out var xid) ? xid.GetString()! : "";
        progress?.Report("Validando Minecraft…");
        using var minecraft = await Post("https://api.minecraftservices.com/authentication/login_with_xbox", new
        {
            identityToken = "XBL3.0 x=" + uhs + ";" + xsts.RootElement.GetProperty("Token").GetString()
        }, "Minecraft", ct);
        string token = minecraft.RootElement.GetProperty("access_token").GetString()!;
        progress?.Report("Comprobando la licencia de Minecraft Java…");
        using (var licenseRequest = new HttpRequestMessage(HttpMethod.Get,
                   "https://api.minecraftservices.com/entitlements/license?requestId=" + Guid.NewGuid().ToString("D")))
        {
            licenseRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var licenseResponse = await client.SendAsync(licenseRequest, ct);
            await RequireSuccess(licenseResponse, "Licencia Minecraft", ct);
            using var license = await ReadJson(licenseResponse, ct);
            if (!license.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array ||
                !items.EnumerateArray().Any(item => item.TryGetProperty("name", out var name) && name.GetString() == "game_minecraft"))
                throw new LauncherException("Licencia Minecraft", "Minecraft no confirmó una licencia activa para jugar Java. Comprueba la compra o tu suscripción de Game Pass.");
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new LauncherException("Minecraft", "Esta cuenta no tiene un perfil de Minecraft Java disponible. Comprueba la compra o Game Pass y crea el perfil en minecraft.net.", 404);
        await RequireSuccess(response, "Perfil Minecraft", ct);
        using var profile = await ReadJson(response, ct);
        string name = profile.RootElement.GetProperty("name").GetString()!;
        string uuid = profile.RootElement.GetProperty("id").GetString()!;
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-zA-Z0-9_]{1,16}$") || !Guid.TryParseExact(uuid, "N", out _))
            throw new LauncherException("Minecraft", "Minecraft devolvió un perfil inválido.");
        // Both an active Java entitlement and an authenticated profile are required; no offline account is constructed.
        return new(name, uuid, token, xuid, clientId);
    }

    private async Task<JsonDocument> Post(string url, object body, string stage, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync(url, body, ct);
        await RequireSuccess(response, stage, ct);
        return await ReadJson(response, ct);
    }

    private static async Task RequireSuccess(HttpResponseMessage response, string stage, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        int status = (int)response.StatusCode;
        if (stage == "Minecraft" && status == 403)
            throw new LauncherException(stage, "Minecraft rechazó el acceso de este launcher (HTTP 403). El administrador debe comprobar el registro y la aprobación de la aplicación; reintentar no concede permisos.", status);
        if (stage == "Xbox")
        {
            using var body = await ReadJson(response, ct);
            long code = body.RootElement.TryGetProperty("XErr", out var xerr) ? xerr.GetInt64() : 0;
            string message = code switch
            {
                2148916233 => "Crea primero tu perfil Xbox en xbox.com.",
                2148916238 => "La cuenta necesita la configuración familiar de Xbox.",
                2148916235 => "Xbox no permite el acceso de esta cuenta o región.",
                _ => "Xbox rechazó el acceso. Comprueba el perfil Xbox y la configuración del launcher."
            };
            throw new LauncherException(stage, message, status);
        }
        throw new LauncherException(stage, $"{stage} rechazó el acceso (HTTP {status}). Inicia sesión de nuevo o revisa la configuración de la aplicación.", status);
    }

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (memory.Length + count > 1024 * 1024) throw new LauncherException("Cuenta", "Respuesta de autenticación demasiado grande.");
            memory.Write(buffer, 0, count);
        }
        try { return JsonDocument.Parse(memory.ToArray()); }
        catch (JsonException) { throw new LauncherException("Cuenta", "El servicio de autenticación devolvió una respuesta no válida."); }
    }

    public Task SignOut()
    {
        AppStorage.RejectLinks(cacheFile);
        if (File.Exists(cacheFile)) File.Delete(cacheFile);
        // Recreate MSAL as well, so a damaged on-disk cache cannot trap an in-memory session.
        application = CreateApplication();
        return Task.CompletedTask;
    }
    public void Dispose() => client.Dispose();
}
