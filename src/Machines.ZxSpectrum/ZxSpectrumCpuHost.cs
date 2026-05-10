using CpuZ80.Core;
using Machines.Common;
using Machines.Sinclair.Common;

namespace Machines.ZxSpectrum;

/// <summary>
/// Handles ZX Spectrum specific hardware behavior including ULA contention.
/// </summary>
public sealed class ZxSpectrumCpuHost : ICpuHost
{
    public void OnPortAccess(ushort address, Cpu cpu)
    {
        // Contention logic will be added here in US-405
    }

    public void OnMemoryAccess(ushort address, Cpu cpu)
    {
        // Contention logic will be added here in US-405
    }
}
