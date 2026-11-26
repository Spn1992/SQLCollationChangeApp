using CollationModifier.Models;
using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CollationModifier.Services
{
    public class DatabaseService : IDatabaseService
    {
        private const string DatabaseCatalogQuery = @"
                        IF OBJECT_ID('tempdb..#CollationModifierColCounts') IS NOT NULL
                            DROP TABLE #CollationModifierColCounts;

                        CREATE TABLE #CollationModifierColCounts (
                            database_id INT PRIMARY KEY,
                            user_character_column_count INT NOT NULL
                        );

                        DECLARE @colCountSql NVARCHAR(MAX) = N'';
                        SELECT @colCountSql = @colCountSql +
                            N'BEGIN TRY INSERT INTO #CollationModifierColCounts (database_id, user_character_column_count) ' +
                            N'SELECT ' + CAST(d.database_id AS NVARCHAR(20)) + N', COUNT(*) ' +
                            N'FROM [' + REPLACE(d.name, N']', N']]') + N'].sys.columns c ' +
                            N'JOIN [' + REPLACE(d.name, N']', N']]') + N'].sys.tables t ON c.object_id = t.object_id ' +
                            N'WHERE t.is_ms_shipped = 0 AND c.is_computed = 0 AND c.collation_name IS NOT NULL; ' +
                            N'END TRY BEGIN CATCH END CATCH; '
                        FROM sys.databases d
                        WHERE d.database_id > 4 AND d.state = 0 AND HAS_DBACCESS(d.name) = 1;

                        IF @colCountSql <> N''
                            EXEC sp_executesql @colCountSql;

                        SELECT
                            d.name,
                            d.collation_name,
                            d.compatibility_level,
                            SUSER_SNAME(d.owner_sid) as owner,
                            mf.size_mb,
                            cc.user_character_column_count
                        FROM sys.databases d
                        LEFT JOIN (
                            SELECT
                                database_id,
                                SUM(CAST(size AS bigint)) * 8 / 1024 as size_mb
                            FROM sys.master_files
                            GROUP BY database_id
                        ) mf ON d.database_id = mf.database_id
                        LEFT JOIN #CollationModifierColCounts cc ON d.database_id = cc.database_id
                        WHERE d.database_id > 4;

                        DROP TABLE #CollationModifierColCounts";

        private static readonly Regex CollationRegex = new Regex(@"^[a-zA-Z0-9_]+$", RegexOptions.Compiled);
        private const string DefaultBackupDirectoryQuery = @"
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
        private readonly LoggerService _logger;
        private string _connectionString = string.Empty;
        private readonly object _collationCacheLock = new object();
        private HashSet<string>? _cachedSupportedCollations;

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

            var newConnectionString = builder.ConnectionString;
            if (!string.Equals(_connectionString, newConnectionString, StringComparison.Ordinal))
            {
                lock (_collationCacheLock)
                {
                    _cachedSupportedCollations?.Clear();
                    _cachedSupportedCollations = null;
                }
            }

            _connectionString = newConnectionString;
        }

        public virtual async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await OpenConnectionAsync(connection, cancellationToken);
                    return true;
                }
            }
            catch (SqlException ex) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("Connection test was cancelled.", ex, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError("Connection test failed.", ex);
                return false;
            }
        }

        public virtual async Task<List<DatabaseInfo>> GetDatabasesAsync(CancellationToken cancellationToken = default)
        {
            var databases = new List<DatabaseInfo>();
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await OpenConnectionAsync(connection, cancellationToken);

                    using (var command = new SqlCommand(DatabaseCatalogQuery, connection))
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                    {
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            databases.Add(MapDatabaseInfo(reader));
                        }
                    }
                }
            }
            catch (SqlException ex) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInfo("Fetching databases was cancelled.");
                throw new OperationCanceledException("Fetching databases was cancelled.", ex, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("Fetching databases was cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to fetch databases.", ex);
                throw;
            }

            return databases;
        }

        public static DatabaseInfo MapDatabaseInfo(IDataRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            int userCharacterColumnCount = 0;
            string userCharacterColumns = "0";
            if (record.FieldCount > 5)
            {
                if (record.IsDBNull(5))
                {
                    userCharacterColumns = "Unknown";
                }
                else
                {
                    userCharacterColumnCount = Convert.ToInt32(record.GetValue(5));
                    userCharacterColumns = userCharacterColumnCount.ToString();
                }
            }

            var dbInfo = new DatabaseInfo
            {
                Name = record.GetString(0),
                Collation = record.IsDBNull(1) ? "Unknown" : record.GetString(1),
                CompatibilityLevel = record.GetByte(2).ToString(),
                Owner = record.IsDBNull(3) ? "Unknown" : record.GetString(3),
                Size = record.IsDBNull(4) ? "Unknown" : $"{record.GetInt64(4)} MB",
                UserCharacterColumnCount = userCharacterColumnCount
            };
            dbInfo.UserCharacterColumns = userCharacterColumns;
            return dbInfo;
        }

        public virtual async Task<List<string>> GetSupportedCollationsAsync(CancellationToken cancellationToken = default)
        {
            var collations = new List<string>();
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await OpenConnectionAsync(connection, cancellationToken);
                    var query = "SELECT name FROM sys.fn_helpcollations() ORDER BY name;";

                    using (var command = new SqlCommand(query, connection))
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                    {
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            if (!reader.IsDBNull(0))
                            {
                                collations.Add(reader.GetString(0));
                            }
                        }
                    }
                }

                lock (_collationCacheLock)
                {
                    _cachedSupportedCollations = new HashSet<string>(collations, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (SqlException ex) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInfo("Fetching supported collations was cancelled.");
                throw new OperationCanceledException("Fetching supported collations was cancelled.", ex, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("Fetching supported collations was cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to fetch supported collations.", ex);
                throw;
            }

            return collations;
        }

        public async Task<string?> GetDefaultBackupDirectoryAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await OpenConnectionAsync(connection, cancellationToken);

                    using (var command = new SqlCommand(DefaultBackupDirectoryQuery, connection))
                    {
                        var result = await command.ExecuteScalarAsync(cancellationToken);
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
            catch (SqlException ex) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInfo("Retrieving default backup directory was cancelled.");
                throw new OperationCanceledException("Retrieving default backup directory was cancelled.", ex, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("Retrieving default backup directory was cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to retrieve default backup directory from server.", ex);
            }

            return null;
        }

        public virtual async Task<ServerInitializationData> GetServerInitializationDataAsync(CancellationToken cancellationToken = default)
        {
            var initData = new ServerInitializationData();

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await OpenConnectionAsync(connection, cancellationToken);

                    var batchedQuery = $@"
                        -- 1. Default Backup Directory
{DefaultBackupDirectoryQuery}

                        -- 2. Databases Catalog
                        {DatabaseCatalogQuery};

                        -- 3. Supported Collations
                        SELECT name FROM sys.fn_helpcollations() ORDER BY name;";

                    using (var command = new SqlCommand(batchedQuery, connection))
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                    {
                        // Result Set 1: Default Backup Directory
                        if (await reader.ReadAsync(cancellationToken))
                        {
                            if (!reader.IsDBNull(0))
                            {
                                var backupPath = reader.GetString(0).Trim();
                                if (!string.IsNullOrWhiteSpace(backupPath))
                                {
                                    initData.DefaultBackupDirectory = backupPath;
                                    _logger.LogInfo($"Retrieved SQL Server default backup path: {backupPath}");
                                }
                            }
                        }

                        // Result Set 2: Databases Catalog
                        if (await reader.NextResultAsync(cancellationToken))
                        {
                            while (await reader.ReadAsync(cancellationToken))
                            {
                                initData.Databases.Add(MapDatabaseInfo(reader));
                            }
                            _logger.LogInfo($"Retrieved {initData.Databases.Count} databases.");
                        }

                        // Result Set 3: Supported Collations
                        if (await reader.NextResultAsync(cancellationToken))
                        {
                            while (await reader.ReadAsync(cancellationToken))
                            {
                                if (!reader.IsDBNull(0))
                                {
                                    initData.SupportedCollations.Add(reader.GetString(0));
                                }
                            }
                            _logger.LogInfo($"Retrieved {initData.SupportedCollations.Count} supported collations.");
                        }
                    }
                }
            }
            catch (SqlException ex) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInfo("Server initialization query was cancelled.");
                throw new OperationCanceledException("Server initialization query was cancelled.", ex, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo("Server initialization query was cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to retrieve server initialization data.", ex);
                throw;
            }

            return initData;
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

        public static bool IsValidCollationName(string? collation)
        {
            if (string.IsNullOrWhiteSpace(collation))
            {
                return false;
            }

            return CollationRegex.IsMatch(collation);
        }

        public static void ValidateCollationFormat(string? collation)
        {
            if (!IsValidCollationName(collation))
            {
                throw new ArgumentException($"Invalid target collation format: '{collation}'. Invalid collation name format. Collation names must contain only alphanumeric characters and underscores.");
            }
        }

        public static void EvaluatePermissions(IEnumerable<string> permissions, PreflightReport report)
        {
            var permissionSet = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
            if (!permissionSet.Contains("ALTER"))
            {
                report.MissingPermissions.Add("User does not have ALTER permission on the database.");
            }
            if (!permissionSet.Contains("BACKUP DATABASE"))
            {
                report.MissingPermissions.Add("User does not have BACKUP DATABASE permission on the database. User lacks required database backup privileges.");
            }
        }

        public static void EvaluateCollation(string databaseName, string? currentCollation, string? targetCollation, PreflightReport report)
        {
            if (string.IsNullOrWhiteSpace(currentCollation) || string.IsNullOrWhiteSpace(targetCollation))
            {
                return;
            }

            if (string.Equals(targetCollation.Trim(), currentCollation.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                string errorMessage = $"Database '{databaseName}' already uses collation '{currentCollation}'. Target collation '{targetCollation}' is identical to the current database collation.";
                if (!report.ValidationErrors.Contains(errorMessage))
                {
                    report.ValidationErrors.Add(errorMessage);
                }
            }
        }

        public static void EvaluateColumnCollationCounts(int userCharacterColumnCount, int retainedColumnCount, string? targetCollation, PreflightReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            int normalizedTotal = Math.Max(0, userCharacterColumnCount);
            int normalizedRetained = string.IsNullOrWhiteSpace(targetCollation)
                ? 0
                : Math.Min(normalizedTotal, Math.Max(0, retainedColumnCount));

            report.TargetCollation = targetCollation?.Trim() ?? string.Empty;
            report.UserCharacterColumnCount = normalizedTotal;
            report.RetainedColumnCount = normalizedRetained;
        }

        public static void EvaluateColumnCollations(IEnumerable<string?> userColumnCollations, string? targetCollation, PreflightReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            string normalizedTarget = targetCollation?.Trim() ?? string.Empty;
            int totalCharacterColumns = 0;
            int retainedColumns = 0;

            if (userColumnCollations != null)
            {
                foreach (var colCollation in userColumnCollations)
                {
                    if (string.IsNullOrWhiteSpace(colCollation))
                    {
                        continue;
                    }

                    totalCharacterColumns++;
                    if (!string.IsNullOrEmpty(normalizedTarget) &&
                        !string.Equals(colCollation.Trim(), normalizedTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        retainedColumns++;
                    }
                }
            }

            EvaluateColumnCollationCounts(totalCharacterColumns, retainedColumns, normalizedTarget, report);
        }

        public virtual async Task<(int UserCharacterColumnCount, int RetainedColumnCount)> GetColumnCollationScopeAsync(
            string databaseName,
            string targetCollation = "",
            CancellationToken cancellationToken = default)
        {
            var safeDbName = EscapeIdentifier(databaseName);
            string normalizedTarget = targetCollation?.Trim() ?? string.Empty;

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync(cancellationToken);

                    var query = $@"
                        USE [{safeDbName}];
                        SELECT
                            COUNT(*) AS user_character_column_count,
                            ISNULL(SUM(CASE WHEN @TargetCollation <> N'' AND c.collation_name COLLATE Latin1_General_CI_AS <> @TargetCollation COLLATE Latin1_General_CI_AS THEN 1 ELSE 0 END), 0) AS retained_column_count
                        FROM sys.columns c
                        JOIN sys.tables t ON c.object_id = t.object_id
                        WHERE t.is_ms_shipped = 0
                          AND c.is_computed = 0
                          AND c.collation_name IS NOT NULL;";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@TargetCollation", normalizedTarget);
                        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            if (await reader.ReadAsync(cancellationToken))
                            {
                                int totalCount = !reader.IsDBNull(0) ? Convert.ToInt32(reader.GetValue(0)) : 0;
                                int retainedCount = !reader.IsDBNull(1) ? Convert.ToInt32(reader.GetValue(1)) : 0;
                                return (Math.Max(0, totalCount), Math.Max(0, retainedCount));
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to inspect column collation scope for {databaseName}.", ex);
                throw;
            }

            return (0, 0);
        }

        internal const int MaxPreflightSampleRows = PreflightReport.MaxDiagnosticItemsPerSection;
        internal const int MaxPreflightDefinitionLength = 200;

        internal static string BuildPreflightBatchedQuery(string safeDbName)
        {
            int topLimit = MaxPreflightSampleRows;
            int maxDefLen = MaxPreflightDefinitionLength;
            int probeDefLen = maxDefLen + 1;

            return $@"
                        USE [{safeDbName}];

                        -- 1. Check permissions in target database context
                        SELECT permission_name FROM fn_my_permissions(NULL, 'DATABASE')
                        WHERE permission_name IN ('BACKUP DATABASE', 'BACKUP LOG', 'ALTER');

                        -- 2. Check active user sessions
                        SELECT TOP ({topLimit})
                            s.session_id,
                            ISNULL(s.login_name, N'') AS login_name,
                            ISNULL(s.host_name, N'') AS host_name,
                            ISNULL(s.program_name, N'') AS program_name,
                            ISNULL(s.status, N'') AS status,
                            COUNT(*) OVER () AS total_count
                        FROM sys.dm_exec_sessions s
                        WHERE s.database_id = DB_ID(@DbName)
                          AND s.session_id <> @@SPID
                          AND s.is_user_process = 1
                        ORDER BY s.session_id;

                        -- 3a. Schema-bound views / modules
                        SELECT TOP ({topLimit})
                            s.name AS schema_name,
                            o.name AS object_name,
                            o.type_desc AS object_type,
                            COUNT(*) OVER () AS total_count
                        FROM sys.sql_modules m
                        JOIN sys.objects o ON m.object_id = o.object_id
                        JOIN sys.schemas s ON o.schema_id = s.schema_id
                        WHERE m.is_schema_bound = 1
                        ORDER BY s.name, o.name;

                        -- 3b. Computed columns
                        SELECT TOP ({topLimit})
                            s.name AS schema_name,
                            t.name AS table_name,
                            c.name AS column_name,
                            CASE
                                WHEN LEN(LEFT(ISNULL(c.definition, N''), {probeDefLen}) + N'.') > {probeDefLen}
                                    THEN LEFT(ISNULL(c.definition, N''), {maxDefLen}) + N'...'
                                ELSE LEFT(ISNULL(c.definition, N''), {maxDefLen})
                            END AS expression,
                            COUNT(*) OVER () AS total_count
                        FROM sys.computed_columns c
                        JOIN sys.tables t ON c.object_id = t.object_id
                        JOIN sys.schemas s ON t.schema_id = s.schema_id
                        ORDER BY s.name, t.name, c.name;

                        -- 3c. Foreign Keys
                        SELECT TOP ({topLimit})
                            s.name AS schema_name,
                            fk.name AS constraint_name,
                            t.name AS parent_table,
                            rt.name AS referenced_table,
                            COUNT(*) OVER () AS total_count
                        FROM sys.foreign_keys fk
                        JOIN sys.tables t ON fk.parent_object_id = t.object_id
                        JOIN sys.tables rt ON fk.referenced_object_id = rt.object_id
                        JOIN sys.schemas s ON fk.schema_id = s.schema_id
                        ORDER BY s.name, fk.name;

                        -- 3d. Check Constraints
                        SELECT TOP ({topLimit})
                            s.name AS schema_name,
                            chk.name AS constraint_name,
                            t.name AS table_name,
                            CASE
                                WHEN LEN(LEFT(ISNULL(chk.definition, N''), {probeDefLen}) + N'.') > {probeDefLen}
                                    THEN LEFT(ISNULL(chk.definition, N''), {maxDefLen}) + N'...'
                                ELSE LEFT(ISNULL(chk.definition, N''), {maxDefLen})
                            END AS expression,
                            COUNT(*) OVER () AS total_count
                        FROM sys.check_constraints chk
                        JOIN sys.tables t ON chk.parent_object_id = t.object_id
                        JOIN sys.schemas s ON chk.schema_id = s.schema_id
                        ORDER BY s.name, chk.name;

                        -- 4. Target database collation
                        SELECT ISNULL(CONVERT(NVARCHAR(128), DATABASEPROPERTYEX(@DbName, 'Collation')), ISNULL((SELECT collation_name FROM sys.databases WHERE name = @DbName), ''));

                        -- 5. User table character column collation audit
                        SELECT
                            COUNT(*) AS user_character_column_count,
                            ISNULL(SUM(CASE WHEN @TargetCollation <> N'' AND c.collation_name COLLATE Latin1_General_CI_AS <> @TargetCollation COLLATE Latin1_General_CI_AS THEN 1 ELSE 0 END), 0) AS retained_column_count
                        FROM sys.columns c
                        JOIN sys.tables t ON c.object_id = t.object_id
                        WHERE t.is_ms_shipped = 0
                          AND c.is_computed = 0
                          AND c.collation_name IS NOT NULL;";
        }

        internal static async Task PopulatePreflightReportFromReaderAsync(
            System.Data.Common.DbDataReader reader,
            string databaseName,
            string? targetCollation,
            PreflightReport report,
            CancellationToken cancellationToken = default)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            if (report == null) throw new ArgumentNullException(nameof(report));

            // 1. Database Permissions
            var permissions = new List<string>(4);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0))
                {
                    permissions.Add(reader.GetString(0));
                }
            }
            EvaluatePermissions(permissions, report);

            // 2. Active User Sessions
            if (await reader.NextResultAsync(cancellationToken))
            {
                int totalSessions = 0;
                int observedSessions = 0;
                while (await reader.ReadAsync(cancellationToken))
                {
                    observedSessions++;
                    if (reader.FieldCount > 5 && !reader.IsDBNull(5))
                    {
                        totalSessions = Math.Max(totalSessions, Convert.ToInt32(reader.GetValue(5)));
                    }

                    if (report.ActiveSessions.Count < MaxPreflightSampleRows)
                    {
                        report.ActiveSessions.Add(new ActiveSessionInfo
                        {
                            SessionId = !reader.IsDBNull(0) ? Convert.ToInt32(reader.GetValue(0)) : 0,
                            LoginName = !reader.IsDBNull(1) ? reader.GetString(1) : string.Empty,
                            HostName = !reader.IsDBNull(2) ? reader.GetString(2) : string.Empty,
                            ProgramName = !reader.IsDBNull(3) ? reader.GetString(3) : string.Empty,
                            Status = !reader.IsDBNull(4) ? reader.GetString(4) : string.Empty
                        });
                    }
                }
                report.TotalActiveSessionCount = Math.Max(totalSessions, observedSessions);
            }

            int totalSchemaDependencies = 0;

            // 3a. Schema-bound views / modules
            if (await reader.NextResultAsync(cancellationToken))
            {
                int sectionTotal = 0;
                int sectionObserved = 0;
                while (await reader.ReadAsync(cancellationToken))
                {
                    sectionObserved++;
                    if (reader.FieldCount > 3 && !reader.IsDBNull(3))
                    {
                        sectionTotal = Math.Max(sectionTotal, Convert.ToInt32(reader.GetValue(3)));
                    }

                    if (report.SchemaDependencies.Count < MaxPreflightSampleRows)
                    {
                        report.SchemaDependencies.Add(new SchemaDependencyInfo
                        {
                            SchemaName = !reader.IsDBNull(0) ? reader.GetString(0) : string.Empty,
                            ObjectName = !reader.IsDBNull(1) ? reader.GetString(1) : string.Empty,
                            ObjectType = "Schema-Bound Object",
                            Details = $"Definition is schema-bound ({(!reader.IsDBNull(2) ? reader.GetString(2) : string.Empty)})."
                        });
                    }
                }
                totalSchemaDependencies += Math.Max(sectionTotal, sectionObserved);
            }

            // 3b. Computed columns
            if (await reader.NextResultAsync(cancellationToken))
            {
                int sectionTotal = 0;
                int sectionObserved = 0;
                while (await reader.ReadAsync(cancellationToken))
                {
                    sectionObserved++;
                    if (reader.FieldCount > 4 && !reader.IsDBNull(4))
                    {
                        sectionTotal = Math.Max(sectionTotal, Convert.ToInt32(reader.GetValue(4)));
                    }

                    if (report.SchemaDependencies.Count < MaxPreflightSampleRows)
                    {
                        string tableName = !reader.IsDBNull(1) ? reader.GetString(1) : string.Empty;
                        string columnName = !reader.IsDBNull(2) ? reader.GetString(2) : string.Empty;
                        string expression = !reader.IsDBNull(3) ? reader.GetString(3) : string.Empty;
                        report.SchemaDependencies.Add(new SchemaDependencyInfo
                        {
                            SchemaName = !reader.IsDBNull(0) ? reader.GetString(0) : string.Empty,
                            ObjectName = $"{tableName}.{columnName}",
                            ObjectType = "Computed Column",
                            Details = $"Computed column expression: {expression}"
                        });
                    }
                }
                totalSchemaDependencies += Math.Max(sectionTotal, sectionObserved);
            }

            // 3c. Foreign Keys
            if (await reader.NextResultAsync(cancellationToken))
            {
                int sectionTotal = 0;
                int sectionObserved = 0;
                while (await reader.ReadAsync(cancellationToken))
                {
                    sectionObserved++;
                    if (reader.FieldCount > 4 && !reader.IsDBNull(4))
                    {
                        sectionTotal = Math.Max(sectionTotal, Convert.ToInt32(reader.GetValue(4)));
                    }

                    if (report.SchemaDependencies.Count < MaxPreflightSampleRows)
                    {
                        string parentTable = !reader.IsDBNull(2) ? reader.GetString(2) : string.Empty;
                        string referencedTable = !reader.IsDBNull(3) ? reader.GetString(3) : string.Empty;
                        report.SchemaDependencies.Add(new SchemaDependencyInfo
                        {
                            SchemaName = !reader.IsDBNull(0) ? reader.GetString(0) : string.Empty,
                            ObjectName = !reader.IsDBNull(1) ? reader.GetString(1) : string.Empty,
                            ObjectType = "Foreign Key Constraint",
                            Details = $"Table '{parentTable}' references '{referencedTable}'."
                        });
                    }
                }
                totalSchemaDependencies += Math.Max(sectionTotal, sectionObserved);
            }

            // 3d. Check Constraints
            if (await reader.NextResultAsync(cancellationToken))
            {
                int sectionTotal = 0;
                int sectionObserved = 0;
                while (await reader.ReadAsync(cancellationToken))
                {
                    sectionObserved++;
                    if (reader.FieldCount > 4 && !reader.IsDBNull(4))
                    {
                        sectionTotal = Math.Max(sectionTotal, Convert.ToInt32(reader.GetValue(4)));
                    }

                    if (report.SchemaDependencies.Count < MaxPreflightSampleRows)
                    {
                        string tableName = !reader.IsDBNull(2) ? reader.GetString(2) : string.Empty;
                        string expression = !reader.IsDBNull(3) ? reader.GetString(3) : string.Empty;
                        report.SchemaDependencies.Add(new SchemaDependencyInfo
                        {
                            SchemaName = !reader.IsDBNull(0) ? reader.GetString(0) : string.Empty,
                            ObjectName = !reader.IsDBNull(1) ? reader.GetString(1) : string.Empty,
                            ObjectType = "Check Constraint",
                            Details = $"Check constraint on table '{tableName}': {expression}"
                        });
                    }
                }
                totalSchemaDependencies += Math.Max(sectionTotal, sectionObserved);
            }

            report.TotalSchemaDependencyCount = Math.Max(totalSchemaDependencies, report.SchemaDependencies.Count);

            // 4. Target database collation
            if (await reader.NextResultAsync(cancellationToken))
            {
                if (await reader.ReadAsync(cancellationToken))
                {
                    string currentCollation = !reader.IsDBNull(0) ? reader.GetString(0) : string.Empty;
                    EvaluateCollation(databaseName, currentCollation, targetCollation, report);
                }
            }

            // 5. User table character column collation audit
            if (await reader.NextResultAsync(cancellationToken))
            {
                if (await reader.ReadAsync(cancellationToken))
                {
                    int userColumnCount = !reader.IsDBNull(0) ? Convert.ToInt32(reader.GetValue(0)) : 0;
                    int retainedColumnCount = !reader.IsDBNull(1) ? Convert.ToInt32(reader.GetValue(1)) : 0;
                    EvaluateColumnCollationCounts(userColumnCount, retainedColumnCount, targetCollation, report);
                }
            }
        }

        public virtual async Task<PreflightReport> PreflightCheckAsync(string databaseName, string backupDirectory = "", string targetCollation = "", CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var report = new PreflightReport
            {
                DatabaseName = databaseName,
                TargetCollation = targetCollation?.Trim() ?? string.Empty
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

            if (!string.IsNullOrEmpty(targetCollation))
            {
                try
                {
                    ValidateCollationFormat(targetCollation);

                    HashSet<string>? cachedCollations;
                    lock (_collationCacheLock)
                    {
                        cachedCollations = _cachedSupportedCollations;
                    }

                    if (cachedCollations == null || cachedCollations.Count == 0)
                    {
                        try
                        {
                            var collations = await GetSupportedCollationsAsync(cancellationToken);
                            lock (_collationCacheLock)
                            {
                                if (_cachedSupportedCollations == null || _cachedSupportedCollations.Count == 0)
                                {
                                    _cachedSupportedCollations = new HashSet<string>(collations, StringComparer.OrdinalIgnoreCase);
                                }
                                cachedCollations = _cachedSupportedCollations;
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Failed to check target collation in sys.fn_helpcollations(): {ex.Message}");
                        }
                    }

                    if (cachedCollations != null && cachedCollations.Count > 0)
                    {
                        if (!cachedCollations.Contains(targetCollation))
                        {
                            report.ValidationErrors.Add($"Target collation '{targetCollation}' is not supported by SQL Server.");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    report.ValidationErrors.Add(ex.Message);
                }
            }

            // Short-circuit: do not open database connection or run queries if validation failed
            if (report.ValidationErrors.Count > 0)
            {
                return report;
            }

            var safeDbName = EscapeIdentifier(databaseName);
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await OpenConnectionAsync(connection, cancellationToken);

                    var batchedQuery = BuildPreflightBatchedQuery(safeDbName);

                    using (var command = new SqlCommand(batchedQuery, connection))
                    {
                        command.Parameters.AddWithValue("@DbName", databaseName);
                        command.Parameters.AddWithValue("@TargetCollation", targetCollation?.Trim() ?? string.Empty);
                        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            await PopulatePreflightReportFromReaderAsync(reader, databaseName, targetCollation, report, cancellationToken);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo($"Preflight check for {databaseName} was cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInfo($"Preflight check for {databaseName} was cancelled.");
                    throw new OperationCanceledException(cancellationToken);
                }

                _logger.LogError($"Preflight check failed for {databaseName}", ex);
                report.ValidationErrors.Add($"Database connection or pre-flight query error: {ex.Message}");
            }

            return report;
        }

        public virtual async Task<string> BackupDatabaseAsync(string databaseName, string backupDirectory, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            ValidateBackupDirectoryPath(backupDirectory);
            try
            {
                // Note: The backupDirectory must exist on the SQL Server machine, not the client running this app.
                var safeDbName = EscapeIdentifier(databaseName);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var safeFileName = string.Join("_", databaseName.Split(Path.GetInvalidFileNameChars()));
                var backupFile = Path.Combine(backupDirectory, $"{safeFileName}_{timestamp}.bak");

                using (var connection = new SqlConnection(_connectionString))
                {
                    AttachProgressHandler(connection, progress);
                    await OpenConnectionAsync(connection, cancellationToken);
                    var query = $"BACKUP DATABASE [{safeDbName}] TO DISK = @BackupFile WITH FORMAT, INIT, STATS = 10;";
                    await ExecuteNonQueryAsync(connection, query, 3600, cancellationToken, new SqlParameter("@BackupFile", backupFile));
                }
                _logger.LogInfo($"Successfully backed up {databaseName} to {backupFile}");
                return backupFile;
            }
            catch (SqlException ex) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInfo($"Backup cancelled for database: {databaseName}");
                throw new OperationCanceledException($"Backup cancelled for database: {databaseName}", ex, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo($"Backup cancelled for database: {databaseName}");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Backup failed for database: {databaseName}", ex);
                throw;
            }
        }

        public virtual async Task RestoreDatabaseAsync(string databaseName, string backupFile, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            var safeDbName = EscapeIdentifier(databaseName);
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    AttachProgressHandler(connection, progress);
                    await OpenConnectionAsync(connection, cancellationToken);
                    await ExecuteNonQueryAsync(connection, "USE [master];", 3600, cancellationToken);
                    await SetDatabaseUserModeAsync(connection, databaseName, "SINGLE_USER", cancellationToken);
                    var restoreQuery = $"RESTORE DATABASE [{safeDbName}] FROM DISK = @BackupFile WITH REPLACE, STATS = 10;";
                    await ExecuteNonQueryAsync(connection, restoreQuery, 3600, cancellationToken, new SqlParameter("@BackupFile", backupFile));
                    await SetDatabaseUserModeAsync(connection, databaseName, "MULTI_USER", CancellationToken.None);
                }
                _logger.LogInfo($"Successfully restored {databaseName} from {backupFile}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Restore failed for database: {databaseName} from {backupFile}", ex);
                // Attempt emergency MULTI_USER reset if batch execution failed
                try
                {
                    await TrySetMultiUserAsync(databaseName, CancellationToken.None);
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

        public virtual async Task AlterCollationAsync(string databaseName, string newCollation, CancellationToken cancellationToken = default)
        {
            ValidateCollationFormat(newCollation);

            var safeDbName = EscapeIdentifier(databaseName);
            bool singleUserAttempted = false;

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await OpenConnectionAsync(connection, cancellationToken);
                    singleUserAttempted = true;
                    await SetDatabaseUserModeAsync(connection, databaseName, "SINGLE_USER", cancellationToken);
                    var query = $"ALTER DATABASE [{safeDbName}] COLLATE {newCollation};";
                    await ExecuteNonQueryAsync(connection, query, 3600, cancellationToken);
                    await SetDatabaseUserModeAsync(connection, databaseName, "MULTI_USER", CancellationToken.None);
                }
                _logger.LogInfo($"Successfully changed collation of {databaseName} to {newCollation}");
            }
            catch (SqlException ex) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInfo($"Collation alteration cancelled for database: {databaseName}");
                if (singleUserAttempted)
                {
                    try
                    {
                        await TrySetMultiUserAsync(databaseName, CancellationToken.None);
                    }
                    catch (Exception resetEx)
                    {
                        _logger.LogError($"Emergency MULTI_USER reset failed following cancellation on {databaseName}", resetEx);
                    }
                }
                throw new OperationCanceledException($"Collation alteration cancelled for database: {databaseName}", ex, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo($"Collation alteration cancelled for database: {databaseName}");
                if (singleUserAttempted)
                {
                    try
                    {
                        await TrySetMultiUserAsync(databaseName, CancellationToken.None);
                    }
                    catch (Exception resetEx)
                    {
                        _logger.LogError($"Emergency MULTI_USER reset failed following cancellation on {databaseName}", resetEx);
                    }
                }
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to alter collation for database: {databaseName}", ex);
                // Attempt to put it back to multi-user if it failed mid-way
                try
                {
                    await TrySetMultiUserAsync(databaseName, CancellationToken.None);
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

        public virtual async Task TrySetMultiUserAsync(string databaseName, CancellationToken cancellationToken = default)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await OpenConnectionAsync(connection, CancellationToken.None);
                    await SetDatabaseUserModeAsync(connection, databaseName, "MULTI_USER", CancellationToken.None);
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

        internal Func<SqlCommand, CancellationToken, Task<int>>? NonQueryCommandExecutor { get; set; }

        protected internal virtual Task OpenConnectionAsync(
            SqlConnection connection,
            CancellationToken cancellationToken = default)
        {
            return connection.OpenAsync(cancellationToken);
        }

        protected virtual Task<int> ExecuteCommandNonQueryAsync(SqlCommand command, CancellationToken cancellationToken)
        {
            return command.ExecuteNonQueryAsync(cancellationToken);
        }

        protected internal virtual async Task<int> ExecuteNonQueryAsync(
            SqlConnection connection,
            string commandText,
            int timeoutSeconds = 3600,
            CancellationToken cancellationToken = default,
            params SqlParameter[] parameters)
        {
            using (var command = new SqlCommand(commandText, connection))
            {
                command.CommandTimeout = timeoutSeconds;
                if (parameters != null && parameters.Length > 0)
                {
                    command.Parameters.AddRange(parameters);
                }
                try
                {
                    if (NonQueryCommandExecutor != null)
                    {
                        return await NonQueryCommandExecutor(command, cancellationToken);
                    }

                    return await ExecuteCommandNonQueryAsync(command, cancellationToken);
                }
                catch (SqlException ex) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException("SQL command execution was cancelled.", ex, cancellationToken);
                }
            }
        }

        protected internal virtual async Task SetDatabaseUserModeAsync(
            SqlConnection connection,
            string databaseName,
            string mode,
            CancellationToken cancellationToken = default)
        {
            var safeDbName = EscapeIdentifier(databaseName);
            bool isSingleUser = string.Equals(mode, "SINGLE_USER", StringComparison.OrdinalIgnoreCase);
            string query = isSingleUser
                ? $"ALTER DATABASE [{safeDbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;"
                : $"ALTER DATABASE [{safeDbName}] SET MULTI_USER;";

            var effectiveToken = isSingleUser ? cancellationToken : CancellationToken.None;
            await ExecuteNonQueryAsync(connection, query, 3600, effectiveToken);
        }

        private static void AttachProgressHandler(SqlConnection connection, IProgress<int>? progress)
        {
            if (progress == null)
            {
                return;
            }

            connection.InfoMessage += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Message))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(e.Message, @"(\d+)\s*percent\s+processed", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int percent))
                    {
                        progress.Report(percent);
                    }
                }
            };
        }
    }
}
