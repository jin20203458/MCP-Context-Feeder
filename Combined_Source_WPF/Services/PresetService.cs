using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Combined_Source_WPF.Models;

namespace Combined_Source_WPF.Services
{
    public class PresetService : IPresetService
    {
        private readonly string _presetFilePath = "presets.json";

        public List<MergePreset> LoadPresets()
        {
            var presets = new List<MergePreset>();
            try
            {
                if (!File.Exists(_presetFilePath)) return presets;

                string json = File.ReadAllText(_presetFilePath);
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
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
            }
            catch (JsonException)
            {
                // JSON 파싱 에러 예외 처리
            }
            catch (IOException)
            {
                // 파일 IO 에러 예외 처리
            }
            catch (UnauthorizedAccessException)
            {
                // 권한 에러 예외 처리
            }
            catch
            {
                // 기타 예외 처리
            }
            return presets;
        }

        public void SavePresets(List<MergePreset> presets)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(presets, options);
                File.WriteAllText(_presetFilePath, json);
            }
            catch (IOException)
            {
                // 파일 IO 에러 예외 처리
            }
            catch (UnauthorizedAccessException)
            {
                // 권한 에러 예외 처리
            }
            catch
            {
                // 기타 예외 처리
            }
        }
    }
}
