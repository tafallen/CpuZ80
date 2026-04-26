using CpuZ80.Core;
using Xunit;
using System.Text;

namespace CpuZ80.Tests;

/// <summary>
/// ZEXALL (Z80 Instruction Exerciser All) functional test suite.
/// This is the industry standard for verifying Z80 correctness.
///
/// Running the full suite takes millions of cycles.
/// 
/// How to run:
/// 1. Download zexall.bin
/// 2. Place it in tests/CpuZ80.Tests/TestData/zexall.bin
/// </summary>
public class IntegrationTests
{
    private const string BinPath = "TestData/zexall.bin";
    private const ushort OrgAddress = 0x0100; // CP/M TPA starts at $100
    private const int MaxSteps = 500_000_000;

    private class CpmBus : IBus
    {
        public byte[] Data = new byte[0x10000];
        public StringBuilder Output = new StringBuilder();

        public byte Read(ushort address) => Data[address];
        public void Write(ushort address, byte value)
        {
            Data[address] = value;

            // CP/M BDOS call hook at address $0005
            if (address == 0x0005)
            {
                // We don't implement the call, but we hook the write to detect it
            }
        }
    }

    [Fact(Skip = "Requires external zexall.bin and takes a long time to run")]
    public void ZexAll_FunctionalTest_RunsToCompletion()
    {
        if (!File.Exists(BinPath)) return;

        byte[] program = File.ReadAllBytes(BinPath);
        var bus = new CpmBus();
        Array.Copy(program, 0, bus.Data, OrgAddress, program.Length);

        // Minimal CP/M environment
        // $0000: RET (to terminate)
        bus.Data[0x0000] = 0xC9; 
        // $0005: Custom hook handler for BDOS calls
        // We'll put a RET there too, and handle the logic in the step loop
        bus.Data[0x0005] = 0xC9;

        var cpu = new Cpu(bus);
        cpu.PC = OrgAddress;

        for (int i = 0; i < MaxSteps; i++)
        {
            // Detect BDOS call (CALL 5)
            if (cpu.PC == 0x0005)
            {
                HandleBdos(cpu, bus);
            }

            cpu.Step();

            if (cpu.PC == 0x0000)
            {
                // Program terminated
                var finalOutput = bus.Output.ToString();
                Assert.DoesNotContain("ERROR", finalOutput);
                return;
            }
        }

        Assert.Fail("Timed out or trapped");
    }

    private void HandleBdos(Cpu cpu, CpmBus bus)
    {
        byte function = cpu.C;
        if (function == 2) // C_WRITE - character in E
        {
            bus.Output.Append((char)cpu.E);
        }
        else if (function == 9) // C_WRITESTR - string at DE terminated by '$'
        {
            ushort addr = cpu.DE;
            while (bus.Data[addr] != '$')
            {
                bus.Output.Append((char)bus.Data[addr++]);
            }
        }
    }
}
