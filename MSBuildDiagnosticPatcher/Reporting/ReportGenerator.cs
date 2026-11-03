using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MSBuildDiagnosticPatcher.Models;

namespace MSBuildDiagnosticPatcher.Reporting
{
    public class ReportGenerator
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public string GenerateMarkdownReport(DiagnosticReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# MSBuild Log Diagnostic Report");
            sb.AppendLine();
            sb.AppendLine($"- **Source Log**: `{report.SourceLogPath}`");
            sb.AppendLine($"- **Generated At**: `{report.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC`");
            sb.AppendLine($"- **Parse Duration**: `{report.ParseDuration.TotalMilliseconds:F1} ms`");
            sb.AppendLine($"- **Roslyn Errors**: `{report.ErrorCount}`");
            sb.AppendLine($"- **Roslyn Warnings**: `{report.WarningCount}`");
            sb.AppendLine($"- **Failed Tests**: `{report.FailureCount}`");
            sb.AppendLine($"- **Candidate Patches**: `{report.PatchCount}`");
            sb.AppendLine();

            // Roslyn Diagnostics Table
            sb.AppendLine("## 1. Compiler Diagnostics");
            sb.AppendLine();
            if (report.Diagnostics.Count == 0)
            {
                sb.AppendLine("No compiler diagnostics detected in the log.");
            }
            else
            {
                sb.AppendLine("| Code | Severity | File | Line:Col | Message |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
                foreach (var diag in report.Diagnostics)
                {
                    string file = Path.GetFileName(diag.FilePath);
                    string loc = diag.LineNumber > 0 ? $"{diag.LineNumber}:{diag.ColumnNumber}" : "-";
                    string cleanMsg = diag.Message.Replace("|", "\\|");
                    sb.AppendLine($"| `{diag.Code}` | `{diag.Severity}` | `{file}` | `{loc}` | {cleanMsg} |");
                }
            }
            sb.AppendLine();

            // Test Failures Section
            sb.AppendLine("## 2. Test Execution Failures");
            sb.AppendLine();
            if (report.TestFailures.Count == 0)
            {
                sb.AppendLine("No test runner failures detected in the log.");
            }
            else
            {
                sb.AppendLine("| Test Suite / Class | Test Method | Failure Message |");
                sb.AppendLine("| :--- | :--- | :--- |");
                foreach (var tf in report.TestFailures)
                {
                    string method = string.IsNullOrWhiteSpace(tf.TestMethod) ? "-" : $"`{tf.TestMethod}`";
                    string cleanMsg = tf.AssertionFailureMessage.Replace("|", "\\|");
                    sb.AppendLine($"| `{tf.TestClass}` | {method} | {cleanMsg} |");
                }

                sb.AppendLine();
                sb.AppendLine("### Failure Details");
                sb.AppendLine();
                foreach (var tf in report.TestFailures)
                {
                    sb.AppendLine($"#### `{tf.TestClass}`{(string.IsNullOrEmpty(tf.TestMethod) ? "" : $".`{tf.TestMethod}`")}");
                    sb.AppendLine($"**Assertion / Error:**");
                    sb.AppendLine("```");
                    sb.AppendLine(tf.AssertionFailureMessage);
                    sb.AppendLine("```");
                    if (!string.IsNullOrWhiteSpace(tf.StackTrace))
                    {
                        sb.AppendLine("**Stack Trace:**");
                        sb.AppendLine("```");
                        sb.AppendLine(tf.StackTrace);
                        sb.AppendLine("```");
                    }
                    sb.AppendLine();
                }
            }
            sb.AppendLine();

            // Candidate Patches Section
            sb.AppendLine("## 3. Candidate Remediation Patches");
            sb.AppendLine();
            if (report.CandidatePatches.Count == 0)
            {
                sb.AppendLine("No automated patches generated for detected diagnostics.");
            }
            else
            {
                foreach (var patch in report.CandidatePatches)
                {
                    sb.AppendLine($"### Patch: `{patch.PatchFileName}`");
                    sb.AppendLine($"- **Target File**: `{patch.TargetFilePath}`");
                    sb.AppendLine($"- **Interface**: `{patch.InterfaceName}`");
                    sb.AppendLine($"- **Target Type**: `{patch.TargetTypeName}`");
                    sb.AppendLine($"- **Diagnostic Code**: `{patch.DiagnosticCode}`");
                    sb.AppendLine($"- **Description**: {patch.Description}");
                    sb.AppendLine();
                    sb.AppendLine("```diff");
                    sb.AppendLine(patch.UnifiedDiff);
                    sb.AppendLine("```");
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        public string GenerateJsonReport(DiagnosticReport report)
        {
            return JsonSerializer.Serialize(report, JsonOptions);
        }

        public void PrintConsoleSummary(DiagnosticReport report)
        {
            Console.WriteLine("=================================================");
            Console.WriteLine(" MSBuild Log Diagnostic & Patch Generator Report");
            Console.WriteLine("=================================================");
            Console.WriteLine($" Source Log:       {report.SourceLogPath}");
            Console.WriteLine($" Parse Duration:   {report.ParseDuration.TotalMilliseconds:F1} ms");
            Console.WriteLine($" Compiler Errors:  {report.ErrorCount}");
            Console.WriteLine($" Compiler Warnings:{report.WarningCount}");
            Console.WriteLine($" Test Failures:    {report.FailureCount}");
            Console.WriteLine($" Patches Created:  {report.PatchCount}");
            Console.WriteLine("-------------------------------------------------");

            if (report.ErrorCount > 0)
            {
                Console.WriteLine("\n[Compiler Errors]");
                foreach (var err in report.Diagnostics.FindAll(d => d.Severity == DiagnosticSeverity.Error))
                {
                    Console.WriteLine($"  [{err.Code}] {err.FilePath}({err.LineNumber},{err.ColumnNumber}): {err.Message}");
                }
            }

            if (report.FailureCount > 0)
            {
                Console.WriteLine("\n[Test Failures]");
                foreach (var tf in report.TestFailures)
                {
                    string name = string.IsNullOrEmpty(tf.TestMethod) ? tf.TestClass : $"{tf.TestClass}.{tf.TestMethod}";
                    Console.WriteLine($"  [FAIL] {name}: {tf.AssertionFailureMessage}");
                }
            }

            if (report.PatchCount > 0)
            {
                Console.WriteLine("\n[Generated Candidate Patches]");
                foreach (var patch in report.CandidatePatches)
                {
                    Console.WriteLine($"  [PATCH] {patch.PatchFileName} -> {patch.TargetFilePath}");
                }
            }
            Console.WriteLine("=================================================");
        }

        public void WriteReportFiles(DiagnosticReport report, string outputDir)
        {
            Directory.CreateDirectory(outputDir);

            // Write markdown report
            string mdPath = Path.Combine(outputDir, "diagnostic-report.md");
            File.WriteAllText(mdPath, GenerateMarkdownReport(report));

            // Write json report
            string jsonPath = Path.Combine(outputDir, "diagnostic-report.json");
            File.WriteAllText(jsonPath, GenerateJsonReport(report));

            // Write each patch file
            foreach (var patch in report.CandidatePatches)
            {
                string patchPath = Path.Combine(outputDir, patch.PatchFileName);
                File.WriteAllText(patchPath, patch.UnifiedDiff);
            }
        }
    }
}
