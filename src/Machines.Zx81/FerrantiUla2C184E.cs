using CpuZ80.Core;
using Machines.Common;
using Machines.Sinclair.Common;

namespace Machines.Zx81;

/// <summary>
/// Represents the Ferranti 2C184E Uncommitted Logic Array (ULA) chip used in the ZX81.
/// Handles SLOW/FAST mode (NMI generator), I/O port decoding, and beeper audio.
/// </summary>
public sealed class FerrantiUla2C184E : IPortBus, ICpuHost
{
    private readonly SinclairKeyboardAdapter? _keyboard;
    private readonly ITapeDevice?            _tape;
    private readonly BeeperDevice            _beeper;
    private readonly IAudioSink?             _audioSink;
    private Cpu? _cpu;

    public bool NmiEnabled { get; private set; }

    public FerrantiUla2C184E(SinclairKeyboardAdapter? keyboard = null, ITapeDevice? tape = null, IAudioSink? audio = null)
    {
        _keyboard = keyboard;
        _tape     = tape;
        _beeper   = new BeeperDevice();
        _audioSink = audio;
    }

    public void ConnectCpu(Cpu cpu) => _cpu = cpu;

    public void OnPortAccess(ushort address, Cpu cpu)
    {
        byte low = (byte)(address & 0xFF);
        if (low == 0xFD) NmiEnabled = false; 
        if (low == 0xFE) NmiEnabled = true;
    }

    public void OnMemoryAccess(ushort address, Cpu cpu) { }

    public void OnFrameStart(ulong tstate)
    {
        _beeper.CommitTransitions();
    }

    public void RenderFrame(IVideoSink sink, ulong endTState)
    {
        if (_audioSink is not null)
        {
            _beeper.Render(_audioSink, endTState);
        }
    }

    public void Reset()
    {
        _beeper.Reset(0);
    }

    public byte In(ushort port)
    {
        byte result = _keyboard?.Read(port) ?? 0xFF;

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
        bool mic = (value & 0x08) != 0;
        _tape?.WriteBit(mic);
        
        if (_cpu is not null)
            _beeper.SetLevel(_cpu.TotalCycles, mic ? 10 : 0);
    }
}
