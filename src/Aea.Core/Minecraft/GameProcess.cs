using System.Diagnostics;
using System.Text.Json;
using Aea.Core.Storage;

namespace Aea.Core.Minecraft;

public static class GameProcess
{
    private sealed record Marker(int Pid, long StartTime, string Executable);
    public static void Remember(AppStorage storage, Process process)
    {
        string executable = process.MainModule?.FileName ?? throw new LauncherException("Minecraft", "No se pudo identificar el proceso Java.");
        var marker = new Marker(process.Id, process.StartTime.ToUniversalTime().Ticks, executable);
        AppStorage.AtomicWrite(Path.Combine(storage.Root, "game-process.json"), JsonSerializer.SerializeToUtf8Bytes(marker, AppStorage.Json));
    }

    public static Process? Running(AppStorage storage)
    {
        string file = Path.Combine(storage.Root, "game-process.json");
        if (!File.Exists(file)) return null;
        AppStorage.RejectLinks(file);
        try
        {
            var marker = JsonSerializer.Deserialize<Marker>(File.ReadAllBytes(file), AppStorage.Json)!;
            if (marker == null) return null;
            if (!Path.GetFullPath(marker.Executable).StartsWith(storage.Cache + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
            var process = Process.GetProcessById(marker.Pid);
            try
            {
                if (!process.HasExited && process.StartTime.ToUniversalTime().Ticks == marker.StartTime &&
                    string.Equals(process.MainModule?.FileName, marker.Executable, StringComparison.OrdinalIgnoreCase)) return process;
            }
            catch { process.Dispose(); throw; }
            process.Dispose();
            return null;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or JsonException or System.ComponentModel.Win32Exception)
        { return null; }
    }
}
