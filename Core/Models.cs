using System;
using System.Collections.Generic;

namespace MediaMetadataEditor.Core
{
    public enum CapabilitySupport { Unsupported, Partial, Supported }

    public class Preset
    {
        public string Name { get; set; } = "";
        public Dictionary<string, string> Values { get; set; } = new();
        public HashSet<string> CheckedFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
