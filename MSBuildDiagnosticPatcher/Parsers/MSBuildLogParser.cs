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
            @"^(?>\d+(?::\d+)?>\s*|)(?<file>(?:[a-zA-Z]:)?[a-zA-Z0-9_\-./\\()@+~][a-zA-Z0-9_\-./\\ ()@+~]*?)\((?<line>\d+)(?:,(?<col>\d+)(?:,\d+){0,2})?\):\s*(?:(?!fatal\s+error\b)[a-zA-Z][a-zA-Z0-9_\-]*\s+){0,3}(?<severity>fatal error|error|warning|info)\s+(?<code>[A-Za-z]{1,8}\d{4,5}[a-zA-Z]?):\s*(?<message>.*?)(?:\s+\[(?<project>[^\]]+)\])?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex MSBuildErrorRegex = new Regex(
            @"^(?>\d+(?::\d+)?>\s*|)(?:(?!(?:[a-zA-Z][a-zA-Z0-9_\-]*\s+){0,3}(?:fatal error|error|warning|info|message)\s+[A-Za-z]{1,8}\d{3,5}[a-zA-Z]?\s*:)(?<file>(?:[a-zA-Z]:)?[a-zA-Z0-9_\-./\\()@+~][a-zA-Z0-9_\-./\\ ()@+~]*?)\s*:\s*)?(?:(?!fatal\s+error\b)[a-zA-Z][a-zA-Z0-9_\-]*\s+){0,3}(?<severity>fatal error|error|warning|info)\s+(?<code>(?:CS|BC|FS|MSB|NETSDK|NU|MC|CA|SYSLIB)\d{4,5}[a-zA-Z]?):\s*(?<message>.*?)(?:\s+\[(?<project>[^\]]+)\])?\s*$",
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

        internal static (string FilePath, int LineNumber, int ColumnNumber, DiagnosticSeverity Severity, string Code, string Message, string ProjectPath) CreateDiagnosticKey(CompilerDiagnostic diagnostic)
        {
            return (
                NormalizePath(diagnostic.FilePath),
                diagnostic.LineNumber,
                diagnostic.ColumnNumber,
                diagnostic.Severity,
                (diagnostic.Code ?? string.Empty).Trim().ToUpperInvariant(),
                (diagnostic.Message ?? string.Empty).Trim(),
                NormalizePath(diagnostic.ProjectPath));
        }

        private static string NormalizePath(string? path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Trim().Replace('\\', '/');
        }

        public CompilerDiagnostic? ParseLine(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                return null;
            }

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
            int searchFrom = 0;
            while (searchFrom < line.Length)
            {
                int codeColon = line.IndexOf(':', searchFrom);
                if (codeColon < 0)
                {
                    return false;
                }

                if (IsDiagnosticCodeBeforeColon(line, codeColon))
                {
                    return true;
                }

                searchFrom = codeColon + 1;
            }

            return false;
        }

        private static bool IsDiagnosticCodeBeforeColon(string line, int codeColon)
        {
            int p = codeColon - 1;
            if (p < 5)
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
            while (p >= 0 && char.IsDigit(line[p]))
            {
                digits++;
                p--;
            }

            if (digits < 4 || digits > 5 || p < 1)
            {
                return false;
            }

            ReadOnlySpan<char> prefixSpan = line.AsSpan(0, p + 1);
            int codeStart;
            if (prefixSpan.EndsWith("CS", StringComparison.OrdinalIgnoreCase) ||
                prefixSpan.EndsWith("BC", StringComparison.OrdinalIgnoreCase) ||
                prefixSpan.EndsWith("FS", StringComparison.OrdinalIgnoreCase) ||
                prefixSpan.EndsWith("NU", StringComparison.OrdinalIgnoreCase) ||
                prefixSpan.EndsWith("MC", StringComparison.OrdinalIgnoreCase) ||
                prefixSpan.EndsWith("CA", StringComparison.OrdinalIgnoreCase))
            {
                codeStart = p - 1;
            }
            else if (prefixSpan.EndsWith("MSB", StringComparison.OrdinalIgnoreCase))
            {
                codeStart = p - 2;
            }
            else if (prefixSpan.EndsWith("NETSDK", StringComparison.OrdinalIgnoreCase) ||
                     prefixSpan.EndsWith("SYSLIB", StringComparison.OrdinalIgnoreCase))
            {
                codeStart = p - 5;
            }
            else
            {
                return false;
            }

            int ws = codeStart - 1;
            if (ws <= 0 || !char.IsWhiteSpace(line[ws]))
            {
                return false;
            }

            while (ws >= 0 && char.IsWhiteSpace(line[ws]))
            {
                ws--;
            }

            return ws >= 0 && char.IsLetter(line[ws]);
        }

        private static CompilerDiagnostic? CreateFromMatch(Match match, string rawLine)
        {
            string severityStr = match.Groups["severity"].Value.Trim();
            DiagnosticSeverity severity;
            if (severityStr.Equals("warning", StringComparison.OrdinalIgnoreCase))
            {
                severity = DiagnosticSeverity.Warning;
            }
            else if (severityStr.Equals("info", StringComparison.OrdinalIgnoreCase))
            {
                severity = DiagnosticSeverity.Info;
            }
            else if (severityStr.Equals("error", StringComparison.OrdinalIgnoreCase) ||
                     severityStr.Equals("fatal error", StringComparison.OrdinalIgnoreCase))
            {
                severity = DiagnosticSeverity.Error;
            }
            else
            {
                return null;
            }

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
