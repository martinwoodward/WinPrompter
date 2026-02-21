using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinPrompter.Helpers;
using WinPrompter.Models;
using WinPrompter.Services;

namespace WinPrompter;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly MarkdownService _markdownService = new();
    private readonly SettingsService _settingsService = new();
    private WebViewBridge? _bridge;
    private DispatcherTimer? _overlayHideTimer;
    private readonly VoiceAdvanceService _voiceService = new();
    private readonly ScriptWordIndex _scriptWordIndex = new();
    private TrayIconHelper? _trayIcon;
    private CancellationTokenSource? _pipeCts;

    public MainWindow()
    {
        this.InitializeComponent();

        // Load saved settings
        _vm.LoadSettings(_settingsService);

        // Set up overlay auto-hide timer
        _overlayHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _overlayHideTimer.Tick += (_, _) =>
        {
            if (!_vm.IsVoiceMode) // Keep overlay visible while voice is active
                HideOverlay();
            _overlayHideTimer.Stop();
        };

        // Populate theme combo
        for (int i = 0; i < MainViewModel.ThemeDisplayNames.Length; i++)
            ThemeCombo.Items.Add(MainViewModel.ThemeDisplayNames[i]);
        ThemeCombo.SelectedIndex = Array.IndexOf(MainViewModel.AvailableThemes, _vm.CurrentTheme);

        // Update UI from view model
        SpeedText.Text = $"{_vm.Speed:F2}x";
        FontSizeText.Text = $"{_vm.FontSize:F0}";
        OpacitySlider.Value = _vm.Opacity * 100;

        // Configure window
        WindowHelper.ConfigureAsFloatingPrompter(this);
        if (_vm.Opacity < 1.0)
            WindowHelper.SetOpacity(this, _vm.Opacity);

        // Reduce opacity when window loses focus
        this.Activated += (_, args) =>
        {
            double baseOpacity = _vm.Opacity;
            if (args.WindowActivationState == WindowActivationState.Deactivated)
                WindowHelper.SetOpacity(this, Math.Max(0.1, baseOpacity - 0.2));
            else
                WindowHelper.SetOpacity(this, baseOpacity);
        };

        // Hide from taskbar, show in system tray
        var appWindow = WindowHelper.GetAppWindow(this);
        appWindow.IsShownInSwitchers = false;

        _trayIcon = new TrayIconHelper(this);
        _trayIcon.ShowRequested += () => DispatcherQueue.TryEnqueue(() =>
        {
            this.Activate();
        });
        _trayIcon.ExitRequested += () => DispatcherQueue.TryEnqueue(() =>
        {
            ExitApp();
        });

        // Wire up voice auto-advance events
        _voiceService.PositionAdvanced += charOffset =>
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                if (_bridge != null)
                    await _bridge.ScrollToWordIndexAsync(charOffset);
            });
        };
        _voiceService.ListeningChanged += listening =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _vm.IsVoiceMode = listening;
                VoiceIndicator.Visibility = listening ? Visibility.Visible : Visibility.Collapsed;
                BtnVoice.IsChecked = listening;
            });
        };
        _voiceService.RecognizedText += text =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                VoiceStatusText.Text = text.Length > 40 ? text[..40] + "…" : text;
            });
        };
        _voiceService.ErrorOccurred += err =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                VoiceStatusText.Text = err;
            });
        };

        // Keyboard handling on the root grid
        RootGrid.KeyDown += RootGrid_KeyDown;
        RootGrid.PointerPressed += RootGrid_PointerPressed;

        // Start named pipe listener for single-instance file passing
        StartPipeListener();

        // Initialize WebView2
        InitializeWebViewAsync();
    }

    private async void InitializeWebViewAsync()
    {
        try
        {
            await PrompterWebView.EnsureCoreWebView2Async();

            var packagePath = AppContext.BaseDirectory;
            PrompterWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app.local",
                Path.Combine(packagePath, "Assets", "Web"),
                CoreWebView2HostResourceAccessKind.Allow);

            // Disable dev tools and context menu for clean UX
            PrompterWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            PrompterWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            PrompterWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            PrompterWebView.CoreWebView2.Settings.IsZoomControlEnabled = false;

            _bridge = new WebViewBridge(PrompterWebView);
            _bridge.Initialize();
            _bridge.MessageReceived += OnWebViewMessage;

            PrompterWebView.CoreWebView2.Navigate("https://app.local/teleprompter.html");

            // Wait for page load then apply settings
            PrompterWebView.CoreWebView2.NavigationCompleted += async (_, _) =>
            {
                try
                {
                    await _bridge.SetThemeAsync(_vm.CurrentTheme);
                    await _bridge.SetFontSizeAsync(_vm.FontSize);
                    await _bridge.SetSpeedAsync(_vm.Speed);
                    await _bridge.SetMirrorAsync(_vm.IsMirrorMode);

                    // Show welcome message if no script loaded
                    if (!_vm.HasScript)
                    {
                        // Check for startup file from command line
                        if (App.StartupFilePath != null && File.Exists(App.StartupFilePath))
                        {
                            await LoadFileFromPathAsync(App.StartupFilePath);
                            App.StartupFilePath = null;
                        }
                        else
                        {
                            var welcomeHtml = "<div class='welcome-message'><h1>WinPrompter</h1><p>Right-click or hover at bottom for controls<br/>Ctrl+O to open · Ctrl+V to paste</p></div>";
                            await _bridge.SetContentAsync(welcomeHtml);
                        }
                    }
                }
                catch (Exception ex) { App.LogCrash("NavCompleted", ex); }
            };
        }
        catch (Exception ex) { App.LogCrash("InitWebView", ex); }
    }

    private void OnWebViewMessage(string json)
    {
        try
        {
            var cleaned = json.Trim('"').Replace("\\\"", "\"").Replace("\\\\", "\\");
            ProcessMessageJson(cleaned);
        }
        catch
        {
            // Sometimes the JSON from WebView2 is double-encoded
            try
            {
                var unescaped = JsonSerializer.Deserialize<string>(json);
                if (unescaped != null)
                    ProcessMessageJson(unescaped);
            }
            catch { /* truly malformed, ignore */ }
        }
    }

    private void ProcessMessageJson(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;
        var type = root.GetProperty("type").GetString();

        // Extract ALL values from JsonDocument NOW, before it's disposed
        double progressPercent = 0;
        bool isPlaying = false;
        string key = "";
        bool ctrl = false, alt = false;

        switch (type)
        {
            case "progress":
                progressPercent = root.GetProperty("percent").GetDouble();
                break;
            case "playbackChanged":
                isPlaying = root.GetProperty("isPlaying").GetBoolean();
                break;
            case "keydown":
                key = root.GetProperty("key").GetString() ?? "";
                ctrl = root.GetProperty("ctrl").GetBoolean();
                alt = root.GetProperty("alt").GetBoolean();
                break;
        }

        // Now dispatch to UI thread with only primitive values (no JsonElement refs)
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (type)
            {
                case "progress":
                    _vm.Progress = progressPercent;
                    break;
                case "playbackChanged":
                    _vm.IsPlaying = isPlaying;
                    PlayIcon.Glyph = _vm.IsPlaying ? "\uE769" : "\uE768";
                    break;
                case "scrollComplete":
                    _vm.IsPlaying = false;
                    PlayIcon.Glyph = "\uE768";
                    break;
                case "keydown":
                    ProcessShortcut(key, ctrl, alt);
                    break;
                case "rightclick":
                    if (_vm.IsOverlayVisible) HideOverlay(); else ShowOverlay();
                    break;
            }
        });
    }

    private async void ProcessShortcut(string key, bool ctrl, bool alt)
    {
        if (_bridge == null) return;

        if (key == " ")
        {
            await TogglePlayPause();
        }
        else if (key == "Escape")
        {
            await StopPlayback();
        }
        else if (ctrl && (key == "+" || key == "="))
        {
            _vm.IncreaseFontSize();
            await _bridge.SetFontSizeAsync(_vm.FontSize);
            FontSizeText.Text = $"{_vm.FontSize:F0}";
            SaveSettings();
        }
        else if (ctrl && key == "-")
        {
            _vm.DecreaseFontSize();
            await _bridge.SetFontSizeAsync(_vm.FontSize);
            FontSizeText.Text = $"{_vm.FontSize:F0}";
            SaveSettings();
        }
        else if (alt && (key == "ArrowUp" || key == "Up"))
        {
            _vm.IncreaseSpeed();
            await _bridge.SetSpeedAsync(_vm.Speed);
            SpeedText.Text = $"{_vm.Speed:F2}x";
            SaveSettings();
        }
        else if (alt && (key == "ArrowDown" || key == "Down"))
        {
            _vm.DecreaseSpeed();
            await _bridge.SetSpeedAsync(_vm.Speed);
            SpeedText.Text = $"{_vm.Speed:F2}x";
            SaveSettings();
        }
        else if (key == "F11" || (!ctrl && !alt && key == "f"))
        {
            WindowHelper.ToggleFullscreen(this);
        }
        else if (!ctrl && !alt && key == "m")
        {
            _vm.ToggleMirror();
            await _bridge.SetMirrorAsync(_vm.IsMirrorMode);
            BtnMirror.IsChecked = _vm.IsMirrorMode;
            SaveSettings();
        }
        else if (ctrl && key == "o")
        {
            await OpenFileAsync();
        }
        else if (ctrl && key == "v")
        {
            await PasteFromClipboardAsync();
        }
        else if (key == "[")
        {
            _vm.Opacity = Math.Max(0.1, _vm.Opacity - 0.1);
            WindowHelper.SetOpacity(this, _vm.Opacity);
            OpacitySlider.Value = _vm.Opacity * 100;
            SaveSettings();
        }
        else if (key == "]")
        {
            _vm.Opacity = Math.Min(1.0, _vm.Opacity + 0.1);
            WindowHelper.SetOpacity(this, _vm.Opacity);
            OpacitySlider.Value = _vm.Opacity * 100;
            SaveSettings();
        }
        else if (!ctrl && !alt && key == "v")
        {
            await ToggleVoiceAsync();
        }
    }

    // ── Playback ──

    private async Task TogglePlayPause()
    {
        if (_bridge == null) return;
        _vm.TogglePlayPause();
        if (_vm.IsPlaying)
        {
            HideOverlay(); // Hide toolbar when countdown starts
            await _bridge.PlayWithCountdownAsync(3);
        }
        else
            await _bridge.PauseAsync();
        PlayIcon.Glyph = _vm.IsPlaying ? "\uE769" : "\uE768";
    }

    private async Task StopPlayback()
    {
        if (_bridge == null) return;
        _vm.StopPlayback();
        await _bridge.StopAsync();
        PlayIcon.Glyph = "\uE768";
    }

    // ── File handling ──

    private async Task OpenFileAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".md");
            picker.FileTypeFilter.Add(".txt");
            picker.FileTypeFilter.Add(".markdown");

            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                var text = await FileIO.ReadTextAsync(file);
                _settingsService.AddRecentFile(file.Path);
                await LoadMarkdownAsync(text, file.Name);
            }
        }
        catch (Exception ex) { App.LogCrash("OpenFile", ex); }
    }

    private async Task PasteFromClipboardAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text))
            {
                var text = await content.GetTextAsync();
                if (!string.IsNullOrWhiteSpace(text))
                    await LoadMarkdownAsync(text, "Clipboard");
            }
        }
        catch (Exception ex) { App.LogCrash("Paste", ex); }
    }

    private async Task LoadMarkdownAsync(string markdown, string sourceName)
    {
        try
        {
            if (_bridge == null) return;

            _vm.ScriptMarkdown = markdown;
            _vm.CurrentFileName = sourceName;
            _vm.HasScript = true;

            var html = _markdownService.ConvertToHtml(markdown);
            _vm.HtmlContent = html;

            // Build word index for voice auto-advance
            var words = _markdownService.ExtractWords(markdown);
            _scriptWordIndex.Build(words);
            _voiceService.LoadScript(_scriptWordIndex);

            await _bridge.SetContentAsync(html);
        }
        catch (Exception ex) { App.LogCrash("LoadMarkdown", ex); }
    }

    // ── Drag & Drop ──

    private void RootGrid_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Load script";
        }
    }

    private async void RootGrid_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                foreach (var item in items)
                {
                    if (item is StorageFile file &&
                        (file.FileType == ".md" || file.FileType == ".txt" || file.FileType == ".markdown"))
                    {
                        var text = await FileIO.ReadTextAsync(file);
                        _settingsService.AddRecentFile(file.Path);
                        await LoadMarkdownAsync(text, file.Name);
                        break;
                    }
                }
            }
        }
        catch (Exception ex) { App.LogCrash("Drop", ex); }
    }

    // ── Overlay ──

    private void ShowOverlay()
    {
        OverlayPanel.Visibility = Visibility.Visible;
        _vm.IsOverlayVisible = true;
        _overlayHideTimer?.Stop();
        _overlayHideTimer?.Start();
    }

    private void HideOverlay()
    {
        OverlayPanel.Visibility = Visibility.Collapsed;
        _vm.IsOverlayVisible = false;
    }

    private void HoverZone_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ShowOverlay();
    }

    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(RootGrid).Properties.IsRightButtonPressed)
        {
            if (_vm.IsOverlayVisible)
                HideOverlay();
            else
                ShowOverlay();
        }
    }

    // ── Keyboard (XAML level) ──

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var alt = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        var key = e.Key switch
        {
            Windows.System.VirtualKey.Space => " ",
            Windows.System.VirtualKey.Escape => "Escape",
            Windows.System.VirtualKey.F11 => "F11",
            Windows.System.VirtualKey.Up => "ArrowUp",
            Windows.System.VirtualKey.Down => "ArrowDown",
            _ => e.Key.ToString().ToLowerInvariant()
        };

        ProcessShortcut(key, ctrl, alt);
    }

    // ── Button handlers ──

    private async void BtnOpen_Click(object sender, RoutedEventArgs e) => await OpenFileAsync();
    private async void BtnPaste_Click(object sender, RoutedEventArgs e) => await PasteFromClipboardAsync();
    private async void BtnPlay_Click(object sender, RoutedEventArgs e) => await TogglePlayPause();
    private async void BtnStop_Click(object sender, RoutedEventArgs e) => await StopPlayback();

    private async void BtnSpeedUp_Click(object sender, RoutedEventArgs e)
    {
        _vm.IncreaseSpeed();
        if (_bridge != null) await _bridge.SetSpeedAsync(_vm.Speed);
        SpeedText.Text = $"{_vm.Speed:F2}x";
        SaveSettings();
    }

    private async void BtnSpeedDown_Click(object sender, RoutedEventArgs e)
    {
        _vm.DecreaseSpeed();
        if (_bridge != null) await _bridge.SetSpeedAsync(_vm.Speed);
        SpeedText.Text = $"{_vm.Speed:F2}x";
        SaveSettings();
    }

    private async void BtnFontUp_Click(object sender, RoutedEventArgs e)
    {
        _vm.IncreaseFontSize();
        if (_bridge != null) await _bridge.SetFontSizeAsync(_vm.FontSize);
        FontSizeText.Text = $"{_vm.FontSize:F0}";
        SaveSettings();
    }

    private async void BtnFontDown_Click(object sender, RoutedEventArgs e)
    {
        _vm.DecreaseFontSize();
        if (_bridge != null) await _bridge.SetFontSizeAsync(_vm.FontSize);
        FontSizeText.Text = $"{_vm.FontSize:F0}";
        SaveSettings();
    }

    private async void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeCombo.SelectedIndex < 0) return;
        _vm.CurrentTheme = MainViewModel.AvailableThemes[ThemeCombo.SelectedIndex];
        if (_bridge != null) await _bridge.SetThemeAsync(_vm.CurrentTheme);

        // Update window background to match theme
        var bgColor = _vm.CurrentTheme switch
        {
            "light" => Microsoft.UI.Colors.WhiteSmoke,
            "studio" => Windows.UI.Color.FromArgb(255, 30, 30, 30),
            "warm" => Windows.UI.Color.FromArgb(255, 44, 24, 16),
            "green-screen" => Windows.UI.Color.FromArgb(255, 0, 177, 64),
            "high-contrast" => Microsoft.UI.Colors.Black,
            _ => Microsoft.UI.Colors.Black,
        };
        RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(bgColor);

        SaveSettings();
    }

    private async void BtnMirror_Click(object sender, RoutedEventArgs e)
    {
        _vm.ToggleMirror();
        if (_bridge != null) await _bridge.SetMirrorAsync(_vm.IsMirrorMode);
        SaveSettings();
    }

    private void OpacitySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_bridge == null) return; // not yet initialized
        _vm.Opacity = e.NewValue / 100.0;
        WindowHelper.SetOpacity(this, _vm.Opacity);
        SaveSettings();
    }

    private void BtnFullscreen_Click(object sender, RoutedEventArgs e)
    {
        WindowHelper.ToggleFullscreen(this);
    }

    private async void BtnVoice_Click(object sender, RoutedEventArgs e)
    {
        await ToggleVoiceAsync();
    }

    private async Task ToggleVoiceAsync()
    {
        try
        {
            if (_voiceService.IsListening)
            {
                await _voiceService.StopAsync();
            }
            else
            {
                if (!_vm.HasScript)
                {
                    VoiceStatusText.Text = "Load a script first";
                    VoiceIndicator.Visibility = Visibility.Visible;
                    return;
                }
                VoiceStatusText.Text = "Starting voice...";
                VoiceIndicator.Visibility = Visibility.Visible;

                // Capture the last error from the service (fires on background thread)
                string? lastError = null;
                void onError(string err) { lastError = err; }
                _voiceService.ErrorOccurred += onError;

                var started = await _voiceService.StartAsync();

                _voiceService.ErrorOccurred -= onError;

                if (!started)
                {
                    var msg = lastError ?? "Voice failed — check mic & speech settings";
                    VoiceStatusText.Text = msg;
                    App.LogCrash("VoiceStart", new Exception(msg));
                }
                else
                {
                    VoiceStatusText.Text = "Listening...";
                    HideOverlay(); // Hide toolbar when voice activation starts
                }
            }
        }
        catch (Exception ex)
        {
            VoiceStatusText.Text = $"Voice error: {ex.Message}";
            VoiceIndicator.Visibility = Visibility.Visible;
            App.LogCrash("ToggleVoice", ex);
        }
    }

    private void BtnExit_Click(object sender, RoutedEventArgs e)
    {
        ExitApp();
    }

    private void ExitApp()
    {
        _pipeCts?.Cancel();
        _trayIcon?.Dispose();
        _voiceService.Dispose();
        this.Close();
    }

    // ── Single-instance file pipe ──

    private void StartPipeListener()
    {
        _pipeCts = new CancellationTokenSource();
        var ct = _pipeCts.Token;
        Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream("WinPrompter_FilePipe",
                        PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(ct);

                    var buffer = new byte[4096];
                    int bytesRead = await server.ReadAsync(buffer, ct);
                    if (bytesRead > 0)
                    {
                        var filePath = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                        if (File.Exists(filePath))
                        {
                            DispatcherQueue.TryEnqueue(async () =>
                            {
                                this.Activate();
                                await LoadFileFromPathAsync(filePath);
                            });
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { /* pipe error — retry */ }
            }
        }, ct);
    }

    /// <summary>Load a file by path (used by CLI args and pipe IPC).</summary>
    public async Task LoadFileFromPathAsync(string filePath)
    {
        try
        {
            var text = await File.ReadAllTextAsync(filePath);
            _settingsService.AddRecentFile(filePath);
            await LoadMarkdownAsync(text, Path.GetFileName(filePath));
        }
        catch (Exception ex) { App.LogCrash("LoadFilePath", ex); }
    }

    private void SaveSettings()
    {
        _vm.SaveSettings(_settingsService);
    }

    // ── Recent Files Flyout ──

    private void RecentFilesFlyout_Opening(object sender, object e)
    {
        RecentFilesFlyout.Items.Clear();
        var recentFiles = _settingsService.GetRecentFiles();
        if (recentFiles.Count == 0)
        {
            var empty = new MenuFlyoutItem { Text = "(no recent files)", IsEnabled = false };
            RecentFilesFlyout.Items.Add(empty);
            return;
        }
        foreach (var path in recentFiles)
        {
            var item = new MenuFlyoutItem { Text = Path.GetFileName(path), Tag = path };
            item.Click += RecentFileItem_Click;
            RecentFilesFlyout.Items.Add(item);
        }
    }

    private async void RecentFileItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string path)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                var text = await FileIO.ReadTextAsync(file);
                _settingsService.AddRecentFile(path);
                await LoadMarkdownAsync(text, file.Name);
            }
            catch { /* file may no longer exist */ }
        }
    }

    // ── Sections Flyout ──

    private async void SectionsFlyout_Opening(object sender, object e)
    {
        SectionsFlyout.Items.Clear();
        if (_bridge == null || !_vm.HasScript)
        {
            var empty = new MenuFlyoutItem { Text = "(no sections)", IsEnabled = false };
            SectionsFlyout.Items.Add(empty);
            return;
        }
        var json = await _bridge.GetHeadingsAsync();
        try
        {
            var headings = JsonSerializer.Deserialize<List<HeadingInfo>>(
                json.Trim('"').Replace("\\\"", "\"").Replace("\\\\", "\\"));
            if (headings == null || headings.Count == 0)
            {
                SectionsFlyout.Items.Add(new MenuFlyoutItem { Text = "(no sections)", IsEnabled = false });
                return;
            }
            foreach (var h in headings)
            {
                var prefix = new string('\u00A0', (h.Level - 1) * 2);
                var item = new MenuFlyoutItem { Text = prefix + h.Text, Tag = h.OffsetPercent };
                item.Click += async (s, _) =>
                {
                    if (s is MenuFlyoutItem mi && mi.Tag is double pct)
                        await _bridge!.ScrollToPositionAsync(pct);
                };
                SectionsFlyout.Items.Add(item);
            }
        }
        catch { SectionsFlyout.Items.Add(new MenuFlyoutItem { Text = "(no sections)", IsEnabled = false }); }
    }

    // ── Settings Dialog ──

    private async void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var fontSizeSlider = new Slider { Minimum = 24, Maximum = 120, Value = _settingsService.FontSize, StepFrequency = 1 };
        var speedSlider = new Slider { Minimum = 0.5, Maximum = 5.0, Value = _settingsService.Speed, StepFrequency = 0.25 };
        var themeCombo = new ComboBox { Width = 200 };
        foreach (var name in MainViewModel.ThemeDisplayNames) themeCombo.Items.Add(name);
        themeCombo.SelectedIndex = Array.IndexOf(MainViewModel.AvailableThemes, _settingsService.Theme);
        var mirrorToggle = new ToggleSwitch { IsOn = _settingsService.MirrorMode };
        var opacitySlider = new Slider { Minimum = 10, Maximum = 100, Value = _settingsService.Opacity * 100, StepFrequency = 5 };
        var countdownToggle = new ToggleSwitch { IsOn = _settingsService.CountdownEnabled };
        var voiceSensitivitySlider = new Slider { Minimum = 1, Maximum = 5, Value = _settingsService.VoiceSensitivity, StepFrequency = 1 };

        var panel = new StackPanel { Spacing = 12, MinWidth = 350 };
        panel.Children.Add(new TextBlock { Text = "Default Font Size" });
        panel.Children.Add(fontSizeSlider);
        panel.Children.Add(new TextBlock { Text = "Default Speed" });
        panel.Children.Add(speedSlider);
        panel.Children.Add(new TextBlock { Text = "Default Theme" });
        panel.Children.Add(themeCombo);
        panel.Children.Add(new TextBlock { Text = "Start in Mirror Mode" });
        panel.Children.Add(mirrorToggle);
        panel.Children.Add(new TextBlock { Text = "Default Opacity (%)" });
        panel.Children.Add(opacitySlider);
        panel.Children.Add(new TextBlock { Text = "Countdown before play" });
        panel.Children.Add(countdownToggle);
        panel.Children.Add(new TextBlock { Text = "Voice Sensitivity (1=strict, 5=loose)" });
        panel.Children.Add(voiceSensitivitySlider);

        var dialog = new ContentDialog
        {
            Title = "Settings",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _settingsService.FontSize = fontSizeSlider.Value;
            _settingsService.Speed = speedSlider.Value;
            if (themeCombo.SelectedIndex >= 0)
                _settingsService.Theme = MainViewModel.AvailableThemes[themeCombo.SelectedIndex];
            _settingsService.MirrorMode = mirrorToggle.IsOn;
            _settingsService.Opacity = opacitySlider.Value / 100.0;
            _settingsService.CountdownEnabled = countdownToggle.IsOn;
            _settingsService.VoiceSensitivity = (int)voiceSensitivitySlider.Value;

            // Apply to view model and UI
            _vm.LoadSettings(_settingsService);
            SpeedText.Text = $"{_vm.Speed:F2}x";
            FontSizeText.Text = $"{_vm.FontSize:F0}";
            OpacitySlider.Value = _vm.Opacity * 100;
            ThemeCombo.SelectedIndex = Array.IndexOf(MainViewModel.AvailableThemes, _vm.CurrentTheme);
            BtnMirror.IsChecked = _vm.IsMirrorMode;
            WindowHelper.SetOpacity(this, _vm.Opacity);

            if (_bridge != null)
            {
                await _bridge.SetFontSizeAsync(_vm.FontSize);
                await _bridge.SetSpeedAsync(_vm.Speed);
                await _bridge.SetThemeAsync(_vm.CurrentTheme);
                await _bridge.SetMirrorAsync(_vm.IsMirrorMode);
            }
        }
    }

    // ── About / Help Dialog ──

    private async void BtnAbout_Click(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Spacing = 12, MinWidth = 380 };

        panel.Children.Add(new TextBlock { Text = "WinPrompter", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = "Version 1.0.0", Opacity = 0.7 });
        panel.Children.Add(new TextBlock { Text = "A modern teleprompter for Windows, powered by WinUI 3 and WebView2.", TextWrapping = TextWrapping.Wrap });

        // Keyboard shortcuts
        panel.Children.Add(new TextBlock { Text = "Keyboard Shortcuts", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });

        var shortcutsGrid = new Grid();
        shortcutsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        shortcutsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        string[][] shortcuts =
        [
            ["Ctrl +/−", "Font size"],
            ["Alt Up/Down", "Speed"],
            ["Space", "Play / Pause"],
            ["Escape", "Stop & reset"],
            ["F / F11", "Fullscreen"],
            ["M", "Mirror"],
            ["V", "Voice mode"],
            ["Ctrl+O", "Open file"],
            ["Ctrl+V", "Paste"],
            ["[ / ]", "Opacity"]
        ];

        for (int i = 0; i < shortcuts.Length; i++)
        {
            shortcutsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var keyText = new TextBlock
            {
                Text = shortcuts[i][0],
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                Opacity = 0.9,
                Margin = new Thickness(0, 2, 0, 2)
            };
            Grid.SetRow(keyText, i);
            Grid.SetColumn(keyText, 0);
            shortcutsGrid.Children.Add(keyText);

            var descText = new TextBlock
            {
                Text = shortcuts[i][1],
                Opacity = 0.7,
                Margin = new Thickness(0, 2, 0, 2)
            };
            Grid.SetRow(descText, i);
            Grid.SetColumn(descText, 1);
            shortcutsGrid.Children.Add(descText);
        }

        panel.Children.Add(shortcutsGrid);
        panel.Children.Add(new TextBlock { Text = "Built with WinUI 3 and \u2764\uFE0F", Opacity = 0.6, Margin = new Thickness(0, 8, 0, 0) });

        var dialog = new ContentDialog
        {
            Title = "About WinPrompter",
            Content = panel,
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot
        };

        await dialog.ShowAsync();
    }

    private record HeadingInfo(
        [property: System.Text.Json.Serialization.JsonPropertyName("level")] int Level,
        [property: System.Text.Json.Serialization.JsonPropertyName("text")] string Text,
        [property: System.Text.Json.Serialization.JsonPropertyName("offsetPercent")] double OffsetPercent);
}
