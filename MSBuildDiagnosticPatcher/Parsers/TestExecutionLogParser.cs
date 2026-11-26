using System;
using System.Collections.Generic;
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
            while (lineReader.TryRead(out string rawLine, out string cleanLine))
            {
                if (cleanLine.Length < 5)
                {
                    continue;
                }

                bool isBracketFail = cleanLine.StartsWith("[FAIL]", StringComparison.OrdinalIgnoreCase);
                bool isColonFail = !isBracketFail && cleanLine.StartsWith("FAIL:", StringComparison.OrdinalIgnoreCase);

                if (isBracketFail || isColonFail)
                {
                    // 1. Try Custom Runner pattern: "FAIL: MainViewModelTestsRunner - System.Exception: Assertion Failed: ..."
                    var customMatch = CustomRunnerFailRegex.Match(cleanLine);
                    if (customMatch.Success)
                    {
                        string fullName = customMatch.Groups["test"].Value.Trim();
                        string details = customMatch.Groups["details"].Success ? customMatch.Groups["details"].Value.Trim() : string.Empty;

                        var (className, methodName) = SplitTestName(fullName);
                        var failure = new TestFailure
                        {
                            TestClass = className,
                            TestMethod = methodName,
                            AssertionFailureMessage = ExtractAssertionMessage(details),
                            RawLine = rawLine,
                            StackTrace = CollectStackTrace(ref lineReader)
                        };

                        results.Add(failure);
                        continue;
                    }

                    // 3. Try [FAIL] TestClass.TestMethod: Message
                    if (isBracketFail)
                    {
                        var bracketMatch = BracketFailRegex.Match(cleanLine);
                        if (bracketMatch.Success)
                        {
                            string fullName = bracketMatch.Groups["test"].Value.Trim();
                            string details = bracketMatch.Groups["details"].Success ? bracketMatch.Groups["details"].Value.Trim() : string.Empty;
                            var (className, methodName) = SplitTestName(fullName);

                            results.Add(new TestFailure
                            {
                                TestClass = className,
                                TestMethod = methodName,
                                AssertionFailureMessage = ExtractAssertionMessage(details),
                                RawLine = rawLine
                            });
                        }
                    }

                    continue;
                }

                // 2. Try Dotnet test: "Failed TestClass.TestMethod [12 ms]"
                if (cleanLine.StartsWith("Failed", StringComparison.OrdinalIgnoreCase))
                {
                    var dotnetMatch = DotnetTestFailedRegex.Match(cleanLine);
                    if (dotnetMatch.Success)
                    {
                        string fullName = dotnetMatch.Groups["test"].Value.Trim();
                        var (className, methodName) = SplitTestName(fullName);

                        string errorMessage = string.Empty;
                        string stackTrace = string.Empty;

                        // Look ahead for "Error Message:" and "Stack Trace:" using 1-line peek
                        while (lineReader.TryPeek(out _, out string lookAhead))
                        {
                            if (lookAhead.StartsWith("Failed ", StringComparison.OrdinalIgnoreCase) ||
                                lookAhead.StartsWith("Passed ", StringComparison.OrdinalIgnoreCase) ||
                                lookAhead.StartsWith("Total tests:", StringComparison.OrdinalIgnoreCase) ||
                                lookAhead.StartsWith("[FAIL]", StringComparison.OrdinalIgnoreCase) ||
                                lookAhead.StartsWith("FAIL:", StringComparison.OrdinalIgnoreCase))
                            {
                                break;
                            }

                            if (lookAhead.StartsWith("Error Message:", StringComparison.OrdinalIgnoreCase))
                            {
                                lineReader.ConsumePeeked();
                                if (lineReader.TryRead(out _, out string messageLine))
                                {
                                    errorMessage = messageLine;
                                }
                                continue;
                            }

                            if (lookAhead.StartsWith("Stack Trace:", StringComparison.OrdinalIgnoreCase))
                            {
                                lineReader.ConsumePeeked();
                                List<string>? stList = null;
                                while (lineReader.TryPeek(out _, out string stClean) &&
                                       (stClean.StartsWith("at ", StringComparison.Ordinal) || stClean.Length == 0))
                                {
                                    lineReader.ConsumePeeked();
                                    if (stClean.Length > 0)
                                    {
                                        stList ??= new List<string>();
                                        stList.Add(stClean);
                                    }
                                }

                                stackTrace = stList != null ? string.Join("\n", stList) : string.Empty;
                                break;
                            }

                            lineReader.ConsumePeeked();
                        }

                        results.Add(new TestFailure
                        {
                            TestClass = className,
                            TestMethod = methodName,
                            AssertionFailureMessage = string.IsNullOrWhiteSpace(errorMessage) ? "Test execution failed." : errorMessage,
                            StackTrace = stackTrace,
                            RawLine = rawLine
                        });
                    }
                }

                break;
            }
        }

        private static bool NextNonEmptyContinuesException(string[] lines, int startIndex)
        {
            for (int k = startIndex; k < lines.Length; k++)
            {
                string peek = AnsiCleaner.Clean(lines[k]);
                if (string.IsNullOrEmpty(peek))
                {
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

        private static string CollectStackTrace(ref PeekableLineReader lineReader)
        {
            List<string>? stackLines = null;
            while (lineReader.TryPeek(out _, out string line))
            {
                if (line.StartsWith("at ", StringComparison.Ordinal) ||
                    line.StartsWith("--- End of inner exception stack trace ---", StringComparison.Ordinal))
                {
                    lineReader.ConsumePeeked();
                    stackLines ??= new List<string>();
                    stackLines.Add(line);
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

            return stackLines != null ? string.Join("\n", stackLines) : string.Empty;
        }
    }
}
