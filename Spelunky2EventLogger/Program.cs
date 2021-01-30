using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Spelunky2EventLogger
{
    public class Program
    {
        [AttributeUsage(AttributeTargets.Field)]
        private class PrintAttribute : Attribute { }

        /// <remarks>
        /// From https://github.com/Dregu/LiveSplit-Spelunky2#game-data
        /// </remarks>
        [StructLayout(LayoutKind.Sequential, Pack=0)]
        private struct AutoSplitter
        {
            public ulong magic;
            public ulong uniq;
            public uint counter;
            public byte screen;
            [Print]
            public byte loading;
            public byte trans;
            [Print, MarshalAs(UnmanagedType.U1)]
            public bool ingame;
            [Print, MarshalAs(UnmanagedType.U1)]
            public bool playing;
            [MarshalAs(UnmanagedType.U1)]
            public bool playing2;
            [Print]
            public byte pause;
            [MarshalAs(UnmanagedType.U1)]
            public bool pause2;
            [Print]
            public uint igt;
            [Print]
            public byte world;
            [Print]
            public byte level;
            [Print]
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
            [Print]
            public ulong currentScore;
            [Print, MarshalAs(UnmanagedType.U1)]
            public bool udjatEyeAvailable;
            [MarshalAs(UnmanagedType.U1)]
            public bool seededRun;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 0)]
        private unsafe struct Player
        {
            [MarshalAs(UnmanagedType.U1)]
            public bool used;
            [Print]
            public byte life;
            [Print]
            public byte numBombs;
            [Print]
            public byte numRopes;
            [Print, MarshalAs(UnmanagedType.U1)]
            public bool hasAnkh;
            [Print, MarshalAs(UnmanagedType.U1)]
            public bool hasKapala;
            [Print, MarshalAs(UnmanagedType.U1)]
            public bool isPoisoned;
            [Print, MarshalAs(UnmanagedType.U1)]
            public bool isCursed;
            public fixed byte reserved[128];
        }

        private static async Task<int> Main(string[] args)
        {
            Console.WriteLine("Searching for Spelunky 2...");
            var process = await FindProcess("Spel2");

            using var scanner = new MemoryScanner(process);

            Console.WriteLine("Searching for AutoSplitter struct...");

            var autoSplitterAddress = scanner.FindString("DREGUASL", Encoding.ASCII, 8).FirstOrDefault();
            Console.WriteLine($"AutoSplitter address: 0x{autoSplitterAddress.ToInt64():x}");

            Console.WriteLine("Searching for Player struct...");

            var playerAddress = scanner.FindUInt32(0xfeedc0de, 4)
                .Select(x => x - 0x2D8)
                .FirstOrDefault(x => x.ToInt64() > autoSplitterAddress.ToInt64());
            Console.WriteLine($"Player address: 0x{playerAddress.ToInt64():x}");

            var writer = new StringWriter();
            var sb = writer.GetStringBuilder();

            while (scanner.ReadStructure<AutoSplitter>(autoSplitterAddress, out var autoSplitter) && scanner.ReadStructure<Player>(playerAddress, out var player))
            {
                Console.Clear();
                sb.Remove(0, sb.Length);

                PrintStruct(writer, autoSplitter);
                PrintStruct(writer, player);

                Console.WriteLine(writer);

                await Task.Delay(500);
            }

            return 0;
        }

        private static void PrintStruct<T>(TextWriter writer, in T value)
            where T : struct
        {
            foreach (var fieldInfo in typeof(T).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (fieldInfo.GetCustomAttribute<PrintAttribute>() == null) continue;

                writer.WriteLine($"{fieldInfo.Name}: {fieldInfo.GetValue(value)}");
            }
        }

        private static async Task<Process> FindProcess(string name)
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
