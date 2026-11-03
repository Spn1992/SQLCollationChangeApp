using System;

namespace MSBuildDiagnosticPatcher.Models
{
    public enum DiagnosticSeverity
    {
        Info,
        Warning,
        Error
    }

    public class CompilerDiagnostic
    {
        public string Code { get; set; } = string.Empty;
        public DiagnosticSeverity Severity { get; set; } = DiagnosticSeverity.Error;
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public int ColumnNumber { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ProjectPath { get; set; } = string.Empty;
        public string RawLine { get; set; } = string.Empty;

        public override string ToString()
        {
            string loc = LineNumber > 0 ? $"({LineNumber},{ColumnNumber})" : "";
            return $"{FilePath}{loc}: {Severity.ToString().ToLowerInvariant()} {Code}: {Message}";
        }
    }
}
