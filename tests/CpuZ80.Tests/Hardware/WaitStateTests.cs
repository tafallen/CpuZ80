using Xunit;
using CpuZ80.Core;

namespace CpuZ80.Tests.Hardware;

/// <summary>
/// Wait-state injection: the mechanism machines use to model hardware
/// contention. A host adds to <see cref="Cpu.WaitCycles"/> from
/// <see cref="ICpuHost.OnMemoryAccess"/>, and those cycles are consumed as extra
/// T-states by the next <c>Tick</c>.
/// </summary>
/// <remarks>
/// <see cref="Cpu.WaitCycles"/> is the only wait mechanism. A <c>WaitPin</c>
/// property used to sit alongside it, but <c>Tick</c> spun on
/// <c>while (WaitPin || WaitCycles > 0)</c> with nothing in that loop able to
/// clear the pin, so asserting it on a single-threaded emulator never returned.
/// Nothing ever set it; it was removed rather than tested.
/// </remarks>
public class WaitStateTests
{
    private sealed class StubBus : IBus
    {
        private readonly byte[] _mem = new byte[65536];
        public byte Read(ushort address) => _mem[address];
        public void Write(ushort address, byte value) => _mem[address] = value;
        public void Poke(ushort address, params byte[] values)
        {
            for (int i = 0; i < values.Length; i++) _mem[address + i] = values[i];
        }
    }

    /// <summary>Injects a fixed number of wait cycles on each memory access.</summary>
    private sealed class WaitInjectingHost : ICpuHost
    {
        private readonly int _waitsPerAccess;
        public int Accesses;

        public WaitInjectingHost(int waitsPerAccess) => _waitsPerAccess = waitsPerAccess;

        public void OnMemoryAccess(ushort address, Cpu cpu)
        {
            Accesses++;
            cpu.WaitCycles += _waitsPerAccess;
        }

        public void OnPortAccess(ushort address, Cpu cpu) => cpu.WaitCycles += _waitsPerAccess;
    }

    [Fact]
    public void NoWaitStates_NopTakesFourCycles()
    {
        var bus = new StubBus();
        bus.Poke(0x0000, 0x00); // NOP
        var cpu = new Cpu(bus);
        cpu.Reset();

        ulong start = cpu.TotalCycles;
        cpu.Step();

        Assert.Equal(4ul, cpu.TotalCycles - start);
    }

    [Fact]
    public void WaitCycles_ExtendInstructionDuration()
    {
        var bus = new StubBus();
        bus.Poke(0x0000, 0x00); // NOP — one opcode fetch, Tick(4)
        var cpu = new Cpu(bus);
        cpu.Reset();
        cpu.WaitCycles = 3;

        ulong start = cpu.TotalCycles;
        cpu.Step();

        // 4 base T-states plus the 3 injected wait states.
        Assert.Equal(7ul, cpu.TotalCycles - start);
    }

    [Fact]
    public void WaitCycles_AreConsumedExactlyOnce()
    {
        var bus = new StubBus();
        bus.Poke(0x0000, 0x00, 0x00); // NOP, NOP
        var cpu = new Cpu(bus);
        cpu.Reset();
        cpu.WaitCycles = 5;

        ulong start = cpu.TotalCycles;
        cpu.Step();
        Assert.Equal(0, cpu.WaitCycles);
        Assert.Equal(9ul, cpu.TotalCycles - start); // 4 + 5

        // The second instruction must not pay the wait states again.
        start = cpu.TotalCycles;
        cpu.Step();
        Assert.Equal(4ul, cpu.TotalCycles - start);
    }

    [Fact]
    public void HostCanInjectWaitCycles_ViaOnMemoryAccess()
    {
        var bus = new StubBus();
        bus.Poke(0x0000, 0x00); // NOP
        var host = new WaitInjectingHost(waitsPerAccess: 2);
        var cpu = new Cpu(bus, null, host);
        cpu.Reset();

        ulong start = cpu.TotalCycles;
        cpu.Step();

        // One access (the opcode fetch) injecting 2 waits, on a 4 T-state NOP.
        Assert.Equal(1, host.Accesses);
        Assert.Equal(6ul, cpu.TotalCycles - start);
    }

