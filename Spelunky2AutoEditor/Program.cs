using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Spelunky2EventLogger;

namespace Spelunky2AutoEditor
{
    class Program
    {
        public class AppConfiguration
        {
            public string InputVideoPath { get; set; }
            public string InputEventsPath { get; set; }
            public string OutputPath { get; set; }
            public DateTime? VideoStartUtc { get; set; }
            public string FfmpegPath { get; set; }

            public string EditorConfigPath { get; set; }

            public DateTime? VideoStartLocal
            {
                get => VideoStartUtc?.ToLocalTime();
                set => VideoStartUtc = value?.ToUniversalTime();
            }

            public TimeSpan? VideoDuration { get; set; }
        }

        public class EditorConfiguration
        {
            public enum Filter
            {
                Default,
                Increase,
                Decrease
            }

            public enum ScoreType
            {
                Constant,
                Linear
            }

            public static readonly EditorConfiguration UsedUtility = new EditorConfiguration
            {
                EventCategories =
                {
                    // Used bombs or ropes
                    new EditorConfiguration.EventCategory
                    {
                        Events = { PlayerState.Fields.numBombs, PlayerState.Fields.numRopes },
                        Filter = EditorConfiguration.Filter.Decrease,
                        ScoreType = EditorConfiguration.ScoreType.Linear
                    },
                }
            };

            public static readonly EditorConfiguration Lowlights = new EditorConfiguration
            {
                EventCategories =
                {
                    // Lost health
                    new EditorConfiguration.EventCategory
                    {
                        Events = { PlayerState.Fields.life },
                        Filter = EditorConfiguration.Filter.Decrease,
                        ScoreType = EditorConfiguration.ScoreType.Linear
                    },
                    // Lost ankh or died
                    new EditorConfiguration.EventCategory
                    {
                        Events = { PlayerState.Fields.hasAnkh, PlayerState.Fields.life },
                        Absolute = true,
                        ExactValue = 0,
                        Score = 100d
                    },
                    // Poisoned or cursed
                    new EditorConfiguration.EventCategory
                    {
                        Events = { PlayerState.Fields.isPoisoned, PlayerState.Fields.isCursed },
                        Absolute = true,
                        ExactValue = 1,
                        Score = 50d
                    }
                }
            };

            public class EventCategory
            {
                public List<EventField> Events { get; } = new List<EventField>();
                public Filter Filter { get; set; }
                public bool Absolute { get; set; }
                public ScoreType ScoreType { get; set; }
                public int Minimum { get; set; }
                public int Maximum { get; set; } = int.MaxValue;
                public int ExactValue
                {
                    get => Minimum;
                    set => Minimum = Maximum = value;
                }
                public double Score { get; set; } = 1d;
                public double BeforeTime { get; set; } = 3d;
                public double AfterTime { get; set; } = 2d;
            }

            public List<EventCategory> EventCategories { get; } = new List<EventCategory>();
            public double MaxMergeTime { get; set; } = 3d;
        }

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

        public static AppConfiguration Configuration { get; set; }
        public static JsonSerializerOptions JsonSerializerOptions { get; set; }

        static int Main(string[] args)
        {
            JsonSerializerOptions = new JsonSerializerOptions
            {
                IgnoreNullValues = true,
                Converters =
                {
                    new JsonStringEnumConverter()
                }
            };

            var configBuilder = new ConfigurationBuilder();

            configBuilder
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    [nameof(AppConfiguration.FfmpegPath)] = "ffmpeg.exe"
                })
                .AddJsonFile("Config.json", true)
                .AddCommandLine(args, new Dictionary<string, string>
                {
                    ["--input-video"] = nameof(AppConfiguration.InputVideoPath),
                    ["--input-events"] = nameof(AppConfiguration.InputEventsPath),

                    ["--output"] = nameof(AppConfiguration.OutputPath),

                    ["--video-start-utc"] = nameof(AppConfiguration.VideoStartUtc),
                    ["--video-start-local"] = nameof(AppConfiguration.VideoStartLocal),

                    ["--video-duration"] = nameof(AppConfiguration.VideoDuration)
                });

            Configuration = configBuilder.Build().Get<AppConfiguration>();

            if (Configuration.InputEventsPath == null)
            {
                Console.Error.WriteLine("Expected event file path. Try using --input-events <path>");
                return 1;
            }

            var videoStartUtc = Configuration.VideoStartUtc ?? default;

            if (!Configuration.VideoStartUtc.HasValue && Configuration.InputVideoPath != null
                && !TryParseDateTime(Path.GetFileNameWithoutExtension(Configuration.InputVideoPath), out videoStartUtc))
            {
                Console.Error.WriteLine($"Unable to determine video start date/time.{Environment.NewLine}" +
                    $"Try using --video-start-utc, or a video with a date in the filename formatted like \"{DateTimeFormat}\".");
                return 1;
            }

            var videoDuration = Configuration.VideoDuration ?? default;

