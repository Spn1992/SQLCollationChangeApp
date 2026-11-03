using System;
using System.Collections.Generic;

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

        public int ErrorCount => Diagnostics.FindAll(d => d.Severity == DiagnosticSeverity.Error).Count;
        public int WarningCount => Diagnostics.FindAll(d => d.Severity == DiagnosticSeverity.Warning).Count;
        public int FailureCount => TestFailures.Count;
        public int PatchCount => CandidatePatches.Count;
    }
}
