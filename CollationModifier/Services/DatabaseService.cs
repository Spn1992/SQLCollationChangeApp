using CollationModifier.Models;
using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.IO;
using System.Threading.Tasks;

namespace CollationModifier.Services
{
    public class DatabaseService : IDatabaseService
    {
        private readonly LoggerService _logger;
        private string _connectionString = string.Empty;

        public DatabaseService(LoggerService logger)
        {
            _logger = logger;
        }

        public virtual void SetConnectionString(string server, string authType, string username, string password)
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = "master",
                ApplicationName = "CollationModifier",
                TrustServerCertificate = true
            };

            if (authType == "Windows Authentication")
            {
                builder.IntegratedSecurity = true;
            }
            else
            {
                builder.UserID = username;
                builder.Password = password;
            }

            _connectionString = builder.ConnectionString;
        }

        public virtual async Task<bool> TestConnectionAsync()
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Connection test failed.", ex);
                return false;
            }
        }

        public virtual async Task<List<DatabaseInfo>> GetDatabasesAsync()
        {
            var databases = new List<DatabaseInfo>();
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Exclude system databases
                    var query = @"
                        SELECT
                            d.name,
                            d.collation_name,
                            d.compatibility_level,
                            SUSER_SNAME(d.owner_sid) as owner,
                            mf.size_mb
                        FROM sys.databases d
                        LEFT JOIN (
                            SELECT
                                database_id,
                                SUM(CAST(size AS bigint)) * 8 / 1024 as size_mb
                            FROM sys.master_files
                            GROUP BY database_id
                        ) mf ON d.database_id = mf.database_id
                        WHERE d.database_id > 4";

                    using (var command = new SqlCommand(query, connection))
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            databases.Add(new DatabaseInfo
                            {
                                Name = reader.GetString(0),
                                Collation = reader.IsDBNull(1) ? "Unknown" : reader.GetString(1),
                                CompatibilityLevel = reader.GetByte(2).ToString(),
                                Owner = reader.IsDBNull(3) ? "Unknown" : reader.GetString(3),
                                Size = reader.IsDBNull(4) ? "Unknown" : $"{reader.GetInt64(4)} MB"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to fetch databases.", ex);
                throw;
            }

            return databases;
        }

        public virtual async Task<List<string>> GetSupportedCollationsAsync()
        {
            var collations = new List<string>();
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    var query = "SELECT name FROM sys.fn_helpcollations() ORDER BY name;";

                    using (var command = new SqlCommand(query, connection))
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            if (!reader.IsDBNull(0))
                            {
                                collations.Add(reader.GetString(0));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to fetch supported collations.", ex);
                throw;
            }

            return collations;
        }

        public async Task<string?> GetDefaultBackupDirectoryAsync()
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    var query = @"
                        DECLARE @path NVARCHAR(4000) = NULL;

                        BEGIN TRY
                            SET @path = CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS NVARCHAR(4000));
                        END TRY
                        BEGIN CATCH
                        END CATCH;

                        IF @path IS NULL OR @path = ''
                        BEGIN
                            BEGIN TRY
                                EXEC master.dbo.xp_instance_regread
                                    N'HKEY_LOCAL_MACHINE',
                                    N'Software\Microsoft\MSSQLServer\MSSQLServer',
                                    N'BackupDirectory',
                                    @path OUTPUT;
                            END TRY
                            BEGIN CATCH
                            END CATCH;
                        END;

                        IF @path IS NULL OR @path = ''
                        BEGIN
                            BEGIN TRY
                                EXEC master.dbo.xp_instance_regread
                                    N'HKEY_LOCAL_MACHINE',
                                    N'Software\Microsoft\MSSQLServer\MSSQLServer\MSSQLServer',
                                    N'BackupDirectory',
                                    @path OUTPUT;
                            END TRY
                            BEGIN CATCH
                            END CATCH;
                        END;

                        IF @path IS NULL OR @path = ''
                        BEGIN
                            BEGIN TRY
                                SET @path = CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS NVARCHAR(4000));
                            END TRY
                            BEGIN CATCH
                            END CATCH;
                        END;

                        SELECT ISNULL(@path, '') AS DefaultBackupPath;";

                    using (var command = new SqlCommand(query, connection))
                    {
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            var backupPath = result.ToString()?.Trim();
                            if (!string.IsNullOrWhiteSpace(backupPath))
                            {
                                _logger.LogInfo($"Retrieved SQL Server default backup path: {backupPath}");
                                return backupPath;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to retrieve default backup directory from server.", ex);
            }

            return null;
        }

        public virtual async Task<List<string>> GetSupportedCollationsAsync()
        {
            var collations = new List<string>();
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    var query = "SELECT name FROM sys.fn_helpcollations() ORDER BY name;";

                    using (var command = new SqlCommand(query, connection))
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            if (!reader.IsDBNull(0))
                            {
                                collations.Add(reader.GetString(0));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to fetch supported collations.", ex);
                throw;
            }

            return collations;
        }

        public static void ValidateBackupDirectoryPath(string backupDirectory)
        {
            if (string.IsNullOrWhiteSpace(backupDirectory))
            {
                throw new ArgumentException("Backup directory path cannot be empty or whitespace.");
            }

            string trimmedPath = backupDirectory.Trim();

            char[] invalidChars = new char[] { '|', '<', '>', '"', '?', '*' };
            if (trimmedPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || trimmedPath.IndexOfAny(invalidChars) >= 0)
            {
                throw new ArgumentException($"Backup directory path contains invalid path characters: '{backupDirectory}'");
            }

            bool isWindowsDrive = System.Text.RegularExpressions.Regex.IsMatch(trimmedPath, @"^[a-zA-Z]:[\\/]");
            bool isUncShare = trimmedPath.StartsWith(@"\\") || trimmedPath.StartsWith("//");
            bool isLinuxRoot = trimmedPath.StartsWith("/");

            if (!isWindowsDrive && !isUncShare && !isLinuxRoot && !Path.IsPathRooted(trimmedPath))
            {
                throw new ArgumentException($"Backup directory '{backupDirectory}' must be a valid absolute path or UNC share on the target SQL Server host.");
            }
        }

        public virtual async Task<PreflightReport> PreflightCheckAsync(string databaseName, string backupDirectory = "")
        {
            var report = new PreflightReport
            {
                DatabaseName = databaseName
            };

            if (!string.IsNullOrWhiteSpace(backupDirectory))
            {
                try
                {
                    ValidateBackupDirectoryPath(backupDirectory);
                }
                catch (Exception ex)
                {
                    report.ValidationErrors.Add($"Invalid backup directory: {ex.Message}");
                }
            }

            var safeDbName = EscapeIdentifier(databaseName);
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // 1. Check permissions in target database context
                    try
                    {
                        var permQuery = $@"
                            USE [{safeDbName}];
                            SELECT permission_name FROM fn_my_permissions(NULL, 'DATABASE')
                            WHERE permission_name IN ('BACKUP DATABASE', 'BACKUP LOG', 'ALTER');";

                        var permissions = new List<string>();
                        using (var command = new SqlCommand(permQuery, connection))
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                permissions.Add(reader.GetString(0));
                            }
                        }

                        if (!permissions.Contains("ALTER"))
                        {
                            report.MissingPermissions.Add("User does not have ALTER permission on the database.");
                        }
                    }
                    catch (Exception ex)
                    {
                        report.ValidationErrors.Add($"Failed to verify database permissions: {ex.Message}");
                    }

                    // 2. Check active user sessions (excluding current connection context @@SPID)
                    try
                    {
                        var sessionsQuery = @"
                            SELECT 
                                s.session_id,
                                ISNULL(s.login_name, '') as login_name,
                                ISNULL(s.host_name, '') as host_name,
                                ISNULL(s.program_name, '') as program_name,
                                ISNULL(s.status, '') as status
                            FROM sys.dm_exec_sessions s
                            WHERE s.database_id = DB_ID(@DbName)
                              AND s.session_id <> @@SPID
                              AND s.is_user_process = 1;";

                        using (var command = new SqlCommand(sessionsQuery, connection))
                        {
                            command.Parameters.AddWithValue("@DbName", databaseName);
                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                while (await reader.ReadAsync())
                                {
                                    report.ActiveSessions.Add(new ActiveSessionInfo
                                    {
                                        SessionId = reader.GetInt16(0),
                                        LoginName = reader.GetString(1),
                                        HostName = reader.GetString(2),
                                        ProgramName = reader.GetString(3),
                                        Status = reader.GetString(4)
                                    });
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to query active sessions: {ex.Message}");
                    }

                    // 3. Check schema dependencies in target database context
                    try
                    {
                        using (var useCmd = new SqlCommand($"USE [{safeDbName}];", connection))
                        {
                            await useCmd.ExecuteNonQueryAsync();
                        }

                        // 3a. Schema-bound views / modules
                        var schemaBoundQuery = @"
                            SELECT 
                                s.name AS schema_name,
                                o.name AS object_name,
                                o.type_desc AS object_type
                            FROM sys.sql_modules m
                            JOIN sys.objects o ON m.object_id = o.object_id
                            JOIN sys.schemas s ON o.schema_id = s.schema_id
                            WHERE m.is_schema_bound = 1;";

                        using (var command = new SqlCommand(schemaBoundQuery, connection))
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                report.SchemaDependencies.Add(new SchemaDependencyInfo
                                {
                                    SchemaName = reader.GetString(0),
                                    ObjectName = reader.GetString(1),
                                    ObjectType = "Schema-Bound Object",
                                    Details = $"Definition is schema-bound ({reader.GetString(2)})."
                                });
                            }
                        }

                        // 3b. Computed columns
                        var computedColsQuery = @"
                            SELECT 
                                s.name AS schema_name,
                                t.name AS table_name,
                                c.name AS column_name,
                                ISNULL(c.definition, '') AS expression
                            FROM sys.computed_columns c
                            JOIN sys.tables t ON c.object_id = t.object_id
                            JOIN sys.schemas s ON t.schema_id = s.schema_id;";

                        using (var command = new SqlCommand(computedColsQuery, connection))
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                report.SchemaDependencies.Add(new SchemaDependencyInfo
                                {
                                    SchemaName = reader.GetString(0),
                                    ObjectName = $"{reader.GetString(1)}.{reader.GetString(2)}",
                                    ObjectType = "Computed Column",
                                    Details = $"Computed column expression: {reader.GetString(3)}"
                                });
                            }
                        }

                        // 3c. Foreign Keys
                        var fkQuery = @"
                            SELECT 
                                s.name AS schema_name,
                                fk.name AS constraint_name,
                                t.name AS parent_table,
                                rt.name AS referenced_table
                            FROM sys.foreign_keys fk
                            JOIN sys.tables t ON fk.parent_object_id = t.object_id
                            JOIN sys.tables rt ON fk.referenced_object_id = rt.object_id
                            JOIN sys.schemas s ON fk.schema_id = s.schema_id;";

                        using (var command = new SqlCommand(fkQuery, connection))
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                report.SchemaDependencies.Add(new SchemaDependencyInfo
                                {
                                    SchemaName = reader.GetString(0),
                                    ObjectName = reader.GetString(1),
                                    ObjectType = "Foreign Key Constraint",
                                    Details = $"Table '{reader.GetString(2)}' references '{reader.GetString(3)}'."
                                });
                            }
                        }

                        // 3d. Check Constraints
                        var checkQuery = @"
                            SELECT 
                                s.name AS schema_name,
                                chk.name AS constraint_name,
                                t.name AS table_name,
                                ISNULL(chk.definition, '') AS expression
                            FROM sys.check_constraints chk
                            JOIN sys.tables t ON chk.parent_object_id = t.object_id
                            JOIN sys.schemas s ON chk.schema_id = s.schema_id;";

                        using (var command = new SqlCommand(checkQuery, connection))
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                report.SchemaDependencies.Add(new SchemaDependencyInfo
                                {
                                    SchemaName = reader.GetString(0),
                                    ObjectName = reader.GetString(1),
                                    ObjectType = "Check Constraint",
                                    Details = $"Check constraint on table '{reader.GetString(2)}': {reader.GetString(3)}"
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        report.ValidationErrors.Add($"Failed to inspect database schema dependencies: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Preflight check failed for {databaseName}", ex);
                report.ValidationErrors.Add($"Database connection or pre-flight query error: {ex.Message}");
            }

            return report;
        }

        public virtual async Task<string> BackupDatabaseAsync(string databaseName, string backupDirectory)
        {
            ValidateBackupDirectoryPath(backupDirectory);
            try
            {
                // Note: The backupDirectory must exist on the SQL Server machine, not the client running this app.
                var safeDbName = EscapeIdentifier(databaseName);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                // Clean the database name for file path safety
                var safeFileName = string.Join("_", databaseName.Split(Path.GetInvalidFileNameChars()));
                var backupFile = Path.Combine(backupDirectory, $"{safeFileName}_{timestamp}.bak");

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    var query = $"BACKUP DATABASE [{safeDbName}] TO DISK = @BackupFile WITH FORMAT, INIT, STATS = 10;";
                    await ExecuteNonQueryAsync(connection, query, 3600, new SqlParameter("@BackupFile", backupFile));
                }
                _logger.LogInfo($"Successfully backed up {databaseName} to {backupFile}");
                return backupFile;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Backup failed for database: {databaseName}", ex);
                throw;
            }
        }

        public virtual async Task<List<string>> GetSupportedCollationsAsync()
        {
            var collations = new List<string>();
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    var query = "SELECT name FROM sys.fn_helpcollations() ORDER BY name;";

                    using (var command = new SqlCommand(query, connection))
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            if (!reader.IsDBNull(0))
                            {
                                collations.Add(reader.GetString(0));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to fetch supported collations.", ex);
                throw;
            }

            return collations;
        }

        public virtual async Task RestoreDatabaseAsync(string databaseName, string backupFile)
        {
            var safeDbName = EscapeIdentifier(databaseName);
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    await ExecuteNonQueryAsync(connection, "USE [master];");
                    await SetDatabaseUserModeAsync(connection, databaseName, "SINGLE_USER");
                    var restoreQuery = $"RESTORE DATABASE [{safeDbName}] FROM DISK = @BackupFile WITH REPLACE;";
                    await ExecuteNonQueryAsync(connection, restoreQuery, 3600, new SqlParameter("@BackupFile", backupFile));
                    await SetDatabaseUserModeAsync(connection, databaseName, "MULTI_USER");
                }
                _logger.LogInfo($"Successfully restored {databaseName} from {backupFile}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Restore failed for database: {databaseName} from {backupFile}", ex);
                // Attempt emergency MULTI_USER reset if batch execution failed
                try
                {
                    await TrySetMultiUserAsync(databaseName);
                }
                catch (Exception resetEx)
                {
                    _logger.LogError($"Emergency MULTI_USER reset failed following restore error on {databaseName}", resetEx);
                    throw new AggregateException(
                        $"Failed to restore database '{databaseName}' ({ex.Message}) and emergency MULTI_USER reset failed: {resetEx.Message}",
                        ex, resetEx);
                }
                throw;
            }
        }

        public virtual async Task AlterCollationAsync(string databaseName, string newCollation)
        {
            // Validate the collation to prevent SQL Injection
            if (!System.Text.RegularExpressions.Regex.IsMatch(newCollation, @"^[a-zA-Z0-9_]+$"))
            {
                throw new ArgumentException("Invalid collation name format. Only alphanumeric characters and underscores are allowed.");
            }

            var safeDbName = EscapeIdentifier(databaseName);

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    await SetDatabaseUserModeAsync(connection, databaseName, "SINGLE_USER");
                    var query = $"ALTER DATABASE [{safeDbName}] COLLATE {newCollation};";
                    await ExecuteNonQueryAsync(connection, query, 3600);
                    await SetDatabaseUserModeAsync(connection, databaseName, "MULTI_USER");
                }
                _logger.LogInfo($"Successfully changed collation of {databaseName} to {newCollation}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to alter collation for database: {databaseName}", ex);
                // Attempt to put it back to multi-user if it failed mid-way
                try
                {
                    await TrySetMultiUserAsync(databaseName);
                }
                catch (Exception resetEx)
                {
                    _logger.LogError($"Emergency MULTI_USER reset failed following collation error on {databaseName}", resetEx);
                    throw new AggregateException(
                        $"Failed to alter collation for database '{databaseName}' ({ex.Message}) and emergency MULTI_USER reset failed: {resetEx.Message}",
                        ex, resetEx);
                }
                throw;
            }
        }

        public virtual async Task TrySetMultiUserAsync(string databaseName)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    await SetDatabaseUserModeAsync(connection, databaseName, "MULTI_USER");
                }
            }
            catch (Exception ex)
            {
                 _logger.LogError($"Failed emergency MULTI_USER reset for database: {databaseName}", ex);
                 throw;
            }
        }

        private static string EscapeIdentifier(string identifier)
        {
            return identifier.Replace("]", "]]");
        }

        private static async Task<int> ExecuteNonQueryAsync(
            SqlConnection connection,
            string commandText,
            int timeoutSeconds = 3600,
            params SqlParameter[] parameters)
        {
            using (var command = new SqlCommand(commandText, connection))
            {
                command.CommandTimeout = timeoutSeconds;
                if (parameters != null && parameters.Length > 0)
                {
                    command.Parameters.AddRange(parameters);
                }
                return await command.ExecuteNonQueryAsync();
            }
        }

        private async Task SetDatabaseUserModeAsync(SqlConnection connection, string databaseName, string mode)
        {
            var safeDbName = EscapeIdentifier(databaseName);
            string query = string.Equals(mode, "SINGLE_USER", StringComparison.OrdinalIgnoreCase)
                ? $"ALTER DATABASE [{safeDbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;"
                : $"ALTER DATABASE [{safeDbName}] SET MULTI_USER;";

            await ExecuteNonQueryAsync(connection, query);
        }
    }
}