            if (!Configuration.VideoStartUtc.HasValue && Configuration.InputVideoPath != null
                && !TryGetVideoDuration(Configuration.InputVideoPath, out videoDuration))
            {
                Console.Error.WriteLine($"Unable to determine video duration.{Environment.NewLine}" +
                    $"Try using --video-duration, or --input-video <path> with a valid video file.");
                return 1;
            }

            EditorConfiguration editorConfig = EditorConfiguration.Lowlights;

            if (Configuration.EditorConfigPath != null)
            {
                editorConfig = JsonSerializer.Deserialize<EditorConfiguration>(File.ReadAllText(Configuration.EditorConfigPath), JsonSerializerOptions);
            }

            var clips = new List<TimeRange>();

            foreach (var evnt in GetEvents(ReadStateUpdates(Configuration.InputEventsPath)))
            {
                var score = GetScore(evnt, editorConfig, out var beforeTime, out var afterTime);
                if (score > 0d)
                {
                    clips.Add(new TimeRange(evnt.Time - videoStartUtc, TimeSpan.Zero)
                        .Extend(TimeSpan.FromSeconds(beforeTime), TimeSpan.FromSeconds(afterTime)));
                }
            }

            clips.Sort((a, b) => a.Start.CompareTo(b.Start));
            TimeRange.RemoveIntersections(clips, TimeSpan.FromSeconds(editorConfig.MaxMergeTime));
            TimeRange.TruncateOutsideRange(clips, new TimeRange(TimeSpan.Zero, videoDuration));

            Console.Error.WriteLine($"Total duration: {clips.Aggregate(TimeSpan.Zero, (s, x) => s + x.Duration)}");

            if (clips.Count == 0)
            {
                Console.Error.WriteLine("No clips found.");
                return 1;
            }

            if (Configuration.OutputPath == null)
            {
                Console.WriteLine(GetComplexFilterString(clips));
                return 0;
            }

            var ext = Path.GetExtension(Configuration.OutputPath)?.ToLower() ?? ".txt";

            if (ext == ".txt")
            {
                File.WriteAllText(Configuration.OutputPath, GetComplexFilterString(clips));
                return 0;
            }

            if (ext == Path.GetExtension(Configuration.InputVideoPath)?.ToLower())
            {
                return EditVideo(Configuration.InputVideoPath, clips, Configuration.OutputPath) ? 0 : 1;
            }

