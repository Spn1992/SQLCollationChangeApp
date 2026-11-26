using System;
using System.Collections.Generic;
using System.IO;
using MSBuildDiagnosticPatcher.Models;

namespace MSBuildDiagnosticPatcher.Parsers
{
    internal sealed class PeekableLineReader
    {
        private readonly TextReader _reader;
        private readonly MSBuildLogParser? _msbuildParser;
        private readonly List<CompilerDiagnostic>? _diagnostics;
        private readonly HashSet<(string FilePath, int LineNumber, int ColumnNumber, DiagnosticSeverity Severity, string Code, string Message, string ProjectPath)>? _seenDiagnosticKeys;
        private readonly List<(string Raw, string Clean)> _lookahead = new();
        private int _head;

        public PeekableLineReader(
            TextReader reader,
            MSBuildLogParser? msbuildParser = null,
            List<CompilerDiagnostic>? diagnostics = null)
        {
            _reader = reader;
            _msbuildParser = msbuildParser;
            _diagnostics = diagnostics;
            if (msbuildParser != null && diagnostics != null)
            {
                _seenDiagnosticKeys = new HashSet<(string FilePath, int LineNumber, int ColumnNumber, DiagnosticSeverity Severity, string Code, string Message, string ProjectPath)>();
            }
        }

        public bool TryRead(out string rawLine, out string cleanLine)
        {
            if (_head < _lookahead.Count)
            {
                var item = _lookahead[_head++];
                if (_head == _lookahead.Count)
                {
                    _lookahead.Clear();
                    _head = 0;
                }
                rawLine = item.Raw;
                cleanLine = item.Clean;
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
            while (_lookahead.Count - _head <= offset)
            {
                if (!ReadNextFromUnderlying(out string nextRaw, out string nextClean))
                {
                    rawLine = string.Empty;
                    cleanLine = string.Empty;
                    return false;
                }

                _lookahead.Add((nextRaw, nextClean));
            }

            var item = _lookahead[_head + offset];
            rawLine = item.Raw;
            cleanLine = item.Clean;
            return true;
        }

        public void ConsumePeeked()
        {
            if (_head < _lookahead.Count)
            {
                _head++;
                if (_head == _lookahead.Count)
                {
                    _lookahead.Clear();
                    _head = 0;
                }
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
                testParser.ParseFromReader(lineReader, testFailures);
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
