using System.Collections.Generic;

namespace MediaMetadataEditor.Core
{
    public enum FieldStatus { Preview, Success, Partial, Failed, Unsupported }
    public enum CapabilitySupport { Unknown, Supported, Conditional, Unsupported }

    public record Preset
    {
        public string Name { get; init; } = "";
        public Dictionary<string, string> Values { get; init; } = new();
        public HashSet<string> CheckedFields { get; init; } = new();
    }

    public record FieldResult
    {
        public string Field { get; init; } = "";
        public FieldStatus Status { get; init; } = FieldStatus.Preview;
        public string Message { get; init; } = "";
        public string? BackupPath { get; init; }
    }

    public record FileReport
    {
        public string FilePath { get; init; } = "";
        public Dictionary<string, FieldResult> Fields { get; init; } = new();
    }
}
