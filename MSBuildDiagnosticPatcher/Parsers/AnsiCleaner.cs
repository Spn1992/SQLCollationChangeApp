using System;
using System.Text.RegularExpressions;

namespace MSBuildDiagnosticPatcher.Parsers
{
    public static class AnsiCleaner
    {
        public static readonly Regex AnsiRegex = new Regex(
            @"\x1B\[[^@-~]*[@-~]",
            RegexOptions.Compiled);

        public static string Clean(string? input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            if (input.IndexOf('\x1B') < 0)
            {
                return input.Trim();
            }

            return AnsiRegex.Replace(input, string.Empty).Trim();
        }
    }
}
