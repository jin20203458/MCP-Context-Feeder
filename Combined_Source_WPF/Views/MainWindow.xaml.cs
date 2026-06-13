using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Win32;

using Combined_Source_WPF.Helpers;
using Combined_Source_WPF.Service;

namespace Combined_Source_WPF.Views
{
    public partial class MainWindow : Window
    {
        private readonly string presetFilePath = "presets.json";
        private List<MergePreset> presets = new List<MergePreset>();
        private List<ReferenceDocument> currentReferences = new List<ReferenceDocument>();

        // 로컬 HTTP 서버 관리 객체
        private LocalContextServer? _server;

        public MainWindow()
        {
            InitializeComponent();
            this.Closing += MainWindow_Closing;
        }
 
        // ==========================================
        // Window Control Buttons (Custom Title Bar)
        // ==========================================
        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }
 
        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
                PathMaximize.Data = System.Windows.Media.Geometry.Parse("M 0 0 L 10 0 L 10 10 L 0 10 Z");
            }
            else
            {
                this.WindowState = WindowState.Maximized;
                PathMaximize.Data = System.Windows.Media.Geometry.Parse("M 2 0 L 10 0 L 10 8 L 8 8 L 8 10 L 0 10 L 0 2 L 2 2 Z M 2 2 L 8 2 L 8 8 L 2 8 Z");
            }
        }
 
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // ==========================================
        // 1. 프리셋(설정) 관련 로직
        // ==========================================
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadPresetsFromFile();
            StartServer(); // 에이전트가 바로 사용할 수 있도록 시작 시 자동 구동
        }

        private void LoadPresetsFromFile()
        {
            try
            {
                if (File.Exists(presetFilePath))
                {
                    string json = File.ReadAllText(presetFilePath);
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        presets.Clear();
                        if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement element in doc.RootElement.EnumerateArray())
                            {
                                var preset = new MergePreset();
                                if (element.TryGetProperty("PresetName", out JsonElement nameProp))
                                {
                                    preset.PresetName = nameProp.GetString() ?? string.Empty;
                                }

                                // 새 포맷 확인 (ReferenceDocuments 필드가 있는지)
                                if (element.TryGetProperty("ReferenceDocuments", out JsonElement refDocsProp) && refDocsProp.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (JsonElement docEl in refDocsProp.EnumerateArray())
                                    {
                                        string path = docEl.TryGetProperty("Path", out JsonElement p) ? p.GetString() ?? string.Empty : string.Empty;
                                        string type = docEl.TryGetProperty("Type", out JsonElement t) ? t.GetString() ?? "File" : "File";
                                        if (!string.IsNullOrEmpty(path))
                                        {
                                            preset.ReferenceDocuments.Add(new ReferenceDocument { Path = path, Type = type });
                                        }
                                    }
                                }
                                else
                                {
                                    // 옛 포맷 마이그레이션 (호환성 보장)
                                    bool isSelective = element.TryGetProperty("IsSelectiveMerge", out JsonElement selProp) && selProp.GetBoolean();
                                    if (isSelective && element.TryGetProperty("SelectedFiles", out JsonElement filesProp) && filesProp.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (JsonElement fileEl in filesProp.EnumerateArray())
                                        {
                                            string filePath = fileEl.GetString() ?? string.Empty;
                                            if (!string.IsNullOrEmpty(filePath))
                                            {
                                                preset.ReferenceDocuments.Add(new ReferenceDocument { Path = filePath, Type = "File" });
                                            }
                                        }
                                    }
                                    else if (element.TryGetProperty("SourcePath", out JsonElement srcProp))
                                    {
                                        string srcPath = srcProp.GetString() ?? string.Empty;
                                        if (!string.IsNullOrEmpty(srcPath))
                                        {
                                            preset.ReferenceDocuments.Add(new ReferenceDocument { Path = srcPath, Type = "Directory" });
                                        }
                                    }
                                }
                                presets.Add(preset);
                            }
                        }
                    }
                    RefreshPresetComboBox();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"프리셋을 불러오는 중 오류가 발생했습니다.\n{ex.Message}");
                presets = new List<MergePreset>();
            }
        }

        private void RefreshPresetComboBox()
        {
            CmbPresets.ItemsSource = presets.Select(p => p.PresetName).ToList();
        }

        private void CmbPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbPresets.SelectedItem is string selectedName)
            {
                var preset = presets.FirstOrDefault(p => p.PresetName == selectedName);
                if (preset != null)
                {
                    currentReferences = new List<ReferenceDocument>(preset.ReferenceDocuments);
                    RefreshReferencesList();
                }
            }
        }

        private void BtnSavePreset_Click(object sender, RoutedEventArgs e)
        {
            string presetName = CmbPresets.Text.Trim();
            if (string.IsNullOrEmpty(presetName))
            {
                MessageBox.Show("저장할 프리셋 이름을 콤보박스에 입력해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var existing = presets.FirstOrDefault(p => p.PresetName == presetName);
            if (existing != null)
            {
                existing.ReferenceDocuments = new List<ReferenceDocument>(currentReferences);
            }
            else
            {
                presets.Add(new MergePreset
                {
                    PresetName = presetName,
                    ReferenceDocuments = new List<ReferenceDocument>(currentReferences)
                });
            }
            SavePresetsToFile();
            RefreshPresetComboBox();
            CmbPresets.SelectedItem = presetName;
            MessageBox.Show($"'{presetName}' 프리셋이 저장되었습니다.", "저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnDeletePreset_Click(object sender, RoutedEventArgs e)
        {
            string presetName = CmbPresets.Text.Trim();
            var preset = presets.FirstOrDefault(p => p.PresetName == presetName);
            if (preset != null)
            {
                presets.Remove(preset);
                SavePresetsToFile();
                RefreshPresetComboBox();
                CmbPresets.Text = "";
                MessageBox.Show($"'{presetName}' 프리셋이 삭제되었습니다.", "삭제 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void SavePresetsToFile()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(presets, options);
                File.WriteAllText(presetFilePath, json);
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 프리셋 저장 중 오류: {ex.Message}");
            }
        }

        // ==========================================
        // 2. 참조 문서 및 폴더 관리 로직
        // ==========================================
        private void BtnAddFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "참조할 파일 선택",
                Multiselect = true,
                Filter = "모든 파일 (*.*)|*.*|소스 코드 (*.cs;*.xaml;*.js;*.ts;*.py;*.txt)|*.cs;*.xaml;*.js;*.ts;*.py;*.txt"
            };
            if (dialog.ShowDialog() == true)
            {
                foreach (string fileName in dialog.FileNames)
                {
                    if (!currentReferences.Any(r => r.Path.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
                    {
                        currentReferences.Add(new ReferenceDocument { Path = fileName, Type = "File" });
                    }
                }
                RefreshReferencesList();
                SaveCurrentPresetReferencesIfSelected();
            }
        }

        private void BtnAddFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "참조할 폴더 선택"
            };
            if (dialog.ShowDialog() == true)
            {
                string folderPath = dialog.FolderName;
                if (!currentReferences.Any(r => r.Path.Equals(folderPath, StringComparison.OrdinalIgnoreCase)))
                {
                    currentReferences.Add(new ReferenceDocument { Path = folderPath, Type = "Directory" });
                }
                RefreshReferencesList();
                SaveCurrentPresetReferencesIfSelected();
            }
        }

        private void BtnRemoveReference_Click(object sender, RoutedEventArgs e)
        {
            if (LstReferences.SelectedItems.Count > 0)
            {
                var selectedList = LstReferences.SelectedItems.Cast<ReferenceDocument>().ToList();
                foreach (var selected in selectedList)
                {
                    currentReferences.Remove(selected);
                }
                RefreshReferencesList();
                SaveCurrentPresetReferencesIfSelected();
            }
            else
            {
                MessageBox.Show("삭제할 참조 항목을 목록에서 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SaveCurrentPresetReferencesIfSelected()
        {
            string presetName = CmbPresets.Text.Trim();
            var currentPreset = presets.FirstOrDefault(p => p.PresetName == presetName);
            if (currentPreset != null)
            {
                currentPreset.ReferenceDocuments = new List<ReferenceDocument>(currentReferences);
                SavePresetsToFile();
            }
        }

        private void RefreshReferencesList()
        {
            LstReferences.ItemsSource = null;
            LstReferences.ItemsSource = currentReferences;
            EstimateTokens();
        }

        // ==========================================
        // 3. 토큰 예측기 (Token Estimator)
        // ==========================================
        private async void EstimateTokens()
        {
            TxtEstimatedTokens.Text = "Calculating...";
            
            var refsCopy = currentReferences.ToList();
            
            int totalChars = await Task.Run(() =>
            {
                int chars = 0;
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var reference in refsCopy)
                {
                    if (reference.Type == "File")
                    {
                        if (File.Exists(reference.Path))
                        {
                            chars += GetCharCount(reference.Path, visited);
                        }
                    }
                    else if (reference.Type == "Directory")
                    {
                        if (Directory.Exists(reference.Path))
                        {
                            try
                            {
                                var files = Directory.EnumerateFiles(reference.Path, "*.*", SearchOption.AllDirectories);
                                foreach (var file in files)
                                {
                                    if (IsExcludedPath(file)) continue;
                                    if (IsTextFile(file))
                                    {
                                        chars += GetCharCount(file, visited);
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
                return chars;
            });

            double estTokens = Math.Ceiling(totalChars / 3.0);
            TxtEstimatedTokens.Text = $"{estTokens:N0} tokens ({totalChars:N0} chars)";
        }

        private int GetCharCount(string filePath, HashSet<string> visited)
        {
            string fullPath = Path.GetFullPath(filePath);
            if (visited.Contains(fullPath)) return 0;
            visited.Add(fullPath);

            try
            {
                var info = new FileInfo(fullPath);
                if (info.Length > 1024 * 1024) return 0; // 1MB 제한
                
                string content = File.ReadAllText(fullPath, Encoding.UTF8);
                return content.Length;
            }
            catch
            {
                return 0;
            }
        }

        private bool IsExcludedPath(string filePath)
        {
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

        private bool IsTextFile(string filePath)
        {
            try
            {
                var info = new FileInfo(filePath);
                if (info.Length > 1024 * 1024) return false;

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
            catch
            {
                return false;
            }
        }

        // ==========================================
        // 4. 서버 제어 및 로깅 로직
        // ==========================================
        private void BtnToggleServer_Click(object sender, RoutedEventArgs e)
        {
            if (_server != null && _server.IsRunning)
            {
                StopServer();
            }
            else
            {
                StartServer();
            }
        }

        private void StartServer()
        {
            try
            {
                _server?.Stop();

                _server = new LocalContextServer(
                    getTaskIntent: () => {
                        string intent = string.Empty;
                        Dispatcher.Invoke(() => { intent = TxtTaskIntent.Text; });
                        return intent;
                    },
                    getReferenceDocuments: () => {
                        List<ReferenceDocument> docs = new List<ReferenceDocument>();
                        Dispatcher.Invoke(() => {
                            docs = currentReferences.ToList();
                        });
                        return docs;
                    },
                    logAction: (msg) => {
                        LogMessage(msg);
                    }
                );

                _server.Start();

                BtnToggleServer.Content = "⏹️ 서버 중지";
                BtnToggleServer.Background = (System.Windows.Media.Brush)Application.Current.FindResource("VSCode.Brushes.Accent.Blue");
                TxtServerAddress.Text = $"서버 동작 중: http://127.0.0.1:{_server.Port}/sse";
                TxtServerAddress.Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("VSCode.Brushes.Accent.Blue");
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 서버 시작 오류: {ex.Message}");
                MessageBox.Show($"서버를 시작하지 못했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                StopServer();
            }
        }

        private void StopServer()
        {
            if (_server != null)
            {
                _server.Stop();
                _server = null;
            }

            BtnToggleServer.Content = "▶️ 서버 시작";
            BtnToggleServer.Background = (System.Windows.Media.Brush)Application.Current.FindResource("VSCode.Brushes.Button.SecondaryBackground");
            TxtServerAddress.Text = "서버 중지됨";
            TxtServerAddress.Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("VSCode.Brushes.Text.Muted");
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            StopServer();
        }

        private void LogMessage(string message)
        {
            Dispatcher.Invoke(() => {
                TxtLog.AppendText(message + Environment.NewLine);
                TxtLog.ScrollToEnd();
            });
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                bool added = false;
                foreach (string path in files)
                {
                    if (File.Exists(path))
                    {
                        if (!currentReferences.Any(r => r.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                        {
                            currentReferences.Add(new ReferenceDocument { Path = path, Type = "File" });
                            added = true;
                        }
                    }
                    else if (Directory.Exists(path))
                    {
                        if (!currentReferences.Any(r => r.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                        {
                            currentReferences.Add(new ReferenceDocument { Path = path, Type = "Directory" });
                            added = true;
                        }
                    }
                }
                if (added)
                {
                    RefreshReferencesList();
                    SaveCurrentPresetReferencesIfSelected();
                }
            }
        }
    }

    // ==========================================
    // 5. 데이터 모델 정의
    // ==========================================
    public class ReferenceDocument
    {
        public string Name => System.IO.Path.GetFileName(Path) is string n && !string.IsNullOrEmpty(n) ? n : Path;
        public string Path { get; set; } = string.Empty;
        public string Type { get; set; } = "File"; // "File" or "Directory"
    }

    public class MergePreset
    {
        public string PresetName { get; set; } = string.Empty;
        public List<ReferenceDocument> ReferenceDocuments { get; set; } = new List<ReferenceDocument>();
    }
}