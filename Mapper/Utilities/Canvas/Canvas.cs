using System.Drawing;
using WorldEditor;

namespace Mapper
{
    public class Canvas : ICanvas
    {
        public Coords TopLeftPoint { get; private set; }
        public Size Size { get; private set; }
        public Direction Direction { get; private set; }

        private readonly PixelTransformation _pixelTransformation;
        private readonly byte[] _pixelData;
        private bool _isEmpty = true;

        public Canvas(Coords topleft, Size size, Direction direction = Direction.North)
        {
            TopLeftPoint = topleft;
            Size = size;
            Direction = direction;

            _pixelTransformation = new PixelTransformation(Direction, TopLeftPoint, Size);
            _pixelData = new byte[(int)size.Width * (int)size.Height * 4];
        }

        public void SetPixel(int xInWorld, int zInWorld, VecRgb color)
        {
            _pixelTransformation.TransformPixelCoords(xInWorld, zInWorld, out int xP, out int yP);

            int offset = (yP * (int)Size.Height + xP) * 4;
            Rgb rgb = color.ToByteRgb();

            _pixelData[offset] = rgb.B;
            _pixelData[offset + 1] = rgb.G;
            _pixelData[offset + 2] = rgb.R;
            _pixelData[offset + 3] = byte.MaxValue;
            _isEmpty = false;
        }

        public int PixelWidth => Size.Height;
        public int PixelHeight => Size.Width;

        public byte[]? GetPixels()
        {
            return _isEmpty ? null : _pixelData;
        }

        public void Dispose() { }
    }
}
