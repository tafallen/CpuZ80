namespace Machines.ZxSpectrum;

/// <summary>
/// Frame geometry for a ULA, in T-states.
/// </summary>
/// <param name="CyclesPerLine">T-states in one scanline, including blanking.</param>
/// <param name="FrameCycles">T-states in a complete frame.</param>
/// <param name="ContentionStart">
/// First T-state of the frame at which the ULA contends the bus — the top-left
/// of the drawn area, measured from the frame interrupt.
/// </param>
/// <param name="ContentionEnd">First T-state after the last contended one.</param>
/// <param name="ContentionPattern">
/// Wait states injected across each 8 T-state group of a drawn line. The 48K and
/// 128 share 6,5,4,3,2,1,0,0; the +2A/+3 gate array uses a different sequence
/// rather than an offset of the same one.
/// </param>
/// <param name="ContendsIo">
/// Whether I/O accesses are contended as well as memory. True on the 48K and
/// 128; false on the +2A/+3, whose gate array contends only while MREQ is active.
/// </param>
/// <remarks>
/// The 128K is not a 48K with extra memory: it runs at 3.5469 MHz with 228
/// T-states per line and 311 lines, so every timing constant moves. Holding
/// these as data lets both machines share <see cref="FerrantiUla5C6C"/>.
///
/// The contention *pattern* (6,5,4,3,2,1,0,0 over the first 128 T-states of each
/// drawn line) is identical on both and stays in the ULA.
/// </remarks>
public readonly record struct UlaTiming(
    int CyclesPerLine,
    int FrameCycles,
    int ContentionStart,
    int ContentionEnd,
    byte[] ContentionPattern,
    bool ContendsIo)
{
    /// <summary>The delay sequence shared by the 48K and 128.</summary>
    private static readonly byte[] ClassicPattern = [6, 5, 4, 3, 2, 1, 0, 0];

    /// <summary>The +2A/+3 gate array's sequence — different, not merely shifted.</summary>
    private static readonly byte[] Plus3Pattern = [1, 0, 7, 6, 5, 4, 3, 2];

    /// <summary>ZX Spectrum 48K: 3.5 MHz, 224 T-states/line, 312 lines, 69,888/frame.</summary>
    public static UlaTiming Spectrum48 { get; } = new(
        CyclesPerLine: 224,
        FrameCycles: 69888,
        ContentionStart: 64 * 224,   // 14,336
        ContentionEnd: 256 * 224,    // 57,344
        ContentionPattern: ClassicPattern,
        ContendsIo: true);

    /// <summary>
    /// ZX Spectrum 128 / +2 (grey): 3.5469 MHz, 228 T-states/line, 311 lines,
    /// 70,908/frame. The drawn area starts at T-state 14,361.
    /// </summary>
    public static UlaTiming Spectrum128 { get; } = new(
        CyclesPerLine: 228,
        FrameCycles: 70908,
        ContentionStart: 14361,
        ContentionEnd: 14361 + (192 * 228),
        ContentionPattern: ClassicPattern,
        ContendsIo: true);

    /// <summary>
    /// ZX Spectrum +2A / +2B / +3 / +3B. Same 228 T-state line and 70,908 T-state
    /// frame as the 128, but the drawn area starts at 14,364, the delay sequence
    /// is 1,0,7,6,5,4,3,2, and the gate array contends memory only.
    /// </summary>
    public static UlaTiming Spectrum2A { get; } = new(
        CyclesPerLine: 228,
        FrameCycles: 70908,
        ContentionStart: 14364,
        ContentionEnd: 14364 + (192 * 228),
        ContentionPattern: Plus3Pattern,
        ContendsIo: false);
}
