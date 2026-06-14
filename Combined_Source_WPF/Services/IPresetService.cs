using System.Collections.Generic;
using Combined_Source_WPF.Models;

namespace Combined_Source_WPF.Services
{
    public interface IPresetService
    {
        List<MergePreset> LoadPresets();
        void SavePresets(List<MergePreset> presets);
    }
}
