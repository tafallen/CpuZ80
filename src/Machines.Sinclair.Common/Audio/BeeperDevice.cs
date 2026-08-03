using Machines.Common;

namespace Machines.Sinclair.Common;

/// <summary>
/// Emulates the ZX Spectrum's single-bit beeper with high-fidelity anti-aliased resampling.
/// Captures speaker bit transitions and averages intensity over sample periods.
/// </summary>
/// <remarks>
/// Transitions are collected during a frame and replayed by <see cref="Render"/>
/// against that same frame's T-state window. The two must describe the same
/// frame: this previously double-buffered the list and rendered the PREVIOUS
/// frame's transitions against the CURRENT frame's window, so every transition
/// fell before the window start, collapsed into the first sample, and a square
/// wave came out as a DC level with one glitch per frame.
///
/// Single-threaded by design — the host loop runs emulation and rendering on one
/// thread, so there are no locks. Threading this would buy under 1% of the frame
/// budget; see the performance review.
/// </remarks>
public sealed class BeeperDevice
{
    private const int SampleRate = 44100;
    private const long CPU_Clock = 3500000;
    
    // Fixed-point scaling factor to prevent drift (2^32)
    private const long Scale = 1L << 32;
    private const long TStatesPerAudioSampleScaled = (CPU_Clock * Scale) / SampleRate;

    private readonly List<(ulong TState, int Level)> _transitions = new(512);

    private int _currentLevel;
    private int _levelAtFrameStart;
    private ulong _frameStartTState;

    // Pre-allocated sample buffer
    private short[] _sampleBuffer = new short[2048];

    private const short VolumeUnit = 1600;

    /// <summary>Notifies the beeper that the speaker bit has changed.</summary>
    public void SetLevel(ulong tstate, int level)
    {
        if (level == _currentLevel) return;
        _currentLevel = level;
        _transitions.Add((tstate, level));
    }

    /// <summary>
    /// Starts a new frame: discards the previous frame's transitions and records
    /// the level carried into this one, which is where <see cref="Render"/> begins.
    /// </summary>
    public void BeginFrame()
    {
        _levelAtFrameStart = _currentLevel;
        _transitions.Clear();
    }

    /// <summary>
    /// Generates audio samples for the snapshot transitions.
    /// Called by the rendering thread.
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
        // Start from the level the frame opened at, not the live level: by the
        // time Render runs, _currentLevel is whatever the frame ended on.
        int currentLevel = _levelAtFrameStart;

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

            _sampleBuffer[i] = (short)((totalEnergyScaled / TStatesPerAudioSampleScaled) * VolumeUnit);
        }

        sink.SubmitSamples(new ReadOnlySpan<short>(_sampleBuffer, 0, sampleCount), SampleRate);

        _frameStartTState = endTState;
    }

    public void Reset(ulong tstate)
    {
        _frameStartTState = tstate;
        _transitions.Clear();
        _currentLevel = 0;
        _levelAtFrameStart = 0;
    }
}
