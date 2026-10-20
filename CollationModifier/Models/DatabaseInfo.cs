namespace CollationModifier.Models
{
    public class DatabaseInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Collation { get; set; } = string.Empty;
        public string CompatibilityLevel { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
    }
}
