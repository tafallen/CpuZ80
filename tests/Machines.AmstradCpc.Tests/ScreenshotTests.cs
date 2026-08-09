using Xunit;
using Machines.AmstradCpc;

namespace Machines.AmstradCpc.Tests;

/// <summary>Writes the boot screen to a PPM so it can be looked at, not just asserted.</summary>
public class ScreenshotTests
{
    [Fact]
    public void WriteBootScreen()
    {
        string? outDir = Environment.GetEnvironmentVariable("CPC_SHOT_DIR");
        if (outDir is null) return;

        var m = CpcBootTests.BuildRealMachine();
        if (m is null) return;
        for (int f = 0; f < 150; f++) m.RunFrame();

        var sink = new CpcBootTests.CaptureSink();
        m.RenderFrame(sink);

        using var fs = File.Create(Path.Combine(outDir, "cpc-boot.ppm"));
        using var w = new StreamWriter(fs);
        w.Write($"P3\n{sink.Width} {sink.Height}\n255\n");
        for (int i = 0; i < sink.Frame.Length; i++)
        {
            uint p = sink.Frame[i];
            w.Write($"{p & 0xFF} {(p >> 8) & 0xFF} {(p >> 16) & 0xFF}\n");
        }
    }
}
