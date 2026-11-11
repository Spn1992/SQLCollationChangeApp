using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MSBuildDiagnosticPatcher.Models;
using MSBuildDiagnosticPatcher.Parsers;
using MSBuildDiagnosticPatcher.Patcher;
using MSBuildDiagnosticPatcher.Reporting;

namespace MSBuildDiagnosticPatcher.Tests
{
    class Program
    {
        private static int _passed = 0;
        private static int _failed = 0;

        static async Task<int> Main(string[] args)
        {
            Console.WriteLine("=================================================");
            Console.WriteLine(" Running MSBuildDiagnosticPatcher Unit Test Suite");
            Console.WriteLine("=================================================");

            // 1. MSBuildLogParser Tests
            RunTest("MSBuildLogParser_ParseLine_ExtractsRoslynError", Test_MSBuildLogParser_ParseLine_ExtractsRoslynError);
            RunTest("MSBuildLogParser_ParseLine_ExtractsRoslynWarning", Test_MSBuildLogParser_ParseLine_ExtractsRoslynWarning);
            RunTest("MSBuildLogParser_ParseLine_ExtractsMSBuildError", Test_MSBuildLogParser_ParseLine_ExtractsMSBuildError);
            RunTest("MSBuildLogParser_ParseLine_HandlesAnsiEscapes", Test_MSBuildLogParser_ParseLine_HandlesAnsiEscapes);
            RunTest("MSBuildLogParser_ParsePerformance_Under10Seconds", Test_MSBuildLogParser_ParsePerformance_Under10Seconds);

            // 1.5 AnsiCleaner Tests
            RunTest("AnsiCleaner_Clean_HandlesNullAndEmpty", Test_AnsiCleaner_Clean_HandlesNullAndEmpty);
            RunTest("AnsiCleaner_Clean_RemovesColorCodes", Test_AnsiCleaner_Clean_RemovesColorCodes);
            RunTest("AnsiCleaner_Clean_RemovesCursorMovementCodes", Test_AnsiCleaner_Clean_RemovesCursorMovementCodes);
            RunTest("AnsiCleaner_Clean_PreservesRegularLogTextAndPunctuation", Test_AnsiCleaner_Clean_PreservesRegularLogTextAndPunctuation);

            // 2. TestExecutionLogParser Tests
            RunTest("TestExecutionLogParser_ParseCustomRunner_ExtractsFailedClassAndAssertion", Test_TestExecutionLogParser_ParseCustomRunner);
            RunTest("TestExecutionLogParser_ParseDotnetTest_ExtractsClassMethodAndError", Test_TestExecutionLogParser_ParseDotnetTest);
            RunTest("TestExecutionLogParser_ParseDotnetTest_ConsecutiveFailuresWithMultiLineStackTrace", Test_TestExecutionLogParser_ParseDotnetTest_ConsecutiveFailuresWithMultiLineStackTrace);
            RunTest("TestExecutionLogParser_ParseBracketFail_ExtractsFailure", Test_TestExecutionLogParser_ParseBracketFail);

            // 3. InterfaceDefinitionResolver Tests
            RunTest("InterfaceDefinitionResolver_ParseCs0535Message_ExtractsComponents", Test_InterfaceDefinitionResolver_ParseCs0535Message);
            RunTest("InterfaceDefinitionResolver_ExtractMemberFromInterfaceSource_FindsMethod", Test_InterfaceDefinitionResolver_ExtractMemberFromInterfaceSource);
            RunTest("InterfaceDefinitionResolver_ExtractMemberFromInterfaceSource_FindsProperty", Test_InterfaceDefinitionResolver_ExtractProperty);
            RunTest("InterfaceDefinitionResolver_Caching_ReadsFileOnce", Test_InterfaceDefinitionResolver_Caching_ReadsFileOnce);
            RunTest("InterfaceDefinitionResolver_NegativeCaching_PreventsRepetitiveSearches", Test_InterfaceDefinitionResolver_NegativeCaching_PreventsRepetitiveSearches);
            RunTest("InterfaceDefinitionResolver_ClearCache_InvalidatesFileAndContentCache", Test_InterfaceDefinitionResolver_ClearCache_InvalidatesFileAndContentCache);

            // 4. PatchGenerator Tests
            RunTest("PatchGenerator_FindClassInsertionPoint_FindsClassClosingBrace", Test_PatchGenerator_FindClassInsertionPoint);
            RunTest("PatchGenerator_GenerateMemberStub_DelegatesToExistingOverload", Test_PatchGenerator_GenerateMemberStub_Delegates);
            RunTest("PatchGenerator_GenerateMemberStub_DelegatesWithValueTypeParameters", Test_PatchGenerator_GenerateMemberStub_DelegatesWithValueTypeParameters);
            RunTest("PatchGenerator_GenerateMemberStub_GeneratesNotImplementedException", Test_PatchGenerator_GenerateMemberStub_NotImplemented);
            RunTest("PatchGenerator_GenerateUnifiedDiff_ProducesValidDiffHeader", Test_PatchGenerator_GenerateUnifiedDiff);
            RunTest("PatchGenerator_RepeatedMemberLookups_UseCacheAndGenerateCorrectStubs", Test_PatchGenerator_RepeatedMemberLookups_UseCacheAndGenerateCorrectStubs);

            // 5. PatchApplicator Tests
            RunTest("PatchApplicator_ApplyPatch_InsertsCodeWithoutDamagingComments", Test_PatchApplicator_ApplyPatch);
            RunTest("PatchApplicator_ApplyPatch_GuardsAgainstDuplicateApplication", Test_PatchApplicator_GuardsDuplicate);

            // 6. ReportGenerator Tests
            RunTest("ReportGenerator_GenerateMarkdownReport_ProducesFormattedMarkdown", Test_ReportGenerator_Markdown);
            RunTest("ReportGenerator_GenerateJsonReport_ProducesValidJson", Test_ReportGenerator_Json);

            // 7. End-to-End Diagnostic & Patching Workflow
            RunTest("EndToEnd_ParseLogAndGeneratePatch_RestoresInterfaceImplementation", Test_EndToEnd_Workflow);

            // 8. Program.Main Exit Code & Unresolved Error Count Tests
            RunTest("DiagnosticReport_UnresolvedErrorCount_Calculation", Test_DiagnosticReport_UnresolvedErrorCount_Calculation);
            RunTest("Program_Main_ReturnsExitCode0_ForCleanBuildLog", Test_Program_Main_ReturnsExitCode0_ForCleanBuildLog);
            RunTest("Program_Main_ReturnsZero_WhenNoErrorsOrFailures", Test_Program_Main_ReturnsZero_WhenNoErrorsOrFailures);
            RunTest("Program_Main_ReturnsZero_WhenWarningsOnly", Test_Program_Main_ReturnsZero_WhenWarningsOnly);
            RunTest("Program_Main_ReturnsExitCode1_WhenBuildErrorsRemainWithoutPatch", Test_Program_Main_ReturnsExitCode1_WhenBuildErrorsRemainWithoutPatch);
            RunTest("Program_Main_ReturnsOne_WhenCompilerErrorsPresent", Test_Program_Main_ReturnsOne_WhenCompilerErrorsPresent);
            RunTest("Program_Main_ReturnsExitCode1_WhenTestFailuresExist", Test_Program_Main_ReturnsExitCode1_WhenTestFailuresExist);
            RunTest("Program_Main_ReturnsOne_WhenTestFailuresPresent", Test_Program_Main_ReturnsOne_WhenTestFailuresPresent);
            RunTest("Program_Main_ReturnsOne_WhenBothErrorsAndFailuresPresent", Test_Program_Main_ReturnsOne_WhenBothErrorsAndFailuresPresent);
            RunTest("Program_Main_ReturnsExitCode0_WhenPatchesRemediateAllCompilerDiagnostics", Test_Program_Main_ReturnsExitCode0_WhenPatchesRemediateAllCompilerDiagnostics);
            RunTest("Program_Main_ReturnsOne_WhenInputFileNotFound", Test_Program_Main_ReturnsOne_WhenInputFileNotFound);

            // 9. CI Workflow Configuration Tests
            RunTest("Workflow_PostMergeBuildVerification_HasDiagnosticReportingAndArtifactUpload", Test_Workflow_PostMergeBuildVerification_HasDiagnosticReportingAndArtifactUpload);

            Console.WriteLine("=================================================");
            Console.WriteLine($" Test Suite Summary: {_passed} Passed, {_failed} Failed");
            Console.WriteLine("=================================================");

            return _failed == 0 ? 0 : 1;
        }

