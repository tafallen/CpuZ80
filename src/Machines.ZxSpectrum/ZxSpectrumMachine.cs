using CpuZ80.Core;
using Machines.Common;
using Machines.Sinclair.Common;

namespace Machines.ZxSpectrum;

/// <summary>
/// Sinclair ZX Spectrum 48K machine compositor.
/// </summary>
public sealed class ZxSpectrumMachine
{
    private const int RomSize = 0x4000; // 16K
    private const int RamSize = 0xC000; // 48K

    public Cpu Cpu { get; }
    public Ram Ram { get; }

    private readonly ZxSpectrumPortBus _ports;

    public ZxSpectrumMachine(byte[] romImage, IPhysicalKeyboard? keyboard = null, IAudioSink? audio = null, ITapeDevice? tape = null)
    {
        if (romImage.Length != RomSize)
            throw new ArgumentException($"ROM must be {RomSize} bytes, got {romImage.Length}.", nameof(romImage));

        var rom = new Rom(romImage);
        Ram = new Ram(RamSize);

        var bus = new AddressDecoder();
        bus.Map(0x0000, 0x3FFF, rom);
        bus.Map(0x4000, 0xFFFF, Ram);

        var kbAdapter = keyboard is not null ? new SinclairKeyboardAdapter(keyboard) : null;
        _ports = new ZxSpectrumPortBus(kbAdapter, tape);

        Cpu = new Cpu(bus, _ports);
    }

    public void Reset()
    {
        Cpu.Reset();
        Cpu.I = 0x3F; // Default font in ROM
    }

    public byte ReadMemory(ushort address) => Cpu.ReadMemory(address);
    public void WriteMemory(ushort address, byte value) => Cpu.WriteMemory(address, value);

    public void Step() => Cpu.Step();
}

internal sealed class ZxSpectrumPortBus : IPortBus
{
    private readonly SinclairKeyboardAdapter? _keyboard;
    private readonly ITapeDevice?             _tape;

    public byte BorderColor { get; private set; }

    public ZxSpectrumPortBus(SinclairKeyboardAdapter? keyboard, ITapeDevice? tape = null)
    {
        _keyboard = keyboard;
        _tape     = tape;
    }

    public byte In(ushort port)
    {
        byte result = 0xFF;
        
        // Keyboard half-row selection via address lines A8-A15
        if ((port & 0x01) == 0)
        {
            result = _keyboard?.Read((byte)(port >> 8)) ?? 0xFF;

            // EAR bit (Tape Input) is on bit 6
            if (_tape is not null)
            {
                if (!_tape.ReadBit()) result &= 0xBF; // EAR bit low
            }
        }

        return result;
    }

    public void Out(ushort port, byte value)
    {
        if ((port & 0x01) == 0)
        {
            BorderColor = (byte)(value & 0x07);
            
            // Speaker (bit 4) and MIC (bit 3)
            if (_tape is not null)
            {
                _tape.WriteBit((value & 0x08) != 0);
            }
        }
    }
}
