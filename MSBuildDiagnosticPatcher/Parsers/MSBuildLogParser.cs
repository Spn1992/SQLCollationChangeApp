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
            var results = new List<CompilerDiagnostic>();
            if (string.IsNullOrWhiteSpace(logContent))
            {
                return results;
            }

            var seenKeys = new HashSet<(string FilePath, int LineNumber, int ColumnNumber, DiagnosticSeverity Severity, string Code, string Message, string ProjectPath)>();

            using var reader = new StringReader(logContent);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var diag = ParseLine(line);
                if (diag != null && seenKeys.Add(CreateDiagnosticKey(diag)))
                {
                    results.Add(diag);
                }
            }

            return results;
        }

        private static (string FilePath, int LineNumber, int ColumnNumber, DiagnosticSeverity Severity, string Code, string Message, string ProjectPath) CreateDiagnosticKey(CompilerDiagnostic diagnostic)
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

            // Clean ANSI escape sequences
            string cleanLine = AnsiCleaner.Clean(rawLine);

            var match = CompilerDiagnosticRegex.Match(cleanLine);
            if (match.Success)
            {
                return CreateFromMatch(match, rawLine);
            }

            var msbMatch = MSBuildErrorRegex.Match(cleanLine);
            if (msbMatch.Success)
            {
                return CreateFromMatch(msbMatch, rawLine);
            }

            return null;
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
