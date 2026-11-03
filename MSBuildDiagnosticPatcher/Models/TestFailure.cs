using System;

namespace MSBuildDiagnosticPatcher.Models
{
    public class TestFailure
    {
        public string TestClass { get; set; } = string.Empty;
        public string TestMethod { get; set; } = string.Empty;
        public string AssertionFailureMessage { get; set; } = string.Empty;
        public string StackTrace { get; set; } = string.Empty;
        public string RawLine { get; set; } = string.Empty;

        public override string ToString()
        {
            string name = string.IsNullOrWhiteSpace(TestMethod) ? TestClass : $"{TestClass}.{TestMethod}";
            return $"FAIL: {name} - {AssertionFailureMessage}";
        }
    }
}