            Console.Error.WriteLine($"Unexpected output extension \"{ext}\". Please use either \".txt\" or the same extension as the input video.");
            return 1;
        }

        private static readonly Regex DurationRegex = new Regex(@"^\s*Duration: (?<duration>[0-9:.]+),", RegexOptions.Multiline);

        static void Ffmpeg(params string[] args)
        {
            var processStart = new ProcessStartInfo(Configuration.FfmpegPath)
            {
                UseShellExecute = false,
                ErrorDialog = false,
                CreateNoWindow = false
            };

            foreach (var arg in args)
            {
                processStart.ArgumentList.Add(arg);
            }

            using var process = Process.Start(processStart);
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new Exception($"Error running ffmpeg.exe: exited with code {process.ExitCode}.");
            }
        }

        static string FfmpegInfo(string path)
        {
            var processStart = new ProcessStartInfo(Configuration.FfmpegPath)
            {
                ArgumentList = { "-i", path },
                UseShellExecute = false,
                ErrorDialog = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(processStart);
            return process.StandardError.ReadToEnd();
        }

        static bool TryGetVideoDuration(string path, out TimeSpan duration)
        {
            var result = FfmpegInfo(path);
            var match = DurationRegex.Match(result);

            if (match.Success)
            {
                duration = TimeSpan.Parse(match.Groups["duration"].Value);
                return true;
            }

            duration = default;
            return false;
        }

        static string GetComplexFilterString(IReadOnlyList<TimeRange> clips)
        {
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

            return writer.ToString();
        }

        static bool EditVideo(string inputPath, IReadOnlyList<TimeRange> clips, string outputPath)
        {
            Console.Error.WriteLine($"Concatenating {clips.Count} clips...");
            
            var args = new List<string>();

            var filterWriter = new StringWriter();

            var index = 0;
            foreach (var clip in clips)
            {
                args.AddRange(new[]
                {
                    "-ss", clip.Start.TotalSeconds.ToString("F3"),
                    "-t", clip.Duration.TotalSeconds.ToString("F3"),
                    "-i", inputPath
                });

                filterWriter.Write($"[{index}:v][{index}:a]");

                ++index;
            }

            filterWriter.Write($"concat=n={clips.Count}:v=1:a=1");

            args.AddRange(new []
            {
                "-filter_complex", filterWriter.ToString(),
                "-y", "-stats",
                "-loglevel", "quiet",
                outputPath
            });

            Ffmpeg(args.ToArray());

            return true;
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
                    yield return JsonSerializer.Deserialize<StateUpdate>(jsonString, JsonSerializerOptions);
                    jsonStringBuilder.Remove(0, jsonStringBuilder.Length);
                }
            }
        }

        static IEnumerable<Event> GetEvents(IEnumerable<StateUpdate> updates)
        {
            var gameState = new GameState();
            var player0State = new PlayerState();

            var lastValidGameState = new GameState();
            var lastValidPlayer0State = new PlayerState();

            var first = true;

            foreach (var update in updates)
            {
                gameState.Update(update.game);
                player0State.Update(update.player0);

                if (first)
                {
                    first = false;

                    if (update.type != UpdateType.Keyframe)
                    {
                        throw new Exception("Expected first update to be a keyframe.");
                    }

                    lastValidGameState.Update(gameState);
                    lastValidPlayer0State.Update(player0State);
                }

                var valid = gameState.ingame == true && gameState.playing == true && gameState.pause == false && gameState.loading == false;

                if (!valid) continue;

                // GameState

                if (lastValidGameState.world.Value != gameState.world.Value)
                {
                    yield return new Event(GameState.Fields.world, update.time,
                        lastValidGameState.world.Value, gameState.world.Value);
                }

                if (lastValidGameState.level.Value != gameState.level.Value)
                {
                    yield return new Event(GameState.Fields.level, update.time,
                        lastValidGameState.level.Value, gameState.level.Value);
                }

                if (lastValidGameState.door.Value != gameState.door.Value)
                {
                    yield return new Event(GameState.Fields.door, update.time,
                        lastValidGameState.door.Value, gameState.door.Value);
                }

                if (lastValidGameState.currentScore.Value != gameState.currentScore.Value)
                {
                    yield return new Event(GameState.Fields.currentScore, update.time,
                        lastValidGameState.currentScore.Value, gameState.currentScore.Value);
                }

                // PlayerState

                if (lastValidPlayer0State.life.Value != player0State.life.Value)
                {
                    yield return new Event(PlayerState.Fields.life, 0, update.time,
                        lastValidPlayer0State.life.Value, player0State.life.Value);
                }

                if (lastValidPlayer0State.numBombs.Value != player0State.numBombs.Value)
                {
                    yield return new Event(PlayerState.Fields.numBombs, 0, update.time,
                        lastValidPlayer0State.numBombs.Value, player0State.numBombs.Value);
                }

                if (lastValidPlayer0State.numRopes.Value != player0State.numRopes.Value)
                {
                    yield return new Event(PlayerState.Fields.numRopes, 0, update.time,
                        lastValidPlayer0State.numRopes.Value, player0State.numRopes.Value);
                }

                if (lastValidPlayer0State.hasAnkh.Value != player0State.hasAnkh.Value)
                {
                    yield return new Event(PlayerState.Fields.hasAnkh, 0, update.time,
                        lastValidPlayer0State.hasAnkh.Value, player0State.hasAnkh.Value);
                }

                if (lastValidPlayer0State.hasKapala.Value != player0State.hasKapala.Value)
                {
                    yield return new Event(PlayerState.Fields.hasKapala, 0, update.time,
                        lastValidPlayer0State.hasKapala.Value, player0State.hasKapala.Value);
                }

                if (lastValidPlayer0State.isPoisoned.Value != player0State.isPoisoned.Value)
                {
                    yield return new Event(PlayerState.Fields.isPoisoned, 0, update.time,
                        lastValidPlayer0State.isPoisoned.Value, player0State.isPoisoned.Value);
                }

                if (lastValidPlayer0State.isCursed.Value != player0State.isCursed.Value)
                {
                    yield return new Event(PlayerState.Fields.isCursed, 0, update.time,
                        lastValidPlayer0State.isCursed.Value, player0State.isCursed.Value);
                }

                lastValidGameState.Update(gameState);
                lastValidPlayer0State.Update(player0State);
            }
        }

        static double GetScore(Event evnt, EditorConfiguration config, out double beforeTime, out double afterTime)
        {
            var score = 0d;
            beforeTime = 0d;
            afterTime = 0d;

            foreach (var category in config.EventCategories)
            {
                if (!category.Events.Contains(evnt.Field))
                {
                    continue;
                }

                var delta = evnt.Delta;

                switch (category.Filter)
                {
                    case EditorConfiguration.Filter.Increase:
                        if (delta < 0) continue;
                        break;
                    case EditorConfiguration.Filter.Decrease:
                        if (delta > 0) continue;
                        delta = -delta;
                        break;
                }

                var value = category.Absolute ? evnt.Value : delta;

                if (value < category.Minimum || value > category.Maximum)
                {
                    continue;
                }

                beforeTime = Math.Max(beforeTime, category.BeforeTime);
                afterTime = Math.Max(afterTime, category.AfterTime);

                switch (category.ScoreType)
                {
                    case EditorConfiguration.ScoreType.Constant:
                        score += category.Score;
                        break;
                    case EditorConfiguration.ScoreType.Linear:
                        score += category.Score * Math.Abs(value);
                        break;
                }
            }

            return score;
        }
    }
}
