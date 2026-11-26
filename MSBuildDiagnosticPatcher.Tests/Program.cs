using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
            RunTest("MSBuildLogParser_ParseLine_ExtractsNetSdkErrorWithCoordinates", Test_MSBuildLogParser_ParseLine_ExtractsNetSdkErrorWithCoordinates);
            RunTest("MSBuildLogParser_ParseLine_ExtractsSyslibWarningWithCoordinates", Test_MSBuildLogParser_ParseLine_ExtractsSyslibWarningWithCoordinates);
            RunTest("MSBuildLogParser_ParseLine_ExtractsNuGetRestoreErrorWithoutCoordinates", Test_MSBuildLogParser_ParseLine_ExtractsNuGetRestoreErrorWithoutCoordinates);
            RunTest("MSBuildLogParser_ParseLine_ExtractsNetSdkCoordinateFreeError", Test_MSBuildLogParser_ParseLine_ExtractsNetSdkCoordinateFreeError);
            RunTest("MSBuildLogParser_ParseLine_ExtractsCoordinateFreeInformationalSeverity", Test_MSBuildLogParser_ParseLine_ExtractsCoordinateFreeInformationalSeverity);
            RunTest("MSBuildLogParser_ParseLine_CoordinateFreeUnrecognizedSeverity_DefaultsToError", Test_MSBuildLogParser_ParseLine_CoordinateFreeUnrecognizedSeverity_DefaultsToError);
            RunTest("MSBuildLogParser_ParseLine_SpacedPathWithDriveColonAndCoordinates", Test_MSBuildLogParser_ParseLine_SpacedPathWithDriveColonAndCoordinates);
            RunTest("MSBuildLogParser_ParseLine_SpacedPathWithCoordinatesWithoutColumnNumber", Test_MSBuildLogParser_ParseLine_SpacedPathWithCoordinatesWithoutColumnNumber);
            RunTest("MSBuildLogParser_ParseLine_FatalErrorSeverityWithCoordinates", Test_MSBuildLogParser_ParseLine_FatalErrorSeverityWithCoordinates);
            RunTest("MSBuildLogParser_ParseLine_CoordinateFreeRoslynErrors_CS_BC_FS", Test_MSBuildLogParser_ParseLine_CoordinateFreeRoslynErrors_CS_BC_FS);
            RunTest("MSBuildLogParser_ParseLine_CoordinateFreeRoslynFatalError", Test_MSBuildLogParser_ParseLine_CoordinateFreeRoslynFatalError);
            RunTest("MSBuildLogParser_ParseLine_CoordinateFreeSpacedPathWithDriveColon", Test_MSBuildLogParser_ParseLine_CoordinateFreeSpacedPathWithDriveColon);
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
            RunTest("TestExecutionLogParser_ParseUnhandledDotnetCrash_ExtractsExceptionAndStackTrace", Test_TestExecutionLogParser_ParseUnhandledDotnetCrash);
            RunTest("TestExecutionLogParser_ParseMultilineAndInnerExceptionChain_PreservesFullDetails", Test_TestExecutionLogParser_ParseMultilineAndInnerExceptionChain);
            RunTest("TestExecutionLogParser_ParseVsTestSubMillisecondParameterizedAndMultiline_PreservesAllFrames", Test_TestExecutionLogParser_ParseVsTestSubMillisecondParameterizedAndMultiline);
            RunTest("TestExecutionLogParser_ParseCustomSuites_ExactFailureCountsAndStackTraces", Test_TestExecutionLogParser_ParseCustomSuites_ExactFailureCountsAndStackTraces);
            RunTest("TestExecutionLogParser_ParsePerformance_Under10Seconds", Test_TestExecutionLogParser_ParsePerformance_Under10Seconds);

            // 3. InterfaceDefinitionResolver Tests
            RunTest("InterfaceDefinitionResolver_ParseCs0535Message_ExtractsComponents", Test_InterfaceDefinitionResolver_ParseCs0535Message);
            RunTest("InterfaceDefinitionResolver_ExtractMemberFromInterfaceSource_FindsMethod", Test_InterfaceDefinitionResolver_ExtractMemberFromInterfaceSource);
            RunTest("InterfaceDefinitionResolver_ExtractMemberFromInterfaceSource_FindsProperty", Test_InterfaceDefinitionResolver_ExtractProperty);
            RunTest("InterfaceDefinitionResolver_Caching_ReadsFileOnce", Test_InterfaceDefinitionResolver_Caching_ReadsFileOnce);
            RunTest("InterfaceDefinitionResolver_NegativeCaching_PreventsRepetitiveSearches", Test_InterfaceDefinitionResolver_NegativeCaching_PreventsRepetitiveSearches);
            RunTest("InterfaceDefinitionResolver_ClearCache_InvalidatesFileAndContentCache", Test_InterfaceDefinitionResolver_ClearCache_InvalidatesFileAndContentCache);
            RunTest("InterfaceDefinitionResolver_ExtractMember_IgnoresDefaultCallsAndComments", Test_InterfaceDefinitionResolver_ExtractMember_IgnoresDefaultCallsAndComments);
            RunTest("InterfaceDefinitionResolver_ExtractMember_OverloadsPrecedenceAndSameNameReturnType", Test_InterfaceDefinitionResolver_ExtractMember_OverloadsPrecedenceAndSameNameReturnType);
            RunTest("InterfaceDefinitionResolver_ClearCache_InvalidatesCachedBodyOffsets", Test_InterfaceDefinitionResolver_ClearCache_InvalidatesCachedBodyOffsets);

            // 4. PatchGenerator Tests
            RunTest("PatchGenerator_FindClassInsertionPoint_FindsClassClosingBrace", Test_PatchGenerator_FindClassInsertionPoint);
            RunTest("PatchGenerator_GenerateMemberStub_DelegatesToExistingOverload", Test_PatchGenerator_GenerateMemberStub_Delegates);
            RunTest("PatchGenerator_GenerateMemberStub_DelegatesWithValueTypeParameters", Test_PatchGenerator_GenerateMemberStub_DelegatesWithValueTypeParameters);
            RunTest("PatchGenerator_ExtractParameterNames_HandlesDefaultsNullablesAndWhitespace", Test_PatchGenerator_ExtractParameterNames_HandlesDefaultsNullablesAndWhitespace);
            RunTest("PatchGenerator_FindDelegationCall_MatchesCandidateOverloadsConsistently", Test_PatchGenerator_FindDelegationCall_MatchesCandidateOverloadsConsistently);
            RunTest("PatchGenerator_GenerateMemberStub_GeneratesNotImplementedException", Test_PatchGenerator_GenerateMemberStub_NotImplemented);
            RunTest("PatchGenerator_GenerateUnifiedDiff_ProducesValidDiffHeader", Test_PatchGenerator_GenerateUnifiedDiff);
            RunTest("PatchGenerator_RepeatedMemberLookups_UseCacheAndGenerateCorrectStubs", Test_PatchGenerator_RepeatedMemberLookups_UseCacheAndGenerateCorrectStubs);
            RunTest("PatchGenerator_GenerateMemberStub_LinearScalingOnLargeWordBlocks", Test_PatchGenerator_GenerateMemberStub_LinearScalingOnLargeWordBlocks);
            RunTest("PatchGenerator_FindDelegationCall_HandlesSameNameReturnTypeAndOverloadCache", Test_PatchGenerator_FindDelegationCall_HandlesSameNameReturnTypeAndOverloadCache);

            // 5. PatchApplicator Tests
            RunTest("PatchApplicator_ApplyPatch_InsertsCodeWithoutDamagingComments", Test_PatchApplicator_ApplyPatch);
            RunTest("PatchApplicator_ApplyPatch_GuardsAgainstDuplicateApplication", Test_PatchApplicator_GuardsDuplicate);

            // 6. ReportGenerator Tests
            RunTest("ReportGenerator_GenerateMarkdownReport_ProducesFormattedMarkdown", Test_ReportGenerator_Markdown);
            RunTest("ReportGenerator_GenerateMarkdownReport_ReplacesLineBreaksInTableCellsAndKeepsDetails", Test_ReportGenerator_Markdown_MultilineTableCellsAndDetails);
            RunTest("ReportGenerator_GenerateJsonReport_ProducesValidJson", Test_ReportGenerator_Json);
            RunTest("ReportGenerator_PrintConsoleSummary_FiltersErrorsAndSkipsWhenNoErrors", Test_ReportGenerator_PrintConsoleSummary);

            // 7. End-to-End Diagnostic & Patching Workflow
            RunTest("EndToEnd_ParseLogAndGeneratePatch_RestoresInterfaceImplementation", Test_EndToEnd_Workflow);

            // 8. Program.Main Exit Code & Unresolved Error Count Tests
            RunTest("DiagnosticReport_UnresolvedErrorCount_Calculation", Test_DiagnosticReport_UnresolvedErrorCount_Calculation);
            RunTest("DiagnosticReport_SeverityCounts_TrackInfoWarningErrorMutations", Test_DiagnosticReport_SeverityCounts_TrackInfoWarningErrorMutations);
            RunTest("DiagnosticReport_ErrorAndWarningCounts_ZeroAllocations", Test_DiagnosticReport_ErrorAndWarningCounts_ZeroAllocations);
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
            RunTest("Program_Main_ReturnsExitCode1_ForUnhandledDotnetCrashAndPersistsJsonDetails", Test_Program_Main_ReturnsExitCode1_ForUnhandledDotnetCrashAndPersistsJsonDetails);

            // 9. CI Workflow Configuration Tests
            RunTest("Workflow_PostMergeBuildVerification_HasDiagnosticReportingAndArtifactUpload", Test_Workflow_PostMergeBuildVerification_HasDiagnosticReportingAndArtifactUpload);

            // 10. Single-Pass Streaming & Regex Guard Regression Tests
            RunTest("AnsiCleaner_Clean_FastPathSkipsRegexWithoutEscapeChar", Test_AnsiCleaner_Clean_FastPathSkipsRegexWithoutEscapeChar);
            RunTest("MSBuildLogParser_Guards_CaseInsensitivePrefixesAndAnySeverityWord", Test_MSBuildLogParser_Guards_CaseInsensitivePrefixesAndAnySeverityWord);
            RunTest("MSBuildLogParser_Guards_FastOnSevenColonProseLines", Test_MSBuildLogParser_Guards_FastOnSevenColonProseLines);
            RunTest("TestExecutionLogParser_Streaming_CaseInsensitivePrefixesAndConsecutiveFailures", Test_TestExecutionLogParser_Streaming_CaseInsensitivePrefixesAndConsecutiveFailures);
            RunTest("LogStreamParser_SinglePassStreaming_MatchesDualPassParseTextOnMixedLogs", Test_LogStreamParser_SinglePassStreaming_MatchesDualPassParseTextOnMixedLogs);

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
                string className = testAction.Method.DeclaringType?.Name ?? nameof(Program);
                string qualifiedName = testName.Contains('.') ? testName : $"{className}.{testName}";
                Console.WriteLine($"  [FAIL] {qualifiedName} - {ex}");
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

        static void Test_MSBuildLogParser_ParseLine_ExtractsNetSdkErrorWithCoordinates()
        {
            var parser = new MSBuildLogParser();
            string line = "targets(112,5): error NETSDK1005: Target framework 'net8.0' is missing [CollationModifier.csproj]";

            var diag = parser.ParseLine(line);
            Assert(diag != null, "Expected NETSDK error with coordinates to be parsed.");
            Assert(diag!.Code == "NETSDK1005", $"Expected code NETSDK1005, got {diag.Code}");
            Assert(diag.Severity == DiagnosticSeverity.Error, $"Expected severity Error, got {diag.Severity}");
            Assert(diag.LineNumber == 112, $"Expected line 112, got {diag.LineNumber}");
            Assert(diag.ColumnNumber == 5, $"Expected column 5, got {diag.ColumnNumber}");
            Assert(diag.FilePath == "targets", $"Expected FilePath 'targets', got '{diag.FilePath}'");
            Assert(diag.ProjectPath == "CollationModifier.csproj", $"Expected ProjectPath 'CollationModifier.csproj', got '{diag.ProjectPath}'");
            Assert(diag.Message.Contains("Target framework 'net8.0' is missing"), "Expected message to contain diagnostic description");
        }

        static void Test_MSBuildLogParser_ParseLine_ExtractsSyslibWarningWithCoordinates()
        {
            var parser = new MSBuildLogParser();
            string line = "Security/Crypto.cs(42,13): warning SYSLIB0011: 'BinaryFormatter' is obsolete [CollationModifier.csproj]";

            var diag = parser.ParseLine(line);
            Assert(diag != null, "Expected SYSLIB warning with coordinates to be parsed.");
            Assert(diag!.Code == "SYSLIB0011", $"Expected code SYSLIB0011, got {diag.Code}");
            Assert(diag.Severity == DiagnosticSeverity.Warning, $"Expected severity Warning, got {diag.Severity}");
            Assert(diag.LineNumber == 42, $"Expected line 42, got {diag.LineNumber}");
            Assert(diag.ColumnNumber == 13, $"Expected column 13, got {diag.ColumnNumber}");
            Assert(diag.FilePath == "Security/Crypto.cs", $"Expected FilePath 'Security/Crypto.cs', got '{diag.FilePath}'");
            Assert(diag.ProjectPath == "CollationModifier.csproj", $"Expected ProjectPath 'CollationModifier.csproj', got '{diag.ProjectPath}'");
            Assert(diag.Message.Contains("'BinaryFormatter' is obsolete"), "Expected message to contain obsolete warning text");
        }

        static void Test_MSBuildLogParser_ParseLine_ExtractsNuGetRestoreErrorWithoutCoordinates()
        {
            var parser = new MSBuildLogParser();
            string line = "/app/CollationModifier/CollationModifier.csproj : error NU1605: Detected package downgrade: Microsoft.Data.SqlClient from 5.1.0 to 5.0.0 [/app/CollationModifier/CollationModifier.csproj]";

            var diag = parser.ParseLine(line);
            Assert(diag != null, "Expected NU1605 error without coordinates to be parsed.");
            Assert(diag!.Code == "NU1605", $"Expected code NU1605, got {diag.Code}");
            Assert(diag.Severity == DiagnosticSeverity.Error, $"Expected severity Error, got {diag.Severity}");
            Assert(diag.LineNumber == 0, $"Expected line 0 for coordinate-free diagnostic, got {diag.LineNumber}");
            Assert(diag.ColumnNumber == 0, $"Expected column 0 for coordinate-free diagnostic, got {diag.ColumnNumber}");
            Assert(diag.FilePath == "/app/CollationModifier/CollationModifier.csproj", $"Expected FilePath '/app/CollationModifier/CollationModifier.csproj', got '{diag.FilePath}'");
            Assert(diag.ProjectPath == "/app/CollationModifier/CollationModifier.csproj", $"Expected ProjectPath '/app/CollationModifier/CollationModifier.csproj', got '{diag.ProjectPath}'");
            Assert(diag.Message.Contains("Detected package downgrade"), "Expected message to contain package downgrade text");
        }

        static void Test_MSBuildLogParser_ParseLine_ExtractsNetSdkCoordinateFreeError()
        {
            var parser = new MSBuildLogParser();
            string line = "/app/Project.csproj : error NETSDK1045: The current .NET SDK does not support targeting .NET 9.0.";

            var diag = parser.ParseLine(line);
            Assert(diag != null, "Expected NETSDK1045 coordinate-free error to be parsed.");
            Assert(diag!.Code == "NETSDK1045", $"Expected code NETSDK1045, got {diag.Code}");
            Assert(diag.Severity == DiagnosticSeverity.Error, $"Expected severity Error, got {diag.Severity}");
            Assert(diag.LineNumber == 0, $"Expected line 0 for coordinate-free diagnostic, got {diag.LineNumber}");
            Assert(diag.ColumnNumber == 0, $"Expected column 0 for coordinate-free diagnostic, got {diag.ColumnNumber}");
            Assert(diag.FilePath == "/app/Project.csproj", $"Expected FilePath '/app/Project.csproj', got '{diag.FilePath}'");
            Assert(diag.Message.Contains("The current .NET SDK does not support targeting .NET 9.0."), "Expected message to match");
        }

        static void Test_MSBuildLogParser_ParseLine_ExtractsCoordinateFreeInformationalSeverity()
        {
            var parser = new MSBuildLogParser();
            string line = "CollationModifier.csproj : info NU1503: Skipping restore for project 'CollationModifier.csproj'.";

            var diag = parser.ParseLine(line);
            Assert(diag != null, "Expected NU1503 info diagnostic to be parsed.");
            Assert(diag!.Code == "NU1503", $"Expected code NU1503, got {diag.Code}");
            Assert(diag.Severity == DiagnosticSeverity.Info, $"Expected severity Info, got {diag.Severity}");
            Assert(diag.LineNumber == 0, $"Expected line 0, got {diag.LineNumber}");
            Assert(diag.ColumnNumber == 0, $"Expected column 0, got {diag.ColumnNumber}");
            Assert(diag.FilePath == "CollationModifier.csproj", $"Expected FilePath 'CollationModifier.csproj', got '{diag.FilePath}'");
            Assert(diag.Message.Contains("Skipping restore for project"), "Expected info message to match");
        }

        static void Test_MSBuildLogParser_ParseLine_CoordinateFreeUnrecognizedSeverity_DefaultsToError()
        {
            var parser = new MSBuildLogParser();
            string line = "MSBUILD : unexpected MSB1003: Unrecognized build failure.";

            var diag = parser.ParseLine(line);
            Assert(diag != null, "Expected diagnostic with unrecognized severity to be parsed.");
            Assert(diag!.Code == "MSB1003", $"Expected code MSB1003, got {diag.Code}");
            Assert(diag.Severity == DiagnosticSeverity.Error, $"Expected unrecognized severity to default to Error, got {diag.Severity}");
            Assert(diag.LineNumber == 0, $"Expected line 0, got {diag.LineNumber}");
            Assert(diag.ColumnNumber == 0, $"Expected column 0, got {diag.ColumnNumber}");
        }

        static void Test_MSBuildLogParser_ParseLine_SpacedPathWithDriveColonAndCoordinates()
        {
            var parser = new MSBuildLogParser();
            string line = @"C:\My Projects\Solution Directory\Services\Database Service.cs(11,36): error CS0535: 'DatabaseService' does not implement interface member 'IDatabaseService.BackupDatabaseAsync(string, string, CancellationToken)' [C:\My Projects\Solution Directory\App.csproj]";

            var diag = parser.ParseLine(line);
            Assert(diag != null, "Expected diagnostic with drive colon and spaced path to be parsed.");
            Assert(diag!.FilePath == @"C:\My Projects\Solution Directory\Services\Database Service.cs", $"Expected spaced path with drive colon, got '{diag.FilePath}'");
            Assert(diag.LineNumber == 11, $"Expected line 11, got {diag.LineNumber}");
            Assert(diag.ColumnNumber == 36, $"Expected col 36, got {diag.ColumnNumber}");
            Assert(diag.Severity == DiagnosticSeverity.Error, $"Expected severity Error, got {diag.Severity}");
            Assert(diag.Code == "CS0535", $"Expected code CS0535, got {diag.Code}");
            Assert(diag.ProjectPath == @"C:\My Projects\Solution Directory\App.csproj", $"Expected project path, got '{diag.ProjectPath}'");
            Assert(diag.Message.Contains("does not implement interface member"), "Expected CS0535 message");
        }

        static void Test_MSBuildLogParser_ParseLine_SpacedPathWithCoordinatesWithoutColumnNumber()
        {
            var parser = new MSBuildLogParser();
            string line = @"C:\Program Files\DotNet Project\Module.cs(42): error CS0103: The name 'myVar' does not exist in the current context";

            var diag = parser.ParseLine(line);
            Assert(diag != null, "Expected diagnostic without column number to be parsed.");
            Assert(diag!.FilePath == @"C:\Program Files\DotNet Project\Module.cs", $"Expected file path, got '{diag.FilePath}'");
            Assert(diag.LineNumber == 42, $"Expected line 42, got {diag.LineNumber}");
            Assert(diag.ColumnNumber == 0, $"Expected column 0 when omitted, got {diag.ColumnNumber}");
            Assert(diag.Severity == DiagnosticSeverity.Error, $"Expected severity Error, got {diag.Severity}");
            Assert(diag.Code == "CS0103", $"Expected code CS0103, got {diag.Code}");
            Assert(diag.Message.Contains("does not exist in the current context"), "Expected error message");
        }

        static void Test_MSBuildLogParser_ParseLine_FatalErrorSeverityWithCoordinates()
        {
            var parser = new MSBuildLogParser();
            string line = @"D:\Build Workspace\App\Compiler.cs(100,5): fatal error CS0009: Metadata file 'System.Core.dll' could not be opened";

            var diag = parser.ParseLine(line);
            Assert(diag != null, "Expected fatal error diagnostic to be parsed.");
            Assert(diag!.FilePath == @"D:\Build Workspace\App\Compiler.cs", $"Expected file path, got '{diag.FilePath}'");
            Assert(diag.LineNumber == 100, $"Expected line 100, got {diag.LineNumber}");
            Assert(diag.ColumnNumber == 5, $"Expected column 5, got {diag.ColumnNumber}");
            Assert(diag.Severity == DiagnosticSeverity.Error, $"Expected severity Error for fatal error, got {diag.Severity}");
            Assert(diag.Code == "CS0009", $"Expected code CS0009, got {diag.Code}");
            Assert(diag.Message.Contains("Metadata file"), "Expected fatal error message");
        }

        static void Test_MSBuildLogParser_ParseLine_CoordinateFreeRoslynErrors_CS_BC_FS()
        {
            var parser = new MSBuildLogParser();

            // CS Roslyn error without coordinates
            string lineCs = "CSC : error CS5001: Program does not contain a static 'Main' method suitable for an entry point";
            var diagCs = parser.ParseLine(lineCs);
            Assert(diagCs != null, "Expected CS5001 coordinate-free error to be parsed.");
            Assert(diagCs!.FilePath == "CSC", $"Expected FilePath 'CSC', got '{diagCs.FilePath}'");
            Assert(diagCs.LineNumber == 0, $"Expected line 0, got {diagCs.LineNumber}");
            Assert(diagCs.ColumnNumber == 0, $"Expected column 0, got {diagCs.ColumnNumber}");
            Assert(diagCs.Severity == DiagnosticSeverity.Error, $"Expected severity Error, got {diagCs.Severity}");
            Assert(diagCs.Code == "CS5001", $"Expected code CS5001, got {diagCs.Code}");

            // BC Visual Basic error without coordinates
            string lineBc = "vbc : error BC2001: file 'Main.vb' could not be found";
            var diagBc = parser.ParseLine(lineBc);
            Assert(diagBc != null, "Expected BC2001 coordinate-free error to be parsed.");
            Assert(diagBc!.FilePath == "vbc", $"Expected FilePath 'vbc', got '{diagBc.FilePath}'");
            Assert(diagBc.LineNumber == 0, $"Expected line 0, got {diagBc.LineNumber}");
            Assert(diagBc.ColumnNumber == 0, $"Expected column 0, got {diagBc.ColumnNumber}");
            Assert(diagBc.Severity == DiagnosticSeverity.Error, $"Expected severity Error, got {diagBc.Severity}");
            Assert(diagBc.Code == "BC2001", $"Expected code BC2001, got {diagBc.Code}");

            // FS F# error without coordinates
            string lineFs = "fsc : error FS0074: The type referenced through 'Module' is defined at an inaccessible location";
            var diagFs = parser.ParseLine(lineFs);
            Assert(diagFs != null, "Expected FS0074 coordinate-free error to be parsed.");
            Assert(diagFs!.FilePath == "fsc", $"Expected FilePath 'fsc', got '{diagFs.FilePath}'");
            Assert(diagFs.LineNumber == 0, $"Expected line 0, got {diagFs.LineNumber}");
            Assert(diagFs.ColumnNumber == 0, $"Expected column 0, got {diagFs.ColumnNumber}");
            Assert(diagFs.Severity == DiagnosticSeverity.Error, $"Expected severity Error, got {diagFs.Severity}");
            Assert(diagFs.Code == "FS0074", $"Expected code FS0074, got {diagFs.Code}");
        }

        static void Test_MSBuildLogParser_ParseLine_CoordinateFreeRoslynFatalError()
        {
            var parser = new MSBuildLogParser();
            string line = "CSC : fatal error CS0009: Metadata file 'System.Runtime.dll' could not be opened";

            var diag = parser.ParseLine(line);
            Assert(diag != null, "Expected coordinate-free fatal error diagnostic to be parsed.");
            Assert(diag!.FilePath == "CSC", $"Expected FilePath 'CSC', got '{diag.FilePath}'");
            Assert(diag.LineNumber == 0, $"Expected line 0, got {diag.LineNumber}");
            Assert(diag.ColumnNumber == 0, $"Expected column 0, got {diag.ColumnNumber}");
            Assert(diag.Severity == DiagnosticSeverity.Error, $"Expected severity Error, got {diag.Severity}");
            Assert(diag.Code == "CS0009", $"Expected code CS0009, got {diag.Code}");
            Assert(diag.Message.Contains("Metadata file 'System.Runtime.dll' could not be opened"), "Expected message to match");
        }

        static void Test_MSBuildLogParser_ParseLine_CoordinateFreeSpacedPathWithDriveColon()
        {
            var parser = new MSBuildLogParser();
            string line = @"C:\My Projects\Solution Directory\App.csproj : error CS5001: Program does not contain a static 'Main' method [C:\My Projects\Solution Directory\App.csproj]";

            var diag = parser.ParseLine(line);
            Assert(diag != null, "Expected coordinate-free diagnostic with drive colon and spaced path to be parsed.");
            Assert(diag!.FilePath == @"C:\My Projects\Solution Directory\App.csproj", $"Expected file path 'C:\\My Projects\\Solution Directory\\App.csproj', got '{diag.FilePath}'");
            Assert(diag.LineNumber == 0, $"Expected line 0, got {diag.LineNumber}");
            Assert(diag.ColumnNumber == 0, $"Expected column 0, got {diag.ColumnNumber}");
            Assert(diag.Severity == DiagnosticSeverity.Error, $"Expected severity Error, got {diag.Severity}");
            Assert(diag.Code == "CS5001", $"Expected code CS5001, got {diag.Code}");
            Assert(diag.ProjectPath == @"C:\My Projects\Solution Directory\App.csproj", $"Expected project path, got '{diag.ProjectPath}'");

            // Also verify non-greedy path matching does not consume trailing delimiters with extra colons in message
            string colonLine = @"C:\Source Code\Project\File.cs : error CS0100: Multiple colons in message: part 1: part 2";
            var colonDiag = parser.ParseLine(colonLine);
            Assert(colonDiag != null, "Expected diagnostic with colons in message to parse.");
            Assert(colonDiag!.FilePath == @"C:\Source Code\Project\File.cs", $"Expected file path without trailing delimiters, got '{colonDiag.FilePath}'");
            Assert(colonDiag.Message == "Multiple colons in message: part 1: part 2", $"Expected full message preserved, got '{colonDiag.Message}'");
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

        static void Test_TestExecutionLogParser_ParseUnhandledDotnetCrash()
        {
            var parser = new TestExecutionLogParser();
            string log = @"
=== Running CollationModifier Unit Tests ===
  [PASS] MainViewModel_SetStatus_UpdatesProperties
Unhandled exception. System.InvalidOperationException: Unhandled async void failure on UI thread
   at System.Threading.Tasks.Task.<>c.<ThrowAsync>b__128_0(Object state)
   at CollationModifier.ViewModels.MainViewModel.<ExecuteBrokenCommand>d__42.MoveNext() in /app/CollationModifier/ViewModels/MainViewModel.cs:line 312
--- End of stack trace from previous location ---
   at CollationModifier.Tests.MainViewModelTests.RunBrokenScenario() in /app/CollationModifier.Tests/MainViewModelTests.cs:line 890
Unhandled Exception: System.NullReferenceException: Object reference not set to an instance of an object.
   at CollationModifier.Program.Main(String[] args) in C:\src\CollationModifier\Program.cs:line 24
";

            var failures = parser.ParseText(log);
            Assert(failures.Count == 2, $"Expected 2 crash failures ('Unhandled exception.' and 'Unhandled Exception:'), got {failures.Count}");

            // First crash: Unhandled exception.
            Assert(failures[0].TestClass == "CollationModifier.ViewModels.MainViewModel", $"Expected user class from async frame, got '{failures[0].TestClass}'");
            Assert(failures[0].TestMethod == "ExecuteBrokenCommand", $"Expected async method ExecuteBrokenCommand, got '{failures[0].TestMethod}'");
            Assert(failures[0].AssertionFailureMessage.Contains("Unhandled async void failure on UI thread"), "Expected crash exception message.");
            Assert(failures[0].StackTrace.Contains("ExecuteBrokenCommand") &&
                   failures[0].StackTrace.Contains("--- End of stack trace from previous location ---") &&
                   failures[0].StackTrace.Contains("RunBrokenScenario"),
                   "Expected complete stack trace across async location boundary.");

            // Second crash: Unhandled Exception:
            Assert(failures[1].TestClass == "CollationModifier.Program", $"Expected CollationModifier.Program, got '{failures[1].TestClass}'");
            Assert(failures[1].TestMethod == "Main", $"Expected Main, got '{failures[1].TestMethod}'");
            Assert(failures[1].AssertionFailureMessage.Contains("Object reference not set to an instance of an object."), "Expected NullReferenceException message.");
            Assert(failures[1].StackTrace.Contains("at CollationModifier.Program.Main"), "Expected stack trace for second crash.");
        }

        static void Test_TestExecutionLogParser_ParseMultilineAndInnerExceptionChain()
        {
            var parser = new TestExecutionLogParser();
            string log = @"
  [FAIL] MainViewModelTests.ConnectAsync_WithNestedException - System.InvalidOperationException: High-level connection workflow failed
Expected: IsConnected == True
Actual:   IsConnected == False
 ---> System.Data.Common.DbException: Login failed for user 'sa'.
Server rejected credentials on port 1433.
   at CollationModifier.Services.DatabaseService.OpenConnectionAsync() in /app/DatabaseService.cs:line 77
--- End of stack trace from previous location ---
   at CollationModifier.ViewModels.MainViewModel.ConnectAsync() in /app/MainViewModel.cs:line 145
   --- End of inner exception stack trace ---
   at CollationModifier.Tests.MainViewModelTests.ConnectAsync_WithNestedException() in /app/MainViewModelTests.cs:line 520
  [PASS] MainViewModelTests.Disconnect_ResetsState
";

            var failures = parser.ParseText(log);
            Assert(failures.Count == 1, $"Expected 1 failure, got {failures.Count}");

            var f = failures[0];
            Assert(f.TestClass == "MainViewModelTests", $"Expected MainViewModelTests, got '{f.TestClass}'");
            Assert(f.TestMethod == "ConnectAsync_WithNestedException", $"Expected ConnectAsync_WithNestedException, got '{f.TestMethod}'");
            Assert(f.AssertionFailureMessage.Contains("High-level connection workflow failed"), "Missing outer message");
            Assert(f.AssertionFailureMessage.Contains("Expected: IsConnected == True") && f.AssertionFailureMessage.Contains("Actual:   IsConnected == False"), "Missing multiline Expected/Actual lines");
            Assert(f.AssertionFailureMessage.Contains("---> System.Data.Common.DbException: Login failed for user 'sa'."), "Missing ---> inner exception header");
            Assert(f.AssertionFailureMessage.Contains("Server rejected credentials on port 1433."), "Missing multiline inner exception continuation");

            Assert(f.StackTrace.Contains("OpenConnectionAsync") &&
                   f.StackTrace.Contains("--- End of stack trace from previous location ---") &&
                   f.StackTrace.Contains("MainViewModel.ConnectAsync") &&
                   f.StackTrace.Contains("--- End of inner exception stack trace ---") &&
                   f.StackTrace.Contains("ConnectAsync_WithNestedException"),
                   $"Expected all inner, async, and outer stack frames in StackTrace, got:\n{f.StackTrace}");
        }

        static void Test_TestExecutionLogParser_ParseVsTestSubMillisecondParameterizedAndMultiline()
        {
            var parser = new TestExecutionLogParser();
            string log = @"
Starting test execution, please wait...
A total of 1 test files matched the specified pattern.
  Failed CollationModifier.Tests.ParserSuite.ParseFile_WithOptions(fileName: ""build.log"", threshold: 1.5, tags: ""[fast, ci]"") [< 1 ms]
  Error Message:
   Assert.Equal() Failure: Strings differ
   Expected: ""SQL_Latin1_General_CP1_CI_AS""
   Actual:   ""Latin1_General_BIN""
   ---> System.FormatException: Collation metadata mismatch
  Stack Trace:
     at CollationModifier.Tests.ParserSuite.VerifyCollationAsync(String actual) in /app/ParserSuite.cs:line 42
  --- End of stack trace from previous location ---
     at CollationModifier.Tests.ParserSuite.ParseFile_WithOptions(String fileName, Double threshold, String tags) in /app/ParserSuite.cs:line 88
  Standard Output Messages:
   Debug trace line that should not corrupt stack trace

  Failed CollationModifier.Tests.GenericSuite.ValidateValue<System.String, System.Int32>(input: ""a | b"", count: 2) [1 s 15 ms]
  Error Message:
   Expected count 2 to match actual count 0
  Stack Trace:
     at CollationModifier.Tests.GenericSuite.InnerCheck() in /app/GenericSuite.cs:line 19
   ----- Inner Stack Trace -----
     at CollationModifier.Tests.GenericSuite.ValidateValue[T1,T2](T1 input, T2 count) in /app/GenericSuite.cs:line 33

Failed!  - Failed:     2, Passed:     5, Skipped:     0, Total:     7, Duration: 1 s - CollationModifier.Tests.dll (net8.0)
";

            var failures = parser.ParseText(log);
            Assert(failures.Count == 2, $"Expected 2 VSTest failures (ignoring 'Failed!' summary line), got {failures.Count}");

            Assert(failures[0].TestClass == "CollationModifier.Tests.ParserSuite", $"Expected CollationModifier.Tests.ParserSuite, got '{failures[0].TestClass}'");
            Assert(failures[0].TestMethod == "ParseFile_WithOptions(fileName: \"build.log\", threshold: 1.5, tags: \"[fast, ci]\")",
                $"Unexpected parameterized TestMethod: '{failures[0].TestMethod}'");
            Assert(failures[0].AssertionFailureMessage.Contains("Assert.Equal() Failure: Strings differ") &&
                   failures[0].AssertionFailureMessage.Contains("Expected: \"SQL_Latin1_General_CP1_CI_AS\"") &&
                   failures[0].AssertionFailureMessage.Contains("Actual:   \"Latin1_General_BIN\"") &&
                   failures[0].AssertionFailureMessage.Contains("---> System.FormatException: Collation metadata mismatch"),
                   $"Expected full multiline VSTest Error Message, got:\n{failures[0].AssertionFailureMessage}");
            Assert(failures[0].StackTrace.Contains("VerifyCollationAsync") &&
                   failures[0].StackTrace.Contains("--- End of stack trace from previous location ---") &&
                   failures[0].StackTrace.Contains("ParseFile_WithOptions") &&
                   !failures[0].StackTrace.Contains("Debug trace line"),
                   $"Expected complete VSTest async stack frames without stdout pollution, got:\n{failures[0].StackTrace}");

            Assert(failures[1].TestClass == "CollationModifier.Tests.GenericSuite", $"Expected CollationModifier.Tests.GenericSuite, got '{failures[1].TestClass}'");
            Assert(failures[1].TestMethod.StartsWith("ValidateValue<System.String, System.Int32>("), $"Expected generic parameterized method, got '{failures[1].TestMethod}'");
            Assert(failures[1].StackTrace.Contains("InnerCheck") && failures[1].StackTrace.Contains("ValidateValue"), "Expected all frames in second VSTest failure.");
        }

        static void Test_TestExecutionLogParser_ParseCustomSuites_ExactFailureCountsAndStackTraces()
        {
            var parser = new TestExecutionLogParser();
            string log = @"
=== Running CollationModifier Unit Tests ===

=== Running GlobalExceptionHandler & Centralized WPF Exception Pipeline Tests ===
  [FAIL] GlobalExceptionHandlerTests.Test_DispatcherUnhandledException_LogsToLoggerService_AndContinuesWhenUserChoosesYes - System.InvalidOperationException: Assertion failed: Prompt should display the error message
   at CollationModifier.Tests.GlobalExceptionHandlerTests.Assert(Boolean condition, String message) in /app/CollationModifier.Tests/GlobalExceptionHandlerTests.cs:line 350
   at CollationModifier.Tests.GlobalExceptionHandlerTests.Test_DispatcherUnhandledException_LogsToLoggerService_AndContinuesWhenUserChoosesYes() in /app/CollationModifier.Tests/GlobalExceptionHandlerTests.cs:line 105
PASS: Test_DispatcherUnhandledException_LogsToLoggerService_AndInitiatesShutdownWhenUserChoosesNo
PASS: Test_FatalExceptions_AreNotSuppressed_AndLoggedAsFatal
PASS: Test_SqlCredentialsAndPasswords_AreSanitizedInDialog
PASS: Test_AppDomainUnhandledException_LogsStackTraceAndFlushesWithin5Seconds
PASS: Test_UnobservedTaskException_LogsAndMarksObserved
PASS: Test_AppXamlCs_ConfigurationAndRegistrationVerification

=== Running MainViewModel & Pre-flight Validation Tests ===
  [FAIL] MainViewModelTests.ConnectAsync_UpdatesIsConnectedState_OnSuccessAndFailure - System.InvalidOperationException: Assertion failed: Expected IsConnected true
   at CollationModifier.Tests.MainViewModelTestsRunner.ConnectAsync_Test() in /app/CollationModifier.Tests/MainViewModelTests.cs:line 665
  [PASS] Disconnect_ResetsConnectionState_ClearsDatabases_And_EnablesConnectionParameters

Test Run Finished: MainViewModel tests completed.

TEST SUITE FAILED with 2 failures.
";

            var failures = parser.ParseText(log);
            Assert(failures.Count == 2, $"Expected exact failure count 2 without duplicate wrapper failures, got {failures.Count}");
            Assert(failures[0].TestClass == "GlobalExceptionHandlerTests", $"Expected GlobalExceptionHandlerTests, got '{failures[0].TestClass}'");
            Assert(failures[0].TestMethod == "Test_DispatcherUnhandledException_LogsToLoggerService_AndContinuesWhenUserChoosesYes", $"Unexpected method '{failures[0].TestMethod}'");
            Assert(!string.IsNullOrWhiteSpace(failures[0].StackTrace), "Expected GlobalExceptionHandlerTests stack trace to be populated.");

            Assert(failures[1].TestClass == "MainViewModelTests", $"Expected MainViewModelTests, got '{failures[1].TestClass}'");
            Assert(failures[1].TestMethod == "ConnectAsync_UpdatesIsConnectedState_OnSuccessAndFailure", $"Unexpected method '{failures[1].TestMethod}'");
            Assert(!string.IsNullOrWhiteSpace(failures[1].StackTrace), "Expected MainViewModelTests stack trace to be populated.");

            // Verify MSBuildDiagnosticPatcher.Tests logs containing embedded PrintConsoleSummary banners report exact FailureCount
            string patcherSuiteLog = @"
=================================================
 MSBuild Log Diagnostic & Patch Generator Report
=================================================
 Source Log:       /tmp/test_failure.log
[Test Failures]
  [FAIL] GreeterTests.TestGreeting: Expected 'Hello' but was null
=================================================
  [PASS] Program_Main_ReturnsExitCode1_WhenTestFailuresExist
  [FAIL] Program.Workflow_PostMergeBuildVerification - System.Exception: Assertion Failed: Missing workflow step
   at MSBuildDiagnosticPatcher.Tests.Program.Assert(Boolean condition, String message) in /app/MSBuildDiagnosticPatcher.Tests/Program.cs:line 130
";
            var patcherSuiteFailures = parser.ParseText(patcherSuiteLog);
            Assert(patcherSuiteFailures.Count == 1, $"Expected exact failure count 1 for MSBuildDiagnosticPatcher.Tests log, got {patcherSuiteFailures.Count}");
            Assert(patcherSuiteFailures[0].TestClass == "Program", $"Expected Program, got '{patcherSuiteFailures[0].TestClass}'");
            Assert(patcherSuiteFailures[0].TestMethod == "Workflow_PostMergeBuildVerification", $"Unexpected method '{patcherSuiteFailures[0].TestMethod}'");
            Assert(patcherSuiteFailures[0].StackTrace.Contains("at MSBuildDiagnosticPatcher.Tests.Program.Assert"), "Expected stack trace for MSBuildDiagnosticPatcher.Tests failure.");
        }

        static void Test_TestExecutionLogParser_ParsePerformance_Under10Seconds()
        {
            var parser = new TestExecutionLogParser();
            var lines = new List<string>(20500);
            for (int i = 0; i < 20000; i++)
            {
                if (i % 500 == 0)
                {
                    lines.Add($"  Failed Suite.Tests.ParamTest(index: {i}, label: \"item.value\") [< 1 ms]");
                    lines.Add("  Error Message:");
                    lines.Add($"   Expected: {i}");
                    lines.Add($"   Actual:   {i + 1}");
                    lines.Add("  Stack Trace:");
                    lines.Add($"     at Suite.Tests.ParamTest(Int32 index, String label) in /src/Tests.cs:line {i % 300 + 1}");
                }
                else
                {
                    lines.Add($"  [PASS] HealthyTest_{i}");
                }
            }

            string fullLog = string.Join("\n", lines);
            var sw = Stopwatch.StartNew();
            var failures = parser.ParseText(fullLog);
            sw.Stop();

            Console.WriteLine($"    TestExecutionLogParser parsed {lines.Count} lines in {sw.ElapsedMilliseconds} ms (Extracted {failures.Count} failures).");
            Assert(sw.Elapsed.TotalSeconds < 10.0, $"Expected parse duration under 10 seconds, got {sw.Elapsed.TotalSeconds:F2} seconds.");
            Assert(failures.Count == 40, $"Expected 40 failures, got {failures.Count}.");
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

        static void Test_InterfaceDefinitionResolver_ExtractMember_IgnoresDefaultCallsAndComments()
        {
            var resolver = new InterfaceDefinitionResolver();
            string interfaceSource = @"
namespace CollationModifier.Services
{
    public interface IDatabaseServiceLike
    {
        /// <summary>
        /// Backs up the specified database.
        /// </summary>
        Task<string> BackupDatabaseAsync(string databaseName, string backupDirectory, IProgress<int>? progress = null, CancellationToken cancellationToken = default);
        Task<string> BackupDatabaseAsync(string databaseName, string backupDirectory, CancellationToken cancellationToken) => BackupDatabaseAsync(databaseName, backupDirectory, null, cancellationToken);

        // Expression-bodied default member with no abstract overload must not match header or arrow call
        Task OnlyDefaultAsync(string databaseName) => OnlyDefaultAsync(databaseName, CancellationToken.None);

        // Single-line comment right above declaration must never pollute ReturnType
        string ServerName { get; set; }

        /// <summary>Multi-line generic return type</summary>
        Task<Dictionary<
            string,
            List<int?>>> FetchMappingsAsync(string schemaName);
    }
}";

            var backupMember = resolver.ExtractMemberFromInterfaceSource(
                interfaceSource,
                "IDatabaseServiceLike",
                "BackupDatabaseAsync",
                "string, string, CancellationToken");
            Assert(backupMember != null, "Expected abstract BackupDatabaseAsync overload fallback to be resolved.");
            Assert(backupMember!.ReturnType == "Task<string>", $"Expected clean ReturnType 'Task<string>', got '{backupMember.ReturnType}'");
            Assert(backupMember.ParameterList.Contains("IProgress<int>? progress = null"), "Expected abstract overload parameters rather than default call arguments.");

            var defaultOnly = resolver.ExtractMemberFromInterfaceSource(
                interfaceSource,
                "IDatabaseServiceLike",
                "OnlyDefaultAsync",
                "string");
            Assert(defaultOnly == null, "Expected default interface implementation and its arrow call to be ignored.");

            var prop = resolver.ExtractMemberFromInterfaceSource(
                interfaceSource,
                "IDatabaseServiceLike",
                "ServerName",
                "");
            Assert(prop != null && prop.IsProperty, "Expected ServerName property to be extracted.");
            Assert(prop!.ReturnType == "string", $"Expected clean property ReturnType 'string', got '{prop.ReturnType}'");

            var multiLineGeneric = resolver.ExtractMemberFromInterfaceSource(
                interfaceSource,
                "IDatabaseServiceLike",
                "FetchMappingsAsync",
                "string");
            Assert(multiLineGeneric != null, "Expected multi-line generic method to be extracted.");
            Assert(multiLineGeneric!.ReturnType == "Task<Dictionary<string, List<int?>>>",
                $"Expected normalized multi-line generic ReturnType, got '{multiLineGeneric.ReturnType}'");
        }

        static void Test_InterfaceDefinitionResolver_ExtractMember_OverloadsPrecedenceAndSameNameReturnType()
        {
            var resolver = new InterfaceDefinitionResolver();
            string source = @"
public interface IUnrelatedInterface
{
    double Compute(string ignored);
}

public interface IAdvancedService
{
    Status Status { get; }
    Status Status(string name);
    Status Status(string name, int timeoutSeconds);
    void WriteOnlySecret { set; }
    Config Config { get; set; }
}";

            // Multi-overload selection & method-over-property precedence + identical return-type and member name
            var statusTwoArgs = resolver.ExtractMemberFromInterfaceSource(source, "IAdvancedService", "Status", "string, int");
            Assert(statusTwoArgs != null && !statusTwoArgs.IsProperty, "Expected method to take precedence over property and match 2-arg overload.");
            Assert(statusTwoArgs!.ReturnType == "Status", $"Expected ReturnType 'Status', got '{statusTwoArgs.ReturnType}'");
            Assert(statusTwoArgs.ParameterList == "string name, int timeoutSeconds", $"Unexpected ParameterList '{statusTwoArgs.ParameterList}'");

            var statusOneArg = resolver.ExtractMemberFromInterfaceSource(source, "IAdvancedService", "Status", "string");
            Assert(statusOneArg != null && !statusOneArg.IsProperty, "Expected 1-arg Status method overload.");
            Assert(statusOneArg!.ParameterList == "string name", $"Unexpected ParameterList '{statusOneArg.ParameterList}'");

            // Fallback to first method when expected parameter types do not match any method overload (still preferring method over property)
            var statusFallback = resolver.ExtractMemberFromInterfaceSource(source, "IAdvancedService", "Status", "Guid");
            Assert(statusFallback != null && !statusFallback.IsProperty, "Expected method precedence over property on parameter mismatch.");
            Assert(statusFallback!.ParameterList == "string name", $"Expected first method overload as bestMatch, got '{statusFallback.ParameterList}'");

            // Property with identical return-type and property name
            var configProp = resolver.ExtractMemberFromInterfaceSource(source, "IAdvancedService", "Config", "");
            Assert(configProp != null && configProp.IsProperty, "Expected Config property.");
            Assert(configProp!.ReturnType == "Config", $"Expected ReturnType 'Config', got '{configProp.ReturnType}'");
            Assert(configProp.HasGetter && configProp.HasSetter, "Expected both getter and setter on Config.");

            // Setter-only property
            var writeOnlyProp = resolver.ExtractMemberFromInterfaceSource(source, "IAdvancedService", "WriteOnlySecret", "");
            Assert(writeOnlyProp != null && writeOnlyProp.IsProperty, "Expected WriteOnlySecret property.");
            Assert(!writeOnlyProp!.HasGetter && writeOnlyProp.HasSetter, "Expected setter-only property flags.");

            // Unrelated interface member is excluded by interface body slicing
            var unrelated = resolver.ExtractMemberFromInterfaceSource(source, "IAdvancedService", "Compute", "string");
            Assert(unrelated == null, "Expected member from IUnrelatedInterface not to match inside IAdvancedService.");
        }

        static void Test_InterfaceDefinitionResolver_ClearCache_InvalidatesCachedBodyOffsets()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"body_offset_cache_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                string ifaceFile = Path.Combine(tempDir, "IShiftedService.cs");
                File.WriteAllText(ifaceFile, @"public interface IShiftedService
{
    string Alpha(int id);
    bool Beta();
}");
                string contextFile = Path.Combine(tempDir, "ShiftedService.cs");
                var resolver = new InterfaceDefinitionResolver();

                var alpha1 = resolver.ResolveInterfaceMember("IShiftedService", "Alpha", "int", contextFile, tempDir);
                var beta1 = resolver.ResolveInterfaceMember("IShiftedService", "Beta", "", contextFile, tempDir);
                Assert(alpha1 != null && alpha1.ReturnType == "string", "Expected initial Alpha return type 'string'.");
                Assert(beta1 != null && beta1.ReturnType == "bool", "Expected initial Beta return type 'bool'.");

                // Rewrite file with a large comment/namespace preamble that shifts '{ ... }' offsets significantly
                File.WriteAllText(ifaceFile, @"
// Preamble shifting interface declaration body offsets by well over 150 characters
// =================================================================================
namespace Shifted.Namespace.Declaration
{
    public interface IOtherDecoy
    {
        double Alpha(int id);
    }

    public interface IShiftedService
    {
        long Alpha(int id);
        Guid Beta();
    }
}");

                // Prior to ClearCache, cached body offsets and content still resolve original signatures
                var cachedAlpha = resolver.ResolveInterfaceMember("IShiftedService", "Alpha", "int", contextFile, tempDir);
                Assert(cachedAlpha != null && cachedAlpha.ReturnType == "string", "Expected cached return type prior to ClearCache.");

                // ClearCache must clear cached body offsets as well as file content
                resolver.ClearCache();
                var refreshedAlpha = resolver.ResolveInterfaceMember("IShiftedService", "Alpha", "int", contextFile, tempDir);
                var refreshedBeta = resolver.ResolveInterfaceMember("IShiftedService", "Beta", "", contextFile, tempDir);
                Assert(refreshedAlpha != null && refreshedAlpha.ReturnType == "long",
                    $"Expected refreshed Alpha return type 'long' after ClearCache, got '{refreshedAlpha?.ReturnType}'");
                Assert(refreshedBeta != null && refreshedBeta.ReturnType == "Guid",
                    $"Expected refreshed Beta return type 'Guid' after ClearCache, got '{refreshedBeta?.ReturnType}'");
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

        static void Test_PatchGenerator_ExtractParameterNames_HandlesDefaultsNullablesAndWhitespace()
        {
            var extractMethod = typeof(PatchGenerator).GetMethod("ExtractParameterNames", BindingFlags.NonPublic | BindingFlags.Static);
            var parseMethod = typeof(PatchGenerator).GetMethod("ParseParameters", BindingFlags.NonPublic | BindingFlags.Static);
            Assert(extractMethod != null, "Expected ExtractParameterNames method on PatchGenerator.");
            Assert(parseMethod != null, "Expected ParseParameters method on PatchGenerator.");

            List<string> InvokeExtract(string? paramList) =>
                (List<string>)extractMethod!.Invoke(null, new object?[] { paramList })!;

            List<(string Type, string Name, bool HasDefault, string DefaultValue)> InvokeParse(string? paramList) =>
                (List<(string Type, string Name, bool HasDefault, string DefaultValue)>)parseMethod!.Invoke(null, new object?[] { paramList })!;

            // 1. Empty, whitespace, and null inputs should return empty lists without throwing
            Assert(InvokeExtract("").Count == 0, "Expected empty parameter list for empty string.");
            Assert(InvokeExtract("    ").Count == 0, "Expected empty parameter list for spaces.");
            Assert(InvokeExtract("\t \r\n ").Count == 0, "Expected empty parameter list for tabs and newlines.");
            Assert(InvokeExtract(null).Count == 0, "Expected empty parameter list for null input.");
            Assert(InvokeExtract(" ,  , ").Count == 0, "Expected empty parameter list for empty comma-separated segments.");

            // 2. Default values, nullable types, and irregular spaces/tabs/newlines
            string complexSignature =
                "  string   databaseName , " +
                "string?   backupDirectory =  null , " +
                "IProgress<int>?  progress = null , " +
                "int?   maxRetries = 3 , " +
                "string ?   label = \"full backup\" , " +
                "CancellationToken\tcancellationToken  =  default(CancellationToken)  ";

            var parsed = InvokeParse(complexSignature);
            var extractedNames = InvokeExtract(complexSignature);

            var expectedNames = new[] { "databaseName", "backupDirectory", "progress", "maxRetries", "label", "cancellationToken" };
            Assert(extractedNames.SequenceEqual(expectedNames),
                $"Expected parameter names [{string.Join(", ", expectedNames)}], got [{string.Join(", ", extractedNames)}]");
            Assert(extractedNames.SequenceEqual(parsed.Select(p => p.Name)),
                "ExtractParameterNames output must match projected names from ParseParameters.");

            // Verify ParseParameters preserves nullable type annotations and default value expressions
            Assert(parsed[0] == ("string", "databaseName", false, ""), $"Unexpected parsed[0]: {parsed[0]}");
            Assert(parsed[1] == ("string?", "backupDirectory", true, "null"), $"Unexpected parsed[1]: {parsed[1]}");
            Assert(parsed[2] == ("IProgress<int>?", "progress", true, "null"), $"Unexpected parsed[2]: {parsed[2]}");
            Assert(parsed[3] == ("int?", "maxRetries", true, "3"), $"Unexpected parsed[3]: {parsed[3]}");
            Assert(parsed[4] == ("string ?", "label", true, "\"full backup\""), $"Unexpected parsed[4]: {parsed[4]}");
            Assert(parsed[5] == ("CancellationToken", "cancellationToken", true, "default(CancellationToken)"), $"Unexpected parsed[5]: {parsed[5]}");
        }

        static void Test_PatchGenerator_FindDelegationCall_MatchesCandidateOverloadsConsistently()
        {
            var generator = new PatchGenerator();

            // Candidate overload has irregular spacing, nullable types, and default expressions;
            // missing member also has extra whitespace, nullable type, and default parameter.
            string classSource = @"
public class BackupCoordinator : IBackupCoordinator
{
    public virtual Task<bool> RunBackupAsync(string databaseName)
    {
        return Task.FromResult(false);
    }

    public virtual async Task<bool> RunBackupAsync(
        string   dbDatabase ,
        string?   targetDirectory ,
        IProgress<int>?   progress = null ,
        string   archiveLabel = ""nightly backup"" ,
        int?   compressionLevel = 1 ,
        CancellationToken   ctToken  =  default )
    {
        return true;
    }
}";

            var member = new InterfaceMemberInfo
            {
                InterfaceName = "IBackupCoordinator",
                MemberName = "RunBackupAsync",
                ReturnType = "Task<bool>",
                ParameterList = "  string   sourceDatabase ,  string?   backupDirectory ,  CancellationToken   cancellationToken = default  "
            };

            string stub = generator.GenerateMemberStub(member, classSource);
            Assert(
                stub.Contains("return RunBackupAsync(sourceDatabase, backupDirectory, null, \"nightly backup\", 1, cancellationToken);"),
                $"Expected delegation call matching candidate overload parameters, got:\n{stub}");

            // Also verify empty parameter list on missing member delegates cleanly when all candidate parameters have defaults or are nullable
            var parameterlessMember = new InterfaceMemberInfo
            {
                InterfaceName = "IBackupCoordinator",
                MemberName = "PingAsync",
                ReturnType = "Task",
                ParameterList = "   "
            };

            string pingClassSource = @"
public class BackupCoordinator : IBackupCoordinator
{
    public virtual Task PingAsync(string? endpoint = null, int timeoutMs = 5000, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}";

            string pingStub = generator.GenerateMemberStub(parameterlessMember, pingClassSource);
            Assert(
                pingStub.Contains("return PingAsync(null, 5000, default);"),
                $"Expected parameterless stub to delegate using defaults and nulls, got:\n{pingStub}");
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

        static void Test_PatchGenerator_GenerateMemberStub_LinearScalingOnLargeWordBlocks()
        {
            var generator = new PatchGenerator();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("namespace ScalingTest {");
            sb.AppendLine("public class LargeService {");
            sb.Append("    /* ");
            for (int i = 0; i < 800; i++)
            {
                sb.Append("wordToken").Append(i).Append(' ');
                if ((i + 1) % 20 == 0) sb.AppendLine();
            }
            sb.AppendLine("*/");
            sb.AppendLine("    public virtual Task<string> RunOperationAsync(string opName, int timeoutMs = 500, CancellationToken cancellationToken = default) => Task.FromResult(opName);");
            sb.AppendLine("} }");
            string largeClassSource = sb.ToString();

            var missingMember = new InterfaceMemberInfo
            {
                InterfaceName = "ILargeService",
                MemberName = "MissingBenchmarkMethodAsync",
                ReturnType = "Task<string>",
                ParameterList = "string opName, CancellationToken cancellationToken = default"
            };

            // Warmup JIT
            generator.GenerateMemberStub(missingMember, largeClassSource);

            var sw = Stopwatch.StartNew();
            for (int iter = 0; iter < 5; iter++)
            {
                string stub = generator.GenerateMemberStub(missingMember, largeClassSource);
                Assert(stub.Contains("throw new NotImplementedException();"), "Expected fallback stub when no overload exists.");
            }
            sw.Stop();

            // Uncached backtracking regex took ~560-720 ms per call (>2800 ms for 5 calls); ordinal scan completes in < 1 ms
            Assert(sw.Elapsed.TotalMilliseconds < 100.0,
                $"Expected 5 GenerateMemberStub calls on 800-word block to complete in < 100 ms, took {sw.Elapsed.TotalMilliseconds:F2} ms");
        }

        static void Test_PatchGenerator_FindDelegationCall_HandlesSameNameReturnTypeAndOverloadCache()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"delegation_edge_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                string ifaceFile = Path.Combine(tempDir, "IStatusService.cs");
                File.WriteAllText(ifaceFile, @"
public interface IStatusService
{
    Status Status(string id);
    Status Status(string id, int retryCount);
}
");
                string classFile = Path.Combine(tempDir, "StatusService.cs");
                File.WriteAllText(classFile, @"
public class StatusService : IStatusService
{
    // public virtual Status Status(string id, string nonDefaultRequired, int retryCount = 99)
    public Status Helper(string id) => Status(id, 5, default);

    public virtual Status Status(string id, int retryCount = 3, CancellationToken cancellationToken = default)
    {
        return new Status();
    }
}
");
                var diagnostics = new List<CompilerDiagnostic>
                {
                    new CompilerDiagnostic
                    {
                        Code = "CS0535",
                        Severity = DiagnosticSeverity.Error,
                        FilePath = classFile,
                        LineNumber = 2,
                        ColumnNumber = 14,
                        Message = "'StatusService' does not implement interface member 'IStatusService.Status(string)'"
                    },
                    new CompilerDiagnostic
                    {
                        Code = "CS0535",
                        Severity = DiagnosticSeverity.Error,
                        FilePath = classFile,
                        LineNumber = 2,
                        ColumnNumber = 14,
                        Message = "'StatusService' does not implement interface member 'IStatusService.Status(string, int)'"
                    }
                };

                var generator = new PatchGenerator(new InterfaceDefinitionResolver());
                var patches = generator.GeneratePatches(diagnostics, tempDir);
                Assert(patches.Count == 1, $"Expected 1 patch candidate, got {patches.Count}");
                Assert(patches[0].ResolvedErrorCount == 2, $"Expected 2 resolved errors, got {patches[0].ResolvedErrorCount}");

                string generated = patches[0].GeneratedCode;
                Assert(generated.Contains("public virtual Status Status(string id)"), "Expected 1-arg Status stub.");
                Assert(generated.Contains("return Status(id, 3, default);"), $"Expected 1-arg Status delegation, got:\n{generated}");
                Assert(generated.Contains("public virtual Status Status(string id, int retryCount)"), "Expected 2-arg Status stub.");
                Assert(generated.Contains("return Status(id, retryCount, default);"), $"Expected 2-arg Status delegation, got:\n{generated}");
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

        static void Test_ReportGenerator_Markdown_MultilineTableCellsAndDetails()
        {
            string multilineMsg = "Assertion Failed: Values differ | check\r\nExpected: \"Alpha\"\nActual:   \"Beta\"";
            string fullStack = "at MyNamespace.MyTests.Helper()\n--- End of stack trace from previous location ---\nat MyNamespace.MyTests.RunAsync()";

            var report = new DiagnosticReport
            {
                SourceLogPath = "/tmp/build.log",
                ParseDuration = TimeSpan.FromMilliseconds(12.3),
                TestFailures = new List<TestFailure>
                {
                    new TestFailure
                    {
                        TestClass = "MyNamespace.MyTests",
                        TestMethod = "RunAsync(mode: \"a|b\")",
                        AssertionFailureMessage = multilineMsg,
                        StackTrace = fullStack
                    }
                }
            };

            var reporter = new ReportGenerator();
            string md = reporter.GenerateMarkdownReport(report);

            // Extract the table row for MyNamespace.MyTests
            var lines = md.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            string? tableRow = lines.FirstOrDefault(l => l.StartsWith("| `MyNamespace.MyTests` |", StringComparison.Ordinal));
            Assert(tableRow != null, "Expected Markdown table row for MyNamespace.MyTests.");
            Assert(tableRow!.Contains("Assertion Failed: Values differ \\| check Expected: \"Alpha\" Actual:   \"Beta\""),
                $"Expected table cell to replace line breaks with spaces and escape pipes, got: {tableRow}");

            // Ensure ### Failure Details preserves full multiline message and stack trace
            int detailsIdx = md.IndexOf("### Failure Details", StringComparison.Ordinal);
            Assert(detailsIdx > 0, "Expected ### Failure Details section.");
            string detailsSection = md.Substring(detailsIdx);
            Assert(detailsSection.Contains(multilineMsg), "Expected ### Failure Details to retain full multiline AssertionFailureMessage.");
            Assert(detailsSection.Contains(fullStack), "Expected ### Failure Details to retain full multiline StackTrace.");
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
            Assert(!json.Contains("\"SeverityCounts\""), "Expected GetSeverityCounts method not to add JSON properties");
        }

        static void Test_ReportGenerator_PrintConsoleSummary()
        {
            var reporter = new ReportGenerator();
            TextWriter originalOut = Console.Out;
            try
            {
                var nonErrorReport = new DiagnosticReport
                {
                    SourceLogPath = "/tmp/warnings.log",
                    ParseDuration = TimeSpan.FromMilliseconds(8.0),
                    Diagnostics = new List<CompilerDiagnostic>
                    {
                        new CompilerDiagnostic { Code = "CS0168", Severity = DiagnosticSeverity.Warning, FilePath = "A.cs", LineNumber = 10, ColumnNumber = 4, Message = "Unused var" },
                        new CompilerDiagnostic { Code = "MSB1001", Severity = DiagnosticSeverity.Info, FilePath = "B.cs", LineNumber = 1, ColumnNumber = 1, Message = "Info msg" }
                    }
                };

                using (var swNoErrors = new StringWriter())
                {
                    Console.SetOut(swNoErrors);
                    reporter.PrintConsoleSummary(nonErrorReport);
                    string output = swNoErrors.ToString();
                    Assert(output.Contains("Compiler Errors:  0"), "Expected 0 compiler errors");
                    Assert(output.Contains("Compiler Warnings:1"), "Expected 1 compiler warning (Info excluded)");
                    Assert(!output.Contains("[Compiler Errors]"), "Expected [Compiler Errors] section to be omitted when no errors exist");
                }

                string mdNoErrors = reporter.GenerateMarkdownReport(nonErrorReport);
                Assert(mdNoErrors.Contains("- **Roslyn Errors**: `0`"), "Expected Markdown to report 0 Roslyn Errors");
                Assert(mdNoErrors.Contains("- **Roslyn Warnings**: `1`"), "Expected Markdown to report 1 Roslyn Warning (Info excluded)");

                nonErrorReport.Diagnostics.Add(new CompilerDiagnostic
                {
                    Code = "CS0535",
                    Severity = DiagnosticSeverity.Error,
                    FilePath = "C.cs",
                    LineNumber = 22,
                    ColumnNumber = 8,
                    Message = "Missing interface member"
                });

                using (var swWithErrors = new StringWriter())
                {
                    Console.SetOut(swWithErrors);
                    reporter.PrintConsoleSummary(nonErrorReport);
                    string output = swWithErrors.ToString();
                    Assert(output.Contains("Compiler Errors:  1"), "Expected 1 compiler error");
                    Assert(output.Contains("Compiler Warnings:1"), "Expected 1 compiler warning");
                    Assert(output.Contains("[Compiler Errors]"), "Expected [Compiler Errors] section when errors exist");
                    Assert(output.Contains("[CS0535] C.cs(22,8): Missing interface member"), "Expected error line in console output");
                    Assert(!output.Contains("[CS0168]"), "Expected warning not to be printed under [Compiler Errors]");
                    Assert(!output.Contains("[MSB1001]"), "Expected info diagnostic not to be printed under [Compiler Errors]");
                }
            }
            finally
            {
                Console.SetOut(originalOut);
            }
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

        static void Test_DiagnosticReport_SeverityCounts_TrackInfoWarningErrorMutations()
        {
            var report = new DiagnosticReport
            {
                Diagnostics = new List<CompilerDiagnostic>
                {
                    new CompilerDiagnostic { Severity = DiagnosticSeverity.Info, Code = "MSB1001" },
                    new CompilerDiagnostic { Severity = DiagnosticSeverity.Warning, Code = "CS0168" },
                    new CompilerDiagnostic { Severity = DiagnosticSeverity.Error, Code = "CS0535" }
                }
            };

            Assert(report.ErrorCount == 1, $"Expected initial ErrorCount 1, got {report.ErrorCount}");
            Assert(report.WarningCount == 1, $"Expected initial WarningCount 1, got {report.WarningCount}");

            report.Diagnostics.Add(new CompilerDiagnostic { Severity = DiagnosticSeverity.Info, Code = "MSB1002" });
            Assert(report.ErrorCount == 1, $"Expected ErrorCount 1 after adding Info, got {report.ErrorCount}");
            Assert(report.WarningCount == 1, $"Expected WarningCount 1 after adding Info, got {report.WarningCount}");

            report.Diagnostics.Add(new CompilerDiagnostic { Severity = DiagnosticSeverity.Warning, Code = "CS0219" });
            report.Diagnostics.Add(new CompilerDiagnostic { Severity = DiagnosticSeverity.Error, Code = "CS0103" });
            Assert(report.ErrorCount == 2, $"Expected ErrorCount 2 after adding Error, got {report.ErrorCount}");
            Assert(report.WarningCount == 2, $"Expected WarningCount 2 after adding Warning, got {report.WarningCount}");

            report.Diagnostics[2].Severity = DiagnosticSeverity.Warning;
            Assert(report.ErrorCount == 1, $"Expected ErrorCount 1 after mutating Error to Warning, got {report.ErrorCount}");
            Assert(report.WarningCount == 3, $"Expected WarningCount 3 after mutating Error to Warning, got {report.WarningCount}");

            report.Diagnostics.RemoveAt(1);
            Assert(report.WarningCount == 2, $"Expected WarningCount 2 after RemoveAt, got {report.WarningCount}");

            report.Diagnostics.Clear();
            Assert(report.ErrorCount == 0, $"Expected ErrorCount 0 after Clear, got {report.ErrorCount}");
            Assert(report.WarningCount == 0, $"Expected WarningCount 0 after Clear, got {report.WarningCount}");
        }

        static void Test_DiagnosticReport_ErrorAndWarningCounts_ZeroAllocations()
        {
            var diagnostics = new List<CompilerDiagnostic>(128);
            for (int i = 0; i < 50; i++)
            {
                diagnostics.Add(new CompilerDiagnostic { Severity = DiagnosticSeverity.Error, Code = "CS0535" });
            }
            for (int i = 0; i < 40; i++)
            {
                diagnostics.Add(new CompilerDiagnostic { Severity = DiagnosticSeverity.Warning, Code = "CS0168" });
            }
            for (int i = 0; i < 30; i++)
            {
                diagnostics.Add(new CompilerDiagnostic { Severity = DiagnosticSeverity.Info, Code = "MSB1001" });
            }

            var report = new DiagnosticReport { Diagnostics = diagnostics };

            // Warm up JIT tiers before measuring current-thread heap allocations
            int warmupTotal = 0;
            for (int i = 0; i < 100; i++)
            {
                warmupTotal += report.ErrorCount + report.WarningCount;
            }

            long beforeError = GC.GetAllocatedBytesForCurrentThread();
            int errorSum = 0;
            for (int i = 0; i < 50; i++)
            {
                errorSum += report.ErrorCount;
            }
            long errorAllocated = GC.GetAllocatedBytesForCurrentThread() - beforeError;

            long beforeWarning = GC.GetAllocatedBytesForCurrentThread();
            int warningSum = 0;
            for (int i = 0; i < 50; i++)
            {
                warningSum += report.WarningCount;
            }
            long warningAllocated = GC.GetAllocatedBytesForCurrentThread() - beforeWarning;

            GC.KeepAlive(warmupTotal);
            Assert(errorSum == 50 * 50, $"Expected errorSum 2500, got {errorSum}");
            Assert(warningSum == 50 * 40, $"Expected warningSum 2000, got {warningSum}");
            Assert(errorAllocated == 0, $"Expected 0 bytes allocated reading ErrorCount, got {errorAllocated}");
            Assert(warningAllocated == 0, $"Expected 0 bytes allocated reading WarningCount, got {warningAllocated}");
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

        static void Test_Program_Main_ReturnsExitCode1_ForUnhandledDotnetCrashAndPersistsJsonDetails()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"exit_crash_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            var originalOut = Console.Out;
            try
            {
                string logFile = Path.Combine(tempDir, "crash_build.log");
                string outputDir = Path.Combine(tempDir, "out");
                File.WriteAllText(logFile, @"
Build succeeded.
    0 Warning(s)
    0 Error(s)
=== Running CollationModifier Unit Tests ===
Unhandled exception. System.InvalidOperationException: Async void crashed host
Expected: Handled on UI SynchronizationContext
Actual:   Process aborted
 ---> System.ArgumentException: Invalid state parameter
   at CollationModifier.Services.Helper.Validate() in /app/Helper.cs:line 19
   --- End of inner exception stack trace ---
   at CollationModifier.ViewModels.MainViewModel.<OnClick>d__10.MoveNext() in /app/MainViewModel.cs:line 88
--- End of stack trace from previous location ---
   at System.Threading.Tasks.Task.<>c.<ThrowAsync>b__128_0(Object state)
");

                using var suppressedConsole = new StringWriter();
                Console.SetOut(suppressedConsole);
                int exitCode = MSBuildDiagnosticPatcher.Program.Main(new[] { logFile, "-o", outputDir });
                Console.SetOut(originalOut);

                Assert(exitCode == 1, $"Expected exit code 1 for unhandled .NET crash log, got {exitCode}");

                string jsonPath = Path.Combine(outputDir, "diagnostic-report.json");
                string mdPath = Path.Combine(outputDir, "diagnostic-report.md");
                Assert(File.Exists(jsonPath), "Expected diagnostic-report.json to be written.");
                Assert(File.Exists(mdPath), "Expected diagnostic-report.md to be written.");

                string json = File.ReadAllText(jsonPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                Assert(root.GetProperty("FailureCount").GetInt32() == 1, $"Expected FailureCount == 1, got {root.GetProperty("FailureCount").GetInt32()}");

                var failureElem = root.GetProperty("TestFailures")[0];
                string testClass = failureElem.GetProperty("TestClass").GetString() ?? "";
                string testMethod = failureElem.GetProperty("TestMethod").GetString() ?? "";
                string assertionMsg = failureElem.GetProperty("AssertionFailureMessage").GetString() ?? "";
                string stackTrace = failureElem.GetProperty("StackTrace").GetString() ?? "";

                Assert(testClass == "CollationModifier.Services.Helper", $"Expected crash origin class CollationModifier.Services.Helper, got '{testClass}'");
                Assert(testMethod == "Validate", $"Expected crash origin method Validate, got '{testMethod}'");
                Assert(assertionMsg.Contains("Async void crashed host") &&
                       assertionMsg.Contains("Expected: Handled on UI SynchronizationContext") &&
                       assertionMsg.Contains("Actual:   Process aborted") &&
                       assertionMsg.Contains("---> System.ArgumentException: Invalid state parameter"),
                       $"Expected full multiline message and InnerException in JSON, got:\n{assertionMsg}");
                Assert(stackTrace.Contains("Helper.Validate") &&
                       stackTrace.Contains("--- End of inner exception stack trace ---") &&
                       stackTrace.Contains("MainViewModel.<OnClick>d__10.MoveNext") &&
                       stackTrace.Contains("--- End of stack trace from previous location ---") &&
                       stackTrace.Contains("ThrowAsync"),
                       $"Expected all frames retained in JSON StackTrace, got:\n{stackTrace}");

                string md = File.ReadAllText(mdPath);
                Assert(!md.Contains("No test runner failures detected in the log."), "Markdown report must not claim zero failures on crash.");
                Assert(md.Contains("### Failure Details"), "Markdown report must include ### Failure Details.");
            }
            finally
            {
                Console.SetOut(originalOut);
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        // 9. CI Workflow Configuration Tests
        static void Test_Workflow_PostMergeBuildVerification_HasDiagnosticReportingAndArtifactUpload()
        {
            string workflowPath = FindWorkflowPath();
            string content = File.ReadAllText(workflowPath);

            // Requirement 1: Redirect build and test output to /tmp/build-and-test.log
            Assert(content.Contains("dotnet build MSBuildDiagnosticPatcher/MSBuildDiagnosticPatcher.csproj -p:UseSharedCompilation=false 2>&1 | tee -a /tmp/build-and-test.log"), "Workflow must redirect patcher build output to /tmp/build-and-test.log");
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

            string cmdLauncherPath = FindLauncherCmdPath();
            string cmdLauncherContent = File.ReadAllText(cmdLauncherPath);
            Assert(cmdLauncherContent.Contains("-p:UseSharedCompilation=false"), "Launcher script bin/msbuild-patcher.cmd must pass -p:UseSharedCompilation=false to dotnet run");

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

        private static string FindLauncherScriptPath(string scriptName = "msbuild-patcher")
        {
            string? dir = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(dir))
            {
                string candidate = Path.Combine(dir, "bin", scriptName);
                if (File.Exists(candidate)) return candidate;
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            dir = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                string candidate = Path.Combine(dir, "bin", scriptName);
                if (File.Exists(candidate)) return candidate;
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            throw new FileNotFoundException($"bin/{scriptName} could not be found.");
        }

        private static string FindLauncherCmdPath() => FindLauncherScriptPath("msbuild-patcher.cmd");

        // 10. Single-Pass Streaming & Regex Guard Regression Tests
        static void Test_AnsiCleaner_Clean_FastPathSkipsRegexWithoutEscapeChar()
        {
            string trimmedPlain = "Services/DatabaseService.cs(11,36): error CS0535: Missing member";
            string cleaned = AnsiCleaner.Clean(trimmedPlain);
            Assert(object.ReferenceEquals(trimmedPlain, cleaned), "Expected already-trimmed plain string without '\\x1B' to return same string instance.");

            string untrimmedPlain = "   Build succeeded with 0 warnings.   ";
            Assert(AnsiCleaner.Clean(untrimmedPlain) == "Build succeeded with 0 warnings.", "Expected whitespace trimming on plain string without '\\x1B'.");

            string escaped = "  \u001b[32mBuild succeeded.\u001b[0m  ";
            Assert(AnsiCleaner.Clean(escaped) == "Build succeeded.", "Expected ANSI stripping and trimming when '\\x1B' is present.");
        }

        static void Test_MSBuildLogParser_Guards_CaseInsensitivePrefixesAndAnySeverityWord()
        {
            var parser = new MSBuildLogParser();

            var cases = new (string Line, string ExpectedCode, DiagnosticSeverity ExpectedSeverity)[]
            {
                ("csc : error cs0535a: Lowercase CS prefix with trailing letter", "CS0535A", DiagnosticSeverity.Error),
                ("vbc : warning bc20010: Lowercase BC 5-digit code", "BC20010", DiagnosticSeverity.Warning),
                ("fsc : info fs0074: Lowercase FS info code", "FS0074", DiagnosticSeverity.Info),
                ("MSBUILD : custom fatal notice msb1003: Unrecognized multi-word severity defaults to Error", "MSB1003", DiagnosticSeverity.Error),
                ("/app/App.csproj : CriticalAlert Netsdk1045: Mixed-case NETSDK code with custom severity", "NETSDK1045", DiagnosticSeverity.Error),
                ("/app/App.csproj : warning Nu1605: Mixed-case NU code", "NU1605", DiagnosticSeverity.Warning)
            };

            foreach (var testCase in cases)
            {
                var diag = parser.ParseLine(testCase.Line);
                Assert(diag != null, $"Expected diagnostic to parse for: {testCase.Line}");
                Assert(diag!.Code == testCase.ExpectedCode, $"Expected code {testCase.ExpectedCode}, got {diag.Code}");
                Assert(diag.Severity == testCase.ExpectedSeverity, $"Expected severity {testCase.ExpectedSeverity}, got {diag.Severity}");
            }

            // Non-supported prefix without coordinates should be rejected quickly by guard
            Assert(parser.ParseLine("tool : error XY1234: Unsupported coordinate-free prefix") == null, "Expected unsupported prefix XY1234 without coordinates to return null.");
        }

        static void Test_MSBuildLogParser_Guards_FastOnSevenColonProseLines()
        {
            var parser = new MSBuildLogParser();
            var lines = new List<string>(10_000);
            for (int i = 0; i < 10_000; i++)
            {
                lines.Add($"Build agent pipeline stage: Restore project packages: Compile application sources: Execute unit test suite: Verify build artifacts: Upload diagnostic summary: Publish workflow status: Completed {i}");
            }

            string proseLog = string.Join("\n", lines);
            var sw = Stopwatch.StartNew();
            var diags = parser.ParseText(proseLog);
            sw.Stop();

            Assert(diags.Count == 0, $"Expected 0 diagnostics on seven-colon prose lines, got {diags.Count}");
            Assert(sw.Elapsed.TotalMilliseconds < 135.48, $"Expected 10,000 seven-colon prose lines under 135.48 ms, took {sw.Elapsed.TotalMilliseconds:F2} ms");
        }

        static void Test_TestExecutionLogParser_Streaming_CaseInsensitivePrefixesAndConsecutiveFailures()
        {
            var parser = new TestExecutionLogParser();
            string log = string.Join("\r\n", new[]
            {
                "  fail: CustomSuite.FirstTest - Assertion Failed: Expected 42 but got 0",
                "   at CustomSuite.FirstTest() in /src/Tests.cs:line 12",
                "   --- End of inner exception stack trace ---",
                "   at CustomSuite.Runner() in /src/Tests.cs:line 30",
                "  failed DotnetSuite.SecondTest [19 ms]",
                "  Error Message:",
                "   Expected true, was false",
                "  Stack Trace:",
                "   at DotnetSuite.SecondTest() in /src/DotnetTests.cs:line 55",
                "  failed DotnetSuite.ThirdTest [5 ms]",
                "  Error Message:",
                "   NullReferenceException",
                "  [fail] BracketSuite.FourthTest: Connection timed out",
                "  Passed DotnetSuite.PassingTest [2 ms]"
            });

            var failures = parser.ParseText(log);
            Assert(failures.Count == 4, $"Expected 4 failures, got {failures.Count}");

            Assert(failures[0].TestClass == "CustomSuite" && failures[0].TestMethod == "FirstTest", "Mismatch on failure 0 name");
            Assert(failures[0].StackTrace.Contains("End of inner exception stack trace") && failures[0].StackTrace.Contains("CustomSuite.Runner"), "Mismatch on failure 0 stack trace");

            Assert(failures[1].TestClass == "DotnetSuite" && failures[1].TestMethod == "SecondTest", "Mismatch on failure 1 name");
            Assert(failures[1].AssertionFailureMessage == "Expected true, was false", $"Mismatch on failure 1 message: {failures[1].AssertionFailureMessage}");
            Assert(failures[1].StackTrace.Contains("DotnetSuite.SecondTest"), "Mismatch on failure 1 stack trace");

            Assert(failures[2].TestClass == "DotnetSuite" && failures[2].TestMethod == "ThirdTest", "Mismatch on failure 2 name");
            Assert(failures[2].AssertionFailureMessage == "NullReferenceException", $"Mismatch on failure 2 message: {failures[2].AssertionFailureMessage}");
            Assert(failures[2].StackTrace == string.Empty, "Expected empty stack trace on failure 2");

            Assert(failures[3].TestClass == "BracketSuite" && failures[3].TestMethod == "FourthTest", "Mismatch on failure 3 name");
            Assert(failures[3].AssertionFailureMessage == "Connection timed out", $"Mismatch on failure 3 message: {failures[3].AssertionFailureMessage}");
        }

        static void Test_LogStreamParser_SinglePassStreaming_MatchesDualPassParseTextOnMixedLogs()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"stream_equiv_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                string mixedLog = string.Join("\r\n", new[]
                {
                    "  Determining projects to restore...",
                    "\u001b[31;1mServices/DatabaseService.cs(11,36): error CS0535: 'DatabaseService' does not implement interface member 'IDatabaseService.BackupDatabaseAsync()' [CollationModifier.csproj]\u001b[0m",
                    "  2026-11-24 08:30:15: Stage 1: Step 2: Action 3: Target 4: Task 5: Detail 6: Done",
                    "MSBUILD : unexpected MSB1003: Unrecognized severity fallback check",
                    "ViewModels/MainViewModel.cs(120,15): warning CS0168: The variable 'ex' is declared but never used [CollationModifier.csproj]",
                    "Failed MainViewModelTests.FirstFailure [45 ms]",
                    "  /app/Interleaved.csproj : error NU1605: Interleaved error inside lookahead block [/app/Interleaved.csproj]",
                    "  Error Message:",
                    "   Assertion Failed: Expected status ready",
                    "  Stack Trace:",
                    "   at CollationModifier.Tests.MainViewModelTests.FirstFailure() in /app/Tests.cs:line 80",
                    "   at CollationModifier.Tests.MainViewModelTests.RunAsync() in /app/Tests.cs:line 120",
                    "Failed DatabaseServiceTests.SecondFailure [60 ms]",
                    "  Error Message:",
                    "   Assertion Failed: Database was not put in SINGLE_USER mode",
                    "  Stack Trace:",
                    "   at CollationModifier.Tests.DatabaseServiceTests.SecondFailure() in /app/DbTests.cs:line 150",
                    "FAIL: CustomRunnerSuite.ThirdFailure - System.Exception: Assertion Failed: Timeout",
                    "   at CustomRunnerSuite.ThirdFailure() in /app/CustomTests.cs:line 44",
                    "  [FAIL] BracketRunner.FourthFailure: Unexpected state",
                    "CollationModifier.csproj : info netSDK1045: Informational SDK message"
                });

                string logPath = Path.Combine(tempDir, "mixed.log");
                File.WriteAllText(logPath, mixedLog);

                var msbDual = new MSBuildLogParser();
                var testDual = new TestExecutionLogParser();
                var expectedDiags = msbDual.ParseText(mixedLog);
                var expectedFailures = testDual.ParseText(mixedLog);

                var msbStream = new MSBuildLogParser();
                var testStream = new TestExecutionLogParser();
                List<CompilerDiagnostic> streamedDiags;
                List<TestFailure> streamedFailures;
                using (var reader = new StreamReader(logPath))
                {
                    (streamedDiags, streamedFailures) = LogStreamParser.Parse(reader, msbStream, testStream);
                }

                Assert(expectedDiags.Count == 5, $"Expected 5 diagnostics, got {expectedDiags.Count}");
                Assert(streamedDiags.Count == expectedDiags.Count, $"Diagnostic count mismatch: {streamedDiags.Count} vs {expectedDiags.Count}");
                for (int i = 0; i < expectedDiags.Count; i++)
                {
                    Assert(streamedDiags[i].Code == expectedDiags[i].Code, $"Diag[{i}].Code mismatch");
                    Assert(streamedDiags[i].Severity == expectedDiags[i].Severity, $"Diag[{i}].Severity mismatch");
                    Assert(streamedDiags[i].FilePath == expectedDiags[i].FilePath, $"Diag[{i}].FilePath mismatch");
                    Assert(streamedDiags[i].LineNumber == expectedDiags[i].LineNumber, $"Diag[{i}].LineNumber mismatch");
                    Assert(streamedDiags[i].ColumnNumber == expectedDiags[i].ColumnNumber, $"Diag[{i}].ColumnNumber mismatch");
                    Assert(streamedDiags[i].Message == expectedDiags[i].Message, $"Diag[{i}].Message mismatch");
                    Assert(streamedDiags[i].ProjectPath == expectedDiags[i].ProjectPath, $"Diag[{i}].ProjectPath mismatch");
                    Assert(streamedDiags[i].RawLine == expectedDiags[i].RawLine, $"Diag[{i}].RawLine mismatch");
                }

                Assert(expectedFailures.Count == 4, $"Expected 4 test failures, got {expectedFailures.Count}");
                Assert(streamedFailures.Count == expectedFailures.Count, $"Failure count mismatch: {streamedFailures.Count} vs {expectedFailures.Count}");
                for (int i = 0; i < expectedFailures.Count; i++)
                {
                    Assert(streamedFailures[i].TestClass == expectedFailures[i].TestClass, $"Failure[{i}].TestClass mismatch");
                    Assert(streamedFailures[i].TestMethod == expectedFailures[i].TestMethod, $"Failure[{i}].TestMethod mismatch");
                    Assert(streamedFailures[i].AssertionFailureMessage == expectedFailures[i].AssertionFailureMessage, $"Failure[{i}].AssertionFailureMessage mismatch");
                    Assert(streamedFailures[i].StackTrace == expectedFailures[i].StackTrace, $"Failure[{i}].StackTrace mismatch");
                    Assert(streamedFailures[i].RawLine == expectedFailures[i].RawLine, $"Failure[{i}].RawLine mismatch");
                }

                string outDir = Path.Combine(tempDir, "out");
                int exitCode = MSBuildDiagnosticPatcher.Program.Main(new[] { logPath, "-o", outDir });
                Assert(exitCode == 1, $"Expected Program.Main exit code 1 on mixed failing log, got {exitCode}");
                Assert(File.Exists(Path.Combine(outDir, "diagnostic-report.json")), "Expected diagnostic-report.json to be written");
                Assert(File.Exists(Path.Combine(outDir, "diagnostic-report.md")), "Expected diagnostic-report.md to be written");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }
    }
}
