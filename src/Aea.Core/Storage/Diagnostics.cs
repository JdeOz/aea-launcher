using System.Text;

namespace Aea.Core.Storage;

public sealed class LauncherException(string stage, string message, int? status = null, Exception? inner = null)
    : Exception(message, inner)
{
    public string Stage { get; } = stage;
    public int? Status { get; } = status;
}

public sealed class Diagnostics(AppStorage storage)
{
    private readonly object gate = new();
    public string FilePath => Path.Combine(storage.Root, "launcher.log");

    // Deliberately exclude exception messages, HTTP bodies, URLs, profile names and launch arguments.
    public void Failure(Exception exception)
    {
        string stage = exception is LauncherException failure ? failure.Stage : "Aplicación";
        int? status = (exception as LauncherException)?.Status;
        string code = exception.GetType().Name;
        lock (gate)
        {
            try
            {
                AppStorage.RejectLinks(FilePath);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 256 * 1024)
                    File.Move(FilePath, FilePath + ".previous", true);
                File.AppendAllText(FilePath, $"{DateTimeOffset.UtcNow:O} stage={stage} type={code} http={status}\n", Encoding.UTF8);
            }
            catch (IOException) { /* UI still reports the failure when storage is unavailable. */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}

public sealed record Preferences(int MaximumRamMb = 4096)
{
    public static int MaximumAllowedMb => Math.Max(2048, (int)Math.Min(32768, NativeMemory.TotalMb() - 2048));
    public static int Validate(int megabytes) => megabytes >= 2048 && megabytes <= MaximumAllowedMb && megabytes % 256 == 0
        ? megabytes : throw new InvalidDataException($"Elige entre 2 y {MaximumAllowedMb / 1024.0:0.#} GB de RAM.");
    public static Preferences Load(AppStorage storage)
    {
        string path = Path.Combine(storage.Root, "preferences.json");
        if (!File.Exists(path)) return new(Math.Min(4096, MaximumAllowedMb));
        try
        {
            AppStorage.RejectLinks(path);
            var value = System.Text.Json.JsonSerializer.Deserialize<Preferences>(File.ReadAllBytes(path), AppStorage.Json)!;
            return new(Validate(value.MaximumRamMb));
        }
        catch (System.Text.Json.JsonException) { return new(Math.Min(4096, MaximumAllowedMb)); }
        catch (InvalidDataException) { return new(Math.Min(4096, MaximumAllowedMb)); }
    }
    public void Save(AppStorage storage) => AppStorage.AtomicWrite(Path.Combine(storage.Root, "preferences.json"),
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new Preferences(Validate(MaximumRamMb)), AppStorage.Json));
}

internal static class NativeMemory
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Status
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended;
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref Status status);
    public static long TotalMb()
    {
        var status = new Status { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Status>() };
        return GlobalMemoryStatusEx(ref status) ? (long)(status.TotalPhysical / (1024 * 1024)) : 6144;
    }
}
