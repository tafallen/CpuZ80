namespace Machines.Common;

/// <summary>
/// Raw physical key state from a host input source (window, console, etc.).
/// Implementations are platform-specific; consumers are machine-specific adapters.
/// </summary>
public interface IPhysicalKeyboard
{
    bool IsKeyDown(PhysicalKey key);
}
