using System;
using System.Collections.Generic;
using System.Linq;

namespace MSBuildDiagnosticPatcher.Models
{
    public class DiagnosticReport
    {
        public string SourceLogPath { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public TimeSpan ParseDuration { get; set; }

        public List<CompilerDiagnostic> Diagnostics { get; set; } = new();
        public List<TestFailure> TestFailures { get; set; } = new();
        public List<PatchCandidate> CandidatePatches { get; set; } = new();

        public int ErrorCount => CountBySeverity(DiagnosticSeverity.Error);
        public int WarningCount => CountBySeverity(DiagnosticSeverity.Warning);
        public int FailureCount => TestFailures.Count;
        public int PatchCount => CandidatePatches.Count;
        public int UnresolvedErrorCount => Math.Max(0, ErrorCount - CandidatePatches.Where(p => p.IsApplied).Sum(p => p.ResolvedErrorCount));

        internal (int ErrorCount, int WarningCount) GetSeverityCounts()
        {
            int errorCount = 0;
            int warningCount = 0;
            foreach (var diagnostic in Diagnostics)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    errorCount++;
                }
                else if (diagnostic.Severity == DiagnosticSeverity.Warning)
                {
                    warningCount++;
                }
            }

            return (errorCount, warningCount);
        }

        private int CountBySeverity(DiagnosticSeverity severity)
        {
            int count = 0;
            foreach (var diagnostic in Diagnostics)
            {
                if (diagnostic.Severity == severity)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
