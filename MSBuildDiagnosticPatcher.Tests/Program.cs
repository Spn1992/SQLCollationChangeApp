using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
            RunTest("MSBuildLogParser_ParseLine_ExtractsNuGetVulnerabilityAuditErrors", Test_MSBuildLogParser_ParseLine_ExtractsNuGetVulnerabilityAuditErrors);
            RunTest("MSBuildLogParser_ParseLine_ExtractsNetSdkCoordinateFreeError", Test_MSBuildLogParser_ParseLine_ExtractsNetSdkCoordinateFreeError);
            RunTest("MSBuildLogParser_ParseLine_ExtractsCoordinateFreeInformationalSeverity", Test_MSBuildLogParser_ParseLine_ExtractsCoordinateFreeInformationalSeverity);
            RunTest("MSBuildLogParser_ParseLine_CoordinateFreeUnrecognizedAndMessageSeverity_ReturnsNull", Test_MSBuildLogParser_ParseLine_CoordinateFreeUnrecognizedAndMessageSeverity_ReturnsNull);
            RunTest("MSBuildLogParser_ParseLine_SpacedPathWithDriveColonAndCoordinates", Test_MSBuildLogParser_ParseLine_SpacedPathWithDriveColonAndCoordinates);
            RunTest("MSBuildLogParser_ParseLine_SpacedPathWithCoordinatesWithoutColumnNumber", Test_MSBuildLogParser_ParseLine_SpacedPathWithCoordinatesWithoutColumnNumber);
            RunTest("MSBuildLogParser_ParseLine_FatalErrorSeverityWithCoordinates", Test_MSBuildLogParser_ParseLine_FatalErrorSeverityWithCoordinates);
            RunTest("MSBuildLogParser_ParseLine_CoordinateFreeRoslynErrors_CS_BC_FS", Test_MSBuildLogParser_ParseLine_CoordinateFreeRoslynErrors_CS_BC_FS);
            RunTest("MSBuildLogParser_ParseLine_CoordinateFreeRoslynFatalError", Test_MSBuildLogParser_ParseLine_CoordinateFreeRoslynFatalError);
            RunTest("MSBuildLogParser_ParseLine_CoordinateFreeSpacedPathWithDriveColon", Test_MSBuildLogParser_ParseLine_CoordinateFreeSpacedPathWithDriveColon);
            RunTest("MSBuildLogParser_ParseLine_SpecialPathCharacters_X86_At_Plus", Test_MSBuildLogParser_ParseLine_SpecialPathCharacters_X86_At_Plus);
            RunTest("MSBuildLogParser_ParseLine_ParallelNodePrefixes_1_And_1Colon7", Test_MSBuildLogParser_ParseLine_ParallelNodePrefixes_1_And_1Colon7);
            RunTest("MSBuildLogParser_ParseLine_ThreeAndFourTupleCoordinates", Test_MSBuildLogParser_ParseLine_ThreeAndFourTupleCoordinates);
            RunTest("MSBuildLogParser_ParseLine_PrefixFreeNuGetAndNetSdkLines", Test_MSBuildLogParser_ParseLine_PrefixFreeNuGetAndNetSdkLines);
            RunTest("MSBuildLogParser_ParseLine_McCaAndSyslibCodes", Test_MSBuildLogParser_ParseLine_McCaAndSyslibCodes);
            RunTest("MSBuildLogParser_ParseLine_SubcategoryWarningsAndErrors", Test_MSBuildLogParser_ParseLine_SubcategoryWarningsAndErrors);
            RunTest("MSBuildLogParser_ParsePerformance_Under10Seconds", Test_MSBuildLogParser_ParsePerformance_Under10Seconds);
            RunTest("MSBuildLogParser_ParseText_DeduplicatesSummaryBlockErrorsAndWarnings", Test_MSBuildLogParser_ParseText_DeduplicatesSummaryBlockErrorsAndWarnings);
            RunTest("MSBuildLogParser_ParseText_DeduplicatesAnsiFormattingAndPathSlashes", Test_MSBuildLogParser_ParseText_DeduplicatesAnsiFormattingAndPathSlashes);
            RunTest("MSBuildLogParser_ParseText_PreservesSameLineDistinctErrorsAndMultiProjectDiagnostics", Test_MSBuildLogParser_ParseText_PreservesSameLineDistinctErrorsAndMultiProjectDiagnostics);

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
            RunTest("PatchGenerator_FindClassInsertionPoint_MasksCommentsStringsAndSingleLineClass", Test_PatchGenerator_FindClassInsertionPoint_MasksCommentsStringsAndSingleLineClass);
            RunTest("PatchGenerator_GenerateMemberStub_DelegatesToExistingOverload", Test_PatchGenerator_GenerateMemberStub_Delegates);
            RunTest("PatchGenerator_GenerateMemberStub_DelegatesWithValueTypeParameters", Test_PatchGenerator_GenerateMemberStub_DelegatesWithValueTypeParameters);
            RunTest("PatchGenerator_ExtractParameterNames_HandlesDefaultsNullablesAndWhitespace", Test_PatchGenerator_ExtractParameterNames_HandlesDefaultsNullablesAndWhitespace);
            RunTest("PatchGenerator_FindDelegationCall_MatchesCandidateOverloadsConsistently", Test_PatchGenerator_FindDelegationCall_MatchesCandidateOverloadsConsistently);
            RunTest("PatchGenerator_GenerateMemberStub_GeneratesNotImplementedException", Test_PatchGenerator_GenerateMemberStub_NotImplemented);
            RunTest("PatchGenerator_GenerateUnifiedDiff_ProducesValidDiffHeader", Test_PatchGenerator_GenerateUnifiedDiff);
            RunTest("PatchGenerator_GenerateUnifiedDiff_GitApplyEofAndMissingTrailingNewline", Test_PatchGenerator_GenerateUnifiedDiff_GitApplyEofAndMissingTrailingNewline);
            RunTest("PatchGenerator_GeneratePatches_GitApplyPreservesUtf8BomAndCrlf", Test_PatchGenerator_GeneratePatches_GitApplyPreservesUtf8BomAndCrlf);
            RunTest("PatchGenerator_RepeatedMemberLookups_UseCacheAndGenerateCorrectStubs", Test_PatchGenerator_RepeatedMemberLookups_UseCacheAndGenerateCorrectStubs);
            RunTest("PatchGenerator_ProjectRelativePaths_ResolvesViaProjectPathAndSearchRoot", Test_PatchGenerator_ProjectRelativePaths_ResolvesViaProjectPathAndSearchRoot);
            RunTest("PatchGenerator_MultiProjectLogs_ResolvesDistinctFilesWithSameRelativePath", Test_PatchGenerator_MultiProjectLogs_ResolvesDistinctFilesWithSameRelativePath);
            RunTest("PatchGenerator_RootRelativeFallback_WhenProjectPathEmptyOrCandidateMissing", Test_PatchGenerator_RootRelativeFallback_WhenProjectPathEmptyOrCandidateMissing);
            RunTest("Launcher_OutsideRootAndFlagOverrides_PreservesRepoRootHelpAndExplicitFlags", Test_Launcher_OutsideRootAndFlagOverrides_PreservesRepoRootHelpAndExplicitFlags);

            // 5. PatchApplicator Tests
            RunTest("PatchApplicator_ApplyPatch_InsertsCodeWithoutDamagingComments", Test_PatchApplicator_ApplyPatch);
            RunTest("PatchApplicator_ApplyPatch_GuardsAgainstDuplicateApplication", Test_PatchApplicator_GuardsDuplicate);
            RunTest("PatchApplicator_ApplyPatch_PreservesBomAndNativeLineEndings", Test_PatchApplicator_PreservesBomAndNativeLineEndings);
            RunTest("PatchApplicator_ApplyPatch_MultiMemberDuplicateGuardFiltersExistingMembers", Test_PatchApplicator_MultiMemberDuplicateGuardFiltersExistingMembers);

            // 6. ReportGenerator Tests
            RunTest("ReportGenerator_GenerateMarkdownReport_ProducesFormattedMarkdown", Test_ReportGenerator_Markdown);
            RunTest("ReportGenerator_GenerateMarkdownReport_ReplacesLineBreaksInTableCellsAndKeepsDetails", Test_ReportGenerator_Markdown_MultilineTableCellsAndDetails);
            RunTest("ReportGenerator_GenerateJsonReport_ProducesValidJson", Test_ReportGenerator_Json);
            RunTest("ReportGenerator_PrintConsoleSummary_DisambiguatesFailuresAndAvoidsParserMatches", Test_ReportGenerator_PrintConsoleSummary_DisambiguatesFailuresAndAvoidsParserMatches);

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
            RunTest("InvokePatcherMain_CapturesOutputAndRestoresConsoleStreams", Test_InvokePatcherMain_CapturesOutputAndRestoresConsoleStreams);
            RunTest("InvokePatcherMain_RestoresConsoleStreams_WhenExceptionThrown", Test_InvokePatcherMain_RestoresConsoleStreams_WhenExceptionThrown);

            // 9. CI Workflow Configuration Tests
            RunTest("Workflow_PostMergeBuildVerification_HasDiagnosticReportingAndArtifactUpload", Test_Workflow_PostMergeBuildVerification_HasDiagnosticReportingAndArtifactUpload);
            RunTest("Workflow_PostMergeBuildVerification_VerifiesWindowsTargetAndMultiOsMatrix", Test_Workflow_PostMergeBuildVerification_VerifiesWindowsTargetAndMultiOsMatrix);

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

        static void Test_MSBuildLogParser_ParseLine_ExtractsNuGetVulnerabilityAuditErrors()
        {
            var parser = new MSBuildLogParser();
            string[] codes = { "NU1901", "NU1902", "NU1903", "NU1904", "NU1004" };
            foreach (string code in codes)
            {
                string standaloneRestoreLine = $"/app/CollationModifier/CollationModifier.csproj : error {code}: Warning As Error: Package 'System.Text.Json' 8.0.0 has a known high severity vulnerability, https://github.com/advisories/GHSA-8g4q-xg66-9fp4";
                var standaloneDiag = parser.ParseLine(standaloneRestoreLine);
                Assert(standaloneDiag != null, $"Expected standalone {code} restore error to be parsed.");
                Assert(standaloneDiag!.Code == code, $"Expected code {code}, got {standaloneDiag.Code}");
                Assert(standaloneDiag.Severity == DiagnosticSeverity.Error, $"Expected severity Error for {code}, got {standaloneDiag.Severity}");
                Assert(standaloneDiag.FilePath == "/app/CollationModifier/CollationModifier.csproj", $"Expected project FilePath for {code}, got '{standaloneDiag.FilePath}'");

                string graphRestoreLine = $"{standaloneRestoreLine} [/app/CollationModifier/CollationModifier.csproj]";
                var graphDiag = parser.ParseLine(graphRestoreLine);
                Assert(graphDiag != null, $"Expected project-suffixed {code} restore error to be parsed.");
                Assert(graphDiag!.Code == code, $"Expected code {code}, got {graphDiag.Code}");
                Assert(graphDiag.Severity == DiagnosticSeverity.Error, $"Expected severity Error for {code}, got {graphDiag.Severity}");
                Assert(graphDiag.ProjectPath == "/app/CollationModifier/CollationModifier.csproj", $"Expected ProjectPath for {code}, got '{graphDiag.ProjectPath}'");
            }
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

        static void Test_MSBuildLogParser_ParseLine_CoordinateFreeUnrecognizedAndMessageSeverity_ReturnsNull()
        {
            var parser = new MSBuildLogParser();

            string unexpectedLine = "MSBUILD : unexpected MSB1003: Unrecognized build failure.";
            Assert(parser.ParseLine(unexpectedLine) == null, "Expected unrecognized 'unexpected' severity line to return null.");

            string msbuildMessageLine = "MSBUILD : message MSB1001: Informational build message.";
            Assert(parser.ParseLine(msbuildMessageLine) == null, "Expected 'message' severity line with origin to return null.");

            string highMessageLine = "MSBUILD : high message MSB4000: High importance informational message.";
            Assert(parser.ParseLine(highMessageLine) == null, "Expected subcategory 'high message' line to return null.");

            string coordMessageLine = "Services/DatabaseService.cs(11,36): message CS8001: Informational compiler message.";
            Assert(parser.ParseLine(coordMessageLine) == null, "Expected coordinate-bearing 'message' line to return null.");

            string prefixFreeMessageLine = "message NU1500: Restore informational output.";
            Assert(parser.ParseLine(prefixFreeMessageLine) == null, "Expected prefix-free 'message' line to return null.");

            string quotedErrorInsideMessageLine = "message MSB4000: error CS1002: Quoted error inside message text";
            Assert(parser.ParseLine(quotedErrorInsideMessageLine) == null, "Expected prefix-free 'message' line containing quoted error text to return null.");
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

        static void Test_MSBuildLogParser_ParseLine_SpecialPathCharacters_X86_At_Plus()
        {
            var parser = new MSBuildLogParser();

            string x86Line = @"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Microsoft.Common.CurrentVersion.targets(2301,5): error MSB3245: Could not resolve this reference. [C:\Work@2\My+App\CollationModifier.csproj]";
            var x86Diag = parser.ParseLine(x86Line);
            Assert(x86Diag != null, "Expected (x86) path diagnostic to parse.");
            Assert(x86Diag!.FilePath == @"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Microsoft.Common.CurrentVersion.targets", $"Unexpected FilePath '{x86Diag.FilePath}'");
            Assert(x86Diag.LineNumber == 2301, $"Expected line 2301, got {x86Diag.LineNumber}");
            Assert(x86Diag.ColumnNumber == 5, $"Expected col 5, got {x86Diag.ColumnNumber}");
            Assert(x86Diag.Severity == DiagnosticSeverity.Error, $"Expected Error, got {x86Diag.Severity}");
            Assert(x86Diag.Code == "MSB3245", $"Expected MSB3245, got {x86Diag.Code}");
            Assert(x86Diag.ProjectPath == @"C:\Work@2\My+App\CollationModifier.csproj", $"Unexpected ProjectPath '{x86Diag.ProjectPath}'");

            string atPlusCoordLine = "/home/runner/work/repo@2/src/Collation+Modifier/Program.cs(42,10): warning CS0168: The variable 'ex' is declared but never used [/home/runner/work/repo@2/src/Collation+Modifier/App.csproj]";
            var atPlusDiag = parser.ParseLine(atPlusCoordLine);
            Assert(atPlusDiag != null, "Expected @ and + path diagnostic with coordinates to parse.");
            Assert(atPlusDiag!.FilePath == "/home/runner/work/repo@2/src/Collation+Modifier/Program.cs", $"Unexpected FilePath '{atPlusDiag.FilePath}'");
            Assert(atPlusDiag.LineNumber == 42 && atPlusDiag.ColumnNumber == 10, $"Expected (42,10), got ({atPlusDiag.LineNumber},{atPlusDiag.ColumnNumber})");
            Assert(atPlusDiag.Severity == DiagnosticSeverity.Warning, $"Expected Warning, got {atPlusDiag.Severity}");

            string numericDirLine = @"C:\Build (2022)\Program Files (x86)\Module.cs(18,4): error CS1002: ; expected";
            var numericDirDiag = parser.ParseLine(numericDirLine);
            Assert(numericDirDiag != null, "Expected numeric directory path before coordinates to parse.");
            Assert(numericDirDiag!.FilePath == @"C:\Build (2022)\Program Files (x86)\Module.cs", $"Unexpected FilePath '{numericDirDiag.FilePath}'");
            Assert(numericDirDiag.LineNumber == 18 && numericDirDiag.ColumnNumber == 4, $"Expected (18,4), got ({numericDirDiag.LineNumber},{numericDirDiag.ColumnNumber})");

            string coordFreeSpecialPath = @"C:\Program Files (x86)\dotnet\sdk@8\My+Targets.targets : error NETSDK1045: The current .NET SDK does not support targeting .NET 9.0.";
            var coordFreeDiag = parser.ParseLine(coordFreeSpecialPath);
            Assert(coordFreeDiag != null, "Expected coordinate-free (x86)/@/+ path diagnostic to parse.");
            Assert(coordFreeDiag!.FilePath == @"C:\Program Files (x86)\dotnet\sdk@8\My+Targets.targets", $"Unexpected FilePath '{coordFreeDiag.FilePath}'");
            Assert(coordFreeDiag.LineNumber == 0 && coordFreeDiag.ColumnNumber == 0, "Expected 0 coordinates.");
            Assert(coordFreeDiag.Code == "NETSDK1045", $"Expected NETSDK1045, got {coordFreeDiag.Code}");
        }

        static void Test_MSBuildLogParser_ParseLine_ParallelNodePrefixes_1_And_1Colon7()
        {
            var parser = new MSBuildLogParser();

            string node1Coord = "1>Services/DatabaseService.cs(11,36): error CS0535: 'DatabaseService' does not implement interface member [CollationModifier.csproj]";
            var diag1 = parser.ParseLine(node1Coord);
            Assert(diag1 != null, "Expected 1> coordinate diagnostic to parse.");
            Assert(diag1!.FilePath == "Services/DatabaseService.cs", $"Expected 'Services/DatabaseService.cs', got '{diag1.FilePath}'");
            Assert(diag1.LineNumber == 11 && diag1.ColumnNumber == 36, $"Expected (11,36), got ({diag1.LineNumber},{diag1.ColumnNumber})");
            Assert(diag1.Code == "CS0535", $"Expected CS0535, got {diag1.Code}");

            string node1Colon7Coord = "1:7>Views/MainWindow.xaml(15,6,15,24): error MC3074: The tag 'InvalidTag' does not exist in XML namespace [CollationModifier.csproj]";
            var diag17 = parser.ParseLine(node1Colon7Coord);
            Assert(diag17 != null, "Expected 1:7> coordinate diagnostic to parse.");
            Assert(diag17!.FilePath == "Views/MainWindow.xaml", $"Expected 'Views/MainWindow.xaml', got '{diag17.FilePath}'");
            Assert(diag17.LineNumber == 15 && diag17.ColumnNumber == 6, $"Expected (15,6), got ({diag17.LineNumber},{diag17.ColumnNumber})");
            Assert(diag17.Code == "MC3074", $"Expected MC3074, got {diag17.Code}");

            string nodeSpacedCoordFree = @"2>  C:\Program Files (x86)\App\App.csproj : warning NU1701: Package restored using fallback framework.";
            var diagSpaced = parser.ParseLine(nodeSpacedCoordFree);
            Assert(diagSpaced != null, "Expected spaced 2> coordinate-free diagnostic to parse.");
            Assert(diagSpaced!.FilePath == @"C:\Program Files (x86)\App\App.csproj", $"Unexpected FilePath '{diagSpaced.FilePath}'");
            Assert(diagSpaced.Severity == DiagnosticSeverity.Warning, $"Expected Warning, got {diagSpaced.Severity}");
            Assert(diagSpaced.Code == "NU1701", $"Expected NU1701, got {diagSpaced.Code}");

            string nodePrefixFree = "1:7>error NU1101: Unable to find package Bogus.Package.";
            var diagPrefixFree = parser.ParseLine(nodePrefixFree);
            Assert(diagPrefixFree != null, "Expected 1:7> prefix-free NuGet error to parse.");
            Assert(diagPrefixFree!.FilePath == string.Empty, $"Expected empty FilePath, got '{diagPrefixFree.FilePath}'");
            Assert(diagPrefixFree.Severity == DiagnosticSeverity.Error, $"Expected Error, got {diagPrefixFree.Severity}");
            Assert(diagPrefixFree.Code == "NU1101", $"Expected NU1101, got {diagPrefixFree.Code}");
        }

        static void Test_MSBuildLogParser_ParseLine_ThreeAndFourTupleCoordinates()
        {
            var parser = new MSBuildLogParser();

            string threeTuple = "Services/Parser.cs(42,8,20): warning CA1822: Member 'Parse' does not access instance data and can be marked as static [CollationModifier.csproj]";
            var diag3 = parser.ParseLine(threeTuple);
            Assert(diag3 != null, "Expected 3-tuple coordinate diagnostic to parse.");
            Assert(diag3!.FilePath == "Services/Parser.cs", $"Expected 'Services/Parser.cs', got '{diag3.FilePath}'");
            Assert(diag3.LineNumber == 42, $"Expected line 42, got {diag3.LineNumber}");
            Assert(diag3.ColumnNumber == 8, $"Expected col 8, got {diag3.ColumnNumber}");
            Assert(diag3.Severity == DiagnosticSeverity.Warning, $"Expected Warning, got {diag3.Severity}");
            Assert(diag3.Code == "CA1822", $"Expected CA1822, got {diag3.Code}");

            string fourTuple = "Views/MainWindow.xaml(14,6,14,28): error MC3074: The tag 'BadControl' does not exist in XML namespace [CollationModifier.csproj]";
            var diag4 = parser.ParseLine(fourTuple);
            Assert(diag4 != null, "Expected 4-tuple coordinate diagnostic to parse.");
            Assert(diag4!.FilePath == "Views/MainWindow.xaml", $"Expected 'Views/MainWindow.xaml', got '{diag4.FilePath}'");
            Assert(diag4.LineNumber == 14, $"Expected line 14, got {diag4.LineNumber}");
            Assert(diag4.ColumnNumber == 6, $"Expected col 6, got {diag4.ColumnNumber}");
            Assert(diag4.Severity == DiagnosticSeverity.Error, $"Expected Error, got {diag4.Severity}");
            Assert(diag4.Code == "MC3074", $"Expected MC3074, got {diag4.Code}");
        }

        static void Test_MSBuildLogParser_ParseLine_PrefixFreeNuGetAndNetSdkLines()
        {
            var parser = new MSBuildLogParser();

            string nuError = "  error NU1101: Unable to find package NonExistent.Package. No packages exist with this id in source(s): nuget.org";
            var diagNu = parser.ParseLine(nuError);
            Assert(diagNu != null, "Expected prefix-free NU1101 error to parse.");
            Assert(diagNu!.FilePath == string.Empty, $"Expected empty FilePath, got '{diagNu.FilePath}'");
            Assert(diagNu.LineNumber == 0 && diagNu.ColumnNumber == 0, $"Expected (0,0), got ({diagNu.LineNumber},{diagNu.ColumnNumber})");
            Assert(diagNu.Severity == DiagnosticSeverity.Error, $"Expected Error, got {diagNu.Severity}");
            Assert(diagNu.Code == "NU1101", $"Expected NU1101, got {diagNu.Code}");
            Assert(diagNu.Message.Contains("Unable to find package NonExistent.Package"), $"Unexpected message '{diagNu.Message}'");

            string netSdkError = "error NETSDK1045: The current .NET SDK does not support targeting .NET 9.0. [/app/CollationModifier/CollationModifier.csproj]";
            var diagSdk = parser.ParseLine(netSdkError);
            Assert(diagSdk != null, "Expected prefix-free NETSDK1045 error to parse.");
            Assert(diagSdk!.FilePath == string.Empty, $"Expected empty FilePath, got '{diagSdk.FilePath}'");
            Assert(diagSdk.LineNumber == 0 && diagSdk.ColumnNumber == 0, $"Expected (0,0), got ({diagSdk.LineNumber},{diagSdk.ColumnNumber})");
            Assert(diagSdk.Severity == DiagnosticSeverity.Error, $"Expected Error, got {diagSdk.Severity}");
            Assert(diagSdk.Code == "NETSDK1045", $"Expected NETSDK1045, got {diagSdk.Code}");
            Assert(diagSdk.ProjectPath == "/app/CollationModifier/CollationModifier.csproj", $"Unexpected ProjectPath '{diagSdk.ProjectPath}'");

            string nuWarning = "warning NU1701: Package 'LegacyLib 1.0.0' was restored using '.NETFramework,Version=v4.6.1'.";
            var diagWarn = parser.ParseLine(nuWarning);
            Assert(diagWarn != null, "Expected prefix-free NU1701 warning to parse.");
            Assert(diagWarn!.FilePath == string.Empty, $"Expected empty FilePath, got '{diagWarn.FilePath}'");
            Assert(diagWarn.Severity == DiagnosticSeverity.Warning, $"Expected Warning, got {diagWarn.Severity}");
            Assert(diagWarn.Code == "NU1701", $"Expected NU1701, got {diagWarn.Code}");

            // Guard check: prefix-free severity must not be consumed as <file> when message itself contains a colon and secondary code
            string nestedErrorText = "error NU1101: Restore failed: error MSB1003: Inner detail";
            var diagNested = parser.ParseLine(nestedErrorText);
            Assert(diagNested != null, "Expected nested colon prefix-free NU1101 to parse.");
            Assert(diagNested!.FilePath == string.Empty, $"Expected empty FilePath, got '{diagNested.FilePath}'");
            Assert(diagNested.Code == "NU1101", $"Expected outer code NU1101, got {diagNested.Code}");
            Assert(diagNested.Message == "Restore failed: error MSB1003: Inner detail", $"Unexpected message '{diagNested.Message}'");
        }

        static void Test_MSBuildLogParser_ParseLine_McCaAndSyslibCodes()
        {
            var parser = new MSBuildLogParser();

            string mcCoordFree = "Views/MainWindow.xaml : error MC3000: 'Cannot find type' [CollationModifier.csproj]";
            var diagMc = parser.ParseLine(mcCoordFree);
            Assert(diagMc != null, "Expected coordinate-free MC3000 error to parse.");
            Assert(diagMc!.FilePath == "Views/MainWindow.xaml", $"Unexpected FilePath '{diagMc.FilePath}'");
            Assert(diagMc.Severity == DiagnosticSeverity.Error, $"Expected Error, got {diagMc.Severity}");
            Assert(diagMc.Code == "MC3000", $"Expected MC3000, got {diagMc.Code}");

            string caCoordFree = "CSC : warning CA1062: Validate arguments of public methods [CollationModifier.csproj]";
            var diagCa = parser.ParseLine(caCoordFree);
            Assert(diagCa != null, "Expected coordinate-free CA1062 warning to parse.");
            Assert(diagCa!.FilePath == "CSC", $"Expected FilePath 'CSC', got '{diagCa.FilePath}'");
            Assert(diagCa.Severity == DiagnosticSeverity.Warning, $"Expected Warning, got {diagCa.Severity}");
            Assert(diagCa.Code == "CA1062", $"Expected CA1062, got {diagCa.Code}");

            string syslibCoordFree = "Security/Crypto.cs : warning SYSLIB1045: Convert to 'GeneratedRegexAttribute' [CollationModifier.csproj]";
            var diagSyslib = parser.ParseLine(syslibCoordFree);
            Assert(diagSyslib != null, "Expected coordinate-free SYSLIB1045 warning to parse.");
            Assert(diagSyslib!.FilePath == "Security/Crypto.cs", $"Unexpected FilePath '{diagSyslib.FilePath}'");
            Assert(diagSyslib.Severity == DiagnosticSeverity.Warning, $"Expected Warning, got {diagSyslib.Severity}");
            Assert(diagSyslib.Code == "SYSLIB1045", $"Expected SYSLIB1045, got {diagSyslib.Code}");
        }

        static void Test_MSBuildLogParser_ParseLine_SubcategoryWarningsAndErrors()
        {
            var parser = new MSBuildLogParser();

            string coordSubcatWarn = "Views/MainWindow.xaml(22,10): Localization warning MC6000: Invalid localization comment [CollationModifier.csproj]";
            var diagCoordWarn = parser.ParseLine(coordSubcatWarn);
            Assert(diagCoordWarn != null, "Expected coordinate subcategory warning to parse.");
            Assert(diagCoordWarn!.Severity == DiagnosticSeverity.Warning, $"Expected Warning for Localization warning, got {diagCoordWarn.Severity}");
            Assert(diagCoordWarn.Code == "MC6000", $"Expected MC6000, got {diagCoordWarn.Code}");

            string coordSubcatErr = "Views/MainWindow.xaml(12,5): XamlInternalError error MC1000: Unknown build error [CollationModifier.csproj]";
            var diagCoordErr = parser.ParseLine(coordSubcatErr);
            Assert(diagCoordErr != null, "Expected coordinate subcategory error to parse.");
            Assert(diagCoordErr!.Severity == DiagnosticSeverity.Error, $"Expected Error for XamlInternalError error, got {diagCoordErr.Severity}");
            Assert(diagCoordErr.Code == "MC1000", $"Expected MC1000, got {diagCoordErr.Code}");

            string coordFreeSubcatWarn = "CSC : compiler warning CS8002: Referenced assembly 'LegacyLib' does not have a strong name.";
            var diagCoordFreeWarn = parser.ParseLine(coordFreeSubcatWarn);
            Assert(diagCoordFreeWarn != null, "Expected coordinate-free subcategory warning to parse.");
            Assert(diagCoordFreeWarn!.FilePath == "CSC", $"Expected FilePath 'CSC', got '{diagCoordFreeWarn.FilePath}'");
            Assert(diagCoordFreeWarn.Severity == DiagnosticSeverity.Warning, $"Expected Warning for compiler warning, got {diagCoordFreeWarn.Severity}");
            Assert(diagCoordFreeWarn.Code == "CS8002", $"Expected CS8002, got {diagCoordFreeWarn.Code}");

            string multiWordSubcatWarn = "CSC : Command line warning CS2002: Source file 'Program.cs' specified multiple times";
            var diagMultiWarn = parser.ParseLine(multiWordSubcatWarn);
            Assert(diagMultiWarn != null, "Expected multi-word subcategory warning to parse.");
            Assert(diagMultiWarn!.Severity == DiagnosticSeverity.Warning, $"Expected Warning for Command line warning, got {diagMultiWarn.Severity}");
            Assert(diagMultiWarn.Code == "CS2002", $"Expected CS2002, got {diagMultiWarn.Code}");

            string prefixFreeSubcatWarn = "restore warning NU1603: Package dependency resolved to higher version.";
            var diagPrefixFreeWarn = parser.ParseLine(prefixFreeSubcatWarn);
            Assert(diagPrefixFreeWarn != null, "Expected prefix-free subcategory warning to parse.");
            Assert(diagPrefixFreeWarn!.FilePath == string.Empty, $"Expected empty FilePath, got '{diagPrefixFreeWarn.FilePath}'");
            Assert(diagPrefixFreeWarn.Severity == DiagnosticSeverity.Warning, $"Expected Warning for restore warning, got {diagPrefixFreeWarn.Severity}");
            Assert(diagPrefixFreeWarn.Code == "NU1603", $"Expected NU1603, got {diagPrefixFreeWarn.Code}");

            // Verify Program.Main returns 0 on logs with message lines and subcategory warnings
            string tempDir = Path.Combine(Path.GetTempPath(), $"subcat_clean_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                string logFile = Path.Combine(tempDir, "clean_with_messages_and_subcat_warnings.log");
                string outputDir = Path.Combine(tempDir, "out");
                File.WriteAllText(logFile, string.Join("\n", new[]
                {
                    "MSBUILD : message MSB1001: Informational build trace.",
                    "CSC : compiler warning CS8002: Referenced assembly 'LegacyLib' does not have a strong name.",
                    "Views/MainWindow.xaml(22,10): Localization warning MC6000: Invalid localization comment [CollationModifier.csproj]",
                    "Build succeeded.",
                    "    2 Warning(s)",
                    "    0 Error(s)"
                }));

                int exitCode = MSBuildDiagnosticPatcher.Program.Main(new[] { logFile, "-o", outputDir });
                Assert(exitCode == 0, $"Expected exit code 0 when only message lines and subcategory warnings exist, got {exitCode}");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
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

        static void Test_MSBuildLogParser_ParseText_DeduplicatesSummaryBlockErrorsAndWarnings()
        {
            var parser = new MSBuildLogParser();
            string firstWarningLine = "  ViewModels/MainViewModel.cs(120,15): warning CS0168: The variable 'ex' is declared but never used [/app/CollationModifier/CollationModifier.csproj]";
            string firstErrorLine = "  Services/DatabaseService.cs(11,36): error CS0535: 'DatabaseService' does not implement interface member 'IDatabaseService.BackupDatabaseAsync(string, string, CancellationToken)' [/app/CollationModifier/CollationModifier.csproj]";
            string firstNugetLine = "  /app/CollationModifier/CollationModifier.csproj : error NU1605: Detected package downgrade: Microsoft.Data.SqlClient from 5.1.0 to 5.0.0 [/app/CollationModifier/CollationModifier.csproj]";

            string log = $@"Microsoft (R) Build Engine version 17.8.3+195e7f5a3 for .NET
  Determining projects to restore...
{firstNugetLine}
{firstWarningLine}
{firstErrorLine}

Build FAILED.

    ViewModels/MainViewModel.cs(120,15): warning CS0168: The variable 'ex' is declared but never used [/app/CollationModifier/CollationModifier.csproj]
    /app/CollationModifier/CollationModifier.csproj : error NU1605: Detected package downgrade: Microsoft.Data.SqlClient from 5.1.0 to 5.0.0 [/app/CollationModifier/CollationModifier.csproj]
    Services/DatabaseService.cs(11,36): error CS0535: 'DatabaseService' does not implement interface member 'IDatabaseService.BackupDatabaseAsync(string, string, CancellationToken)' [/app/CollationModifier/CollationModifier.csproj]
    1 Warning(s)
    2 Error(s)

Time Elapsed 00:00:02.15
";

            var parsed = parser.ParseText(log);
            Assert(parsed.Count == 3, $"Expected 3 unique diagnostics after collapsing summary duplicates, got {parsed.Count}.");
            Assert(parsed[0].Code == "NU1605" && parsed[0].RawLine == firstNugetLine, "Expected first diagnostic to preserve log order and first RawLine for NU1605.");
            Assert(parsed[1].Code == "CS0168" && parsed[1].Severity == DiagnosticSeverity.Warning && parsed[1].RawLine == firstWarningLine, "Expected second diagnostic to preserve first warning occurrence.");
            Assert(parsed[2].Code == "CS0535" && parsed[2].Severity == DiagnosticSeverity.Error && parsed[2].RawLine == firstErrorLine, "Expected third diagnostic to preserve first CS0535 occurrence.");

            var report = new DiagnosticReport { SourceLogPath = "/tmp/build.log", Diagnostics = parsed };
            Assert(report.ErrorCount == 2, $"Expected ErrorCount 2, got {report.ErrorCount}.");
            Assert(report.WarningCount == 1, $"Expected WarningCount 1, got {report.WarningCount}.");

            var reporter = new ReportGenerator();
            string markdown = reporter.GenerateMarkdownReport(report);
            Assert(markdown.Contains("- **Roslyn Errors**: `2`"), "Expected Markdown summary to report 2 errors.");
            Assert(markdown.Contains("- **Roslyn Warnings**: `1`"), "Expected Markdown summary to report 1 warning.");
            int cs0535Rows = markdown.Split(new[] { "| `CS0535` |" }, StringSplitOptions.None).Length - 1;
            Assert(cs0535Rows == 1, $"Expected exactly 1 CS0535 row in Markdown table, found {cs0535Rows}.");
        }

        static void Test_MSBuildLogParser_ParseText_DeduplicatesAnsiFormattingAndPathSlashes()
        {
            var parser = new MSBuildLogParser();
            string inlineAnsiError = "  Services\\DatabaseService.cs(11,36): \u001b[31;1merror CS0535\u001b[0m: 'DatabaseService' does not implement interface member 'IDatabaseService.BackupDatabaseAsync()' [C:\\Repo\\CollationModifier\\CollationModifier.csproj]";
            string summaryAnsiError = "    \u001b[31;1mServices/DatabaseService.cs(11,36): error cs0535: 'DatabaseService' does not implement interface member 'IDatabaseService.BackupDatabaseAsync()' [C:/Repo/CollationModifier/CollationModifier.csproj]\u001b[0m";
            string plainWarning = "ViewModels/MainViewModel.cs(45,9): warning CS0219: The variable 'flag' is assigned but its value is never used [C:/Repo/CollationModifier/CollationModifier.csproj]";
            string summaryAnsiWarning = "  \u001b[33mViewModels\\MainViewModel.cs(45,9): warning CS0219: The variable 'flag' is assigned but its value is never used [C:\\Repo\\CollationModifier\\CollationModifier.csproj]\u001b[0m";

            string log = string.Join("\n", new[]
            {
                inlineAnsiError,
                plainWarning,
                "Build FAILED.",
                summaryAnsiWarning,
                summaryAnsiError
            });

            var parsed = parser.ParseText(log);
            Assert(parsed.Count == 2, $"Expected 2 unique diagnostics across ANSI and slash variants, got {parsed.Count}.");
            Assert(parsed[0].Code == "CS0535" && parsed[0].RawLine == inlineAnsiError, "Expected first error RawLine preserved.");
            Assert(parsed[1].Code == "CS0219" && parsed[1].RawLine == plainWarning, "Expected first warning RawLine preserved.");

            // Ensure ParseLine itself remains stateless for direct callers
            var single1 = parser.ParseLine(inlineAnsiError);
            var single2 = parser.ParseLine(inlineAnsiError);
            Assert(single1 != null && single2 != null, "Expected ParseLine to remain stateless across repeated calls.");
        }

        static void Test_MSBuildLogParser_ParseText_PreservesSameLineDistinctErrorsAndMultiProjectDiagnostics()
        {
            var parser = new MSBuildLogParser();
            string member1Error = "Services/DatabaseService.cs(11,36): error CS0535: 'DatabaseService' does not implement interface member 'IDatabaseService.BackupDatabaseAsync(string, string, CancellationToken)' [CollationModifier/CollationModifier.csproj]";
            string member2Error = "Services/DatabaseService.cs(11,36): error CS0535: 'DatabaseService' does not implement interface member 'IDatabaseService.RestoreDatabaseAsync(string, string, CancellationToken)' [CollationModifier/CollationModifier.csproj]";
            string sameCoordOtherCode = "Services/DatabaseService.cs(11,36): warning CS1591: Missing XML comment for publicly visible type or member 'DatabaseService' [CollationModifier/CollationModifier.csproj]";
            string sharedFileProjA = "Shared/SqlHelper.cs(22,13): warning CS0168: The variable 'err' is declared but never used [CollationModifier/CollationModifier.csproj]";
            string sharedFileProjB = "Shared/SqlHelper.cs(22,13): warning CS0168: The variable 'err' is declared but never used [CollationModifier.Tests/CollationModifier.Tests.csproj]";

            string log = $@"
  {member1Error}
  {member2Error}
  {sameCoordOtherCode}
  {sharedFileProjA}
Build FAILED.
    {sameCoordOtherCode}
    {member1Error}
    {member2Error}
    {sharedFileProjA}

  {sharedFileProjB}
Build succeeded.
    {sharedFileProjB}
";

            var parsed = parser.ParseText(log);
            Assert(parsed.Count == 5, $"Expected 5 distinct diagnostics (3 same-line distinct + 2 per-project), got {parsed.Count}.");
            Assert(parsed.Count(d => d.Code == "CS0535") == 2, "Expected both same-line CS0535 missing members to be kept.");
            Assert(parsed.Count(d => d.Code == "CS1591") == 1, "Expected same-line warning with distinct code/severity to be kept.");
            Assert(parsed.Count(d => d.Code == "CS0168") == 2, "Expected shared-file diagnostics from distinct ProjectPath entries to be kept per project.");
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
            Assert(diff.Contains("@@ -1,3 +1,4 @@"), $"Expected accurate 3->4 hunk header without trailing empty line inflation, got:\n{diff}");
        }

        static void Test_PatchGenerator_FindClassInsertionPoint_MasksCommentsStringsAndSingleLineClass()
        {
            // Case 1: Comments, strings, verbatim strings, interpolated strings with nested quotes/braces, and char literals
            string complexSource = string.Join("\r\n", new[]
            {
                "// class TrickyService { }",
                "/* Multi-line comment mentioning class TrickyService { and extra } } */",
                "namespace SampleNs",
                "{",
                "    public class TrickyService : ITrickyService",
                "    {",
                "        private string _s1 = \"unmatched } brace \\\" { in regular string\";",
                "        private string _s2 = @\"verbatim \"\"quote\"\" and } } brace\";",
                "        private char _c1 = '}';",
                "        public string Format(string x) => $\"interp {{ literal }} and {x.Trim('}')} end\";",
                "        // trailing comment with unmatched } brace",
                "    }",
                "}",
                ""
            });

            int insertPos = PatchGenerator.FindClassInsertionPoint(complexSource, "TrickyService");
            Assert(insertPos > 0, "Expected valid insertion point for TrickyService.");
            // Verify insertion point is advanced past \r\n to the start of "    }" line, not between \r and \n
            Assert(complexSource[insertPos - 2] == '\r' && complexSource[insertPos - 1] == '\n',
                "Expected FindClassInsertionPoint to advance past \\r\\n.");
            Assert(complexSource.Substring(insertPos).StartsWith("    }\r\n}"),
                $"Expected insertion point at class closing brace line, got: '{complexSource.Substring(insertPos)}'");

            // Case 2: Single-line class following a using directive
            string singleLineSource = "using System;\r\npublic class CompactService : ICompactService { }\r\n";
            int singlePos = PatchGenerator.FindClassInsertionPoint(singleLineSource, "CompactService");
            Assert(singlePos > singleLineSource.IndexOf('{'),
                "Single-line class insertion point must be inside '{ }', not before class declaration.");
            Assert(singleLineSource[singlePos] == '}',
                $"Expected insertion point at closing brace of single-line class, got '{singleLineSource[singlePos]}'");
        }

        static void Test_PatchGenerator_GenerateUnifiedDiff_GitApplyEofAndMissingTrailingNewline()
        {
            string repoDir = Path.Combine(Path.GetTempPath(), $"git_apply_eof_{Guid.NewGuid():N}");
            Directory.CreateDirectory(repoDir);

            try
            {
                RunGitCommand(repoDir, "init");

                // 1. File whose class closing brace is within 2 lines of EOF (like DatabaseService.cs:L828-L832)
                string ifacePath = Path.Combine(repoDir, "IDbService.cs");
                File.WriteAllText(ifacePath, "using System.Threading;\nusing System.Threading.Tasks;\npublic interface IDbService\n{\n    Task PingAsync(CancellationToken cancellationToken = default);\n}\n", new UTF8Encoding(false));

                string serviceDir = Path.Combine(repoDir, "Services");
                Directory.CreateDirectory(serviceDir);
                string servicePath = Path.Combine(serviceDir, "DbService.cs");
                string eofServiceContent = string.Join("\n", new[]
                {
                    "using System;",
                    "using System.Threading;",
                    "using System.Threading.Tasks;",
                    "",
                    "namespace Demo.Services",
                    "{",
                    "    public class DbService : IDbService",
                    "    {",
                    "        private static void Helper()",
                    "        {",
                    "            Console.WriteLine(\"ok\");",
                    "        }",
                    "    }",
                    "}",
                    ""
                });
                File.WriteAllText(servicePath, eofServiceContent, new UTF8Encoding(false));

                var diagnostics = new List<CompilerDiagnostic>
                {
                    new CompilerDiagnostic
                    {
                        Code = "CS0535",
                        Severity = DiagnosticSeverity.Error,
                        FilePath = servicePath,
                        LineNumber = 7,
                        ColumnNumber = 18,
                        Message = "'DbService' does not implement interface member 'IDbService.PingAsync(CancellationToken)'"
                    }
                };

                var generator = new PatchGenerator(new InterfaceDefinitionResolver());
                var patches = generator.GeneratePatches(diagnostics, repoDir);
                Assert(patches.Count == 1, $"Expected 1 patch candidate, got {patches.Count}");
                Assert(!string.IsNullOrWhiteSpace(patches[0].MemberSignature), "Expected MemberSignature to be populated.");
                Assert(patches[0].Members.Count == 1, $"Expected 1 PatchMemberEntry, got {patches[0].Members.Count}");

                string patchFile = Path.Combine(repoDir, "DbService.patch");
                File.WriteAllText(patchFile, patches[0].UnifiedDiff, new UTF8Encoding(false));

                RunGitCommand(repoDir, $"apply --verbose \"{patchFile}\"");
                string patchedDisk = File.ReadAllText(servicePath);
                Assert(patchedDisk.Contains("public virtual Task PingAsync(CancellationToken cancellationToken = default)"),
                    "Expected git apply to insert PingAsync cleanly on EOF-adjacent class.");

                // 2. File lacking trailing newline at EOF must emit '\ No newline at end of file' and pass git apply
                string noNlPath = Path.Combine(repoDir, "NoNewlineService.cs");
                string noNlOriginal = "public class NoNewlineService : IDbService\n{\n}"; // no trailing \n
                File.WriteAllText(noNlPath, noNlOriginal, new UTF8Encoding(false));

                var noNlDiag = new List<CompilerDiagnostic>
                {
                    new CompilerDiagnostic
                    {
                        Code = "CS0535",
                        Severity = DiagnosticSeverity.Error,
                        FilePath = noNlPath,
                        LineNumber = 1,
                        ColumnNumber = 14,
                        Message = "'NoNewlineService' does not implement interface member 'IDbService.PingAsync(CancellationToken)'"
                    }
                };

                var noNlPatches = generator.GeneratePatches(noNlDiag, repoDir);
                Assert(noNlPatches.Count == 1, "Expected 1 patch for no-newline file.");
                Assert(noNlPatches[0].UnifiedDiff.Contains("\\ No newline at end of file"),
                    $"Expected missing EOF newline marker in diff, got:\n{noNlPatches[0].UnifiedDiff}");

                string noNlPatchFile = Path.Combine(repoDir, "NoNewlineService.patch");
                File.WriteAllText(noNlPatchFile, noNlPatches[0].UnifiedDiff, new UTF8Encoding(false));

                RunGitCommand(repoDir, $"apply --verbose \"{noNlPatchFile}\"");
                byte[] noNlAfterBytes = File.ReadAllBytes(noNlPath);
                Assert(noNlAfterBytes[noNlAfterBytes.Length - 1] == (byte)'}',
                    "Expected git apply to preserve absence of trailing newline at EOF.");
            }
            finally
            {
                if (Directory.Exists(repoDir)) Directory.Delete(repoDir, true);
            }
        }

        static void Test_PatchGenerator_GeneratePatches_GitApplyPreservesUtf8BomAndCrlf()
        {
            string repoDir = Path.Combine(Path.GetTempPath(), $"git_apply_bom_crlf_{Guid.NewGuid():N}");
            Directory.CreateDirectory(repoDir);

            try
            {
                RunGitCommand(repoDir, "init");

                string ifacePath = Path.Combine(repoDir, "IBomWorker.cs");
                File.WriteAllText(ifacePath, "public interface IBomWorker\r\n{\r\n    void RunWork(string taskName);\r\n}\r\n", new UTF8Encoding(true));

                // Short file (3 lines) with UTF-8 BOM and CRLF so the unified diff hunk starts at line 1
                string classPath = Path.Combine(repoDir, "BomWorker.cs");
                string originalCrlf = "public class BomWorker : IBomWorker\r\n{\r\n}\r\n";
                File.WriteAllText(classPath, originalCrlf, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

                var diagnostics = new List<CompilerDiagnostic>
                {
                    new CompilerDiagnostic
                    {
                        Code = "CS0535",
                        Severity = DiagnosticSeverity.Error,
                        FilePath = classPath,
                        LineNumber = 1,
                        ColumnNumber = 14,
                        Message = "'BomWorker' does not implement interface member 'IBomWorker.RunWork(string)'"
                    }
                };

                var generator = new PatchGenerator(new InterfaceDefinitionResolver());
                var patches = generator.GeneratePatches(diagnostics, repoDir);
                Assert(patches.Count == 1, "Expected 1 patch for BomWorker.");
                Assert(patches[0].UnifiedDiff.Contains(" \uFEFFpublic class BomWorker"),
                    "Expected line-1 UTF-8 BOM to be preserved in unified diff.");

                string patchFile = Path.Combine(repoDir, "BomWorker.patch");
                File.WriteAllText(patchFile, patches[0].UnifiedDiff, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                RunGitCommand(repoDir, $"apply --verbose \"{patchFile}\"");

                byte[] appliedBytes = File.ReadAllBytes(classPath);
                Assert(appliedBytes.Length >= 3 && appliedBytes[0] == 0xEF && appliedBytes[1] == 0xBB && appliedBytes[2] == 0xBF,
                    "Expected UTF-8 BOM bytes (EF BB BF) to be preserved after git apply.");

                string appliedText = new UTF8Encoding(true).GetString(appliedBytes, 3, appliedBytes.Length - 3);
                Assert(appliedText.Contains("public virtual void RunWork(string taskName)"),
                    "Expected RunWork stub after git apply.");
                AssertNoBareLf(appliedText, "git apply on CRLF + BOM file");
            }
            finally
            {
                if (Directory.Exists(repoDir)) Directory.Delete(repoDir, true);
            }
        }

        static void Test_PatchGenerator_CreatePatchForFile_LineEndingsAndEdgeCases()
        {
            var generator = new PatchGenerator();
            string tempRoot = Path.Combine(Path.GetTempPath(), $"patch_diff_edge_{Guid.NewGuid():N}");
            string targetFile = Path.Combine(tempRoot, "Src", "Sample.cs");

            string stubLf = "        public virtual void Missing()\n        {\n            throw new NotImplementedException();\n        }";
            string stubCrlf = stubLf.Replace("\n", "\r\n");

            string multiLineTemplate = string.Join("\n", new[]
            {
                "using System;",
                "namespace EdgeTests",
                "{",
                "    public class TargetClass : ITarget",
                "    {",
                "        public void Existing1() { }",
                "        public void Existing2() { }",
                "        public void Existing3() { }",
                "        public void Existing4() { }",
                "    }",
                "",
                "    public class NeighborClass",
                "    {",
                "        public void Neighbor1() { }",
                "        public void Neighbor2() { }",
                "        public void Neighbor3() { }",
                "    }",
                "}",
                ""
            });

            var scenarios = new (string Name, string Content, string Stubs, string ClassName)[]
            {
                ("MultiLine LF", multiLineTemplate, stubLf, "TargetClass"),
                ("MultiLine CRLF with LF stub", multiLineTemplate.Replace("\n", "\r\n"), stubLf, "TargetClass"),
                ("MultiLine CRLF with CRLF stub", multiLineTemplate.Replace("\n", "\r\n"), stubCrlf, "TargetClass"),
                ("MultiLine CR", multiLineTemplate.Replace("\n", "\r"), stubLf, "TargetClass"),
                ("CR closing brace immediately after CR", "namespace N\r{\r    public class TargetClass : ITarget\r    {\r        void A() {}\r}\r}\r", stubLf, "TargetClass"),
                ("Single-line class", "public class TargetClass : ITarget { }", stubLf, "TargetClass"),
                ("Single-line class with trailing LF", "public class TargetClass : ITarget { }\n", stubLf, "TargetClass"),
                ("Class near start of file (<4 breaks before)", "class TargetClass : ITarget\n{\n    void M() {}\n}\n// 1\n// 2\n// 3\n// 4\n// 5\n// 6\n", stubLf, "TargetClass"),
                ("Class at very end of file (<5 breaks after)", "// 1\n// 2\n// 3\n// 4\n// 5\nclass TargetClass : ITarget\n{\n    void M() {}\n}", stubLf, "TargetClass"),
                ("Stub matching lines in 5-line post-insert margin (fallback)", multiLineTemplate, "    }\n\n    public class NeighborClass\n    {\n        public void Injected() { }", "TargetClass")
            };

            foreach (var sc in scenarios)
            {
                var candidate = generator.CreatePatchForFile(
                    targetFile,
                    sc.Content,
                    sc.Stubs,
                    sc.ClassName,
                    "ITarget",
                    tempRoot);

                Assert(candidate != null, $"Expected patch candidate for scenario '{sc.Name}'");

                int insertPos = PatchGenerator.FindClassInsertionPoint(sc.Content, sc.ClassName);
                string fullNewContent = sc.Content.Insert(insertPos, "\n" + sc.Stubs + "\n");
                string expectedDiff = ReferenceGenerateUnifiedDiff(
                    sc.Content,
                    fullNewContent,
                    "a/Src/Sample.cs",
                    "b/Src/Sample.cs");

                AssertExactDiffEquals(expectedDiff, candidate!.UnifiedDiff, sc.Name);
            }
        }

        static void Test_PatchGenerator_LargeFileDiff_ExactOutputAndBoundedAllocations()
        {
            var generator = new PatchGenerator();
            string tempRoot = Path.Combine(Path.GetTempPath(), $"patch_large_{Guid.NewGuid():N}");
            string targetFile = Path.Combine(tempRoot, "LargeService.cs");
            string stubs =
                "        public virtual Task<int> ComputeAsync(CancellationToken cancellationToken = default)\n" +
                "        {\n" +
                "            throw new NotImplementedException();\n" +
                "        }";

            foreach (string nl in new[] { "\n", "\r\n", "\r" })
            {
                string nlLabel = nl == "\n" ? "LF" : (nl == "\r\n" ? "CRLF" : "CR");
                var sb = new StringBuilder(20_000 * 56);
                sb.Append("using System;").Append(nl);
                sb.Append("namespace LargeTests").Append(nl);
                sb.Append("{").Append(nl);
                sb.Append("    public class LargeService : ILargeService").Append(nl);
                sb.Append("    {").Append(nl);
                for (int i = 0; i < 200; i++)
                {
                    sb.Append("        public int Prop").Append(i).Append(" { get; set; } = ").Append(i).Append(';').Append(nl);
                }
                sb.Append("    }").Append(nl);
                sb.Append("    public class TrailingFixture").Append(nl);
                sb.Append("    {").Append(nl);
                for (int i = 0; i < 19_790; i++)
                {
                    sb.Append("        // Trailing fixture line ").Append(i).Append(" for 20k line file verification").Append(nl);
                }
                sb.Append("    }").Append(nl);
                sb.Append("}").Append(nl);

                string largeContent = sb.ToString();
                int insertPos = PatchGenerator.FindClassInsertionPoint(largeContent, "LargeService");
                string largeModified = largeContent.Insert(insertPos, "\n" + stubs + "\n");

                string expectedDiff = ReferenceGenerateUnifiedDiff(
                    largeContent,
                    largeModified,
                    "a/LargeService.cs",
                    "b/LargeService.cs");

                var candidate = generator.CreatePatchForFile(
                    targetFile,
                    largeContent,
                    stubs,
                    "LargeService",
                    "ILargeService",
                    tempRoot);

                Assert(candidate != null, $"Expected candidate for 20k {nlLabel} file");
                AssertExactDiffEquals(expectedDiff, candidate!.UnifiedDiff, $"CreatePatchForFile 20k {nlLabel}");

                string directDiff = PatchGenerator.GenerateUnifiedDiff(
                    largeContent,
                    largeModified,
                    "a/LargeService.cs",
                    "b/LargeService.cs");
                AssertExactDiffEquals(expectedDiff, directDiff, $"GenerateUnifiedDiff 20k {nlLabel}");

                if (nl == "\n")
                {
                    // Warm up pooled buffers and JIT before checking thread allocations
                    for (int w = 0; w < 5; w++)
                    {
                        generator.CreatePatchForFile(targetFile, largeContent, stubs, "LargeService", "ILargeService", tempRoot);
                        PatchGenerator.GenerateUnifiedDiff(largeContent, largeModified, "a/LargeService.cs", "b/LargeService.cs");
                    }

                    long beforePatchAlloc = GC.GetAllocatedBytesForCurrentThread();
                    var measuredCandidate = generator.CreatePatchForFile(targetFile, largeContent, stubs, "LargeService", "ILargeService", tempRoot);
                    long patchAlloc = GC.GetAllocatedBytesForCurrentThread() - beforePatchAlloc;
                    GC.KeepAlive(measuredCandidate);

                    long beforeDiffAlloc = GC.GetAllocatedBytesForCurrentThread();
                    string measuredDiff = PatchGenerator.GenerateUnifiedDiff(largeContent, largeModified, "a/LargeService.cs", "b/LargeService.cs");
                    long diffAlloc = GC.GetAllocatedBytesForCurrentThread() - beforeDiffAlloc;
                    GC.KeepAlive(measuredDiff);

                    const long maxAllowedBytes = 64 * 1024;
                    Assert(patchAlloc <= maxAllowedBytes,
                        $"Expected CreatePatchForFile on 20k lines to avoid full-file allocations (<= {maxAllowedBytes} B), but allocated {patchAlloc} B");
                    Assert(diffAlloc <= maxAllowedBytes,
                        $"Expected GenerateUnifiedDiff on 20k lines to avoid full-file line arrays (<= {maxAllowedBytes} B), but allocated {diffAlloc} B");
                }
            }
        }

        private static string ReferenceGenerateUnifiedDiff(
            string oldText,
            string newText,
            string oldHeader,
            string newHeader)
        {
            var oldLines = oldText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var newLines = newText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            int startCommon = 0;
            while (startCommon < oldLines.Length && startCommon < newLines.Length &&
                   oldLines[startCommon] == newLines[startCommon])
            {
                startCommon++;
            }

            int endCommonOld = oldLines.Length - 1;
            int endCommonNew = newLines.Length - 1;
            while (endCommonOld >= startCommon && endCommonNew >= startCommon &&
                   oldLines[endCommonOld] == newLines[endCommonNew])
            {
                endCommonOld--;
                endCommonNew--;
            }

            int contextBefore = Math.Max(0, startCommon - 3);
            int contextAfterOld = Math.Min(oldLines.Length, endCommonOld + 4);
            int contextAfterNew = Math.Min(newLines.Length, endCommonNew + 4);

            int hunkOldStart = contextBefore + 1;
            int hunkOldCount = contextAfterOld - contextBefore;
            int hunkNewStart = contextBefore + 1;
            int hunkNewCount = contextAfterNew - contextBefore;

            var sb = new StringBuilder();
            sb.AppendLine($"--- {oldHeader}");
            sb.AppendLine($"+++ {newHeader}");
            sb.AppendLine($"@@ -{hunkOldStart},{hunkOldCount} +{hunkNewStart},{hunkNewCount} @@");

            for (int i = contextBefore; i < startCommon; i++)
            {
                sb.AppendLine($" {oldLines[i]}");
            }

            for (int i = startCommon; i <= endCommonOld; i++)
            {
                sb.AppendLine($"-{oldLines[i]}");
            }

            for (int i = startCommon; i <= endCommonNew; i++)
            {
                sb.AppendLine($"+{newLines[i]}");
            }

            int afterLinesCount = Math.Min(oldLines.Length - (endCommonOld + 1), 3);
            for (int i = 0; i < afterLinesCount; i++)
            {
                sb.AppendLine($" {oldLines[endCommonOld + 1 + i]}");
            }

            return sb.ToString();
        }

        private static void AssertExactDiffEquals(string expected, string actual, string scenario)
        {
            Assert(string.Equals(expected, actual, StringComparison.Ordinal),
                $"Diff mismatch in scenario '{scenario}'.\nExpected ({expected.Length} chars):\n{expected}\nActual ({actual.Length} chars):\n{actual}");
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

        static void Test_PatchGenerator_ProjectRelativePaths_ResolvesViaProjectPathAndSearchRoot()
        {
            string originalCwd = Directory.GetCurrentDirectory();
            string tempRoot = Path.Combine(Path.GetTempPath(), $"patch_proj_rel_{Guid.NewGuid():N}");
            string outsideCwd = Path.Combine(Path.GetTempPath(), $"patch_outside_cwd_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempRoot);
            Directory.CreateDirectory(outsideCwd);

            try
            {
                Directory.CreateDirectory(Path.Combine(tempRoot, ".git"));
                string contractsDir = Path.Combine(tempRoot, "Contracts");
                string projectDir = Path.Combine(tempRoot, "CollationModifier");
                string servicesDir = Path.Combine(projectDir, "Services");
                Directory.CreateDirectory(contractsDir);
                Directory.CreateDirectory(servicesDir);

                string csprojPath = Path.Combine(projectDir, "CollationModifier.csproj");
                File.WriteAllText(csprojPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

                // Place interface in sibling Contracts/ folder so FindRepoRoot(targetFilePath) is required when projectRoot is unset
                File.WriteAllText(Path.Combine(contractsDir, "IRemoteCatalog.cs"), @"
public interface IRemoteCatalog
{
    Task<IReadOnlyList<string>> FetchEntriesAsync(string filter, CancellationToken cancellationToken = default);
}
");

                string implPath = Path.Combine(servicesDir, "RemoteCatalog.cs");
                File.WriteAllText(implPath, @"
public class RemoteCatalog : IRemoteCatalog
{
}
");

                Directory.SetCurrentDirectory(outsideCwd);
                var parser = new MSBuildLogParser();

                // 1. Rooted ProjectPath with unset projectRoot (CWD outside repo)
                string rootedProjLog = $"Services/RemoteCatalog.cs(2,14): error CS0535: 'RemoteCatalog' does not implement interface member 'IRemoteCatalog.FetchEntriesAsync(string, CancellationToken)' [{csprojPath}]";
                var rootedProjDiags = parser.ParseText(rootedProjLog);
                Assert(rootedProjDiags.Count == 1, "Expected 1 parsed diagnostic for rooted ProjectPath log");

                var generator = new PatchGenerator(new InterfaceDefinitionResolver());
                var patchesUnsetRoot = generator.GeneratePatches(rootedProjDiags);
                Assert(patchesUnsetRoot.Count == 1, $"Expected 1 patch when projectRoot is unset, got {patchesUnsetRoot.Count}");
                Assert(Path.GetFullPath(patchesUnsetRoot[0].TargetFilePath) == Path.GetFullPath(implPath),
                    $"Expected TargetFilePath '{implPath}', got '{patchesUnsetRoot[0].TargetFilePath}'");
                Assert(patchesUnsetRoot[0].UnifiedDiff.Contains("Task<IReadOnlyList<string>> FetchEntriesAsync(string filter, CancellationToken cancellationToken = default)"),
                    $"Expected interface signature from repo root Contracts/, got:\n{patchesUnsetRoot[0].UnifiedDiff}");
                Assert(patchesUnsetRoot[0].UnifiedDiff.Contains("--- a/CollationModifier/Services/RemoteCatalog.cs"),
                    $"Expected repo-relative diff header, got:\n{patchesUnsetRoot[0].UnifiedDiff}");

                // 2. Relative ProjectPath resolved against explicit root
                string relProjLog = "Services/RemoteCatalog.cs(2,14): error CS0535: 'RemoteCatalog' does not implement interface member 'IRemoteCatalog.FetchEntriesAsync(string, CancellationToken)' [CollationModifier/CollationModifier.csproj]";
                var relProjDiags = parser.ParseText(relProjLog);
                var patchesRelProj = new PatchGenerator(new InterfaceDefinitionResolver()).GeneratePatches(relProjDiags, tempRoot);
                Assert(patchesRelProj.Count == 1, $"Expected 1 patch for relative ProjectPath, got {patchesRelProj.Count}");
                Assert(Path.GetFullPath(patchesRelProj[0].TargetFilePath) == Path.GetFullPath(implPath),
                    $"Expected TargetFilePath '{implPath}', got '{patchesRelProj[0].TargetFilePath}'");

                // 3. Unset projectRoot without .git directory falls back to project directory for interface lookup
                Directory.Delete(Path.Combine(tempRoot, ".git"), true);
                string abstractionsDir = Path.Combine(projectDir, "Abstractions");
                string nestedImplDir = Path.Combine(projectDir, "Impl", "Nested");
                Directory.CreateDirectory(abstractionsDir);
                Directory.CreateDirectory(nestedImplDir);

                File.WriteAllText(Path.Combine(abstractionsDir, "ILocalRule.cs"), @"
public interface ILocalRule
{
    bool ValidateRule(string inputCode);
}
");
                string nestedImplPath = Path.Combine(nestedImplDir, "LocalRule.cs");
                File.WriteAllText(nestedImplPath, @"
public class LocalRule : ILocalRule
{
}
");
                string noGitLog = $"Impl/Nested/LocalRule.cs(2,14): error CS0535: 'LocalRule' does not implement interface member 'ILocalRule.ValidateRule(string)' [{csprojPath}]";
                var noGitDiags = parser.ParseText(noGitLog);
                var noGitPatches = new PatchGenerator(new InterfaceDefinitionResolver()).GeneratePatches(noGitDiags);
                Assert(noGitPatches.Count == 1, $"Expected 1 patch using project directory search root without .git, got {noGitPatches.Count}");
                Assert(noGitPatches[0].UnifiedDiff.Contains("public virtual bool ValidateRule(string inputCode)"),
                    $"Expected bool ValidateRule(string inputCode) resolved from project directory, got:\n{noGitPatches[0].UnifiedDiff}");
            }
            finally
            {
                Directory.SetCurrentDirectory(originalCwd);
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
                if (Directory.Exists(outsideCwd)) Directory.Delete(outsideCwd, true);
            }
        }

        static void Test_PatchGenerator_MultiProjectLogs_ResolvesDistinctFilesWithSameRelativePath()
        {
            string solutionRoot = Path.Combine(Path.GetTempPath(), $"patch_multiproj_{Guid.NewGuid():N}");
            Directory.CreateDirectory(solutionRoot);

            try
            {
                string projADir = Path.Combine(solutionRoot, "ProjectAlpha", "Services");
                string projBDir = Path.Combine(solutionRoot, "ProjectBeta", "Services");
                Directory.CreateDirectory(projADir);
                Directory.CreateDirectory(projBDir);

                File.WriteAllText(Path.Combine(projADir, "ISyncAgent.cs"), @"
public interface ISyncAgent
{
    Task<int> RunBatchAsync(string batchId);
}
");
                string workerA = Path.Combine(projADir, "SyncAgent.cs");
                File.WriteAllText(workerA, @"
public class SyncAgent : ISyncAgent
{
}
");

                File.WriteAllText(Path.Combine(projBDir, "ISyncAgent.cs"), @"
public interface ISyncAgent
{
    Task<bool> VerifyChecksumAsync(string batchId);
}
");
                string workerB = Path.Combine(projBDir, "SyncAgent.cs");
                File.WriteAllText(workerB, @"
public class SyncAgent : ISyncAgent
{
}
");

                string multiProjLog = @"
Services/SyncAgent.cs(2,14): error CS0535: 'SyncAgent' does not implement interface member 'ISyncAgent.RunBatchAsync(string)' [ProjectAlpha/ProjectAlpha.csproj]
Services/SyncAgent.cs(2,14): error CS0535: 'SyncAgent' does not implement interface member 'ISyncAgent.VerifyChecksumAsync(string)' [ProjectBeta/ProjectBeta.csproj]
";

                var diagnostics = new MSBuildLogParser().ParseText(multiProjLog);
                Assert(diagnostics.Count == 2, $"Expected 2 diagnostics, got {diagnostics.Count}");

                var generator = new PatchGenerator(new InterfaceDefinitionResolver());
                var patches = generator.GeneratePatches(diagnostics, solutionRoot);
                Assert(patches.Count == 2, $"Expected 2 separate patches for ProjectAlpha and ProjectBeta, got {patches.Count}");

                var patchA = patches.FirstOrDefault(p => Path.GetFullPath(p.TargetFilePath) == Path.GetFullPath(workerA));
                var patchB = patches.FirstOrDefault(p => Path.GetFullPath(p.TargetFilePath) == Path.GetFullPath(workerB));
                Assert(patchA != null, "Expected patch targeting ProjectAlpha/Services/SyncAgent.cs");
                Assert(patchB != null, "Expected patch targeting ProjectBeta/Services/SyncAgent.cs");
                Assert(patchA!.UnifiedDiff.Contains("Task<int> RunBatchAsync(string batchId)"), $"Expected ProjectAlpha method signature, got:\n{patchA.UnifiedDiff}");
                Assert(patchB!.UnifiedDiff.Contains("Task<bool> VerifyChecksumAsync(string batchId)"), $"Expected ProjectBeta method signature, got:\n{patchB.UnifiedDiff}");
            }
            finally
            {
                if (Directory.Exists(solutionRoot)) Directory.Delete(solutionRoot, true);
            }
        }

        static void Test_PatchGenerator_RootRelativeFallback_WhenProjectPathEmptyOrCandidateMissing()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), $"patch_root_fallback_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempRoot);

            try
            {
                string subServicesDir = Path.Combine(tempRoot, "SubModule", "Services");
                Directory.CreateDirectory(subServicesDir);

                File.WriteAllText(Path.Combine(subServicesDir, "IAuditSink.cs"), @"
public interface IAuditSink
{
    void WriteEntry(string category);
    void FlushEntries();
}
");
                string targetFile = Path.Combine(subServicesDir, "AuditSink.cs");
                File.WriteAllText(targetFile, @"
public class AuditSink : IAuditSink
{
}
");

                // 1. Empty ProjectPath falls back to root
                var emptyProjDiags = new List<CompilerDiagnostic>
                {
                    new CompilerDiagnostic
                    {
                        Code = "CS0535",
                        Severity = DiagnosticSeverity.Error,
                        FilePath = "SubModule/Services/AuditSink.cs",
                        ProjectPath = string.Empty,
                        LineNumber = 2,
                        ColumnNumber = 14,
                        Message = "'AuditSink' does not implement interface member 'IAuditSink.WriteEntry(string)'"
                    }
                };

                var generator = new PatchGenerator(new InterfaceDefinitionResolver());
                var patchesEmptyProj = generator.GeneratePatches(emptyProjDiags, tempRoot);
                Assert(patchesEmptyProj.Count == 1, $"Expected 1 patch when ProjectPath is empty, got {patchesEmptyProj.Count}");
                Assert(Path.GetFullPath(patchesEmptyProj[0].TargetFilePath) == Path.GetFullPath(targetFile),
                    $"Expected fallback to root file '{targetFile}', got '{patchesEmptyProj[0].TargetFilePath}'");

                // 2. Non-empty ProjectPath where FilePath is already root-relative (SubModule/SubModule/Services/AuditSink.cs missing -> falls back to root)
                var rootRelWithProjDiags = new List<CompilerDiagnostic>
                {
                    new CompilerDiagnostic
                    {
                        Code = "CS0535",
                        Severity = DiagnosticSeverity.Error,
                        FilePath = "SubModule/Services/AuditSink.cs",
                        ProjectPath = "SubModule/SubModule.csproj",
                        LineNumber = 2,
                        ColumnNumber = 14,
                        Message = "'AuditSink' does not implement interface member 'IAuditSink.FlushEntries()'"
                    }
                };

                var patchesFallback = generator.GeneratePatches(rootRelWithProjDiags, tempRoot);
                Assert(patchesFallback.Count == 1, $"Expected 1 patch when project candidate is missing and root candidate exists, got {patchesFallback.Count}");
                Assert(patchesFallback[0].UnifiedDiff.Contains("public virtual void FlushEntries()"),
                    $"Expected FlushEntries stub in diff, got:\n{patchesFallback[0].UnifiedDiff}");
            }
            finally
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
            }
        }

        static void Test_Launcher_OutsideRootAndFlagOverrides_PreservesRepoRootHelpAndExplicitFlags()
        {
            string originalCwd = Directory.GetCurrentDirectory();
            string outsideCwd = Path.Combine(Path.GetTempPath(), $"launcher_outside_{Guid.NewGuid():N}");
            string customProjectRoot = Path.Combine(Path.GetTempPath(), $"launcher_custom_proj_{Guid.NewGuid():N}");
            Directory.CreateDirectory(outsideCwd);
            Directory.CreateDirectory(customProjectRoot);

            try
            {
                string launcherPath = FindLauncherScriptPath();
                string cmdLauncherPath = FindLauncherCmdPath();
                string repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(launcherPath)!, ".."));

                string bashContent = File.ReadAllText(launcherPath);
                Assert(bashContent.Contains("--project-root \"$REPO_ROOT\""), "bin/msbuild-patcher must forward --project-root \"$REPO_ROOT\"");

                string cmdContent = File.ReadAllText(cmdLauncherPath);
                Assert(cmdContent.Contains("setlocal"), "bin/msbuild-patcher.cmd must use setlocal");
                Assert(cmdContent.Contains("--project-root \"%REPO_ROOT%\""), "bin/msbuild-patcher.cmd must forward --project-root \"%REPO_ROOT%\"");

                Directory.SetCurrentDirectory(outsideCwd);

                // 1. Program.Main detects -h/--help after wrapper --project-root flag without creating output dir
                int helpShortExit = MSBuildDiagnosticPatcher.Program.Main(new[] { "--project-root", repoRoot, "-h" });
                int helpLongExit = MSBuildDiagnosticPatcher.Program.Main(new[] { "--project-root", repoRoot, "--help" });
                Assert(helpShortExit == 0, $"Expected exit code 0 for -h after --project-root, got {helpShortExit}");
                Assert(helpLongExit == 0, $"Expected exit code 0 for --help after --project-root, got {helpLongExit}");
                Assert(!Directory.Exists(Path.Combine(outsideCwd, "build-diagnostics")),
                    "Help invocation must not create build-diagnostics directory");

                // 2. Execute launcher process from outside REPO_ROOT
                (int ExitCode, string Output) RunLauncher(params string[] launcherArgs)
                {
                    var psi = new ProcessStartInfo
                    {
                        WorkingDirectory = outsideCwd,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    if (OperatingSystem.IsWindows())
                    {
                        psi.FileName = "cmd.exe";
                        string quotedArgs = string.Join(" ", launcherArgs.Select(a => $"\"{a}\""));
                        psi.Arguments = launcherArgs.Length == 0
                            ? $"/c \"\"{cmdLauncherPath}\"\""
                            : $"/c \"\"{cmdLauncherPath}\" {quotedArgs}\"";
                    }
                    else
                    {
                        psi.FileName = "bash";
                        psi.ArgumentList.Add(launcherPath);
                        foreach (var arg in launcherArgs)
                        {
                            psi.ArgumentList.Add(arg);
                        }
                    }

                    using var proc = Process.Start(psi)!;
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                    return (proc.ExitCode, stdout + "\n" + stderr);
                }

                // 2a. Empty arguments invocation prints help and returns 0
                var emptyRun = RunLauncher();
                Assert(emptyRun.ExitCode == 0, $"Expected exit code 0 for empty-arg launcher run, got {emptyRun.ExitCode}:\n{emptyRun.Output}");
                Assert(emptyRun.Output.Contains("Usage:"), $"Expected help output for empty-arg launcher run, got:\n{emptyRun.Output}");

                // 2b. -h invocation through launcher prints help and returns 0
                var helpRun = RunLauncher("-h");
                Assert(helpRun.ExitCode == 0, $"Expected exit code 0 for -h launcher run, got {helpRun.ExitCode}:\n{helpRun.Output}");
                Assert(helpRun.Output.Contains("Usage:"), $"Expected help output for -h launcher run, got:\n{helpRun.Output}");

                // 2c. Outside-root invocation resolves repo project-relative CS0535 log via default REPO_ROOT while writing output to CWD
                string repoRelativeLog = Path.Combine(outsideCwd, "repo_rel.log");
                File.WriteAllText(repoRelativeLog,
                    "Services/DatabaseService.cs(11,36): error CS0535: 'DatabaseService' does not implement interface member 'IDatabaseService.AlterCollationAsync(string, string, CancellationToken)' [CollationModifier/CollationModifier.csproj]\n");

                var repoRelRun = RunLauncher(repoRelativeLog);
                Assert(repoRelRun.ExitCode == 1, $"Expected exit code 1 without -a when error is unpatched, got {repoRelRun.ExitCode}:\n{repoRelRun.Output}");

                string generatedPatchPath = Path.Combine(outsideCwd, "build-diagnostics", "DatabaseService.patch");
                Assert(File.Exists(generatedPatchPath),
                    $"Expected patch written under outside CWD at '{generatedPatchPath}'. Output:\n{repoRelRun.Output}");
                string patchContent = File.ReadAllText(generatedPatchPath);
                Assert(patchContent.Contains("AlterCollationAsync(string databaseName, string newCollation, CancellationToken cancellationToken = default)"),
                    $"Expected IDatabaseService.AlterCollationAsync signature resolved from REPO_ROOT, got:\n{patchContent}");

                Directory.Delete(Path.Combine(outsideCwd, "build-diagnostics"), true);

                // 2d. Explicit -p overrides launcher's --project-root default while outputDir still defaults to outsideCwd/build-diagnostics
                File.WriteAllText(Path.Combine(customProjectRoot, "ICustomTask.cs"), @"
public interface ICustomTask
{
    string ExecuteCustom(int retries);
}
");
                string customClassFile = Path.Combine(customProjectRoot, "CustomTask.cs");
                File.WriteAllText(customClassFile, @"
public class CustomTask : ICustomTask
{
}
");
                string customLog = Path.Combine(outsideCwd, "custom_proj.log");
                File.WriteAllText(customLog,
                    "CustomTask.cs(2,14): error CS0535: 'CustomTask' does not implement interface member 'ICustomTask.ExecuteCustom(int)'\n");

                var overrideRun = RunLauncher(customLog, "-p", customProjectRoot, "-a", "-y");
                Assert(overrideRun.ExitCode == 0, $"Expected exit code 0 when explicit -p remedies custom project error, got {overrideRun.ExitCode}:\n{overrideRun.Output}");
                Assert(File.ReadAllText(customClassFile).Contains("public virtual string ExecuteCustom(int retries)"),
                    "Expected CustomTask.cs in customProjectRoot to be patched via -p override");
                Assert(File.Exists(Path.Combine(outsideCwd, "build-diagnostics", "diagnostic-report.json")),
                    "Expected diagnostic-report.json in outsideCwd/build-diagnostics when -o is omitted");
            }
            finally
            {
                Directory.SetCurrentDirectory(originalCwd);
                if (Directory.Exists(outsideCwd)) Directory.Delete(outsideCwd, true);
                if (Directory.Exists(customProjectRoot)) Directory.Delete(customProjectRoot, true);
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

        static void Test_PatchApplicator_PreservesBomAndNativeLineEndings()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"patch_bom_nl_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                var applicator = new PatchApplicator();

                // 1. UTF-8 with BOM + CRLF file, given a stub containing bare LF newlines
                string utf8BomFile = Path.Combine(tempDir, "Utf8BomService.cs");
                string crlfSource = "namespace Demo\r\n{\r\n    public class Utf8BomService : IService\r\n    {\r\n        public void Existing()\r\n        {\r\n        }\r\n    }\r\n}\r\n";
                File.WriteAllText(utf8BomFile, crlfSource, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

                var candidate1 = new PatchCandidate
                {
                    TargetFilePath = utf8BomFile,
                    TargetTypeName = "Utf8BomService",
                    MemberSignature = "public virtual void AddedMethod()",
                    GeneratedCode = "        public virtual void AddedMethod()\n        {\n            throw new NotImplementedException();\n        }"
                };

                Assert(applicator.ApplyPatch(candidate1, requireConfirmation: false), "Expected UTF-8 BOM patch to apply.");
                byte[] utf8BomBytes = File.ReadAllBytes(utf8BomFile);
                Assert(utf8BomBytes.Length >= 3 && utf8BomBytes[0] == 0xEF && utf8BomBytes[1] == 0xBB && utf8BomBytes[2] == 0xBF,
                    "Expected UTF-8 BOM (EF BB BF) to be preserved by PatchApplicator.");
                string utf8BomContent = new UTF8Encoding(true).GetString(utf8BomBytes, 3, utf8BomBytes.Length - 3);
                Assert(utf8BomContent.Contains("AddedMethod()"), "Expected AddedMethod in UTF-8 BOM file.");
                AssertNoBareLf(utf8BomContent, "UTF-8 BOM CRLF file after PatchApplicator");

                // 2. UTF-8 without BOM + LF file, given a stub containing CRLF newlines
                string utf8NoBomFile = Path.Combine(tempDir, "Utf8NoBomService.cs");
                string lfSource = "namespace Demo\n{\n    public class Utf8NoBomService : IService\n    {\n    }\n}\n";
                File.WriteAllText(utf8NoBomFile, lfSource, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                var candidate2 = new PatchCandidate
                {
                    TargetFilePath = utf8NoBomFile,
                    TargetTypeName = "Utf8NoBomService",
                    MemberSignature = "public virtual void AddedLfMethod()",
                    GeneratedCode = "        public virtual void AddedLfMethod()\r\n        {\r\n            throw new NotImplementedException();\r\n        }"
                };

                Assert(applicator.ApplyPatch(candidate2, requireConfirmation: false), "Expected UTF-8 no-BOM patch to apply.");
                byte[] noBomBytes = File.ReadAllBytes(utf8NoBomFile);
                Assert(!(noBomBytes.Length >= 3 && noBomBytes[0] == 0xEF && noBomBytes[1] == 0xBB && noBomBytes[2] == 0xBF),
                    "Expected UTF-8 file without BOM to remain without BOM.");
                string lfContent = Encoding.UTF8.GetString(noBomBytes);
                Assert(!lfContent.Contains('\r'), "Expected LF file to contain no '\\r' characters after patching.");
                Assert(lfContent.Contains("AddedLfMethod()"), "Expected AddedLfMethod in LF file.");

                // 3. UTF-16 LE BOM + CRLF file
                string utf16File = Path.Combine(tempDir, "Utf16Service.cs");
                File.WriteAllText(utf16File, crlfSource.Replace("Utf8BomService", "Utf16Service"), new UnicodeEncoding(bigEndian: false, byteOrderMark: true));

                var candidate3 = new PatchCandidate
                {
                    TargetFilePath = utf16File,
                    TargetTypeName = "Utf16Service",
                    MemberSignature = "public virtual void AddedUtf16Method()",
                    GeneratedCode = "        public virtual void AddedUtf16Method()\n        {\n            throw new NotImplementedException();\n        }"
                };

                Assert(applicator.ApplyPatch(candidate3, requireConfirmation: false), "Expected UTF-16 LE BOM patch to apply.");
                byte[] utf16Bytes = File.ReadAllBytes(utf16File);
                Assert(utf16Bytes.Length >= 2 && utf16Bytes[0] == 0xFF && utf16Bytes[1] == 0xFE,
                    "Expected UTF-16 LE BOM (FF FE) to be preserved by PatchApplicator.");
                string utf16Content = new UnicodeEncoding(false, true).GetString(utf16Bytes, 2, utf16Bytes.Length - 2);
                AssertNoBareLf(utf16Content, "UTF-16 LE CRLF file after PatchApplicator");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        static void Test_PatchApplicator_MultiMemberDuplicateGuardFiltersExistingMembers()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"patch_multimember_guard_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                string ifaceFile = Path.Combine(tempDir, "ISyncService.cs");
                File.WriteAllText(ifaceFile, @"
using System;
using System.Threading;
using System.Threading.Tasks;

public interface ISyncService
{
    Task<string> SyncAsync(string dbName, IProgress<int>? progress = null, CancellationToken cancellationToken = default);
    Task ResetAsync(string dbName, CancellationToken cancellationToken = default);
    string CurrentStatus { get; }
}
");

                // Class already implements SyncAsync with `async`, custom body, different parameter names, and System.Threading.CancellationToken
                string classFile = Path.Combine(tempDir, "SyncService.cs");
                File.WriteAllText(classFile, @"
using System;
using System.Threading.Tasks;

public class SyncService : ISyncService
{
    public async Task<string> SyncAsync(
        string database,
        IProgress<int> reporter,
        System.Threading.CancellationToken token = default)
    {
        await Task.Yield();
        return database;
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
                        LineNumber = 5,
                        ColumnNumber = 14,
                        Message = "'SyncService' does not implement interface member 'ISyncService.SyncAsync(string, IProgress<int>, CancellationToken)'"
                    },
                    new CompilerDiagnostic
                    {
                        Code = "CS0535",
                        Severity = DiagnosticSeverity.Error,
                        FilePath = classFile,
                        LineNumber = 5,
                        ColumnNumber = 14,
                        Message = "'SyncService' does not implement interface member 'ISyncService.ResetAsync(string, CancellationToken)'"
                    },
                    new CompilerDiagnostic
                    {
                        Code = "CS0535",
                        Severity = DiagnosticSeverity.Error,
                        FilePath = classFile,
                        LineNumber = 5,
                        ColumnNumber = 14,
                        Message = "'SyncService' does not implement interface member 'ISyncService.CurrentStatus'"
                    }
                };

                var generator = new PatchGenerator(new InterfaceDefinitionResolver());
                var patches = generator.GeneratePatches(diagnostics, tempDir);
                Assert(patches.Count == 1, $"Expected 1 combined PatchCandidate, got {patches.Count}");
                Assert(patches[0].Members.Count == 3, $"Expected 3 per-member entries, got {patches[0].Members.Count}");
                Assert(patches[0].MemberSignature.Contains("SyncAsync") && patches[0].MemberSignature.Contains("ResetAsync"),
                    $"Expected MemberSignature to list member signatures, got: {patches[0].MemberSignature}");

                var applicator = new PatchApplicator();
                bool applied = applicator.ApplyPatch(patches[0], requireConfirmation: false);
                Assert(applied && patches[0].IsApplied, "Expected multi-member patch application to succeed.");

                string updated = File.ReadAllText(classFile);
                int syncCount = Regex.Matches(updated, @"\bSyncAsync\s*\(").Count;
                int resetCount = Regex.Matches(updated, @"\bResetAsync\s*\(").Count;
                int propCount = Regex.Matches(updated, @"\bCurrentStatus\b").Count;

                Assert(syncCount == 1, $"Expected SyncAsync NOT to be duplicated (preventing CS0111), found {syncCount} declarations:\n{updated}");
                Assert(updated.Contains("await Task.Yield();"), "Expected existing async custom body of SyncAsync to remain intact.");
                Assert(resetCount == 1, $"Expected missing ResetAsync to be inserted once, found {resetCount}:\n{updated}");
                Assert(propCount == 1, $"Expected missing CurrentStatus property to be inserted once, found {propCount}:\n{updated}");

                // Re-applying the same multi-member candidate must be idempotent
                string snapshotAfterFirstApply = updated;
                bool appliedAgain = applicator.ApplyPatch(patches[0], requireConfirmation: false);
                Assert(appliedAgain, "Expected second ApplyPatch call to return true.");
                Assert(File.ReadAllText(classFile) == snapshotAfterFirstApply, "Expected second ApplyPatch call not to modify file.");

                // Fallback verification: manually constructed multi-member PatchCandidate (Members left empty)
                var manualCandidate = new PatchCandidate
                {
                    TargetFilePath = classFile,
                    TargetTypeName = "SyncService",
                    GeneratedCode =
                        "        public virtual Task ResetAsync(string dbName, System.Threading.CancellationToken cancellationToken = default)\n        {\n            throw new NotImplementedException();\n        }\n\n" +
                        "        public virtual void ExtraAction(int retries)\n        {\n            throw new NotImplementedException();\n        }"
                };
                Assert(applicator.ApplyPatch(manualCandidate, requireConfirmation: false), "Expected manual multi-member fallback patch to apply.");
                string afterManual = File.ReadAllText(classFile);
                Assert(Regex.Matches(afterManual, @"\bResetAsync\s*\(").Count == 1, "Manual fallback must not duplicate ResetAsync.");
                Assert(Regex.Matches(afterManual, @"\bExtraAction\s*\(").Count == 1, "Manual fallback must insert missing ExtraAction.");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        private static void RunGitCommand(string workingDirectory, string arguments)
        {
            var psi = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            Assert(proc != null, $"Failed to start git {arguments}");
            string stdout = proc!.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            Assert(proc.ExitCode == 0, $"git {arguments} failed (exit {proc.ExitCode}):\nSTDOUT: {stdout}\nSTDERR: {stderr}");
        }

        private static void AssertNoBareLf(string text, string context)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n' && (i == 0 || text[i - 1] != '\r'))
                {
                    throw new Exception($"Assertion Failed: Found bare LF at index {i} in {context}.");
                }
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

        static void Test_ReportGenerator_PrintConsoleSummary_DisambiguatesFailuresAndAvoidsParserMatches()
        {
            var report = new DiagnosticReport
            {
                SourceLogPath = "/tmp/build-and-test.log",
                ParseDuration = TimeSpan.FromMilliseconds(12.3),
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
                        TestMethod = string.Empty,
                        AssertionFailureMessage = "Assertion Failed: Enqueue took too long"
                    },
                    new TestFailure
                    {
                        TestClass = "GreeterTests",
                        TestMethod = "TestGreeting",
                        AssertionFailureMessage = "Expected 'Hello' but was null"
                    }
                },
                CandidatePatches = new List<PatchCandidate>
                {
                    new PatchCandidate
                    {
                        PatchFileName = "DatabaseService.patch",
                        TargetFilePath = "Services/DatabaseService.cs"
                    }
                }
            };

            TextWriter originalOut = Console.Out;
            TextWriter originalError = Console.Error;
            using var summaryWriter = new StringWriter();
            string summaryOutput;
            try
            {
                Console.SetOut(summaryWriter);
                var reporter = new ReportGenerator();
                reporter.PrintConsoleSummary(report);
                summaryOutput = summaryWriter.ToString();
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }

            Assert(summaryOutput.Contains("  [TEST-FAILURE] MainViewModelTestsRunner: Assertion Failed: Enqueue took too long"), "Expected class-level failure with [TEST-FAILURE] prefix");
            Assert(summaryOutput.Contains("  [TEST-FAILURE] GreeterTests.TestGreeting: Expected 'Hello' but was null"), "Expected method-level failure with [TEST-FAILURE] prefix");
            Assert(!summaryOutput.Contains("[FAIL]"), "Expected PrintConsoleSummary output not to contain [FAIL]");

            var testParser = new TestExecutionLogParser();
            var parsedFailures = testParser.ParseText(summaryOutput);
            Assert(parsedFailures.Count == 0, $"Expected 0 TestExecutionLogParser matches from PrintConsoleSummary output, got {parsedFailures.Count}");

            var msbuildParser = new MSBuildLogParser();
            var parsedDiagnostics = msbuildParser.ParseText(summaryOutput);
            Assert(parsedDiagnostics.Count == 0, $"Expected 0 MSBuildLogParser matches from PrintConsoleSummary output, got {parsedDiagnostics.Count}");
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

        private static int InvokePatcherMain(string[] args, out string stdout, out string stderr)
        {
            TextWriter originalOut = Console.Out;
            TextWriter originalError = Console.Error;
            using var outWriter = new StringWriter();
            using var errorWriter = new StringWriter();
            try
            {
                Console.SetOut(outWriter);
                Console.SetError(errorWriter);
                int exitCode = MSBuildDiagnosticPatcher.Program.Main(args);
                stdout = outWriter.ToString();
                stderr = errorWriter.ToString();
                return exitCode;
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }

        private static int InvokePatcherMain(string[] args) => InvokePatcherMain(args, out _, out _);

        static void Test_Program_Main_ReturnsExitCode0_ForCleanBuildLog()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"main_test_clean_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                string logFile = Path.Combine(tempDir, "clean.log");
                File.WriteAllText(logFile, "Build succeeded.\n    0 Warning(s)\n    0 Error(s)\n\nTime Elapsed 00:00:01.23\n");
                string outputDir = Path.Combine(tempDir, "out");

                int exitCode = InvokePatcherMain(new[] { logFile, "-o", outputDir });
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
                int exitCode = InvokePatcherMain(new[] { logFile, "-o", outputDir });
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
                int exitCode = InvokePatcherMain(new[] { logFile, "-o", outputDir });
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

                int exitCode = InvokePatcherMain(new[] { logFile, "-o", outputDir, "-p", tempDir });
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
                int exitCode = InvokePatcherMain(new[] { logFile, "-o", outputDir });
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

                int exitCode = InvokePatcherMain(new[] { logFile, "-o", outputDir });
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
                int exitCode = InvokePatcherMain(new[] { logFile, "-o", outputDir });
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
                int exitCode = InvokePatcherMain(new[] { logFile, "-o", outputDir });
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
                string projectFile = Path.Combine(tempDir, "Greeter.csproj");
                File.WriteAllText(logFile, $@"Microsoft (R) Build Engine version 17.8.3 for .NET
  Determining projects to restore...
  All projects are up-to-date for restore.
  {classFile}(1,1): warning CS0168: The variable 'unused' is declared but never used [{projectFile}]
  {classFile}(2,14): error CS0535: 'Greeter' does not implement interface member 'IGreeter.Greet(string)' [{projectFile}]

Build FAILED.

    {classFile}(1,1): warning CS0168: The variable 'unused' is declared but never used [{projectFile}]
    {"\u001b[31;1m"}{classFile}(2,14): error CS0535: 'Greeter' does not implement interface member 'IGreeter.Greet(string)' [{projectFile}]{"\u001b[0m"}
    1 Warning(s)
    1 Error(s)

Time Elapsed 00:00:01.42
");
                string outputDir = Path.Combine(tempDir, "out");

                int exitCode = MSBuildDiagnosticPatcher.Program.Main(new[] { logFile, "-o", outputDir, "-p", tempDir, "-a", "-y" });
                Assert(exitCode == 0, $"Expected exit code 0 when patches remediate all diagnostics in multi-section MSBuild log, got {exitCode}");

                string patchedContent = File.ReadAllText(classFile);
                Assert(patchedContent.Contains("public virtual string Greet(string name)"), "Expected class to be patched with Greet method");

                string jsonReportPath = Path.Combine(outputDir, "diagnostic-report.json");
                Assert(File.Exists(jsonReportPath), "Expected diagnostic-report.json to exist");
                string jsonContent = File.ReadAllText(jsonReportPath);
                Assert(jsonContent.Contains("\"IsApplied\": true"), "Expected persisted report to record IsApplied: true");
                Assert(jsonContent.Contains("\"ErrorCount\": 1"), "Expected persisted report to record deduplicated ErrorCount: 1");
                Assert(jsonContent.Contains("\"WarningCount\": 1"), "Expected persisted report to record deduplicated WarningCount: 1");
                Assert(jsonContent.Contains("\"UnresolvedErrorCount\": 0"), "Expected persisted report to record UnresolvedErrorCount: 0");

                string mdReportPath = Path.Combine(outputDir, "diagnostic-report.md");
                Assert(File.Exists(mdReportPath), "Expected diagnostic-report.md to exist");
                string mdContent = File.ReadAllText(mdReportPath);
                Assert(mdContent.Contains("- **Roslyn Errors**: `1`"), "Expected Markdown report to record 1 Roslyn error");
                Assert(mdContent.Contains("- **Roslyn Warnings**: `1`"), "Expected Markdown report to record 1 Roslyn warning");
                int greetRows = mdContent.Split(new[] { "IGreeter.Greet(string)" }, StringSplitOptions.None).Length - 1;
                Assert(greetRows == 1, $"Expected 1 diagnostic row for IGreeter.Greet(string) in Markdown report, got {greetRows}");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        static void Test_Program_Main_ReturnsOne_WhenInputFileNotFound()
        {
            string missingPath = Path.Combine(Path.GetTempPath(), $"missing_log_{Guid.NewGuid():N}.log");
            int exitCode = InvokePatcherMain(new[] { missingPath });
            Assert(exitCode == 1, $"Expected exit code 1 when input file is not found, got {exitCode}");
        }

        static void Test_InvokePatcherMain_CapturesOutputAndRestoresConsoleStreams()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"invoke_capture_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            TextWriter savedOut = Console.Out;
            TextWriter savedError = Console.Error;
            using var outerOut = new StringWriter();
            using var outerError = new StringWriter();
            try
            {
                string logFile = Path.Combine(tempDir, "test_failure.log");
                string outputDir = Path.Combine(tempDir, "out");
                File.WriteAllText(logFile, "  [FAIL] SampleSuite.SampleTest - Expected 1 but got 2\n");

                Console.SetOut(outerOut);
                Console.SetError(outerError);

                int exitCode = InvokePatcherMain(new[] { logFile, "-o", outputDir }, out string capturedOut, out string capturedError);
                Assert(exitCode == 1, $"Expected exit code 1, got {exitCode}");
                Assert(capturedOut.Contains("[TEST-FAILURE] SampleSuite.SampleTest"), "Expected captured stdout to include disambiguated failure summary");
                Assert(string.IsNullOrEmpty(capturedError), $"Expected empty captured stderr, got '{capturedError}'");
                Assert(string.IsNullOrEmpty(outerOut.ToString()), $"Expected outer Console.Out to remain empty during InvokePatcherMain, got '{outerOut}'");
                Assert(string.IsNullOrEmpty(outerError.ToString()), $"Expected outer Console.Error to remain empty during InvokePatcherMain, got '{outerError}'");

                // Verify post-call writes reach the restored writers (Console.SetOut wraps writers in a synchronized TextWriter)
                Console.Out.WriteLine("post-call-stdout-marker");
                Console.Error.WriteLine("post-call-stderr-marker");

                Assert(outerOut.ToString().Contains("post-call-stdout-marker"), "Expected Console.Out to write to restored outer stdout writer");
                Assert(outerError.ToString().Contains("post-call-stderr-marker"), "Expected Console.Error to write to restored outer stderr writer");
            }
            finally
            {
                Console.SetOut(savedOut);
                Console.SetError(savedError);
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        static void Test_InvokePatcherMain_RestoresConsoleStreams_WhenExceptionThrown()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"invoke_throw_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            TextWriter savedOut = Console.Out;
            TextWriter savedError = Console.Error;
            using var outerOut = new StringWriter();
            using var outerError = new StringWriter();
            try
            {
                string logFile = Path.Combine(tempDir, "clean.log");
                File.WriteAllText(logFile, "Build succeeded.\n");

                Console.SetOut(outerOut);
                Console.SetError(outerError);

                bool threw = false;
                try
                {
                    // Passing an empty outputDir causes Program.Main to print to Console.Out and then throw ArgumentException in Directory.CreateDirectory("")
                    InvokePatcherMain(new[] { logFile, "-o", string.Empty });
                }
                catch (ArgumentException)
                {
                    threw = true;
                }

                Assert(threw, "Expected InvokePatcherMain to propagate ArgumentException from empty outputDir");
                Assert(string.IsNullOrEmpty(outerOut.ToString()), $"Expected pre-exception output not to leak to outer Console.Out, got '{outerOut}'");
                Assert(string.IsNullOrEmpty(outerError.ToString()), $"Expected outer Console.Error to remain empty during throwing call, got '{outerError}'");

                Console.Out.WriteLine("restored-after-exception-stdout");
                Console.Error.WriteLine("restored-after-exception-stderr");

                Assert(outerOut.ToString().Contains("restored-after-exception-stdout"), "Expected Console.Out to be restored after exception");
                Assert(outerError.ToString().Contains("restored-after-exception-stderr"), "Expected Console.Error to be restored after exception");
            }
            finally
            {
                Console.SetOut(savedOut);
                Console.SetError(savedError);
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        // 9. CI Workflow Configuration Tests
        static void Test_Workflow_PostMergeBuildVerification_HasDiagnosticReportingAndArtifactUpload()
        {
            string workflowPath = FindWorkflowPath();
            string content = File.ReadAllText(workflowPath);

            // Requirement 1: Redirect solution restore, build, and test output to /tmp/build-and-test.log in order
            string restoreCmd = "dotnet restore SQLCollationChangeApp.sln 2>&1 | tee /tmp/build-and-test.log";
            string buildCmd = "dotnet build SQLCollationChangeApp.sln --no-restore -p:UseSharedCompilation=false 2>&1 | tee -a /tmp/build-and-test.log";
            string testCmd = "dotnet test SQLCollationChangeApp.sln --no-build -p:UseSharedCompilation=false 2>&1 | tee -a /tmp/build-and-test.log";

            Assert(content.Contains(restoreCmd), "Workflow must redirect solution restore output to /tmp/build-and-test.log");
            Assert(content.Contains(buildCmd), "Workflow must append solution build output to /tmp/build-and-test.log");
            Assert(content.Contains(testCmd), "Workflow must append solution test output to /tmp/build-and-test.log");

            int restoreIndex = content.IndexOf(restoreCmd, StringComparison.Ordinal);
            int buildIndex = content.IndexOf(buildCmd, StringComparison.Ordinal);
            int testIndex = content.IndexOf(testCmd, StringComparison.Ordinal);
            Assert(restoreIndex != -1 && buildIndex != -1 && testIndex != -1 && restoreIndex < buildIndex && buildIndex < testIndex, "Workflow must run solution restore, build, and test steps in order");

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

        static void Test_Workflow_PostMergeBuildVerification_VerifiesWindowsTargetAndMultiOsMatrix()
        {
            string workflowPath = FindWorkflowPath();
            string content = File.ReadAllText(workflowPath);

            Assert(content.Contains("ubuntu-latest"), "Workflow matrix must include ubuntu-latest");
            Assert(content.Contains("windows-latest"), "Workflow matrix must include windows-latest");
            Assert(content.Contains("runs-on: ${{ matrix.os }}"), "Workflow must run across ${{ matrix.os }}");
            Assert(content.Contains("shell: bash"), "Workflow must set shell: bash so pipefail propagates non-zero exit codes across Linux and Windows");
            Assert(content.Contains("dotnet build CollationModifier/CollationModifier.csproj -f net8.0-windows -p:UseSharedCompilation=false 2>&1 | tee -a /tmp/build-and-test.log"),
                "Workflow must explicitly verify net8.0-windows compilation and redirect output to /tmp/build-and-test.log");

            int patcherBuildIndex = content.IndexOf("dotnet build MSBuildDiagnosticPatcher/MSBuildDiagnosticPatcher.csproj", StringComparison.Ordinal);
            int windowsBuildIndex = content.IndexOf("dotnet build CollationModifier/CollationModifier.csproj -f net8.0-windows", StringComparison.Ordinal);
            Assert(patcherBuildIndex != -1 && windowsBuildIndex != -1 && patcherBuildIndex < windowsBuildIndex,
                "Workflow must build MSBuildDiagnosticPatcher before net8.0-windows verification step");
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
