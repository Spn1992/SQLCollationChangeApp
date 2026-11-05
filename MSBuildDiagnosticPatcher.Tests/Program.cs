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

            // 2. TestExecutionLogParser Tests
            RunTest("TestExecutionLogParser_ParseCustomRunner_ExtractsFailedClassAndAssertion", Test_TestExecutionLogParser_ParseCustomRunner);
            RunTest("TestExecutionLogParser_ParseDotnetTest_ExtractsClassMethodAndError", Test_TestExecutionLogParser_ParseDotnetTest);
            RunTest("TestExecutionLogParser_ParseBracketFail_ExtractsFailure", Test_TestExecutionLogParser_ParseBracketFail);

            // 3. InterfaceDefinitionResolver Tests
            RunTest("InterfaceDefinitionResolver_ParseCs0535Message_ExtractsComponents", Test_InterfaceDefinitionResolver_ParseCs0535Message);
            RunTest("InterfaceDefinitionResolver_ExtractMemberFromInterfaceSource_FindsMethod", Test_InterfaceDefinitionResolver_ExtractMemberFromInterfaceSource);
            RunTest("InterfaceDefinitionResolver_ExtractMemberFromInterfaceSource_FindsProperty", Test_InterfaceDefinitionResolver_ExtractProperty);

            // 4. PatchGenerator Tests
            RunTest("PatchGenerator_FindClassInsertionPoint_FindsClassClosingBrace", Test_PatchGenerator_FindClassInsertionPoint);
            RunTest("PatchGenerator_GenerateMemberStub_DelegatesToExistingOverload", Test_PatchGenerator_GenerateMemberStub_Delegates);
            RunTest("PatchGenerator_GenerateMemberStub_GeneratesNotImplementedException", Test_PatchGenerator_GenerateMemberStub_NotImplemented);
            RunTest("PatchGenerator_GenerateUnifiedDiff_ProducesValidDiffHeader", Test_PatchGenerator_GenerateUnifiedDiff);

            // 5. PatchApplicator Tests
            RunTest("PatchApplicator_ApplyPatch_InsertsCodeWithoutDamagingComments", Test_PatchApplicator_ApplyPatch);
            RunTest("PatchApplicator_ApplyPatch_GuardsAgainstDuplicateApplication", Test_PatchApplicator_GuardsDuplicate);

            // 6. ReportGenerator Tests
            RunTest("ReportGenerator_GenerateMarkdownReport_ProducesFormattedMarkdown", Test_ReportGenerator_Markdown);
            RunTest("ReportGenerator_GenerateJsonReport_ProducesValidJson", Test_ReportGenerator_Json);

            // 7. End-to-End Diagnostic & Patching Workflow
            RunTest("EndToEnd_ParseLogAndGeneratePatch_RestoresInterfaceImplementation", Test_EndToEnd_Workflow);

            // 8. CI Workflow Configuration Tests
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

        // 8. CI Workflow Configuration Tests
        static void Test_Workflow_PostMergeBuildVerification_HasDiagnosticReportingAndArtifactUpload()
        {
            string workflowPath = FindWorkflowPath();
            string content = File.ReadAllText(workflowPath);

            // Requirement 1: Redirect build and test output to /tmp/build-and-test.log
            Assert(content.Contains("tee /tmp/build-and-test.log"), "Workflow must redirect build output to /tmp/build-and-test.log using tee");
            Assert(content.Contains("tee -a /tmp/build-and-test.log"), "Workflow must append test output to /tmp/build-and-test.log using tee -a");

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
    }
}
