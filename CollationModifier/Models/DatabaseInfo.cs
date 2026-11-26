using CollationModifier.ViewModels;

namespace CollationModifier.Models
{
    public class DatabaseInfo : ViewModelBase
    {
        private string _name = string.Empty;
        private string _collation = string.Empty;
        private string _compatibilityLevel = string.Empty;
        private string _owner = string.Empty;
        private string _size = string.Empty;
        private int _userCharacterColumnCount;
        private string _userCharacterColumns = "0";
        private int _retainedColumnCount;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string Collation
        {
            get => _collation;
            set => SetProperty(ref _collation, value);
        }

        public string CompatibilityLevel
        {
            get => _compatibilityLevel;
            set => SetProperty(ref _compatibilityLevel, value);
        }

        public string Owner
        {
            get => _owner;
            set => SetProperty(ref _owner, value);
        }

        public string Size
        {
            get => _size;
            set => SetProperty(ref _size, value);
        }

        public int UserCharacterColumnCount
        {
            get => _userCharacterColumnCount;
            set
            {
                int normalized = value < 0 ? 0 : value;
                if (SetProperty(ref _userCharacterColumnCount, normalized))
                {
                    UserCharacterColumns = normalized.ToString();
                }
                else if (_userCharacterColumns != normalized.ToString() && _userCharacterColumns != "Unknown")
                {
                    UserCharacterColumns = normalized.ToString();
                }
            }
        }

        public string UserCharacterColumns
        {
            get => _userCharacterColumns;
            set => SetProperty(ref _userCharacterColumns, value);
        }

        public int RetainedColumnCount
        {
            get => _retainedColumnCount;
            set => SetProperty(ref _retainedColumnCount, value < 0 ? 0 : value);
        }
    }
}
