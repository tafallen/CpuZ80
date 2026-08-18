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

    private const byte OpenBus = 0xFF;

    private readonly ZxSpectrumVideo   _video;
    private readonly BeeperDevice      _beeper;
    private readonly SinclairKeyboardAdapter? _keyboard;
    private readonly ITapeDevice?             _tape;
    private readonly IAudioSink?              _audioSink;
    private Ram                               _ram;
    private Cpu? _cpu;

    // Border changes recorded during the frame currently executing. Rendered at
    // the end of that same frame, against that frame's T-state window.
    //
    // This used to be double-buffered and swapped at frame start, which meant
    // RenderFrame replayed the PREVIOUS frame's transitions against the CURRENT
    // frame's window. Every transition then fell before the window and collapsed
    // into the first pixel, so mid-frame border effects rendered as a flat colour.
    //
    // No lock: the host loop runs emulation and rendering on one thread. See the
    // performance review for why threading the renderer is not worth it.
    private readonly List<(ulong TState, byte Color)> _borderTransitions = new(256);

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

    /// <summary>Frame geometry this ULA is running to. Defaults to the 48K.</summary>
    public UlaTiming Timing { get; }

    /// <summary>
    /// Decides whether an address is subject to contention.
    /// </summary>
    /// <remarks>
    /// On a 48K this is purely the address: only 0x4000-0x7FFF is contended. The
    /// 128K supplies its own rule because 0xC000-0xFFFF contends only while an
    /// odd RAM bank is paged there, which the address alone cannot tell you.
    /// </remarks>
    private readonly Func<ushort, bool> _isContended;

    private static bool Contended48K(ushort address) => address >= 0x4000 && address <= 0x7FFF;

    public FerrantiUla5C6C(
        Ram ram,
        SinclairKeyboardAdapter? keyboard = null,
        IAudioSink? audio = null,
        ITapeDevice? tape = null,
        UlaTiming? timing = null,
        Func<ushort, bool>? isContended = null)
    {
        Timing       = timing ?? UlaTiming.Spectrum48;
        _isContended = isContended ?? Contended48K;
        _ram       = ram;
        _video     = new ZxSpectrumVideo(ram);
        _beeper    = new BeeperDevice();
        _keyboard  = keyboard;
        _tape      = tape;
        _audioSink = audio;
    }

    public void ConnectCpu(Cpu cpu) => _cpu = cpu;

    /// <summary>
    /// Points the display and the floating bus at another 16K bank. The 128K
    /// uses this for the shadow screen; the 48K never calls it.
    /// </summary>
    public void SetScreenSource(Ram ram)
    {
        _ram = ram;
        _video.SetSource(ram);
    }

    public void Reset()
    {
        _beeper.Reset(0);
        BorderColor = 0;
        _borderTransitions.Clear();
        _frameStartCycles = 0;
    }

    public void OnFrameStart(ulong tstate)
    {
        _frameStartCycles = tstate;
        _borderTransitions.Clear();
        _beeper.BeginFrame();
        _keyboard?.Invalidate();
    }

    public void RenderFrame(IVideoSink sink, ulong endTState)
    {
        // Flash toggles are handled internally or by counter
        _video.Render(sink, _borderTransitions, BorderColor, false, _frameStartCycles);

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
                    _borderTransitions.Add((_cpu.TotalCycles, newColor));
                }
            }

            bool mic     = (value & MIC_Bit) != 0;
            bool speaker = (value & Speaker_Bit) != 0;
            
            if (_cpu is not null)
            {
                int level = (speaker ? 9 : 0) + (mic ? 1 : 0);
                _beeper.SetLevel(_cpu.TotalCycles, level);
            }

            _tape?.WriteBit(mic, _cpu?.TotalCycles ?? 0);
        }
    }

    // --- ICpuHost implementation ---
    public void OnPortAccess(ushort address, Cpu cpu)
    {
        // The +2A/+3 gate array contends only while MREQ is active, so I/O is
        // not contended there at all.
        if (!Timing.ContendsIo) return;

        if ((address & 0x01) == 0 || (address >= 0x4000 && address <= 0x7FFF))
        {
            ApplyContention(cpu);
        }
    }

    public void OnMemoryAccess(ushort address, Cpu cpu)
    {
        if (_isContended(address))
        {
            ApplyContention(cpu);
        }
    }

    private void ApplyContention(Cpu cpu)
    {
        int t = (int)(cpu.TotalCycles - _frameStartCycles);
        if (t >= Timing.ContentionStart && t < Timing.ContentionEnd)
        {
            int lineCycle = (t - Timing.ContentionStart) % Timing.CyclesPerLine;
            if (lineCycle < 128)
            {
                cpu.WaitCycles += Timing.ContentionPattern[lineCycle % 8];
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
        if (t < Timing.ContentionStart || t >= Timing.ContentionEnd) return OpenBus;

        // Only the first 128 T-states of each line are drawn.
        int lineCycle = (t - Timing.ContentionStart) % Timing.CyclesPerLine;
        if (lineCycle >= 128) return OpenBus;

        // The ULA fetches two bytes every 8 T-states:
        //   0,1: bitmap    2,3: attribute <- visible to the CPU
        //   4,5: bitmap    6,7: attribute <- visible to the CPU
        // Check this before computing an address, so the common case is cheap.
        int subCycle = lineCycle & 7;
        if (subCycle != 2 && subCycle != 3 && subCycle != 6 && subCycle != 7) return OpenBus;

        int charX      = lineCycle / 4;                              // 0..31
        int charRow    = (t - Timing.ContentionStart) / Timing.CyclesPerLine / 8;
        int third      = charRow / 8;
        int rowInThird = charRow & 7;

        // Attribute address: 0x5800 + (third << 8) + (rowInThird << 5) + charX
        int attrOffset = 0x1800 + (third << 8) + (rowInThird << 5) + charX;
        return _ram.Read((ushort)attrOffset);
    }
}
