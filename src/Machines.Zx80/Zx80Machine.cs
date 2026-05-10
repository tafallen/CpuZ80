using CpuZ80.Core;
using Machines.Common;
using Machines.Sinclair.Common;

namespace Machines.Zx80;

/// <summary>
/// Sinclair ZX80 machine motherboard.
/// </summary>
public sealed class Zx80Machine
{
    private const int    RomSize        = 0x1000; // 4K
    private const int    RamSize        = 0x0400; // 1K
    private const ulong  CyclesPerFrame = 64167;  // 3,250,000 Hz ÷ 50 Hz

    public Cpu Cpu { get; }
    public Ram Ram { get; }

    private readonly SinclairVideo   _video;
    private readonly FerrantiUla2C158E _ula;

    public Zx80Machine(byte[] romImage, IPhysicalKeyboard? keyboard = null, ITapeDevice? tape = null)
    {
        if (romImage.Length != RomSize)
            throw new ArgumentException($"ROM must be {RomSize} bytes, got {romImage.Length}.", nameof(romImage));

        Ram = new Ram(RamSize);
        var rom = new Rom(romImage);

        var bus = new AddressDecoder();
        bus.Map(0x0000, 0x0FFF, rom);
        bus.Map(0x4000, 0x43FF, Ram);

        var kbAdapter = keyboard is not null ? new SinclairKeyboardAdapter(keyboard) : null;
        _ula = new FerrantiUla2C158E(kbAdapter, tape);

        Cpu = new Cpu(bus, _ula, _ula);
        _video = new SinclairVideo(rom, Ram, 0x0E00);
    }

    public void Reset()
    {
        Cpu.Reset();
        Cpu.I = 0x0E;
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

    private ulong _nextFrameTarget;

    public void Step() => Cpu.Step();

    public void RunFrame()
    {
        _nextFrameTarget += CyclesPerFrame;
        
        if (Cpu.TotalCycles > _nextFrameTarget + CyclesPerFrame)
            _nextFrameTarget = Cpu.TotalCycles + CyclesPerFrame;

        while (Cpu.TotalCycles < _nextFrameTarget)
            Cpu.Step();
    }

    public void RenderFrame(IVideoSink sink) => _video.Render(sink);
}
