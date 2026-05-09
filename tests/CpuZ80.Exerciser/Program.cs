using CpuZ80.Core;
using System.Text;

namespace CpuZ80.Exerciser;

public class Program
{
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

    public static void Main(string[] args)
    {
        string actualPath = args.Length > 0 ? args[0] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, BinPath);
        
        if (!File.Exists(actualPath))
        {
             actualPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "zexall.bin");
        }

        if (!File.Exists(actualPath))
        {
            Console.WriteLine($"Error: ZEXALL binary not found. Tried: {actualPath}");
            Console.WriteLine($"Base Directory: {AppDomain.CurrentDomain.BaseDirectory}");
            return;
        }

        Console.WriteLine($"Loading {actualPath}...");
        byte[] program = File.ReadAllBytes(actualPath);
        var bus = new CpmBus();
        Array.Copy(program, 0, bus.Data, OrgAddress, program.Length);

        // Minimal CP/M environment
        bus.Data[0x0000] = 0xC9; // RET at 0
        bus.Data[0x0005] = 0xC9; // RET at 5

        var cpu = new Cpu(bus);
        cpu.PC = OrgAddress;

        int lastOutputLen = 0;
        Console.WriteLine("Starting ZEXALL functional test...");
        
        for (long i = 0; i < MaxSteps; i++)
        {
            if (cpu.PC == 0x0005) HandleBdos(cpu, bus);

            if (i % 50_000_000 == 0) 
            {
                Console.WriteLine($"Step {i / 1_000_000}M, PC: {cpu.PC:X4}");
            }

            cpu.Step();

            if (bus.Output.Length > lastOutputLen)
            {
                var newContent = bus.Output.ToString().Substring(lastOutputLen);
                Console.Write(newContent);
                lastOutputLen = bus.Output.Length;
            }

            if (cpu.PC == 0x0000)
            {
                Console.WriteLine("\nZEXALL completed.");
                var finalOutput = bus.Output.ToString();
                if (finalOutput.Contains("ERROR"))
                {
                    Console.WriteLine("FAILURES DETECTED!");
                    Environment.Exit(1);
                }
                else
                {
                    Console.WriteLine("SUCCESS!");
                    Environment.Exit(0);
                }
                return;
            }
        }

        Console.WriteLine($"\nTimed out or trapped. Output: {bus.Output.ToString()}");
        Environment.Exit(1);
    }

    private static void HandleBdos(Cpu cpu, CpmBus bus)
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
