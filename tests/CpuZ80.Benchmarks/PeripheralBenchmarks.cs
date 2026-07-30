using BenchmarkDotNet.Attributes;
using Machines.ZxSpectrum;

namespace CpuZ80.Benchmarks;

/// <summary>
/// Keyboard and beeper paths, driven through the CPU exactly as a game would
/// drive them.
/// </summary>
/// <remarks>
/// Tracks review findings:
///   * KEYS  — SinclairKeyboardAdapter queries the host once per key per port
///             read. Under the real Raylib adapter each query is a native
///             P/Invoke, so the managed time here understates the true cost;
///             see the "metrics" mode for the query COUNT, which is the number
///             that actually needs to come down.
///   * LOCK  — BeeperDevice.SetLevel takes a lock and appends to a List on every
///             speaker transition, on a path that is single-threaded today.
/// </remarks>
[MemoryDiagnoser]
public class PeripheralBenchmarks
{
    private ZxSpectrumMachine _keyboardMachine = null!;
    private ZxSpectrumMachine _beeperMachine = null!;
    private CountingKeyboard _keyboard = null!;

    [GlobalSetup]
    public void Setup()
    {
        _keyboard = new CountingKeyboard();
        _keyboardMachine = Build(Workloads.KeyboardPoll, _keyboard);
        _beeperMachine   = Build(Workloads.BeeperToggle, null);
    }

    private static ZxSpectrumMachine Build(byte[] program, CountingKeyboard? keyboard)
    {
        var machine = new ZxSpectrumMachine(new byte[0x4000], keyboard: keyboard, audio: new NullAudioSink());
        machine.Reset();
        for (int i = 0; i < program.Length; i++)
            machine.Ram.Write((ushort)(Workloads.Origin - 0x4000 + i), program[i]);
        machine.Cpu.PC = Workloads.Origin;
        machine.RunFrame(); // warm
        return machine;
    }

    /// <summary>A frame spent polling the keyboard in a tight IN loop.</summary>
    [Benchmark(Baseline = true, Description = "Keyboard poll, 1 frame [KEYS]")]
    public void KeyboardPollFrame() => _keyboardMachine.RunFrame();

    /// <summary>A frame spent toggling the speaker, hitting the lock every time.</summary>
    [Benchmark(Description = "Beeper toggle, 1 frame [LOCK]")]
    public void BeeperToggleFrame() => _beeperMachine.RunFrame();

    /// <summary>Host key queries made during the last keyboard-poll frame.</summary>
    public long KeyboardQueries => _keyboard.Queries;
}
