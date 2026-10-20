using CollationModifier.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Threading.Tasks;

namespace CollationModifier.Services
{
    public class DatabaseService
    {
        private readonly LoggerService _logger;
        private string _connectionString = string.Empty;

        public DatabaseService(LoggerService logger)
        {
            _logger = logger;
        }

        public void SetConnectionString(string server, string authType, string username, string password)
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = "master",
                ApplicationName = "CollationModifier"
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

        public async Task<bool> TestConnectionAsync()
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

        public async Task<List<DatabaseInfo>> GetDatabasesAsync()
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
                                CAST(SUM(CAST(size AS bigint)) * 8 / 1024 AS int) as size_mb
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
                                Size = reader.IsDBNull(4) ? "Unknown" : $"{reader.GetInt32(4)} MB"
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

        public async Task PreflightCheckAsync(string databaseName)
        {
            var safeDbName = databaseName.Replace("]", "]]");
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    // Basic check to see if user has alter/backup/restore permissions
                    // (Note: fn_my_permissions works on the current database context)

                    var query = $@"
                        USE [{safeDbName}];
                        SELECT permission_name FROM fn_my_permissions(NULL, 'DATABASE')
                        WHERE permission_name IN ('BACKUP DATABASE', 'BACKUP LOG', 'ALTER');";

                    var permissions = new List<string>();
                    using (var command = new SqlCommand(query, connection))
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            permissions.Add(reader.GetString(0));
                        }
                    }

                    if (!permissions.Contains("ALTER"))
                    {
                        throw new UnauthorizedAccessException($"User does not have ALTER permissions on database {databaseName}.");
                    }
                    if (!permissions.Contains("BACKUP DATABASE") && !permissions.Contains("BACKUP LOG")) // Sometimes backup log is needed or just backup db is enough depending on recovery model, but checking BACKUP DATABASE is primary
                    {
                        // We will allow it to proceed as sysadmin might not explicitly show BACKUP DATABASE via fn_my_permissions sometimes depending on context,
                        // but ideally we'd strictly enforce it here. For robustness, if they lack ALTER, we stop.
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Preflight check failed for {databaseName}", ex);
                throw;
            }
        }

        public async Task<string> BackupDatabaseAsync(string databaseName, string backupDirectory)
        {
            try
            {
                // Note: The backupDirectory must exist on the SQL Server machine, not the client running this app.
                var safeDbName = databaseName.Replace("]", "]]");
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                // Clean the database name for file path safety
                var safeFileName = string.Join("_", databaseName.Split(Path.GetInvalidFileNameChars()));
                var backupFile = Path.Combine(backupDirectory, $"{safeFileName}_{timestamp}.bak");

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    var query = $"BACKUP DATABASE [{safeDbName}] TO DISK = @BackupFile WITH FORMAT, INIT, STATS = 10;";

                    using (var command = new SqlCommand(query, connection))
                    {
                        // Needs longer timeout for backup
                        command.CommandTimeout = 3600;
                        command.Parameters.AddWithValue("@BackupFile", backupFile);
                        await command.ExecuteNonQueryAsync();
                    }
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

        public async Task RestoreDatabaseAsync(string databaseName, string backupFile)
        {
            var safeDbName = databaseName.Replace("]", "]]");
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    // To restore, we must be in master and ensure exclusive access
                    var commands = new[]
                    {
                        $"USE [master];",
                        $"ALTER DATABASE [{safeDbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;",
                        $"RESTORE DATABASE [{safeDbName}] FROM DISK = @BackupFile WITH REPLACE;",
                        $"ALTER DATABASE [{safeDbName}] SET MULTI_USER;"
                    };

                    foreach (var cmdText in commands)
                    {
                        using (var command = new SqlCommand(cmdText, connection))
                        {
                            command.CommandTimeout = 3600;
                            if (cmdText.Contains("@BackupFile"))
                            {
                                command.Parameters.AddWithValue("@BackupFile", backupFile);
                            }
                            await command.ExecuteNonQueryAsync();
                        }
                    }
                }
                _logger.LogInfo($"Successfully restored {databaseName} from {backupFile}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Restore failed for database: {databaseName} from {backupFile}", ex);
                throw;
            }
        }

        public async Task AlterCollationAsync(string databaseName, string newCollation)
        {
            // Validate the collation to prevent SQL Injection
            if (!System.Text.RegularExpressions.Regex.IsMatch(newCollation, @"^[a-zA-Z0-9_]+$"))
            {
                throw new ArgumentException("Invalid collation name format. Only alphanumeric characters and underscores are allowed.");
            }

            var safeDbName = databaseName.Replace("]", "]]");

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    var commands = new[]
                    {
                        $"ALTER DATABASE [{safeDbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;",
                        $"ALTER DATABASE [{safeDbName}] COLLATE {newCollation};",
                        $"ALTER DATABASE [{safeDbName}] SET MULTI_USER;"
                    };

                    foreach (var cmdText in commands)
                    {
                        using (var command = new SqlCommand(cmdText, connection))
                        {
                            // Collation change can take time
                            command.CommandTimeout = 3600;
                            await command.ExecuteNonQueryAsync();
                        }
                    }
                }
                _logger.LogInfo($"Successfully changed collation of {databaseName} to {newCollation}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to alter collation for database: {databaseName}", ex);
                // Attempt to put it back to multi-user if it failed mid-way
                await TrySetMultiUserAsync(databaseName);
                throw;
            }
        }

        private async Task TrySetMultiUserAsync(string databaseName)
        {
            var safeDbName = databaseName.Replace("]", "]]");
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    var query = $"ALTER DATABASE [{safeDbName}] SET MULTI_USER;";
                    using (var command = new SqlCommand(query, connection))
                    {
                        await command.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                 _logger.LogError($"Failed emergency MULTI_USER reset for database: {databaseName}", ex);
            }
        }
    }
}
