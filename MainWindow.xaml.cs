using System.Net;
using System.Threading;
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace FreeTurnClient;

public partial class MainWindow : Window
{
    private readonly string _runtimeRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PickmeTurn", "Runtime");
    private Process? _freeTurn;
    private string? _tunnelName;
    private string? _configPath;
    private readonly StringBuilder _freeTurnLog = new();
    private bool _busy;
    private string _profileName = "";
    private readonly List<string> _temporaryDirectRoutes = new();
    private const int MaxTurnStreams = 12;
    private int _activeTurnAllocations;
    private int _localRelayPort = 9000;
    private TaskCompletionSource<bool> _routeReadyField = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _sessionMonitorCts;
    private readonly SemaphoreSlim _disconnectLock = new(1, 1);
    private readonly string _profilesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PickmeTurn", "profiles.dat");
    private readonly List<FreeTurnProfile> _profiles = new();
    private Guid? _selectedProfileId;
    private bool _profileEditorUpdating;
    private bool _allowWindowClose;
    private readonly DispatcherTimer _diagnosticsTimer;
    private bool _diagnosticsRefreshing;
    private Grid? _activeUtilityView;
    private Grid? _returnView;
    private bool _checkingUpdate;
    private const string UpdateRepository = "dimchach/PickmeTurn";
    private int _setupStep = 1;
    private bool _drawerOpen;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _trayIdleIcon;
    private System.Drawing.Icon? _trayConnectedIcon;
    private System.Windows.Forms.ToolStripMenuItem? _trayStatusItem;
    private System.Windows.Forms.ToolStripMenuItem? _trayConnectItem;

    public MainWindow()
    {
        InitializeComponent();

        _diagnosticsTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _diagnosticsTimer.Tick += DiagnosticsTimer_Tick;

        var appVersion = Assembly.GetExecutingAssembly().GetName().Version;
        InfoVersionText.Text = $"Версия {appVersion?.ToString(3) ?? "неизвестна"}";

        InitializeTray();
        MigrateLegacyStorage();
        LoadProfiles();
        ApplySelectedProfile();
        RefreshProfileList();

        if (_profiles.Count == 0)
        {
            ShowSetupWizard();
        }
        else
        {
            ShowHomeView();
            SetStatus($"Ожидание — {_profileName}", "Выберите профиль и подключитесь", StatusState.Waiting);
        }

        UpdateConnectButtonState();
        Closing += MainWindow_Closing;
    }

    private void MigrateLegacyStorage()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var oldRoot = Path.Combine(localAppData, "FreeTurnClient");
            var newRoot = Path.Combine(localAppData, "PickmeTurn");

            if (!Directory.Exists(oldRoot))
                return;

            if (!Directory.Exists(newRoot))
            {
                Directory.Move(oldRoot, newRoot);
                return;
            }

            // If PickmeTurn already exists, migrate only missing legacy data.
            var oldProfiles = Path.Combine(oldRoot, "profiles.dat");
            var newProfiles = Path.Combine(newRoot, "profiles.dat");
            if (File.Exists(oldProfiles) && !File.Exists(newProfiles))
                File.Move(oldProfiles, newProfiles);

            var oldRuntime = Path.Combine(oldRoot, "Runtime");
            var newRuntime = Path.Combine(newRoot, "Runtime");
            if (Directory.Exists(oldRuntime) && !Directory.Exists(newRuntime))
                Directory.Move(oldRuntime, newRuntime);

