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
using MediaMetadataEditor.Helpers;

namespace MediaMetadataEditor.Core
{
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

    public class MainViewModel : ObservableObject
    {
        public ObservableCollection<FileItem> Files { get; } = new();
        public ObservableCollection<object> SelectedFiles { get; } = new();
        public ObservableCollection<Preset> Presets { get; } = new();
        public Preset? SelectedPreset { get; set; }
        public ObservableCollection<FieldVM> FieldViewModels { get; } = new();

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
        public ICommand CmdOpenLogs { get; }

        bool _showHidden = false; public bool ShowHiddenFields { get => _showHidden; set { SetProperty(ref _showHidden, value); UpdateFieldSupport(); } }
        string _status = "Pronto"; public string StatusText { get => _status; set => SetProperty(ref _status, value); }

        bool isDark = false;

        public MainViewModel()
        {
            var keys = new[] {
                ("Title","Título"),
                ("Comment","Descrição / Comentário"),
                ("Artist","Artista / Interprete"),
                ("Genre","Gênero"),
                ("Year","Ano"),
                ("Director","Diretor"),
                ("Producer","Produtor"),
                ("Writer","Roteirista"),
                ("Provider","Fornecedor"),
                ("EncodedBy","Codificado por"),
                ("Copyright","Copyright"),
                ("AuthorUrl","URL do Autor"),
                ("CustomURL","URL Customizada")
            };
            foreach (var (k, label) in keys) FieldViewModels.Add(new FieldVM { Key = k, DisplayName = label });

            foreach (var p in PresetSvc.GetAll()) Presets.Add(p);

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
            CmdOpenLogs = new RelayCommand(_ => OpenLogs());
        }

        public void AddFilesDialog()
        {
            var ofd = new OpenFileDialog { Multiselect = true, Filter = "Arquivos de vídeo|*.mp4;*.m4v;*.mov;*.wmv;*.mkv;*.avi;*.flv|Todos|*.*" };
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
            StatusText = $"{Files.Count} arquivos carregados";
            UpdateFieldSupport();
        }

        public void RemoveSelected()
        {
            var toRemove = SelectedFiles.OfType<FileItem>().ToArray();
            foreach (var f in toRemove) Files.Remove(f);
            SelectedFiles.Clear();
            UpdateFieldSupport();
            StatusText = "Removidos seleção.";
        }

        public void ClearAll()
        {
            Files.Clear();
            SelectedFiles.Clear();
            UpdateFieldSupport();
            StatusText = "Lista limpa.";
        }

        public void Dedupe()
        {
            var unique = Files.GroupBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            Files.Clear();
            foreach (var u in unique) Files.Add(u);
            UpdateFieldSupport();
            StatusText = "Duplicatas removidas.";
        }

        public void PreviewSelected()
        {
            var first = SelectedFiles.OfType<FileItem>().FirstOrDefault() ?? Files.FirstOrDefault();
            if (first == null) { MessageBox.Show("Selecione um arquivo."); return; }
            var tags = TagLibSvc.ReadTags(first.FullPath);
            var sb = new StringBuilder();
            sb.AppendLine($"Arquivo: {first.FileName}");
            foreach (var kv in tags) sb.AppendLine($"{kv.Key}: {kv.Value}");
            MessageBox.Show(sb.ToString(), "Pré-visualização");
        }

        private async Task<string> WriteLogAndReturnPathAsync(object entry)
        {
            await LoggerSvc.AppendOperationAsync(entry);
            return SettingsSvc.OperationsLog;
        }

