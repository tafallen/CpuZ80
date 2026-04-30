namespace Machines.Common;

/// <summary>
/// Receives audio samples from a chip emulation.
/// Implementations pass samples to the OS audio API.
/// Samples are signed 16-bit mono at <paramref name="sampleRate"/> Hz.
/// </summary>
public interface IAudioSink
{
    void SubmitSamples(ReadOnlySpan<short> samples, int sampleRate);
}
