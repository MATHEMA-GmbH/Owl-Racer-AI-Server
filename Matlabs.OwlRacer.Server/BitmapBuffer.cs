using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Matlabs.OwlRacer.Server
{
    public class BitmapBuffer
    {
        public byte[] Data { get; init; }
        public int Height { get; init; }
        public int Width { get; init; }
        public int BitDepth { get; init; }
        public int ByteDepth => BitDepth / 8;

        public BitmapBuffer(Bitmap bitmap)
        {
            if (bitmap == null)
            {
                throw new ArgumentNullException(nameof(bitmap));
            }

            var srcRect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(srcRect, ImageLockMode.ReadWrite, bitmap.PixelFormat);

            Width = bitmap.Width;
            Height = bitmap.Height;
            BitDepth = Image.GetPixelFormatSize(data.PixelFormat);
            Data = new byte[data.Width * data.Height * ByteDepth];
            
            Marshal.Copy(data.Scan0, Data, 0, Data.Length);

            bitmap.UnlockBits(data);
        }

        public Color GetPixel(int x, int y)
        {
            var baseIndex = (x * ByteDepth) + (y * (Width * ByteDepth));

            return BitDepth switch
            {
                32 => Color.FromArgb(Data[baseIndex + 3], Data[baseIndex], Data[baseIndex + 1], Data[baseIndex + 2]),
                24 => Color.FromArgb(Data[baseIndex], Data[baseIndex + 1], Data[baseIndex + 2]),
                _ => throw new InvalidOperationException("Unsupported pixel depth.")
            };
        }
    }
}