    [Fact]
    public void WaitCycles_FromFinalAccess_AreDeferredToTheNextInstruction()
    {
        // LD (HL),n is encoded as Tick(4); Tick(3); Fetch(); Tick(3); Write().
        // The write is the LAST thing it does, so the wait state that access
        // injects has no remaining Tick to consume it and carries over. Wait
        // cycles are never lost — aggregate timing stays correct — but they are
        // attributed to the following instruction.
        //
        // This is an approximation: real WAIT extends the M-cycle containing the
        // access. It matters only if a host inspects per-instruction timing.
        var bus = new StubBus();
        bus.Poke(0x0000, 0x36, 0xAB, 0x00); // LD (HL),n ; NOP
        var host = new WaitInjectingHost(waitsPerAccess: 1);
        var cpu = new Cpu(bus, null, host);
        cpu.Reset();
        cpu.HL = 0x4000;

        ulong noWaitBaseline;
        {
            var cleanBus = new StubBus();
            cleanBus.Poke(0x0000, 0x36, 0xAB);
            var cleanCpu = new Cpu(cleanBus);
            cleanCpu.Reset();
            cleanCpu.HL = 0x4000;
            ulong s = cleanCpu.TotalCycles;
            cleanCpu.Step();
            noWaitBaseline = cleanCpu.TotalCycles - s;
        }
        Assert.Equal(10ul, noWaitBaseline); // documented LD (HL),n duration

        ulong start = cpu.TotalCycles;
        cpu.Step();
        ulong withWaits = cpu.TotalCycles - start;

        Assert.Equal(3, host.Accesses);          // opcode fetch, operand fetch, write
        Assert.Equal(noWaitBaseline + 2, withWaits); // only two were consumable
        Assert.Equal(1, cpu.WaitCycles);         // the third is still pending
        Assert.Equal(0xAB, bus.Read(0x4000));

        // The deferred wait is paid by the next instruction, so nothing is lost:
        // NOP is 4 T-states, plus 1 carried over, plus 1 for its own fetch.
        start = cpu.TotalCycles;
        cpu.Step();
        Assert.Equal(6ul, cpu.TotalCycles - start);
    }

    [Fact]
    public void WaitCycles_ApplyToBlockInstructions()
    {
        // Regression guard: LDIR's bus traffic must reach the host, so wait
        // states must lengthen it. Before block ops were routed through the
        // hooked Read/Write this stayed at the unwaited duration.
        static ulong RunLdir(int waitsPerAccess, out int accesses)
        {
            var bus = new StubBus();
            bus.Poke(0x0000, 0x21, 0x00, 0x20, 0x11, 0x00, 0x30, 0x01, 0x04, 0x00, 0xED, 0xB0);
            var host = new WaitInjectingHost(waitsPerAccess);
            var cpu = new Cpu(bus, null, host);
            cpu.Reset();

            ulong start = cpu.TotalCycles;
            for (int i = 0; i < 8; i++) cpu.Step();
            accesses = host.Accesses;
            return cpu.TotalCycles - start;
        }

        ulong unwaited = RunLdir(0, out int accesses);
        ulong waited = RunLdir(1, out _);

        Assert.True(accesses > 0);
        Assert.Equal(unwaited + (ulong)accesses, waited);
    }

    [Fact]
    public void WaitCycles_ApplyToStackInstructions()
    {
        // Regression guard for CALL/RET/PUSH/POP going through the hooked path.
        static ulong RunStack(int waitsPerAccess, out int accesses)
        {
            var bus = new StubBus();
            bus.Poke(0x0000, 0xCD, 0x00, 0x01); // CALL 0x0100
            bus.Poke(0x0100, 0xC9);             // RET
            var host = new WaitInjectingHost(waitsPerAccess);
            var cpu = new Cpu(bus, null, host);
            cpu.Reset();
            cpu.SP = 0xFFF0;

            ulong start = cpu.TotalCycles;
            cpu.Step(); // CALL
            cpu.Step(); // RET
            accesses = host.Accesses;
            return cpu.TotalCycles - start;
        }

        ulong unwaited = RunStack(0, out int accesses);
        ulong waited = RunStack(1, out _);

        Assert.True(accesses > 0);
        Assert.Equal(unwaited + (ulong)accesses, waited);
    }

    [Fact]
    public void PortAccess_HonoursInjectedWaitCycles()
    {
        var bus = new StubBus();
        bus.Poke(0x0000, 0xDB, 0xFE); // IN A,(0xFE)
        var host = new WaitInjectingHost(waitsPerAccess: 2);
        var cpu = new Cpu(bus, new StubPorts(), host);
        cpu.Reset();

        ulong start = cpu.TotalCycles;
        cpu.Step();
        ulong waited = cpu.TotalCycles - start;

        var cleanBus = new StubBus();
        cleanBus.Poke(0x0000, 0xDB, 0xFE);
        var cleanCpu = new Cpu(cleanBus, new StubPorts());
        cleanCpu.Reset();
        ulong s = cleanCpu.TotalCycles;
        cleanCpu.Step();

        Assert.True(waited > cleanCpu.TotalCycles - s);
    }

    private sealed class StubPorts : IPortBus
    {
        public byte In(ushort port) => 0xFF;
        public void Out(ushort port, byte value) { }
    }
}
