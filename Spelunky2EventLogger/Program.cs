using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Spelunky2EventLogger
{
    public partial class Program
    {
        public class AppConfiguration
        {
            public int PollPeriodMilliseconds { get; set; }
            public int KeyframePeriodMilliseconds { get; set; }
            public string OutputDirectory { get; set; }
            public string OutputFileName { get; set; }
        }

        public static AppConfiguration Configuration { get; set; }

        private static async Task<int> Main(string[] args)
        {
            var configBuilder = new ConfigurationBuilder();

            configBuilder
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    [$"{nameof(AppConfiguration.PollPeriodMilliseconds)}"] = "10",
                    [$"{nameof(AppConfiguration.KeyframePeriodMilliseconds)}"] = "30000",
                    [$"{nameof(AppConfiguration.OutputDirectory)}"] = "{userprofile}\\Videos\\Spelunky 2",
                    [$"{nameof(AppConfiguration.OutputFileName)}"] = "Spelunky 2 {utcNow:yyyy.MM.dd} - {utcNow:HH.mm.ss.ff}.DVR.log",
                })
                .AddJsonFile("Config.json", true);

            Configuration = configBuilder.Build().Get<AppConfiguration>();

            Console.WriteLine("Searching for Spelunky 2...");
            var process = await FindProcess("Spel2");

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

            var outputPath = Path.Combine(Configuration.OutputDirectory, Configuration.OutputFileName);

            outputPath = FormatPath(outputPath, new Dictionary<string, object>
            {
                ["userprofile"] = Environment.GetEnvironmentVariable("userprofile"),
                ["now"] = DateTime.Now,
                ["utcNow"] = DateTime.UtcNow
            });

            Console.WriteLine($"Creating log: \"{outputPath}\"");

            var outputDir = Path.GetDirectoryName(outputPath);

            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            using var writer = File.CreateText(outputPath);

            var timer = new Stopwatch();
            timer.Start();

            var keyframeTimer = new Stopwatch();
            keyframeTimer.Start();

            var firstKeyframe = true;

            AutoSplitter autoSplitterPrev = default;
            Player playerPrev = default;

            var autoSplitterChangedFields = new List<FieldInfo>();
            var playerChangedFields = new List<FieldInfo>();

            var changesSinceKeyframe = true;

            while (scanner.ReadStructure<AutoSplitter>(autoSplitterAddress, out var autoSplitter) && scanner.ReadStructure<Player>(playerAddress, out var player))
            {
                var now = DateTime.UtcNow;

                if (changesSinceKeyframe && (firstKeyframe || keyframeTimer.ElapsedMilliseconds >= Configuration.KeyframePeriodMilliseconds))
                {
                    keyframeTimer.Restart();
                    firstKeyframe = false;

                    changesSinceKeyframe = false;

                    writer.WriteLine($"keyframe \"{now:O}\":");
                    PrintFields(writer, autoSplitter);
                    PrintFields(writer, player);

                    await writer.FlushAsync();
                }
                else
                {
                    autoSplitterChangedFields.Clear();
                    playerChangedFields.Clear();

                    var autoSplitterChanged = GetChangedFields(autoSplitterChangedFields, autoSplitterPrev, autoSplitter);
                    var playerChanged = GetChangedFields(playerChangedFields, playerPrev, player);

                    if (autoSplitterChanged || playerChanged)
                    {
                        changesSinceKeyframe = true;

                        writer.WriteLine($"delta: \"{now:O}\":");
                        PrintChangedFields(writer, autoSplitterChangedFields, autoSplitter);
                        PrintChangedFields(writer, playerChangedFields, player);
                    }
                }

                autoSplitterPrev = autoSplitter;
                playerPrev = player;

                await Task.Delay(Math.Max(Configuration.PollPeriodMilliseconds - (int) timer.ElapsedMilliseconds, 0));
                timer.Restart();
            }

            return 0;
        }

        private static readonly Regex FormatRegex = new Regex(@"\{\s*(?<name>[A-Za-z0-9_]+)\s*(?::(?<format>[^}]+))?\}");

        private static string FormatPath(string path, IReadOnlyDictionary<string, object> values)
        {
            return FormatRegex.Replace(path, match =>
            {
                var name = match.Groups["name"].Value;

                if (!values.TryGetValue(name, out var value))
                {
                    return "";
                }

                if (value is IFormattable formattable && match.Groups["format"].Success)
                {
                    var format = match.Groups["format"].Value;
                    return formattable.ToString(format, null);
                }

                return value.ToString();
            });
        }

        private static async Task<Process> FindProcess(string name)
        {
            while (true)
            {
                var process = Process.GetProcessesByName(name).FirstOrDefault();

                if (process != null)
                {
                    return process;
                }

                await Task.Delay(500);
            }
        }
    }
}
