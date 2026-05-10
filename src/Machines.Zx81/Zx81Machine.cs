using CpuZ80.Core;
using Machines.Common;
using Machines.Zx80;
using Machines.Sinclair.Common;

namespace Machines.Zx81;

/// <summary>
/// Sinclair ZX81 machine motherboard.
/// </summary>
public sealed class Zx81Machine
{
    private const int    RomSize        = 0x2000; // 8K
    private const int    RamSize        = 0x0400; // 1K
    private const ulong  CyclesPerFrame = 65000;  // 3.25 MHz clock
    private const int    CyclesPerScanline = 207;

    public Cpu Cpu { get; }
    public Ram Ram { get; }

    private readonly SinclairVideo   _video;
    private readonly FerrantiUla2C184E _ula;

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
        _ula = new FerrantiUla2C184E(kbAdapter, tape);

        Cpu = new Cpu(bus, _ula, _ula);
        _video = new SinclairVideo(rom, Ram, 0x1E00);
    }

    public void Reset()
    {
        Cpu.Reset();
        Cpu.I = 0x1E; 
        _nextNmiCycles = CyclesPerScanline;
        _nextFrameTarget = 0;
    }

    public byte ReadMemory(ushort address) => Cpu.ReadMemory(address);
    public void WriteMemory(ushort address, byte value) => Cpu.WriteMemory(address, value);

    public byte ReadPort(ushort address) => _ula.In(address);
    public void WritePort(ushort address, byte value)
    {
        _ula.OnPortAccess(address, Cpu);
        _ula.Out(address, value);
    }

    private ulong _nextNmiCycles;
    private ulong _nextFrameTarget;

    public void Step()
    {
        Cpu.Step();

        if (_ula.NmiEnabled && Cpu.TotalCycles >= _nextNmiCycles)
        {
            Cpu.TriggerNmi();
            _nextNmiCycles += CyclesPerScanline;
        }
    }

    public void RunFrame()
    {
        _nextFrameTarget += CyclesPerFrame;

        if (Cpu.TotalCycles > _nextFrameTarget + CyclesPerFrame)
            _nextFrameTarget = Cpu.TotalCycles + CyclesPerFrame;

        while (Cpu.TotalCycles < _nextFrameTarget)
        {
            Step();
        }
    }

    public void RenderFrame(IVideoSink sink) => _video.Render(sink);

    public void LoadSnapshot(Stream data)
    {
        byte[] buffer = new byte[Ram.RawBytes.Length];
        int read = data.Read(buffer, 0, buffer.Length);
        Ram.Load(0, buffer.Take(read).ToArray());
    }
}
