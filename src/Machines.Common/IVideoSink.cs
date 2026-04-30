namespace Machines.Common;

/// <summary>
/// Receives rendered video frames from a video chip emulation.
/// Implementations render to screen (SDL2, WPF, etc.).
/// Pixels are packed ARGB32, top-left origin.
/// </summary>
public interface IVideoSink
{
    void SubmitFrame(ReadOnlySpan<uint> pixels, int width, int height);
}
