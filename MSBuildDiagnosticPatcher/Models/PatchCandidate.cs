using System;
using System.Collections.Generic;

namespace MSBuildDiagnosticPatcher.Models
{
    public class PatchMemberEntry
    {
        public string MemberName { get; set; } = string.Empty;
        public string ReturnType { get; set; } = string.Empty;
        public string ParameterList { get; set; } = string.Empty;
        public List<string> ParameterTypes { get; set; } = new();
        public bool IsProperty { get; set; }
        public string MemberSignature { get; set; } = string.Empty;
        public string GeneratedCode { get; set; } = string.Empty;
    }

    public class PatchCandidate
    {
        public string TargetFilePath { get; set; } = string.Empty;
        public string DiagnosticCode { get; set; } = string.Empty;
        public string InterfaceName { get; set; } = string.Empty;
        public string TargetTypeName { get; set; } = string.Empty;
        public string MemberSignature { get; set; } = string.Empty;
        public string GeneratedCode { get; set; } = string.Empty;
        public List<PatchMemberEntry> Members { get; set; } = new();
        public string UnifiedDiff { get; set; } = string.Empty;
        public string PatchFileName { get; set; } = string.Empty;
        public bool IsApplied { get; set; }
        public int ResolvedErrorCount { get; set; }
        public string Description { get; set; } = string.Empty;
    }
}
