using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace Spelunky2EventLogger
{
    [AttributeUsage(AttributeTargets.Field)]
    public class IgnoreChangeAttribute : Attribute { }

    public interface IGameStruct { }

    // From https://github.com/Dregu/LiveSplit-Spelunky2#game-data

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public struct AutoSplitter : IGameStruct
    {
        public ulong magic;
        public ulong uniq;
        public uint counter;
        public byte screen;
        public byte _loading;
        public bool loading => _loading != 0;
        public byte trans;
        [MarshalAs(UnmanagedType.U1)]
        public bool ingame;
        [MarshalAs(UnmanagedType.U1)]
        public bool playing;
        [MarshalAs(UnmanagedType.U1)] public bool playing2;
        public byte _pause;
        [MarshalAs(UnmanagedType.U1)]
        public bool pause;
        [IgnoreChange]
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
        [MarshalAs(UnmanagedType.U1)] public bool seededRun;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public unsafe struct Player : IGameStruct
    {
        [MarshalAs(UnmanagedType.U1)] public bool used;
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
        public fixed byte reserved[128];
    }

    public struct WatchedProperty
    {
        public readonly MemberInfo SrcFieldOrProperty;
        public readonly PropertyInfo DstProperty;
        public readonly Type ValueType;

        public WatchedProperty(MemberInfo srcFieldOrProperty, PropertyInfo dstProperty, Type valueType)
        {
            SrcFieldOrProperty = srcFieldOrProperty;
            DstProperty = dstProperty;
            ValueType = valueType;
        }
    }

    public static class WatchedProperties<TGameStruct, TEventState>
        where TGameStruct : struct, IGameStruct
        where TEventState : class, IEventState
    {
        private static WatchedProperty[] Cached;

        private static WatchedProperty[] GetAll()
        {
            if (Cached != null) return Cached;

            var watched = new List<WatchedProperty>();

            foreach (var dstProperty in typeof(TEventState).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var valueType = dstProperty.PropertyType;

                if (valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    valueType = valueType.GenericTypeArguments[0];
                }

                var srcField = typeof(TGameStruct).GetField(dstProperty.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var srcProperty = typeof(TGameStruct).GetProperty(dstProperty.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if ((srcField == null || srcField.FieldType != valueType) && (srcProperty == null || srcProperty.PropertyType != valueType))
                {
                    throw new Exception($"Unable to find matching field \"{dstProperty.Name}\" in {typeof(TGameStruct).FullName}.");
                }

                watched.Add(new WatchedProperty((MemberInfo) srcField ?? srcProperty, dstProperty, valueType));
            }

            return Cached = watched.ToArray();
        }

        public static void CopyAllFields(in TGameStruct gameStruct, TEventState eventState)
        {
            var watched = GetAll();

            foreach (var watchedProperty in watched)
            {
                var srcField = watchedProperty.SrcFieldOrProperty as FieldInfo;
                var srcProperty = watchedProperty.SrcFieldOrProperty as PropertyInfo;

                var value = srcField?.GetValue(gameStruct) ?? srcProperty?.GetValue(gameStruct);
                
                // Will be true if DstField is nullable
                if (watchedProperty.DstProperty.PropertyType != watchedProperty.ValueType)
                {
                    value = Activator.CreateInstance(watchedProperty.DstProperty.PropertyType, value);
                }

                watchedProperty.DstProperty.SetValue(eventState, value);
            }
        }

        public static bool HaveFieldsChanged(in TGameStruct oldGameStruct, in TGameStruct newGameStruct)
        {
            var watched = GetAll();

            foreach (var watchedProperty in watched)
            {
                var srcField = watchedProperty.SrcFieldOrProperty as FieldInfo;
                var srcProperty = watchedProperty.SrcFieldOrProperty as PropertyInfo;

                var oldValue = srcField?.GetValue(oldGameStruct) ?? srcProperty?.GetValue(oldGameStruct);
                var newValue = srcField?.GetValue(newGameStruct) ?? srcProperty?.GetValue(newGameStruct);

                // Only count changes if DstField is nullable
                if (!oldValue.Equals(newValue) && watchedProperty.DstProperty.PropertyType != watchedProperty.ValueType)
                {
                    return true;
                }
            }

            return false;
        }

        public static int CopyChangedFields(in TGameStruct oldGameStruct, in TGameStruct newGameStruct, TEventState eventState)
        {
            var changeCount = 0;
            var watched = GetAll();

            foreach (var watchedProperty in watched)
            {
                var srcField = watchedProperty.SrcFieldOrProperty as FieldInfo;
                var srcProperty = watchedProperty.SrcFieldOrProperty as PropertyInfo;

                var oldValue = srcField?.GetValue(oldGameStruct) ?? srcProperty?.GetValue(oldGameStruct);
                var newValue = srcField?.GetValue(newGameStruct) ?? srcProperty?.GetValue(newGameStruct);
                
                // Will be true if DstField is nullable
                if (watchedProperty.DstProperty.PropertyType != watchedProperty.ValueType)
                {
                    if (oldValue.Equals(newValue))
                    {
                        watchedProperty.DstProperty.SetValue(eventState, null);
                        continue;
                    }

                    // Only count changes if DstField is nullable
                    ++changeCount;
                    newValue = Activator.CreateInstance(watchedProperty.DstProperty.PropertyType, newValue);
                }

                watchedProperty.DstProperty.SetValue(eventState, newValue);
            }

            return changeCount;
        }
    }
}
