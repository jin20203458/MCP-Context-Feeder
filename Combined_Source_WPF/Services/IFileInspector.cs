using System.Collections.Generic;

namespace Combined_Source_WPF.Services
{
    public interface IFileInspector
    {
        bool IsExcludedPath(string filePath);
        bool IsTextFile(string filePath);
        int GetCharCount(string filePath, HashSet<string> visited);
    }
}
