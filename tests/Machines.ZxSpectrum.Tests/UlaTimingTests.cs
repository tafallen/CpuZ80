using Xunit;
using CpuZ80.Core;
using Machines.ZxSpectrum;

namespace Machines.ZxSpectrum.Tests;

/// <summary>
/// The ULA's frame geometry as data rather than constants, so the 128K (228
/// T-states per line, 70,908 per frame) can share the 48K implementation.
/// </summary>
public class UlaTimingTests
{
    [Fact]
    public void Spectrum48_HasTheDocumented48KGeometry()
    {
        var t = UlaTiming.Spectrum48;

        Assert.Equal(224, t.CyclesPerLine);
        Assert.Equal(69888, t.FrameCycles);
        Assert.Equal(64 * 224, t.ContentionStart);
        Assert.Equal(256 * 224, t.ContentionEnd);
    }

    [Fact]
    public void Spectrum128_HasTheDocumented128KGeometry()
    {
        // 128K: 228 T-states per line, 311 lines, 70,908 per frame.
        var t = UlaTiming.Spectrum128;

        Assert.Equal(228, t.CyclesPerLine);
        Assert.Equal(70908, t.FrameCycles);
        Assert.Equal(311 * 228, t.FrameCycles);
    }

    [Fact]
    public void Spectrum128_ContentionWindowIsShiftedRelativeTo48K()
    {
        // The drawn area starts later on the 128K, and each line is longer.
        Assert.True(UlaTiming.Spectrum128.ContentionStart > UlaTiming.Spectrum48.ContentionStart);
        Assert.Equal(192, (UlaTiming.Spectrum128.ContentionEnd - UlaTiming.Spectrum128.ContentionStart) / 228);
        Assert.Equal(192, (UlaTiming.Spectrum48.ContentionEnd - UlaTiming.Spectrum48.ContentionStart) / 224);
    }

    [Fact]
    public void UlaDefaultsTo48KTiming()
    {
        // Existing callers construct the ULA without timing and must be unaffected.
        var ula = new FerrantiUla5C6C(new Ram(0xC000));
        Assert.Equal(UlaTiming.Spectrum48, ula.Timing);
    }

    [Fact]
    public void UlaAcceptsExplicitTiming()
    {
        var ula = new FerrantiUla5C6C(new Ram(0xC000), timing: UlaTiming.Spectrum128);
        Assert.Equal(UlaTiming.Spectrum128, ula.Timing);
    }

    [Fact]
    public void ContentionFollowsTheSuppliedLineLength()
    {
        // With 228-cycle lines the delay pattern must repeat on a 228 boundary,
        // not 224. Probe the same offset into two consecutive lines.
        var ram = new Ram(0xC000);
        var ula = new FerrantiUla5C6C(ram, timing: UlaTiming.Spectrum128);
        var cpu = new Cpu(new Ram(0x10000), null, ula);
        ula.ConnectCpu(cpu);
        ula.OnFrameStart(0);

        int start = UlaTiming.Spectrum128.ContentionStart;

        // Offset 0 into a drawn line has the maximum delay of 6.
        Assert.Equal(6, DelayAt(ula, cpu, (ulong)start));
        // One full 228-cycle line later, the same offset must give the same delay.
        Assert.Equal(6, DelayAt(ula, cpu, (ulong)(start + 228)));
        // 224 later — a whole line on a 48K — must NOT line up.
        Assert.NotEqual(6, DelayAt(ula, cpu, (ulong)(start + 224)));
    }

    private static int DelayAt(FerrantiUla5C6C ula, Cpu cpu, ulong tState)
    {
        cpu.TotalCycles = tState;
        cpu.WaitCycles = 0;
        ula.OnMemoryAccess(0x4000, cpu);
        return cpu.WaitCycles;
    }
}
