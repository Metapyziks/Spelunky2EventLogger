using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Spelunky2EventLogger
{
    [AttributeUsage(AttributeTargets.Field)]
    public class PrintAttribute : Attribute { }

    public interface IGameStruct { }

    // From https://github.com/Dregu/LiveSplit-Spelunky2#game-data

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public struct AutoSplitter : IGameStruct
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
    public unsafe struct Player : IGameStruct
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

    public partial class Program
    {
        public static void PrintFields<T>(TextWriter writer, in T value, string prefix = "")
            where T : struct, IGameStruct
        {
            foreach (var fieldInfo in typeof(T).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (fieldInfo.GetCustomAttribute<PrintAttribute>() == null) continue;

                writer.WriteLine($"  {prefix}{fieldInfo.Name}: {fieldInfo.GetValue(value)}");
            }
        }

        public static bool GetChangedFields<T>(List<FieldInfo> outChangedFields, in T oldValue, in T newValue)
            where T : struct, IGameStruct
        {
            var anyChanges = false;

            foreach (var fieldInfo in typeof(T).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (fieldInfo.GetCustomAttribute<PrintAttribute>() == null) continue;

                var oldFieldValue = fieldInfo.GetValue(oldValue);
                var newFieldValue = fieldInfo.GetValue(newValue);

                if (oldFieldValue.Equals(newFieldValue)) continue;

                outChangedFields.Add(fieldInfo);
                anyChanges = true;
            }

            return anyChanges;
        }

        public static void PrintChangedFields<T>(TextWriter writer, List<FieldInfo> changedFields, in T value, string prefix = "")
            where T : struct, IGameStruct
        {
            foreach (var fieldInfo in changedFields)
            {
                writer.WriteLine($"  {prefix}{fieldInfo.Name}: {fieldInfo.GetValue(value)}");
            }
        }
    }
}
