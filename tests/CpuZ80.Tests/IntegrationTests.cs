using CpuZ80.Core;
using Xunit;
using Xunit.Abstractions;
using System.Text;

namespace CpuZ80.Tests;

public class IntegrationTests
{
    private readonly ITestOutputHelper _output;
    public IntegrationTests(ITestOutputHelper output) => _output = output;

    private const string BinPath = "TestData/zexall.bin";
    private const ushort OrgAddress = 0x0100; 
    private const long MaxSteps = 10_000_000_000;

    private class CpmBus : IBus
    {
        public byte[] Data = new byte[0x10000];
        public StringBuilder Output = new StringBuilder();

        public byte Read(ushort address) => Data[address];
        public void Write(ushort address, byte value)
        {
            Data[address] = value;
        }
    }

    [Fact]
    public void ZexAll_FunctionalTest_RunsToCompletion()
    {
        string actualPath = BinPath;
        if (!File.Exists(actualPath))
        {
            actualPath = Path.Combine("..", "..", "..", BinPath);
        }

        if (!File.Exists(actualPath))
        {
            Assert.Fail($"ZEXALL binary not found at {Path.GetFullPath(actualPath)}");
        }

        byte[] program = File.ReadAllBytes(actualPath);
        var bus = new CpmBus();
        Array.Copy(program, 0, bus.Data, OrgAddress, program.Length);

        // Minimal CP/M environment
        bus.Data[0x0000] = 0xC9; // RET at 0
        bus.Data[0x0005] = 0xC9; // RET at 5

        var cpu = new Cpu(bus);
        cpu.PC = OrgAddress;

        int lastOutputLen = 0;
        for (long i = 0; i < MaxSteps; i++)
        {
            if (cpu.PC == 0x0005) HandleBdos(cpu, bus);

            if (i % 50_000_000 == 0) _output.WriteLine($"Step {i}, PC: {cpu.PC:X4}");

            cpu.Step();

            if (bus.Output.Length > lastOutputLen)
            {
                var newContent = bus.Output.ToString().Substring(lastOutputLen);
                _output.WriteLine(newContent);
                lastOutputLen = bus.Output.Length;
            }

            if (cpu.PC == 0x0000)
            {
                var finalOutput = bus.Output.ToString();
                Assert.DoesNotContain("ERROR", finalOutput);
                return;
            }
        }

        Assert.Fail($"Timed out or trapped. Output: {bus.Output.ToString()}");
    }

    private void HandleBdos(Cpu cpu, CpmBus bus)
    {
        byte function = cpu.C;
        if (function == 2) // C_WRITE
        {
            bus.Output.Append((char)cpu.E);
        }
        else if (function == 9) // C_WRITESTR
        {
            ushort addr = cpu.DE;
            while (bus.Data[addr] != '$')
            {
                bus.Output.Append((char)bus.Data[addr++]);
            }
        }
        else if (function == 0) // P_TERMCPM
        {
            cpu.PC = 0x0000;
        }
    }
}
