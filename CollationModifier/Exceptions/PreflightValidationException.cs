using System;
using CollationModifier.Models;

namespace CollationModifier.Exceptions
{
    public class PreflightValidationException : Exception
    {
        public PreflightReport Report { get; }

        public PreflightValidationException(string message, PreflightReport report) : base(message)
        {
            Report = report;
        }

        public PreflightValidationException(PreflightReport report)
            : base($"Pre-flight validation failed for database '{report.DatabaseName}'. Blocking conditions found.\n{report.GetDiagnosticSummary()}")
        {
            Report = report;
        }
    }
}
