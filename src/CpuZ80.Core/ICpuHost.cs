namespace CpuZ80.Core;

/// <summary>
/// Defines a host environment for the Z80 CPU.
/// Allows machines to inject custom timing (contention) and monitor bus activity.
/// </summary>
public interface ICpuHost
{
    /// <summary>
    /// Called when the CPU initiates an I/O operation (IN/OUT).
    /// The host may update cpu.WaitCycles or cpu.WaitPin to model hardware contention.
    /// </summary>
    void OnPortAccess(ushort address, Cpu cpu);

    /// <summary>
    /// Called before every M-cycle memory access.
    /// Allows modeling of memory contention (e.g. Spectrum ULA).
    /// </summary>
    void OnMemoryAccess(ushort address, Cpu cpu);
}
