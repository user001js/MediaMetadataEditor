// Core/ViewModels.cs
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Diagnostics;

namespace MediaMetadataEditor.Core
{
    // base viewmodel helper
    public class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? prop = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? prop = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value; OnPropertyChanged(prop); return true;
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;
        public RelayCommand(Action<object?> exec, Predicate<object?>? canExec = null) { _execute = exec; _canExecute = canExec; }
        public bool CanExecute(object? param) => _canExecute?.Invoke(param) ?? true;
        public event EventHandler? CanExecuteChanged;
        public void Execute(object? param) => _execute(param);
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    // File item + FieldVM
    public class FileItem
    {
        public string FullPath { get; init; } = "";
        public string FileName => Path.GetFileName(FullPath);
        public string Extension => Path.GetExtension(FullPath)?.ToLowerInvariant() ?? "";
        public override string ToString() => FileName;
    }

    public class FieldVM : ObservableObject
    {
        public string Key { get; init; } = "";
        public string DisplayName { get; init; } = "";
        bool _isChecked = true; public bool IsChecked { get => _isChecked; set => SetProperty(ref _isChecked, value); }
        bool _isEnabled = true; public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
        string _value = ""; public string Value { get => _value; set => SetProperty(ref _value, value); }
        public string Tooltip { get; set; } = "";
    }

    // MainViewModel (consolidated, defensive)
    public class MainViewModel : ObservableObject
    {
        public ObservableCollection<FileItem> Files { get; } = new();
        public ObservableCollection<FileItem> SelectedFiles { get; } = new();
        public ObservableCollection<Preset> Presets { get; } = new();
        public Preset? SelectedPreset { get; set; }
        public ObservableCollection<FieldVM> FieldViewModels { get; } = new();

        // Commands
        public ICommand CmdAddFiles { get; }
        public ICommand CmdRemoveSelected { get; }
        public ICommand CmdClear { get; }
        public ICommand CmdDedupe { get; }
        public ICommand CmdApply { get; }
        public ICommand CmdPreviewSelected { get; }
        public ICommand CmdRemoveSubstring { get; }
        public ICommand CmdSavePreset { get; }
        public ICommand CmdDeletePreset { get; }
        public ICommand CmdExportReport { get; }
        public ICommand CmdToggleTheme { get; }
        public ICommand CmdOpenCapability { get; }
        public ICommand CmdToggleShowHidden { get; }
        public ICommand CmdRestoreFromBackup { get; }

        // options/state
        bool _createBackup = SettingsSvc.CreateBackup; public bool CreateBackup { get => _createBackup; set => SetProperty(ref _createBackup, value); }
        bool _allowExperimental = false; public bool AllowExperimental { get => _allowExperimental; set => SetProperty(ref _allowExperimental, value); }
        bool _showHiddenFields = false; public bool ShowHiddenFields { get => _showHiddenFields; set => SetProperty(ref _showHiddenFields, value); }
        string _status = "Ready"; public string StatusText { get => _status; set => SetProperty(ref _status, value); }

        bool isDark = false;

        public MainViewModel()
        {
            // fields
            var keys = new[] {
                ("Title","Title"),
                ("Comment","Description / Comment"),
                ("Artist","Artist / Performer"),
                ("Genre","Genre"),
                ("Year","Year"),
                ("Director","Director"),
                ("Producer","Producer"),
                ("Writer","Writer"),
                ("Provider","Provider"),
                ("EncodedBy","Encoded by"),
                ("Copyright","Copyright"),
                ("AuthorUrl","Author URL"),
                ("CustomURL","Custom URL")
            };
            foreach (var (k, label) in keys) FieldViewModels.Add(new FieldVM { Key = k, DisplayName = label });

            foreach (var p in PresetSvc.GetAll()) Presets.Add(p);

            // commands
            CmdAddFiles = new RelayCommand(_ => AddFilesDialog());
            CmdRemoveSelected = new RelayCommand(_ => RemoveSelected());
            CmdClear = new RelayCommand(_ => ClearAll());
            CmdDedupe = new RelayCommand(_ => Dedupe());
            CmdApply = new RelayCommand(async _ => await ApplyAsync());
            CmdPreviewSelected = new RelayCommand(_ => PreviewSelected());
            CmdRemoveSubstring = new RelayCommand(async _ => await RemoveSubstringAsync());
            CmdSavePreset = new RelayCommand(_ => SavePreset());
            CmdDeletePreset = new RelayCommand(_ => DeletePreset());
            CmdExportReport = new RelayCommand(_ => ExportReport());
            CmdToggleTheme = new RelayCommand(_ => ToggleTheme());
            CmdOpenCapability = new RelayCommand(_ => OpenCapability());
            CmdToggleShowHidden = new RelayCommand(_ => UpdateFieldSupport());
            CmdRestoreFromBackup = new RelayCommand(_ => RestoreBackupDialog());
        }

