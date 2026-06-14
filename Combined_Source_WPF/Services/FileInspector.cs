using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Combined_Source_WPF.Services
{
    public class FileInspector : IFileInspector
    {
        public bool IsExcludedPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return true;
            
            var parts = filePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var p = part.ToLower();
                if (p == "bin" || p == "obj" || p == ".git" || p == ".vs" || p == "node_modules" || p == "dist" || p == "out")
                {
                    return true;
                }
            }
            return false;
        }

        public bool IsTextFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;

                var info = new FileInfo(filePath);
                if (info.Length > 1024 * 1024) return false; // 1MB 제한

                string ext = Path.GetExtension(filePath).ToLower();
                string[] textExtensions = new[]
                {
                    ".cs", ".xaml", ".xml", ".json", ".txt", ".md", ".js", ".ts", ".html",
                    ".css", ".py", ".java", ".cpp", ".h", ".c", ".go", ".rs", ".yaml", ".yml",
                    ".ini", ".conf", ".sh", ".bat", ".ps1", ".sql", ".config", ".csproj", ".sln"
                };
                if (textExtensions.Contains(ext)) return true;

                using (var stream = File.OpenRead(filePath))
                {
                    byte[] buffer = new byte[1024];
                    int read = stream.Read(buffer, 0, buffer.Length);
                    for (int i = 0; i < read; i++)
                    {
                        if (buffer[i] == 0) return false;
                    }
                }
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        public int GetCharCount(string filePath, HashSet<string> visited)
        {
            string fullPath = Path.GetFullPath(filePath);
            if (visited.Contains(fullPath)) return 0;
            visited.Add(fullPath);

            try
            {
                if (!IsTextFile(fullPath)) return 0;

                string content = File.ReadAllText(fullPath, System.Text.Encoding.UTF8);
                return content.Length;
            }
            catch (IOException)
            {
                return 0;
            }
            catch (UnauthorizedAccessException)
            {
                return 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
