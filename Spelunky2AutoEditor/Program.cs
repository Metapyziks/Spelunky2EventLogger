using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
            get => (long) (time - DateTime.UnixEpoch).TotalMilliseconds;
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

    class Program
    {
        private const string DateTimeFormat = "YYYY.MM.DD - hh.mm.ss.ff";

        private static readonly Regex DateTimeRegex = new Regex(@"(?:^|[^0-9])(?<year>[0-9]{2}(?:[0-9]{2})?)\.(?<month>[0-9]{1,2})\.(?<day>[0-9]{1,2})\s*-\s*(?<hour>[0-9]{1,2})\.(?<minute>[0-9]{1,2})\.(?<second>[0-9]{1,2}(?:\.[0-9]+)?)(?:$|[^0-9])");

        static bool TryParseDateTime(string value, out DateTime dateTime)
        {
            var match = DateTimeRegex.Match(value);

            if (!match.Success)
            {
                dateTime = default;
                return false;
            }

            var year = int.Parse(match.Groups["year"].Value);
            var month = int.Parse(match.Groups["month"].Value);
            var day = int.Parse(match.Groups["day"].Value);

            var hour = int.Parse(match.Groups["hour"].Value);
            var minute = int.Parse(match.Groups["minute"].Value);
            var second = double.Parse(match.Groups["second"].Value);

            if (match.Groups["year"].Length == 2)
            {
                var currentYear = DateTime.UtcNow.Year;
                var currentCentury = (currentYear / 100) * 100;

                if (year + currentCentury <= currentYear + 1)
                {
                    year += currentCentury;
                }
                else
                {
                    year += currentCentury - 100;
                }
            }

            dateTime = new DateTime(year, month, day, hour, minute, 0).AddSeconds(second);
            return true;
        }

        static int Main(string[] args)
        {
            string inputVideoPath = null;
            string inputEventsPath = null;
            string outputPath = null;
            DateTime videoStartUtc = default;
            TimeSpan videoDuration = default;

            for (var i = 0; i < args.Length - 1; ++i)
            {
                var option = args[i];

                switch (option)
                {
                    case "--input-video":
                        inputVideoPath = args[i + 1];
                        break;

                    case "--input-events":
                        inputEventsPath = args[i + 1];
                        break;

                    case "--output":
                        outputPath = args[i + 1];
                        break;

                    case "--video-start":
                        if (!TryParseDateTime(args[i + 1], out videoStartUtc))
                        {
                            Console.WriteLine($"Invalid date format for --video-start, expected \"{DateTimeFormat}\".");
                        }
                        else
                        {
                            videoStartUtc = videoStartUtc.ToUniversalTime();
                        }
                        break;

                    case "--video-start-utc":
                        if (!TryParseDateTime(args[i + 1], out videoStartUtc))
                        {
                            Console.WriteLine($"Invalid date format for --video-start-utc, expected \"{DateTimeFormat}\".");
                        }
                        break;

                    case "--video-duration":
                        if (!TimeSpan.TryParse(args[i + 1], out videoDuration))
                        {
                            Console.WriteLine($"Invalid time span format for --video-duration.");
                        }
                        break;
                }
            }

            if (inputEventsPath == null)
            {
                Console.WriteLine("Expected event file path. Try using --input-events <path>");
                return 0;
            }

            if (videoStartUtc == default && inputVideoPath != null
                && !TryParseDateTime(Path.GetFileNameWithoutExtension(inputVideoPath), out videoStartUtc))
            {
                Console.WriteLine($"Unable to determine video start date/time.{Environment.NewLine}" +
                    $"Try using --video-start-utc, or a video with a date in the filename formatted like \"{DateTimeFormat}\".");
                return 1;
            }

            if (videoDuration == default)
            {
                Console.WriteLine($"Unable to determine video duration. (TODO: read from video metadata)");
                return 1;
            }

            var clips = new List<TimeRange>();
            var gameState = new GameState();
            var player0State = new PlayerState();

            var beforeEventTime = TimeSpan.FromSeconds(3d);
            var afterEventTime = TimeSpan.FromSeconds(1d);

            foreach (var update in ReadStateUpdates(inputEventsPath))
            {
                if (update.type == UpdateType.Delta && update.player0.life < player0State.life
                    && gameState.ingame == true && gameState.playing == true && gameState.pause == false)
                {
                    Console.WriteLine($"{update.time}: Took {player0State.life.Value - update.player0.life.Value} damage!");
                    clips.Add(new TimeRange(update.time - videoStartUtc, TimeSpan.Zero).Extend(beforeEventTime, afterEventTime));
                }

                gameState.Update(update.game);
                player0State.Update(update.player0);
            }

            TimeRange.RemoveIntersections(clips, TimeSpan.FromSeconds(5d));
            TimeRange.TruncateOutsideRange(clips, new TimeRange(TimeSpan.Zero, videoDuration));

            var writer = new StringWriter();

            for (var i = 0; i < clips.Count; ++i)
            {
                var clip = clips[i];

                writer.WriteLine($"[0:v]trim=start={clip.Start.TotalSeconds}:end={clip.End.TotalSeconds},setpts=PTS-STARTPTS[clip{i}v];");
                writer.WriteLine($"[0:a]atrim=start={clip.Start.TotalSeconds}:end={clip.End.TotalSeconds},asetpts=PTS-STARTPTS[clip{i}a];");
            }

            for (var i = 0; i < clips.Count; ++i)
            {
                writer.Write($"[clip{i}v][clip{i}a]");

                if (i % 4 == 3 || i == clips.Count - 1)
                {
                    writer.WriteLine();
                }
            }

            writer.WriteLine($"concat=n={clips.Count}:v=1:a=1[outv][outa]");

            if (outputPath != null)
            {
                File.WriteAllText(outputPath, writer.ToString());
            }
            else
            {
                Console.WriteLine(writer);
            }

            return 0;
        }

        static IEnumerable<StateUpdate> ReadStateUpdates(string path)
        {
            using var reader = File.OpenText(path);

            var jsonStringBuilder = new StringBuilder();
            var objectDepth = 0;

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var commentIndex = line.IndexOf('#');
                if (commentIndex != -1) line = line.Substring(0, commentIndex);

                var objectStartCount = line.Count(x => x == '{');
                var objectEndCount = line.Count(x => x == '}');

                jsonStringBuilder.AppendLine(line);

                objectDepth += objectStartCount - objectEndCount;

                if (objectDepth < 0)
                {
                    throw new Exception("Unexpected \"}\" encountered.");
                }

                if (objectEndCount > 0 && objectDepth == 0)
                {
                    var jsonString = jsonStringBuilder.ToString().TrimEnd(' ', '\r', '\n', ',');
                    yield return JsonConvert.DeserializeObject<StateUpdate>(jsonString);
                    jsonStringBuilder.Remove(0, jsonStringBuilder.Length);
                }
            }
        }
    }
}
