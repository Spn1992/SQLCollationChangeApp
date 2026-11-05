using System;
using System.Diagnostics;
using System.IO;
using MSBuildDiagnosticPatcher.Models;
using MSBuildDiagnosticPatcher.Parsers;
using MSBuildDiagnosticPatcher.Patcher;
using MSBuildDiagnosticPatcher.Reporting;

namespace MSBuildDiagnosticPatcher
{
    public class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] == "-h" || args[0] == "--help")
            {
                PrintHelp();
                return 0;
            }

            string logFilePath = string.Empty;
            string outputDir = Path.Combine(Directory.GetCurrentDirectory(), "build-diagnostics");
            string projectRoot = Directory.GetCurrentDirectory();
            bool applyPatches = false;
            bool autoConfirm = false;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "-o" || arg == "--output-dir")
                {
                    if (i + 1 < args.Length) outputDir = args[++i];
                }
                else if (arg == "-p" || arg == "--project-root")
                {
                    if (i + 1 < args.Length) projectRoot = args[++i];
                }
                else if (arg == "-a" || arg == "--apply")
                {
                    applyPatches = true;
                }
                else if (arg == "-y" || arg == "--yes")
                {
                    autoConfirm = true;
                }
                else if (!arg.StartsWith("-") && string.IsNullOrEmpty(logFilePath))
                {
                    logFilePath = arg;
                }
            }

            if (string.IsNullOrWhiteSpace(logFilePath))
            {
                Console.WriteLine("[ERROR] No input log file specified.");
                PrintHelp();
                return 1;
            }

            if (!File.Exists(logFilePath))
            {
                Console.WriteLine($"[ERROR] Log file not found: {logFilePath}");
                return 1;
            }

            var stopwatch = Stopwatch.StartNew();

            Console.WriteLine($"[INFO] Parsing build & test execution log: {logFilePath}...");
            var msbuildParser = new MSBuildLogParser();
            var testParser = new TestExecutionLogParser();

            string logContent = File.ReadAllText(logFilePath);

            var diagnostics = msbuildParser.ParseText(logContent);
            var testFailures = testParser.ParseText(logContent);

            stopwatch.Stop();

            Console.WriteLine($"[INFO] Parsed log in {stopwatch.ElapsedMilliseconds} ms: {diagnostics.Count} diagnostics, {testFailures.Count} test failures.");

            var patchResolver = new InterfaceDefinitionResolver();
            var patchGenerator = new PatchGenerator(patchResolver);
            var patchCandidates = patchGenerator.GeneratePatches(diagnostics, projectRoot);

            var report = new DiagnosticReport
            {
                SourceLogPath = Path.GetFullPath(logFilePath),
                GeneratedAt = DateTime.UtcNow,
                ParseDuration = stopwatch.Elapsed,
                Diagnostics = diagnostics,
                TestFailures = testFailures,
                CandidatePatches = patchCandidates
            };

            var reporter = new ReportGenerator();
            reporter.PrintConsoleSummary(report);
            reporter.WriteReportFiles(report, outputDir);
            Console.WriteLine($"[INFO] Diagnostic reports and patches written to: {Path.GetFullPath(outputDir)}");

            if (applyPatches && patchCandidates.Count > 0)
            {
                Console.WriteLine("\n[INFO] Applying candidate remediation patches...");
                var applicator = new PatchApplicator();
                foreach (var patch in patchCandidates)
                {
                    applicator.ApplyPatch(patch, requireConfirmation: !autoConfirm);
                }
            }

            // Return non-zero if errors or failures were found
            return (report.ErrorCount > 0 || report.FailureCount > 0) ? 1 : 0;
        }

        private static void PrintHelp()
        {
            Console.WriteLine(@"MSBuild Log Diagnostic Parser and Patch Generator Utility

Usage:
  dotnet run --project MSBuildDiagnosticPatcher -- <log-file> [options]

Arguments:
  <log-file>                     Path to MSBuild stdout log or test runner output file.

Options:
  -o, --output-dir <directory>   Directory to write diagnostic reports and patch files (default: ./build-diagnostics)
  -p, --project-root <directory> Project root directory for resolving interface files (default: current directory)
  -a, --apply                    Apply candidate patches to source files
  -y, --yes                      Automatically confirm patch application without prompting
  -h, --help                     Show command line help
");
        }
    }
}
