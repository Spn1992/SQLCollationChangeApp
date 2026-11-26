using System;
using System.Collections.Generic;
using System.IO;
using MSBuildDiagnosticPatcher.Models;

namespace MSBuildDiagnosticPatcher.Parsers
{
    internal struct PeekableLineReader
    {
        private readonly TextReader _reader;
        private readonly MSBuildLogParser? _msbuildParser;
        private readonly List<CompilerDiagnostic>? _diagnostics;
        private readonly HashSet<(string FilePath, int LineNumber, int ColumnNumber, DiagnosticSeverity Severity, string Code, string Message, string ProjectPath)>? _seenDiagnostics;
        private bool _hasPeeked;
        private string _peekRaw;
        private string _peekClean;

        public PeekableLineReader(
            TextReader reader,
            MSBuildLogParser? msbuildParser = null,
            List<CompilerDiagnostic>? diagnostics = null)
        {
            _reader = reader;
            _msbuildParser = msbuildParser;
            _diagnostics = diagnostics;
            _seenDiagnostics = diagnostics != null
                ? new HashSet<(string FilePath, int LineNumber, int ColumnNumber, DiagnosticSeverity Severity, string Code, string Message, string ProjectPath)>()
                : null;
            _hasPeeked = false;
            _peekRaw = string.Empty;
            _peekClean = string.Empty;
        }

        public bool TryRead(out string rawLine, out string cleanLine)
        {
            if (_hasPeeked)
            {
                rawLine = _peekRaw;
                cleanLine = _peekClean;
                _hasPeeked = false;
                _peekRaw = string.Empty;
                _peekClean = string.Empty;
                return true;
            }

            return ReadNextFromUnderlying(out rawLine, out cleanLine);
        }

        public bool TryPeek(out string rawLine, out string cleanLine)
        {
            if (!_hasPeeked)
            {
                if (!ReadNextFromUnderlying(out _peekRaw, out _peekClean))
                {
                    rawLine = string.Empty;
                    cleanLine = string.Empty;
                    return false;
                }

                _hasPeeked = true;
            }

            rawLine = _peekRaw;
            cleanLine = _peekClean;
            return true;
        }

        public void ConsumePeeked()
        {
            _hasPeeked = false;
            _peekRaw = string.Empty;
            _peekClean = string.Empty;
        }

        private bool ReadNextFromUnderlying(out string rawLine, out string cleanLine)
        {
            string? line = _reader.ReadLine();
            if (line == null)
            {
                rawLine = string.Empty;
                cleanLine = string.Empty;
                return false;
            }

            rawLine = line;
            cleanLine = AnsiCleaner.Clean(line);

            if (_msbuildParser != null && _diagnostics != null && cleanLine.Length > 0)
            {
                var diag = _msbuildParser.ParseCleanedLine(rawLine, cleanLine);
                if (diag != null && (_seenDiagnostics == null || _seenDiagnostics.Add(MSBuildLogParser.CreateDiagnosticKey(diag))))
                {
                    _diagnostics.Add(diag);
                }
            }

            return true;
        }
    }

    public static class LogStreamParser
    {
        public static (List<CompilerDiagnostic> Diagnostics, List<TestFailure> TestFailures) Parse(
            TextReader reader,
            MSBuildLogParser? msbuildParser,
            TestExecutionLogParser? testParser)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            var diagnostics = new List<CompilerDiagnostic>();
            var testFailures = new List<TestFailure>();

            var lineReader = new PeekableLineReader(
                reader,
                msbuildParser,
                msbuildParser != null ? diagnostics : null);

            if (testParser != null)
            {
                testParser.ParseFromReader(ref lineReader, testFailures);
            }
            else
            {
                while (lineReader.TryRead(out _, out _))
                {
                }
            }

            return (diagnostics, testFailures);
        }
    }
}
