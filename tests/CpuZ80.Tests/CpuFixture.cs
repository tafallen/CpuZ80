using CpuZ80.Core;

namespace CpuZ80.Tests;

public abstract class CpuFixture
{
    protected Ram Ram { get; }
    protected Cpu Cpu { get; }

    protected CpuFixture()
    {
        Ram = new Ram(0x10000);
        Cpu = new Cpu(Ram);
    }

    protected void Load(ushort address, params byte[] data)
    {
        Ram.Load(address, data);
        Cpu.PC = address;
    }

    protected void Step(int count = 1)
    {
        for (int i = 0; i < count; i++)
            Cpu.Step();
    }
}
