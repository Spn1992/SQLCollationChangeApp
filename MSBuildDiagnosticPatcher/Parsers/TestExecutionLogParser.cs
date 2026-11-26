using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using MSBuildDiagnosticPatcher.Models;

namespace MSBuildDiagnosticPatcher.Parsers
{
    public class TestExecutionLogParser
    {
        // Matches custom runner: "[FAIL] Class.Method - Details", "[FAIL] Class.Method: Details", "FAIL: Class.Method - Details"
        private static readonly Regex CustomRunnerFailRegex = new Regex(
            @"^(?:\[FAIL\]|FAIL:)\s+(?<test>[A-Za-z0-9_.+`]+(?:<[^>]+>)?(?:\([^)]*\))?)(?:\s*(?:-|:)\s*(?<details>.*))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Matches .NET runtime crashes: "Unhandled exception. ..." and "Unhandled Exception: ..."
        private static readonly Regex UnhandledExceptionRegex = new Regex(
            @"^(?:Testhost process exited with error:\s*|Test host process crashed\s*:\s*)?(?<prefix>Unhandled\s+exception(?:\.|:))\s*(?<details>.*)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Matches trailing VSTest duration brackets such as [45 ms], [< 1 ms], [1 s], [1 m 23 s]
        private static readonly Regex DurationBracketRegex = new Regex(
            @"\s*\[(?:<\s*)?\d+(?:[.,]\d+)?\s*(?:ms|s|m|h|d)(?:\s+\d+(?:[.,]\d+)?\s*(?:ms|s|m|h|d))*\]$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Matches custom runner pass/skip or banner boundaries
        private static readonly Regex RunnerBoundaryRegex = new Regex(
            @"^(?:(?:\[PASS\]|PASS:|\[SKIP\]|SKIP:)\s+|===\s+.*===$|={5,}$|-{5,}$|Test Run Finished:|Test Suite Summary:|ALL TESTS PASSED|TEST SUITE FAILED|Build (?:FAILED|succeeded)\.|Time Elapsed\s+|\d+\s+(?:Warning|Error)\(s\)$|\[(?:INFO|WARN|WARNING|ERROR|SUCCESS|PATCH|Perf|Verify|Test Failures|Compiler Errors|Generated Candidate Patches)\])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Matches compiler/MSBuild error/warning lines so they terminate preceding crash/failure blocks cleanly
        private static readonly Regex CompilerDiagnosticBoundaryRegex = new Regex(
            @":\s*(?:fatal error|error|warning)\s+[A-Za-z]{2,8}\d{4,5}[A-Za-z]?\s*:",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Extracts target method from a stack frame line: "at Namespace.Class.Method(..."
        private static readonly Regex StackFrameTargetRegex = new Regex(
            @"^at\s+(?<target>[^\(\s]+)",
            RegexOptions.Compiled);

        // Normalizes compiler-generated async/closure targets such as Namespace.Class.<Method>d__5.MoveNext
        private static readonly Regex GeneratedMemberRegex = new Regex(
            @"^(?<owner>.+?)\.(?:<>c(?:__DisplayClass\d+_\d+)?\.)?<+(?<method>[A-Za-z_][A-Za-z0-9_]*)>",
            RegexOptions.Compiled);

        // Strips leading "System.SomeException: " only when immediately wrapping "Assertion Failed:"
        private static readonly Regex LeadingExceptionBeforeAssertionRegex = new Regex(
            @"^(?:[A-Za-z_][A-Za-z0-9_.]*Exception\s*:\s*)+(?=Assertion\s+Failed\s*:)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private enum VstestSection
        {
            None,
            ErrorMessage,
            StackTrace,
            Other
        }

        public List<TestFailure> ParseText(string logContent)
        {
            if (string.IsNullOrWhiteSpace(logContent))
            {
                return new List<TestFailure>();
            }

            using var reader = new StringReader(logContent);
            return ParseStream(reader);
        }

        public List<TestFailure> ParseStream(TextReader reader)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            return LogStreamParser.Parse(reader, msbuildParser: null, this).TestFailures;
        }

        internal void ParseFromReader(ref PeekableLineReader lineReader, List<TestFailure> results)
        {
            bool insidePatcherSummaryBanner = false;
            int bannerDividerCount = 0;

            while (lineReader.TryRead(out string rawLine, out string cleanLine))
            {
                if (cleanLine.Length == 0)
                {
                    continue;
                }

                if (cleanLine.Equals("MSBuild Log Diagnostic & Patch Generator Report", StringComparison.Ordinal))
                {
                    insidePatcherSummaryBanner = true;
                    bannerDividerCount = 0;
                    continue;
                }

                if (insidePatcherSummaryBanner)
                {
                    if (cleanLine.StartsWith("==========", StringComparison.Ordinal))
                    {
                        bannerDividerCount++;
                        if (bannerDividerCount >= 2)
                        {
                            insidePatcherSummaryBanner = false;
                        }
                    }
                    continue;
                }

                char firstChar = cleanLine[0];

                // 1. Custom runner pattern: "[FAIL] ..." or "FAIL: ..."
                if (firstChar == '[' || firstChar == 'F' || firstChar == 'f')
                {
                    var customMatch = CustomRunnerFailRegex.Match(cleanLine);
                    if (customMatch.Success)
                    {
                        string fullName = customMatch.Groups["test"].Value.Trim();
                        string details = customMatch.Groups["details"].Success
                            ? customMatch.Groups["details"].Value.Trim()
                            : string.Empty;

                        var (className, methodName) = SplitTestName(fullName);
                        var (message, stackTrace) = CollectExceptionMessageAndStackTrace(ref lineReader, details, isUnhandledCrash: false);

                        results.Add(new TestFailure
                        {
                            TestClass = className,
                            TestMethod = methodName,
                            AssertionFailureMessage = message,
                            StackTrace = stackTrace,
                            RawLine = rawLine
                        });
                        continue;
                    }
                }

                // 2. VSTest / dotnet test pattern: "Failed TestClass.TestMethod [12 ms]" or parameterized / [< 1 ms]
                if ((firstChar == 'F' || firstChar == 'f') && TryParseDotnetTestFailedHeader(cleanLine, out string vstestFullName))
                {
                    var failure = ParseDotnetTestFailure(ref lineReader, rawLine, vstestFullName);
                    results.Add(failure);
                    continue;
                }

                // 3. Unhandled .NET runtime crash pattern: "Unhandled exception." or "Unhandled Exception:"
                if (firstChar == 'U' || firstChar == 'u' || firstChar == 'T' || firstChar == 't')
                {
                    var crashMatch = UnhandledExceptionRegex.Match(cleanLine);
                    if (crashMatch.Success)
                    {
                        string prefix = crashMatch.Groups["prefix"].Value.Trim();
                        string details = crashMatch.Groups["details"].Value.Trim();
                        string initialHeader = string.IsNullOrEmpty(details) ? prefix : $"{prefix} {details}";

                        var (message, stackTrace) = CollectExceptionMessageAndStackTrace(ref lineReader, initialHeader, isUnhandledCrash: true);
                        var (crashClass, crashMethod) = ResolveCrashOrigin(stackTrace, details);

                        results.Add(new TestFailure
                        {
                            TestClass = crashClass,
                            TestMethod = crashMethod,
                            AssertionFailureMessage = message,
                            StackTrace = stackTrace,
                            RawLine = rawLine
                        });
                        continue;
                    }
                }
            }
        }

        private static TestFailure ParseDotnetTestFailure(ref PeekableLineReader lineReader, string rawHeaderLine, string fullName)
        {
            var (className, methodName) = SplitTestName(fullName);
            var errorLines = new List<string>();
            var stackLines = new List<string>();
            VstestSection section = VstestSection.None;

            while (lineReader.TryPeekAt(0, out _, out string lookAhead))
            {
                if (IsVstestBlockBoundary(lookAhead))
                {
                    break;
                }

                if (lookAhead.StartsWith("Error Message:", StringComparison.OrdinalIgnoreCase))
                {
                    section = VstestSection.ErrorMessage;
                    string inlineMsg = lookAhead.Substring("Error Message:".Length).Trim();
                    if (!string.IsNullOrEmpty(inlineMsg))
                    {
                        errorLines.Add(inlineMsg);
                    }
                    lineReader.ConsumePeeked(1);
                    continue;
                }

                if (lookAhead.StartsWith("Stack Trace:", StringComparison.OrdinalIgnoreCase))
                {
                    section = VstestSection.StackTrace;
                    string inlineFrame = lookAhead.Substring("Stack Trace:".Length).Trim();
                    if (!string.IsNullOrEmpty(inlineFrame))
                    {
                        stackLines.Add(inlineFrame);
                    }
                    lineReader.ConsumePeeked(1);
                    continue;
                }

                if (lookAhead.StartsWith("Standard Output Messages:", StringComparison.OrdinalIgnoreCase) ||
                    lookAhead.StartsWith("Standard Error Messages:", StringComparison.OrdinalIgnoreCase))
                {
                    section = VstestSection.Other;
                    lineReader.ConsumePeeked(1);
                    continue;
                }

                if (section == VstestSection.ErrorMessage)
                {
                    errorLines.Add(lookAhead);
                    lineReader.ConsumePeeked(1);
                    continue;
                }

                if (section == VstestSection.StackTrace)
                {
                    if (string.IsNullOrWhiteSpace(lookAhead))
                    {
                        lineReader.ConsumePeeked(1);
                        continue;
                    }

                    if (IsStackFrameOrBoundaryLine(lookAhead) || IsInnerExceptionHeader(lookAhead))
                    {
                        stackLines.Add(lookAhead);
                        lineReader.ConsumePeeked(1);
                        continue;
                    }

                    break;
                }

                lineReader.ConsumePeeked(1);
            }

            TrimBlankEdges(errorLines);
            string errorMessage = string.Join("\n", errorLines);
            string stackTrace = string.Join("\n", stackLines);

            return new TestFailure
            {
                TestClass = className,
                TestMethod = methodName,
                AssertionFailureMessage = string.IsNullOrWhiteSpace(errorMessage) ? "Test execution failed." : errorMessage,
                StackTrace = stackTrace,
                RawLine = rawHeaderLine
            };
        }

        private static (string Message, string StackTrace) CollectExceptionMessageAndStackTrace(
            ref PeekableLineReader lineReader,
            string initialDetails,
            bool isUnhandledCrash)
        {
            var messageLines = new List<string>();
            var stackLines = new List<string>();

            // Look ahead to determine the extent of the candidate block and whether stack/inner markers exist
            int scanCount = 0;
            bool blockHasStackOrInnerMarkers = false;
            while (lineReader.TryPeekAt(scanCount, out _, out string peek))
            {
                if (string.IsNullOrEmpty(peek))
                {
                    if (NextNonEmptyContinuesException(ref lineReader, scanCount + 1))
                    {
                        scanCount++;
                        continue;
                    }
                    break;
                }

                if (IsNewLogEntryOrBoundary(peek))
                {
                    break;
                }

                if (IsStackFrameOrBoundaryLine(peek) || IsInnerExceptionHeader(peek))
                {
                    blockHasStackOrInnerMarkers = true;
                }

                scanCount++;
            }

            if (!string.IsNullOrWhiteSpace(initialDetails))
            {
                string normalizedFirst = isUnhandledCrash
                    ? initialDetails.Trim()
                    : NormalizeFirstFailureLine(initialDetails);
                messageLines.Add(normalizedFirst);
            }

            bool inMessageSection = true;
            int consumed = 0;
            while (consumed < scanCount && lineReader.TryPeekAt(0, out _, out string clean))
            {
                if (string.IsNullOrEmpty(clean))
                {
                    lineReader.ConsumePeeked(1);
                    consumed++;
                    continue;
                }

                if (IsInnerExceptionHeader(clean))
                {
                    messageLines.Add(clean);
                    if (stackLines.Count > 0)
                    {
                        stackLines.Add(clean);
                    }
                    inMessageSection = true;
                    lineReader.ConsumePeeked(1);
                    consumed++;
                    continue;
                }

                if (IsStackFrameOrBoundaryLine(clean))
                {
                    inMessageSection = false;
                    stackLines.Add(clean);
                    lineReader.ConsumePeeked(1);
                    consumed++;
                    continue;
                }

                if (inMessageSection)
                {
                    if (blockHasStackOrInnerMarkers ||
                        isUnhandledCrash ||
                        string.IsNullOrWhiteSpace(initialDetails) ||
                        LooksLikeAssertionContinuation(clean))
                    {
                        messageLines.Add(clean);
                        lineReader.ConsumePeeked(1);
                        consumed++;
                        continue;
                    }
                }

                break;
            }

            string finalMessage = messageLines.Count > 0
                ? string.Join("\n", messageLines)
                : (isUnhandledCrash ? "Unhandled runtime exception." : "Assertion failed or unhandled exception.");

            return (finalMessage, string.Join("\n", stackLines));
        }

        private static bool NextNonEmptyContinuesException(ref PeekableLineReader lineReader, int startOffset)
        {
            int k = startOffset;
            while (lineReader.TryPeekAt(k, out _, out string peek))
            {
                if (string.IsNullOrEmpty(peek))
                {
                    k++;
                    continue;
                }

                return IsStackFrameOrBoundaryLine(peek) || IsInnerExceptionHeader(peek);
            }
            return false;
        }

        private static bool LooksLikeAssertionContinuation(string cleanLine)
        {
            return cleanLine.StartsWith("Expected:", StringComparison.OrdinalIgnoreCase) ||
                   cleanLine.StartsWith("Actual:", StringComparison.OrdinalIgnoreCase) ||
                   cleanLine.StartsWith("But was:", StringComparison.OrdinalIgnoreCase) ||
                   cleanLine.StartsWith("Assert.", StringComparison.OrdinalIgnoreCase) ||
                   cleanLine.StartsWith("Assertion ", StringComparison.OrdinalIgnoreCase) ||
                   cleanLine.StartsWith("Message:", StringComparison.OrdinalIgnoreCase) ||
                   cleanLine.StartsWith("Difference:", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStackFrameOrBoundaryLine(string cleanLine)
        {
            return cleanLine.StartsWith("at ", StringComparison.Ordinal) ||
                   cleanLine.StartsWith("--- End of ", StringComparison.OrdinalIgnoreCase) ||
                   cleanLine.StartsWith("----- Inner Stack Trace", StringComparison.OrdinalIgnoreCase) ||
                   cleanLine == "<---";
        }

        private static bool IsInnerExceptionHeader(string cleanLine)
        {
            return cleanLine.StartsWith("--->", StringComparison.Ordinal) ||
                   cleanLine.StartsWith("---- ", StringComparison.Ordinal);
        }

        private static bool IsNewLogEntryOrBoundary(string cleanLine)
        {
            if (string.IsNullOrEmpty(cleanLine))
            {
                return false;
            }

            if (IsStackFrameOrBoundaryLine(cleanLine) || IsInnerExceptionHeader(cleanLine))
            {
                return false;
            }

            if (RunnerBoundaryRegex.IsMatch(cleanLine) ||
                CustomRunnerFailRegex.IsMatch(cleanLine) ||
                UnhandledExceptionRegex.IsMatch(cleanLine) ||
                TryParseDotnetTestFailedHeader(cleanLine, out _) ||
                IsVstestSummaryOrOutcomeLine(cleanLine) ||
                CompilerDiagnosticBoundaryRegex.IsMatch(cleanLine))
            {
                return true;
            }

            return false;
        }

        private static bool IsVstestBlockBoundary(string cleanLine)
        {
            if (string.IsNullOrEmpty(cleanLine))
            {
                return false;
            }

            return TryParseDotnetTestFailedHeader(cleanLine, out _) ||
                   IsVstestSummaryOrOutcomeLine(cleanLine) ||
                   CustomRunnerFailRegex.IsMatch(cleanLine) ||
                   UnhandledExceptionRegex.IsMatch(cleanLine) ||
                   RunnerBoundaryRegex.IsMatch(cleanLine) ||
                   CompilerDiagnosticBoundaryRegex.IsMatch(cleanLine);
        }

        private static bool IsVstestSummaryOrOutcomeLine(string cleanLine)
        {
            return cleanLine.StartsWith("Passed ", StringComparison.OrdinalIgnoreCase) ||
                   cleanLine.StartsWith("Skipped ", StringComparison.OrdinalIgnoreCase) ||
                   cleanLine.StartsWith("Total tests:", StringComparison.OrdinalIgnoreCase) ||
                   cleanLine.StartsWith("Failed!", StringComparison.OrdinalIgnoreCase) ||
                   cleanLine.StartsWith("Passed!", StringComparison.OrdinalIgnoreCase) ||
                   cleanLine.StartsWith("Test Run Failed.", StringComparison.OrdinalIgnoreCase) ||
                   cleanLine.StartsWith("Test Run Successful.", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseDotnetTestFailedHeader(string cleanLine, out string fullTestName)
        {
            fullTestName = string.Empty;
            if (!cleanLine.StartsWith("Failed ", StringComparison.OrdinalIgnoreCase) || cleanLine.Length <= 7)
            {
                return false;
            }

            string rest = cleanLine.Substring(7).Trim();
            if (rest.Length == 0)
            {
                return false;
            }

            bool hasDuration = false;
            if (rest.EndsWith("]", StringComparison.Ordinal))
            {
                var durMatch = DurationBracketRegex.Match(rest);
                if (durMatch.Success)
                {
                    hasDuration = true;
                    rest = rest.Substring(0, durMatch.Index).TrimEnd();
                }
            }

            if (rest.Length == 0)
            {
                return false;
            }

            int openParen = rest.IndexOf('(');
            if (openParen > 0)
            {
                if (!rest.EndsWith(")", StringComparison.Ordinal))
                {
                    return false;
                }

                string identifierPart = rest.Substring(0, openParen).TrimEnd();
                if (!IsValidTestIdentifier(identifierPart))
                {
                    return false;
                }

                fullTestName = rest;
                return true;
            }

            if (openParen < 0 && IsValidTestIdentifier(rest) && (rest.Contains('.') || hasDuration))
            {
                fullTestName = rest;
                return true;
            }

            return false;
        }

        private static bool IsValidTestIdentifier(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                return false;
            }

            char first = identifier[0];
            if (!char.IsLetter(first) && first != '_')
            {
                return false;
            }

            if (identifier[identifier.Length - 1] == '.')
            {
                return false;
            }

            int genericDepth = 0;
            for (int i = 0; i < identifier.Length; i++)
            {
                char c = identifier[i];
                if (c == '<')
                {
                    genericDepth++;
                }
                else if (c == '>')
                {
                    genericDepth--;
                    if (genericDepth < 0)
                    {
                        return false;
                    }
                }
                else if (genericDepth == 0)
                {
                    if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '+' || c == '`'))
                    {
                        return false;
                    }
                }
            }

            return genericDepth == 0;
        }

        private static (string ClassName, string MethodName) SplitTestName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return (string.Empty, string.Empty);
            }

            int parenIdx = fullName.IndexOf('(');
            int searchLimit = parenIdx >= 0 ? parenIdx : fullName.Length;

            int genericDepth = 0;
            int splitDot = -1;
            for (int i = 0; i < searchLimit; i++)
            {
                char c = fullName[i];
                if (c == '<')
                {
                    genericDepth++;
                }
                else if (c == '>' && genericDepth > 0)
                {
                    genericDepth--;
                }
                else if (c == '.' && genericDepth == 0)
                {
                    splitDot = i;
                }
            }

            if (splitDot > 0 && splitDot < fullName.Length - 1)
            {
                return (fullName.Substring(0, splitDot).Trim(), fullName.Substring(splitDot + 1).Trim());
            }

            return (fullName.Trim(), string.Empty);
        }

        private static string NormalizeFirstFailureLine(string rawDetails)
        {
            if (string.IsNullOrWhiteSpace(rawDetails))
            {
                return string.Empty;
            }

            string trimmed = rawDetails.Trim();
            string stripped = LeadingExceptionBeforeAssertionRegex.Replace(trimmed, string.Empty);
            return stripped.Trim();
        }

        private static (string ClassName, string MethodName) ResolveCrashOrigin(string stackTrace, string headerDetails)
        {
            if (!string.IsNullOrWhiteSpace(stackTrace))
            {
                (string ClassName, string MethodName)? fallbackFrame = null;
                var stackLines = stackTrace.Split('\n');

                foreach (string line in stackLines)
                {
                    var match = StackFrameTargetRegex.Match(line.Trim());
                    if (!match.Success)
                    {
                        continue;
                    }

                    string rawTarget = match.Groups["target"].Value.Trim();
                    var parsed = ParseStackFrameTarget(rawTarget);
                    if (string.IsNullOrWhiteSpace(parsed.ClassName))
                    {
                        continue;
                    }

                    fallbackFrame ??= parsed;

                    bool isFrameworkFrame =
                        parsed.ClassName.StartsWith("System.", StringComparison.Ordinal) ||
                        parsed.ClassName.Equals("System", StringComparison.Ordinal) ||
                        parsed.ClassName.StartsWith("Microsoft.", StringComparison.Ordinal) ||
                        parsed.ClassName.EndsWith(".SynchronousSynchronizationContext", StringComparison.Ordinal);

                    if (!isFrameworkFrame)
                    {
                        return parsed;
                    }
                }

                if (fallbackFrame.HasValue)
                {
                    return fallbackFrame.Value;
                }
            }

            string exceptionType = ExtractLeadingExceptionType(headerDetails);
            return ("UnhandledException", string.IsNullOrEmpty(exceptionType) ? "ProcessCrash" : exceptionType);
        }

        private static (string ClassName, string MethodName) ParseStackFrameTarget(string target)
        {
            var generatedMatch = GeneratedMemberRegex.Match(target);
            if (generatedMatch.Success)
            {
                return (generatedMatch.Groups["owner"].Value, generatedMatch.Groups["method"].Value);
            }

            return SplitTestName(target);
        }

        private static string ExtractLeadingExceptionType(string details)
        {
            if (string.IsNullOrWhiteSpace(details))
            {
                return string.Empty;
            }

            int colonIdx = details.IndexOf(':');
            string candidate = (colonIdx > 0 ? details.Substring(0, colonIdx) : details).Trim().TrimEnd('.');
            if (candidate.Length > 0 && !candidate.Contains(' '))
            {
                return candidate;
            }

            return string.Empty;
        }

        private static void TrimBlankEdges(List<string> lines)
        {
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
            {
                lines.RemoveAt(0);
            }

            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
            {
                lines.RemoveAt(lines.Count - 1);
            }
        }
    }
}
