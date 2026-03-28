namespace NuraLib.Utilities;

public static class ByteArrays {
    public static byte[] Combine(params byte[][] parts) {
        var length = parts.Sum(part => part.Length);
        var output = new byte[length];
        var offset = 0;

        foreach (var part in parts) {
            Buffer.BlockCopy(part, 0, output, offset, part.Length);
            offset += part.Length;
        }

        return output;
    }
}
