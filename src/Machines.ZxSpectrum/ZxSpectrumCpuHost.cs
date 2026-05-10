using CpuZ80.Core;
using Machines.Common;
using Machines.Sinclair.Common;

namespace Machines.ZxSpectrum;

/// <summary>
/// Handles ZX Spectrum specific hardware behavior including ULA contention and floating bus.
/// </summary>
public sealed class ZxSpectrumCpuHost : ICpuHost
{
    private const int CyclesPerLine = 224;
    private const int VisibleStartLine = 64;
    private const int VisibleEndLine = 255; // 192 lines: 64 to 255 inclusive
    private const int VisibleTStatesStart = VisibleStartLine * CyclesPerLine;
    private const int VisibleTStatesEnd = (VisibleEndLine + 1) * CyclesPerLine;

    // Standard 48K contention table for the 128-cycle visible portion of a line.
    // Pattern: 6, 5, 4, 3, 2, 1, 0, 0 (repeats every 8 cycles)
    private static readonly byte[] ContentionTable = [ 6, 5, 4, 3, 2, 1, 0, 0 ];

    public ulong FrameStartCycles { get; set; }
    public byte CurrentFloatingBusValue { get; set; } = 0xFF;

    public void OnPortAccess(ushort address, Cpu cpu)
    {
        // 1. Contention: If Port A0 is low (ULA), it is ALWAYS contended if the ULA is busy.
        // Also, if the address is in the contended RAM range ($4000-$7FFF), it follows RAM rules.
        bool isUlaPort = (address & 0x01) == 0;
        bool isContendedAddress = address >= 0x4000 && address <= 0x7FFF;

        if (isUlaPort || isContendedAddress)
        {
            ApplyContention(cpu);
        }
    }

    public void OnMemoryAccess(ushort address, Cpu cpu)
    {
        // 2. Contention: Access to the first 16K bank of RAM ($4000-$7FFF) is contended by the ULA.
        if (address >= 0x4000 && address <= 0x7FFF)
        {
            ApplyContention(cpu);
        }

        // 3. Floating Bus: Update the value based on ULA fetches.
        // This is a simplified model; real floating bus changes mid-instruction.
        // We calculate it here so that the next IN instruction sees it.
        UpdateFloatingBus(cpu);
    }

    private void ApplyContention(Cpu cpu)
    {
        int t = (int)(cpu.TotalCycles - FrameStartCycles);
        
        // Is the ULA currently rendering a visible line?
        if (t >= VisibleTStatesStart && t < VisibleTStatesEnd)
        {
            int lineCycle = t % CyclesPerLine;
            
            // Is the ULA in the 128-cycle visible pixel area?
            if (lineCycle < 128)
            {
                cpu.WaitCycles += ContentionTable[lineCycle % 8];
            }
        }
    }

    private void UpdateFloatingBus(Cpu cpu)
    {
        int t = (int)(cpu.TotalCycles - FrameStartCycles);
        
        // If not in visible area, bus is "quiet" (usually 0xFF)
        if (t < VisibleTStatesStart || t >= VisibleTStatesEnd)
        {
            CurrentFloatingBusValue = 0xFF;
            return;
        }

        int lineCycle = t % CyclesPerLine;
        if (lineCycle >= 128)
        {
            CurrentFloatingBusValue = 0xFF;
            return;
        }

        // The ULA fetches attributes in pairs.
        // Cycles 0,1: Bitmap
        // Cycles 2,3: Attribute <-- This is what the floating bus returns
        // Cycles 4,5: Bitmap
        // Cycles 6,7: Attribute
        
        // For simplicity, we sample the attribute of the current ULA position.
        // In US-405, we just need to ensure ZxSpectrumPortBus.FloatingBusValue is synced.
    }
}
