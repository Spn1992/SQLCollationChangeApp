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

        public string DisplaySummary =>
            $"Session ID: {SessionId}, User: {LoginName}, Host: {HostName}, Program: {ProgramName}, Status: {Status}";

        public override string ToString() => DisplaySummary;
    }

    public class SchemaDependencyInfo
    {
        public string ObjectName { get; set; } = string.Empty;
        public string ObjectType { get; set; } = string.Empty;
        public string SchemaName { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;

        public string DisplaySummary
        {
            get
            {
                var schemaObj = string.IsNullOrWhiteSpace(SchemaName) ? ObjectName : $"{SchemaName}.{ObjectName}";
                return $"[{ObjectType}] {schemaObj}: {Details}";
            }
        }

        public override string ToString() => DisplaySummary;
    }

    public class PreflightReport
    {
        private static readonly IReadOnlyList<string> DefaultPassedChecks = new[]
        {
            "Required database permissions verified (ALTER, BACKUP DATABASE).",
            "No active user sessions blocking SINGLE_USER mode.",
            "No blocking schema dependencies detected.",
            "Backup path and collation configuration validated."
        };

        public string DatabaseName { get; set; } = string.Empty;
        public string TargetCollation { get; set; } = string.Empty;
        public int UserCharacterColumnCount { get; set; }
        public int RetainedColumnCount { get; set; }
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

        public string StatusBadgeText => IsValid ? "PASSED" : "BLOCKED";

        public string SummaryMessage => IsValid
            ? $"All pre-flight readiness checks passed for database '{DatabaseName}'."
            : $"Blocking conditions detected for database '{DatabaseName}'. Resolve the items below before changing collation.";

        public IReadOnlyList<string> PassedChecks => IsValid ? DefaultPassedChecks : System.Array.Empty<string>();

        public string GetDiagnosticSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Pre-flight Validation Report for Database '{DatabaseName}':");
            sb.AppendLine($"Status: {(IsValid ? "PASSED" : "FAILED (Blocking items found)")}");

            string targetCollationDetail = string.IsNullOrWhiteSpace(TargetCollation)
                ? string.Empty
                : $" (Differing from '{TargetCollation}')";
            sb.AppendLine("\n[Column Collation Scope (Informational)]");
            sb.AppendLine($"  - User Table Character Columns: {UserCharacterColumnCount}");
            sb.AppendLine($"  - User Columns Retaining Prior Collations{targetCollationDetail}: {RetainedColumnCount}");
            sb.AppendLine("  - Scope Note: ALTER DATABASE COLLATE changes only the database default collation and leaves existing user columns unchanged.");

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
