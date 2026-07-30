using Xunit;
using CpuZ80.Core;

namespace CpuZ80.Tests.Hardware;

/// <summary>
/// Every memory access the CPU makes must be visible to <see cref="ICpuHost"/>.
/// Machines rely on this to model wait states (ZX Spectrum ULA contention) and to
/// track bus activity; an access that skips the hook is an access that silently
/// escapes contention.
/// </summary>
public class CpuHostHookTests
{
    /// <summary>Counts bus traffic so it can be compared against hook invocations.</summary>
    private sealed class CountingBus : IBus
    {
        private readonly byte[] _mem = new byte[65536];
        public int Reads, Writes;
        public int Accesses => Reads + Writes;

        public byte Read(ushort address) { Reads++; return _mem[address]; }
        public void Write(ushort address, byte value) { Writes++; _mem[address] = value; }

        /// <summary>Seeds memory without disturbing the counters.</summary>
        public void Poke(ushort address, params byte[] values)
        {
            for (int i = 0; i < values.Length; i++) _mem[address + i] = values[i];
        }
    }

    private sealed class CountingHost : ICpuHost
    {
        public int MemoryAccesses, PortAccesses;
        public void OnMemoryAccess(ushort address, Cpu cpu) => MemoryAccesses++;
        public void OnPortAccess(ushort address, Cpu cpu) => PortAccesses++;
    }

    /// <summary>Assembles <paramref name="program"/> at 0x0000 and runs <paramref name="steps"/> instructions.</summary>
    private static (CountingBus Bus, CountingHost Host, Cpu Cpu) Run(int steps, params byte[] program)
    {
        var bus = new CountingBus();
        var host = new CountingHost();
        bus.Poke(0x0000, program);

        var cpu = new Cpu(bus, null, host);
        cpu.Reset();
        cpu.SP = 0xFFF0;

        bus.Reads = 0;
        bus.Writes = 0;
        for (int i = 0; i < steps; i++) cpu.Step();

        return (bus, host, cpu);
    }

    [Fact]
    public void Push_NotifiesHostForEveryBusAccess()
    {
        // LD HL,0x1234 ; PUSH HL
        var (bus, host, _) = Run(2, 0x21, 0x34, 0x12, 0xE5);

        Assert.Equal(bus.Accesses, host.MemoryAccesses);
    }

    [Fact]
    public void Pop_NotifiesHostForEveryBusAccess()
    {
        // LD HL,0x1234 ; PUSH HL ; POP DE
        var (bus, host, cpu) = Run(3, 0x21, 0x34, 0x12, 0xE5, 0xD1);

        Assert.Equal(0x1234, cpu.DE);
        Assert.Equal(bus.Accesses, host.MemoryAccesses);
    }

    [Fact]
    public void Call_NotifiesHostForStackWrites()
    {
        // CALL 0x0100
        var (bus, host, cpu) = Run(1, 0xCD, 0x00, 0x01);

        Assert.Equal(0x0100, cpu.PC);
        Assert.Equal(bus.Accesses, host.MemoryAccesses);
    }

    [Fact]
    public void Ret_NotifiesHostForStackReads()
    {
        // CALL 0x0100, then RET placed at 0x0100
        var bus = new CountingBus();
        var host = new CountingHost();
        bus.Poke(0x0000, 0xCD, 0x00, 0x01);
        bus.Poke(0x0100, 0xC9);

        var cpu = new Cpu(bus, null, host);
        cpu.Reset();
        cpu.SP = 0xFFF0;

        bus.Reads = 0;
        bus.Writes = 0;
        cpu.Step(); // CALL
        cpu.Step(); // RET

        Assert.Equal(0x0003, cpu.PC);
        Assert.Equal(bus.Accesses, host.MemoryAccesses);
    }

    [Fact]
    public void Rst_NotifiesHostForStackWrites()
    {
        // RST 08h
        var (bus, host, cpu) = Run(1, 0xCF);

        Assert.Equal(0x0008, cpu.PC);
        Assert.Equal(bus.Accesses, host.MemoryAccesses);
    }

    [Fact]
    public void Ldir_NotifiesHostForEveryBusAccess()
    {
        // LD HL,0x0200 ; LD DE,0x0300 ; LD BC,0x0010 ; LDIR
        // LDIR repeats by rewinding PC, so step generously to run it to completion.
        var (bus, host, cpu) = Run(20,
            0x21, 0x00, 0x02,
            0x11, 0x00, 0x03,
            0x01, 0x10, 0x00,
            0xED, 0xB0);

        Assert.Equal(0, cpu.BC);
        Assert.Equal(bus.Accesses, host.MemoryAccesses);
    }

    [Fact]
    public void Lddr_NotifiesHostForEveryBusAccess()
    {
        // LD HL,0x0200 ; LD DE,0x0300 ; LD BC,0x0008 ; LDDR
        var (bus, host, cpu) = Run(14,
            0x21, 0x00, 0x02,
            0x11, 0x00, 0x03,
            0x01, 0x08, 0x00,
            0xED, 0xB8);

        Assert.Equal(0, cpu.BC);
        Assert.Equal(bus.Accesses, host.MemoryAccesses);
    }

    [Fact]
    public void Cpir_NotifiesHostForEveryBusAccess()
    {
        // LD HL,0x0200 ; LD BC,0x0008 ; LD A,0xFF ; CPIR  (0xFF never matches zeroed RAM)
        var (bus, host, cpu) = Run(12,
            0x21, 0x00, 0x02,
            0x01, 0x08, 0x00,
            0x3E, 0xFF,
            0xED, 0xB1);

        Assert.Equal(0, cpu.BC);
        Assert.Equal(bus.Accesses, host.MemoryAccesses);
    }

    [Fact]
    public void Ldi_NotifiesHostForEveryBusAccess()
    {
        // LD HL,0x0200 ; LD DE,0x0300 ; LD BC,0x0004 ; LDI
        var (bus, host, _) = Run(4,
            0x21, 0x00, 0x02,
            0x11, 0x00, 0x03,
            0x01, 0x04, 0x00,
            0xED, 0xA0);

        Assert.Equal(bus.Accesses, host.MemoryAccesses);
    }

    [Fact]
    public void ExSpHl_NotifiesHostForEveryBusAccess()
    {
        // LD HL,0x1234 ; PUSH HL ; EX (SP),HL
        var (bus, host, _) = Run(3, 0x21, 0x34, 0x12, 0xE5, 0xE3);

        Assert.Equal(bus.Accesses, host.MemoryAccesses);
    }

    [Fact]
    public void HookDoesNotAlterCycleCount()
    {
        // The hook is an observation point, not a timing source: attaching a host
        // that adds no wait states must not change TotalCycles.
        byte[] program = [0x21, 0x34, 0x12, 0xE5, 0xD1, 0xCD, 0x00, 0x01];

        var hostedBus = new CountingBus();
        hostedBus.Poke(0x0000, program);
        var hosted = new Cpu(hostedBus, null, new CountingHost());
        hosted.Reset();
        hosted.SP = 0xFFF0;

        var bareBus = new CountingBus();
        bareBus.Poke(0x0000, program);
        var bare = new Cpu(bareBus);
        bare.Reset();
        bare.SP = 0xFFF0;

        for (int i = 0; i < 3; i++) { hosted.Step(); bare.Step(); }

        Assert.Equal(bare.TotalCycles, hosted.TotalCycles);
        Assert.Equal(bareBus.Accesses, hostedBus.Accesses);
    }
}
