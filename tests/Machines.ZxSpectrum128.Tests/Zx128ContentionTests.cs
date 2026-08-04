using Xunit;
using Machines.ZxSpectrum;
using Machines.ZxSpectrum128;

namespace Machines.ZxSpectrum128.Tests;

/// <summary>
/// ULA contention on the 128, where it depends on paging state as well as address.
/// </summary>
/// <remarks>
/// Banks 1, 3, 5 and 7 are contended. Bank 5 is fixed at 0x4000 so that window is
/// always contended; 0xC000-0xFFFF is contended only while an odd bank is paged
/// there. Bank 2 at 0x8000 and the ROM never contend.
///
/// The 128's drawn area starts at T-state 14,361 and lines are 228 T-states, so
/// the delay pattern is anchored differently from the 48K.
/// </remarks>
public class Zx128ContentionTests
{
    private static readonly int ContentionStart = UlaTiming.Spectrum128.ContentionStart;

    private static Zx128Machine MachineAt(ulong tState)
    {
        var machine = new Zx128Machine(new byte[0x8000]);
        machine.Reset();
        machine.Cpu.TotalCycles = tState;
        return machine;
    }

    /// <summary>T-states consumed by a 3-cycle read of <paramref name="address"/>.</summary>
    private static ulong ReadDuration(Zx128Machine machine, ushort address)
    {
        machine.Cpu.WaitCycles = 0;
        ulong start = machine.Cpu.TotalCycles;
        machine.ReadMemory(address);
        return machine.Cpu.TotalCycles - start;
    }

    [Fact]
    public void Bank5Window_IsAlwaysContendedInTheVisibleArea()
    {
        var machine = MachineAt((ulong)ContentionStart);
        Assert.True(ReadDuration(machine, 0x4000) > 3);
    }

    [Fact]
    public void Bank2Window_IsNeverContended()
    {
        var machine = MachineAt((ulong)ContentionStart);
        Assert.Equal(3ul, ReadDuration(machine, 0x8000));
    }

    [Fact]
    public void Rom_IsNeverContended()
    {
        var machine = MachineAt((ulong)ContentionStart);
        Assert.Equal(3ul, ReadDuration(machine, 0x0000));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(7, true)]
    public void PagedWindow_IsContendedOnlyForOddBanks(int bank, bool expectContended)
    {
        var machine = MachineAt((ulong)ContentionStart);
        machine.WritePort(0x7FFD, (byte)bank);

        // WritePort advances nothing, but re-seat the clock so every case is
        // measured at the same point in the frame.
        machine.Cpu.TotalCycles = (ulong)ContentionStart;

        ulong duration = ReadDuration(machine, 0xC000);

        if (expectContended) Assert.True(duration > 3, $"bank {bank} should contend, took {duration}");
        else Assert.Equal(3ul, duration);
    }

    [Fact]
    public void ContentionChangesAsBanksArePagedInAndOut()
    {
        var machine = MachineAt((ulong)ContentionStart);

        machine.WritePort(0x7FFD, 1); // odd -> contended
        machine.Cpu.TotalCycles = (ulong)ContentionStart;
        ulong odd = ReadDuration(machine, 0xC000);

        machine.WritePort(0x7FFD, 2); // even -> not contended
        machine.Cpu.TotalCycles = (ulong)ContentionStart;
        ulong even = ReadDuration(machine, 0xC000);

        Assert.True(odd > even);
        Assert.Equal(3ul, even);
    }

    [Fact]
    public void OutsideTheVisibleArea_NothingIsContended()
    {
        var machine = MachineAt(0);
        machine.WritePort(0x7FFD, 1); // contended bank paged in
        machine.Cpu.TotalCycles = 0;  // top border

        Assert.Equal(3ul, ReadDuration(machine, 0x4000));
        Assert.Equal(3ul, ReadDuration(machine, 0xC000));
    }

    [Fact]
    public void DelayPatternRepeatsEvery228TStates()
    {
        // The 128's line is 228 T-states, not 224.
        var machine = MachineAt((ulong)ContentionStart);
        ulong first = ReadDuration(machine, 0x4000);

        machine.Cpu.TotalCycles = (ulong)(ContentionStart + 228);
        Assert.Equal(first, ReadDuration(machine, 0x4000));

        machine.Cpu.TotalCycles = (ulong)(ContentionStart + 224);
        Assert.NotEqual(first, ReadDuration(machine, 0x4000));
    }

    [Fact]
    public void Spectrum48Contention_IsUnchangedByTheInjectedRule()
    {
        // The 48K must keep the plain address test. Guard against the 128's
        // paging-aware rule leaking into the shared ULA as a default.
        var ula = new FerrantiUla5C6C(new CpuZ80.Core.Ram(0xC000));
        var cpu = new CpuZ80.Core.Cpu(new CpuZ80.Core.Ram(0x10000), null, ula);
        ula.ConnectCpu(cpu);
        ula.OnFrameStart(0);

        cpu.TotalCycles = (ulong)UlaTiming.Spectrum48.ContentionStart;
        cpu.WaitCycles = 0;
        ula.OnMemoryAccess(0x4000, cpu);
        Assert.True(cpu.WaitCycles > 0);

        cpu.WaitCycles = 0;
        ula.OnMemoryAccess(0xC000, cpu);
        Assert.Equal(0, cpu.WaitCycles); // 48K: upper 32K never contends
    }
}
