using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinForms = System.Windows.Forms;

namespace OneClickConverter;

public partial class MainWindow : Window
{
    private string _outputFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    private bool _isConverting;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private LowLevelKeyboardProc? _keyboardProc;
    private LowLevelMouseProc? _mouseProc;
    private bool _shiftDown;
    private bool _exitRequested;
    private readonly bool _startInTray;
    private WheelOverlayWindow? _wheelWindow;
    private WinForms.NotifyIcon? _trayIcon;
    private WinForms.ContextMenuStrip? _trayMenu;
    private System.Drawing.Icon? _applicationIcon;
    private System.Windows.Threading.DispatcherTimer? _dragCloseTimer;

    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int WmLButtonUp = 0x0202;
    private const int VkShift = 0x10;
    private const int VkLShift = 0xA0;
    private const int VkRShift = 0xA1;

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr message, IntPtr data);
    private delegate IntPtr LowLevelMouseProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardData { public uint VirtualKey; public uint ScanCode; public uint Flags; public uint Time; public IntPtr ExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookEx", SetLastError = true)] private static extern IntPtr SetWindowsHookExMouse(int idHook, LowLevelMouseProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr GetModuleHandle(string? moduleName);

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }

    public ObservableCollection<ConversionFile> Files { get; } = [];
    public string OutputFolder => _outputFolder;

    public MainWindow(bool startInTray = false)
    {
        _startInTray = startInTray;
        InitializeComponent();
        DataContext = this;
        OutputFolderText.Text = _outputFolder;
        Files.CollectionChanged += (_, _) => UpdateFileSummary();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _keyboardProc = KeyboardHookCallback;
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, GetModuleHandle(null), 0);
        _mouseProc = MouseHookCallback;
        _mouseHook = SetWindowsHookExMouse(WhMouseLl, _mouseProc, GetModuleHandle(null), 0);
        ConfigureStartupRegistration();
        InitializeTrayIcon();
        if (_keyboardHook == IntPtr.Zero)
            StatusText.Text = "The global Shift shortcut could not be enabled. You can still use the app normally.";
        if (_startInTray)
        {
            ShowInTaskbar = false;
            Hide();
        }
    }

    private void InitializeTrayIcon()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
                _applicationIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath);
        }
        catch { }
        _trayMenu = new WinForms.ContextMenuStrip();
        var openItem = new WinForms.ToolStripMenuItem("Open One Click Converter");
        openItem.Click += (_, _) => ShowMainWindow();
        var startupItem = new WinForms.ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = IsStartupEnabled()
        };
        startupItem.CheckedChanged += (_, _) => SetStartupEnabled(startupItem.Checked);
        var exitItem = new WinForms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            _exitRequested = true;
            Close();
        };
        _trayMenu.Items.Add(openItem);
        _trayMenu.Items.Add(new WinForms.ToolStripSeparator());
        _trayMenu.Items.Add(startupItem);
        _trayMenu.Items.Add(new WinForms.ToolStripSeparator());
        _trayMenu.Items.Add(exitItem);

        _trayIcon = new WinForms.NotifyIcon
        {
            Text = "One Click Converter — Shift-wheel ready",
            Icon = _applicationIcon ?? System.Drawing.SystemIcons.Application,
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private static Microsoft.Win32.RegistryKey? StartupKey(bool writable) =>
        Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable);

    private static bool IsStartupEnabled()
    {
        try
        {
            using var key = StartupKey(false);
            return key?.GetValue("OneClickConverter") is string;
        }
        catch { return false; }
    }

    private void ConfigureStartupRegistration()
    {
        try
        {
            using var settings = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\OneClickConverter");
            if (settings?.GetValue("StartupConfigured") is null)
            {
                SetStartupEnabled(true);
                settings?.SetValue("StartupConfigured", 1, Microsoft.Win32.RegistryValueKind.DWord);
            }
            else if (IsStartupEnabled())
            {
                SetStartupEnabled(true); // Refresh the registered executable path after an update or move.
            }
        }
        catch { }
    }

    private static void SetStartupEnabled(bool enabled)
    {
        try
        {
            using var key = StartupKey(true);
            if (key is null) return;
            if (enabled && !string.IsNullOrWhiteSpace(Environment.ProcessPath))
                key.SetValue("OneClickConverter", $"\"{Environment.ProcessPath}\" --background");
            else key.DeleteValue("OneClickConverter", false);
        }
        catch { }
    }

    private void ShowMainWindow()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_exitRequested) return;
        e.Cancel = true;
        ShowInTaskbar = false;
        Hide();
        _trayIcon?.ShowBalloonTip(1800, "One Click Converter", "Still running in the system tray. Hold Shift over a selected file.", WinForms.ToolTipIcon.Info);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = IntPtr.Zero;
        _mouseHook = IntPtr.Zero;
        _dragCloseTimer?.Stop();
        CloseWheel();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _applicationIcon?.Dispose();
        _trayMenu?.Dispose();
    }

    private IntPtr KeyboardHookCallback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var key = Marshal.PtrToStructure<KeyboardData>(data).VirtualKey;
            var isShift = key is VkShift or VkLShift or VkRShift;
            var msg = message.ToInt32();
            if (isShift && (msg == WmKeyDown || msg == WmSysKeyDown) && !_shiftDown)
            {
                _shiftDown = true;
                Dispatcher.BeginInvoke(ShowExplorerWheel);
            }
            else if (isShift && (msg == WmKeyUp || msg == WmSysKeyUp))
            {
                _shiftDown = false;
                Dispatcher.BeginInvoke(() =>
                {
                    if (_wheelWindow is { IsDragging: false }) CloseWheel();
                });
            }
        }
        return CallNextHookEx(_keyboardHook, code, message, data);
    }

    private IntPtr MouseHookCallback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && message.ToInt32() == WmLButtonUp)
            Dispatcher.BeginInvoke(QueueDragClose);
        return CallNextHookEx(_mouseHook, code, message, data);
    }

    private void QueueDragClose()
    {
        if (_wheelWindow is not { IsDragging: true }) return;
        _dragCloseTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _dragCloseTimer.Stop();
        _dragCloseTimer.Tick -= DragCloseTimer_Tick;
        _dragCloseTimer.Tick += DragCloseTimer_Tick;
        _dragCloseTimer.Start();
    }

    private void DragCloseTimer_Tick(object? sender, EventArgs e)
    {
        _dragCloseTimer?.Stop();
        if (_wheelWindow is { IsDragging: true }) CloseWheel();
    }

    private void ShowExplorerWheel()
    {
        var paths = GetSelectedExplorerFiles();
        if (paths.Count == 0) return;
        var targets = GetCommonTargets(paths);
        if (targets.Count == 0) return;

        if (!GetCursorPos(out var cursor)) return;
        if (_wheelWindow is null) _wheelWindow = new WheelOverlayWindow(this);
        _wheelWindow.SetOptions(paths, targets);

        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var width = _wheelWindow.Width;
        var height = _wheelWindow.Height;
        var left = cursor.X / dpi.DpiScaleX + 80;
        var top = cursor.Y / dpi.DpiScaleY - height / 2;
        var work = SystemParameters.WorkArea;
        _wheelWindow.Left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - width));
        _wheelWindow.Top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - height));
        if (!_wheelWindow.IsVisible) _wheelWindow.Show();
    }

    public void CloseWheel()
    {
        var wheel = _wheelWindow;
        _wheelWindow = null;
        if (wheel is not null && !wheel.Dispatcher.HasShutdownStarted) wheel.Close();
    }

    private static IReadOnlyList<string> GetSelectedExplorerFiles()
    {
        var selectedPaths = new List<string>();
        object? shell = null;
        object? windows = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return selectedPaths;
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return selectedPaths;
            dynamic shellDynamic = shell;
            windows = shellDynamic.Windows();
            dynamic windowsDynamic = windows;
            var foreground = GetForegroundWindow().ToInt64();
            for (var i = 0; i < (int)windowsDynamic.Count; i++)
            {
                object? window = null;
                object? document = null;
                object? selected = null;
                try
                {
                    window = windowsDynamic.Item(i);
                    if (window is null || Convert.ToInt64(((dynamic)window).HWND) != foreground) continue;
#pragma warning disable CS8600, CS8602 // Shell COM dispatch members are unannotated; null is checked before use.
                    object? documentResult = ((dynamic)window).Document;
#pragma warning restore CS8600, CS8602
                    document = documentResult;
                    if (document is null) continue;
                    selected = ((dynamic)document).SelectedItems();
                    dynamic selectedDynamic = selected;
                    for (var itemIndex = 0; itemIndex < (int)selectedDynamic.Count; itemIndex++)
                    {
                        object? item = null;
                        try
                        {
                            item = selectedDynamic.Item(itemIndex);
                            var path = Convert.ToString(((dynamic)item!).Path);
                            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && ConversionService.IsSupportedInput(path)) selectedPaths.Add(path);
                        }
                        catch { }
                        finally { ReleaseCom(item); }
                    }
                }
                catch { }
                finally { ReleaseCom(selected); ReleaseCom(document); ReleaseCom(window); }
            }
        }
        catch { }
        finally { ReleaseCom(windows); ReleaseCom(shell); }
        return selectedPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try { Marshal.FinalReleaseComObject(value); } catch { }
        }
    }

    private static IReadOnlyList<FormatChoice> GetCommonTargets(IReadOnlyList<string> paths)
    {
        return ConversionService.GetTargetsForSelection(paths);
    }

    public async Task ConvertFromWheelAsync(IEnumerable<string> paths, string format)
    {
        Files.Clear();
        AddFiles(paths);
        CloseWheel();
        await ConvertFilesAsync(format);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose files to convert",
            Multiselect = true,
            Filter = "All files (*.*)|*.*|Supported formats|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.avif;*.heic;*.heif;*.svg;*.pdf;*.txt;*.md;*.markdown;*.html;*.htm;*.csv;*.tsv;*.srt;*.vtt;*.mp3;*.wav;*.flac;*.ogg;*.m4a;*.aac;*.opus;*.wma;*.aiff;*.aif;*.mp4;*.mkv;*.webm;*.mov;*.avi;*.wmv;*.mpeg;*.mpg;*.doc;*.docx;*.odt;*.rtf;*.xls;*.xlsx;*.ods;*.ppt;*.pptx;*.odp;*.zip;*.gz;*.tar;*.tgz;*.tar.gz;*.7z;*.rar"
        };
        if (dialog.ShowDialog(this) == true) AddFiles(dialog.FileNames);
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose where converted files should be saved" };
        if (Directory.Exists(_outputFolder)) dialog.InitialDirectory = _outputFolder;
        if (dialog.ShowDialog(this) == true)
        {
            _outputFolder = dialog.FolderName;
            OutputFolderText.Text = _outputFolder;
        }
    }

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Copy;
    }

    private void DropZone_DragOver(object sender, System.Windows.DragEventArgs e) => Window_DragOver(sender, e);
    private void Window_Drop(object sender, System.Windows.DragEventArgs e) => HandleDrop(e);
    private void DropZone_Drop(object sender, System.Windows.DragEventArgs e) => HandleDrop(e);

    private void HandleDrop(System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        AddFiles(paths);
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        var known = Files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ignored = 0;
        foreach (var path in paths)
        {
            if (!File.Exists(path) || !known.Add(path)) continue;
            if (!ConversionService.IsSupportedInput(path)) { ignored++; continue; }
            try { Files.Add(new ConversionFile(path)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        UpdateFileSummary();
        if (ignored > 0) StatusText.Text = $"Added supported files; skipped {ignored} unsupported file{(ignored == 1 ? "" : "s")}.";
        else if (Files.Count > 0)
            StatusText.Text = ConversionService.GetEngineHint(Files[0].Path)
                ?? $"{Files.Count} file{(Files.Count == 1 ? "" : "s")} ready. Formats are matched to your selection.";
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ConversionFile file) Files.Remove(file);
        UpdateFileSummary();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Files.Clear();
        StatusText.Text = "Ready when you are.";
        UpdateFileSummary();
    }

    private async void Convert_Click(object sender, RoutedEventArgs e) => await ConvertFilesAsync();

    private async Task ConvertFilesAsync(string? requestedFormat = null)
    {
        if (Files.Count == 0 || _isConverting) return;
        if (requestedFormat is not null) FormatCombo.SelectedValue = requestedFormat;
        if (FormatCombo.SelectedValue is not string format)
        {
            StatusText.Text = "No compatible output format is available for this selection.";
            return;
        }
        Directory.CreateDirectory(_outputFolder);
        _isConverting = true;
        ConvertButton.IsEnabled = false;
        OpenFolderButton.IsEnabled = false;
        var succeeded = 0;
        var failed = 0;
        string? firstError = null;
        try
        {
            if (format is "mergepdf" or "imagespdf")
            {
                var outputPath = GetUniqueOutputPath(_outputFolder, Path.GetFileNameWithoutExtension(Files[0].Path), "pdf");
                foreach (var item in Files) item.Status = "Converting…";
                try
                {
                    var paths = Files.Select(item => item.Path).ToArray();
                    await Task.Run(() => ConversionService.ConvertSelection(paths, outputPath, format));
                    foreach (var item in Files) item.Status = "Done";
                    succeeded = Files.Count;
                }
                catch (Exception ex)
                {
                    foreach (var item in Files) item.Status = "Could not convert";
                    firstError = ex.Message;
                    failed = Files.Count;
                }
            }
            else
            {
            foreach (var item in Files)
            {
                item.Status = "Converting…";
                try
                {
                    var outputExtension = ConversionService.GetOutputExtension(item.Path, format);
                    var outputPath = GetUniqueOutputPath(_outputFolder, Path.GetFileNameWithoutExtension(item.Path), outputExtension);
                    await Task.Run(() => ConversionService.ConvertSelection([item.Path], outputPath, format));
                    item.Status = "Done";
                    succeeded++;
                }
                catch (Exception ex)
                {
                    item.Status = "Could not convert";
                    firstError ??= ex.Message;
                    failed++;
                }
            }
            }
        }
        finally
        {
            _isConverting = false;
            ConvertButton.IsEnabled = Files.Count > 0 && FormatCombo.SelectedValue is string;
            OpenFolderButton.IsEnabled = succeeded > 0;
            StatusText.Text = failed == 0
                ? $"Converted {succeeded} file{(succeeded == 1 ? "" : "s")} to {format.ToUpperInvariant()}."
                : $"Converted {succeeded}; {failed} failed. {firstError}";
        }
    }

    private static string GetUniqueOutputPath(string folder, string name, string extension)
    {
        var candidate = extension switch
        {
            "extract" or "folder" => Path.Combine(folder, $"{name}_extracted"),
            "pages" => Path.Combine(folder, $"{name}_pages"),
            _ => Path.Combine(folder, $"{name}.{extension}")
        };
        var counter = 2;
        while (File.Exists(candidate) || Directory.Exists(candidate))
            candidate = extension switch
            {
                "extract" or "folder" => Path.Combine(folder, $"{name}_extracted ({counter++})"),
                "pages" => Path.Combine(folder, $"{name}_pages ({counter++})"),
                _ => Path.Combine(folder, $"{name} ({counter++}).{extension}")
            };
        return candidate;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(_outputFolder)) Process.Start(new ProcessStartInfo("explorer.exe", _outputFolder) { UseShellExecute = true });
    }

    private void UpdateFileSummary()
    {
        FileCountText.Text = Files.Count.ToString();
        EmptyHint.Visibility = Files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        IReadOnlyList<FormatChoice> compatible = Files.Count == 0
            ? Array.Empty<FormatChoice>()
            : Files.Select(file => ConversionService.GetTargets(file.Path))
                .Aggregate((left, right) => left.Where(item => right.Any(other => other.Extension == item.Extension)).ToArray());
        FormatCombo.ItemsSource = compatible;
        FormatCombo.DisplayMemberPath = nameof(FormatChoice.Name);
        FormatCombo.SelectedValuePath = nameof(FormatChoice.Extension);
        FormatCombo.SelectedIndex = compatible.Count > 0 ? 0 : -1;
        ConvertButton.IsEnabled = Files.Count > 0 && compatible.Count > 0 && !_isConverting;
        if (Files.Count > 0 && compatible.Count == 0)
            StatusText.Text = "These files need an additional local conversion tool. See the README for setup.";
    }

}

public sealed class ConversionFile : INotifyPropertyChanged
{
    private string _status = "Ready";
    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    public string ExtensionLabel => System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();
    public string SizeLabel { get; }
    public string Status { get => _status; set { _status = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status))); } }
    public event PropertyChangedEventHandler? PropertyChanged;

    public ConversionFile(string path)
    {
        Path = path;
        var bytes = new FileInfo(path).Length;
        SizeLabel = bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024d:0.#} KB",
            _ => $"{bytes / (1024d * 1024):0.#} MB"
        };
    }
}

