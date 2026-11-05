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
            var results = new List<TestFailure>();
            if (string.IsNullOrWhiteSpace(logContent))
            {
                return results;
            }

            var lines = logContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                string rawLine = lines[i];
                string cleanLine = AnsiCleaner.Clean(rawLine);

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
                        RawLine = rawLine
                    };

                    // Collect subsequent stack trace lines if available
                    failure.StackTrace = CollectStackTrace(lines, ref i);
                    results.Add(failure);
                    continue;
                }

                // 2. Try Dotnet test: "Failed TestClass.TestMethod [12 ms]"
                var dotnetMatch = DotnetTestFailedRegex.Match(cleanLine);
                if (dotnetMatch.Success)
                {
                    string fullName = dotnetMatch.Groups["test"].Value.Trim();
                    var (className, methodName) = SplitTestName(fullName);

                    string errorMessage = "";
                    string stackTrace = "";

                    // Look ahead for "Error Message:" and "Stack Trace:"
                    int j = i + 1;
                    while (j < lines.Length)
                    {
                        string lookAhead = AnsiCleaner.Clean(lines[j]);
                        if (lookAhead.StartsWith("Failed ") || lookAhead.StartsWith("Passed ") || lookAhead.StartsWith("Total tests:"))
                        {
                            break;
                        }

                        if (lookAhead.StartsWith("Error Message:", StringComparison.OrdinalIgnoreCase))
                        {
                            j++;
                            if (j < lines.Length)
                            {
                                errorMessage = AnsiCleaner.Clean(lines[j]);
                            }
                        }
                        else if (lookAhead.StartsWith("Stack Trace:", StringComparison.OrdinalIgnoreCase))
                        {
                            j++;
                            var stList = new List<string>();
                            while (j < lines.Length && (lines[j].Trim().StartsWith("at ") || string.IsNullOrWhiteSpace(lines[j])))
                            {
                                if (!string.IsNullOrWhiteSpace(lines[j]))
                                {
                                    stList.Add(AnsiCleaner.Clean(lines[j]));
                                }
                                j++;
                            }
                            stackTrace = string.Join("\n", stList);
                            break;
                        }
                        j++;
                    }

                    results.Add(new TestFailure
                    {
                        TestClass = className,
                        TestMethod = methodName,
                        AssertionFailureMessage = string.IsNullOrWhiteSpace(errorMessage) ? "Test execution failed." : errorMessage,
                        StackTrace = stackTrace,
                        RawLine = rawLine
                    });
                    continue;
                }

                // 3. Try [FAIL] TestClass.TestMethod: Message
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

            return results;
        }

        public List<TestFailure> ParseFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Log file not found: {filePath}");
            }

            string content = File.ReadAllText(filePath);
            return ParseText(content);
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

        private static string CollectStackTrace(string[] lines, ref int currentIndex)
        {
            var stackLines = new List<string>();
            int next = currentIndex + 1;
            while (next < lines.Length)
            {
                string line = AnsiCleaner.Clean(lines[next]);
                if (line.StartsWith("at ") || line.StartsWith("--- End of inner exception stack trace ---"))
                {
                    stackLines.Add(line);
                    currentIndex = next;
                    next++;
                }
                else
                {
                    break;
                }
            }
            return string.Join("\n", stackLines);
        }
    }
}
