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

        public MainWindow()
        {
            InitializeComponent();
        }

        // ==========================================
        // 1. 프리셋(설정) 관련 로직
        // ==========================================
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadPresetsFromFile();
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
                    TxtOutputPath.Text = preset.OutputPath;
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
                existing.OutputPath = TxtOutputPath.Text;
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
                    OutputPath = TxtOutputPath.Text,
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

        private void BtnBrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "합쳐진 파일을 저장할 폴더를 선택하세요", InitialDirectory = TxtOutputPath.Text };
            if (dialog.ShowDialog() == true) TxtOutputPath.Text = dialog.FolderName;
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

        private async void BtnMerge_Click(object sender, RoutedEventArgs e)
        {
            string sourceDir = TxtSourcePath.Text.Trim();
            string outputDir = TxtOutputPath.Text.Trim();

            if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir))
            {
                MessageBox.Show("올바른 소스 폴더 경로를 설정해주세요.", "경로 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrEmpty(outputDir))
            {
                MessageBox.Show("올바른 저장 폴더 경로를 설정해주세요.", "경로 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!Directory.Exists(outputDir))
            {
                try { Directory.CreateDirectory(outputDir); }
                catch { MessageBox.Show("저장 폴더를 생성할 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error); return; }
            }

            string dateTag = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string targetFolderName = new DirectoryInfo(sourceDir).Name;
            string outputFile = Path.Combine(outputDir, $"Combined_{targetFolderName}_{dateTag}.txt");

            List<string> filesToMerge;
            bool isSelectiveMerge = ChkSelectiveMerge.IsChecked == true;

            // 체크박스가 켜져있다면, 미리 선택해둔 리스트를 사용
            if (isSelectiveMerge)
            {
                if (currentSelectedFiles == null || currentSelectedFiles.Count == 0)
                {
                    MessageBox.Show("선택된 파일이 없습니다. '파일 선택...' 버튼을 눌러 병합할 파일을 먼저 선택해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                // 안전 장치: 병합 직전 실제 존재하는 파일만 필터링
                filesToMerge = currentSelectedFiles.Where(f => File.Exists(f)).ToList();
            }
            else // 체크박스가 꺼져있다면 전체 필터링 검색 진행
            {
                string[] allowedExtensions = TxtExtensions.Text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(ext => ext.Trim().ToLower()).ToArray();
                string[] excludedFolders = TxtExcludedFolders.Text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(folder => folder.Trim()).ToArray();

                filesToMerge = GetFilteredFiles(sourceDir, allowedExtensions, excludedFolders);
            }

            BtnMerge.IsEnabled = false;
            BtnCopyToClipboard.IsEnabled = false;
            TxtLog.Clear();
            LogMessage("병합 작업을 시작합니다...\n");

            // 병합 쓰기 진행
            await Task.Run(() => MergeSelectedFiles(filesToMerge, outputFile));

            if (File.Exists(lastMergedFilePath))
            {
                try
                {
                    string content = File.ReadAllText(lastMergedFilePath, Encoding.UTF8);
                    Clipboard.SetText(content);
                    LogMessage("✅ 병합된 코드가 클립보드에 자동으로 복사되었습니다.");
                }
                catch (Exception ex)
                {
                    LogMessage($"\n❌ 클립보드 자동 복사 오류: {ex.Message}");
                }
            }
            BtnMerge.IsEnabled = true;
        }

        private void MergeSelectedFiles(List<string> filesToMerge, string outputFile)
        {
            int fileCount = 0;
            try
            {
                using (StreamWriter writer = new StreamWriter(outputFile, false, new UTF8Encoding(false)))
                {
                    foreach (var file in filesToMerge)
                    {
                        writer.WriteLine($"\n# ==========================================");
                        writer.WriteLine($"# FILE: {Path.GetFileName(file)}");
                        writer.WriteLine($"# ==========================================");
                        writer.WriteLine($"// Path: {file}");
                        writer.WriteLine("");
                        writer.WriteLine(File.ReadAllText(file, Encoding.UTF8));
                        writer.WriteLine($"\n// [END OF FILE: {Path.GetFileName(file)}]");
                        writer.WriteLine("/* ------------------------------------------ */");
                        fileCount++;
                        LogMessage($"[병합 완료] {Path.GetFileName(file)}");
                    }
                }
                lastMergedFilePath = outputFile;
                LogMessage($"\n==========================================================");
                LogMessage($"✅ 성공! 총 {fileCount}개 파일 병합 완료");
                LogMessage($"📁 저장 경로: {Path.GetDirectoryName(outputFile)}");
                LogMessage($"📄 파 일 명: {Path.GetFileName(outputFile)}");
                LogMessage($"==========================================================\n");
                Dispatcher.Invoke(() =>
                {
                    BtnCopyToClipboard.IsEnabled = true;
                });
            }
            catch (Exception ex)
            {
                LogMessage($"\n❌ 오류 발생: {ex.Message}");
            }
        }

        private void BtnCopyToClipboard_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(lastMergedFilePath))
            {
                try
                {
                    string content = File.ReadAllText(lastMergedFilePath, Encoding.UTF8);
                    Clipboard.SetText(content);
                    MessageBox.Show("합쳐진 소스 코드가 클립보드에 복사되었습니다.\n원하시는 곳에 붙여넣기(Ctrl+V) 하세요.", "복사 완료", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"클립보드 복사 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("복사할 결과물 파일이 존재하지 않습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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