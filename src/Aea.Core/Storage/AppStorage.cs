using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aea.Core.Storage;

public sealed partial class AppStorage
{
    public string Root { get; }
    public string Cache => Directory.CreateDirectory(Path.Combine(Root, "cache")).FullName;
    public string Slots => Directory.CreateDirectory(Path.Combine(Root, "releases")).FullName;
    public string Auth => Directory.CreateDirectory(Path.Combine(Root, "account")).FullName;
    public AppStorage(string? root = null)
    {
        Root = Path.GetFullPath(root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AeaLauncher"));
        Directory.CreateDirectory(Root);
        RejectLinks(Root);
    }

    public string Instance(string id) => Directory.CreateDirectory(Inside(Root, "instances/" + Identifier(id))).FullName;
    public string ActiveFile(string id) => Inside(Root, "active/" + Identifier(id) + ".json");
    public static string Identifier(string id) => IdentifierPattern().IsMatch(id) && id is not "." and not ".."
        ? id : throw new InvalidDataException("Identificador de pack no permitido.");

    public static string Inside(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':') ||
            relative.StartsWith('/') || relative.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ') ||
                p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || ReservedPattern().IsMatch(p)))
            throw new InvalidDataException("La descarga contiene una ruta no permitida.");
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La ruta sale de la carpeta del launcher.");
        RejectLinks(full);
        return full;
    }

    public static void RejectLinks(string full)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(full)); current != null; current = current.Parent)
            if ((current.Exists || File.Exists(current.FullName)) && (File.GetAttributes(current.FullName) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("No se permiten enlaces en las carpetas del launcher.");
    }

    public static void AtomicWrite(string path, ReadOnlySpan<byte> bytes)
    {
        RejectLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
    [GeneratedRegex("^(con|prn|aux|nul|com[0-9]|lpt[0-9])(?:\\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReservedPattern();
}
