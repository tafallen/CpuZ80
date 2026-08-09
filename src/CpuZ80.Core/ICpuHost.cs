namespace CpuZ80.Core;

/// <summary>
/// Defines a host environment for the Z80 CPU.
/// Allows machines to inject custom timing (contention) and monitor bus activity.
/// </summary>
public interface ICpuHost
{
    /// <summary>
    /// Called when the CPU initiates an I/O operation (IN/OUT).
    /// The host may add to cpu.WaitCycles to model hardware contention.
    /// </summary>
    void OnPortAccess(ushort address, Cpu cpu);

    /// <summary>
    /// Called before every M-cycle memory access.
    /// Allows modeling of memory contention (e.g. Spectrum ULA).
    /// </summary>
    void OnMemoryAccess(ushort address, Cpu cpu);

    /// <summary>
    /// Called when the CPU accepts a maskable interrupt.
    /// </summary>
    /// <remarks>
    /// INT is level-triggered, so a host whose interrupt line stays asserted
    /// until acknowledged must clear it here. Doing it in the machine's frame
    /// loop instead leaves a plain <c>Step()</c> loop — which is what a debugger
    /// does — spinning in the interrupt handler forever.
    ///
    /// Defaulted so existing hosts are unaffected: the Sinclair machines drop
    /// INT on a timer instead, which is what their hardware does.
    /// </remarks>
    void OnInterruptAcknowledged(Cpu cpu) { }

    /// <summary>No-op implementation of ICpuHost.</summary>
    public sealed class NullHost : ICpuHost
    {
        public static readonly NullHost Instance = new();
        public void OnPortAccess(ushort address, Cpu cpu) { }
        public void OnMemoryAccess(ushort address, Cpu cpu) { }
    }
}
