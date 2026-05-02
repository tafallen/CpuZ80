using Machines.Common;
using Raylib_cs;
using System.Numerics;

namespace Adapters.Raylib;

/// <summary>
/// Cross-platform host window using Raylib.
/// Implements IVideoSink (renders pixel frames) and IPhysicalKeyboard (queries key state).
///
/// Copied from Cpu6502/Adapters.Raylib; Machines.Atom dependency removed.
/// Audio (IAudioSink) is not implemented — add when a machine requires it.
///
/// Typical loop:
/// <code>
///   using var host = new RaylibHost("Sinclair ZX80", scale: 3);
///   while (host.IsRunning)
///   {
///       host.PollEvents();
///       machine.RunFrame();
///       machine.RenderFrame(host);
///   }
/// </code>
/// </summary>
public sealed class RaylibHost : IVideoSink, IPhysicalKeyboard, IDisposable
{
    private readonly int     _scale;
    private readonly int     _frameWidth;
    private readonly int     _frameHeight;
    private          Texture2D _texture;
    private readonly uint[]  _rgbaBuffer;
    private          bool    _disposed;

    public RaylibHost(string title = "ZX80", int scale = 3, int frameWidth = 256, int frameHeight = 192)
    {
        _scale       = scale;
        _frameWidth  = frameWidth;
        _frameHeight = frameHeight;

        Raylib_cs.Raylib.InitWindow(frameWidth * scale, frameHeight * scale, title);
        Raylib_cs.Raylib.SetTargetFPS(50);

        var img = Raylib_cs.Raylib.GenImageColor(frameWidth, frameHeight, Color.Black);
        _texture = Raylib_cs.Raylib.LoadTextureFromImage(img);
        Raylib_cs.Raylib.UnloadImage(img);

        _rgbaBuffer = new uint[frameWidth * frameHeight];
    }

    public bool IsRunning => !Raylib_cs.Raylib.WindowShouldClose();

    /// <summary>Process OS events. Call once per frame before RunFrame.</summary>
    public void PollEvents() => Raylib_cs.Raylib.PollInputEvents();

    // ── IVideoSink ────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts ARGB32 pixels to RGBA32 (Raylib's native format), uploads to GPU,
    /// and draws scaled to the window.
    /// </summary>
    public unsafe void SubmitFrame(ReadOnlySpan<uint> pixels, int width, int height)
    {
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

    // ── IPhysicalKeyboard ─────────────────────────────────────────────────────

    public bool IsKeyDown(PhysicalKey key) =>
        RaylibKeyMap.TryGet(key, out var rk) && Raylib_cs.Raylib.IsKeyDown(rk);

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Raylib_cs.Raylib.UnloadTexture(_texture);
        Raylib_cs.Raylib.CloseWindow();
    }
}
