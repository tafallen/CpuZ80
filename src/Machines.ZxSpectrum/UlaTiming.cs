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
    int ContentionEnd)
{
    /// <summary>ZX Spectrum 48K: 3.5 MHz, 224 T-states/line, 312 lines, 69,888/frame.</summary>
    public static UlaTiming Spectrum48 { get; } = new(
        CyclesPerLine: 224,
        FrameCycles: 69888,
        ContentionStart: 64 * 224,   // 14,336
        ContentionEnd: 256 * 224);   // 57,344

    /// <summary>
    /// ZX Spectrum 128 / +2 (grey): 3.5469 MHz, 228 T-states/line, 311 lines,
    /// 70,908/frame. The drawn area starts at T-state 14,361.
    /// </summary>
    public static UlaTiming Spectrum128 { get; } = new(
        CyclesPerLine: 228,
        FrameCycles: 70908,
        ContentionStart: 14361,
        ContentionEnd: 14361 + (192 * 228));
}
