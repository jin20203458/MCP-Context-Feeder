using System.IO;

namespace Combined_Source_WPF.Models
{
    public class ReferenceDocument
    {
        public string Name => System.IO.Path.GetFileName(this.Path) is string n && !string.IsNullOrEmpty(n) ? n : this.Path;
        public string Path { get; set; } = string.Empty;
        public string Type { get; set; } = "File"; // "File" or "Directory"
    }
}
