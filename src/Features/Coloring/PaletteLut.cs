using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Features.Coloring;

/// <summary>
/// GPU 256x8 RGBA lookup texture baked directly from Palette so the GPU shader
/// never duplicates palette stop tables.
/// </summary>
public sealed class PaletteLut : IDisposable
{
    public const int Width = 256;

    Texture _texture;
    bool _loaded;

    public Texture Texture => _texture;
    public bool IsLoaded => _loaded;

    public void Load()
    {
        if (_loaded) return;

        int height = Palette.Count;
        var pixels = new Color[Width * height];
        for (int row = 0; row < height; row++)
        {
            int offset = row * Width;
            for (int col = 0; col < Width; col++)
            {
                double t = col / (double)(Width - 1);
                pixels[offset + col] = Palette.SampleUnwrapped(row, t);
            }
        }

        Image image = Raylib.GenImageColor(Width, height, new Color(0, 0, 0, 255));
        _texture = Raylib.LoadTextureFromImage(image);
        Raylib.UnloadImage(image);

        unsafe
        {
            fixed (Color* ptr = pixels)
                Raylib.UpdateTexture(_texture, ptr);
        }

        Raylib.SetTextureFilter(_texture, TextureFilter.TEXTURE_FILTER_POINT);
        Raylib.SetTextureWrap(_texture, TextureWrap.TEXTURE_WRAP_CLAMP);
        _loaded = true;
    }

    public void Dispose()
    {
        if (!_loaded) return;
        Raylib.UnloadTexture(_texture);
        _loaded = false;
    }
}
