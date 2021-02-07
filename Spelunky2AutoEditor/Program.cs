using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

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

            public DateTime? VideoStartLocal
            {
                get => VideoStartUtc?.ToLocalTime();
                set => VideoStartUtc = value?.ToUniversalTime();
            }

            public TimeSpan? VideoDuration { get; set; }
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

        static int Main(string[] args)
        {
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

            var clips = new List<TimeRange>();
            var gameState = new GameState();
            var player0State = new PlayerState();

            var lastValidGameState = new GameState();
            var lastValidPlayer0State = new PlayerState();

            var beforeEventTime = TimeSpan.FromSeconds(3d);
            var afterEventTime = TimeSpan.FromSeconds(2d);

            var first = true;

            foreach (var update in ReadStateUpdates(Configuration.InputEventsPath))
            {
                gameState.Update(update.game);
                player0State.Update(update.player0);

                if (first)
                {
                    first = false;

                    lastValidGameState.Update(gameState);
                    lastValidPlayer0State.Update(player0State);
                }

                var valid = gameState.ingame == true && gameState.playing == true && gameState.pause == false && gameState.loading == false;

                if (!valid) continue;

                var levelChanged = gameState.level != lastValidGameState.level;
                var worldChanged = gameState.world != lastValidGameState.world;

                var scoreChanged = gameState.currentScore != lastValidGameState.currentScore;

                var lostHealth = player0State.life < lastValidPlayer0State.life;
                var gainedLotsHealth = player0State.life >= lastValidPlayer0State.life + 4;
                var lostAnkh = !player0State.hasAnkh.Value && lastValidPlayer0State.hasAnkh.Value;
                var gainedAnkh = player0State.hasAnkh.Value && !lastValidPlayer0State.hasAnkh.Value;
                var usedRope = player0State.numRopes < lastValidPlayer0State.numRopes;
                var gainedRopes = player0State.numRopes > lastValidPlayer0State.numRopes;
                var usedBomb = player0State.numBombs < lastValidPlayer0State.numBombs;
                var gainedBombs = player0State.numBombs > lastValidPlayer0State.numBombs;
                var gainedKapala = player0State.hasKapala.Value && !lastValidPlayer0State.hasKapala.Value;
                var poisoned = player0State.isPoisoned.Value && !lastValidPlayer0State.isPoisoned.Value;
                var cured = !player0State.isPoisoned.Value && lastValidPlayer0State.isPoisoned.Value;
                var cursed = player0State.isCursed.Value && !lastValidPlayer0State.isCursed.Value;
                var uncursed = !player0State.isCursed.Value && lastValidPlayer0State.isCursed.Value;
                var spentMoney = gameState.currentScore < lastValidGameState.currentScore;
                var gainedMoney = gameState.currentScore > lastValidGameState.currentScore;

                var videoTime = update.time - videoStartUtc;
                
                if (levelChanged || worldChanged)
                {
                    Console.Error.WriteLine($"{videoTime}: Level changed ({lastValidGameState.world}-{lastValidGameState.level} -> {gameState.world}-{gameState.level})");
                }

                if (lostHealth)
                {
                    Console.Error.WriteLine($"{videoTime}: Lost health ({lastValidPlayer0State.life} -> {player0State.life})");
                }

                if (gainedLotsHealth)
                {
                    Console.Error.WriteLine($"{videoTime}: Gained lots of health ({lastValidPlayer0State.life} -> {player0State.life})");
                }

                if (lostAnkh)
                {
                    Console.Error.WriteLine($"{videoTime}: Lost Ankh");
                }

                if (gainedAnkh)
                {
                    Console.Error.WriteLine($"{videoTime}: Gained Ankh");
                }

                if (gainedKapala)
                {
                    Console.Error.WriteLine($"{videoTime}: Gained Kapala");
                }

                if (poisoned)
                {
                    Console.Error.WriteLine($"{videoTime}: Poisoned");
                }

                if (cured)
                {
                    Console.Error.WriteLine($"{videoTime}: Cured");
                }

                if (cursed)
                {
                    Console.Error.WriteLine($"{videoTime}: Cursed");
                }

                if (uncursed)
                {
                    Console.Error.WriteLine($"{videoTime}: Uncursed");
                }

                if (spentMoney)
                {
                    Console.Error.WriteLine($"{videoTime}: Spent money (${lastValidGameState.currentScore.Value - gameState.currentScore.Value})");
                }

                if (lostHealth || gainedLotsHealth || lostAnkh || poisoned || cursed || gainedKapala || gainedAnkh)
                {
                    clips.Add(new TimeRange(videoTime, TimeSpan.Zero).Extend(beforeEventTime, afterEventTime));
                }
                else if (spentMoney || cured || uncursed)
                {
                    clips.Add(new TimeRange(videoTime, TimeSpan.Zero).Extend(beforeEventTime, afterEventTime));
                }

                lastValidGameState = gameState;
                lastValidPlayer0State = player0State;
            }

            clips.Sort((a, b) => a.Start.CompareTo(b.Start));
            TimeRange.RemoveIntersections(clips, TimeSpan.FromSeconds(5d));
            TimeRange.TruncateOutsideRange(clips, new TimeRange(TimeSpan.Zero, videoDuration));

            Console.Error.WriteLine($"Total duration: {clips.Aggregate(TimeSpan.Zero, (s, x) => s + x.Duration)}");

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
                    yield return JsonConvert.DeserializeObject<StateUpdate>(jsonString);
                    jsonStringBuilder.Remove(0, jsonStringBuilder.Length);
                }
            }
        }
    }
}
