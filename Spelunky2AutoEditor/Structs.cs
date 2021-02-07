using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Spelunky2AutoEditor
{
    public enum UpdateType
    {
        Keyframe,
        Delta
    }

    public struct StateUpdate
    {
        public UpdateType type;

        [JsonIgnore]
        public DateTime time;

        [JsonProperty("time")]
        private long TimeInternal
        {
            get => (long)(time - DateTime.UnixEpoch).TotalMilliseconds;
            set => time = DateTime.UnixEpoch.AddMilliseconds(value);
        }

        public GameState game;
        public PlayerState player0;
    }

    public struct GameState
    {
        public const int MaxPlayers = 4;

        public uint? igt;
        public bool? loading;
        public bool? ingame;
        public bool? playing;
        public bool? pause;
        public byte? world;
        public byte? level;
        public byte? door;
        public ulong? currentScore;
        public bool? udjatEyeAvailable;

        public void Update(in GameState delta)
        {
            igt = delta.igt ?? igt;
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

    public struct PlayerState
    {
        public byte? life;
        public byte? numBombs;
        public byte? numRopes;
        public bool? hasAnkh;
        public bool? hasKapala;
        public bool? isPoisoned;
        public bool? isCursed;

        public void Update(in PlayerState delta)
        {
            life = delta.life ?? life;
            numBombs = delta.numBombs ?? numBombs;
            numRopes = delta.numRopes ?? numRopes;
            hasAnkh = delta.hasAnkh ?? hasAnkh;
            hasKapala = delta.hasKapala ?? hasKapala;
            isPoisoned = delta.isPoisoned ?? isPoisoned;
            isCursed = delta.isCursed ?? isCursed;
        }
    }

    public struct TimeRange : IEquatable<TimeRange>
    {
        public static TimeRange Union(TimeRange a, TimeRange b)
        {
            var start = Math.Min(a.Start.Ticks, b.Start.Ticks);
            var end = Math.Max(a.End.Ticks, b.End.Ticks);

            return new TimeRange(new TimeSpan(start), new TimeSpan(end - start));
        }

        public static TimeRange Intersection(TimeRange a, TimeRange b)
        {
            if (!a.Intersects(b))
            {
                return default;
            }

            var start = Math.Max(a.Start.Ticks, b.Start.Ticks);
            var end = Math.Min(a.End.Ticks, b.End.Ticks);

            return new TimeRange(new TimeSpan(start), new TimeSpan(end - start));
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

        public readonly TimeSpan Start;
        public readonly TimeSpan Duration;

        public TimeSpan End => Start + Duration;

        public TimeRange(TimeSpan start, TimeSpan duration)
        {
            Start = start;
            Duration = duration;
        }

        public TimeRange Extend(TimeSpan margin)
        {
            return Extend(margin, margin);
        }

        public TimeRange Extend(TimeSpan beforeStart, TimeSpan afterEnd)
        {
            return new TimeRange(Start - beforeStart, Duration + beforeStart + afterEnd);
        }

        public bool Intersects(TimeRange other)
        {
            return Start < other.End && End > other.Start;
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
