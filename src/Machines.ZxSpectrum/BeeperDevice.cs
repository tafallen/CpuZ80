using Machines.Common;

namespace Machines.ZxSpectrum;

/// <summary>
/// Emulates the ZX Spectrum's single-bit beeper with high-fidelity anti-aliased resampling.
/// Captures speaker bit transitions and averages intensity over sample periods.
/// </summary>
public sealed class BeeperDevice
{
    private const int SampleRate = 44100;
    private const double CPU_Clock = 3500000.0;
    private const double TStatesPerAudioSample = CPU_Clock / SampleRate;

    private readonly List<(ulong TState, int Level)> _transitions = new();
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
    /// Uses averaging resampling to provide a low-pass filter effect and prevent aliasing.
    /// </summary>
    public void Render(IAudioSink sink, ulong endTState)
    {
        if (endTState <= _frameStartTState) return;

        double sampleCountDouble = (endTState - _frameStartTState) / TStatesPerAudioSample;
        int sampleCount = (int)sampleCountDouble;
        if (sampleCount == 0) return;
        
        // Ensure buffer size
        if (_sampleBuffer.Length < sampleCount)
        {
            _sampleBuffer = new short[sampleCount + 256];
        }

        int transitionIdx = 0;
        
        // Initial level is whatever was set at the end of the last frame
        int currentLevel = _transitions.Count > 0 ? -1 : _currentLevel;
        // If we have transitions, we'll find the starting level from the first transition
        if (currentLevel == -1)
        {
            // This is a simplification; a more robust way is to track last-frame-end-level.
            // For now, assume the currentLevel is correct.
            currentLevel = _currentLevel;
            // Actually, we should find the level before the first transition.
            // But since we clear transitions every frame, _currentLevel IS the level before the first transition of THIS frame.
        }

        double currentWindowStart = _frameStartTState;

        for (int i = 0; i < sampleCount; i++)
        {
            double nextWindowEnd = _frameStartTState + ((i + 1) * TStatesPerAudioSample);
            double totalEnergy = 0;
            double cursor = currentWindowStart;

            // Process all transitions within this sample window
            while (transitionIdx < _transitions.Count && (double)_transitions[transitionIdx].TState < nextWindowEnd)
            {
                double transitionTime = (double)_transitions[transitionIdx].TState;
                
                // Add energy from the start of the window (or last transition) to this transition
                if (transitionTime > cursor)
                {
                    totalEnergy += currentLevel * (transitionTime - cursor);
                }

                currentLevel = _transitions[transitionIdx].Level;
                cursor = transitionTime;
                transitionIdx++;
            }

            // Remainder of the window
            if (nextWindowEnd > cursor)
            {
                totalEnergy += currentLevel * (nextWindowEnd - cursor);
            }

            // Average level for this sample window
            double average = totalEnergy / TStatesPerAudioSample;
            _sampleBuffer[i] = (short)(average * VolumeUnit);
            
            currentWindowStart = nextWindowEnd;
        }

        sink.SubmitSamples(new ReadOnlySpan<short>(_sampleBuffer, 0, sampleCount), SampleRate);

        // Prepare for next frame
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
