using System.Numerics;
using System.Runtime.InteropServices;

namespace CanvasForge.App;

// Exactly the original native BGRA -> RGBA conversion, including opaque alpha.
// Process several pixels at a time to reduce synchronous capture work while a
// stroke is held. Pixel values, capture geometry and proof policy stay identical.
internal static class NativePixelBuffer
{
    internal static void BgraToOpaqueRgba(Span<byte> buffer)
    {
        if(buffer.Length%4!=0)throw new ArgumentException("Expected complete BGRA pixels.",nameof(buffer));
        if(!BitConverter.IsLittleEndian)
        {
            for(int p=0;p<buffer.Length;p+=4)
            {byte oldBlue=buffer[p];buffer[p]=buffer[p+2];buffer[p+2]=oldBlue;buffer[p+3]=255;}
            return;
        }
        var pixels=MemoryMarshal.Cast<byte,uint>(buffer);
        var red=new Vector<uint>(0x00ff0000u);var blue=new Vector<uint>(0x000000ffu);
        var green=new Vector<uint>(0x0000ff00u);var alpha=new Vector<uint>(0xff000000u);
        int i=0;
        if(Vector.IsHardwareAccelerated)
            for(;i<=pixels.Length-Vector<uint>.Count;i+=Vector<uint>.Count)
            {
                var v=new Vector<uint>(pixels.Slice(i,Vector<uint>.Count));
                ((v&green)|((v&red)>>16)|((v&blue)<<16)|alpha).CopyTo(pixels.Slice(i,Vector<uint>.Count));
            }
        for(;i<pixels.Length;i++)
        {uint v=pixels[i];pixels[i]=(v&0x0000ff00u)|((v&0x00ff0000u)>>16)|((v&0x000000ffu)<<16)|0xff000000u;}
    }
}
