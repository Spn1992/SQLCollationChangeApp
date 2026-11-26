using System;
using CollationModifier.Models;

namespace CollationModifier.Exceptions
{
    public class PreflightValidationException : Exception
    {
        public PreflightReport Report { get; }

        public string? DiagnosticSummary { get; }

        public PreflightValidationException(string message, PreflightReport report)
            : this(message, report, diagnosticSummary: null)
        {
        }

        public PreflightValidationException(PreflightReport report)
            : this(
                report != null ? report.GetSingleLineFailureSummary() : "Pre-flight validation failed.",
                report!,
                diagnosticSummary: null)
        {
        }

        internal PreflightValidationException(string message, PreflightReport report, string? diagnosticSummary)
            : base(message)
        {
            Report = report;
            DiagnosticSummary = diagnosticSummary;
        }
    }
}
