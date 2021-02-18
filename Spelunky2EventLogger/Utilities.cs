using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Spelunky2EventLogger
{
    public static class Utilities
    {
        private static readonly Regex FormatRegex = new Regex(@"\{\s*(?<name>[A-Za-z0-9_]+)\s*(?::(?<format>[^}]+))?\}");

        public static string FormatPath(string path, IReadOnlyDictionary<string, object> values = null)
        {
            var extraValues = new Dictionary<string, object>
            {
                ["userprofile"] = Environment.GetEnvironmentVariable("userprofile")
            };

            return FormatRegex.Replace(path, match =>
            {
                var name = match.Groups["name"].Value;

                object value = null;
                if (!(values?.TryGetValue(name, out value) ?? false) && !extraValues.TryGetValue(name, out value))
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

        public static async Task<Process> FindProcess(string name)
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
