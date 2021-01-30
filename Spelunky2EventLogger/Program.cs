using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Spelunky2EventLogger
{
    public class Program
    {
        [StructLayout(LayoutKind.Sequential, Pack=0)]
        private struct AutoSplitter
        {
            public ulong magic;
            public ulong uniq;
            public uint counter;
            public byte screen;
            public byte loading;
            public byte trans;
            [MarshalAs(UnmanagedType.U1)]
            public bool ingame;
            [MarshalAs(UnmanagedType.U1)]
            public bool playing;
            [MarshalAs(UnmanagedType.U1)]
            public bool playing2;
            public byte pause;
            [MarshalAs(UnmanagedType.U1)]
            public bool pause2;
            public uint igt;
            public byte world;
            public byte level;
            public byte door;
            public uint characters;
            public uint unlockedCharacterCount;
            public byte shortcuts;
            public uint tries;
            public uint deaths;
            public uint normalWins;
            public uint hardWins;
            public uint specialWins;
            public ulong averageScore;
            public uint topScore;
            public ulong averageTime;
            public uint bestTime;
            public byte bestWorld;
            public byte bestLevel;
            public ulong currentScore;
            [MarshalAs(UnmanagedType.U1)]
            public bool udjatEyeAvailable;
            [MarshalAs(UnmanagedType.U1)]
            public bool seededRun;
        }

        static async Task Main(string[] args)
        {
            Console.WriteLine("Searching for Spelunky 2...");
            var process = await FindProcess("Spel2");

            using var scanner = new MemoryScanner(process);

            Console.WriteLine("Searching for AutoSplitter struct...");
            var address = scanner.FindString("DREGUASL", Encoding.ASCII, 8);

            Console.WriteLine($"Address: 0x{address.ToInt64():x}");

            while (scanner.ReadStructure<AutoSplitter>(address, out var autoSplitter))
            {
                Console.Clear();

                foreach (var fieldInfo in typeof(AutoSplitter).GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    Console.WriteLine($"{fieldInfo.Name}: {fieldInfo.GetValue(autoSplitter)}");
                }

                await Task.Delay(1000);
            }
        }

        static async Task<Process> FindProcess(string name)
        {
            while (true)
            {
                var process = Process.GetProcessesByName(name).FirstOrDefault();

                if (process != null)
                {
                    return process;
                }

                await Task.Delay(500);
            }
        }
    }
}
