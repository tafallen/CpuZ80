namespace Machines.Common;

/// <summary>
/// Receives rendered video frames from a video chip emulation.
/// Implementations render to screen (Raylib, SDL2, WPF, etc.).
/// </summary>
/// <remarks>
/// Pixels are packed RGBA32, top-left origin. On a little-endian machine the
/// bytes land in memory as R, G, B, A — the layout GPU texture uploads expect —
/// so a host can hand the buffer straight to the graphics API with no
/// conversion. Packed into a <see cref="uint"/> that reads 0xAABBGGRR, meaning
/// pure red is 0xFF0000FF and pure blue is 0xFFFF0000.
///
/// This was ARGB32 until the per-pixel conversion in the Raylib adapter turned
/// out to cost more than the entire ZX80 frame render. Producers use fixed
/// palettes, so emitting the host's order directly is free.
/// </remarks>
public interface IVideoSink
{
    void SubmitFrame(ReadOnlySpan<uint> pixels, int width, int height);
}
