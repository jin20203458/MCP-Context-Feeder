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

namespace CodeMergerUI
{
    public partial class MainWindow : Window
    {
        private readonly string presetFilePath = "presets.json";
        private List<MergePreset> presets = new List<MergePreset>();
        private string lastMergedFilePath = string.Empty;

        // 현재 선택된 파일 목록을 유지할 변수 추가
        private List<string> currentSelectedFiles = new List<string>();

        // 로컬 HTTP 서버 관리 객체
        private LocalContextServer? _server;

        public MainWindow()
        {
            InitializeComponent();
            this.Closing += MainWindow_Closing;
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
                    presets = JsonSerializer.Deserialize<List<MergePreset>>(json) ?? new List<MergePreset>();
                    RefreshPresetComboBox();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"프리셋을 불러오는 중 오류가 발생했습니다.\n{ex.Message}");
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
                    TxtSourcePath.Text = preset.SourcePath;
                    TxtExtensions.Text = preset.Extensions;
                    TxtExcludedFolders.Text = preset.ExcludedFolders;
                    ChkSelectiveMerge.IsChecked = preset.IsSelectiveMerge;

                    // 프리셋의 파일 선택 목록 동기화
                    currentSelectedFiles = preset.SelectedFiles != null ? new List<string>(preset.SelectedFiles) : new List<string>();
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
                existing.SourcePath = TxtSourcePath.Text;
                existing.OutputPath = "";
                existing.Extensions = TxtExtensions.Text;
                existing.ExcludedFolders = TxtExcludedFolders.Text;
                existing.IsSelectiveMerge = ChkSelectiveMerge.IsChecked == true;

                // 선택 파일 갱신 추가
                existing.SelectedFiles = currentSelectedFiles;
            }
            else
            {
                presets.Add(new MergePreset
                {
                    PresetName = presetName,
                    SourcePath = TxtSourcePath.Text,
                    OutputPath = "",
                    Extensions = TxtExtensions.Text,
                    ExcludedFolders = TxtExcludedFolders.Text,
                    IsSelectiveMerge = ChkSelectiveMerge.IsChecked == true,

                    // 선택 파일 갱신 추가
                    SelectedFiles = currentSelectedFiles
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
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(presets, options);
            File.WriteAllText(presetFilePath, json);
        }

        // ==========================================
        // 2. 폴더 찾기 로직
        // ==========================================
        private void BtnBrowseSource_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "소스 코드가 들어있는 폴더를 선택하세요", InitialDirectory = TxtSourcePath.Text };
            if (dialog.ShowDialog() == true) TxtSourcePath.Text = dialog.FolderName;
        }

        // ==========================================
        // 3. 파일 병합 및 클립보드 복사 로직
        // ==========================================

        // 파일 필터링 공통 메서드 추가
        private List<string> GetFilteredFiles(string sourceDir, string[] allowedExtensions, string[] excludedFolders)
        {
            return Directory.EnumerateFiles(sourceDir, "*.*", SearchOption.AllDirectories)
                .Where(filePath =>
                {
                    string ext = Path.GetExtension(filePath).ToLower();
                    bool hasValidExt = allowedExtensions.Contains(ext);
                    bool isExcluded = excludedFolders.Any(ex => filePath.IndexOf($@"\{ex}\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                filePath.IndexOf($@"/{ex}/", StringComparison.OrdinalIgnoreCase) >= 0);
                    return hasValidExt && !isExcluded;
                }).ToList();
        }

        // 새롭게 분리된 파일 선택 버튼 이벤트
        private void BtnSelectFiles_Click(object sender, RoutedEventArgs e)
        {
            string sourceDir = TxtSourcePath.Text.Trim();
            if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir))
            {
                MessageBox.Show("올바른 소스 폴더 경로를 먼저 설정해주세요.", "경로 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string[] allowedExtensions = TxtExtensions.Text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ext => ext.Trim().ToLower()).ToArray();
            string[] excludedFolders = TxtExcludedFolders.Text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(folder => folder.Trim()).ToArray();

            // 조건에 맞는 파일 검색
            var allFilteredFiles = GetFilteredFiles(sourceDir, allowedExtensions, excludedFolders);

            // 선택 창 열기
            var selectionWindow = new FileSelectionWindow(allFilteredFiles, currentSelectedFiles);
            selectionWindow.Owner = this;

            if (selectionWindow.ShowDialog() == true)
            {
                currentSelectedFiles = selectionWindow.FinalSelectedFiles;

                // 현재 선택된 프리셋이 있다면 프리셋에도 즉시 갱신 및 저장
                var currentPreset = presets.FirstOrDefault(p => p.PresetName == CmbPresets.Text.Trim());
                if (currentPreset != null)
                {
                    currentPreset.SelectedFiles = currentSelectedFiles;
                    SavePresetsToFile();
                }

                MessageBox.Show($"{currentSelectedFiles.Count}개의 파일이 병합 대상으로 선택되었습니다.", "선택 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

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
                    getFiles: () => {
                        List<string> files = new List<string>();
                        Dispatcher.Invoke(() => {
                            bool isSelective = ChkSelectiveMerge.IsChecked == true;
                            if (isSelective)
                            {
                                files = currentSelectedFiles.Where(f => File.Exists(f)).ToList();
                            }
                            else
                            {
                                string sourceDir = TxtSourcePath.Text.Trim();
                                if (!string.IsNullOrEmpty(sourceDir) && Directory.Exists(sourceDir))
                                {
                                    string[] allowedExtensions = TxtExtensions.Text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(ext => ext.Trim().ToLower()).ToArray();
                                    string[] excludedFolders = TxtExcludedFolders.Text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(folder => folder.Trim()).ToArray();
                                    files = GetFilteredFiles(sourceDir, allowedExtensions, excludedFolders);
                                }
                            }
                        });
                        return files;
                    },
                    logAction: (msg) => {
                        LogMessage(msg);
                    }
                );

                _server.Start();

                BtnToggleServer.Content = "⏹️ 서버 중지";
                BtnToggleServer.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)); // Red #DC3545
                TxtServerAddress.Text = $"서버 동작 중: http://127.0.0.1:{_server.Port}/sse";
                TxtServerAddress.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69)); // Green #28A745
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
            BtnToggleServer.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69)); // Green #28A745
            TxtServerAddress.Text = "서버 중지됨";
            TxtServerAddress.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 90, 158)); // Blue
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
    }

    // ==========================================
    // 4. 프리셋 데이터 모델 (수정됨)
    // ==========================================
    public class MergePreset
    {
        public string PresetName { get; set; }
        public string SourcePath { get; set; }
        public string OutputPath { get; set; }
        public string Extensions { get; set; }
        public string ExcludedFolders { get; set; }

        // 새로 추가된 프로퍼티
        public bool IsSelectiveMerge { get; set; }
        public List<string> SelectedFiles { get; set; } = new List<string>();
    }
}