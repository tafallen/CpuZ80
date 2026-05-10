using CpuZ80.Core;
using Machines.Common;
using Machines.Zx80;
using Machines.Sinclair.Common;

namespace Machines.Zx81;

/// <summary>
/// Sinclair ZX81 machine compositor.
/// </summary>
public sealed class Zx81Machine
{
    private const int    RomSize        = 0x2000; // 8K
    private const int    RamSize        = 0x0400; // 1K
    private const ulong  CyclesPerFrame = 65000;  // 3.25 MHz clock
    private const int    CyclesPerScanline = 207;

    public Cpu Cpu { get; }
    public Ram Ram { get; }
    public Zx81CpuHost Host { get; }

    private readonly SinclairVideo _video;
    private readonly Zx81PortBus   _ports;

    /// <param name="romImage">8K ROM image. Must be exactly 8192 bytes.</param>
    /// <param name="keyboard">Physical keyboard source.</param>
    /// <param name="tape">Tape device.</param>
    public Zx81Machine(byte[] romImage, IPhysicalKeyboard? keyboard = null, ITapeDevice? tape = null)
    {
        if (romImage.Length != RomSize)
            throw new ArgumentException($"ROM must be {RomSize} bytes, got {romImage.Length}.", nameof(romImage));

        Ram = new Ram(RamSize);
        var rom = new Rom(romImage);

        var bus = new AddressDecoder();
        bus.Map(0x0000, 0x1FFF, rom);
        bus.Map(0x2000, 0x3FFF, rom); 
        
        bus.Map(0x4000, 0x43FF, Ram);
        for (ushort addr = 0x4400; addr < 0x8000; addr += 0x0400)
        {
            bus.Map(addr, (ushort)(addr + 0x03FF), Ram);
        }

        var kbAdapter = keyboard is not null ? new SinclairKeyboardAdapter(keyboard) : null;
        _ports = new Zx81PortBus(kbAdapter, tape);
        Host = new Zx81CpuHost();

        Cpu = new Cpu(bus, _ports, Host);
        _video = new SinclairVideo(rom, Ram, 0x1E00);
    }

    public void Reset()
    {
        Cpu.Reset();
        Cpu.I = 0x1E; 
        _nextNmiCycles = CyclesPerScanline;
    }

    public byte ReadMemory(ushort address) => Cpu.ReadMemory(address);
    public void WriteMemory(ushort address, byte value) => Cpu.WriteMemory(address, value);

    public byte ReadPort(ushort address) => _ports.In(address);
    public void WritePort(ushort address, byte value)
    {
        Host.OnPortAccess(address, Cpu);
        _ports.Out(address, value);
    }

    private ulong _nextNmiCycles;

    public void Step()
    {
        Cpu.Step();

        if (Host.NmiEnabled && Cpu.TotalCycles >= _nextNmiCycles)
        {
            Cpu.TriggerNmi();
            _nextNmiCycles += CyclesPerScanline;
        }
    }

    public void RunFrame()
    {
        ulong target = Cpu.TotalCycles + CyclesPerFrame;
        while (Cpu.TotalCycles < target)
        {
            Step();
        }
    }

    public void RenderFrame(IVideoSink sink) => _video.Render(sink);
}

internal sealed class Zx81PortBus : IPortBus
{
    private readonly SinclairKeyboardAdapter? _keyboard;
    private readonly ITapeDevice?            _tape;

    public Zx81PortBus(SinclairKeyboardAdapter? keyboard, ITapeDevice? tape = null)
    {
        _keyboard = keyboard;
        _tape     = tape;
    }

    public byte In(ushort port)
    {
        byte result = _keyboard?.Read((byte)(port >> 8)) ?? 0xFF;

        if (_tape is not null)
        {
            bool pulse = !_tape.ReadBit();
            if (pulse) result &= 0xBF;
            else result |= 0x40;
        }

        return result;
    }

    public void Out(ushort port, byte value)
    {
        _tape?.WriteBit((value & 0x08) != 0);
    }
}
