using CpuZ80.Core;
using Machines.Common;
using Machines.Sinclair.Common;
using System.Runtime.CompilerServices;

namespace Machines.ZxSpectrum;

/// <summary>
/// Represents the Ferranti 5C6C Uncommitted Logic Array (ULA) chip used in the ZX Spectrum.
/// Consolidates video generation, I/O decoding, beeper audio, and memory contention timing.
/// </summary>
public sealed class FerrantiUla5C6C : IPortBus, ICpuHost
{
    private const ushort UlaPortMask = 0x0001;
    private const byte BorderMask   = 0x07;
    private const byte MIC_Bit      = 0x08;
    private const byte Speaker_Bit  = 0x10;
    private const byte EAR_Bit      = 0x40;

    private const int CyclesPerLine = 224;
    private const int VisibleStartLine = 64;
    private const int VisibleEndLine = 255;
    private const int VisibleTStatesStart = VisibleStartLine * CyclesPerLine;
    private const int VisibleTStatesEnd = (VisibleEndLine + 1) * CyclesPerLine;
    private const byte OpenBus = 0xFF;
    private static readonly byte[] ContentionTable = [ 6, 5, 4, 3, 2, 1, 0, 0 ];

    private readonly ZxSpectrumVideo   _video;
    private readonly BeeperDevice      _beeper;
    private readonly SinclairKeyboardAdapter? _keyboard;
    private readonly ITapeDevice?             _tape;
    private readonly IAudioSink?              _audioSink;
    private readonly Ram                      _ram;
    private Cpu? _cpu;

    private readonly object _lock = new();
    private List<(ulong TState, byte Color)> _activeBorder = new(256);
    private List<(ulong TState, byte Color)> _renderBorder = new(256);

    public byte BorderColor { get; private set; }

    /// <summary>
    /// What the ULA currently has on the data bus, as seen by a read of an
    /// unattached port.
    /// </summary>
    /// <remarks>
    /// Sampled on demand from the CPU's current T-state. It used to be
    /// recomputed eagerly on every memory access, which cost a division and two
    /// modulos per access — roughly half of <c>RunFrame</c> — to maintain a
    /// value read only when a port with A0 high is read. Worse, it was sampled
    /// at the wrong moment: by the time an IN executed, the stored value came
    /// from that instruction's operand fetch rather than the I/O cycle, so it
    /// almost always read back as 0xFF.
    /// </remarks>
    public byte FloatingBusValue => ComputeFloatingBus(_cpu?.TotalCycles ?? 0);

    private ulong _frameStartCycles;

    public FerrantiUla5C6C(Ram ram, SinclairKeyboardAdapter? keyboard = null, IAudioSink? audio = null, ITapeDevice? tape = null)
    {
        _ram       = ram;
        _video     = new ZxSpectrumVideo(ram);
        _beeper    = new BeeperDevice();
        _keyboard  = keyboard;
        _tape      = tape;
        _audioSink = audio;
    }

    public void ConnectCpu(Cpu cpu) => _cpu = cpu;

    public void Reset()
    {
        lock (_lock)
        {
            _beeper.Reset(0);
            BorderColor = 0;
            _activeBorder.Clear();
            _renderBorder.Clear();
            _frameStartCycles = 0;
        }
    }

    public void OnFrameStart(ulong tstate)
    {
        lock (_lock)
        {
            _frameStartCycles = tstate;
            var temp = _renderBorder;
            _renderBorder = _activeBorder;
            _activeBorder = temp;
            _activeBorder.Clear();
            _beeper.CommitTransitions();
        }
    }

    public void RenderFrame(IVideoSink sink, ulong endTState)
    {
        // Flash toggles are handled internally or by counter
        _video.Render(sink, _renderBorder, BorderColor, false, _frameStartCycles);

        if (_audioSink is not null)
        {
            _beeper.Render(_audioSink, endTState);
        }
    }

    // --- IPortBus implementation ---
    public byte In(ushort port)
    {
        if ((port & UlaPortMask) == 0)
        {
            byte result = _keyboard?.Read(port) ?? 0xFF;
            if (_tape is not null)
            {
                if (!_tape.ReadBit(_cpu?.TotalCycles ?? 0)) result &= unchecked((byte)~EAR_Bit); 
                else result |= EAR_Bit;
            }
            return result;
        }
        return FloatingBusValue;
    }

    public void Out(ushort port, byte value)
    {
        if ((port & UlaPortMask) == 0)
        {
            byte newColor = (byte)(value & BorderMask);
            if (newColor != BorderColor)
            {
                BorderColor = newColor;
                if (_cpu is not null)
                {
                    lock (_lock) _activeBorder.Add((_cpu.TotalCycles, newColor));
                }
            }

            bool mic     = (value & MIC_Bit) != 0;
            bool speaker = (value & Speaker_Bit) != 0;
            
            if (_cpu is not null)
            {
                int level = (speaker ? 9 : 0) + (mic ? 1 : 0);
                _beeper.SetLevel(_cpu.TotalCycles, level);
            }

            _tape?.WriteBit(mic);
        }
    }

    // --- ICpuHost implementation ---
    public void OnPortAccess(ushort address, Cpu cpu)
    {
        if ((address & 0x01) == 0 || (address >= 0x4000 && address <= 0x7FFF))
        {
            ApplyContention(cpu);
        }
    }

    public void OnMemoryAccess(ushort address, Cpu cpu)
    {
        if (address >= 0x4000 && address <= 0x7FFF)
        {
            ApplyContention(cpu);
        }
    }

    private void ApplyContention(Cpu cpu)
    {
        int t = (int)(cpu.TotalCycles - _frameStartCycles);
        if (t >= VisibleTStatesStart && t < VisibleTStatesEnd)
        {
            int lineCycle = t % CyclesPerLine;
            if (lineCycle < 128)
            {
                cpu.WaitCycles += ContentionTable[lineCycle % 8];
            }
        }
    }

    /// <summary>
    /// The byte the ULA is putting on the bus at <paramref name="totalCycles"/>,
    /// or 0xFF when it is not driving one.
    /// </summary>
    private byte ComputeFloatingBus(ulong totalCycles)
    {
        int t = (int)(totalCycles - _frameStartCycles);

        // Borders and vertical blanking: the ULA is not fetching.
        if (t < VisibleTStatesStart || t >= VisibleTStatesEnd) return OpenBus;

        // Only the first 128 T-states of each line are drawn.
        int lineCycle = t % CyclesPerLine;
        if (lineCycle >= 128) return OpenBus;

        // The ULA fetches two bytes every 8 T-states:
        //   0,1: bitmap    2,3: attribute <- visible to the CPU
        //   4,5: bitmap    6,7: attribute <- visible to the CPU
        // Check this before computing an address, so the common case is cheap.
        int subCycle = lineCycle & 7;
        if (subCycle != 2 && subCycle != 3 && subCycle != 6 && subCycle != 7) return OpenBus;

        int charX      = lineCycle / 4;                              // 0..31
        int charRow    = (t - VisibleTStatesStart) / CyclesPerLine / 8;
        int third      = charRow / 8;
        int rowInThird = charRow & 7;

        // Attribute address: 0x5800 + (third << 8) + (rowInThird << 5) + charX
        int attrOffset = 0x1800 + (third << 8) + (rowInThird << 5) + charX;
        return _ram.Read((ushort)attrOffset);
    }
}
