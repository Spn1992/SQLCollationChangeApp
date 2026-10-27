using System.Collections.Generic;
using System.Text;

namespace CollationModifier.Models
{
    public class ActiveSessionInfo
    {
        public int SessionId { get; set; }
        public string LoginName { get; set; } = string.Empty;
        public string HostName { get; set; } = string.Empty;
        public string ProgramName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public class SchemaDependencyInfo
    {
        public string ObjectName { get; set; } = string.Empty;
        public string ObjectType { get; set; } = string.Empty;
        public string SchemaName { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }

    public class PreflightReport
    {
        public string DatabaseName { get; set; } = string.Empty;
        public List<ActiveSessionInfo> ActiveSessions { get; set; } = new();
        public List<SchemaDependencyInfo> SchemaDependencies { get; set; } = new();
        public List<string> MissingPermissions { get; set; } = new();
        public List<string> ValidationErrors { get; set; } = new();

        public bool HasActiveSessions => ActiveSessions.Count > 0;
        public bool HasSchemaDependencies => SchemaDependencies.Count > 0;
        public bool HasMissingPermissions => MissingPermissions.Count > 0;
        public bool HasValidationErrors => ValidationErrors.Count > 0;

        public bool IsValid => !HasActiveSessions && !HasSchemaDependencies && !HasMissingPermissions && !HasValidationErrors;
        public bool HasBlockingConditions => !IsValid;

        public string GetDiagnosticSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Pre-flight Validation Report for Database '{DatabaseName}':");
            sb.AppendLine($"Status: {(IsValid ? "PASSED" : "FAILED (Blocking items found)")}");

            if (HasActiveSessions)
            {
                sb.AppendLine($"\n[Active User Sessions ({ActiveSessions.Count})]");
                foreach (var s in ActiveSessions)
                {
                    sb.AppendLine($"  - Session ID: {s.SessionId}, User: {s.LoginName}, Host: {s.HostName}, Program: {s.ProgramName}, Status: {s.Status}");
                }
            }

            if (HasSchemaDependencies)
            {
                sb.AppendLine($"\n[Schema Dependencies ({SchemaDependencies.Count})]");
                foreach (var dep in SchemaDependencies)
                {
                    var schemaObj = string.IsNullOrWhiteSpace(dep.SchemaName) ? dep.ObjectName : $"{dep.SchemaName}.{dep.ObjectName}";
                    sb.AppendLine($"  - [{dep.ObjectType}] {schemaObj}: {dep.Details}");
                }
            }

            if (HasMissingPermissions)
            {
                sb.AppendLine($"\n[Missing Permissions ({MissingPermissions.Count})]");
                foreach (var perm in MissingPermissions)
                {
                    sb.AppendLine($"  - {perm}");
                }
            }

            if (HasValidationErrors)
            {
                sb.AppendLine($"\n[Validation Errors ({ValidationErrors.Count})]");
                foreach (var err in ValidationErrors)
                {
                    sb.AppendLine($"  - {err}");
                }
            }

            return sb.ToString();
        }
    }
}
