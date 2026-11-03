using System.Collections.Generic;

namespace CollationModifier.Models
{
    public class ServerInitializationData
    {
        public string? DefaultBackupDirectory { get; set; }

        public string? BackupDirectory
        {
            get => DefaultBackupDirectory;
            set => DefaultBackupDirectory = value;
        }

        public List<DatabaseInfo> Databases { get; set; } = new();

        public List<string> SupportedCollations { get; set; } = new();

        public List<string> Collations
        {
            get => SupportedCollations;
            set => SupportedCollations = value;
        }
    }
}
