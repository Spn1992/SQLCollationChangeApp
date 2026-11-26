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
        private readonly HashSet<(string FilePath, int LineNumber, int ColumnNumber, DiagnosticSeverity Severity, string Code, string Message, string ProjectPath)>? _seenDiagnosticKeys;
        private List<(string Raw, string Clean)>? _lookahead;
        private int _lookaheadStart;

        public PeekableLineReader(
            TextReader reader,
            MSBuildLogParser? msbuildParser = null,
            List<CompilerDiagnostic>? diagnostics = null)
        {
            _reader = reader;
            _msbuildParser = msbuildParser;
            _diagnostics = diagnostics;
            _seenDiagnosticKeys = msbuildParser != null && diagnostics != null
                ? new HashSet<(string, int, int, DiagnosticSeverity, string, string, string)>()
                : null;
            _lookahead = null;
            _lookaheadStart = 0;
        }

        public bool TryRead(out string rawLine, out string cleanLine)
        {
            if (_lookahead != null && _lookaheadStart < _lookahead.Count)
            {
                (rawLine, cleanLine) = _lookahead[_lookaheadStart++];
                if (_lookaheadStart >= _lookahead.Count)
                {
                    _lookahead.Clear();
                    _lookaheadStart = 0;
                }
                return true;
            }

            return ReadNextFromUnderlying(out rawLine, out cleanLine);
        }

        public bool TryPeek(out string rawLine, out string cleanLine)
        {
            return TryPeekAt(0, out rawLine, out cleanLine);
        }

        public bool TryPeekAt(int offset, out string rawLine, out string cleanLine)
        {
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            _lookahead ??= new List<(string Raw, string Clean)>();
            while (_lookahead.Count - _lookaheadStart <= offset)
            {
                if (!ReadNextFromUnderlying(out string nextRaw, out string nextClean))
                {
                    rawLine = string.Empty;
                    cleanLine = string.Empty;
                    return false;
                }

                _lookahead.Add((nextRaw, nextClean));
            }

            (rawLine, cleanLine) = _lookahead[_lookaheadStart + offset];
            return true;
        }

        public void ConsumePeeked(int count = 1)
        {
            if (_lookahead == null || count <= 0)
            {
                return;
            }

            int available = _lookahead.Count - _lookaheadStart;
            if (count >= available)
            {
                _lookahead.Clear();
                _lookaheadStart = 0;
            }
            else
            {
                _lookaheadStart += count;
            }
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
                if (diag != null && (_seenDiagnosticKeys == null || _seenDiagnosticKeys.Add(MSBuildLogParser.CreateDiagnosticKey(diag))))
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
