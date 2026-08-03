using Machines.Common;

namespace Machines.Sinclair.Common;

/// <summary>
/// Maps IPhysicalKeyboard state to Sinclair 8×5 matrix bytes.
/// Used by ZX80, ZX81, and the ZX Spectrum.
/// </summary>
/// <remarks>
/// The host is scanned at most once per frame and the result cached. Programs
/// poll the keyboard by reading port 0xFE in a tight loop — over 15,000 times a
/// frame in a busy input loop — and under the Raylib adapter every key query is
/// a native P/Invoke, so querying per read made the cost scale with how eagerly
/// the guest polled. Caching makes it a fixed 40 queries per frame.
///
/// The scan is deferred to the first read after <see cref="Invalidate"/> rather
/// than done eagerly at frame start, so a machine driven by direct
/// <c>ReadPort</c> calls without running frames still sees current key state.
/// </remarks>
public sealed class SinclairKeyboardAdapter
{
    private readonly IPhysicalKeyboard _keyboard;

    private static readonly PhysicalKey[][] Rows =
    [
        [PhysicalKey.LeftShift, PhysicalKey.Z, PhysicalKey.X, PhysicalKey.C, PhysicalKey.V],
        [PhysicalKey.A, PhysicalKey.S, PhysicalKey.D, PhysicalKey.F, PhysicalKey.G],
        [PhysicalKey.Q, PhysicalKey.W, PhysicalKey.E, PhysicalKey.R, PhysicalKey.T],
        [PhysicalKey.D1, PhysicalKey.D2, PhysicalKey.D3, PhysicalKey.D4, PhysicalKey.D5],
        [PhysicalKey.D0, PhysicalKey.D9, PhysicalKey.D8, PhysicalKey.D7, PhysicalKey.D6],
        [PhysicalKey.P, PhysicalKey.O, PhysicalKey.I, PhysicalKey.U, PhysicalKey.Y],
        [PhysicalKey.Return, PhysicalKey.L, PhysicalKey.K, PhysicalKey.J, PhysicalKey.H],
        [PhysicalKey.Space, PhysicalKey.RightShift, PhysicalKey.M, PhysicalKey.N, PhysicalKey.B],
    ];

    /// <summary>Cached half-row bytes, active low. 0xFF means no key in that row is down.</summary>
    private readonly byte[] _rowStates = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
    private bool _stale = true;

    public SinclairKeyboardAdapter(IPhysicalKeyboard keyboard) => _keyboard = keyboard;

    /// <summary>
    /// Marks the cached matrix as out of date. Machines call this once per frame;
    /// the next read re-scans the host.
    /// </summary>
    public void Invalidate() => _stale = true;

    /// <summary>
    /// Decodes the high byte of the port address to select rows and returns the key state.
    /// Bit 0 of portHigh corresponds to address line A8, Bit 7 to A15.
    /// </summary>
    public byte Read(ushort port)
    {
        if (_stale) Scan();

        byte portHigh = (byte)(port >> 8);
        byte result = 0xFF;
        for (int row = 0; row < 8; row++)
        {
            if ((portHigh & (1 << row)) != 0) continue;
            result &= _rowStates[row];
        }
        return result;
    }

    /// <summary>Reads all 40 keys from the host into <see cref="_rowStates"/>.</summary>
    private void Scan()
    {
        for (int row = 0; row < 8; row++)
        {
            PhysicalKey[] keys = Rows[row];
            byte b = 0xFF;
            for (int bit = 0; bit < keys.Length; bit++)
            {
                if (_keyboard.IsKeyDown(keys[bit]))
                    b &= (byte)~(1 << bit);
            }
            _rowStates[row] = b;
        }
        _stale = false;
    }
}
