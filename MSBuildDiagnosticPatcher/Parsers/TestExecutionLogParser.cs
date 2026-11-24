using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using MSBuildDiagnosticPatcher.Models;

namespace MSBuildDiagnosticPatcher.Parsers
{
    public class TestExecutionLogParser
    {
        // Matches custom runner: FAIL: ClassOrTestName - Message / Exception / Assertion Failed: ...
        private static readonly Regex CustomRunnerFailRegex = new Regex(
            @"^\s*(?:\[FAIL\]|FAIL:)\s+(?<test>[A-Za-z0-9_.]+)(?:\s*-\s*(?<details>.*))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Matches dotnet test: Failed TestClass.TestMethod [12 ms] or Failed TestClass.TestMethod
        private static readonly Regex DotnetTestFailedRegex = new Regex(
            @"^\s*Failed\s+(?<test>[A-Za-z0-9_.]+)(?:\s*\[[\d\s\w.]+\])?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Matches [FAIL] TestClass.TestMethod: Message
        private static readonly Regex BracketFailRegex = new Regex(
            @"^\s*\[FAIL\]\s+(?<test>[A-Za-z0-9_.]+)(?::\s*(?<details>.*))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
            }
        }

        private static (string ClassName, string MethodName) SplitTestName(string fullName)
        {
            int lastDot = fullName.LastIndexOf('.');
            if (lastDot > 0 && lastDot < fullName.Length - 1)
            {
                return (fullName.Substring(0, lastDot), fullName.Substring(lastDot + 1));
            }
            return (fullName, string.Empty);
        }

        private static string ExtractAssertionMessage(string rawDetails)
        {
            if (string.IsNullOrWhiteSpace(rawDetails))
            {
                return "Assertion failed or unhandled exception.";
            }

            // If it contains "Assertion Failed: ...", extract the friendly message
            int assertIdx = rawDetails.IndexOf("Assertion Failed:", StringComparison.OrdinalIgnoreCase);
            if (assertIdx >= 0)
            {
                return rawDetails.Substring(assertIdx).Trim();
            }

            // Or if it contains "Exception: ..."
            int exIdx = rawDetails.IndexOf("Exception:", StringComparison.OrdinalIgnoreCase);
            if (exIdx >= 0)
            {
                return rawDetails.Substring(exIdx).Trim();
            }

            return rawDetails.Trim();
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
                else
                {
                    break;
                }
            }

            return stackLines != null ? string.Join("\n", stackLines) : string.Empty;
        }
    }
}
