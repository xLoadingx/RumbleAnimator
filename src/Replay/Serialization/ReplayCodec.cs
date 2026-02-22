using System.IO;
using System.IO.Compression;

namespace ReplayMod;

public class ReplayCodec
{
    public static byte[] Compress(byte[] data)
    {
        using var ms = new MemoryStream();
        
        using (var brotli = new BrotliStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            brotli.Write(data, 0, data.Length);
        }

        return ms.ToArray();
    }

    public static byte[] Decompress(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        
        brotli.CopyTo(output);
        return output.ToArray();
    }
}