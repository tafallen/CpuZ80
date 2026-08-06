using Xunit;
using CpuZ80.Core;
using Machines.ZxSpectrum;

namespace Machines.ZxSpectrum.Tests;

/// <summary>
/// The ULA's contention delay pattern and whether I/O is contended, both of
/// which vary by machine.
/// </summary>
/// <remarks>
/// The 48K and 128 share the 6,5,4,3,2,1,0,0 sequence and contend I/O as well as
/// memory. The +2A/+3 gate array uses 1,0,7,6,5,4,3,2 and applies contention
/// only when MREQ is active, so I/O is not contended at all.
/// </remarks>
public class UlaContentionPatternTests
{
    [Fact]
    public void Spectrum48_KeepsTheClassicPattern()
    {
        Assert.Equal(new byte[] { 6, 5, 4, 3, 2, 1, 0, 0 }, UlaTiming.Spectrum48.ContentionPattern);
        Assert.True(UlaTiming.Spectrum48.ContendsIo);
    }

    [Fact]
    public void Spectrum128_KeepsTheClassicPattern()
    {
        Assert.Equal(new byte[] { 6, 5, 4, 3, 2, 1, 0, 0 }, UlaTiming.Spectrum128.ContentionPattern);
        Assert.True(UlaTiming.Spectrum128.ContendsIo);
    }

    [Fact]
    public void Spectrum2A_HasItsOwnPatternAndDoesNotContendIo()
    {
        var t = UlaTiming.Spectrum2A;

        Assert.Equal(new byte[] { 1, 0, 7, 6, 5, 4, 3, 2 }, t.ContentionPattern);
        Assert.False(t.ContendsIo);
        Assert.Equal(14364, t.ContentionStart);
        Assert.Equal(228, t.CyclesPerLine);
        Assert.Equal(70908, t.FrameCycles);
    }

    [Fact]
    public void ContentionFollowsTheSuppliedPattern()
    {
        // Probe the first eight drawn T-states and compare against the table.
        var ula = new FerrantiUla5C6C(new Ram(0xC000), timing: UlaTiming.Spectrum2A);
        var cpu = new Cpu(new Ram(0x10000), null, ula);
        ula.ConnectCpu(cpu);
        ula.OnFrameStart(0);

        int start = UlaTiming.Spectrum2A.ContentionStart;
        byte[] expected = UlaTiming.Spectrum2A.ContentionPattern;

        for (int i = 0; i < 8; i++)
        {
            cpu.TotalCycles = (ulong)(start + i);
            cpu.WaitCycles = 0;
            ula.OnMemoryAccess(0x4000, cpu);
            Assert.Equal(expected[i], (byte)cpu.WaitCycles);
        }
    }

    [Fact]
    public void IoIsContendedOn48K()
    {
        var ula = new FerrantiUla5C6C(new Ram(0xC000));
        var cpu = new Cpu(new Ram(0x10000), null, ula);
        ula.ConnectCpu(cpu);
        ula.OnFrameStart(0);

        cpu.TotalCycles = (ulong)UlaTiming.Spectrum48.ContentionStart;
        cpu.WaitCycles = 0;
        ula.OnPortAccess(0x00FE, cpu);   // even port: the ULA's own

        Assert.True(cpu.WaitCycles > 0);
    }

    [Fact]
    public void IoIsNotContendedOnPlus3()
    {
        // The +2A/+3 gate array contends only on MREQ.
        var ula = new FerrantiUla5C6C(new Ram(0xC000), timing: UlaTiming.Spectrum2A);
        var cpu = new Cpu(new Ram(0x10000), null, ula);
        ula.ConnectCpu(cpu);
        ula.OnFrameStart(0);

        cpu.TotalCycles = (ulong)UlaTiming.Spectrum2A.ContentionStart;
        cpu.WaitCycles = 0;
        ula.OnPortAccess(0x00FE, cpu);

        Assert.Equal(0, cpu.WaitCycles);
    }

    [Fact]
    public void MemoryIsStillContendedOnPlus3()
    {
        // Only I/O is exempt; memory contention still applies.
        var ula = new FerrantiUla5C6C(new Ram(0xC000), timing: UlaTiming.Spectrum2A);
        var cpu = new Cpu(new Ram(0x10000), null, ula);
        ula.ConnectCpu(cpu);
        ula.OnFrameStart(0);

        cpu.TotalCycles = (ulong)UlaTiming.Spectrum2A.ContentionStart;
        cpu.WaitCycles = 0;
        ula.OnMemoryAccess(0x4000, cpu);

        Assert.True(cpu.WaitCycles > 0);
    }
}
