using BenchmarkDotNet.Attributes;
using Machines.ZxSpectrum;

namespace CpuZ80.Benchmarks;

/// <summary>
/// The pixel hand-off between a machine's ARGB32 frame buffer and the host's
/// native texture format.
/// </summary>
/// <remarks>
/// IMPORTANT: <see cref="ConvertArgbToRgba"/> mirrors the loop in
/// <c>Adapters.Raylib.RaylibHost.SubmitFrame</c>. It is duplicated here rather
/// than referenced because Adapters.Raylib needs the native Raylib binary and a
/// window, which a headless benchmark run cannot open. If that loop changes,
/// change this one to match, or the benchmark stops meaning anything.
///
/// Tracks review finding:
///   * PIXEL — every producer uses a compile-time constant palette, so storing
///             those literals in host byte order turns this loop into a bulk
///             copy. <see cref="BulkCopy"/> is the target to converge on.
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

    /// <summary>What the host does today: unpack and repack every pixel.</summary>
    [Benchmark(Baseline = true, Description = "ARGB32 -> RGBA32 per pixel (current) [PIXEL]")]
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

    /// <summary>What it costs if palettes are already stored in host byte order.</summary>
    [Benchmark(Description = "Bulk copy (palettes already host-order) [target]")]
    public void BulkCopy() => Array.Copy(_source, _destination, PixelCount);
}