        static void RunTest(string testName, Action testAction)
        {
            try
            {
                testAction();
                Console.WriteLine($"  [PASS] {testName}");
                _passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [FAIL] {testName} - {ex.Message}");
                _failed++;
            }
        }

        static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception($"Assertion Failed: {message}");
            }
        }

        // 1. MSBuildLogParser Tests
        static void Test_MSBuildLogParser_ParseLine_ExtractsRoslynError()
        {
            var parser = new MSBuildLogParser();
            string line = "/app/SQLCollationChangeApp/CollationModifier/Services/DatabaseService.cs(11,36): error CS0535: 'DatabaseService' does not implement interface member 'IDatabaseService.BackupDatabaseAsync(string, string, CancellationToken)' [/app/SQLCollationChangeApp/CollationModifier/CollationModifier.csproj]";

            var diag = parser.ParseLine(line);
            Assert(diag != null, "Expected diagnostic to be parsed.");
            Assert(diag!.Code == "CS0535", $"Expected code CS0535, got {diag.Code}");
            Assert(diag.Severity == DiagnosticSeverity.Error, $"Expected severity Error, got {diag.Severity}");
            Assert(diag.LineNumber == 11, $"Expected line 11, got {diag.LineNumber}");
            Assert(diag.ColumnNumber == 36, $"Expected column 36, got {diag.ColumnNumber}");
            Assert(diag.FilePath.Contains("DatabaseService.cs"), "Expected FilePath to contain DatabaseService.cs");
            Assert(diag.ProjectPath.Contains("CollationModifier.csproj"), "Expected ProjectPath to contain CollationModifier.csproj");
            Assert(diag.Message.Contains("does not implement interface member"), "Expected message to contain CS0535 description");
        }

        static void Test_MSBuildLogParser_ParseLine_ExtractsRoslynWarning()
        {
            var parser = new MSBuildLogParser();
            string line = "ViewModels/MainViewModel.cs(120,15): warning CS0168: The variable 'ex' is declared but never used [CollationModifier.csproj]";

            var diag = parser.ParseLine(line);
            Assert(diag != null, "Expected warning to be parsed.");
            Assert(diag!.Code == "CS0168", $"Expected code CS0168, got {diag.Code}");
            Assert(diag.Severity == DiagnosticSeverity.Warning, $"Expected severity Warning, got {diag.Severity}");
            Assert(diag.LineNumber == 120, $"Expected line 120, got {diag.LineNumber}");
            Assert(diag.ColumnNumber == 15, $"Expected col 15, got {diag.ColumnNumber}");
        }

        static void Test_MSBuildLogParser_ParseLine_ExtractsMSBuildError()
        {
            var parser = new MSBuildLogParser();
            string line = "MSBUILD : error MSB1003: Specify a project or solution file. The current working directory does not contain a project or solution file.";

            var diag = parser.ParseLine(line);
            Assert(diag != null, "Expected MSB1003 to be parsed.");
            Assert(diag!.Code == "MSB1003", $"Expected code MSB1003, got {diag.Code}");
            Assert(diag.Severity == DiagnosticSeverity.Error, $"Expected severity Error, got {diag.Severity}");
        }

        static void Test_MSBuildLogParser_ParseLine_HandlesAnsiEscapes()
        {
            var parser = new MSBuildLogParser();
            string line = "\u001b[31;1mServices/DatabaseService.cs(11,36): error CS0535: 'DatabaseService' does not implement interface member 'IDatabaseService.RestoreDatabaseAsync(string, string, CancellationToken)'\u001b[0m";

            var diag = parser.ParseLine(line);
            Assert(diag != null, "Expected ANSI escaped line to be parsed.");
            Assert(diag!.Code == "CS0535", $"Expected code CS0535, got {diag.Code}");
            Assert(diag.FilePath == "Services/DatabaseService.cs", $"Expected clean path, got '{diag.FilePath}'");
        }

        static void Test_MSBuildLogParser_ParsePerformance_Under10Seconds()
        {
            var parser = new MSBuildLogParser();

            // Generate 20,000 log lines
            var lines = new List<string>(20000);
            for (int i = 0; i < 20000; i++)
            {
                if (i % 20 == 0)
                {
                    lines.Add($"Services/Service{i}.cs({i % 500 + 1},10): error CS0535: 'Service{i}' does not implement interface member 'IService.Method{i}()' [Project.csproj]");
                }
                else if (i % 50 == 0)
                {
                    lines.Add($"Services/Service{i}.cs({i % 500 + 1},5): warning CS0168: The variable 'unused' is declared but never used [Project.csproj]");
                }
                else
                {
                    lines.Add($"   at Microsoft.Build.Execution.BuildEngine.ExecuteTask(Int32 step {i}) in /src/Engine.cs:line {i}");
                }
            }

            string fullLog = string.Join("\n", lines);

            var sw = Stopwatch.StartNew();
            var parsed = parser.ParseText(fullLog);
            sw.Stop();

            Console.WriteLine($"    Parsed 20,000 lines in {sw.ElapsedMilliseconds} ms (Extracted {parsed.Count} diagnostics).");
            Assert(sw.Elapsed.TotalSeconds < 10.0, $"Expected parse duration under 10 seconds, got {sw.Elapsed.TotalSeconds:F2} seconds.");
            Assert(parsed.Count == 1200, $"Expected 1200 diagnostics, got {parsed.Count}.");
        }

        // 1.5 AnsiCleaner Tests
        static void Test_AnsiCleaner_Clean_HandlesNullAndEmpty()
        {
            Assert(AnsiCleaner.Clean(null) == string.Empty, "Expected empty string for null input.");
            Assert(AnsiCleaner.Clean(string.Empty) == string.Empty, "Expected empty string for empty input.");
            Assert(AnsiCleaner.Clean("   ") == string.Empty, "Expected empty string for whitespace input.");
            Assert(AnsiCleaner.Clean("\t\r\n") == string.Empty, "Expected empty string for tab/newline input.");
            Assert(AnsiCleaner.Clean("\u001b[0m") == string.Empty, "Expected empty string for escape-only input.");
            Assert(AnsiCleaner.Clean("   \u001b[31m\u001b[0m   ") == string.Empty, "Expected empty string for escaped whitespace input.");
        }

        static void Test_AnsiCleaner_Clean_RemovesColorCodes()
        {
            // Standard 16-color codes & reset
            string basicColor = "\u001b[31mError message\u001b[0m";
            Assert(AnsiCleaner.Clean(basicColor) == "Error message", "Failed standard 16-color stripping.");

            // Bold & bright foreground/background combinations
            string boldColored = "\u001b[1;32;40mBuild: 1 succeeded, 0 failed\u001b[0m";
            Assert(AnsiCleaner.Clean(boldColored) == "Build: 1 succeeded, 0 failed", "Failed multi-attribute SGR stripping.");

            // 256-color palette sequence: ESC[38;5;<n>m
            string color256 = "\u001b[38;5;196mHigh-intensity red warning\u001b[0m";
            Assert(AnsiCleaner.Clean(color256) == "High-intensity red warning", "Failed 256-color sequence stripping.");

            // 24-bit TrueColor RGB sequence: ESC[38;2;<r>;<g>;<b>m
            string rgbColor = "\u001b[38;2;255;128;0mOrange TrueColor output\u001b[0m";
            Assert(AnsiCleaner.Clean(rgbColor) == "Orange TrueColor output", "Failed TrueColor RGB sequence stripping.");

            // Multiple chained style sequences
            string chained = "\u001b[1m\u001b[4m\u001b[33mUnderlined Bold Yellow Warning\u001b[0m";
            Assert(AnsiCleaner.Clean(chained) == "Underlined Bold Yellow Warning", "Failed chained style sequence stripping.");
        }

        static void Test_AnsiCleaner_Clean_RemovesCursorMovementCodes()
        {
            // Cursor Up (A), Down (B), Forward (C), Back (D)
            string cursorRelative = "\u001b[2A\u001b[10CProcessing line...\u001b[3B\u001b[4D";
            Assert(AnsiCleaner.Clean(cursorRelative) == "Processing line...", "Failed cursor movement (A,B,C,D) stripping.");

            // Cursor Position: ESC[<row>;<col>H and ESC[<row>;<col>f
            string cursorAbsolute = "\u001b[12;34HCoordinates line";
            Assert(AnsiCleaner.Clean(cursorAbsolute) == "Coordinates line", "Failed cursor position (H) stripping.");
            string cursorHome = "\u001b[;fHome positioned text";
            Assert(AnsiCleaner.Clean(cursorHome) == "Home positioned text", "Failed cursor home (f) stripping.");

            // Erase in Display (J) and Erase in Line (K)
            string eraseControls = "\u001b[2J\u001b[KCleared screen line text";
            Assert(AnsiCleaner.Clean(eraseControls) == "Cleared screen line text", "Failed erase controls (J, K) stripping.");

            // Device / cursor control private modes: ESC[?25h and ESC[?25l
            string privateModes = "\u001b[?25hLog status: Ready\u001b[?25l";
            Assert(AnsiCleaner.Clean(privateModes) == "Log status: Ready", "Failed private mode escape sequence stripping.");
        }

        static void Test_AnsiCleaner_Clean_PreservesRegularLogTextAndPunctuation()
        {
            // Plain text without ANSI escapes
            string plainText = "  CollationModifier.Tests.Program: Passed  ";
            Assert(AnsiCleaner.Clean(plainText) == "CollationModifier.Tests.Program: Passed", "Failed plain text whitespace trimming.");

            // Complex log line with parentheses, commas, colons, brackets, backslashes
            string complexLog = "C:\\src\\Project\\Services\\DatabaseService.cs(11,36): error CS0535: 'DatabaseService' does not implement interface [C:\\src\\Project.csproj]";
            Assert(AnsiCleaner.Clean(complexLog) == complexLog, "Corrupted standard log line structure without escapes.");

            // Mixed ANSI escape in the middle of log line
            string mixedLog = "Services/DatabaseService.cs(11,36): \u001b[31;1merror CS0535\u001b[0m: 'DatabaseService' does not implement interface member";
            string expectedClean = "Services/DatabaseService.cs(11,36): error CS0535: 'DatabaseService' does not implement interface member";
            Assert(AnsiCleaner.Clean(mixedLog) == expectedClean, "Failed inline ANSI escape sequence removal in log line.");
        }

        // 2. TestExecutionLogParser Tests
        static void Test_TestExecutionLogParser_ParseCustomRunner()
        {
            var parser = new TestExecutionLogParser();
            string log = @"
=== Running CollationModifier Unit Tests ===
FAIL: MainViewModelTestsRunner - System.Exception: Assertion Failed: Enqueueing 1000 log entries took too long, calling threads were blocked!
   at CollationModifier.Tests.Program.Assert(Boolean condition, String message) in /app/Tests/Program.cs:line 230
   at CollationModifier.Tests.Program.TestHighFrequencyConcurrentLoggingWithoutDataLoss() in /app/Tests/Program.cs:line 140
PASS: TestFlushOnDispose
";

            var failures = parser.ParseText(log);
            Assert(failures.Count == 1, $"Expected 1 failure, got {failures.Count}");
            Assert(failures[0].TestClass == "MainViewModelTestsRunner", $"Expected class MainViewModelTestsRunner, got '{failures[0].TestClass}'");
            Assert(failures[0].AssertionFailureMessage.Contains("Enqueueing 1000 log entries took too long"), "Expected failure message to be extracted.");
            Assert(failures[0].StackTrace.Contains("at CollationModifier.Tests.Program.Assert"), "Expected stack trace to be extracted.");
        }

        static void Test_TestExecutionLogParser_ParseDotnetTest()
        {
            var parser = new TestExecutionLogParser();
            string log = @"
Starting test execution, please wait...
Failed MainViewModelTests.ProcessCollationChangeAsync_UpdatesCollationInMemory [45 ms]
  Error Message:
   Assertion Failed: Expected status ready
  Stack Trace:
   at CollationModifier.Tests.MainViewModelTests.ProcessCollationChangeAsync_UpdatesCollationInMemory() in /app/Tests.cs:line 80
Passed MainViewModelTests.TestConnectionAsync [12 ms]

Total tests: 2. Passed: 1. Failed: 1. Skipped: 0.
";

            var failures = parser.ParseText(log);
            Assert(failures.Count == 1, $"Expected 1 failure, got {failures.Count}");
            Assert(failures[0].TestClass == "MainViewModelTests", $"Expected MainViewModelTests, got '{failures[0].TestClass}'");
            Assert(failures[0].TestMethod == "ProcessCollationChangeAsync_UpdatesCollationInMemory", $"Expected method name, got '{failures[0].TestMethod}'");
            Assert(failures[0].AssertionFailureMessage.Contains("Expected status ready"), "Expected error message");
            Assert(failures[0].StackTrace.Contains("at CollationModifier.Tests.MainViewModelTests"), "Expected stack trace");
        }

        static void Test_TestExecutionLogParser_ParseDotnetTest_ConsecutiveFailuresWithMultiLineStackTrace()
        {
            var parser = new TestExecutionLogParser();
            string failure1 = @"Failed MainViewModelTests.ProcessCollationChangeAsync_UpdatesCollationInMemory [45 ms]
  Error Message:
   Assertion Failed: Expected status ready
  Stack Trace:
   at CollationModifier.Tests.MainViewModelTests.ProcessCollationChangeAsync_UpdatesCollationInMemory() in /app/Tests.cs:line 80
   at CollationModifier.Tests.MainViewModelTests.RunAsync() in /app/Tests.cs:line 120
   at System.Threading.Tasks.Task.Execute() in /runtime/Task.cs:line 450";

            string failure2 = @"Failed DatabaseServiceTests.AlterCollationAsync [60 ms]
  Error Message:
   Assertion Failed: Database was not put in SINGLE_USER mode
  Stack Trace:
   at CollationModifier.Tests.DatabaseServiceTests.AlterCollationAsync() in /app/DbTests.cs:line 150
   at CollationModifier.Tests.DatabaseServiceTests.RunAsync() in /app/DbTests.cs:line 200";

            string log = $@"
Starting test execution, please wait...
{failure1}
{failure2}
Passed MainViewModelTests.TestConnectionAsync [12 ms]

Total tests: 3. Passed: 1. Failed: 2. Skipped: 0.
";

            // Parse consecutive failures together
            var failures = parser.ParseText(log);
            Assert(failures.Count == 2, $"Expected 2 failures, got {failures.Count}");

            // Parse individual failures in isolation
            var singleFailure1 = parser.ParseText(failure1);
            var singleFailure2 = parser.ParseText(failure2);
            Assert(singleFailure1.Count == 1, "Expected single failure 1 to parse.");
            Assert(singleFailure2.Count == 1, "Expected single failure 2 to parse.");

            // Verify first failure properties and multi-line stack trace
            Assert(failures[0].TestClass == "MainViewModelTests", $"Expected MainViewModelTests, got '{failures[0].TestClass}'");
            Assert(failures[0].TestMethod == "ProcessCollationChangeAsync_UpdatesCollationInMemory", $"Expected method name, got '{failures[0].TestMethod}'");
            Assert(failures[0].AssertionFailureMessage.Contains("Expected status ready"), "Expected error message for failure 1");
            Assert(failures[0].StackTrace.Contains("ProcessCollationChangeAsync_UpdatesCollationInMemory") &&
                   failures[0].StackTrace.Contains("RunAsync") &&
                   failures[0].StackTrace.Contains("Task.Execute"),
                   "Expected multi-line stack trace with all 3 frames for failure 1");

            // Verify second failure properties and multi-line stack trace
            Assert(failures[1].TestClass == "DatabaseServiceTests", $"Expected DatabaseServiceTests, got '{failures[1].TestClass}'");
            Assert(failures[1].TestMethod == "AlterCollationAsync", $"Expected AlterCollationAsync, got '{failures[1].TestMethod}'");
            Assert(failures[1].AssertionFailureMessage.Contains("Database was not put in SINGLE_USER mode"), "Expected error message for failure 2");
            Assert(failures[1].StackTrace.Contains("AlterCollationAsync") &&
                   failures[1].StackTrace.Contains("RunAsync"),
                   "Expected multi-line stack trace with all frames for failure 2");

            // Assert that the parser extracts identical TestFailure fields for consecutive dotnet test failures vs isolated parsing
            Assert(failures[0].TestClass == singleFailure1[0].TestClass, "TestClass must be identical");
            Assert(failures[0].TestMethod == singleFailure1[0].TestMethod, "TestMethod must be identical");
            Assert(failures[0].AssertionFailureMessage == singleFailure1[0].AssertionFailureMessage, "AssertionFailureMessage must be identical");
            Assert(failures[0].StackTrace == singleFailure1[0].StackTrace, "StackTrace must be identical");
            Assert(failures[0].RawLine == singleFailure1[0].RawLine, "RawLine must be identical");

            Assert(failures[1].TestClass == singleFailure2[0].TestClass, "TestClass must be identical");
            Assert(failures[1].TestMethod == singleFailure2[0].TestMethod, "TestMethod must be identical");
            Assert(failures[1].AssertionFailureMessage == singleFailure2[0].AssertionFailureMessage, "AssertionFailureMessage must be identical");
            Assert(failures[1].StackTrace == singleFailure2[0].StackTrace, "StackTrace must be identical");
            Assert(failures[1].RawLine == singleFailure2[0].RawLine, "RawLine must be identical");

            // Also test identical consecutive failures to verify field equivalence across consecutive identical failures
            string identicalLog = $"{failure1}\n{failure1}";
            var identicalFailures = parser.ParseText(identicalLog);
            Assert(identicalFailures.Count == 2, $"Expected 2 failures, got {identicalFailures.Count}");
            Assert(identicalFailures[0].TestClass == identicalFailures[1].TestClass, "Consecutive identical failures must have identical TestClass");
            Assert(identicalFailures[0].TestMethod == identicalFailures[1].TestMethod, "Consecutive identical failures must have identical TestMethod");
            Assert(identicalFailures[0].AssertionFailureMessage == identicalFailures[1].AssertionFailureMessage, "Consecutive identical failures must have identical AssertionFailureMessage");
            Assert(identicalFailures[0].StackTrace == identicalFailures[1].StackTrace, "Consecutive identical failures must have identical StackTrace");
            Assert(identicalFailures[0].RawLine == identicalFailures[1].RawLine, "Consecutive identical failures must have identical RawLine");
        }

        static void Test_TestExecutionLogParser_ParseBracketFail()
        {
            var parser = new TestExecutionLogParser();
            string log = "  [FAIL] DatabaseServiceTests.AlterCollationAsync: Database was not put in SINGLE_USER mode";

            var failures = parser.ParseText(log);
            Assert(failures.Count == 1, $"Expected 1 failure, got {failures.Count}");
            Assert(failures[0].TestClass == "DatabaseServiceTests", $"Expected DatabaseServiceTests, got {failures[0].TestClass}");
            Assert(failures[0].TestMethod == "AlterCollationAsync", $"Expected AlterCollationAsync, got {failures[0].TestMethod}");
            Assert(failures[0].AssertionFailureMessage.Contains("Database was not put in SINGLE_USER mode"), "Expected failure message");
        }

        // 3. InterfaceDefinitionResolver Tests
        static void Test_InterfaceDefinitionResolver_ParseCs0535Message()
        {
            string msg = "'DatabaseService' does not implement interface member 'IDatabaseService.BackupDatabaseAsync(string, string, CancellationToken)'";
            var parsed = InterfaceDefinitionResolver.ParseCs0535Message(msg);

            Assert(parsed != null, "Expected CS0535 message to parse.");
            Assert(parsed!.Value.ClassName == "DatabaseService", $"Expected DatabaseService, got '{parsed.Value.ClassName}'");
            Assert(parsed.Value.InterfaceName == "IDatabaseService", $"Expected IDatabaseService, got '{parsed.Value.InterfaceName}'");
            Assert(parsed.Value.MemberName == "BackupDatabaseAsync", $"Expected BackupDatabaseAsync, got '{parsed.Value.MemberName}'");
            Assert(parsed.Value.ExpectedParamTypes == "string, string, CancellationToken", $"Expected param types, got '{parsed.Value.ExpectedParamTypes}'");
        }

        static void Test_InterfaceDefinitionResolver_ExtractMemberFromInterfaceSource()
        {
            var resolver = new InterfaceDefinitionResolver();
            string interfaceSource = @"
namespace CollationModifier.Services
{
    public interface IDatabaseService
    {
        Task<string> BackupDatabaseAsync(string databaseName, string backupDirectory, CancellationToken cancellationToken = default);
        Task RestoreDatabaseAsync(string databaseName, string backupFile, CancellationToken cancellationToken = default);
    }
}";

            var member = resolver.ExtractMemberFromInterfaceSource(
                interfaceSource,
                "IDatabaseService",
                "BackupDatabaseAsync",
                "string, string, CancellationToken");

            Assert(member != null, "Expected member to be found in interface.");
            Assert(member!.ReturnType == "Task<string>", $"Expected Task<string>, got '{member.ReturnType}'");
            Assert(member.MemberName == "BackupDatabaseAsync", $"Expected BackupDatabaseAsync, got '{member.MemberName}'");
            Assert(member.ParameterList.Contains("string databaseName"), "Expected parameter names preserved.");
            Assert(member.ParameterList.Contains("CancellationToken cancellationToken = default"), "Expected default parameter preserved.");
        }

        static void Test_InterfaceDefinitionResolver_ExtractProperty()
        {
            var resolver = new InterfaceDefinitionResolver();
            string interfaceSource = @"
public interface IConfigService
{
    string ConnectionString { get; set; }
    int TimeoutSeconds { get; }
}";

            var member = resolver.ExtractMemberFromInterfaceSource(
                interfaceSource,
                "IConfigService",
                "ConnectionString",
                "");

            Assert(member != null, "Expected property to be found.");
            Assert(member!.IsProperty, "Expected IsProperty == true");
            Assert(member.ReturnType == "string", $"Expected return type string, got '{member.ReturnType}'");
            Assert(member.HasGetter && member.HasSetter, "Expected getter and setter");
        }

        static void Test_InterfaceDefinitionResolver_Caching_ReadsFileOnce()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"cache_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                string ifaceFile = Path.Combine(tempDir, "ICacheDemoService.cs");
                File.WriteAllText(ifaceFile, @"
public interface ICacheDemoService
{
    string MethodA(string input);
    int MethodB(int count);
}
");
                string contextFile = Path.Combine(tempDir, "CacheDemoService.cs");
                var resolver = new InterfaceDefinitionResolver();

                // First resolution: reads file from disk and populates caches
                var memberA = resolver.ResolveInterfaceMember("ICacheDemoService", "MethodA", "string", contextFile, tempDir);
                Assert(memberA != null, "Expected MethodA to be resolved.");
                Assert(memberA!.ReturnType == "string", "Expected return type string for MethodA.");

                // Now delete the interface file from disk!
                File.Delete(ifaceFile);
                Assert(!File.Exists(ifaceFile), "Interface file should be deleted.");

                // Second resolution for different member of same interface: should succeed from cached content without reading disk
                var memberB = resolver.ResolveInterfaceMember("ICacheDemoService", "MethodB", "int", contextFile, tempDir);
                Assert(memberB != null, "Expected MethodB to be resolved from in-memory cache even though file was deleted from disk.");
                Assert(memberB!.ReturnType == "int", "Expected return type int for MethodB from cache.");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        static void Test_InterfaceDefinitionResolver_NegativeCaching_PreventsRepetitiveSearches()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"neg_cache_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                string contextFile = Path.Combine(tempDir, "SampleClass.cs");
                var resolver = new InterfaceDefinitionResolver();

                // 1. Initial lookup for non-existent interface returns null and caches negative lookup
                string? missing = resolver.FindInterfaceFile("IMissingInterface", contextFile, tempDir);
                Assert(missing == null, "Expected null for non-existent interface.");

                // 2. Create the file on disk after negative lookup
                string lateFile = Path.Combine(tempDir, "IMissingInterface.cs");
                File.WriteAllText(lateFile, "public interface IMissingInterface { }");

                // 3. Repeated lookup returns cached null, preventing repeated disk scans
                string? cachedResult = resolver.FindInterfaceFile("IMissingInterface", contextFile, tempDir);
                Assert(cachedResult == null, "Expected negative cache hit returning null even after file was created.");

                // 4. Calling ClearCache invalidates cache and allows discovering the newly created file
                resolver.ClearCache();
                string? refreshedResult = resolver.FindInterfaceFile("IMissingInterface", contextFile, tempDir);
                Assert(refreshedResult != null, "Expected non-null after ClearCache re-scans disk.");
                Assert(Path.GetFullPath(refreshedResult!) == Path.GetFullPath(lateFile), "Expected resolved path to match created file.");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        static void Test_InterfaceDefinitionResolver_ClearCache_InvalidatesFileAndContentCache()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"clear_cache_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                string ifaceFile = Path.Combine(tempDir, "IMutableInterface.cs");
                File.WriteAllText(ifaceFile, @"
public interface IMutableInterface
{
    string GetValue();
}
");
                string contextFile = Path.Combine(tempDir, "MutableClass.cs");
                var resolver = new InterfaceDefinitionResolver();

                // 1. Resolve initial version
                var member1 = resolver.ResolveInterfaceMember("IMutableInterface", "GetValue", "", contextFile, tempDir);
                Assert(member1 != null && member1.ReturnType == "string", "Expected initial return type string.");

                // 2. Overwrite file with new signature
                File.WriteAllText(ifaceFile, @"
public interface IMutableInterface
{
    int GetValue();
}
");
                // 3. Without ClearCache, cached content returns 'string'
                var memberCached = resolver.ResolveInterfaceMember("IMutableInterface", "GetValue", "", contextFile, tempDir);
                Assert(memberCached != null && memberCached.ReturnType == "string", "Expected cached return type 'string' prior to ClearCache.");

                // 4. After ClearCache, disk content is re-read and returns 'int'
                resolver.ClearCache();
                var memberRefreshed = resolver.ResolveInterfaceMember("IMutableInterface", "GetValue", "", contextFile, tempDir);
                Assert(memberRefreshed != null && memberRefreshed.ReturnType == "int", "Expected refreshed return type 'int' after ClearCache.");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        // 4. PatchGenerator Tests
        static void Test_PatchGenerator_FindClassInsertionPoint()
        {
            string source = @"
namespace TestNamespace
{
    public class SampleService : ISampleService
    {
        public void DoSomething()
        {
            // Implementation
        }
    }
}
";
            int pos = PatchGenerator.FindClassInsertionPoint(source, "SampleService");
            Assert(pos > 0, "Expected positive insertion position.");
            string beforeInsert = source.Substring(0, pos);
            Assert(beforeInsert.Contains("DoSomething()"), "Insertion point must be after DoSomething()");
        }

        static void Test_PatchGenerator_GenerateMemberStub_Delegates()
        {
            var generator = new PatchGenerator();
            string classSource = @"
public class DatabaseService : IDatabaseService
{
    public virtual async Task<string> BackupDatabaseAsync(string databaseName, string backupDirectory, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        return ""backup.bak"";
    }
}";

            var member = new InterfaceMemberInfo
            {
                InterfaceName = "IDatabaseService",
                MemberName = "BackupDatabaseAsync",
                ReturnType = "Task<string>",
                ParameterList = "string databaseName, string backupDirectory, CancellationToken cancellationToken = default"
            };

            string stub = generator.GenerateMemberStub(member, classSource);
            Assert(stub.Contains("return BackupDatabaseAsync(databaseName, backupDirectory, null, cancellationToken);"),
                $"Expected delegation call, got:\n{stub}");
        }

        static void Test_PatchGenerator_GenerateMemberStub_DelegatesWithValueTypeParameters()
        {
            var generator = new PatchGenerator();

            // Case 1: Unsupplied CancellationToken with default value
            string classSource1 = @"
public class DatabaseService : IDatabaseService
{
    public virtual async Task<string> BackupDatabaseAsync(string databaseName, string backupDirectory, CancellationToken cancellationToken = default)
    {
        return ""backup.bak"";
    }
}";

            var member1 = new InterfaceMemberInfo
            {
                InterfaceName = "IDatabaseService",
                MemberName = "BackupDatabaseAsync",
                ReturnType = "Task<string>",
                ParameterList = "string databaseName, string backupDirectory"
            };

            string stub1 = generator.GenerateMemberStub(member1, classSource1);
            Assert(stub1.Contains("return BackupDatabaseAsync(databaseName, backupDirectory, default);"),
                $"Expected delegation call with default for CancellationToken, got:\n{stub1}");

            // Case 2: Unsupplied integer and boolean parameters with explicit literal defaults
            string classSource2 = @"
public class OperationsService : IOperationsService
{
    public virtual void ExecuteOperation(string operationName, int timeoutSeconds = 30, bool throwOnError = false, int retryCount = 0)
    {
    }
}";

            var member2 = new InterfaceMemberInfo
            {
                InterfaceName = "IOperationsService",
                MemberName = "ExecuteOperation",
                ReturnType = "void",
                ParameterList = "string operationName"
            };

            string stub2 = generator.GenerateMemberStub(member2, classSource2);
            Assert(stub2.Contains("ExecuteOperation(operationName, 30, false, 0);"),
                $"Expected delegation call with explicit int and bool defaults, got:\n{stub2}");

            // Case 3: Parameter hasDefault with whitespace defaultValue falls back to default, and nullable reference type without default emits null
            string classSource3 = @"
public class CacheService : ICacheService
{
    public virtual void CacheItem(string key, string? tag, CancellationToken cancellationToken =   )
    {
    }
}";

            var member3 = new InterfaceMemberInfo
            {
                InterfaceName = "ICacheService",
                MemberName = "CacheItem",
                ReturnType = "void",
                ParameterList = "string key"
            };

            string stub3 = generator.GenerateMemberStub(member3, classSource3);
            Assert(stub3.Contains("CacheItem(key, null, default);"),
                $"Expected delegation call with null for nullable reference and default for whitespace defaultValue, got:\n{stub3}");
        }

        static void Test_PatchGenerator_GenerateMemberStub_NotImplemented()
        {
            var generator = new PatchGenerator();
            string classSource = @"
public class EmptyService : ISampleService
{
}";

            var member = new InterfaceMemberInfo
            {
                InterfaceName = "ISampleService",
                MemberName = "ExecuteQuery",
                ReturnType = "void",
                ParameterList = "string sql"
            };

            string stub = generator.GenerateMemberStub(member, classSource);
            Assert(stub.Contains("throw new NotImplementedException();"), $"Expected NotImplementedException, got:\n{stub}");
        }

        static void Test_PatchGenerator_GenerateUnifiedDiff()
        {
            string oldText = "line 1\nline 2\nline 3\n";
            string newText = "line 1\nline 2\nline 2.5 added\nline 3\n";

            string diff = PatchGenerator.GenerateUnifiedDiff(oldText, newText, "a/Test.cs", "b/Test.cs");
            Assert(diff.Contains("--- a/Test.cs"), "Expected --- header");
            Assert(diff.Contains("+++ b/Test.cs"), "Expected +++ header");
            Assert(diff.Contains("+line 2.5 added"), "Expected + line");
        }

        static void Test_PatchGenerator_RepeatedMemberLookups_UseCacheAndGenerateCorrectStubs()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"patch_cache_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                string ifaceFile = Path.Combine(tempDir, "ISqlWorker.cs");
                File.WriteAllText(ifaceFile, @"
public interface ISqlWorker
{
    Task<string> ExecuteJobAsync(string jobName, CancellationToken cancellationToken = default);
    Task ClearStateAsync();
}
");
                string classFile = Path.Combine(tempDir, "SqlWorker.cs");
                File.WriteAllText(classFile, @"
public class SqlWorker : ISqlWorker
{
    public virtual async Task<string> ExecuteJobAsync(string jobName, int retryCount = 3, CancellationToken cancellationToken = default)
    {
        return ""done"";
    }
}
");
                // Diagnostics for two missing members on the same class
                var diagnostics = new List<CompilerDiagnostic>
                {
                    new CompilerDiagnostic
                    {
                        Code = "CS0535",
                        Severity = DiagnosticSeverity.Error,
                        FilePath = classFile,
                        LineNumber = 2,
                        ColumnNumber = 14,
                        Message = "'SqlWorker' does not implement interface member 'ISqlWorker.ExecuteJobAsync(string, CancellationToken)'"
                    },
                    new CompilerDiagnostic
                    {
                        Code = "CS0535",
                        Severity = DiagnosticSeverity.Error,
                        FilePath = classFile,
                        LineNumber = 2,
                        ColumnNumber = 14,
                        Message = "'SqlWorker' does not implement interface member 'ISqlWorker.ClearStateAsync()'"
                    }
                };

                var resolver = new InterfaceDefinitionResolver();
                var generator = new PatchGenerator(resolver);

                var patches = generator.GeneratePatches(diagnostics, tempDir);
                Assert(patches.Count == 1, $"Expected 1 patch file, got {patches.Count}");

                string diff = patches[0].UnifiedDiff;
                // Verify interpreted regex correctly matched the overload and delegated
                Assert(diff.Contains("return ExecuteJobAsync(jobName, 3, cancellationToken);"),
                    $"Expected delegation call in diff, got:\n{diff}");
                // Verify missing method without overload generates NotImplementedException
                Assert(diff.Contains("throw new NotImplementedException();"),
                    $"Expected NotImplementedException in diff, got:\n{diff}");

                // Delete the interface file from disk to verify second run uses cached content
                File.Delete(ifaceFile);
                var patches2 = generator.GeneratePatches(diagnostics, tempDir);
                Assert(patches2.Count == 1, "Expected patch generation to succeed from cache after file deletion.");
                Assert(patches2[0].UnifiedDiff == diff, "Expected identical diff output from cache.");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        // 5. PatchApplicator Tests
        static void Test_PatchApplicator_ApplyPatch()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"patch_test_{Guid.NewGuid():N}.cs");
            try
            {
                string originalCode = @"
namespace Demo
{
    // Class comment preserved
    public class DemoService : IDemoService
    {
        public void ExistingMethod()
        {
            // do not delete
        }
    }
}
";
                File.WriteAllText(tempFile, originalCode);

                var patchCandidate = new PatchCandidate
                {
                    TargetFilePath = tempFile,
                    TargetTypeName = "DemoService",
                    MemberSignature = "public virtual void MissingMethod()",
                    GeneratedCode = "        public virtual void MissingMethod()\n        {\n            throw new NotImplementedException();\n        }"
                };

                var applicator = new PatchApplicator();
                bool applied = applicator.ApplyPatch(patchCandidate, requireConfirmation: false);

                Assert(applied, "Expected patch application to succeed.");
                string patchedCode = File.ReadAllText(tempFile);

                Assert(patchedCode.Contains("Class comment preserved"), "Comments must be preserved.");
                Assert(patchedCode.Contains("ExistingMethod()"), "Existing methods must be preserved.");
                Assert(patchedCode.Contains("MissingMethod()"), "Missing method must be inserted.");
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        static void Test_PatchApplicator_GuardsDuplicate()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"patch_test_guard_{Guid.NewGuid():N}.cs");
            try
            {
                string originalCode = @"
public class DemoService : IDemoService
{
    public virtual void MissingMethod() { }
}
";
                File.WriteAllText(tempFile, originalCode);

                var patchCandidate = new PatchCandidate
                {
                    TargetFilePath = tempFile,
                    TargetTypeName = "DemoService",
                    MemberSignature = "public virtual void MissingMethod()",
                    GeneratedCode = "public virtual void MissingMethod() { }"
                };

                var applicator = new PatchApplicator();
                bool applied = applicator.ApplyPatch(patchCandidate, requireConfirmation: false);

                Assert(applied, "Expected patch applicator to report true on already-applied member.");
                string content = File.ReadAllText(tempFile);
                int count = content.Split(new[] { "MissingMethod" }, StringSplitOptions.None).Length - 1;
                Assert(count == 1, $"Expected exactly 1 occurrence of MissingMethod, found {count}");
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        // 6. ReportGenerator Tests
        static void Test_ReportGenerator_Markdown()
        {
            var report = new DiagnosticReport
            {
                SourceLogPath = "/tmp/build.log",
                ParseDuration = TimeSpan.FromMilliseconds(42.5),
                Diagnostics = new List<CompilerDiagnostic>
                {
                    new CompilerDiagnostic
                    {
                        Code = "CS0535",
                        Severity = DiagnosticSeverity.Error,
                        FilePath = "Services/DatabaseService.cs",
                        LineNumber = 11,
                        ColumnNumber = 36,
                        Message = "'DatabaseService' does not implement interface member 'IDatabaseService.BackupDatabaseAsync'"
                    }
                },
                TestFailures = new List<TestFailure>
                {
                    new TestFailure
                    {
                        TestClass = "MainViewModelTestsRunner",
                        AssertionFailureMessage = "Assertion Failed: Enqueue took too long"
                    }
                }
            };

            var reporter = new ReportGenerator();
            string md = reporter.GenerateMarkdownReport(report);

            Assert(md.Contains("# MSBuild Log Diagnostic Report"), "Expected title");
            Assert(md.Contains("CS0535"), "Expected CS0535 in report");
            Assert(md.Contains("MainViewModelTestsRunner"), "Expected test class in report");
            Assert(md.Contains("Enqueue took too long"), "Expected failure message in report");
        }

        static void Test_ReportGenerator_Json()
        {
            var report = new DiagnosticReport
            {
                SourceLogPath = "/tmp/build.log",
                ParseDuration = TimeSpan.FromMilliseconds(15),
                Diagnostics = new List<CompilerDiagnostic>
                {
                    new CompilerDiagnostic { Code = "CS0535", Message = "Missing member" }
                }
            };

            var reporter = new ReportGenerator();
            string json = reporter.GenerateJsonReport(report);

            Assert(json.Contains("\"CS0535\""), "Expected CS0535 in json");
            Assert(json.Contains("\"SourceLogPath\""), "Expected SourceLogPath in json");
        }

        // 7. End-to-End Workflow Test
        static void Test_EndToEnd_Workflow()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"e2e_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                string ifaceFile = Path.Combine(tempDir, "IGreeter.cs");
                File.WriteAllText(ifaceFile, @"
public interface IGreeter
{
    string Greet(string name);
}
");

                string classFile = Path.Combine(tempDir, "Greeter.cs");
                File.WriteAllText(classFile, @"
public class Greeter : IGreeter
{
    // Greeter implementation
}
");

                string mockLog = $@"
{classFile}(2,14): error CS0535: 'Greeter' does not implement interface member 'IGreeter.Greet(string)'
";

                var msbuildParser = new MSBuildLogParser();
                var diagnostics = msbuildParser.ParseText(mockLog);
                Assert(diagnostics.Count == 1, "Expected 1 diagnostic");

                var resolver = new InterfaceDefinitionResolver();
                var generator = new PatchGenerator(resolver);
                var patches = generator.GeneratePatches(diagnostics, tempDir);

                Assert(patches.Count == 1, $"Expected 1 patch generated, got {patches.Count}");
                Assert(patches[0].UnifiedDiff.Contains("+        public virtual string Greet(string name)"), "Expected Greet method in diff");

                var applicator = new PatchApplicator();
                bool applied = applicator.ApplyPatch(patches[0], requireConfirmation: false);
                Assert(applied, "Expected patch application to succeed.");

                string patchedClass = File.ReadAllText(classFile);
                Assert(patchedClass.Contains("public virtual string Greet(string name)"), "Expected Greet method in patched source file");
                Assert(patchedClass.Contains("// Greeter implementation"), "Comments must be preserved");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        // 8. Exit Code & Unresolved Error Count Tests
        static void Test_DiagnosticReport_UnresolvedErrorCount_Calculation()
        {
            var report = new DiagnosticReport
            {
                Diagnostics = new List<CompilerDiagnostic>
                {
                    new CompilerDiagnostic { Severity = DiagnosticSeverity.Error, Code = "CS0535" },
                    new CompilerDiagnostic { Severity = DiagnosticSeverity.Error, Code = "CS0535" },
                    new CompilerDiagnostic { Severity = DiagnosticSeverity.Warning, Code = "CS0168" }
                },
                CandidatePatches = new List<PatchCandidate>
                {
                    new PatchCandidate { ResolvedErrorCount = 1, IsApplied = true },
                    new PatchCandidate { ResolvedErrorCount = 1, IsApplied = false }
                }
            };

            Assert(report.ErrorCount == 2, $"Expected ErrorCount 2, got {report.ErrorCount}");
            Assert(report.UnresolvedErrorCount == 1, $"Expected UnresolvedErrorCount 1, got {report.UnresolvedErrorCount}");

            report.CandidatePatches[1].IsApplied = true;
            Assert(report.UnresolvedErrorCount == 0, $"Expected UnresolvedErrorCount 0, got {report.UnresolvedErrorCount}");
        }

        static void Test_Program_Main_ReturnsExitCode0_ForCleanBuildLog()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"main_test_clean_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                string logFile = Path.Combine(tempDir, "clean.log");
                File.WriteAllText(logFile, "Build succeeded.\n    0 Warning(s)\n    0 Error(s)\n\nTime Elapsed 00:00:01.23\n");
                string outputDir = Path.Combine(tempDir, "out");

                int exitCode = MSBuildDiagnosticPatcher.Program.Main(new[] { logFile, "-o", outputDir });
                Assert(exitCode == 0, $"Expected exit code 0 for clean build log, got {exitCode}");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        static void Test_Program_Main_ReturnsZero_WhenNoErrorsOrFailures()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"exit_zero_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                string logFile = Path.Combine(tempDir, "clean_build.log");
                string outputDir = Path.Combine(tempDir, "out");
                File.WriteAllText(logFile, @"
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:01.23
");
                int exitCode = MSBuildDiagnosticPatcher.Program.Main(new[] { logFile, "-o", outputDir });
                Assert(exitCode == 0, $"Expected exit code 0 for clean build log, got {exitCode}");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        static void Test_Program_Main_ReturnsZero_WhenWarningsOnly()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"exit_warn_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                string logFile = Path.Combine(tempDir, "warning_build.log");
                string outputDir = Path.Combine(tempDir, "out");
                File.WriteAllText(logFile, @"
ViewModels/MainViewModel.cs(120,15): warning CS0168: The variable 'ex' is declared but never used [CollationModifier.csproj]
Build succeeded.
    1 Warning(s)
    0 Error(s)
");
                int exitCode = MSBuildDiagnosticPatcher.Program.Main(new[] { logFile, "-o", outputDir });
                Assert(exitCode == 0, $"Expected exit code 0 when only warnings are present, got {exitCode}");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        static void Test_Program_Main_ReturnsExitCode1_WhenBuildErrorsRemainWithoutPatch()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"main_test_unpatched_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                string classFile = Path.Combine(tempDir, "Greeter.cs");
                File.WriteAllText(classFile, @"
public class Greeter : IGreeter
{
}
");
                string logFile = Path.Combine(tempDir, "build_error.log");
                File.WriteAllText(logFile, $@"{classFile}(2,14): error CS0535: 'Greeter' does not implement interface member 'IGreeter.Greet(string)'
");
                string outputDir = Path.Combine(tempDir, "out");

                int exitCode = MSBuildDiagnosticPatcher.Program.Main(new[] { logFile, "-o", outputDir, "-p", tempDir });
                Assert(exitCode == 1, $"Expected exit code 1 when build errors remain unpatched, got {exitCode}");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        static void Test_Program_Main_ReturnsOne_WhenCompilerErrorsPresent()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"exit_error_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                string logFile = Path.Combine(tempDir, "error_build.log");
                string outputDir = Path.Combine(tempDir, "out");
                File.WriteAllText(logFile, @"
Services/DatabaseService.cs(11,36): error CS0535: 'DatabaseService' does not implement interface member 'IDatabaseService.BackupDatabaseAsync()' [CollationModifier.csproj]
Build FAILED.
    1 Error(s)
");
                int exitCode = MSBuildDiagnosticPatcher.Program.Main(new[] { logFile, "-o", outputDir });
                Assert(exitCode == 1, $"Expected exit code 1 for compiler errors, got {exitCode}");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        static void Test_Program_Main_ReturnsExitCode1_WhenTestFailuresExist()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"main_test_failures_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                string logFile = Path.Combine(tempDir, "test_failure.log");
                File.WriteAllText(logFile, @"
=== Running Unit Tests ===
  [FAIL] GreeterTests.TestGreeting - Expected 'Hello' but was null
  [PASS] GreeterTests.TestConstructor
Test Suite Summary: 1 Passed, 1 Failed
");
                string outputDir = Path.Combine(tempDir, "out");

                int exitCode = MSBuildDiagnosticPatcher.Program.Main(new[] { logFile, "-o", outputDir });
                Assert(exitCode == 1, $"Expected exit code 1 when test runner failures exist, got {exitCode}");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        static void Test_Program_Main_ReturnsOne_WhenTestFailuresPresent()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"exit_fail_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                string logFile = Path.Combine(tempDir, "test_failure.log");
                string outputDir = Path.Combine(tempDir, "out");
                File.WriteAllText(logFile, @"
Failed MainViewModelTests.ProcessCollationChangeAsync_UpdatesCollationInMemory [45 ms]
  Error Message:
   Assertion Failed: Expected status ready
Total tests: 2. Passed: 1. Failed: 1. Skipped: 0.
");
                int exitCode = MSBuildDiagnosticPatcher.Program.Main(new[] { logFile, "-o", outputDir });
                Assert(exitCode == 1, $"Expected exit code 1 for test failures, got {exitCode}");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        static void Test_Program_Main_ReturnsOne_WhenBothErrorsAndFailuresPresent()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"exit_both_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                string logFile = Path.Combine(tempDir, "errors_and_failures.log");
                string outputDir = Path.Combine(tempDir, "out");
                File.WriteAllText(logFile, @"
Services/DatabaseService.cs(11,36): error CS0535: 'DatabaseService' does not implement interface member 'IDatabaseService.BackupDatabaseAsync()' [CollationModifier.csproj]
Failed MainViewModelTests.ProcessCollationChangeAsync_UpdatesCollationInMemory [45 ms]
  Error Message:
   Assertion Failed: Expected status ready
");
                int exitCode = MSBuildDiagnosticPatcher.Program.Main(new[] { logFile, "-o", outputDir });
                Assert(exitCode == 1, $"Expected exit code 1 for both compiler errors and test failures, got {exitCode}");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        static void Test_Program_Main_ReturnsExitCode0_WhenPatchesRemediateAllCompilerDiagnostics()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"main_test_remediated_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                string ifaceFile = Path.Combine(tempDir, "IGreeter.cs");
                File.WriteAllText(ifaceFile, @"
public interface IGreeter
{
    string Greet(string name);
}
");

                string classFile = Path.Combine(tempDir, "Greeter.cs");
                File.WriteAllText(classFile, @"
public class Greeter : IGreeter
{
    // Greeter class
}
");
                string logFile = Path.Combine(tempDir, "build.log");
                File.WriteAllText(logFile, $@"{classFile}(2,14): error CS0535: 'Greeter' does not implement interface member 'IGreeter.Greet(string)'
");
                string outputDir = Path.Combine(tempDir, "out");

                int exitCode = MSBuildDiagnosticPatcher.Program.Main(new[] { logFile, "-o", outputDir, "-p", tempDir, "-a", "-y" });
                Assert(exitCode == 0, $"Expected exit code 0 when patches remediate all diagnostics, got {exitCode}");

                string patchedContent = File.ReadAllText(classFile);
                Assert(patchedContent.Contains("public virtual string Greet(string name)"), "Expected class to be patched with Greet method");

                string jsonReportPath = Path.Combine(outputDir, "diagnostic-report.json");
                Assert(File.Exists(jsonReportPath), "Expected diagnostic-report.json to exist");
                string jsonContent = File.ReadAllText(jsonReportPath);
                Assert(jsonContent.Contains("\"IsApplied\": true"), "Expected persisted report to record IsApplied: true");
                Assert(jsonContent.Contains("\"UnresolvedErrorCount\": 0"), "Expected persisted report to record UnresolvedErrorCount: 0");

                string mdReportPath = Path.Combine(outputDir, "diagnostic-report.md");
                Assert(File.Exists(mdReportPath), "Expected diagnostic-report.md to exist");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        static void Test_Program_Main_ReturnsOne_WhenInputFileNotFound()
        {
            string missingPath = Path.Combine(Path.GetTempPath(), $"missing_log_{Guid.NewGuid():N}.log");
            int exitCode = MSBuildDiagnosticPatcher.Program.Main(new[] { missingPath });
            Assert(exitCode == 1, $"Expected exit code 1 when input file is not found, got {exitCode}");
        }

        // 9. CI Workflow Configuration Tests
        static void Test_Workflow_PostMergeBuildVerification_HasDiagnosticReportingAndArtifactUpload()
        {
            string workflowPath = FindWorkflowPath();
            string content = File.ReadAllText(workflowPath);

            // Requirement 1: Redirect build and test output to /tmp/build-and-test.log
            Assert(content.Contains("dotnet build MSBuildDiagnosticPatcher/MSBuildDiagnosticPatcher.csproj -p:UseSharedCompilation=false 2>&1 | tee /tmp/build-and-test.log"), "Workflow must redirect patcher build output to /tmp/build-and-test.log");
            Assert(content.Contains("dotnet build CollationModifier/CollationModifier.csproj -p:UseSharedCompilation=false 2>&1 | tee -a /tmp/build-and-test.log"), "Workflow must append CollationModifier build output to /tmp/build-and-test.log");
            int patcherBuildIndex = content.IndexOf("dotnet build MSBuildDiagnosticPatcher/MSBuildDiagnosticPatcher.csproj");
            int collationModifierBuildIndex = content.IndexOf("dotnet build CollationModifier/CollationModifier.csproj");
            Assert(patcherBuildIndex != -1 && collationModifierBuildIndex != -1 && patcherBuildIndex < collationModifierBuildIndex, "Workflow must build patcher before CollationModifier");
            Assert(content.Contains("tee /tmp/build-and-test.log"), "Workflow must redirect build output to /tmp/build-and-test.log using tee");
            Assert(content.Contains("tee -a /tmp/build-and-test.log"), "Workflow must append test output to /tmp/build-and-test.log using tee -a");

            // Launcher requirement: Disable shared Roslyn compilation
            string launcherPath = FindLauncherScriptPath();
            string launcherContent = File.ReadAllText(launcherPath);
            Assert(launcherContent.Contains("-p:UseSharedCompilation=false"), "Launcher script bin/msbuild-patcher must pass -p:UseSharedCompilation=false to dotnet run");

            // Requirement 2: Invoke bin/msbuild-patcher under if: failure()
            Assert(content.Contains("bin/msbuild-patcher /tmp/build-and-test.log"), "Workflow must invoke bin/msbuild-patcher /tmp/build-and-test.log");
            Assert(content.Contains("if: failure()"), "Diagnostic reporting steps must run under if: failure()");

            // Requirement 4 & 6: Verify diagnostic-report.md exists before writing to $GITHUB_STEP_SUMMARY
            Assert(content.Contains("build-diagnostics/diagnostic-report.md"), "Workflow must reference build-diagnostics/diagnostic-report.md");
            Assert(content.Contains("$GITHUB_STEP_SUMMARY"), "Workflow must write to $GITHUB_STEP_SUMMARY");
            Assert(content.Contains("-f \"build-diagnostics/diagnostic-report.md\"") || content.Contains("-f build-diagnostics/diagnostic-report.md"), "Workflow must verify diagnostic-report.md exists before appending");

            // Requirement 5 & Criterion 3: Upload build-diagnostics-report artifact via actions/upload-artifact@v4
            Assert(content.Contains("actions/upload-artifact@v4"), "Workflow must use actions/upload-artifact@v4");
            Assert(content.Contains("name: build-diagnostics-report"), "Workflow must name artifact build-diagnostics-report");
            Assert(content.Contains("build-diagnostics/"), "Workflow artifact path must include build-diagnostics/");
            Assert(content.Contains("/tmp/build-and-test.log"), "Workflow artifact path must include /tmp/build-and-test.log");
        }

        private static string FindWorkflowPath()
        {
            string? dir = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(dir))
            {
                string candidate = Path.Combine(dir, ".github", "workflows", "post-merge-build-verification.yml");
                if (File.Exists(candidate)) return candidate;
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            dir = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                string candidate = Path.Combine(dir, ".github", "workflows", "post-merge-build-verification.yml");
                if (File.Exists(candidate)) return candidate;
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            throw new FileNotFoundException("post-merge-build-verification.yml could not be found.");
        }

        private static string FindLauncherScriptPath()
        {
            string? dir = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(dir))
            {
                string candidate = Path.Combine(dir, "bin", "msbuild-patcher");
                if (File.Exists(candidate)) return candidate;
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            dir = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                string candidate = Path.Combine(dir, "bin", "msbuild-patcher");
                if (File.Exists(candidate)) return candidate;
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            throw new FileNotFoundException("bin/msbuild-patcher could not be found.");
        }
    }
}
