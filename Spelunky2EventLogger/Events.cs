using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Spelunky2EventLogger
{
    public enum UpdateType
    {
        Keyframe,
        Delta
    }

    [Serializable]
    public struct StateUpdate
    {
        public UpdateType type { get; set; }

        [JsonIgnore]
        public DateTime time { get; set; }

        [JsonPropertyName("time")]
        public long TimeInternal
        {
            get => (long)(time - DateTime.UnixEpoch).TotalMilliseconds;
            set => time = DateTime.UnixEpoch.AddMilliseconds(value);
        }

        public GameState game { get; set; }
        public PlayerState player0 { get; set; }
    }

    public enum StateStruct
    {
        Game,
        Player
    }

    [System.Text.Json.Serialization.JsonConverter(typeof(EventFieldConverter))]
    public struct EventField : IEquatable<EventField>
    {
        private static readonly Regex _regex = new Regex($@"^\s*(?<struct>{string.Join("|", Enum.GetNames(typeof(StateStruct)))})\s*\.\s*(?<field>[a-z_][a-z0-9_]*)\s*$", RegexOptions.IgnoreCase);

        public static bool TryParse(string str, out EventField value)
        {
            var match = _regex.Match(str);

            if (!match.Success)
            {
                value = default;
                return false;
            }

            var stateStruct = Enum.Parse<StateStruct>(match.Groups["struct"].Value, true);
            var field = match.Groups["field"].Value;

            value = new EventField(stateStruct, field);
            return true;
        }

        public readonly StateStruct StateStruct;
        public readonly string Name;

        public EventField(StateStruct stateStruct, string name)
        {
            StateStruct = stateStruct;
            Name = name;
        }

        public bool Equals(EventField other)
        {
            return StateStruct == other.StateStruct && Name == other.Name;
        }

        public override bool Equals(object obj)
        {
            return obj is EventField other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int) StateStruct * 397) ^ (Name == null ? 0 : Name.GetHashCode());
            }
        }

        public override string ToString()
        {
            return $"{StateStruct}.{Name}";
        }
    }

    public class EventFieldConverter : JsonConverter<EventField>
    {
        public override EventField Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException();
            }

            if (!EventField.TryParse(reader.GetString(), out var value))
            {
                throw new JsonException();
            }

            return value;
        }

        public override void Write(Utf8JsonWriter writer, EventField value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }

    public interface IEventState
    {

    }

    [Serializable]
    public class GameState : IEventState
    {
        public static class Fields
        {
            public static readonly EventField world = new EventField(StateStruct.Game, nameof(world));
            public static readonly EventField level = new EventField(StateStruct.Game, nameof(level));
            public static readonly EventField door = new EventField(StateStruct.Game, nameof(door));
            public static readonly EventField currentScore = new EventField(StateStruct.Game, nameof(currentScore));
        }

        public const int MaxPlayers = 4;

        public uint igt { get; set; }
        public bool? loading { get; set; }
        public bool? ingame { get; set; }
        public bool? playing { get; set; }
        public bool? pause { get; set; }
        public byte? world { get; set; }
        public byte? level { get; set; }
        public byte? door { get; set; }
        public ulong? currentScore { get; set; }
        public bool? udjatEyeAvailable { get; set; }

        public void Update(GameState delta)
        {
            if (delta == null) return;

            igt = delta.igt;
            loading = delta.loading ?? loading;
            ingame = delta.ingame ?? ingame;
            playing = delta.playing ?? playing;
            pause = delta.pause ?? pause;
            world = delta.world ?? world;
            level = delta.level ?? level;
            door = delta.door ?? door;
            currentScore = delta.currentScore ?? currentScore;
            udjatEyeAvailable = delta.udjatEyeAvailable ?? udjatEyeAvailable;
        }
    }

    [Serializable]
    public class PlayerState : IEventState
    {
        public static class Fields
        {
            public static readonly EventField life = new EventField(StateStruct.Player, nameof(life));
            public static readonly EventField numBombs = new EventField(StateStruct.Player, nameof(numBombs));
            public static readonly EventField numRopes = new EventField(StateStruct.Player, nameof(numRopes));
            public static readonly EventField hasAnkh = new EventField(StateStruct.Player, nameof(hasAnkh));
            public static readonly EventField hasKapala = new EventField(StateStruct.Player, nameof(hasKapala));
            public static readonly EventField isPoisoned = new EventField(StateStruct.Player, nameof(isPoisoned));
            public static readonly EventField isCursed = new EventField(StateStruct.Player, nameof(isCursed));
        }

        public byte? life { get; set; }
        public byte? numBombs { get; set; }
        public byte? numRopes { get; set; }
        public bool? hasAnkh { get; set; }
        public bool? hasKapala { get; set; }
        public bool? isPoisoned { get; set; }
        public bool? isCursed { get; set; }

        public void Update(PlayerState delta)
        {
            if (delta == null) return;

            life = delta.life ?? life;
            numBombs = delta.numBombs ?? numBombs;
            numRopes = delta.numRopes ?? numRopes;
            hasAnkh = delta.hasAnkh ?? hasAnkh;
            hasKapala = delta.hasKapala ?? hasKapala;
            isPoisoned = delta.isPoisoned ?? isPoisoned;
            isCursed = delta.isCursed ?? isCursed;
        }
    }

    public struct Event
    {
        public readonly EventField Field;
        public readonly byte Player;
        public readonly DateTime Time;
        public readonly long Value;
        public readonly long Delta;

        public Event(EventField field, DateTime time, bool oldValue, bool newValue)
        {
            Field = field;
            Player = 0;
            Time = time;
            Value = newValue ? 1 : 0;
            Delta = (newValue ? 1 : 0) - (oldValue ? 1 : 0);
        }

        public Event(EventField field, DateTime time, byte oldValue, byte newValue)
        {
            Field = field;
            Player = 0;
            Time = time;
            Value = newValue;
            Delta = newValue - oldValue;
        }

        public Event(EventField field, DateTime time, ulong oldValue, ulong newValue)
        {
            Field = field;
            Player = 0;
            Time = time;
            Value = (long) newValue;
            Delta = (long) newValue - (long) oldValue;
        }

        public Event(EventField field, byte player, DateTime time, bool oldValue, bool newValue)
        {
            Field = field;
            Player = player;
            Time = time;
            Value = newValue ? 1 : 0;
            Delta = (newValue ? 1 : 0) - (oldValue ? 1 : 0);
        }

        public Event(EventField field, byte player, DateTime time, byte oldValue, byte newValue)
        {
            Field = field;
            Player = player;
            Time = time;
            Value = newValue;
            Delta = newValue - oldValue;
        }

        public Event(EventField field, byte player, DateTime time, ulong oldValue, ulong newValue)
        {
            Field = field;
            Player = player;
            Time = time;
            Value = (long)newValue;
            Delta = (long)newValue - (long)oldValue;
        }
    }

    public struct TimeRange : IEquatable<TimeRange>
    {
        public static TimeRange Union(TimeRange a, TimeRange b)
        {
            var start = Math.Min(a.Start.Ticks, b.Start.Ticks);
            var end = Math.Max(a.End.Ticks, b.End.Ticks);

            return new TimeRange(new DateTime(start), new TimeSpan(end - start));
        }

        public static TimeRange Intersection(TimeRange a, TimeRange b)
        {
            if (!a.Intersects(b))
            {
                return default;
            }

            var start = Math.Max(a.Start.Ticks, b.Start.Ticks);
            var end = Math.Min(a.End.Ticks, b.End.Ticks);

            return new TimeRange(new DateTime(start), new TimeSpan(end - start));
        }

        public static void TruncateOutsideRange(List<TimeRange> clips, TimeRange range)
        {
            for (var i = clips.Count - 1; i >= 0; --i)
            {
                var clip = clips[i];

                if (!clip.Intersects(range))
                {
                    clips.RemoveAt(i);
                    continue;
                }

                if (clip.Start < range.Start || clip.End > range.End)
                {
                    clips[i] = Intersection(clip, range);
                }
            }
        }

        public static void RemoveIntersections(List<TimeRange> clips, TimeSpan margin = default)
        {
            if (clips.Count < 2) return;

            var next = clips[^1];

            for (var i = clips.Count - 2; i >= 0; --i)
            {
                var clip = clips[i];

                if (clip.Extend(TimeSpan.Zero, margin).Intersects(next))
                {
                    clips[i] = next = Union(clip, next);
                    clips.RemoveAt(i + 1);
                }
                else
                {
                    next = clip;
                }
            }
        }

        public readonly DateTime Start;
        public readonly TimeSpan Duration;

        public DateTime End => Start + Duration;

        public TimeRange(DateTime start, TimeSpan duration)
        {
            Start = start;
            Duration = duration;
        }

        [Pure]
        public TimeRange Extend(TimeSpan margin)
        {
            return Extend(margin, margin);
        }

        [Pure]
        public TimeRange Extend(TimeSpan beforeStart, TimeSpan afterEnd)
        {
            return new TimeRange(Start - beforeStart, Duration + beforeStart + afterEnd);
        }

        [Pure]
        public bool Intersects(TimeRange other)
        {
            return Start < other.End && End > other.Start;
        }

        [Pure]
        public bool Contains(TimeRange other)
        {
            return Start <= other.Start && End >= other.End;
        }

        public bool Equals(TimeRange other)
        {
            return Start.Equals(other.Start) && Duration.Equals(other.Duration);
        }

        public override bool Equals(object obj)
        {
            return obj is TimeRange other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Start.GetHashCode() * 397) ^ Duration.GetHashCode();
            }
        }
    }
}
