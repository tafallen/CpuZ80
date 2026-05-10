using Machines.Common;

namespace Machines.ZxSpectrum;

/// <summary>
/// Emulates the ZX Spectrum's single-bit beeper.
/// Captures speaker bit transitions and resamples them to a high-quality audio stream.
/// </summary>
public sealed class BeeperDevice
{
    private const int SampleRate = 44100;
    private const double CPU_Clock = 3500000.0;
    private const double TStatesPerAudioSample = CPU_Clock / SampleRate;

    private readonly List<(ulong TState, bool Level)> _transitions = new();
    private bool _currentLevel;
    private ulong _frameStartTState;

    // Amplitude for signed 16-bit
    private const short Volume = 12000;

    /// <summary>
    /// Notifies the beeper that the speaker bit has changed.
    /// </summary>
    public void SetLevel(ulong tstate, bool level)
    {
        if (level == _currentLevel) return;
        _currentLevel = level;
        _transitions.Add((tstate, level));
    }

    /// <summary>
    /// Generates audio samples for the frame ending at <paramref name="endTState"/>.
    /// </summary>
    public void Render(IAudioSink sink, ulong endTState)
    {
        if (endTState <= _frameStartTState) return;

        int sampleCount = (int)((endTState - _frameStartTState) / TStatesPerAudioSample);
        if (sampleCount == 0) return;

        short[] samples = new short[sampleCount];
        int transitionIdx = 0;
        
        // The level at the start of the frame is whatever it was at the end of the last frame.
        // We find the state of the speaker *before* any transitions in this frame.
        bool level = _transitions.Count > 0 ? !_transitions[0].Level : _currentLevel;
        // Actually, if we have transitions, the level *before* the first one is the inverse of the first one.
        // If we have NO transitions, it's just _currentLevel.

        for (int i = 0; i < sampleCount; i++)
        {
            // Use double precision for the sample timestamp to avoid drift
            double sampleTState = _frameStartTState + (i * TStatesPerAudioSample);

            // Advance through transitions that happened before or at this sample point
            while (transitionIdx < _transitions.Count && (double)_transitions[transitionIdx].TState <= sampleTState)
            {
                level = _transitions[transitionIdx].Level;
                transitionIdx++;
            }

            samples[i] = level ? Volume : (short)0;
        }

        sink.SubmitSamples(samples, SampleRate);

        // Prepare for next frame
        _frameStartTState = endTState;
        _transitions.Clear();
    }

    public void Reset(ulong tstate)
    {
        _frameStartTState = tstate;
        _transitions.Clear();
        _currentLevel = false;
    }
}
