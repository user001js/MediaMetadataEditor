using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using MediaMetadataEditor.Core;

using TFile = TagLib.File;

namespace MediaMetadataEditor.Core
{
    public static class SettingsSvc
    {
        public static readonly string[] SupportedExtensions = new[] { ".mp4", ".m4v", ".mov", ".wmv", ".mkv", ".avi" };
        public static bool CreateBackup { get; private set; } = true;
        public static int MaxBatchDefault { get; private set; } = 50;
        public static string AtomicParsleyPath { get; private set; } = "AtomicParsley";
        public static string ExifToolPath { get; private set; } = "exiftool";
        public static string MediaInfoPath { get; private set; } = "mediainfo";

        static SettingsSvc()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory;
                var cfg = Path.Combine(baseDir, "appsettings.json");
                if (File.Exists(cfg))
                {
                    var j = JObject.Parse(File.ReadAllText(cfg));
                    var b = j["Behavior"];
                    if (b != null)
                    {
                        CreateBackup = b.Value<bool?>("CreateBackup") ?? CreateBackup;
                        MaxBatchDefault = b.Value<int?>("MaxBatchDefault") ?? MaxBatchDefault;
                    }
                    var t = j["ExternalTools"];
                    if (t != null)
                    {
                        AtomicParsleyPath = t.Value<string?>("AtomicParsleyPath") ?? AtomicParsleyPath;
                        ExifToolPath = t.Value<string?>("ExifToolPath") ?? ExifToolPath;
                        MediaInfoPath = t.Value<string?>("MediaInfoPath") ?? MediaInfoPath;
                    }
                }
            }
            catch { /* keep defaults */ }
        }
    }

    public static class PresetSvc
    {
        private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory, "presets.json");
        private static readonly object locker = new();

        public static IEnumerable<Preset> GetAll()
        {
            try
            {
                lock (locker)
                {
                    if (!File.Exists(FilePath)) return Enumerable.Empty<Preset>();
                    var txt = File.ReadAllText(FilePath);
                    if (string.IsNullOrWhiteSpace(txt)) return Enumerable.Empty<Preset>();
                    return JsonConvert.DeserializeObject<List<Preset>>(txt) ?? Enumerable.Empty<Preset>();
                }
            }
            catch { return Enumerable.Empty<Preset>(); }
        }

        public static void AddOrReplace(Preset p)
        {
            try
            {
                lock (locker)
                {
                    var list = GetAll().ToList();
                    var ex = list.FirstOrDefault(x => string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase));
                    if (ex != null) list.Remove(ex);
                    list.Add(p);
                    File.WriteAllText(FilePath, JsonConvert.SerializeObject(list, Formatting.Indented));
                }
            }
            catch { }
        }

        public static void Remove(string name)
        {
            try
            {
                lock (locker)
                {
                    var list = GetAll().ToList();
                    var ex = list.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (ex != null) { list.Remove(ex); File.WriteAllText(FilePath, JsonConvert.SerializeObject(list, Formatting.Indented)); }
                }
            }
            catch { }
        }
    }

    public static class TagLibSvc
    {
        public static Dictionary<string, string> ReadTags(string path)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var f = TFile.Create(path);
                var t = f.Tag;
                if (t != null)
                {
                    if (!string.IsNullOrEmpty(t.Title)) d["Title"] = t.Title;
                    if (!string.IsNullOrEmpty(t.Comment)) d["Comment"] = t.Comment;
                    if (t.Performers?.Length > 0) d["Artist"] = string.Join(";", t.Performers);
                    if (t.Genres?.Length > 0) d["Genre"] = string.Join(";", t.Genres);
                    if (t.Year != 0) d["Year"] = t.Year.ToString();
                    if (!string.IsNullOrEmpty(t.Copyright)) d["Copyright"] = t.Copyright;
                }
            }
            catch { /* best-effort */ }
            return d;
        }

        public static TFile? SafeOpen(string path)
        {
            try { return TFile.Create(path); } catch { return null; }
        }

        public static bool TryWriteBasic(string path, IDictionary<string, string> values, out string message)
        {
            message = "";
            try
            {
                using var f = TFile.Create(path);
                var t = f.Tag;
                if (t == null) { message = "Cannot open tag"; return false; }

                if (values.TryGetValue("Title", out var title)) t.Title = title;
                if (values.TryGetValue("Comment", out var comment)) t.Comment = comment;
                if (values.TryGetValue("Artist", out var artist)) t.Performers = new[] { artist };
                if (values.TryGetValue("Genre", out var genre)) t.Genres = new[] { genre };
                if (values.TryGetValue("Year", out var year) && uint.TryParse(year, out var y)) t.Year = y;
                if (values.TryGetValue("Copyright", out var c)) t.Copyright = c;

                f.Save();
                return true;
            }
            catch (Exception ex) { message = ex.Message; return false; }
        }
    }

    public static class ExternalToolsSvc
    {
        static ProcessStartInfo CreateInfo(string exe, string args) => new ProcessStartInfo { FileName = exe, Arguments = args, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };

        public static (bool Success, string Error) TryWriteWithAtomicParsley(string path, string field, string value)
        {
            try
            {
                var exe = SettingsSvc.AtomicParsleyPath;
                if (string.IsNullOrEmpty(exe)) return (false, "AtomicParsley not configured");
                string? args = field.ToLowerInvariant() switch
                {
                    "title" => $"\"{path}\" --title \"{Escape(value)}\" --overWrite",
                    "comment" => $"\"{path}\" --comment \"{Escape(value)}\" --overWrite",
                    _ => null
                };
                if (args == null) return (false, "Field not supported");
                using var p = Process.Start(CreateInfo(exe, args)) ?? throw new Exception("AtomicParsley failed");
                p.WaitForExit(15000);
                var err = p.StandardError.ReadToEnd();
                if (p.ExitCode != 0) return (false, string.IsNullOrEmpty(err) ? $"Exit {p.ExitCode}" : err);
                return (true, "");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        public static (bool Success, string Error) TryWriteWithExifTool(string path, string field, string value)
        {
            try
            {
                var exe = SettingsSvc.ExifToolPath;
                if (string.IsNullOrEmpty(exe)) return (false, "ExifTool not configured");
                string? tag = field.ToLowerInvariant() switch
                {
                    "title" => $"-Title={Escape(value)}",
                    "comment" => $"-Comment={Escape(value)}",
                    "director" => $"-Director={Escape(value)}",
                    _ => null
                };
                if (tag == null) return (false, "Field not mapped");
                var args = $"-overwrite_original {tag} \"{path}\"";
                using var p = Process.Start(CreateInfo(exe, args)) ?? throw new Exception("exiftool failed");
                p.WaitForExit(20000);
                var err = p.StandardError.ReadToEnd();
                if (p.ExitCode != 0) return (false, string.IsNullOrEmpty(err) ? $"Exit {p.ExitCode}" : err);
                return (true, "");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        private static string Escape(string s) => s?.Replace("\"", "\\\"") ?? string.Empty;
    }

    public static class CapabilitySvc
    {
        private static readonly string MatrixFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory, "capability_matrix.json");
        private static JObject? matrix = null;

        static CapabilitySvc() => LoadMatrix();

        private static void LoadMatrix()
        {
            try
            {
                if (File.Exists(MatrixFile))
                {
                    var txt = File.ReadAllText(MatrixFile);
                    if (!string.IsNullOrWhiteSpace(txt)) matrix = JObject.Parse(txt);
                }
                else matrix = null;
            }
            catch { matrix = null; }
        }

        public static void ReloadIfPossible() => LoadMatrix();

        public static CapabilitySupport GetSupport(string ext, string field)
        {
            try
            {
                if (matrix == null) return CapabilitySupport.Unknown;
                var key = (ext ?? string.Empty).TrimStart('.').ToLowerInvariant();
                var formats = matrix["formats"] as JObject;
                if (formats == null) return CapabilitySupport.Unknown;
                var node = formats[key] as JObject;
                if (node == null) return CapabilitySupport.Unknown;
                var propName = $"Supports{field}";
                var token = node[propName];
                if (token == null) return CapabilitySupport.Unknown;
                var val = token.ToString() ?? string.Empty;
                return val.Equals("true", StringComparison.OrdinalIgnoreCase) ? CapabilitySupport.Supported :
                       val.Equals("unsupported", StringComparison.OrdinalIgnoreCase) ? CapabilitySupport.Unsupported :
                       CapabilitySupport.Conditional;
            }
            catch { return CapabilitySupport.Unknown; }
        }
    }
}
