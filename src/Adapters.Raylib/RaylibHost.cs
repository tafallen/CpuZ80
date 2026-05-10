using Machines.Common;
using Raylib_cs;
using System.Numerics;

namespace Adapters.Raylib;

/// <summary>
/// Cross-platform host window using Raylib.
/// Implements IVideoSink (renders pixel frames) and IPhysicalKeyboard (queries key state).
/// </summary>
public sealed class RaylibHost : IVideoSink, IPhysicalKeyboard, IDisposable
{
    private readonly int _scale;
    private int          _width;
    private int          _height;
    private Texture2D    _texture;
    private uint[]       _rgbaBuffer;
    private bool         _disposed;

    public RaylibHost(string title = "ZX Spectrum", int scale = 3, int width = 320, int height = 240)
    {
        _scale  = scale;
        _width  = width;
        _height = height;

        Raylib_cs.Raylib.SetTraceLogLevel(TraceLogLevel.None);
        Raylib_cs.Raylib.InitWindow(width * scale, height * scale, title);
        Raylib_cs.Raylib.SetTargetFPS(50);

        var img = Raylib_cs.Raylib.GenImageColor(width, height, Color.Black);
        _texture = Raylib_cs.Raylib.LoadTextureFromImage(img);
        Raylib_cs.Raylib.UnloadImage(img);

        _rgbaBuffer = new uint[width * height];
    }

    public bool IsRunning => !Raylib_cs.Raylib.WindowShouldClose();

    public void PollEvents() => Raylib_cs.Raylib.PollInputEvents();

    public unsafe void SubmitFrame(ReadOnlySpan<uint> pixels, int width, int height)
    {
        // Resize texture/buffer if resolution changed (e.g. switching machines)
        if (width != _width || height != _height)
        {
            _width = width;
            _height = height;
            Raylib_cs.Raylib.UnloadTexture(_texture);
            var img = Raylib_cs.Raylib.GenImageColor(width, height, Color.Black);
            _texture = Raylib_cs.Raylib.LoadTextureFromImage(img);
            Raylib_cs.Raylib.UnloadImage(img);
            _rgbaBuffer = new uint[width * height];
            Raylib_cs.Raylib.SetWindowSize(width * _scale, height * _scale);
        }

        int count = Math.Min(pixels.Length, _rgbaBuffer.Length);
        for (int i = 0; i < count; i++)
        {
            uint argb = pixels[i];
            uint r = (argb >> 16) & 0xFF;
            uint g = (argb >>  8) & 0xFF;
            uint b =  argb        & 0xFF;
            uint a = (argb >> 24) & 0xFF;
            _rgbaBuffer[i] = r | (g << 8) | (b << 16) | (a << 24);
        }

        fixed (uint* ptr = _rgbaBuffer)
            Raylib_cs.Raylib.UpdateTexture(_texture, ptr);

        Raylib_cs.Raylib.BeginDrawing();
        Raylib_cs.Raylib.ClearBackground(Color.Black);
        Raylib_cs.Raylib.DrawTextureEx(_texture, Vector2.Zero, rotation: 0f, scale: _scale, Color.White);
        Raylib_cs.Raylib.EndDrawing();
    }

    public bool IsKeyDown(PhysicalKey key) =>
        RaylibKeyMap.TryGet(key, out var rk) && Raylib_cs.Raylib.IsKeyDown(rk);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Raylib_cs.Raylib.UnloadTexture(_texture);
        Raylib_cs.Raylib.CloseWindow();
    }
}
