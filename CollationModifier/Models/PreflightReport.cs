using System;
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
        public const int MaxDiagnosticItemsPerSection = 25;
        public const int MaxRenderedDetailLength = 256;

        private static readonly IReadOnlyList<string> DefaultPassedChecks = new[]
        {
            "Required database permissions verified (ALTER, BACKUP DATABASE).",
            "No active user sessions blocking SINGLE_USER mode.",
            "No blocking schema dependencies detected.",
            "Backup path and collation configuration validated."
        };

        private int _totalActiveSessionCount;
        private int _totalSchemaDependencyCount;

        public string DatabaseName { get; set; } = string.Empty;
        public string TargetCollation { get; set; } = string.Empty;
        public int UserCharacterColumnCount { get; set; }
        public int RetainedColumnCount { get; set; }
        public List<ActiveSessionInfo> ActiveSessions { get; set; } = new();
        public List<SchemaDependencyInfo> SchemaDependencies { get; set; } = new();
        public List<string> MissingPermissions { get; set; } = new();
        public List<string> ValidationErrors { get; set; } = new();

        public int TotalActiveSessionCount
        {
            get => Math.Max(_totalActiveSessionCount, ActiveSessions?.Count ?? 0);
            set => _totalActiveSessionCount = Math.Max(0, value);
        }

        public int TotalSchemaDependencyCount
        {
            get => Math.Max(_totalSchemaDependencyCount, SchemaDependencies?.Count ?? 0);
            set => _totalSchemaDependencyCount = Math.Max(0, value);
        }

        public bool HasActiveSessions => TotalActiveSessionCount > 0;
        public bool HasSchemaDependencies => TotalSchemaDependencyCount > 0;
        public bool HasMissingPermissions => (MissingPermissions?.Count ?? 0) > 0;
        public bool HasValidationErrors => (ValidationErrors?.Count ?? 0) > 0;

        public bool IsValid => !HasActiveSessions && !HasSchemaDependencies && !HasMissingPermissions && !HasValidationErrors;
        public bool HasBlockingConditions => !IsValid;

        public string StatusBadgeText => IsValid ? "PASSED" : "BLOCKED";

        public string SummaryMessage => IsValid
            ? $"All pre-flight readiness checks passed for database '{DatabaseName}'."
            : $"Blocking conditions detected for database '{DatabaseName}'. Resolve the items below before changing collation.";

        public IReadOnlyList<string> PassedChecks => IsValid ? DefaultPassedChecks : Array.Empty<string>();

        public string GetSingleLineFailureSummary()
        {
            int sessionCount = TotalActiveSessionCount;
            int dependencyCount = TotalSchemaDependencyCount;
            int permissionCount = MissingPermissions?.Count ?? 0;
            int validationCount = ValidationErrors?.Count ?? 0;

            var sb = new StringBuilder(192);
            sb.Append("Pre-flight validation failed");
            if (!string.IsNullOrWhiteSpace(DatabaseName))
            {
                sb.Append(" for database '");
                sb.Append(NormalizeSingleLine(DatabaseName, 128));
                sb.Append('\'');
            }

            bool hasAnyCount = sessionCount > 0 || dependencyCount > 0 || permissionCount > 0 || validationCount > 0;
            if (hasAnyCount)
            {
                sb.Append(" (");
                bool needSeparator = false;

                if (sessionCount > 0)
                {
                    sb.Append(sessionCount);
                    sb.Append(sessionCount == 1 ? " active session" : " active sessions");
                    needSeparator = true;
                }

                if (dependencyCount > 0)
                {
                    if (needSeparator) sb.Append(", ");
                    sb.Append(dependencyCount);
                    sb.Append(dependencyCount == 1 ? " schema dependency" : " schema dependencies");
                    needSeparator = true;
                }

                if (permissionCount > 0)
                {
                    if (needSeparator) sb.Append(", ");
                    sb.Append(permissionCount);
                    sb.Append(permissionCount == 1 ? " missing permission" : " missing permissions");
                    needSeparator = true;
                }

                if (validationCount > 0)
                {
                    if (needSeparator) sb.Append(", ");
                    sb.Append(validationCount);
                    sb.Append(validationCount == 1 ? " validation error" : " validation errors");
                }

                sb.Append(')');
            }

            if (validationCount > 0 && ValidationErrors != null)
            {
                string firstError = NormalizeSingleLine(ValidationErrors[0], MaxRenderedDetailLength);
                if (!string.IsNullOrEmpty(firstError))
                {
                    sb.Append(": ");
                    sb.Append(firstError);
                }
            }

            return sb.ToString();
        }

        public virtual string GetDiagnosticSummary()
        {
            int totalSessions = TotalActiveSessionCount;
            int renderedSessions = ActiveSessions != null ? Math.Min(ActiveSessions.Count, MaxDiagnosticItemsPerSection) : 0;

            int totalDependencies = TotalSchemaDependencyCount;
            int renderedDependencies = SchemaDependencies != null ? Math.Min(SchemaDependencies.Count, MaxDiagnosticItemsPerSection) : 0;

            int totalPermissions = MissingPermissions?.Count ?? 0;
            int renderedPermissions = Math.Min(totalPermissions, MaxDiagnosticItemsPerSection);

            int totalValidationErrors = ValidationErrors?.Count ?? 0;
            int renderedValidationErrors = Math.Min(totalValidationErrors, MaxDiagnosticItemsPerSection);

            int totalRenderedItems = renderedSessions + renderedDependencies + renderedPermissions + renderedValidationErrors;
            int estimatedCapacity = 512 + (totalRenderedItems * 128);

            var sb = new StringBuilder(estimatedCapacity);
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
                sb.AppendLine($"\n[Active User Sessions ({totalSessions})]");
                for (int i = 0; i < renderedSessions; i++)
                {
                    var s = ActiveSessions![i];
                    sb.AppendLine(
                        $"  - Session ID: {s.SessionId}, User: {NormalizeSingleLine(s.LoginName, 128)}, Host: {NormalizeSingleLine(s.HostName, 128)}, Program: {NormalizeSingleLine(s.ProgramName, 128)}, Status: {NormalizeSingleLine(s.Status, 64)}");
                }

                int omittedSessions = totalSessions - renderedSessions;
                if (omittedSessions > 0)
                {
                    sb.AppendLine($"  ... ({omittedSessions} additional active session(s) omitted)");
                }
            }

            if (HasSchemaDependencies)
            {
                sb.AppendLine($"\n[Schema Dependencies ({totalDependencies})]");
                for (int i = 0; i < renderedDependencies; i++)
                {
                    var dep = SchemaDependencies![i];
                    var schemaObj = string.IsNullOrWhiteSpace(dep.SchemaName) ? dep.ObjectName : $"{dep.SchemaName}.{dep.ObjectName}";
                    sb.AppendLine($"  - [{dep.ObjectType}] {schemaObj}: {NormalizeSingleLine(dep.Details, MaxRenderedDetailLength)}");
                }

                int omittedDependencies = totalDependencies - renderedDependencies;
                if (omittedDependencies > 0)
                {
                    sb.AppendLine($"  ... ({omittedDependencies} additional schema dependency item(s) omitted)");
                }
            }

            if (HasMissingPermissions)
            {
                sb.AppendLine($"\n[Missing Permissions ({totalPermissions})]");
                for (int i = 0; i < renderedPermissions; i++)
                {
                    sb.AppendLine($"  - {NormalizeSingleLine(MissingPermissions![i], MaxRenderedDetailLength)}");
                }

                int omittedPermissions = totalPermissions - renderedPermissions;
                if (omittedPermissions > 0)
                {
                    sb.AppendLine($"  ... ({omittedPermissions} additional missing permission(s) omitted)");
                }
            }

            if (HasValidationErrors)
            {
                sb.AppendLine($"\n[Validation Errors ({totalValidationErrors})]");
                for (int i = 0; i < renderedValidationErrors; i++)
                {
                    sb.AppendLine($"  - {NormalizeSingleLine(ValidationErrors![i], MaxRenderedDetailLength)}");
                }

                int omittedValidationErrors = totalValidationErrors - renderedValidationErrors;
                if (omittedValidationErrors > 0)
                {
                    sb.AppendLine($"  ... ({omittedValidationErrors} additional validation error(s) omitted)");
                }
            }

            return sb.ToString();
        }

        internal static string NormalizeSingleLine(string? text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string trimmed = text.Trim();
            bool hasLineBreak = trimmed.IndexOf('\r') >= 0 || trimmed.IndexOf('\n') >= 0;
            if (!hasLineBreak && trimmed.Length <= maxLength)
            {
                return trimmed;
            }

            int limit = Math.Min(trimmed.Length, maxLength);
            var sb = new StringBuilder(limit + 3);
            bool previousWasNewline = false;

            for (int i = 0; i < trimmed.Length && sb.Length < maxLength; i++)
            {
                char ch = trimmed[i];
                if (ch == '\r' || ch == '\n')
                {
                    if (!previousWasNewline && sb.Length > 0 && sb[sb.Length - 1] != ' ')
                    {
                        sb.Append(' ');
                    }
                    previousWasNewline = true;
                }
                else
                {
                    sb.Append(ch);
                    previousWasNewline = false;
                }
            }

            if (trimmed.Length > maxLength && sb.Length >= maxLength)
            {
                if (maxLength > 3)
                {
                    sb.Length = maxLength - 3;
                    sb.Append("...");
                }
            }

            return sb.ToString().TrimEnd();
        }
    }
}