        public async Task ApplyAsync()
        {
            var targets = SelectedFiles.OfType<FileItem>().Any() ? SelectedFiles.OfType<FileItem>().ToArray() : Files.ToArray();
            if (!targets.Any()) { MessageBox.Show("Nenhum arquivo selecionado."); return; }
            if (MessageBox.Show($"Aplicar a {targets.Length} arquivo(s)?", "Confirmar", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            var values = FieldViewModels.Where(f => f.IsChecked).ToDictionary(f => f.Key, f => f.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            StatusText = "Processando...";
            var overall = new List<object>();

            foreach (var file in targets)
            {
                var oldTags = TagLibSvc.ReadTags(file.FullPath);
                var entry = new
                {
                    timestamp = DateTime.UtcNow,
                    file = file.FullPath,
                    attempt = new Dictionary<string, object>(),
                };
                try
                {
                    var ok = TagLibSvc.TryWriteBasic(file.FullPath, values, out var msg);
                    if (ok)
                    {
                        entry.attempt["method"] = "TagLib";
                        entry.attempt["result"] = "success";
                        entry.attempt["message"] = msg;
                    }
                    else
                    {
                        entry.attempt["method"] = "TagLib_Failed";
                        entry.attempt["message"] = msg;
                        var msgs = new List<string>();
                        bool wrote = false;
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
                        entry.attempt["fallbacks"] = msgs;
                        entry.attempt["result"] = wrote ? "success_with_external" : "partial_or_failed";
                    }
                }
                catch (Exception ex)
                {
                    entry.attempt["method"] = "Exception";
                    entry.attempt["result"] = "failed";
                    entry.attempt["message"] = ex.Message;
                }

                var afterTags = TagLibSvc.ReadTags(file.FullPath);
                var logEntry = new
                {
                    timestamp = DateTime.UtcNow,
                    file = file.FullPath,
                    before = oldTags,
                    after = afterTags,
                    attempt = entry.attempt
                };
                await WriteLogAndReturnPathAsync(logEntry);
                overall.Add(logEntry);
            }

            var reportPath = SettingsSvc.OperationsLog;
            MessageBox.Show($"Operação finalizada. Relatório de operações em: {reportPath}", "Concluído");
            StatusText = "Pronto";
        }

        public async Task RemoveSubstringAsync()
        {
            var dlg = new Views.PromptWindow("Substring a remover:", "Remover substring", "");
            if (dlg.ShowDialog() != true) return;
            var sub = dlg.Result ?? "";
            if (string.IsNullOrEmpty(sub)) return;

            var field = FieldViewModels.FirstOrDefault(f => f.IsChecked)?.Key ?? "Title";
            var targets = SelectedFiles.OfType<FileItem>().Any() ? SelectedFiles.OfType<FileItem>() : Files;
            var logs = new List<object>();
            foreach (var file in targets)
            {
                try
                {
                    using var tf = TagLibSvc.SafeOpen(file.FullPath);
                    if (tf == null) { logs.Add(new { file = file.FullPath, result = "no_tag" }); continue; }
                    var tag = tf.Tag;
                    string oldv = "", newv = "";
                    switch (field)
                    {
                        case "Title": oldv = tag.Title ?? ""; newv = oldv.Replace(sub, "", StringComparison.OrdinalIgnoreCase); tag.Title = newv; break;
                        case "Comment": oldv = tag.Comment ?? ""; newv = oldv.Replace(sub, "", StringComparison.OrdinalIgnoreCase); tag.Comment = newv; break;
                        case "Artist": var arr = tag.Performers ?? Array.Empty<string>(); var arr2 = arr.Select(a => a.Replace(sub, "", StringComparison.OrdinalIgnoreCase)).ToArray(); oldv = string.Join("|", arr); newv = string.Join("|", arr2); tag.Performers = arr2; break;
                        case "Genre": var g = tag.Genres ?? Array.Empty<string>(); var g2 = g.Select(a => a.Replace(sub, "", StringComparison.OrdinalIgnoreCase)).ToArray(); oldv = string.Join("|", g); newv = string.Join("|", g2); tag.Genres = g2; break;
                    }
                    tf.Save();
                    var after = TagLibSvc.ReadTags(file.FullPath);
                    var log = new { timestamp = DateTime.UtcNow, file = file.FullPath, field = field, before = oldv, after = newv, afterTags = after, result = "ok" };
                    logs.Add(log);
                    await LoggerSvc.AppendOperationAsync(log);
                }
                catch (Exception ex)
                {
                    var log = new { timestamp = DateTime.UtcNow, file = file.FullPath, field = field, error = ex.Message, result = "error" };
                    logs.Add(log);
                    await LoggerSvc.AppendOperationAsync(log);
                }
            }
            MessageBox.Show($"Operação concluída. Logs adicionados em: {SettingsSvc.OperationsLog}", "Concluído");
        }

        public void SavePreset()
        {
            var dlg = new Views.PromptWindow("Nome do preset:", "Salvar preset", "MeuPreset");
            if (dlg.ShowDialog() != true) return;
            var name = dlg.Result ?? ""; if (string.IsNullOrWhiteSpace(name)) return;
            var p = new Preset { Name = name };
            foreach (var f in FieldViewModels) p.Values[f.Key] = f.Value ?? "";
            foreach (var f in FieldViewModels.Where(x => x.IsChecked)) p.CheckedFields.Add(f.Key);
            PresetSvc.AddOrReplace(p);
            Presets.Clear(); foreach (var pp in PresetSvc.GetAll()) Presets.Add(pp);
            MessageBox.Show("Preset salvo.");
        }

        public void DeletePreset()
        {
            if (SelectedPreset == null) { MessageBox.Show("Selecione um preset."); return; }
            PresetSvc.Remove(SelectedPreset.Name); Presets.Remove(SelectedPreset); SelectedPreset = null;
        }

        public void ExportReport() => MessageBox.Show($"Os logs de operações estão em: {SettingsSvc.OperationsLog}");

        public void ToggleTheme()
        {
            isDark = !isDark;
            try
            {
                var dict = new ResourceDictionary();
                var basePath = SettingsSvc.AppFolder;
                var resPath = Path.Combine(basePath, "Resources", isDark ? "Dark.xaml" : "Light.xaml");
                dict.Source = new Uri(resPath, UriKind.Absolute);
                Application.Current.Resources.MergedDictionaries.Clear();
                Application.Current.Resources.MergedDictionaries.Add(dict);
            }
            catch { }
        }

        public void OpenCapability()
        {
            var path = Path.Combine(SettingsSvc.AppFolder, "capability_matrix.json");
            try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); } catch { MessageBox.Show("Não foi possível abrir capability_matrix.json."); }
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
                if (total == 0) { fvm.IsEnabled = true; fvm.Tooltip = "Sem arquivos"; }
                else if (supported == 0) { fvm.IsEnabled = ShowHiddenFields; fvm.Tooltip = ShowHiddenFields ? $"Suportado 0/{total}" : "Oculto"; }
                else if (supported < total) { fvm.IsEnabled = true; fvm.Tooltip = $"Suportado {supported}/{total}"; }
                else { fvm.IsEnabled = true; fvm.Tooltip = "Suportado em todos"; }
            }
            OnPropertyChanged(nameof(FieldViewModels));
        }

        public void RestoreBackupDialog()
        {
            MessageBox.Show("Funcionalidade de restauração via backup desabilitada (não criamos .bak). Use operations_log.jsonl para auditoria.");
        }

        public void OpenLogs()
        {
            try
            {
                var p = SettingsSvc.OperationsLog;
                Process.Start(new ProcessStartInfo { FileName = p, UseShellExecute = true });
            }
            catch { MessageBox.Show("Não foi possível abrir logs."); }
        }
    }
}