            try
            {
                if (Directory.Exists(oldRoot) &&
                    Directory.GetFileSystemEntries(oldRoot).Length == 0)
                {
                    Directory.Delete(oldRoot);
                }
            }
            catch
            {
                // Legacy cleanup is best-effort; it must not affect startup.
            }
        }
        catch
        {
            // Storage migration is best-effort; existing legacy data remains untouched on failure.
        }
    }

    private void InitializeTray()
    {
        _trayIdleIcon = LoadTrayIcon("Assets/FreeTurn.ico");
        _trayConnectedIcon = LoadTrayIcon("Assets/FreeTurn-connected.ico");

        var fallback = System.Drawing.SystemIcons.Application;
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "PickmeTurn",
            Visible = true,
            Icon = _trayIdleIcon ?? fallback
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        _trayStatusItem = new System.Windows.Forms.ToolStripMenuItem("Статус: Ожидание") { Enabled = false };
        _trayConnectItem = new System.Windows.Forms.ToolStripMenuItem("Подключиться");
        _trayConnectItem.Click += (_, _) => Dispatcher.InvokeAsync(() => ConnectButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        var openItem = new System.Windows.Forms.ToolStripMenuItem("Открыть PickmeTurn");
        openItem.Click += (_, _) => Dispatcher.Invoke(ShowMainWindow);
        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Выйти");
        exitItem.Click += async (_, _) => await ExitApplicationAsync();

        menu.Items.Add(_trayStatusItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(_trayConnectItem);
        menu.Items.Add(openItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMainWindow);
    }

    private static System.Drawing.Icon? LoadTrayIcon(string resourceName)
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(new Uri(resourceName, UriKind.Relative));
            if (resource == null) return null;
            using var input = resource.Stream;
            using var buffer = new MemoryStream();
            input.CopyTo(buffer);
            buffer.Position = 0;
            return new System.Drawing.Icon(buffer);
        }
        catch
        {
            return null;
        }
    }

    private void SetVisualIconState(bool connected)
    {
        // The executable's Explorer icon stays the stable blue FreeTurn mascot.
        // Runtime window/taskbar and tray icons follow the connection state.
        try
        {
            var resourceName = connected ? "Assets/FreeTurn-connected.ico" : "Assets/FreeTurn.ico";
            var resource = System.Windows.Application.GetResourceStream(new Uri(resourceName, UriKind.Relative));
            if (resource != null)
            {
                using var stream = resource.Stream;
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Icon = frame;
            }
        }
        catch
        {
            // Keep the previous window icon if a runtime icon cannot be loaded.
        }

        if (_trayIcon != null)
        {
            var target = connected ? _trayConnectedIcon : _trayIdleIcon;
            if (target != null)
                _trayIcon.Icon = target;
        }
    }

    private void ShowMainWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowWindowClose) return;
        e.Cancel = true;
        Hide();
        _trayIcon?.ShowBalloonTip(1200, "PickmeTurn", "Приложение продолжает работать в фоне", System.Windows.Forms.ToolTipIcon.Info);
    }

    private async Task ExitApplicationAsync()
    {
        if (_allowWindowClose) return;
        _allowWindowClose = true;
        await DisconnectAsync(updateUi: false, forceFreeTurnCleanup: true);
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _trayIdleIcon?.Dispose();
        _trayIdleIcon = null;
        _trayConnectedIcon?.Dispose();
        _trayConnectedIcon = null;
        Application.Current.Shutdown();
    }

    private void WindowExit_Click(object sender, RoutedEventArgs e) => _ = ExitApplicationAsync();

    private async void InfoButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowUtilityViewAsync(InfoView);
    }

    private async void DiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowUtilityViewAsync(DiagnosticsView);
    }

    private async Task ShowUtilityViewAsync(Grid view)
    {
        if (_drawerOpen)
            ShowProfilesDrawer(false);

        _returnView = HomeView.Visibility == Visibility.Visible
            ? HomeView
            : SetupView.Visibility == Visibility.Visible
                ? SetupView
                : null;

        HomeView.Visibility = Visibility.Collapsed;
        SetupView.Visibility = Visibility.Collapsed;
        DiagnosticsView.Visibility = Visibility.Collapsed;
        InfoView.Visibility = Visibility.Collapsed;

        _activeUtilityView = view;
        view.Opacity = 0;
        view.Visibility = Visibility.Visible;

        var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140));
        view.BeginAnimation(System.Windows.UIElement.OpacityProperty, fade);

        if (view == DiagnosticsView)
        {
            _diagnosticsTimer.Start();
            await RefreshDiagnosticsViewAsync();
        }
        else if (view == InfoView)
        {
            UpdateStatusText.Visibility = Visibility.Collapsed;
            UpdateStatusText.Text = "";
            CheckUpdateButton.IsEnabled = true;
            CheckUpdateButton.Content = "Проверить обновления";
            _checkingUpdate = false;
        }
    }

    private async void BackFromUtilityView_Click(object sender, RoutedEventArgs e)
    {
        await ReturnFromUtilityViewAsync();
    }

    private async Task ReturnFromUtilityViewAsync()
    {
        _diagnosticsTimer.Stop();

        var current = _activeUtilityView;
        if (current != null)
        {
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(100));
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fadeOut.Completed += (_, _) => tcs.TrySetResult(true);
            current.BeginAnimation(System.Windows.UIElement.OpacityProperty, fadeOut);
            await tcs.Task;
            current.Visibility = Visibility.Collapsed;
            current.Opacity = 1;
        }

        var target = _returnView ?? HomeView;
        _activeUtilityView = null;
        _returnView = null;

        if (target == SetupView)
        {
            SetupView.Visibility = Visibility.Visible;
            SetupView.Opacity = 0;
            SetupView.BeginAnimation(System.Windows.UIElement.OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        }
        else
        {
            HomeView.Visibility = Visibility.Visible;
            HomeView.Opacity = 0;
            HomeView.BeginAnimation(System.Windows.UIElement.OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        }
    }

    private async void DiagnosticsTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshDiagnosticsViewAsync();
    }

    private async Task RefreshDiagnosticsViewAsync()
    {
        if (_diagnosticsRefreshing || DiagnosticsView.Visibility != Visibility.Visible)
            return;

        _diagnosticsRefreshing = true;
        try
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(DiagnosticsTextBox);
            var previousOffset = scrollViewer?.VerticalOffset ?? 0;
            var wasAtBottom = scrollViewer == null ||
                              scrollViewer.ScrollableHeight - scrollViewer.VerticalOffset <= 4;

            var snapshot = await BuildDiagnosticsSnapshotAsync();
            DiagnosticsTextBox.Text = snapshot;

            // Keep the user's current position while reading the log. Only follow
            // new log lines automatically when the user was already at the bottom.
            await Dispatcher.InvokeAsync(() =>
            {
                var viewer = FindVisualChild<ScrollViewer>(DiagnosticsTextBox);
                if (viewer == null)
                    return;

                if (wasAtBottom)
                    DiagnosticsTextBox.ScrollToEnd();
                else
                    viewer.ScrollToVerticalOffset(Math.Min(previousOffset, viewer.ScrollableHeight));
            }, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            DiagnosticsTextBox.Text = ex.ToString();
        }
        finally
        {
            _diagnosticsRefreshing = false;
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null)
            return null;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;

            var result = FindVisualChild<T>(child);
            if (result != null)
                return result;
        }

        return null;
    }

    internal Task ExitForUpdateAsync() => ExitApplicationAsync();

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        OpenExternalUrl(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_checkingUpdate) return;

        _checkingUpdate = true;
        CheckUpdateButton.IsEnabled = false;
        CheckUpdateButton.Content = "Проверяю…";
        UpdateStatusText.Visibility = Visibility.Visible;
        UpdateStatusText.Text = "";

        try
        {
            using var http = CreateUpdateHttpClient();
            var json = await http.GetStringAsync($"https://api.github.com/repos/{UpdateRepository}/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString()?.Trim() ?? "";
            var releaseName = root.GetProperty("name").GetString() ?? tag;
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            var latestText = tag.TrimStart('v', 'V');

            if (!Version.TryParse(latestText, out var latest))
            {
                UpdateStatusText.Text = "Не удалось определить версию последнего релиза.";
                return;
            }

            if (latest <= current)
            {
                UpdateStatusText.Text = $"Установлена актуальная версия.";
                return;
            }

            var asset = FindInstallerAsset(root, latestText);
            if (asset is null)
            {
                UpdateStatusText.Text = $"Доступна версия {latest.ToString(3)}, но установщик не найден. Открою страницу релиза.";
                if (MessageBox.Show(this, "Установщик релиза не найден. Открыть страницу GitHub?",
                    "Обновление", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                    OpenExternalUrl($"https://github.com/{UpdateRepository}/releases/tag/{tag}");
                return;
            }

            var checksum = asset.Value.digest ??
                ExtractSha256(root.GetProperty("body").GetString() ?? "", asset.Value.name);

            var answer = MessageBox.Show(this,
                $"Доступно обновление {releaseName} ({latest.ToString(3)}).\n\n" +
                "PickmeTurn скачает официальный установщик GitHub, проверит SHA-256 и запустит обновление. " +
                "Приложение будет закрыто.\n\nПродолжить?",
                "Доступно обновление", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (answer != MessageBoxResult.Yes) return;

            CheckUpdateButton.Content = "Скачиваю…";
            var installer = await DownloadAndVerifyUpdateAsync(http, asset.Value.url, checksum, latestText);
            UpdateStatusText.Text = "Установщик проверен. Перезапускаю приложение после обновления…";

            Process.Start(new ProcessStartInfo
            {
                FileName = installer,
                UseShellExecute = true,
                Verb = "runas"
            });

            await ExitForUpdateAsync();
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = "Не удалось проверить обновления автоматически.";
            if (MessageBox.Show(this,
                $"Ошибка обновления:\n{ex.Message}\n\nОткрыть страницу релизов GitHub?",
                "Обновление", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                OpenExternalUrl($"https://github.com/{UpdateRepository}/releases");
        }
        finally
        {
            _checkingUpdate = false;
            CheckUpdateButton.IsEnabled = true;
            CheckUpdateButton.Content = "Проверить обновления";
        }
    }

    private static HttpClient CreateUpdateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"PickmeTurn-UpdateChecker/{Assembly.GetExecutingAssembly().GetName().Version}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    private static (string name, string url, string? digest)? FindInstallerAsset(JsonElement root, string version)
    {
        var expected = $"PickmeTurn-Setup-{version}.exe";
        if (!root.TryGetProperty("assets", out var assets)) return null;

        foreach (var item in assets.EnumerateArray())
        {
            var name = item.GetProperty("name").GetString();
            if (!string.Equals(name, expected, StringComparison.OrdinalIgnoreCase))
                continue;

            var digest = item.TryGetProperty("digest", out var digestElement)
                ? digestElement.GetString()
                : null;

            if (!string.IsNullOrWhiteSpace(digest) &&
                digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                digest = digest[7..];

            return (name!, item.GetProperty("browser_download_url").GetString()!, digest);
        }

        return null;
    }

    private static string? ExtractSha256(string body, string assetName)
    {
        var escaped = Regex.Escape(assetName);
        var match = Regex.Match(body, $"(?is){escaped}.{{0,250}}?([0-9a-f]{{64}})");
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    private static async Task<string> DownloadAndVerifyUpdateAsync(
        HttpClient http, string url, string? expectedHash, string version)
    {
        if (string.IsNullOrWhiteSpace(expectedHash))
            throw new InvalidOperationException(
                "В релизе отсутствует SHA-256 установщика; автоматическое обновление остановлено.");

        var path = Path.Combine(Path.GetTempPath(),
            $"PickmeTurn-Setup-{version}-{Guid.NewGuid():N}.exe");

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using (var input = await response.Content.ReadAsStreamAsync())
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                     FileShare.None, 1024 * 64, useAsync: true))
        {
            await input.CopyToAsync(output);
        }

        await using var hashStream = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(hashStream)).ToLowerInvariant();

        if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(path); } catch { }
            throw new InvalidOperationException(
                "SHA-256 установщика не совпал с опубликованным checksum.");
        }

        return path;
    }

    private static void OpenExternalUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    internal Task<string> BuildDiagnosticsSnapshotAsync()
    {
        // The diagnostics view intentionally contains only the live FreeTurn log.
        // Detailed process/WireGuard state remains available in connection diagnostics files.
        return Task.FromResult(GetFreeTurnLog());
    }

    private void CopyDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = DiagnosticsTextBox.Text ?? string.Empty;
            if (string.IsNullOrEmpty(text))
                return;

            Clipboard.SetText(text);

            var original = CopyDiagnosticsButton.Content;
            CopyDiagnosticsButton.Content = new System.Windows.Shapes.Path
            {
                Width = 15,
                Height = 15,
                Stretch = Stretch.Uniform,
                Fill = Brushes.White,
                Data = System.Windows.Media.Geometry.Parse("M3,9 L7,13 L16,4 L18,6 L7,16 L1,10 Z")
            };

            _ = Task.Run(async () =>
            {
                await Task.Delay(900);
                await Dispatcher.InvokeAsync(() => CopyDiagnosticsButton.Content = original);
            });
        }
        catch
        {
            // Clipboard access can fail when another process temporarily owns it.
        }
    }

    private void LinkBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_busy || _profileEditorUpdating) return;
        UpdateCurrentProfileFromEditor(autoSave: false);
    }

    private void CallLinkBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_busy || _profileEditorUpdating) return;
        UpdateCurrentProfileFromEditor(autoSave: false);
    }

    private void UpdateCurrentProfileFromEditor(bool autoSave)
    {
        var profile = LinkBox.Text.Trim();
        var embeddedCallLink = TryExtractVkCallLink(profile);
        if (!string.IsNullOrWhiteSpace(embeddedCallLink) && string.IsNullOrWhiteSpace(CallLinkBox.Text.Trim()))
            CallLinkBox.Text = embeddedCallLink;

        _profileName = profile.Length > 0 ? (TryExtractProfileName(profile) ?? "Без названия") : "";
        CurrentProfileText.Text = string.IsNullOrWhiteSpace(_profileName) ? "—" : _profileName;
        var selected = _profiles.FirstOrDefault(x => x.Id == _selectedProfileId);
        if (selected != null)
        {
            selected.Name = _profileName.Length > 0 ? _profileName : selected.Name;
            selected.FreeTurnUri = profile;
            selected.CallLink = CallLinkBox.Text.Trim();
            if (autoSave) SaveProfiles();
            RefreshProfileList();
        }
        UpdateInputState();
    }

    private void LoadProfiles()
    {
        try
        {
            if (!File.Exists(_profilesPath)) return;
            var encrypted = File.ReadAllBytes(_profilesPath);
            var json = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var loaded = JsonSerializer.Deserialize<ProfileStore>(json);
            if (loaded?.Profiles != null)
                _profiles.AddRange(loaded.Profiles);
            _selectedProfileId = loaded?.SelectedProfileId;
            if (_selectedProfileId == null || !_profiles.Any(x => x.Id == _selectedProfileId))
                _selectedProfileId = _profiles.FirstOrDefault()?.Id;
        }
        catch
        {
            _profiles.Clear();
            _selectedProfileId = null;
        }
    }

    private void SaveProfiles()
    {
        try
        {
            var directory = Path.GetDirectoryName(_profilesPath)!;
            Directory.CreateDirectory(directory);
            var store = new ProfileStore { Profiles = _profiles, SelectedProfileId = _selectedProfileId };
            var json = JsonSerializer.SerializeToUtf8Bytes(store, new JsonSerializerOptions { WriteIndented = false });
            var encrypted = ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser);
            var tmp = _profilesPath + ".tmp";
            File.WriteAllBytes(tmp, encrypted);
            File.Move(tmp, _profilesPath, true);
        }
        catch { }
    }

    private void ApplySelectedProfile()
    {
        var profile = _profiles.FirstOrDefault(x => x.Id == _selectedProfileId);
        _profileEditorUpdating = true;
        try
        {
            LinkBox.Text = profile?.FreeTurnUri ?? "";
            CallLinkBox.Text = profile?.CallLink ?? TryExtractVkCallLink(LinkBox.Text) ?? "";
            _profileName = profile?.Name ?? (TryExtractProfileName(LinkBox.Text) ?? "");
        }
        finally { _profileEditorUpdating = false; }
        CurrentProfileText.Text = string.IsNullOrWhiteSpace(_profileName) ? "—" : _profileName;
        UpdateInputState();
    }

    private void RefreshProfileList()
    {
        ProfilesList.Items.Clear();
        foreach (var profile in _profiles)
        {
            var item = new ListBoxItem
            {
                Content = profile.Name,
                Tag = profile.Id,
                Padding = new Thickness(10, 8, 10, 8),
                FontSize = 14
            };
            ProfilesList.Items.Add(item);
            if (profile.Id == _selectedProfileId) ProfilesList.SelectedItem = item;
        }
        ProfileCountText.Text = _profiles.Count == 0 ? "Нет сохранённых профилей" : $"Профили · {_profiles.Count}";
        CurrentProfileText.Text = string.IsNullOrWhiteSpace(_profileName) ? "—" : _profileName;
    }

    private void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_busy || ProfilesList.SelectedItem is not ListBoxItem item || item.Tag is not Guid id) return;
        if (_selectedProfileId == id) return;
        SaveCurrentEditor();
        _selectedProfileId = id;
        ApplySelectedProfile();
        SaveProfiles();
        SetStatus($"Ожидание — {_profileName}", "Профиль выбран", StatusState.Waiting);
    }

    private void SaveCurrentEditor()
    {
        var selected = _profiles.FirstOrDefault(x => x.Id == _selectedProfileId);
        if (selected == null) return;
        selected.FreeTurnUri = LinkBox.Text.Trim();
        selected.CallLink = CallLinkBox.Text.Trim();
        selected.Name = TryExtractProfileName(selected.FreeTurnUri) ?? selected.Name;
        SaveProfiles();
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var uri = LinkBox.Text.Trim();
        var call = CallLinkBox.Text.Trim();
        if (!uri.StartsWith("freeturn://", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("Добавьте профиль", "Вставьте корректную ссылку freeturn://", StatusState.Error);
            return;
        }
        if (!IsVkCallLink(call))
        {
            SetStatus("Добавьте ссылку VK", "Для профиля нужна ссылка на звонок VK", StatusState.Error);
            return;
        }

        var selected = _profiles.FirstOrDefault(x => x.Id == _selectedProfileId);
        if (selected == null)
        {
            selected = new FreeTurnProfile { Id = Guid.NewGuid() };
            _profiles.Add(selected);
            _selectedProfileId = selected.Id;
        }
        selected.FreeTurnUri = uri;
        selected.CallLink = call;
        selected.Name = TryExtractProfileName(uri) ?? "Без названия";
        _profileName = selected.Name;
        SaveProfiles();
        RefreshProfileList();
        CurrentProfileText.Text = _profileName;
        SetStatus($"Ожидание — {_profileName}", "Профиль сохранён", StatusState.Waiting);
        ShowProfilesDrawer(false);
        UpdateConnectButtonState();
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SaveCurrentEditor();
        var profile = new FreeTurnProfile { Id = Guid.NewGuid(), Name = "Новый профиль" };
        _profiles.Add(profile);
        _selectedProfileId = profile.Id;
        ApplySelectedProfile();
        RefreshProfileList();
        ShowProfilesDrawer(true);
        LinkBox.Focus();
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _selectedProfileId == null) return;
        var index = _profiles.FindIndex(x => x.Id == _selectedProfileId);
        if (index < 0) return;
        _profiles.RemoveAt(index);
        _selectedProfileId = _profiles.ElementAtOrDefault(Math.Min(index, _profiles.Count - 1))?.Id;
        ApplySelectedProfile();
        RefreshProfileList();
        SaveProfiles();
        if (_profiles.Count == 0)
        {
            ShowSetupWizard();
        }
    }

    private void ProfilesToggle_Click(object sender, RoutedEventArgs e) => ShowProfilesDrawer(!_drawerOpen);

    private void ShowProfilesDrawer(bool show)
    {
        if (_profiles.Count == 0) return;
        _drawerOpen = show;
        ProfilesToggleButton.Content = show ? "Закрыть" : "Профили";

        if (show)
        {
            DrawerLayer.Visibility = Visibility.Visible;
            DrawerTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new System.Windows.Media.Animation.DoubleAnimation
                { From = 380, To = 0, Duration = TimeSpan.FromMilliseconds(220),
                  EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
        }
        else
        {
            var animation = new System.Windows.Media.Animation.DoubleAnimation
            { From = 0, To = 380, Duration = TimeSpan.FromMilliseconds(180),
              EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn } };
            animation.Completed += (_, _) => DrawerLayer.Visibility = Visibility.Collapsed;
            DrawerTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, animation);
        }
    }

    private void ShowHomeView()
    {
        SetupView.Visibility = Visibility.Collapsed;
        HomeView.Visibility = Visibility.Visible;
        _setupStep = 1;
    }

    private void ShowSetupWizard()
    {
        HomeView.Visibility = Visibility.Collapsed;
        DrawerLayer.Visibility = Visibility.Collapsed;
        _drawerOpen = false;
        SetupView.Visibility = Visibility.Visible;
        _setupStep = 1;
        SetupLinkBox.Text = "";
        SetupCallLinkBox.Text = "";
        UpdateSetupStepVisuals(false);
        SetupLinkBox.Focus();
    }

    private void SetupLinkBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_profileEditorUpdating) return;
        var embedded = TryExtractVkCallLink(SetupLinkBox.Text.Trim());
        if (!string.IsNullOrWhiteSpace(embedded) && string.IsNullOrWhiteSpace(SetupCallLinkBox.Text))
            SetupCallLinkBox.Text = embedded;

        var name = TryExtractProfileName(SetupLinkBox.Text.Trim());
        SetupProfileHint.Text = string.IsNullOrWhiteSpace(name)
            ? "Вставьте корректную ссылку freeturn://"
            : $"Профиль будет сохранён как «{name}».";
    }

    private void SetupCallLinkBox_TextChanged(object sender, TextChangedEventArgs e) { }

    private bool ValidateSetupStep(int step)
    {
        if (step == 1)
        {
            if (!SetupLinkBox.Text.Trim().StartsWith("freeturn://", StringComparison.OrdinalIgnoreCase))
            {
                SetupProfileHint.Text = "Вставьте корректную ссылку freeturn://";
                SetupLinkBox.Focus();
                return false;
            }
        }
        if (step == 2)
        {
            if (!IsVkCallLink(SetupCallLinkBox.Text.Trim()))
            {
                SetupSubtitle.Text = "Нужна действующая ссылка на звонок VK";
                SetupCallLinkBox.Focus();
                return false;
            }
        }
        return true;
    }

    private void SetupNext_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateSetupStep(_setupStep)) return;
        _setupStep++;
        if (_setupStep == 3)
        {
            var name = TryExtractProfileName(SetupLinkBox.Text.Trim()) ?? "Без названия";
            SetupSummaryName.Text = name;
            SetupSummaryCall.Text = SetupCallLinkBox.Text.Trim();
            SetupNextButton.Visibility = Visibility.Collapsed;
            SetupSaveButton.Visibility = Visibility.Visible;
        }
        UpdateSetupStepVisuals(true);
    }

    private void SetupBack_Click(object sender, RoutedEventArgs e)
    {
        if (_setupStep <= 1) return;
        _setupStep--;
        if (_setupStep < 3)
        {
            SetupNextButton.Visibility = Visibility.Visible;
            SetupSaveButton.Visibility = Visibility.Collapsed;
        }
        UpdateSetupStepVisuals(true);
    }

    private void SetupSave_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateSetupStep(1) || !ValidateSetupStep(2)) return;

        var profile = new FreeTurnProfile
        {
            Id = Guid.NewGuid(),
            FreeTurnUri = SetupLinkBox.Text.Trim(),
            CallLink = SetupCallLinkBox.Text.Trim(),
            Name = TryExtractProfileName(SetupLinkBox.Text.Trim()) ?? "Без названия"
        };
        _profiles.Add(profile);
        _selectedProfileId = profile.Id;
        _profileName = profile.Name;
        SaveProfiles();
        ApplySelectedProfile();
        RefreshProfileList();
        ShowHomeView();
        SetStatus($"Ожидание — {_profileName}", "Профиль сохранён • всё готово к подключению", StatusState.Waiting);
        UpdateConnectButtonState();
    }

    private void UpdateSetupStepVisuals(bool animate)
    {
        var panels = new[] { SetupStep1, SetupStep2, SetupStep3 };
        var target = panels[_setupStep - 1];

        Action apply = () =>
        {
            foreach (var panel in panels)
            {
                panel.Visibility = panel == target ? Visibility.Visible : Visibility.Collapsed;
                panel.Opacity = panel == target ? 1 : 0;
            }
            SetupBackButton.Visibility = _setupStep > 1 ? Visibility.Visible : Visibility.Collapsed;
            SetupSubtitle.Text = _setupStep switch
            {
                1 => "Добавим профиль за несколько шагов",
                2 => "Теперь укажем ссылку на VK Call",
                _ => "Проверьте данные и сохраните профиль"
            };

            var active = new[] { StepDot1, StepDot2, StepDot3 };
            for (var i = 0; i < active.Length; i++)
            {
                active[i].Background = i + 1 <= _setupStep ? (Brush)FindResource("Accent") : (Brush)FindResource("Border");
                if (active[i].Child is TextBlock t)
                    t.Foreground = i + 1 <= _setupStep ? Brushes.White : (Brush)FindResource("Muted");
            }
        };

        if (!animate)
        {
            apply();
            return;
        }

        var current = panels.FirstOrDefault(x => x.Visibility == Visibility.Visible);
        if (current == null || current == target)
        {
            apply();
            return;
        }

        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
        { From = 1, To = 0, Duration = TimeSpan.FromMilliseconds(120) };
        fadeOut.Completed += (_, _) =>
        {
            apply();
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
            { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(170),
              EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
            target.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };
        current.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void DrawerLayer_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource == DrawerLayer)
            ShowProfilesDrawer(false);
    }

    private sealed class FreeTurnProfile
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "Новый профиль";
        public string FreeTurnUri { get; set; } = "";
        public string CallLink { get; set; } = "";
    }

    private sealed class ProfileStore
    {
        public List<FreeTurnProfile> Profiles { get; set; } = new();
        public Guid? SelectedProfileId { get; set; }
    }

    private void UpdateInputState()
    {
        var profile = LinkBox.Text.Trim();
        var callLink = CallLinkBox.Text.Trim();
        var hasProfile = profile.StartsWith("freeturn://", StringComparison.OrdinalIgnoreCase);
        var hasCallLink = IsVkCallLink(callLink);
        var canConnect = hasProfile && hasCallLink && !_busy;

        UpdateConnectButtonState(canConnect);

        if (!hasProfile)
        {
            _profileName = "";
            SetStatus(
                "Добавьте первый профиль",
                "Вставьте профиль FreeTurn и ссылку на звонок VK",
                StatusState.Waiting);
            return;
        }

        if (string.IsNullOrWhiteSpace(_profileName))
            _profileName = TryExtractProfileName(profile) ?? "Без названия";

        if (!hasCallLink)
        {
            SetStatus(
                $"Ожидание — {_profileName}",
                "Добавьте ссылку на звонок VK для подключения",
                StatusState.Waiting);
            return;
        }

        SetStatus(
            $"Ожидание — {_profileName}",
            "Профиль и ссылка на звонок VK готовы к подключению",
            StatusState.Waiting);
    }

    private void UpdateConnectButtonState(bool? enabled = null)
    {
        if (_busy)
        {
            ConnectButton.IsEnabled = false;
            return;
        }

        var canConnect = enabled ??
            (LinkBox.Text.Trim().StartsWith("freeturn://", StringComparison.OrdinalIgnoreCase) &&
             IsVkCallLink(CallLinkBox.Text.Trim()));

        // Keep the button visibly disabled until both required inputs are valid.
        // The accent outline remains visible so it still belongs to the active UI.
        ConnectButton.IsEnabled = canConnect;
    }

    private void TelegramLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore shell/browser errors; the main application remains usable.
        }
        e.Handled = true;
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_tunnelName != null || (_freeTurn != null && !_freeTurn.HasExited))
        {
            _busy = true;
            ConnectButton.IsEnabled = false;
            ConnectButton.Content = "Отключение";
            await DisconnectAsync();
            return;
        }

        try
        {
            _busy = true;
            ConnectButton.IsEnabled = false;
            ConnectButton.Content = "Подключение";
            _profileName = TryExtractProfileName(LinkBox.Text.Trim()) ?? "Без названия";
            SetStatus($"Подключение — {_profileName}", "Проверяю профиль и подготавливаю туннель", StatusState.Connecting);

            var uri = LinkBox.Text.Trim();
            if (!uri.StartsWith("freeturn://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Нужна ссылка freeturn://");

            var callLink = CallLinkBox.Text.Trim();
            if (!IsVkCallLink(callLink))
                throw new InvalidOperationException(
                    "Укажите активную VK Call ссылку. Если она есть в freeturn:// профиле, клиент подставит её автоматически.");

            var originalWg = ExtractWireGuard(uri);
            var peerEndpoint = ExtractPeerEndpoint(originalWg);

            Directory.CreateDirectory(_runtimeRoot);
            var freeTurnExe = await ExtractResourceAsync("FreeTurnClient.Assets.client-windows-amd64.exe", "client-windows-amd64.exe");
            var wireguardExe = await ExtractResourceAsync("FreeTurnClient.Assets.wireguard.exe", "wireguard.exe");
            var wgExe = await ExtractResourceAsync("FreeTurnClient.Assets.wg.exe", "wg.exe");

            // A previous crashed/closed GUI can leave FreeTurn holding UDP 9000.
            // Clean up stale copies of our bundled relay, then choose a free
            // localhost UDP port and use the same port for WireGuard + FreeTurn.
            await StopStaleFreeTurnProcessesAsync(freeTurnExe);
            _localRelayPort = GetAvailableUdpPort(9000, 9100);
            var wg = NormalizeWireGuard(originalWg, _localRelayPort);

            // The tunnel service name is derived from the .conf filename by WireGuard.
            _tunnelName = $"FreeTurn{Guid.NewGuid():N}".Substring(0, 16);
            _configPath = Path.Combine(Path.GetTempPath(), _tunnelName + ".conf");
            await File.WriteAllTextAsync(_configPath, wg, new UTF8Encoding(false));

            SetStatus($"Подключение — {_profileName}", "Запускаю FreeTurn и подключаюсь к VK Call", StatusState.Connecting);
            _freeTurnLog.Clear();
            Interlocked.Exchange(ref _activeTurnAllocations, 0);
            UpdateStreamsText();
            _routeReadyField.TrySetCanceled();
            // Create a fresh readiness signal for this connection attempt.
            // The previous task may already be completed/canceled.
            _routeReadyField = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var psi = new ProcessStartInfo
            {
                FileName = freeTurnExe,
                WorkingDirectory = _runtimeRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
            };

            // freeturn:// carries the client/WireGuard parameters, but the
            // desktop CLI still requires an active VK Call URL separately.
            // Put option/value pairs before the positional freeturn:// URI.
            // This is important for clients/parsers that stop processing flags
            // after the first positional argument.
            // v1.1: let FreeTurn use its native automatic CAPTCHA solver.
            // The bundled core is updated to 4.0.1, which contains the
            // current CAPTCHA parsing fix. If automatic solving cannot
            // complete, FreeTurn falls back to its manual browser flow.
            // We intentionally do NOT pass -manual-captcha here.
            // Desktop is explicit so the auth persona matches Windows.
            psi.ArgumentList.Add("-platform");
            psi.ArgumentList.Add("desktop");
            psi.ArgumentList.Add("-n");
            psi.ArgumentList.Add(MaxTurnStreams.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-link");
            psi.ArgumentList.Add(callLink);
            // Pass the VPS peer explicitly as well as through freeturn://.
            // This removes any ambiguity in URI parsing and matches the
            // documented Windows invocation of the FreeTurn client.
            psi.ArgumentList.Add("-peer");
            psi.ArgumentList.Add(peerEndpoint);
            psi.ArgumentList.Add("-listen");
            psi.ArgumentList.Add($"127.0.0.1:{_localRelayPort}");
            psi.ArgumentList.Add("-routes");
            psi.ArgumentList.Add(uri);

            _freeTurn = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _freeTurn.OutputDataReceived += (_, a) => CaptureFreeTurnLine(a.Data);
            _freeTurn.ErrorDataReceived += (_, a) => CaptureFreeTurnLine(a.Data);
            _freeTurn.Start();
            _freeTurn.BeginOutputReadLine();
            _freeTurn.BeginErrorReadLine();

            // FreeTurn's own documentation says to enable the VPN only after
            // it has installed the route exception for the TURN/VPS path.
            // Waiting only for UDP :9000 is too early: the listener can exist
            // before the relay is actually ready.
            await WaitForFreeTurnRelayReadyAsync(TimeSpan.FromSeconds(180));
            await AddDirectRouteExceptionsAsync(originalWg);

            if (_freeTurn.HasExited)
                throw new InvalidOperationException(
                    $"FreeTurn завершился с кодом {_freeTurn.ExitCode}.\n{GetFreeTurnLog()}");

            SetStatus($"Подключение — {_profileName}", "Relay VK готов • запускаю WireGuard", StatusState.Connecting);

            // WireGuard for Windows bundles the signed WireGuardNT driver resources inside wireguard.exe.
            // /installtunnelservice creates a dedicated WireGuardTunnel$<name> service.
            var wgInstall = new ProcessStartInfo
            {
                FileName = wireguardExe,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            wgInstall.ArgumentList.Add("/installtunnelservice");
            wgInstall.ArgumentList.Add(_configPath);

            using (var p = Process.Start(wgInstall)!)
            {
                var stdout = await p.StandardOutput.ReadToEndAsync();
                var stderr = await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();
                if (p.ExitCode != 0)
                    throw new InvalidOperationException(
                        "WireGuard не смог установить туннель.\n" +
                        (string.IsNullOrWhiteSpace(stderr) ? stdout : stderr));
            }

            await WaitForServiceAsync("WireGuardTunnel$" + _tunnelName, TimeSpan.FromSeconds(15));

            // Do not claim success just because the Windows service is RUNNING.
            // A /0 WireGuard profile can activate the adapter while the relay/
            // handshake is still dead, which also triggers WireGuard's firewall
            // protection and makes Windows report "No Internet".
            SetStatus($"Подключение — {_profileName}", "Проверяю handshake и relay FreeTurn", StatusState.Connecting);
            await WaitForWireGuardHandshakeAsync(wgExe, _tunnelName, TimeSpan.FromSeconds(40));
            await CheckTunnelConnectivityAsync();

            SetStatus($"Подключено — {_profileName}", "WireGuard handshake подтверждён • трафик идёт через VPS", StatusState.Connected);
            ConnectButton.Content = "Отключиться";
            ConnectButton.IsEnabled = true;
            _busy = false;

            // Keep the UI session-aware without treating a later CAPTCHA
            // message as a disconnect. The FreeTurn core owns relay/session
            // recovery; PickmeTurn only reacts if the core process itself dies.
            StartFreeTurnSessionMonitor();
        }
        catch (Exception ex)
        {
            var diagnosticPath = await SaveConnectionDiagnosticsAsync(ex);
            await DisconnectAsync(updateUi: false, forceFreeTurnCleanup: true);
            var uiError = FormatErrorForUi(ex);
            if (ex.Message.Contains("handshake с VPS не получен", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("anonym_token.not_found", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("FATAL_CAPTCHA_FAILED_NO_STREAMS", StringComparison.OrdinalIgnoreCase))
                uiError += $"\nДиагностика сохранена: {diagnosticPath}";
            SetStatus($"Ошибка — {_profileName}", uiError, StatusState.Error);
            ConnectButton.Content = "Подключиться";
            _busy = false;
            UpdateConnectButtonState();
        }
    }

    private async Task DisconnectAsync(
        bool updateUi = true,
        bool forceFreeTurnCleanup = false)
    {
        await _disconnectLock.WaitAsync();
        try
        {
            _sessionMonitorCts?.Cancel();
            _sessionMonitorCts?.Dispose();
            _sessionMonitorCts = null;

            // Stop the WireGuard tunnel first. This removes the VPN path before
            // we terminate FreeTurn, so the relay never remains half-attached to
            // a dead tunnel.
            var tunnelName = _tunnelName;
            if (!string.IsNullOrWhiteSpace(tunnelName))
            {
                var wgExe = Path.Combine(_runtimeRoot, "wireguard.exe");
                if (File.Exists(wgExe))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = wgExe,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    psi.ArgumentList.Add("/uninstalltunnelservice");
                    psi.ArgumentList.Add(tunnelName);

                    try
                    {
                        using var p = Process.Start(psi);
                        if (p != null)
                        {
                            await p.WaitForExitAsync();
                            // Give the WireGuard service a short moment to
                            // release its interface/routes.
                            await Task.Delay(250);
                        }
                    }
                    catch { }
                }
            }

            // First terminate the exact Process object that belongs to this
            // connection attempt.
            var relay = _freeTurn;
            _freeTurn = null;

            if (relay != null)
            {
                try
                {
                    if (!relay.HasExited)
                    {
                        relay.Kill(entireProcessTree: true);
                        await relay.WaitForExitAsync();
                    }
                }
                catch { }

                try { relay.Dispose(); } catch { }
            }

            // A failed FreeTurn session may have started retry/worker goroutines
            // or survived a previous crash. Do a second, path-based sweep so
            // CAPTCHA/relay retries cannot continue after Disconnect or failure.
            if (forceFreeTurnCleanup)
            {
                try
                {
                    var freeTurnExe = Path.Combine(_runtimeRoot, "client-windows-amd64.exe");
                    await StopAllBundledFreeTurnProcessesAsync(freeTurnExe);
                }
                catch { }
            }

            // Remove any temporary local route exceptions only after FreeTurn
            // has been stopped.
            await RemoveTemporaryDirectRoutesAsync();

            if (_configPath != null)
            {
                try { File.Delete(_configPath); } catch { }
                _configPath = null;
            }

            _tunnelName = null;
            Interlocked.Exchange(ref _activeTurnAllocations, 0);
            UpdateStreamsText();
            IpText.Text = "";

            if (updateUi)
            {
                SetStatus(
                    $"Ожидание — {_profileName}",
                    string.IsNullOrWhiteSpace(_profileName)
                        ? "Вставьте профиль FreeTurn и ссылку на звонок VK"
                        : "VPN-туннель остановлен",
                    StatusState.Disconnected);

                ConnectButton.Content = "Подключиться";
                _busy = false;
                UpdateConnectButtonState();
            }
        }
        catch
        {
            // Cleanup must be best-effort and idempotent. Even if one cleanup
            // operation fails, never leave the UI in a connecting state.
            if (updateUi)
            {
                SetStatus(
                    $"Ожидание — {_profileName}",
                    string.IsNullOrWhiteSpace(_profileName)
                        ? "Вставьте профиль FreeTurn и ссылку на звонок VK"
                        : "VPN-туннель остановлен",
                    StatusState.Disconnected);

                ConnectButton.Content = "Подключиться";
                _busy = false;
                UpdateConnectButtonState();
            }
        }
        finally
        {
            _disconnectLock.Release();
        }
    }

    private static async Task StopAllBundledFreeTurnProcessesAsync(string clientExe)
    {
        var target = Path.GetFullPath(clientExe);
        var processName = Path.GetFileNameWithoutExtension(clientExe);

        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                string? path = null;
                try { path = process.MainModule?.FileName; } catch { }

                if (!string.IsNullOrWhiteSpace(path) &&
                    string.Equals(Path.GetFullPath(path), target, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        if (!process.HasExited)
                            process.Kill(entireProcessTree: true);
                    }
                    catch { }

                    try
                    {
                        await process.WaitForExitAsync();
                    }
                    catch { }
                }
            }
            catch { }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static bool IsVkCallLink(string value)
    {
        return value.StartsWith("https://vk.ru/call/", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("https://vk.com/call/", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("https://www.vk.ru/call/", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("https://www.vk.com/call/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryExtractVkCallLink(string uri)
    {
        if (!uri.StartsWith("freeturn://", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var payload = uri.Substring("freeturn://".Length).Trim();
            if (payload.Length == 0) return null;

            // First try the current FreeTurn URI format: base64url(JSON).
            var bytes = DecodeBase64Url(payload);
            var directText = Encoding.UTF8.GetString(bytes);

            var directUrl = ExtractVkUrlFromString(directText);
            if (directUrl != null) return directUrl;

            try
            {
                using var doc = JsonDocument.Parse(directText);
                var found = FindVkCallLink(doc.RootElement);
                if (found != null) return found;
            }
            catch
            {
                // Some older/third-party profiles may wrap their payload in
                // another encoding. Continue with the generic decoder below.
            }

            // Be liberal with imported profiles: walk text/base64 layers so a
            // VK URL that is merely encoded (not cryptographically encrypted)
            // can still be discovered.
            return FindVkCallLinkInEncodedText(directText, 0);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] DecodeBase64Url(string value)
    {
        value = value.Trim();

        var pad = value.Length % 4;
        if (pad != 0) value += new string('=', 4 - pad);

        value = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(value);
    }

    private static string? FindVkCallLinkInEncodedText(string text, int depth)
    {
        if (depth > 3 || string.IsNullOrWhiteSpace(text))
            return null;

        var direct = ExtractVkUrlFromString(text);
        if (direct != null) return direct;

        var candidates = new[]
        {
            text.Trim(),
            Uri.UnescapeDataString(text.Trim())
        };

        foreach (var candidate in candidates)
        {
            var url = ExtractVkUrlFromString(candidate);
            if (url != null) return url;

            try
            {
                using var doc = JsonDocument.Parse(candidate);
                var found = FindVkCallLink(doc.RootElement);
                if (found != null) return found;
            }
            catch { }

            // Search whitespace-separated tokens for base64/base64url blobs.
            foreach (var token in Regex.Split(candidate, @"[\s,;]+"))
            {
                var t = token.Trim('"', '\'', '[', ']', '{', '}', '(', ')');
                if (t.Length < 24) continue;

                try
                {
                    var decoded = DecodeBase64Url(t);
                    var decodedText = Encoding.UTF8.GetString(decoded);
                    var found = FindVkCallLinkInEncodedText(decodedText, depth + 1);
                    if (found != null) return found;
                }
                catch { }
            }
        }

        return null;
    }

    private static string? ExtractVkUrlFromString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        // Match VK Call URLs in plain text, JSON strings, URL-encoded wrappers,
        // or text that contains punctuation immediately after the URL.
        var match = Regex.Match(
            value,
            @"https://(?:www\.)?vk\.(?:ru|com)/call/[^\s""'<>]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!match.Success)
            return null;

        return match.Value.TrimEnd('.', ',', ';', ':', ')', ']', '}');
    }

    private static string? FindVkCallLink(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString();
                var direct = ExtractVkUrlFromString(value);
                if (direct != null) return direct;

                if (!string.IsNullOrWhiteSpace(value) && value!.Length >= 24)
                {
                    try
                    {
                        var decoded = Encoding.UTF8.GetString(DecodeBase64Url(value));
                        return FindVkCallLinkInEncodedText(decoded, 1);
                    }
                    catch { }
                }
                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var found = FindVkCallLink(property.Value);
                    if (found != null) return found;
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var found = FindVkCallLink(item);
                    if (found != null) return found;
                }
                break;
        }

        return null;
    }

    private static string? TryExtractProfileName(string uri)
    {
        if (!uri.StartsWith("freeturn://", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var payload = uri.Substring("freeturn://".Length).Trim();
            var bytes = DecodeBase64Url(payload);
            using var doc = JsonDocument.Parse(bytes);
            var root = doc.RootElement;

            foreach (var key in new[] { "name", "profile_name", "profileName", "client_name", "clientName" })
            {
                if (root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    var name = value.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                        return name;
                }
            }
        }
        catch { }

        return null;
    }

    private static string FormatErrorForUi(Exception ex)
    {
        var message = ex.Message.Trim();
        if (message.Contains("handshake с VPS не получен", StringComparison.OrdinalIgnoreCase))
            return "WireGuard не получил handshake от VPS. Проверяется relay FreeTurn.";
        if (message.Contains("anonym_token.not_found", StringComparison.OrdinalIgnoreCase))
            return "VK не выдал TURN после подтверждения капчи. Проверьте свежесть VK Call и попробуйте подключиться ещё раз.";
        if (message.Contains("FATAL_CAPTCHA_FAILED_NO_STREAMS", StringComparison.OrdinalIgnoreCase))
            return "FreeTurn не получил ни одного VK relay-потока после капчи.";
        if (message.Contains("не подтвердил подготовку маршрута", StringComparison.OrdinalIgnoreCase))
            return "FreeTurn не подготовил relay-маршрут. Проверьте VK Call и журнал FreeTurn.";
        if (message.Contains("нет TCP-доступа", StringComparison.OrdinalIgnoreCase))
            return "Handshake есть, но через VPN нет доступа в Интернет. Проверьте маршрутизацию на VPS.";
        return message.Length > 220 ? message[..220] + "…" : message;
    }

    private static string ExtractWireGuard(string uri)
    {
        var payload = uri.Substring("freeturn://".Length).Trim();
        if (payload.Length == 0) throw new InvalidOperationException("Пустая freeturn:// ссылка.");

        var pad = payload.Length % 4;
        if (pad != 0) payload += new string('=', 4 - pad);
        payload = payload.Replace('-', '+').Replace('_', '/');

        byte[] bytes;
        try { bytes = Convert.FromBase64String(payload); }
        catch { throw new InvalidOperationException("Некорректная base64url часть freeturn://."); }

        using var doc = JsonDocument.Parse(bytes);
        if (!doc.RootElement.TryGetProperty("wg", out var wg))
            throw new InvalidOperationException("В профиле нет встроенного WireGuard-конфига.");

        if (wg.ValueKind == JsonValueKind.String)
            return wg.GetString() ?? throw new InvalidOperationException("Поле wg пустое.");

        if (wg.ValueKind == JsonValueKind.Object)
            return wg.GetRawText();

        throw new InvalidOperationException("Неподдерживаемый формат поля wg.");
    }

    private static string ExtractPeerEndpoint(string config)
    {
        var lines = config.Replace("\r\n", "\n").Split('\n');
        var inPeer = false;

        foreach (var raw in lines)
        {
            var t = raw.Trim();
            if (t.Equals("[Peer]", StringComparison.OrdinalIgnoreCase))
            {
                inPeer = true;
                continue;
            }

            if (inPeer && t.StartsWith("[", StringComparison.Ordinal))
                break;

            if (inPeer && t.StartsWith("Endpoint", StringComparison.OrdinalIgnoreCase))
            {
                var eq = t.IndexOf('=');
                if (eq >= 0)
                {
                    var endpoint = t[(eq + 1)..].Trim();
                    if (!string.IsNullOrWhiteSpace(endpoint))
                        return endpoint;
                }
            }
        }

        throw new InvalidOperationException("В профиле не найден адрес VPS для FreeTurn.");
    }

    private static int GetAvailableUdpPort(int preferred, int max)
    {
        for (var port = preferred; port <= max; port++)
        {
            try
            {
                using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                return port;
            }
            catch (SocketException)
            {
            }
        }

        throw new InvalidOperationException(
            $"Не удалось найти свободный локальный UDP-порт в диапазоне {preferred}-{max}.");
    }

    private static async Task StopStaleFreeTurnProcessesAsync(string clientExe)
    {
        await StopAllBundledFreeTurnProcessesAsync(clientExe);
    }

    private string NormalizeWireGuard(string config, int localRelayPort)
    {
        // Current FreeTurn freeturn:// profiles contain wg-quick style text.
        // If an object was supplied, convert its common fields to wg-quick syntax.
        if (config.TrimStart().StartsWith("{"))
        {
            using var doc = JsonDocument.Parse(config);
            var r = doc.RootElement;
            var sb = new StringBuilder("[Interface]\n");
            Add(sb, "PrivateKey", r, "PrivateKey");
            Add(sb, "Address", r, "Address");
            Add(sb, "DNS", r, "DNS");
            Add(sb, "MTU", r, "MTU");

            sb.Append("\n[Peer]\n");
            Add(sb, "PublicKey", r, "PublicKey");
            Add(sb, "PresharedKey", r, "PresharedKey");
            Add(sb, "AllowedIPs", r, "AllowedIPs");
            Add(sb, "Endpoint", r, "Endpoint");
            Add(sb, "PersistentKeepalive", r, "PersistentKeepalive");
            config = sb.ToString();
        }

        var lines = config.Replace("\r\n", "\n").Split('\n').ToList();
        var inInterface = false;
        var inPeer = false;
        var endpointDone = false;
        var mtuDone = false;
        var allowedIpsDone = false;

        for (int i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.Equals("[Interface]", StringComparison.OrdinalIgnoreCase))
            {
                inInterface = true;
                inPeer = false;
            }
            else if (t.Equals("[Peer]", StringComparison.OrdinalIgnoreCase))
            {
                inInterface = false;
                inPeer = true;
            }
            else if (t.StartsWith("[") && t.EndsWith("]"))
            {
                inInterface = false;
                inPeer = false;
            }

            if (inPeer && t.StartsWith("AllowedIPs", StringComparison.OrdinalIgnoreCase) && t.Contains("="))
            {
                // WireGuard for Windows enables its restrictive kill-switch
                // when a single peer has 0.0.0.0/0 or ::/0. FreeTurn itself
                // must still reach the TURN servers outside the VPN. Use the
                // standard split-default form instead; it preserves a full
                // tunnel while avoiding the kill-switch/WFP block. The
                // FreeTurn -routes flag supplies the more specific /32
                // exceptions for the actual TURN servers.
                var key = t[..t.IndexOf('=')].Trim();
                if (!allowedIpsDone)
                {
                    lines[i] = $"{key} = 0.0.0.0/1, 128.0.0.0/1, ::/1, 8000::/1";
                    allowedIpsDone = true;
                }
            }

            if (t.StartsWith("Endpoint", StringComparison.OrdinalIgnoreCase) && t.Contains("="))
            {
                var key = t[..t.IndexOf('=')].Trim();
                if (!endpointDone)
                {
                    lines[i] = $"{key} = 127.0.0.1:{localRelayPort}";
                    endpointDone = true;
                }
            }

            if (inInterface && t.StartsWith("MTU", StringComparison.OrdinalIgnoreCase) && t.Contains("="))
            {
                lines[i] = "MTU = 1280";
                mtuDone = true;
            }
        }

        if (!endpointDone) throw new InvalidOperationException("В WireGuard-конфиге не найден Endpoint.");
        if (!allowedIpsDone) throw new InvalidOperationException("В WireGuard-конфиге не найден AllowedIPs.");
        if (!mtuDone)
        {
            var idx = lines.FindIndex(x => x.Trim().Equals("[Peer]", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) lines.Insert(idx, "MTU = 1280");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void Add(StringBuilder sb, string outKey, JsonElement obj, string key)
    {
        if (obj.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String)
            sb.Append(outKey).Append(" = ").Append(p.GetString()).Append('\n');
    }

    private async Task<string> ExtractResourceAsync(string resourceName, string fileName)
    {
        var target = Path.Combine(_runtimeRoot, fileName);
        if (File.Exists(target)) return target;

        await using var input = typeof(MainWindow).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Встроенный ресурс не найден: {resourceName}");
        await using var output = File.Create(target);
        await input.CopyToAsync(output);
        return target;
    }

    private async Task WaitForFreeTurnRelayReadyAsync(TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        var routeSeen = false;
        var captchaShown = false;

        while (DateTime.UtcNow - start < timeout)
        {
            if (_freeTurn != null && _freeTurn.HasExited)
            {
                throw new InvalidOperationException(
                    $"FreeTurn завершился с кодом {_freeTurn.ExitCode}.\n{GetFreeTurnLog()}");
            }

            var log = GetFreeTurnLog();

            if (log.Contains("Ensuring route to", StringComparison.OrdinalIgnoreCase))
                routeSeen = true;

            // CAPTCHA is part of the relay lifecycle. During initial
            // connection, automatic solving is preferred and manual browser
            // fallback is only shown when FreeTurn explicitly asks for it.
            var manualCaptchaRequired = log.Contains(
                "ACTION REQUIRED: manual captcha solving needed",
                StringComparison.OrdinalIgnoreCase);
            var manualCaptchaFallback = log.Contains(
                "Triggering manual captcha fallback",
                StringComparison.OrdinalIgnoreCase);
            var automaticCaptcha = log.Contains(
                "Solving VK Smart Captcha automatically",
                StringComparison.OrdinalIgnoreCase);

            if ((manualCaptchaRequired || manualCaptchaFallback) && !captchaShown)
            {
                captchaShown = true;
                SetStatus(
                    $"Подключение — {_profileName}",
                    manualCaptchaFallback
                        ? "Автокапча не прошла • подтверждите капчу VK в браузере"
                        : "Подтвердите капчу VK в открывшемся окне",
                    StatusState.Connecting);
            }
            else if (automaticCaptcha && !captchaShown)
            {
                SetStatus(
                    $"Подключение — {_profileName}",
                    "FreeTurn автоматически проходит капчу VK",
                    StatusState.Connecting);
            }

            if (log.Contains("Got token from browser", StringComparison.OrdinalIgnoreCase) && captchaShown)
            {
                SetStatus(
                    $"Подключение — {_profileName}",
                    "Капча пройдена • устанавливаем relay VK",
                    StatusState.Connecting);
            }

            // Relay-first gate: at least one TURN allocation must actually
            // exist before WireGuard is started.
            if (routeSeen && Volatile.Read(ref _activeTurnAllocations) > 0)
            {
                await Task.Delay(300);
                return;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException(
            captchaShown
                ? "Ожидание подтверждения капчи VK истекло. Попробуйте подключиться ещё раз."
                : "FreeTurn не поднял активный VK TURN relay за отведённое время.");
    }

    private async Task AddDirectRouteExceptionsAsync(string originalWg)
    {
        var dnsAddresses = ExtractDnsAddresses(originalWg)
            .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
            .Select(ip => ip.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (dnsAddresses.Count == 0)
            return;

        var gateway = GetPhysicalGatewayForRoute();

        foreach (var ip in dnsAddresses)
        {
            var result = await RunProcessAsync(
                "route.exe",
                new[] { "ADD", ip, "MASK", "255.255.255.255", gateway });

            if (result.ExitCode == 0)
            {
                lock (_temporaryDirectRoutes)
                    _temporaryDirectRoutes.Add(ip);
            }
        }
    }

    private async Task RemoveTemporaryDirectRoutesAsync()
    {
        string[] routes;
        lock (_temporaryDirectRoutes)
        {
            routes = _temporaryDirectRoutes.ToArray();
            _temporaryDirectRoutes.Clear();
        }

        foreach (var ip in routes)
        {
            try
            {
                await RunProcessAsync("route.exe", new[] { "DELETE", ip });
            }
            catch { }
        }
    }

    private static List<IPAddress> ExtractDnsAddresses(string config)
    {
        var result = new List<IPAddress>();
        var lines = config.Replace("\r\n", "\n").Split('\n');
        var inInterface = false;

        foreach (var raw in lines)
        {
            var t = raw.Trim();

            if (t.Equals("[Interface]", StringComparison.OrdinalIgnoreCase))
            {
                inInterface = true;
                continue;
            }

            if (t.StartsWith("[") && t.EndsWith("]") &&
                !t.Equals("[Interface]", StringComparison.OrdinalIgnoreCase))
            {
                inInterface = false;
            }

            if (!inInterface ||
                !t.StartsWith("DNS", StringComparison.OrdinalIgnoreCase) ||
                !t.Contains('='))
                continue;

            var value = t[(t.IndexOf('=') + 1)..];
            foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (IPAddress.TryParse(part, out var ip))
                    result.Add(ip);
            }
        }

        return result;
    }

    private static string GetPhysicalGatewayForRoute()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up ||
                ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                continue;

            try
            {
                var gateway = ni.GetIPProperties().GatewayAddresses
                    .Select(x => x.Address)
                    .FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork &&
                                         !IPAddress.IsLoopback(x));

                if (gateway != null)
                    return gateway.ToString();
            }
            catch { }
        }

        throw new InvalidOperationException(
            "Не удалось определить физический шлюз Windows для исключения DNS из VPN.");
    }

    private async Task WaitForWireGuardHandshakeAsync(string wgExe, string tunnelName, TimeSpan timeout)
    {
        var startedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var deadline = DateTime.UtcNow + timeout;
        string lastError = "";

        while (DateTime.UtcNow < deadline)
        {
            if (_freeTurn != null && _freeTurn.HasExited)
            {
                throw new InvalidOperationException(
                    $"FreeTurn завершился во время WireGuard handshake (код {_freeTurn.ExitCode}).\n{await GetDiagnosticsAsync(wgExe, tunnelName)}");
            }

            try
            {
                var result = await RunProcessAsync(wgExe, new[] { "show", tunnelName, "latest-handshakes" });
                if (result.ExitCode == 0)
                {
                    foreach (var line in result.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 2) continue;

                        var timestampText = parts[^1];
                        if (ulong.TryParse(timestampText, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp) &&
                            timestamp >= (ulong)Math.Max(0, startedAt - 3))
                        {
                            SetStatus($"Подключение — {_profileName}", "Handshake получен • проверяю Интернет", StatusState.Connecting);
                            return;
                        }
                    }
                }
                else
                {
                    lastError = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }

            await Task.Delay(500);
        }

        var diagnostic = await GetDiagnosticsAsync(wgExe, tunnelName);
        if (!string.IsNullOrWhiteSpace(lastError))
            diagnostic += "\nwg.exe: " + lastError.Trim();

        throw new TimeoutException(
            "WireGuard-интерфейс запущен, но handshake с VPS не получен за 40 секунд.\n" +
            "Это означает, что проблема находится между локальным WireGuard и relay FreeTurn, а не в проверке внешнего IP.\n\n" +
            diagnostic);
    }

    private async Task<string> SaveConnectionDiagnosticsAsync(Exception error)
    {
        try
        {
            var logDir = Path.Combine(_runtimeRoot, "Logs");
            Directory.CreateDirectory(logDir);
            var path = Path.Combine(logDir, "last-connection-diagnostics.txt");
            var sb = new StringBuilder();
            sb.AppendLine("FreeTurn Client connection diagnostics");
            sb.AppendLine(DateTimeOffset.Now.ToString("O"));
            sb.AppendLine();
            sb.AppendLine("Error:");
            sb.AppendLine(error.ToString());
            sb.AppendLine();
            sb.AppendLine("Profile:");
            sb.AppendLine(string.IsNullOrWhiteSpace(_profileName) ? "(unknown)" : _profileName);
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(_tunnelName))
            {
                var wgExe = Path.Combine(_runtimeRoot, "wg.exe");
                if (File.Exists(wgExe))
                {
                    foreach (var what in new[] { "latest-handshakes", "transfer", "endpoints" })
                    {
                        try
                        {
                            var r = await RunProcessAsync(wgExe, new[] { "show", _tunnelName, what });
                            sb.AppendLine($"WireGuard {what}:");
                            sb.AppendLine(string.IsNullOrWhiteSpace(r.StdOut) ? r.StdErr.Trim() : r.StdOut.Trim());
                            sb.AppendLine();
                        }
                        catch (Exception e)
                        {
                            sb.AppendLine($"WireGuard {what}: {e.Message}");
                        }
                    }
                }
            }

            sb.AppendLine($"Local FreeTurn UDP endpoint: 127.0.0.1:{_localRelayPort}");
            sb.AppendLine();
            sb.AppendLine("Failure cleanup: WireGuard uninstall + FreeTurn process-tree shutdown");
            sb.AppendLine();
            sb.AppendLine("Relay readiness: relay-first / captcha-aware");
            sb.AppendLine();
            sb.AppendLine("Active TURN allocations:");
            sb.AppendLine(Volatile.Read(ref _activeTurnAllocations).ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();

            sb.AppendLine("Temporary direct routes:");
            lock (_temporaryDirectRoutes)
                sb.AppendLine(_temporaryDirectRoutes.Count == 0
                    ? "(none)"
                    : string.Join(", ", _temporaryDirectRoutes));
            sb.AppendLine();

            sb.AppendLine("FreeTurn log:");
            sb.AppendLine(GetFreeTurnLog());
            sb.AppendLine();

            // Capture the Windows route table so we can verify that the actual
            // VPS/peer address remains outside the default WireGuard route.
            try
            {
                var route = await RunProcessAsync("route.exe", new[] { "print" });
                sb.AppendLine("Windows route print:");
                sb.AppendLine(route.StdOut.Trim());
            }
            catch (Exception e)
            {
                sb.AppendLine("route.exe: " + e.Message);
            }

            await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }
        catch
        {
            return "не удалось сохранить";
        }
    }

    private async Task CheckTunnelConnectivityAsync()
    {
        using var tcp = new TcpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));

        try
        {
            await tcp.ConnectAsync("1.1.1.1", 443, timeout.Token);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Handshake WireGuard есть, но через туннель нет TCP-доступа в Интернет.\n" +
                "Проверьте маршрутизацию/конфигурацию VPS.\n\nFreeTurn:\n" + GetFreeTurnLog(), ex);
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var ip = (await http.GetStringAsync("https://api.ipify.org", timeout.Token)).Trim();
            if (!System.Net.IPAddress.TryParse(ip, out _))
                throw new InvalidOperationException("Сервис вернул некорректный внешний IP.");

            IpText.Text = "Внешний IPv4: " + ip;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Handshake есть и TCP работает, но проверить внешний IPv4 не удалось.\n" +
                "Возможна проблема DNS/HTTPS после установки туннеля.", ex);
        }
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(string fileName, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Не удалось запустить {Path.GetFileName(fileName)}.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private async Task<string> GetDiagnosticsAsync(string wgExe, string tunnelName, bool includeFreeTurnLog = true)
    {
        var sb = new StringBuilder();

        try
        {
            var hs = await RunProcessAsync(wgExe, new[] { "show", tunnelName, "latest-handshakes" });
            sb.AppendLine("WireGuard latest-handshakes:");
            sb.AppendLine(string.IsNullOrWhiteSpace(hs.StdOut) ? "(нет данных)" : hs.StdOut.Trim());
        }
        catch (Exception ex)
        {
            sb.AppendLine("WireGuard handshake: " + ex.Message);
        }

        try
        {
            var tr = await RunProcessAsync(wgExe, new[] { "show", tunnelName, "transfer" });
            sb.AppendLine("WireGuard transfer:");
            sb.AppendLine(string.IsNullOrWhiteSpace(tr.StdOut) ? "(нет данных)" : tr.StdOut.Trim());
        }
        catch (Exception ex)
        {
            sb.AppendLine("WireGuard transfer: " + ex.Message);
        }

        try
        {
            var ep = await RunProcessAsync(wgExe, new[] { "show", tunnelName, "endpoints" });
            sb.AppendLine("WireGuard endpoint:");
            sb.AppendLine(string.IsNullOrWhiteSpace(ep.StdOut) ? "(нет данных)" : ep.StdOut.Trim());
        }
        catch (Exception ex)
        {
            sb.AppendLine("WireGuard endpoint: " + ex.Message);
        }

        if (includeFreeTurnLog)
        {
            sb.AppendLine("\nFreeTurn:");
            sb.Append(GetFreeTurnLog());
        }

        return sb.ToString().Trim();
    }

    private async Task WaitForUdpPortAsync(string host, int port, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            if (_freeTurn != null && _freeTurn.HasExited)
            {
                throw new InvalidOperationException(
                    $"FreeTurn завершился с кодом {_freeTurn.ExitCode}.\n{GetFreeTurnLog()}");
            }

            try
            {
                var listeners = System.Net.NetworkInformation.IPGlobalProperties
                    .GetIPGlobalProperties()
                    .GetActiveUdpListeners();

                if (listeners.Any(ep => ep.Port == port &&
                    (System.Net.IPAddress.IsLoopback(ep.Address) ||
                     ep.Address.Equals(System.Net.IPAddress.Any) ||
                     ep.Address.Equals(System.Net.IPAddress.IPv6Any))))
                    return;
            }
            catch { }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"FreeTurn не поднял локальный relay 127.0.0.1:{port}.\n" +
            GetFreeTurnLog());
    }

    private static async Task WaitForServiceAsync(string serviceName, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            var psi = new ProcessStartInfo("sc.exe", $"query \"{serviceName}\"")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using var p = Process.Start(psi)!;
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            if (output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase)) return;
            await Task.Delay(300);
        }
        throw new TimeoutException("WireGuard tunnel service не запустился.");
    }

    private void StartFreeTurnSessionMonitor()
    {
        _sessionMonitorCts?.Cancel();
        _sessionMonitorCts?.Dispose();
        var cts = new CancellationTokenSource();
        _sessionMonitorCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);

                    var relay = _freeTurn;
                    if (relay == null || relay.HasExited)
                    {
                        if (cts.Token.IsCancellationRequested)
                            return;

                        var dispatcherOperation = Dispatcher.InvokeAsync(async () =>
                        {
                            if (!_busy && _tunnelName == null)
                                return;

                            SetStatus(
                                $"Ошибка — {_profileName}",
                                "FreeTurn завершился во время активного подключения.",
                                StatusState.Error);
                            ConnectButton.Content = "Подключиться";
                            _busy = false;
                            await DisconnectAsync(updateUi: false, forceFreeTurnCleanup: true);
                            SetStatus(
                                $"Ошибка — {_profileName}",
                                "Relay FreeTurn завершился. Подключитесь повторно.",
                                StatusState.Error);
                            UpdateConnectButtonState();
                        });
                        await dispatcherOperation.Task.Unwrap();
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // Monitoring must never become a reason to tear down a healthy
                // tunnel. The FreeTurn core remains the authority for relay
                // recovery and liveness.
            }
        }, cts.Token);
    }

    private async Task CheckExternalIpAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var ip = (await http.GetStringAsync("https://api.ipify.org")).Trim();
            IpText.Text = "Внешний IPv4: " + ip;
        }
        catch
        {
            IpText.Text = "Туннель активен; внешний IP проверить не удалось.";
        }
    }

    private void CaptureFreeTurnLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        lock (_freeTurnLog)
        {
            _freeTurnLog.AppendLine(line);
            if (_freeTurnLog.Length > 12000)
                _freeTurnLog.Remove(0, _freeTurnLog.Length - 12000);
        }

        if (line.Contains("Ensuring route to", StringComparison.OrdinalIgnoreCase))
            _routeReadyField.TrySetResult(true);

        var allocationChanged = false;
        if (line.Contains("TURN allocation up:", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _activeTurnAllocations);
            allocationChanged = true;
        }
        else if (line.Contains("TURN allocation released:", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Exchange(ref _activeTurnAllocations,
                Math.Max(0, Volatile.Read(ref _activeTurnAllocations) - 1));
            allocationChanged = true;
        }

        if (allocationChanged)
            Dispatcher.BeginInvoke(UpdateStreamsText);
    }

    private void UpdateStreamsText()
    {
        var active = Math.Clamp(Volatile.Read(ref _activeTurnAllocations), 0, MaxTurnStreams);
        StreamsText.Text = $"Потоки: {active}/{MaxTurnStreams}";
        StreamsText.Visibility = _tunnelName != null || active > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private string GetFreeTurnLog()
    {
        lock (_freeTurnLog)
            return _freeTurnLog.ToString().Trim();
    }

    private enum StatusState
    {
        Waiting,
        Connecting,
        Connected,
        Error,
        Disconnected
    }

    private void SetStatus(string status, string detail, StatusState state)
    {
        StatusText.Text = status;
        DetailText.Text = detail;

        var mascotConnected = state == StatusState.Connected;
        SetVisualIconState(mascotConnected);
        var fade = new Duration(TimeSpan.FromMilliseconds(180));
        WaitingMascot.BeginAnimation(System.Windows.UIElement.OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(mascotConnected ? 0 : 1, fade));
        ConnectedMascot.BeginAnimation(System.Windows.UIElement.OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(mascotConnected ? 1 : 0, fade));

        var brush = state switch
        {
            StatusState.Connected => Brushes.LimeGreen,
            StatusState.Error => Brushes.IndianRed,
            StatusState.Connecting => Brushes.Gold,
            StatusState.Disconnected => Brushes.Gray,
            _ => Brushes.DodgerBlue
        };
        ConnectButton.Background = brush;
        ConnectButton.BorderBrush = brush;
        ConnectButton.Foreground = Brushes.White;
        ConnectButton.Content = state switch
        {
            StatusState.Connecting => "Подключение",
            StatusState.Connected => "Отключиться",
            _ => "Подключиться"
        };
        StreamsText.Visibility = state == StatusState.Connected
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (state == StatusState.Connected)
            UpdateStreamsText();
        UpdateTrayState();
    }

    private void UpdateTrayState()
    {
        if (_trayStatusItem == null || _trayConnectItem == null) return;
        _trayStatusItem.Text = StatusText.Text;
        var connected = _tunnelName != null;
        _trayConnectItem.Text = connected ? "Отключиться" : "Подключиться";
    }

}



