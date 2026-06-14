using System.Collections.Generic;

namespace Combined_Source_WPF.Models
{
    public class MergePreset
    {
        public string PresetName { get; set; } = string.Empty;
        public List<ReferenceDocument> ReferenceDocuments { get; set; } = new List<ReferenceDocument>();
    }
}
