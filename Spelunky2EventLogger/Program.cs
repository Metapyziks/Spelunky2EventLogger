using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Spelunky2EventLogger
{
    public partial class Program
    {
        public class AppConfiguration
        {
            public double PollPeriod { get; set; }
            public double KeyframePeriod { get; set; }
            public string OutputDirectory { get; set; }
            public string OutputFileName { get; set; }
            public bool ForceSingleFile { get; set; }

            public string OutputPath
            {
                get => Path.Join(OutputDirectory, OutputFileName);
                set
                {
                    OutputDirectory = Path.GetDirectoryName(value);
                    OutputFileName = Path.GetFileName(value);
                }
            }
        }

        public static AppConfiguration Configuration { get; set; }

        private static string GetOutputFileName(DateTime timeUtc, int index)
        {
            var baseOutputPath = Path.Combine(Configuration.OutputDirectory, Configuration.OutputFileName);

            return Utilities.FormatPath(baseOutputPath, new Dictionary<string, object>
            {
                ["startTime"] = timeUtc.ToLocalTime(),
                ["startTimeUtc"] = timeUtc,
                ["index"] = index
            });
        }

        private static StreamWriter CreateLogFile(DateTime timeUtc, bool useIndex, ref int lastIndex)
        {
            var baseOutputPath = Path.Combine(Configuration.OutputDirectory, Configuration.OutputFileName);

            if (!useIndex)
            {
                return CreateLogFile(GetOutputFileName(timeUtc, 0));
            }

            string path;
            while (File.Exists(path = GetOutputFileName(timeUtc, ++lastIndex))) ;

            return CreateLogFile(path);
        }

        private static StreamWriter CreateLogFile(string path)
        {
            Console.WriteLine($"Creating log: \"{path}\"");

            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            return File.CreateText(path);
        }

        private static async Task<int> Main(string[] args)
        {
            var configBuilder = new ConfigurationBuilder();

            configBuilder
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    [nameof(AppConfiguration.PollPeriod)] = "0.01",
                    [nameof(AppConfiguration.KeyframePeriod)] = "60",
                    [nameof(AppConfiguration.OutputDirectory)] = "{userprofile}\\Videos\\Spelunky 2",
                    [nameof(AppConfiguration.OutputFileName)] = "Spelunky 2 {startTimeUtc:yyyy.MM.dd - HH.mm.ss}.{index:00}.log",
                    [nameof(AppConfiguration.ForceSingleFile)] = "false"
                })
                .AddJsonFile("Config.json", true)
                .AddCommandLine(args, new Dictionary<string, string>
                {
                    ["--poll-period"] = nameof(AppConfiguration.PollPeriod),
                    ["--keyframe-period"] = nameof(AppConfiguration.KeyframePeriod),

                    ["-o"] = nameof(AppConfiguration.OutputPath),
                    ["--output"] = nameof(AppConfiguration.OutputPath),

                    ["--output-dir"] = nameof(AppConfiguration.OutputDirectory),
                    ["--output-filename"] = nameof(AppConfiguration.OutputFileName),

                    ["--force-single-file"] = nameof(AppConfiguration.ForceSingleFile)
                });

            Configuration = configBuilder.Build().Get<AppConfiguration>();

            Console.WriteLine("Searching for Spelunky 2...");
            var process = await Utilities.FindProcess("Spel2");

            using var scanner = new MemoryScanner(process);

            IntPtr autoSplitterAddress, playerAddress;

            Console.WriteLine("Searching for AutoSplitter struct...");

            while (true)
            {
                autoSplitterAddress = scanner.FindString("DREGUASL", Encoding.ASCII, 8);
                if (autoSplitterAddress != IntPtr.Zero) break;
                await Task.Delay(100);
            }

            Console.WriteLine($"AutoSplitter address: 0x{autoSplitterAddress.ToInt64():x}");
            Console.WriteLine("Searching for Player struct...");

            while (true)
            {
                playerAddress = scanner.FindUInt32(0xfeedc0de, 4, autoSplitterAddress);
                if (playerAddress != IntPtr.Zero) break;
                await Task.Delay(100);
            }

            playerAddress -= 0x2D8;

            Console.WriteLine($"Player address: 0x{playerAddress.ToInt64():x}");

            var utcNow = DateTime.UtcNow;
            var singleFile = Configuration.ForceSingleFile || GetOutputFileName(utcNow, 0) == GetOutputFileName(utcNow.AddSeconds(5d), 1);
            var useIndex = GetOutputFileName(utcNow, 0) != GetOutputFileName(utcNow, 1);

            var timer = new Stopwatch();
            timer.Start();

            var keyframeTimer = new Stopwatch();
            keyframeTimer.Start();

            var firstKeyframe = true;

            AutoSplitter autoSplitterWritten = default;
            AutoSplitter autoSplitterPrev = default;
            Player playerWritten = default;
            Player playerPrev = default;

            var lastIgt = uint.MaxValue;

            GameState gameState = new GameState();
            PlayerState player0State = new PlayerState();

            var changedSinceKeyframe = true;
            var jsonOptions = new JsonSerializerOptions
            {
                IgnoreNullValues = true,
                Converters =
                {
                    new JsonStringEnumConverter()
                }
            };

            StreamWriter writer = null;
            var lastFileIndex = 0;

            if (singleFile)
            {
                writer = CreateLogFile(utcNow, useIndex, ref lastFileIndex);
            }

            try
            {
                while (scanner.ReadStructure<AutoSplitter>(autoSplitterAddress, out var autoSplitter) &&
                       scanner.ReadStructure<Player>(playerAddress, out var player))
                {
                    if (autoSplitterPrev.uniq == autoSplitter.uniq) continue;

                    var stable =
                        !WatchedProperties<AutoSplitter, GameState>.HaveFieldsChanged(autoSplitterPrev, autoSplitter)
                        && !WatchedProperties<Player, PlayerState>.HaveFieldsChanged(playerPrev, player);

                    if (stable)
                    {
                        var now = DateTime.UtcNow;

                        if (!singleFile && autoSplitter.playing && autoSplitter.ingame)
                        {
                            if (autoSplitter.igt < lastIgt)
                            {
                                firstKeyframe = true;
                                writer?.Dispose();
                                writer = CreateLogFile(now, useIndex, ref lastFileIndex);
                            }

                            lastIgt = autoSplitter.igt;
                        }

                        if (writer != null)
                        {
                            if (changedSinceKeyframe && (firstKeyframe || keyframeTimer.Elapsed.TotalSeconds >= Configuration.KeyframePeriod))
                            {
                                keyframeTimer.Restart();

                                firstKeyframe = false;
                                changedSinceKeyframe = false;

                                WatchedProperties<AutoSplitter, GameState>.CopyAllFields(in autoSplitter, gameState);
                                WatchedProperties<Player, PlayerState>.CopyAllFields(in player, player0State);

                                writer.Write(JsonSerializer.Serialize(new StateUpdate
                                {
                                    type = UpdateType.Keyframe,
                                    time = now,
                                    game = gameState,
                                    player0 = player0State
                                }, jsonOptions));
                                writer.WriteLine(",");
                                await writer.FlushAsync();

                                autoSplitterWritten = autoSplitter;
                                playerWritten = player;
                            }
                            else
                            {
                                var autoSplitterChanged = WatchedProperties<AutoSplitter, GameState>.CopyChangedFields(
                                    autoSplitterWritten, autoSplitter, gameState);

                                var playerChanged = WatchedProperties<Player, PlayerState>.CopyChangedFields(
                                    playerWritten, player, player0State);

                                if (autoSplitterChanged + playerChanged > 0)
                                {
                                    changedSinceKeyframe = true;

                                    writer.Write(JsonSerializer.Serialize(new StateUpdate
                                    {
                                        type = UpdateType.Delta,
                                        time = now,
                                        game = gameState,
                                        player0 = playerChanged > 0 ? player0State : null
                                    }, jsonOptions));
                                    writer.WriteLine(",");

                                    autoSplitterWritten = autoSplitter;
                                    playerWritten = player;
                                }
                            }
                        }
                    }

                    autoSplitterPrev = autoSplitter;
                    playerPrev = player;

                    await Task.Delay(
                        TimeSpan.FromSeconds(Math.Max(Configuration.PollPeriod - timer.Elapsed.TotalSeconds, 0)));
                    timer.Restart();
                }
            }
            finally
            {
                writer?.Dispose();
            }

            return 0;
        }
    }
}
