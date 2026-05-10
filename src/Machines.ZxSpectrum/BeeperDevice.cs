using Machines.Common;

namespace Machines.ZxSpectrum;

/// <summary>
/// Emulates the ZX Spectrum's single-bit beeper with high-fidelity anti-aliased resampling.
/// Captures speaker bit transitions and averages intensity over sample periods.
/// </summary>
public sealed class BeeperDevice
{
    private const int SampleRate = 44100;
    private const long CPU_Clock = 3500000;
    
    // Fixed-point scaling factor to prevent drift (2^32)
    private const long Scale = 1L << 32;
    private const long TStatesPerAudioSampleScaled = (CPU_Clock * Scale) / SampleRate;

    private readonly List<(ulong TState, int Level)> _transitions = new(512);
    private int _currentLevel;
    private ulong _frameStartTState;

    // Pre-allocated sample buffer (approx 1 frame + safety margin)
    private short[] _sampleBuffer = new short[2048];

    // Normalized amplitude for signed 16-bit
    // Max level is 10 (Bit 4 = 9, Bit 3 = 1)
    private const short VolumeUnit = 1600; // 1600 * 10 = 16000 peak

    /// <summary>
    /// Notifies the beeper that the speaker bit has changed.
    /// </summary>
    public void SetLevel(ulong tstate, int level)
    {
        if (level == _currentLevel) return;
        _currentLevel = level;
        _transitions.Add((tstate, level));
    }

    /// <summary>
    /// Generates audio samples for the frame ending at <paramref name="endTState"/>.
    /// Uses averaging resampling with fixed-point math for zero-drift synchronization.
    /// </summary>
    public void Render(IAudioSink sink, ulong endTState)
    {
        if (endTState <= _frameStartTState) return;

        long totalTStates = (long)(endTState - _frameStartTState);
        int sampleCount = (int)((totalTStates * Scale) / TStatesPerAudioSampleScaled);
        if (sampleCount == 0) return;
        
        if (_sampleBuffer.Length < sampleCount)
        {
            _sampleBuffer = new short[sampleCount + 256];
        }

        int transitionIdx = 0;
        int currentLevel = _currentLevel;

        long frameStartScaled = (long)_frameStartTState * Scale;

        for (int i = 0; i < sampleCount; i++)
        {
            long windowStartScaled = frameStartScaled + (i * TStatesPerAudioSampleScaled);
            long windowEndScaled = frameStartScaled + ((i + 1) * TStatesPerAudioSampleScaled);
            
            long totalEnergyScaled = 0;
            long cursorScaled = windowStartScaled;

            while (transitionIdx < _transitions.Count && (long)_transitions[transitionIdx].TState * Scale < windowEndScaled)
            {
                long transitionTimeScaled = (long)_transitions[transitionIdx].TState * Scale;
                
                if (transitionTimeScaled > cursorScaled)
                {
                    totalEnergyScaled += (long)currentLevel * (transitionTimeScaled - cursorScaled);
                }

                currentLevel = _transitions[transitionIdx].Level;
                cursorScaled = transitionTimeScaled;
                transitionIdx++;
            }

            if (windowEndScaled > cursorScaled)
            {
                totalEnergyScaled += (long)currentLevel * (windowEndScaled - cursorScaled);
            }

            // Average level = TotalEnergy / WindowWidth
            // totalEnergyScaled is L * T * Scale.
            // Width is T * Scale.
            // Result is L.
            _sampleBuffer[i] = (short)((totalEnergyScaled / TStatesPerAudioSampleScaled) * VolumeUnit);
        }

        sink.SubmitSamples(new ReadOnlySpan<short>(_sampleBuffer, 0, sampleCount), SampleRate);

        _frameStartTState = endTState;
        _transitions.Clear();
        _currentLevel = currentLevel;
    }

    public void Reset(ulong tstate)
    {
        _frameStartTState = tstate;
        _transitions.Clear();
        _currentLevel = 0;
    }
}
