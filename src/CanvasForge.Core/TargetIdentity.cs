using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CanvasForge.Core;

// Prepared image identity; independent of dispatch, brush routing and resume.
public static class TargetIdentity
{
    public static string Compute(PixelImage target,IReadOnlyList<int> indices,IReadOnlyList<PaletteEntry> palette)
    {
        if(indices.Count!=target.Width*target.Height)throw new ArgumentException("Invalid target indices.");
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("pixora-target-v1"));Span<byte> bytes=stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes,target.Width);hash.AppendData(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes,target.Height);hash.AppendData(bytes);hash.AppendData(target.Rgba);
        foreach(int i in indices){BinaryPrimitives.WriteInt32LittleEndian(bytes,i);hash.AppendData(bytes);}
        foreach(var entry in palette){bytes[0]=entry.Color.R;bytes[1]=entry.Color.G;bytes[2]=entry.Color.B;hash.AppendData(bytes[..3]);}
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
