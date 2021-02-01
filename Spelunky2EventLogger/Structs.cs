using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Spelunky2EventLogger
{
    [AttributeUsage(AttributeTargets.Field)]
    public class IgnoreChangeAttribute : Attribute { }

    public interface IGameStruct { }

    // From https://github.com/Dregu/LiveSplit-Spelunky2#game-data

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public struct AutoSplitter : IGameStruct
    {
        [JsonIgnore] public ulong magic;
        [JsonIgnore] public ulong uniq;
        [JsonIgnore] public uint counter;
        [JsonIgnore] public byte screen;
        [JsonIgnore] public byte loading;
        [JsonProperty("loading")]
        public bool IsLoading => loading != 0;
        [JsonIgnore] public byte trans;
        [MarshalAs(UnmanagedType.U1)]
        public bool ingame;
        [MarshalAs(UnmanagedType.U1)]
        public bool playing;
        [JsonIgnore, MarshalAs(UnmanagedType.U1)] public bool playing2;
        [JsonIgnore] public byte pause;
        [JsonProperty("pause"), MarshalAs(UnmanagedType.U1)]
        public bool pause2;
        [IgnoreChange]
        public uint igt;
        public byte world;
        public byte level;
        public byte door;
        [JsonIgnore] public uint characters;
        [JsonIgnore] public uint unlockedCharacterCount;
        [JsonIgnore] public byte shortcuts;
        [JsonIgnore] public uint tries;
        [JsonIgnore] public uint deaths;
        [JsonIgnore] public uint normalWins;
        [JsonIgnore] public uint hardWins;
        [JsonIgnore] public uint specialWins;
        [JsonIgnore] public ulong averageScore;
        [JsonIgnore] public uint topScore;
        [JsonIgnore] public ulong averageTime;
        [JsonIgnore] public uint bestTime;
        [JsonIgnore] public byte bestWorld;
        [JsonIgnore] public byte bestLevel;
        public ulong currentScore;
        [MarshalAs(UnmanagedType.U1)]
        public bool udjatEyeAvailable;
        [JsonIgnore, MarshalAs(UnmanagedType.U1)] public bool seededRun;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public unsafe struct Player : IGameStruct
    {
        [JsonIgnore, MarshalAs(UnmanagedType.U1)] public bool used;
        public byte life;
        public byte numBombs;
        public byte numRopes;
        [MarshalAs(UnmanagedType.U1)]
        public bool hasAnkh;
        [MarshalAs(UnmanagedType.U1)]
        public bool hasKapala;
        [MarshalAs(UnmanagedType.U1)]
        public bool isPoisoned;
        [MarshalAs(UnmanagedType.U1)]
        public bool isCursed;
        [JsonIgnore] public fixed byte reserved[128];
    }

    public partial class Program
    {
        public static JObject GetFields<T>(in T value)
            where T : struct, IGameStruct
        {
            return JObject.FromObject(value);
        }

        public static bool HaveFieldsChanged<T>(in T oldValue, in T newValue)
        {
            foreach (var fieldInfo in typeof(T).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (fieldInfo.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
                if (fieldInfo.GetCustomAttribute<IgnoreChangeAttribute>() != null) continue;

                var oldFieldValue = fieldInfo.GetValue(oldValue);
                var newFieldValue = fieldInfo.GetValue(newValue);

                if (!oldFieldValue.Equals(newFieldValue)) return true;
            }

            return false;
        }

        public static JObject GetChangedFields<T>(in T oldValue, in T newValue)
            where T : struct, IGameStruct
        {
            JObject outObj = null;

            foreach (var fieldInfo in typeof(T).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (fieldInfo.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;

                var oldFieldValue = fieldInfo.GetValue(oldValue);
                var newFieldValue = fieldInfo.GetValue(newValue);

                if (oldFieldValue.Equals(newFieldValue)) continue;

                var jsonPropertyAttrib = fieldInfo.GetCustomAttribute<JsonPropertyAttribute>();
                var name = jsonPropertyAttrib?.PropertyName ?? fieldInfo.Name;

                if (outObj == null) outObj = new JObject();
                outObj.Add(name, JToken.FromObject(newFieldValue));
            }

            return outObj;
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
