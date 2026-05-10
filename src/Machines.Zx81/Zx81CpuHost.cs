using CpuZ80.Core;

namespace Machines.Zx81;

/// <summary>
/// Handles ZX81-specific hardware behavior including NMI generation and memory interception.
/// </summary>
public sealed class Zx81CpuHost : ICpuHost
{
    public bool NmiEnabled { get; set; }

    public void OnPortAccess(ushort address, Cpu cpu)
    {
        byte low = (byte)(address & 0xFF);
        if (low == 0xFD) NmiEnabled = false; // FAST mode: disable NMI generator
        if (low == 0xFE) NmiEnabled = true;  // SLOW mode: enable NMI generator
    }

    public void OnMemoryAccess(ushort address, Cpu cpu)
    {
        // Intercept display generation fetches (M1 cycle with A15=1)
        // This is handled by the ROM's display routine.
        // For now, we only track NMI enablement.
    }
}
