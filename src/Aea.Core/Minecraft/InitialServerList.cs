using System.Buffers.Binary;
using Aea.Core.Storage;

namespace Aea.Core.Minecraft;

public static class InitialServerList
{
    public static void Ensure(string gameDirectory, string name, string? address)
    {
        if (address == null) return;
        string target = Path.Combine(gameDirectory, "servers.dat");
        AppStorage.RejectLinks(target);
        if (File.Exists(target)) return;
        // servers.dat is uncompressed NBT. Write a default only once; never replace a player's list.
        using var data = new MemoryStream();
        data.WriteByte(10); Text(data, "");
        data.WriteByte(9); Text(data, "servers"); data.WriteByte(10);
        Span<byte> count = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(count, 1); data.Write(count);
        data.WriteByte(8); Text(data, "name"); Text(data, name);
        data.WriteByte(8); Text(data, "ip"); Text(data, address);
        data.WriteByte(0); data.WriteByte(0);
        AppStorage.AtomicWrite(target, data.ToArray());
    }

    private static void Text(Stream output, string value)
    {
        // Java DataInput uses modified UTF-8, including surrogate pairs as separate code units.
        using var bytes = new MemoryStream();
        foreach (char c in value)
        {
            if (c is >= '\u0001' and <= '\u007f') bytes.WriteByte((byte)c);
            else if (c <= '\u07ff')
            {
                bytes.WriteByte((byte)(0xc0 | c >> 6)); bytes.WriteByte((byte)(0x80 | c & 0x3f));
            }
            else
            {
                bytes.WriteByte((byte)(0xe0 | c >> 12)); bytes.WriteByte((byte)(0x80 | c >> 6 & 0x3f)); bytes.WriteByte((byte)(0x80 | c & 0x3f));
            }
        }
        if (bytes.Length > ushort.MaxValue) throw new InvalidDataException("El nombre del servidor es demasiado largo.");
        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)bytes.Length); output.Write(length);
        bytes.Position = 0; bytes.CopyTo(output);
    }
}
