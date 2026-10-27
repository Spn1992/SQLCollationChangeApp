using CollationModifier.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CollationModifier.Services
{
    public interface IDatabaseService
    {
        void SetConnectionString(string server, string authType, string username, string password);
        Task<bool> TestConnectionAsync();
        Task<List<DatabaseInfo>> GetDatabasesAsync();
        Task<List<string>> GetSupportedCollationsAsync();
        Task<string?> GetDefaultBackupDirectoryAsync();
        Task<List<DatabaseInfo>> GetDatabasesAsync();
        Task<List<string>> GetSupportedCollationsAsync();
        Task<PreflightReport> PreflightCheckAsync(string databaseName, string backupDirectory = "");
        Task<string> BackupDatabaseAsync(string databaseName, string backupDirectory);
        Task RestoreDatabaseAsync(string databaseName, string backupFile);
        Task AlterCollationAsync(string databaseName, string newCollation);
        Task TrySetMultiUserAsync(string databaseName);
    }
}
