using System.Diagnostics;
using System.Text;
using Aea.Core.Authentication;
using Aea.Core.Storage;

namespace Aea.Core.Minecraft;

public static class GameLaunch
{
    public static ProcessStartInfo Build(PreparedGame prepared, GameAccount account, int ramMb)
    {
        Preferences.Validate(ramMb);
        if (string.IsNullOrWhiteSpace(account.AccessToken) || !Guid.TryParseExact(account.Uuid, "N", out _))
            throw new LauncherException("Cuenta", "Inicia sesión en una cuenta válida de Minecraft Java.");
        var pack = prepared.Release.Pack;
        var values = new Dictionary<string, string>
        {
            ["auth_player_name"] = account.Name,
            ["version_name"] = pack.Minecraft,
            ["game_directory"] = prepared.GameDirectory,
            ["assets_root"] = prepared.AssetsDirectory,
            ["assets_index_name"] = prepared.Game.GetProperty("assetIndex").GetProperty("id").GetString()!,
            ["auth_uuid"] = account.Uuid,
            ["auth_access_token"] = account.AccessToken,
            ["clientid"] = account.ClientId,
            ["auth_xuid"] = account.Xuid,
            ["user_type"] = "msa",
            ["version_type"] = "release",
            ["natives_directory"] = Path.Combine(prepared.Slot, "natives"),
            ["launcher_name"] = "Aea",
            ["launcher_version"] = "1.0.0",
            ["classpath"] = string.Join(Path.PathSeparator, prepared.Classpath),
            ["library_directory"] = prepared.LibrariesDirectory,
            ["classpath_separator"] = Path.PathSeparator.ToString(),
            ["user_properties"] = "{}"
        };
        var arguments = new List<string> { "-Xms512M", $"-Xmx{ramMb}M" };
        var gameArguments = prepared.Game.GetProperty("arguments");
        arguments.AddRange(GameRules.Arguments(gameArguments.GetProperty("jvm"), values));
        if (prepared.Loader.TryGetProperty("arguments", out var loaderArgs) && loaderArgs.TryGetProperty("jvm", out var jvm))
            arguments.AddRange(GameRules.Arguments(jvm, values));
        if (pack.Mods.Length > 0)
            arguments.Add("-Dfabric.addMods=" + string.Join(Path.PathSeparator, pack.Mods.Select(mod => AppStorage.Inside(prepared.Slot, "mods/" + mod.Path))));
        if (prepared.Game.TryGetProperty("logging", out var logging) && logging.TryGetProperty("client", out var log))
            arguments.Add(log.GetProperty("argument").GetString()!.Replace("${path}", Path.Combine(prepared.Slot, "logging.xml"), StringComparison.Ordinal));
        arguments.Add(prepared.Loader.GetProperty("mainClass").GetString()!);
        arguments.AddRange(GameRules.Arguments(gameArguments.GetProperty("game"), values));
        if (prepared.Loader.TryGetProperty("arguments", out loaderArgs) && loaderArgs.TryGetProperty("game", out var extra))
            arguments.AddRange(GameRules.Arguments(extra, values));
        var start = new ProcessStartInfo(prepared.JavaExecutable)
        {
            WorkingDirectory = prepared.GameDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        // The game's own logs stay in its instance. The launcher never logs command-line tokens.
        foreach (string value in arguments) start.ArgumentList.Add(value);
        return start;
    }

    public static Process Start(PreparedGame prepared, GameAccount account, int ramMb)
    {
        Directory.CreateDirectory(prepared.GameDirectory);
        string options = Path.Combine(prepared.GameDirectory, "options.txt");
        if (!File.Exists(options)) AppStorage.AtomicWrite(options, Encoding.UTF8.GetBytes("lang:es_es\nrenderDistance:12\nsimulationDistance:8\n"));
        AppStorage.RejectLinks(options);
        InitialServerList.Ensure(prepared.GameDirectory, prepared.Release.Pack.Name, prepared.Release.Pack.Server);
        return Process.Start(Build(prepared, account, ramMb)) ?? throw new LauncherException("Minecraft", "Windows no pudo iniciar Java.");
    }
}
