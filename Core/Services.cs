using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Diagnostics;
using System.Threading.Tasks;
using TagFile = TagLib.File;

namespace MediaMetadataEditor.Core
{
    public static class SettingsSvc
    {
        public static readonly string[] SupportedExtensions = new[] { ".mp4", ".m4v", ".mov", ".wmv", ".mkv", ".avi", ".flv" };
        public static bool CreateBackup { get; set; } = false;
        public static string AppFolder => AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory;
        public static string OperationsLog => Path.Combine(AppFolder, "operations_log.jsonl");
        public static int ExternalToolTimeoutMs { get; set; } = 7000;
    }

    public static class PresetSvc
    {
        private static string PresetFile => Path.Combine(SettingsSvc.AppFolder, "presets.json");
        public static IEnumerable<Preset> GetAll()
        {
            try
            {
                if (!File.Exists(PresetFile)) return Enumerable.Empty<Preset>();
                var txt = File.ReadAllText(PresetFile);
                return JsonSerializer.Deserialize<List<Preset>>(txt) ?? Enumerable.Empty<Preset>();
            }
            catch { return Enumerable.Empty<Preset>(); }
        }

        public static void AddOrReplace(Preset p)
        {
            var all = GetAll().ToList();
            var idx = all.FindIndex(x => string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) all[idx] = p; else all.Add(p);
            File.WriteAllText(PresetFile, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static void Remove(string name)
        {
            var all = GetAll().Where(x => !string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            File.WriteAllText(PresetFile, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    public static class LoggerSvc
    {
        private static readonly object lockObj = new();
        public static async Task AppendOperationAsync(object entry)
        {
            try
            {
                var path = SettingsSvc.OperationsLog;
                var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = false });
                lock (lockObj)
                {
                    File.AppendAllText(path, json + Environment.NewLine);
                }
                await Task.CompletedTask;
            }
            catch { }
        }
    }

    public static class TagLibSvc
    {
        public static TagFile? SafeOpen(string path)
        {
            try { return TagFile.Create(path); } catch { return null; }
        }

        public static Dictionary<string, string> ReadTags(string path)
        {
            var ret = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var tf = SafeOpen(path);
                if (tf == null) return ret;
                var tag = tf.Tag;
                ret["Title"] = tag.Title ?? "";
                ret["Comment"] = tag.Comment ?? "";
                ret["Artist"] = string.Join(", ", tag.Performers ?? Array.Empty<string>());
                ret["Genre"] = string.Join(", ", tag.Genres ?? Array.Empty<string>());
                ret["Year"] = tag.Year != 0 ? tag.Year.ToString() : "";
                ret["Copyright"] = tag.Copyright ?? "";
            }
            catch { }
            return ret;
        }

        public static bool TryWriteBasic(string path, IDictionary<string, string> values, out string message)
        {
            message = "";
            try
            {
                using var tf = SafeOpen(path);
                if (tf == null) { message = "Não foi possível abrir o arquivo para escrita."; return false; }
                var tag = tf.Tag;
                if (values.TryGetValue("Title", out var title)) tag.Title = title ?? "";
                if (values.TryGetValue("Comment", out var comment)) tag.Comment = comment ?? "";
                if (values.TryGetValue("Artist", out var artist)) tag.Performers = (string.IsNullOrWhiteSpace(artist) ? Array.Empty<string>() : new[] { artist });
                if (values.TryGetValue("Genre", out var genre)) tag.Genres = (string.IsNullOrWhiteSpace(genre) ? Array.Empty<string>() : new[] { genre });
                if (values.TryGetValue("Year", out var year) && uint.TryParse(year, out var y)) tag.Year = y;
                tf.Save();
                message = "OK";
                return true;
            }
            catch (Exception ex) { message = ex.Message; return false; }
        }
    }

    public static class CapabilitySvc
    {
        private static readonly Dictionary<string, Dictionary<string, string>> _matrix = LoadMatrix();

        private static Dictionary<string, Dictionary<string, string>> LoadMatrix()
        {
            try
            {
                var path = Path.Combine(SettingsSvc.AppFolder, "capability_matrix.json");
                if (!File.Exists(path))
                {
                    return new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        [".mp4"] = new() { ["Title"] = "Supported", ["Comment"] = "Supported", ["Artist"] = "Supported", ["Genre"] = "Supported", ["Year"] = "Supported", ["AuthorUrl"] = "Conditional" },
                        [".mkv"] = new() { ["Title"] = "Supported", ["Comment"] = "Supported", ["Artist"] = "Partial", ["Genre"] = "Partial", ["Year"] = "Unsupported" },
                    };
                }
                var txt = File.ReadAllText(path);
                var doc = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(txt);
                return doc ?? new();
            }
            catch { return new(); }
        }

        public static CapabilitySupport GetSupport(string extension, string field)
        {
            try
            {
                if (string.IsNullOrEmpty(extension)) return CapabilitySupport.Unsupported;
                if (!_matrix.TryGetValue(extension.ToLowerInvariant(), out var fieldMap)) return CapabilitySupport.Unsupported;
                if (!fieldMap.TryGetValue(field, out var val)) return CapabilitySupport.Unsupported;
                return val switch
                {
                    "Supported" => CapabilitySupport.Supported,
                    "Partial" => CapabilitySupport.Partial,
                    "Conditional" => CapabilitySupport.Partial,
                    _ => CapabilitySupport.Unsupported
                };
            }
            catch { return CapabilitySupport.Unsupported; }
        }
    }

    public static class ExternalToolsSvc
    {
        public static (bool Success, string Error) TryWriteWithAtomicParsley(string file, string key, string value)
        {
            try
            {
                var exe = "AtomicParsley";
                var args = $"\"{file}\" --{key} \"{value}\" --overWrite";
                var psi = new ProcessStartInfo { FileName = exe, Arguments = args, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                using var p = Process.Start(psi);
                if (p == null) return (false, "Falha ao iniciar AtomicParsley");
                if (!p.WaitForExit(SettingsSvc.ExternalToolTimeoutMs)) { try { p.Kill(); } catch { } return (false, "Timeout AtomicParsley"); }
                var err = p.StandardError.ReadToEnd();
                return (p.ExitCode == 0, err);
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        public static (bool Success, string Error) TryWriteWithExifTool(string file, string key, string value)
        {
            try
            {
                var exe = "exiftool";
                var args = $"-{key}=\"{value}\" \"{file}\" -overwrite_original";
                var psi = new ProcessStartInfo { FileName = exe, Arguments = args, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                using var p = Process.Start(psi);
                if (p == null) return (false, "Falha ao iniciar exiftool");
                if (!p.WaitForExit(SettingsSvc.ExternalToolTimeoutMs)) { try { p.Kill(); } catch { } return (false, "Timeout exiftool"); }
                var err = p.StandardError.ReadToEnd();
                return (p.ExitCode == 0, err);
            }
            catch (Exception ex) { return (false, ex.Message); }
        }
    }
}
