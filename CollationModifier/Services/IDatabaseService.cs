using CollationModifier.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CollationModifier.Services
{
    public interface IDatabaseService
    {
        void SetConnectionString(string server, string authType, string username, string password);
        Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
        Task<List<DatabaseInfo>> GetDatabasesAsync(CancellationToken cancellationToken = default);
        Task<string?> GetDefaultBackupDirectoryAsync(CancellationToken cancellationToken = default);
        Task<List<string>> GetSupportedCollationsAsync(CancellationToken cancellationToken = default);
        Task<PreflightReport> PreflightCheckAsync(string databaseName, string backupDirectory = "", CancellationToken cancellationToken = default);
        Task<string> BackupDatabaseAsync(string databaseName, string backupDirectory, CancellationToken cancellationToken = default);
        Task RestoreDatabaseAsync(string databaseName, string backupFile, CancellationToken cancellationToken = default);
        Task AlterCollationAsync(string databaseName, string newCollation, CancellationToken cancellationToken = default);
    }
}
