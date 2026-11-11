using System.Text.Json;
using System.Text.RegularExpressions;
using Aea.Core.Storage;

namespace Aea.Core.Minecraft;

public static class GameRules
{
    public static bool Allowed(JsonElement node)
    {
        if (!node.TryGetProperty("rules", out var rules)) return true;
        bool allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            bool matches = true;
            if (rule.TryGetProperty("os", out var os))
            {
                if (os.TryGetProperty("name", out var name)) matches &= name.GetString() == "windows";
                if (os.TryGetProperty("arch", out var arch)) matches &= arch.GetString() is "amd64" or "x86_64";
                if (os.TryGetProperty("version", out var version))
                    matches &= Regex.IsMatch(Environment.OSVersion.Version.ToString(), version.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            }
            if (rule.TryGetProperty("features", out var features))
                foreach (var feature in features.EnumerateObject()) matches &= !feature.Value.GetBoolean();
            if (matches)
            {
                string action = rule.GetProperty("action").GetString()!;
                if (action is not "allow" and not "disallow") throw new InvalidDataException("Regla de Minecraft desconocida.");
                allowed = action == "allow";
            }
        }
        return allowed;
    }

    public static IEnumerable<string> Arguments(JsonElement arguments, IReadOnlyDictionary<string, string> variables)
    {
        foreach (var argument in arguments.EnumerateArray())
        {
            if (argument.ValueKind == JsonValueKind.String) yield return Replace(argument.GetString()!, variables);
            else if (Allowed(argument))
            {
                var value = argument.GetProperty("value");
                if (value.ValueKind == JsonValueKind.Array)
                    foreach (var item in value.EnumerateArray()) yield return Replace(item.GetString()!, variables);
                else yield return Replace(value.GetString()!, variables);
            }
        }
    }

    public static string Replace(string value, IReadOnlyDictionary<string, string> variables) =>
        Regex.Replace(value, @"\$\{([^}]+)\}", match => variables.TryGetValue(match.Groups[1].Value, out string? replacement)
            ? replacement : throw new LauncherException("Minecraft", "Esta versión del juego contiene un argumento todavía no compatible."));
}