        // UI actions
        public void AddFilesDialog()
        {
            var ofd = new OpenFileDialog { Multiselect = true, Filter = "Video files|*.mp4;*.m4v;*.mov;*.wmv;*.mkv;*.avi;*.flv|All|*.*" };
            if (ofd.ShowDialog() == true) AddFiles(ofd.FileNames);
        }

        public void AddFiles(IEnumerable<string> paths)
        {
            var allowed = SettingsSvc.SupportedExtensions;
            foreach (var p in paths)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(p)) continue;
                    if (!File.Exists(p)) continue;
                    var ext = Path.GetExtension(p)?.ToLowerInvariant() ?? "";
                    if (!allowed.Contains(ext)) continue;
                    if (!Files.Any(f => string.Equals(f.FullPath, p, StringComparison.OrdinalIgnoreCase)))
                        Files.Add(new FileItem { FullPath = p });
                }
                catch { }
            }
            StatusText = $"{Files.Count} files loaded";
            UpdateFieldSupport();
        }

        public void RemoveSelected()
        {
            var arr = SelectedFiles.ToArray();
            foreach (var f in arr) Files.Remove(f);
            SelectedFiles.Clear();
            UpdateFieldSupport();
        }

        public void ClearAll()
        {
            Files.Clear();
            SelectedFiles.Clear();
            UpdateFieldSupport();
        }

        public void Dedupe()
        {
            var unique = Files.GroupBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            Files.Clear();
            foreach (var u in unique) Files.Add(u);
            UpdateFieldSupport();
        }

        public void PreviewSelected()
        {
            var t = SelectedFiles.FirstOrDefault() ?? Files.FirstOrDefault();
            if (t == null) { MessageBox.Show("No file selected."); return; }
            var tags = TagLibSvc.ReadTags(t.FullPath);
            var sb = new StringBuilder();
            sb.AppendLine($"File: {t.FileName}");
            foreach (var kv in tags) sb.AppendLine($"{kv.Key}: {kv.Value}");
            MessageBox.Show(sb.ToString(), "Preview");
        }

        public async Task ApplyAsync()
        {
            var targets = SelectedFiles.Any() ? SelectedFiles.ToArray() : Files.ToArray();
            if (!targets.Any()) { MessageBox.Show("No files selected."); return; }
            if (MessageBox.Show($"Apply to {targets.Length} files? Continue?", "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            var values = FieldViewModels.Where(f => f.IsChecked).ToDictionary(f => f.Key, f => f.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            var report = new List<FileReport>();
            StatusText = "Processing...";

            foreach (var file in targets)
            {
                var fr = new FileReport { FilePath = file.FullPath };
                try
                {
                    if (CreateBackup)
                    {
                        var bak = file.FullPath + ".bak_mme";
                        if (!File.Exists(bak)) File.Copy(file.FullPath, bak);
                    }

                    if (TagLibSvc.TryWriteBasic(file.FullPath, values, out var msg))
                    {
                        fr.Fields["Write"] = new FieldResult { Field = "Write", Status = FieldStatus.Success, Message = "TagLib" };
                    }
                    else
                    {
                        var msgs = new List<string>(); bool wrote = false;
                        if (values.TryGetValue("Title", out var title) && !string.IsNullOrWhiteSpace(title))
                        {
                            var a = ExternalToolsSvc.TryWriteWithAtomicParsley(file.FullPath, "title", title);
                            if (a.Success) wrote = true; else msgs.Add("AP:" + a.Error);
                            if (!wrote)
                            {
                                var e = ExternalToolsSvc.TryWriteWithExifTool(file.FullPath, "title", title);
                                if (e.Success) wrote = true; else msgs.Add("Exif:" + e.Error);
                            }
                        }
                        if (!wrote && values.TryGetValue("Comment", out var comment) && !string.IsNullOrWhiteSpace(comment))
                        {
                            var a = ExternalToolsSvc.TryWriteWithAtomicParsley(file.FullPath, "comment", comment);
                            if (a.Success) wrote = true; else msgs.Add("AP:" + a.Error);
                            if (!wrote)
                            {
                                var e = ExternalToolsSvc.TryWriteWithExifTool(file.FullPath, "comment", comment);
                                if (e.Success) wrote = true; else msgs.Add("Exif:" + e.Error);
                            }
                        }

                        fr.Fields["Write"] = wrote ? new FieldResult { Field = "Write", Status = FieldStatus.Success, Message = "External" } :
                                                     new FieldResult { Field = "Write", Status = FieldStatus.Partial, Message = string.Join("; ", msgs) };
                    }
                }
                catch (Exception ex)
                {
                    fr.Fields["Write"] = new FieldResult { Field = "Write", Status = FieldStatus.Failed, Message = ex.Message };
                }
                report.Add(fr);
            }

            var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory;
            var outp = Path.Combine(baseDir, $"apply_report_{DateTime.Now:yyyyMMddHHmmss}.json");
            await File.WriteAllTextAsync(outp, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            MessageBox.Show($"Done. Report: {outp}");
            StatusText = "Ready";
        }

        public async Task RemoveSubstringAsync()
        {
            var dlg = new Views.PromptWindow("Substring to remove:", "Remove substring");
            if (dlg.ShowDialog() != true) return;
            var sub = dlg.Result ?? "";
            if (string.IsNullOrEmpty(sub)) return;

            var field = FieldViewModels.FirstOrDefault(f => f.IsChecked)?.Key ?? "Title";
            var targets = SelectedFiles.Any() ? SelectedFiles : Files;
            var logs = new List<string>();

            foreach (var file in targets)
            {
                try
                {
                    using var tf = TagLibSvc.SafeOpen(file.FullPath);
                    if (tf == null) { logs.Add($"{file.FullPath},NO_TAG"); continue; }
                    var tag = tf.Tag;
                    string oldv = "", newv = "";
                    switch (field)
                    {
                        case "Title": oldv = tag.Title ?? ""; newv = oldv.Replace(sub, "", StringComparison.OrdinalIgnoreCase); tag.Title = newv; break;
                        case "Comment": oldv = tag.Comment ?? ""; newv = oldv.Replace(sub, "", StringComparison.OrdinalIgnoreCase); tag.Comment = newv; break;
                        case "Artist": var arr = tag.Performers ?? Array.Empty<string>(); var arr2 = arr.Select(a => a.Replace(sub, "", StringComparison.OrdinalIgnoreCase)).ToArray(); oldv = string.Join("|", arr); newv = string.Join("|", arr2); tag.Performers = arr2; break;
                        case "Genre": var g = tag.Genres ?? Array.Empty<string>(); var g2 = g.Select(a => a.Replace(sub, "", StringComparison.OrdinalIgnoreCase)).ToArray(); oldv = string.Join("|", g); newv = string.Join("|", g2); tag.Genres = g2; break;
                    }
                    tf.Save(); logs.Add($"{file.FullPath},OK,{field},{oldv}->{newv}");
                }
                catch (Exception ex) { logs.Add($"{file.FullPath},ERR,{ex.Message}"); }
            }

            var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory;
            var outlog = Path.Combine(baseDir, $"remove_{DateTime.Now:yyyyMMddHHmmss}.log");
            await File.WriteAllLinesAsync(outlog, logs);
            MessageBox.Show($"Done. Log: {outlog}");
        }

        public void SavePreset()
        {
            var dlg = new Views.PromptWindow("Preset name:", "Save preset", "MyPreset");
            if (dlg.ShowDialog() != true) return;
            var name = dlg.Result ?? ""; if (string.IsNullOrWhiteSpace(name)) return;
            var p = new Preset { Name = name };
            foreach (var f in FieldViewModels) p.Values[f.Key] = f.Value ?? "";
            foreach (var f in FieldViewModels.Where(x => x.IsChecked)) p.CheckedFields.Add(f.Key);
            PresetSvc.AddOrReplace(p);
            Presets.Clear(); foreach (var pp in PresetSvc.GetAll()) Presets.Add(pp);
            MessageBox.Show("Preset saved.");
        }

        public void DeletePreset()
        {
            if (SelectedPreset == null) { MessageBox.Show("Select preset."); return; }
            PresetSvc.Remove(SelectedPreset.Name); Presets.Remove(SelectedPreset); SelectedPreset = null;
        }

        public void ExportReport() => MessageBox.Show("Reports are in app folder after apply.");

        public void ToggleTheme()
        {
            isDark = !isDark;
            try
            {
                var dict = new ResourceDictionary();
                var basePath = AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory;
                var resPath = Path.Combine(basePath, "Resources", isDark ? "Dark.xaml" : "Light.xaml");
                dict.Source = File.Exists(resPath) ? new Uri(resPath, UriKind.Absolute) : new Uri($"/Resources/{(isDark ? "Dark.xaml" : "Light.xaml")}", UriKind.Relative);
                Application.Current.Resources.MergedDictionaries.Clear();
                Application.Current.Resources.MergedDictionaries.Add(dict);
            }
            catch { }
        }

        public void OpenCapability()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory, "capability_matrix.json");
            try
            {
                var psi = new ProcessStartInfo { FileName = path, UseShellExecute = true };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Cannot open capability file: " + ex.Message);
            }
        }

        public void UpdateFieldSupport()
        {
            var files = Files.ToArray(); int total = files.Length;
            foreach (var fvm in FieldViewModels)
            {
                int supported = 0;
                foreach (var f in files)
                {
                    var support = CapabilitySvc.GetSupport(f.Extension, fvm.Key);
                    if (support != CapabilitySupport.Unsupported) supported++;
                }
                if (total == 0) { fvm.IsEnabled = true; fvm.Tooltip = "No files"; }
                else if (supported == 0) { fvm.IsEnabled = ShowHiddenFields; fvm.Tooltip = ShowHiddenFields ? $"Supported 0/{total}" : "Hidden"; }
                else if (supported < total) { fvm.IsEnabled = true; fvm.Tooltip = $"Supported {supported}/{total}"; }
                else { fvm.IsEnabled = true; fvm.Tooltip = "Supported in all"; }
            }
            OnPropertyChanged(nameof(FieldViewModels));
        }

        public void RestoreBackupDialog()
        {
            var dlg = new OpenFileDialog { Filter = "Backup files|*.bak_mme|All|*.*" };
            if (dlg.ShowDialog() != true) return;
            foreach (var bak in dlg.FileNames)
            {
                try
                {
                    var orig = bak.EndsWith(".bak_mme") ? bak.Substring(0, bak.Length - ".bak_mme".Length) : null;
                    if (orig == null) { MessageBox.Show("Invalid backup"); continue; }
                    File.Copy(bak, orig, true);
                }
                catch (Exception ex) { MessageBox.Show("Restore failed: " + ex.Message); }
            }
            MessageBox.Show("Restore attempts finished.");
        }
    }
}
