using FluentFlyoutWPF.CapsFlyout;
using System.Windows;
using System.IO;

namespace FluentFlyoutWPF;

public partial class StartupSettingsWindow : Window
{
    private bool _initializing = true;
    private bool _busy;
    private bool _closed;
    private readonly InputMethodExclusions _exclusions = InputMethodExclusions.Current;
    private sealed record ExcludedProgram(string FullPath, ApplicationExclusion? Application = null)
    {
        public string Name => Application?.Name ?? Path.GetFileName(FullPath);
        public string Description => Application is null ? "EXE 路径 · " + FullPath : "应用身份 · " + Application.AppId;
    }

    public StartupSettingsWindow()
    {
        InitializeComponent();
        StartWithWindowsCheckBox.IsChecked = StartupRegistration.IsEnabled;
        _initializing = false;
        Closed += (_, _) => _closed = true;
        RefreshExclusions();
        if (_exclusions.LoadError is not null)
        {
            AddExclusionButton.IsEnabled = false;
            AddApplicationButton.IsEnabled = false;
            ExclusionStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            ExclusionStatusText.Text = "禁用名单读取失败，原文件未改动：" + _exclusions.LoadError;
        }
    }

    private void RefreshExclusions()
    {
        ExcludedProgramsList.ItemsSource = _exclusions.Paths.Select(path => new ExcludedProgram(path))
            .Concat(_exclusions.Applications.Select(app => new ExcludedProgram(app.AppId, app))).ToArray();
        EmptyExclusionsText.Visibility = _exclusions.HasEntries ? Visibility.Collapsed : Visibility.Visible;
        RemoveExclusionButton.IsEnabled = false;
        ConvertExclusionButton.IsEnabled = false;
    }

    private void OnExclusionSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (RemoveExclusionButton is not null && ConvertExclusionButton is not null) UpdateButtons();
    }

    private void UpdateButtons()
    {
        var available = !_busy && _exclusions.LoadError is null;
        AddExclusionButton.IsEnabled = AddApplicationButton.IsEnabled = available;
        RemoveExclusionButton.IsEnabled = available && ExcludedProgramsList.SelectedItems.Count > 0;
        ConvertExclusionButton.IsEnabled = available &&
            ExcludedProgramsList.SelectedItems.Cast<ExcludedProgram>().Any(item => item.Application is null);
    }

    private void OnAddApplicationClick(object sender, RoutedEventArgs e)
    {
        var picker = new InstalledApplicationsWindow { Owner = this };
        if (picker.ShowDialog() == true)
            SaveRules(_exclusions.Paths, _exclusions.Applications.Concat(picker.SelectedApplications));
    }

    private async void OnConvertExclusionClick(object sender, RoutedEventArgs e)
    {
        var selected = ExcludedProgramsList.SelectedItems.Cast<ExcludedProgram>()
            .Where(item => item.Application is null).ToArray();
        _busy = true;
        UpdateButtons();
        ExclusionStatusText.Text = "正在核对已安装应用，原规则暂不改变…";
        try
        {
            var applications = await InstalledApplications.LoadAsync().WaitAsync(TimeSpan.FromSeconds(20));
            if (_closed) return;
            var converted = selected.Select(item => (item.FullPath,
                App: InstalledApplications.FindConversion(item.FullPath, applications))).Where(x => x.App is not null).ToArray();
            if (converted.Length == 0)
            {
                ExclusionStatusText.Text = "未找到可可靠转换的路径。请使用“选择已安装应用”，旧规则未改动。";
                return;
            }
            if (System.Windows.MessageBox.Show(this,
                $"将 {converted.Length} 条 EXE 规则转换为应用身份规则，更新后仍有效。其余未识别的规则保留。是否继续？",
                "转换禁用规则", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            var replaced = converted.Select(x => x.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            SaveRules(_exclusions.Paths.Where(path => !replaced.Contains(path)),
                _exclusions.Applications.Concat(converted.Select(x => x.App!.ToRule())));
        }
        catch (Exception error) { if (!_closed) ExclusionStatusText.Text = "转换失败，原规则未改变：" + error.Message; }
        finally { _busy = false; if (!_closed) UpdateButtons(); }
    }

    private void OnAddExclusionClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择不显示输入法浮窗的程序",
            Filter = "应用程序 (*.exe)|*.exe",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
            SaveExclusions(_exclusions.Paths.Concat(dialog.FileNames));
    }

    private void OnRemoveExclusionClick(object sender, RoutedEventArgs e)
    {
        var selected = ExcludedProgramsList.SelectedItems.Cast<ExcludedProgram>().ToArray();
        var paths = selected.Where(x => x.Application is null).Select(x => x.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var apps = selected.Where(x => x.Application is not null).Select(x => x.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        SaveRules(_exclusions.Paths.Where(path => !paths.Contains(path)),
            _exclusions.Applications.Where(app => !apps.Contains(app.AppId)));
    }

    private void SaveExclusions(IEnumerable<string> paths)
        => SaveRules(paths, _exclusions.Applications);

    private void SaveRules(IEnumerable<string> paths, IEnumerable<ApplicationExclusion> apps)
    {
        if (_exclusions.TrySave(paths, apps, out var error))
        {
            RefreshExclusions();
            ExclusionStatusText.Foreground = System.Windows.Media.Brushes.ForestGreen;
            ExclusionStatusText.Text = "已保存并生效，配置位于安装目录 Data 中。";
        }
        else
        {
            ExclusionStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            ExclusionStatusText.Text = "保存失败，名单未改变：" + error;
        }
    }

    private void OnStartupChoiceChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing)
            return;

        var requested = StartWithWindowsCheckBox.IsChecked == true;
        if (StartupRegistration.TrySetEnabled(requested, out var error))
        {
            StatusText.Text = requested ? "已启用服务开机自启" : "已关闭服务开机自启";
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(16, 124, 16));
            return;
        }

        StatusText.Text = $"保存失败：{error}";
        _initializing = true;
        StartWithWindowsCheckBox.IsChecked = !requested;
        _initializing = false;
    }

    private void OnDoneClick(object sender, RoutedEventArgs e) => Close();

    private void OnExitClick(object sender, RoutedEventArgs e) =>
        System.Windows.Application.Current.Shutdown();
}
