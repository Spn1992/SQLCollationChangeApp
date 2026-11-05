using System;

namespace MSBuildDiagnosticPatcher.Models
{
    public class PatchCandidate
    {
        public string TargetFilePath { get; set; } = string.Empty;
        public string DiagnosticCode { get; set; } = string.Empty;
        public string InterfaceName { get; set; } = string.Empty;
        public string TargetTypeName { get; set; } = string.Empty;
        public string MemberSignature { get; set; } = string.Empty;
        public string GeneratedCode { get; set; } = string.Empty;
        public string UnifiedDiff { get; set; } = string.Empty;
        public string PatchFileName { get; set; } = string.Empty;
        public bool IsApplied { get; set; }
        public int ResolvedErrorCount { get; set; }
        public string Description { get; set; } = string.Empty;
    }
}
