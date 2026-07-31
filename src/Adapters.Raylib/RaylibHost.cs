using Machines.Common;
using Raylib_cs;
using System.Numerics;

namespace Adapters.Raylib;

/// <summary>
/// Cross-platform host window using Raylib.
/// Implements IVideoSink (renders pixel frames), IPhysicalKeyboard (queries key state),
/// and IAudioSink (outputs sound samples).
/// </summary>
public sealed class RaylibHost : IVideoSink, IPhysicalKeyboard, IAudioSink, IDisposable
{
    private readonly int _scale;
    private int          _width;
    private int          _height;
    private Texture2D    _texture;
    private bool         _disposed;

    // Audio members
    private AudioStream _audioStream;
    private readonly Queue<short> _audioBuffer = new(8192);
    private const int TargetSampleRate = 44100;

    public RaylibHost(string title = "ZX Spectrum", int scale = 3, int width = 320, int height = 240)
    {
        _scale  = scale;
        _width  = width;
        _height = height;

        Raylib_cs.Raylib.SetTraceLogLevel(TraceLogLevel.None);
        Raylib_cs.Raylib.InitWindow(width * scale, height * scale, title);
        Raylib_cs.Raylib.SetTargetFPS(50);

        // Initialize Audio
        Raylib_cs.Raylib.InitAudioDevice();
        Raylib_cs.Raylib.SetAudioStreamBufferSizeDefault(2048);
        _audioStream = Raylib_cs.Raylib.LoadAudioStream(TargetSampleRate, 16, 1);
        Raylib_cs.Raylib.PlayAudioStream(_audioStream);

        var img = Raylib_cs.Raylib.GenImageColor(width, height, Color.Black);
        _texture = Raylib_cs.Raylib.LoadTextureFromImage(img);
        Raylib_cs.Raylib.UnloadImage(img);

    }

    public bool IsRunning => !Raylib_cs.Raylib.WindowShouldClose();

    public void PollEvents()
    {
        Raylib_cs.Raylib.PollInputEvents();
        UpdateAudio();
    }

    private unsafe void UpdateAudio()
    {
        if (!Raylib_cs.Raylib.IsAudioStreamPlaying(_audioStream)) return;

        // Feed Raylib any processed audio buffers
        while (Raylib_cs.Raylib.IsAudioStreamProcessed(_audioStream))
        {
            int count = Math.Min(_audioBuffer.Count, 1024);
            if (count == 0) break;

            short[] samples = new short[count];
            for (int i = 0; i < count; i++) samples[i] = _audioBuffer.Dequeue();

            fixed (short* ptr = samples)
            {
                Raylib_cs.Raylib.UpdateAudioStream(_audioStream, ptr, count);
            }
        }
    }

    public void SubmitSamples(ReadOnlySpan<short> samples, int sampleRate)
    {
        // For simplicity, we assume machines provide 44.1kHz.
        // We cap the buffer to prevent runaway latency if the host is slow.
        if (_audioBuffer.Count > 16384) return;

        foreach (var s in samples)
        {
            _audioBuffer.Enqueue(s);
        }
    }

    public unsafe void SubmitFrame(ReadOnlySpan<uint> pixels, int width, int height)
    {
        // Resize texture if resolution changed (e.g. switching machines)
        if (width != _width || height != _height)
        {
            _width = width;
            _height = height;
            Raylib_cs.Raylib.UnloadTexture(_texture);
            var img = Raylib_cs.Raylib.GenImageColor(width, height, Color.Black);
            _texture = Raylib_cs.Raylib.LoadTextureFromImage(img);
            Raylib_cs.Raylib.UnloadImage(img);
            Raylib_cs.Raylib.SetWindowSize(width * _scale, height * _scale);
        }

        if (pixels.Length < width * height)
            throw new ArgumentException($"Frame is {pixels.Length} pixels, expected at least {width * height}.", nameof(pixels));

        // Machines emit RGBA32 (see IVideoSink), which is exactly what the
        // texture expects, so the frame uploads straight from the machine's
        // buffer. This used to unpack and repack all 76,800 pixels every frame.
        fixed (uint* ptr = pixels)
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
        Raylib_cs.Raylib.UnloadAudioStream(_audioStream);
        Raylib_cs.Raylib.CloseAudioDevice();
        Raylib_cs.Raylib.UnloadTexture(_texture);
        Raylib_cs.Raylib.CloseWindow();
    }
}
