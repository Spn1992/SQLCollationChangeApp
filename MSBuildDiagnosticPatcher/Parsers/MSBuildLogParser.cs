using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using MSBuildDiagnosticPatcher.Models;

namespace MSBuildDiagnosticPatcher.Parsers
{
    public class MSBuildLogParser
    {
        private static readonly Regex CompilerDiagnosticRegex = new Regex(
            @"^(?<file>[a-zA-Z0-9_\-./\\: ]+(?:\.cs|\.xaml|\.props|\.targets)?)\((?<line>\d+)(?:,(?<col>\d+))?\):\s*(?<severity>fatal error|error|warning|info)\s+(?<code>[A-Za-z]{1,8}\d{4,5}[a-zA-Z]?):\s*(?<message>.*?)(?:\s+\[(?<project>[^\]]+)\])?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex MSBuildErrorRegex = new Regex(
            @"^(?<file>[a-zA-Z0-9_\-./\\: ]+?)\s*:\s*(?<severity>[a-zA-Z]+(?:\s+[a-zA-Z]+)*)\s+(?<code>(?:CS|BC|FS|MSB|NETSDK|NU)\d{4,5}[a-zA-Z]?):\s*(?<message>.*?)(?:\s+\[(?<project>[^\]]+)\])?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public List<CompilerDiagnostic> ParseText(string logContent)
        {
            if (string.IsNullOrWhiteSpace(logContent))
            {
                return new List<CompilerDiagnostic>();
            }

            using var reader = new StringReader(logContent);
            return ParseStream(reader);
        }

        public List<CompilerDiagnostic> ParseStream(TextReader reader)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            return LogStreamParser.Parse(reader, this, testParser: null).Diagnostics;
        }

        public CompilerDiagnostic? ParseLine(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                return null;
            }

            // Clean ANSI escape sequences
            string cleanLine = AnsiCleaner.Clean(rawLine);
            return ParseCleanedLine(rawLine, cleanLine);
        }

        internal CompilerDiagnostic? ParseCleanedLine(string rawLine, string cleanLine)
        {
            if (cleanLine.Length == 0)
            {
                return null;
            }

            if (cleanLine.Contains("):", StringComparison.Ordinal))
            {
                var match = CompilerDiagnosticRegex.Match(cleanLine);
                if (match.Success)
                {
                    return CreateFromMatch(match, rawLine);
                }
            }

            if (HasMSBuildDiagnosticCode(cleanLine))
            {
                var msbMatch = MSBuildErrorRegex.Match(cleanLine);
                if (msbMatch.Success)
                {
                    return CreateFromMatch(msbMatch, rawLine);
                }
            }

            return null;
        }

        private static bool HasMSBuildDiagnosticCode(string line)
        {
            int firstColon = line.IndexOf(':');
            if (firstColon < 0)
            {
                return false;
            }

            int searchFrom = firstColon + 1;
            while (searchFrom < line.Length)
            {
                int codeColon = line.IndexOf(':', searchFrom);
                if (codeColon < 0)
                {
                    return false;
                }

                if (IsDiagnosticCodeBeforeColon(line, firstColon, codeColon))
                {
                    return true;
                }

                searchFrom = codeColon + 1;
            }

            return false;
        }

        private static bool IsDiagnosticCodeBeforeColon(string line, int firstColon, int codeColon)
        {
            int p = codeColon - 1;
            if (p <= firstColon)
            {
                return false;
            }

            // Optional trailing letter [a-zA-Z]?
            if (char.IsLetter(line[p]))
            {
                p--;
            }

            // 4 or 5 digits \d{4,5}
            int digits = 0;
            while (p > firstColon && char.IsDigit(line[p]))
            {
                digits++;
                p--;
            }

            if (digits < 4 || digits > 5)
            {
                return false;
            }

            // Match CS, BC, FS, MSB, NETSDK, and NU code prefixes case-insensitively
            ReadOnlySpan<char> prefixSpan = line.AsSpan(0, p + 1);
            int codeStart;
            if (prefixSpan.EndsWith("CS", StringComparison.OrdinalIgnoreCase) ||
                prefixSpan.EndsWith("BC", StringComparison.OrdinalIgnoreCase) ||
                prefixSpan.EndsWith("FS", StringComparison.OrdinalIgnoreCase) ||
                prefixSpan.EndsWith("NU", StringComparison.OrdinalIgnoreCase))
            {
                codeStart = p - 1;
            }
            else if (prefixSpan.EndsWith("MSB", StringComparison.OrdinalIgnoreCase))
            {
                codeStart = p - 2;
            }
            else if (prefixSpan.EndsWith("NETSDK", StringComparison.OrdinalIgnoreCase))
            {
                codeStart = p - 5;
            }
            else
            {
                return false;
            }

            // Require whitespace before code and any severity word after an earlier colon
            int ws = codeStart - 1;
            if (ws <= firstColon || !char.IsWhiteSpace(line[ws]))
            {
                return false;
            }

            while (ws > firstColon && char.IsWhiteSpace(line[ws]))
            {
                ws--;
            }

            return ws > firstColon && char.IsLetter(line[ws]);
        }

        private static CompilerDiagnostic CreateFromMatch(Match match, string rawLine)
        {
            string severityStr = match.Groups["severity"].Value;
            DiagnosticSeverity severity = severityStr.Equals("warning", StringComparison.OrdinalIgnoreCase)
                ? DiagnosticSeverity.Warning
                : severityStr.Equals("info", StringComparison.OrdinalIgnoreCase)
                    ? DiagnosticSeverity.Info
                    : DiagnosticSeverity.Error;

            int line = int.TryParse(match.Groups["line"].Value, out int l) ? l : 0;
            int col = int.TryParse(match.Groups["col"].Value, out int c) ? c : 0;

            return new CompilerDiagnostic
            {
                FilePath = match.Groups["file"].Value.Trim(),
                LineNumber = line,
                ColumnNumber = col,
                Severity = severity,
                Code = match.Groups["code"].Value.Trim().ToUpperInvariant(),
                Message = match.Groups["message"].Value.Trim(),
                ProjectPath = match.Groups["project"].Value.Trim(),
                RawLine = rawLine
            };
        }
    }
}
