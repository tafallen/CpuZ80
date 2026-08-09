using CpuZ80.Core;

namespace Machines.AmstradCpc;

/// <summary>
/// A ROM laid over RAM: reads come from the ROM, writes reach the RAM beneath.
/// </summary>
/// <remarks>
/// This is how CPC ROM paging works, and it is not how any Sinclair machine in
/// this repo works — there, paging in a ROM replaces the RAM entirely and
/// writes are discarded. The CPC firmware keeps its variables in the RAM
/// underneath the lower ROM, so discarding writes would break it immediately.
/// </remarks>
internal sealed class RomOverlay(Rom rom, Ram ram) : IBus
{
    private readonly Rom _rom = rom;
    private readonly Ram _ram = ram;

    public byte Read(ushort address) => _rom.Read(address);

    public void Write(ushort address, byte value) => _ram.Write(address, value);

    /// <summary>True if this overlay already pairs exactly these two devices.</summary>
    public bool Matches(Rom candidateRom, Ram candidateRam) =>
        ReferenceEquals(_rom, candidateRom) && ReferenceEquals(_ram, candidateRam);
}
