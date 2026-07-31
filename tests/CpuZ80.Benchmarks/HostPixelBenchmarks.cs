using BenchmarkDotNet.Attributes;
using Machines.ZxSpectrum;

namespace CpuZ80.Benchmarks;

/// <summary>
/// The pixel hand-off between a machine's frame buffer and the host's texture.
/// </summary>
/// <remarks>
/// Machines now emit RGBA32 (see <c>IVideoSink</c>), the same layout the texture
/// wants, so <c>RaylibHost.SubmitFrame</c> pins the incoming span and uploads it
/// directly — no conversion and no intermediate buffer. <see cref="DirectUpload"/>
/// stands in for that: a pin plus a checksum read, since a headless run cannot
/// call into Raylib.
///
/// <see cref="ConvertArgbToRgba"/> is the loop that used to run every frame,
/// kept as the reference point. It is what returning to an ARGB contract would
/// cost.
///
/// Tracks review finding PIXEL.
/// </remarks>
public class HostPixelBenchmarks
{
    private const int Width = ZxSpectrumVideo.TotalWidth;
    private const int Height = ZxSpectrumVideo.TotalHeight;
    private const int PixelCount = Width * Height;

    private uint[] _source = null!;
    private uint[] _destination = null!;

    [GlobalSetup]
    public void Setup()
    {
        _source = new uint[PixelCount];
        _destination = new uint[PixelCount];
        var rnd = new Random(20260729);
        for (int i = 0; i < PixelCount; i++) _source[i] = (uint)rnd.Next();
    }

    /// <summary>The former per-pixel unpack/repack. Baseline for comparison only.</summary>
    [Benchmark(Baseline = true, Description = "ARGB32 -> RGBA32 per pixel (former cost) [PIXEL]")]
    public void ConvertArgbToRgba()
    {
        var src = _source;
        var dst = _destination;
        for (int i = 0; i < src.Length; i++)
        {
            uint argb = src[i];
            uint r = (argb >> 16) & 0xFF;
            uint g = (argb >> 8) & 0xFF;
            uint b = argb & 0xFF;
            uint a = (argb >> 24) & 0xFF;
            dst[i] = r | (g << 8) | (b << 16) | (a << 24);
        }
    }

    /// <summary>A defensive copy, if a host needed its own buffer.</summary>
    [Benchmark(Description = "Bulk copy")]
    public void BulkCopy() => Array.Copy(_source, _destination, PixelCount);

    /// <summary>What the host does now: pin the machine's buffer and upload it.</summary>
    [Benchmark(Description = "Direct upload, no copy (current)")]
    public unsafe uint DirectUpload()
    {
        ReadOnlySpan<uint> pixels = _source;
        fixed (uint* ptr = pixels)
        {
            return ptr[0] ^ ptr[PixelCount - 1];
        }
    }
}
