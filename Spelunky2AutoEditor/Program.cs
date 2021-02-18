using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
            public string InputVideoDirectory { get; set; } = "{userprofile}\\Videos\\Spelunky 2";
            public string InputVideoFileName { get; set; } = "Spelunky 2 {?startTime:yyyy.MM.dd - HH.mm.ss.ff}[.DVR].mp4";

            public string InputVideoPath
            {
                get => Path.Join(InputVideoDirectory, InputVideoFileName);
                set
                {
                    InputVideoDirectory = Path.GetDirectoryName(value);
                    InputVideoFileName = Path.GetFileName(value);
                }
            }

            public string InputEventsPath { get; set; }
            public string OutputPath { get; set; }
            public DateTime? VideoStartUtc { get; set; }
            public string FfmpegPath { get; set; } = "ffmpeg.exe";

            public string EditorConfigPath { get; set; }

            public DateTime? VideoStartLocal
            {
                get => VideoStartUtc?.ToLocalTime();
                set => VideoStartUtc = value?.ToUniversalTime();
            }
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

            public static readonly EditorConfiguration LowlightsRapid = new EditorConfiguration
            {
                EventCategories =
                {
                    // Lost health
                    new EditorConfiguration.EventCategory
                    {
                        Events = { PlayerState.Fields.life },
                        Filter = EditorConfiguration.Filter.Decrease,
                        ScoreType = EditorConfiguration.ScoreType.Linear,
                        BeforeTime = 1d,
                        AfterTime = 0.5d,
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

        public struct InputVideo
        {
            public readonly string Path;
            public readonly TimeRange TimeRange;

            public InputVideo(string path, DateTime startTime, TimeSpan duration)
            {
                Path = path;
                TimeRange = new TimeRange(startTime, duration);
            }
        }

        private const string DateTimeFormat = "yyyy.MM.dd - HH.mm.ss.ff";

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
                .AddJsonFile("Config.json", true)
                .AddCommandLine(args, new Dictionary<string, string>
                {
                    ["--input-video"] = nameof(AppConfiguration.InputVideoPath),
                    ["--input-video-dir"] = nameof(AppConfiguration.InputVideoDirectory),
                    ["--input-video-filename"] = nameof(AppConfiguration.InputVideoFileName),
                    ["--input-events"] = nameof(AppConfiguration.InputEventsPath),

                    ["--output"] = nameof(AppConfiguration.OutputPath),

                    ["--video-start-utc"] = nameof(AppConfiguration.VideoStartUtc),
                    ["--video-start-local"] = nameof(AppConfiguration.VideoStartLocal)
                });

            Configuration = configBuilder.Build().Get<AppConfiguration>();

            if (Configuration.InputEventsPath == null)
            {
                Console.Error.WriteLine("Expected event file path. Try using --input-events <path>");
                return 1;
            }

            var editorConfig = EditorConfiguration.LowlightsRapid;

            if (Configuration.EditorConfigPath != null)
            {
                editorConfig = JsonSerializer.Deserialize<EditorConfiguration>(File.ReadAllText(Configuration.EditorConfigPath), JsonSerializerOptions);
            }

            var stateUpdates = ReadStateUpdates(Configuration.InputEventsPath)
                .ToArray();

            var firstUpdateTime = stateUpdates[0].time;
            var lastUpdateTime = stateUpdates[^1].time;
            var updateTimeRange = new TimeRange(firstUpdateTime, lastUpdateTime - firstUpdateTime);

            Console.Error.WriteLine($"Total updates: {stateUpdates.Length}");
            Console.Error.WriteLine($"  Start time: {updateTimeRange.Start}");
            Console.Error.WriteLine($"  Duration: {updateTimeRange.Duration}");
            Console.Error.WriteLine();

            var inputVideos = GetInputVideos(Configuration.InputVideoPath)
                .Where(x => x.TimeRange.Intersects(updateTimeRange))
                .ToArray();

            if (inputVideos.Length == 0)
            {
                Console.Error.WriteLine("Unable to find any matching input videos.");
                return 1;
            }

            foreach (var inputVideo in inputVideos)
            {
                Console.Error.WriteLine($"Input video: {inputVideo.Path}");
                Console.Error.WriteLine($"  Start time: {inputVideo.TimeRange.Start}");
                Console.Error.WriteLine($"  Duration: {inputVideo.TimeRange.Duration}");
                Console.Error.WriteLine();
            }

            var clips = new List<TimeRange>();

            foreach (var evnt in GetEvents(stateUpdates))
            {
                var score = GetScore(evnt, editorConfig, out var beforeTime, out var afterTime);
                if (score > 0d)
                {
                    clips.Add(new TimeRange(evnt.Time, TimeSpan.Zero)
                        .Extend(TimeSpan.FromSeconds(beforeTime), TimeSpan.FromSeconds(afterTime)));
                }
            }

            Console.Error.WriteLine($"Events to clip: {clips.Count}");

            clips.Sort((a, b) => a.Start.CompareTo(b.Start));
            clips.RemoveAll(x => inputVideos.All(y => !y.TimeRange.Contains(x)));

            // TODO: might merge clips from different input videos
            TimeRange.RemoveIntersections(clips, TimeSpan.FromSeconds(editorConfig.MaxMergeTime));

            Console.Error.WriteLine($"Total duration: {clips.Aggregate(TimeSpan.Zero, (s, x) => s + x.Duration)}");
            Console.Error.WriteLine();

            if (clips.Count == 0)
            {
                Console.Error.WriteLine("No clips found.");
                return 1;
            }

            var editArgs = GetVideoEditArgs(inputVideos, clips, Configuration.OutputPath);

            if (Configuration.OutputPath == null)
            {
                Console.WriteLine(FormatArgs(editArgs));
                return 0;
            }

            var ext = Path.GetExtension(Configuration.OutputPath)?.ToLower() ?? ".txt";

            if (ext == ".txt")
            {
                File.WriteAllText(Configuration.OutputPath, FormatArgs(editArgs));
                return 0;
            }

            Ffmpeg(editArgs);
            return 0;
        }

        private static readonly Regex SpecialCharactersRegex = new Regex(@"[.+*?^$()[\]{}\\]");

        static string EscapeRegexSpecialCharacters(string value)
        {
            return SpecialCharactersRegex.Replace(value, match => $"\\{match.Value}");
        }

        private static readonly Regex PatternRegex = new Regex(@"\{\s*\?\s*(?<name>[A-Za-z0-9_]+)\s*(?::(?<format>[^}]+))?\}|\[(?<optional>[^\]]+)\]");
        private static readonly Regex DateTimeFormatRegex = new Regex(@"yyyy|yy|MM|M|dd|d|HH|H|mm|m|ss|s|f{1,3}");

        static string GetDateTimeRegexString(string format)
        {
            return DateTimeFormatRegex.Replace(EscapeRegexSpecialCharacters(format), match =>
            {
                var character = match.Value[0];
                var length = match.Length;

                var minLength = length == 1 ? 1 : length;
                var maxLength = length == 1 ? 2 : length;

                if (character == 'f') minLength = maxLength = length;

                return minLength == maxLength ? minLength == 1 ? @"[0-9]" : $@"[0-9]{{{minLength}}}" : $@"[0-9]{{{minLength},{maxLength}}}";
            });
        }

        static IEnumerable<InputVideo> GetInputVideos(string inputVideoPath)
        {
            inputVideoPath = Utilities.FormatPath(inputVideoPath);

            var inputDir = Path.GetDirectoryName(inputVideoPath);
            var inputFileName = Path.GetFileName(inputVideoPath);

            if (PatternRegex.IsMatch(inputDir))
            {
                throw new NotImplementedException("Wildcards in directory names aren't supported yet.");
            }

            if (!PatternRegex.IsMatch(inputFileName))
            {
                if (!Configuration.VideoStartUtc.HasValue)
                {
                    throw new Exception("Unable to determine input video start time.");
                }

                yield return new InputVideo(inputVideoPath, Configuration.VideoStartUtc.Value, GetVideoDuration(inputVideoPath));
                yield break;
            }

            var parts = new List<string>();

            var matches = PatternRegex.Matches(inputFileName);
            var lastMatchEnd = 0;

            var formats = new Dictionary<string, string>();

            foreach (Match match in matches)
            {
                if (match.Index > lastMatchEnd)
                {
                    parts.Add(EscapeRegexSpecialCharacters(inputFileName.Substring(lastMatchEnd, match.Index - lastMatchEnd)));
                }

                if (match.Groups["optional"].Success)
                {
                    parts.Add($@"({EscapeRegexSpecialCharacters(match.Groups["optional"].Value)})?");
                }
                else if (match.Groups["name"].Success)
                {
                    var name = match.Groups["name"].Value;
                    var format = match.Groups["format"].Success ? match.Groups["format"].Value : DateTimeFormat;

                    formats.Add(name, format);

                    switch (name)
                    {
                        case "startTime":
                        case "startTimeUtc":
                            parts.Add($@"(?<{name}>{GetDateTimeRegexString(format)})");
                            break;
                        default:
                            throw new Exception($"Unrecognised substitution name \"{name}\" in input video path.");
                    }
                }

                lastMatchEnd = match.Index + match.Length;
            }

            if (lastMatchEnd < inputFileName.Length)
            {
                parts.Add(EscapeRegexSpecialCharacters(inputFileName.Substring(lastMatchEnd)));
            }

            var regexString = string.Join("", parts);

            var regex = new Regex(regexString);

            foreach (var file in Directory.GetFiles(inputDir))
            {
                var fileName = Path.GetFileName(file);
                var match = regex.Match(fileName);

                if (!match.Success) continue;

                DateTime? startTime = null;

                foreach (Group group in match.Groups)
                {
                    if (!group.Success || group.Name == null) continue;

                    switch (group.Name)
                    {
                        case "startTime":
                        case "startTimeUtc":
                            startTime = DateTime.ParseExact(group.Value, formats[group.Name], CultureInfo.InvariantCulture);
                            if (!group.Name.EndsWith("Utc")) startTime = startTime.Value.ToUniversalTime();
                            break;
                    }
                }

                if (!startTime.HasValue)
                {
                    throw new Exception("Unable to determine input video start time.");
                }

                yield return new InputVideo(file, startTime.Value, GetVideoDuration(file));
            }
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

        static TimeSpan GetVideoDuration(string path)
        {
            if (!TryGetVideoDuration(path, out var duration))
            {
                throw new Exception($"Unable to determine video duration for \"{path}\".");
            }

            return duration;
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

        static string[] GetVideoEditArgs(IReadOnlyList<InputVideo> inputVideos, IReadOnlyList<TimeRange> clips, string outputPath)
        {
            var args = new List<string>();
            var filterWriter = new StringWriter();

            var index = 0;
            foreach (var clip in clips)
            {
                var inputVideo = inputVideos.First(x => x.TimeRange.Contains(clip));

                args.AddRange(new[]
                {
                    "-ss", (clip.Start - inputVideo.TimeRange.Start).TotalSeconds.ToString("F3"),
                    "-t", clip.Duration.TotalSeconds.ToString("F3"),
                    "-i", inputVideo.Path
                });

                filterWriter.Write($"[{index}:v][{index}:a]");

                ++index;
            }

            filterWriter.Write($"concat=n={clips.Count}:v=1:a=1");

            args.AddRange(new []
            {
                "-filter_complex", filterWriter.ToString(),
                "-y", "-stats",
                "-loglevel", "quiet"
            });

            if (!string.IsNullOrEmpty(outputPath))
            {
                args.Add(outputPath);
            }

            return args.ToArray();
        }

        static string FormatArgs(IReadOnlyList<string> args)
        {
            var writer = new StringWriter();

            var first = true;

            foreach (var arg in args)
            {
                if (!first) writer.Write(" ");
                else first = false;

                writer.Write(arg.StartsWith('-') ? arg : $"\"{arg}\"");
            }

            return writer.ToString();
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
