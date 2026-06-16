using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Combined_Source_WPF.Models;
using Combined_Source_WPF.Services;

namespace Combined_Source_WPF.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IPresetService _presetService;
        private readonly IFileInspector _fileInspector;
        private LocalContextServer? _server;
        private List<MergePreset> _rawPresets = new List<MergePreset>();
        private CancellationTokenSource? _tokenCalcCts;

        [ObservableProperty]
        private string _taskIntent = "예: 메인 화면의 버튼 디자인을 조금 더 현대적인 스타일로 변경하고 다크모드를 지원하도록 수정해줘.";

        [ObservableProperty]
        private string? _selectedPresetName;

        [ObservableProperty]
        private string _serverStatusText = "서버 중지됨";

        [ObservableProperty]
        private string _estimatedTokensText = "0 tokens";

        [ObservableProperty]
        private bool _isServerRunning;

        [ObservableProperty]
        private string _logText = string.Empty;

        public ObservableCollection<ReferenceDocument> ReferenceDocuments { get; } = new ObservableCollection<ReferenceDocument>();
        public ObservableCollection<string> Presets { get; } = new ObservableCollection<string>();

        public MainViewModel(IPresetService presetService, IFileInspector fileInspector)
        {
            _presetService = presetService ?? throw new ArgumentNullException(nameof(presetService));
            _fileInspector = fileInspector ?? throw new ArgumentNullException(nameof(fileInspector));
        }

        // 초기화 메서드 (Window Loaded 시 호출)
        public void Initialize()
        {
            LoadPresets();
            StartServer(); // 자동 구동
        }

        private void LoadPresets()
        {
            try
            {
                _rawPresets = _presetService.LoadPresets();
                Presets.Clear();
                foreach (var preset in _rawPresets)
                {
                    Presets.Add(preset.PresetName);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 프리셋 로드 중 오류: {ex.Message}");
            }
        }

        partial void OnSelectedPresetNameChanged(string? value)
        {
            if (string.IsNullOrEmpty(value)) return;

            var preset = _rawPresets.FirstOrDefault(p => p.PresetName.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (preset != null)
            {
                ReferenceDocuments.Clear();
                foreach (var doc in preset.ReferenceDocuments)
                {
                    ReferenceDocuments.Add(doc);
                }
                EstimateTokens();
            }
        }

        [RelayCommand]
        private void SavePreset()
        {
            string? presetName = SelectedPresetName?.Trim();
            if (string.IsNullOrEmpty(presetName))
            {
                MessageBox.Show("저장할 프리셋 이름을 입력해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var existing = _rawPresets.FirstOrDefault(p => p.PresetName.Equals(presetName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.ReferenceDocuments = ReferenceDocuments.ToList();
            }
            else
            {
                _rawPresets.Add(new MergePreset
                {
                    PresetName = presetName,
                    ReferenceDocuments = ReferenceDocuments.ToList()
                });
                Presets.Add(presetName);
            }

            _presetService.SavePresets(_rawPresets);
            SelectedPresetName = presetName;
            MessageBox.Show($"'{presetName}' 프리셋이 저장되었습니다.", "저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        [RelayCommand]
        private void DeletePreset()
        {
            string? presetName = SelectedPresetName?.Trim();
            if (string.IsNullOrEmpty(presetName)) return;

            var preset = _rawPresets.FirstOrDefault(p => p.PresetName.Equals(presetName, StringComparison.OrdinalIgnoreCase));
            if (preset != null)
            {
                _rawPresets.Remove(preset);
                Presets.Remove(presetName);
                _presetService.SavePresets(_rawPresets);
                SelectedPresetName = string.Empty;
                ReferenceDocuments.Clear();
                MessageBox.Show($"'{presetName}' 프리셋이 삭제되었습니다.", "삭제 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        [RelayCommand]
        private void AddFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "참조할 파일 선택",
                Multiselect = true,
                Filter = "모든 파일 (*.*)|*.*|소스 코드 (*.cs;*.xaml;*.js;*.ts;*.py;*.txt)|*.cs;*.xaml;*.js;*.ts;*.py;*.txt"
            };
            if (dialog.ShowDialog() == true)
            {
                bool added = false;
                foreach (string fileName in dialog.FileNames)
                {
                    if (!ReferenceDocuments.Any(r => r.Path.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
                    {
                        ReferenceDocuments.Add(new ReferenceDocument { Path = fileName, Type = "File" });
                        added = true;
                    }
                }
                if (added)
                {
                    EstimateTokens();
                    SaveCurrentPresetReferencesIfSelected();
                }
            }
        }

        [RelayCommand]
        private void AddFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "참조할 폴더 선택"
            };
            if (dialog.ShowDialog() == true)
            {
                string folderPath = dialog.FolderName;
                if (!ReferenceDocuments.Any(r => r.Path.Equals(folderPath, StringComparison.OrdinalIgnoreCase)))
                {
                    ReferenceDocuments.Add(new ReferenceDocument { Path = folderPath, Type = "Directory" });
                    EstimateTokens();
                    SaveCurrentPresetReferencesIfSelected();
                }
            }
        }

        [RelayCommand]
        private void RemoveReference(object? parameter)
        {
            if (parameter is IList selectedList && selectedList.Count > 0)
            {
                var listToRemove = selectedList.Cast<ReferenceDocument>().ToList();
                foreach (var doc in listToRemove)
                {
                    ReferenceDocuments.Remove(doc);
                }
                EstimateTokens();
                SaveCurrentPresetReferencesIfSelected();
            }
            else
            {
                MessageBox.Show("삭제할 참조 항목을 목록에서 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public void HandleFileDrop(string[] paths)
        {
            bool added = false;
            foreach (string path in paths)
            {
                if (File.Exists(path))
                {
                    if (!ReferenceDocuments.Any(r => r.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                    {
                        ReferenceDocuments.Add(new ReferenceDocument { Path = path, Type = "File" });
                        added = true;
                    }
                }
                else if (Directory.Exists(path))
                {
                    if (!ReferenceDocuments.Any(r => r.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                    {
                        ReferenceDocuments.Add(new ReferenceDocument { Path = path, Type = "Directory" });
                        added = true;
                    }
                }
            }
            if (added)
            {
                EstimateTokens();
                SaveCurrentPresetReferencesIfSelected();
            }
        }

        private void SaveCurrentPresetReferencesIfSelected()
        {
            string? presetName = SelectedPresetName?.Trim();
            if (string.IsNullOrEmpty(presetName)) return;

            var currentPreset = _rawPresets.FirstOrDefault(p => p.PresetName.Equals(presetName, StringComparison.OrdinalIgnoreCase));
            if (currentPreset != null)
            {
                currentPreset.ReferenceDocuments = ReferenceDocuments.ToList();
                _presetService.SavePresets(_rawPresets);
            }
        }

        public void EstimateTokens()
        {
            _tokenCalcCts?.Cancel();
            _tokenCalcCts?.Dispose();
            _tokenCalcCts = new CancellationTokenSource();

            var token = _tokenCalcCts.Token;
            var docsCopy = ReferenceDocuments.ToList();

            EstimatedTokensText = "Calculating...";

            Task.Run(async () =>
            {
                try
                {
                    int chars = 0;
                    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var doc in docsCopy)
                    {
                        token.ThrowIfCancellationRequested();

                        if (doc.Type == "File")
                        {
                            if (File.Exists(doc.Path))
                            {
                                chars += _fileInspector.GetCharCount(doc.Path, visited);
                            }
                        }
                        else if (doc.Type == "Directory")
                        {
                            if (Directory.Exists(doc.Path))
                            {
                                try
                                {
                                    var files = Directory.EnumerateFiles(doc.Path, "*.*", SearchOption.AllDirectories);
                                    foreach (var file in files)
                                    {
                                        token.ThrowIfCancellationRequested();

                                        if (_fileInspector.IsExcludedPath(file)) continue;
                                        if (_fileInspector.IsTextFile(file))
                                        {
                                            chars += _fileInspector.GetCharCount(file, visited);
                                        }
                                    }
                                }
                                catch (UnauthorizedAccessException) { }
                                catch (IOException) { }
                            }
                        }
                    }

                    double estTokens = Math.Ceiling(chars / 3.0);
                    
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (!token.IsCancellationRequested)
                        {
                            EstimatedTokensText = $"{estTokens:N0} tokens ({chars:N0} chars)";
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    // 취소 시 무시
                }
            }, token);
        }

        [RelayCommand]
        private void ToggleServer()
        {
            if (IsServerRunning)
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
                    getTaskIntent: () => TaskIntent,
                    getReferenceDocuments: () => ReferenceDocuments.ToList(),
                    logAction: LogMessage,
                    fileInspector: _fileInspector
                );

                _server.Start();

                IsServerRunning = true;
                ServerStatusText = $"서버 동작 중: http://127.0.0.1:{_server.Port}/sse";
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 서버 시작 오류: {ex.Message}");
                MessageBox.Show($"서버를 시작하지 못했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                StopServer();
            }
        }

        public void StopServer()
        {
            if (_server != null)
            {
                _server.Stop();
                _server = null;
            }
            IsServerRunning = false;
            ServerStatusText = "서버 중지됨";
        }

        [RelayCommand]
        private void CopyToolName()
        {
            try
            {
                Clipboard.SetText("get_reference_context");
                LogMessage("[UI] 📋 도구명 'get_reference_context'가 클립보드에 복사되었습니다.");
            }
            catch (Exception ex)
            {
                LogMessage($"❌ 클립보드 복사 중 오류 발생: {ex.Message}");
            }
        }

        private void LogMessage(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                LogText += message + Environment.NewLine;
            });
        }
    }
}
