using System.IO.Compression;
using Aea.Core.Storage;

namespace Aea.Core.Downloads;

public static class SafeArchive
{
    public static void Extract(string archive, string destination, CancellationToken ct, bool nativeOnly = false)
    {
        AppStorage.RejectLinks(destination);
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > 100000) throw new InvalidDataException("Archivo comprimido con demasiadas entradas.");
        long total = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith('/')) continue;
            string target = AppStorage.Inside(destination, entry.FullName);
            if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000 || !names.Add(entry.FullName))
                throw new InvalidDataException("El archivo comprimido contiene enlaces o rutas duplicadas.");
            total = checked(total + entry.Length);
            if (total > 3L * 1024 * 1024 * 1024 || entry.Length > 1024L * 1024 * 1024)
                throw new InvalidDataException("El archivo comprimido supera los límites de extracción.");
            if (nativeOnly && !entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
    }
}
