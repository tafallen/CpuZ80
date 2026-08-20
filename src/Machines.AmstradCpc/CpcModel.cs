namespace Machines.AmstradCpc;

/// <summary>
/// Which CPC this is. The three share a motherboard design and differ in memory,
/// storage and firmware.
/// </summary>
/// <remarks>
/// <code>
///        RAM   Storage          BASIC   Gate Array
///  464    64K  cassette          1.0    40007
///  664    64K  3" disk           1.1    40008
/// 6128   128K  3" disk           1.1    40010
/// </code>
///
/// The Gate Array part numbers differ but the programming model does not, so
/// they are recorded here rather than modelled.
///
/// The differences that matter to the emulation are the ones below: how much
/// RAM there is, whether banking does anything at all, and whether a drive and
/// a cassette are fitted.
/// </remarks>
public enum CpcModel
{
    /// <summary>64K, cassette, BASIC 1.0. No disk and no RAM banking.</summary>
    Cpc464,

    /// <summary>64K, 3" disk, BASIC 1.1. No RAM banking.</summary>
    Cpc664,

    /// <summary>128K, 3" disk, BASIC 1.1.</summary>
    Cpc6128,
}

/// <summary>What a given model actually has.</summary>
public static class CpcModelInfo
{
    /// <summary>128K on a 6128; the others have the base 64K and no expansion PAL.</summary>
    public static bool Has128K(this CpcModel model) => model == CpcModel.Cpc6128;

    /// <summary>
    /// A 664 and a 6128 have a built-in 3" drive; a 464 has none.
    /// </summary>
    /// <remarks>
    /// A 464 could take an external DDI-1, which is a drive plus its own AMSDOS
    /// ROM. That is an expansion rather than part of the machine, so it is
    /// modelled by fitting the drive explicitly rather than by the model.
    /// </remarks>
    public static bool HasDiskDrive(this CpcModel model) => model != CpcModel.Cpc464;

    /// <summary>Only the 464 has a cassette deck built in.</summary>
    public static bool HasCassette(this CpcModel model) => model == CpcModel.Cpc464;

    /// <summary>The name the machine is known by, for window titles and logs.</summary>
    public static string DisplayName(this CpcModel model) => model switch
    {
        CpcModel.Cpc464 => "Amstrad CPC 464",
        CpcModel.Cpc664 => "Amstrad CPC 664",
        _ => "Amstrad CPC 6128",
    };
}
