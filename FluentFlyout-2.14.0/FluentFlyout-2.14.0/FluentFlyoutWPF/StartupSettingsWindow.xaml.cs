using FluentFlyoutWPF.CapsFlyout;
using System.Windows;
using System.IO;

namespace FluentFlyoutWPF;

public partial class StartupSettingsWindow : Window
{
    private bool _initializing = true;
    private readonly InputMethodExclusions _exclusions = InputMethodExclusions.Current;
    private sealed record ExcludedProgram(string FullPath)
    {
        public string Name => Path.GetFileName(FullPath);
    }

    public StartupSettingsWindow()
    {
        InitializeComponent();
        StartWithWindowsCheckBox.IsChecked = StartupRegistration.IsEnabled;
        _initializing = false;
        RefreshExclusions();
        if (_exclusions.LoadError is not null)
        {
            AddExclusionButton.IsEnabled = false;
            ExclusionStatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            ExclusionStatusText.Text = "禁用名单读取失败，原文件未改动：" + _exclusions.LoadError;
        }
    }

    private void RefreshExclusions()
    {
        ExcludedProgramsList.ItemsSource = _exclusions.Paths.Select(path => new ExcludedProgram(path)).ToArray();
        EmptyExclusionsText.Visibility = _exclusions.HasEntries ? Visibility.Collapsed : Visibility.Visible;
        RemoveExclusionButton.IsEnabled = false;
    }

    private void OnExclusionSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (RemoveExclusionButton is not null)
            RemoveExclusionButton.IsEnabled = _exclusions.LoadError is null &&
                ExcludedProgramsList.SelectedItems.Count > 0;
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
        var selected = ExcludedProgramsList.SelectedItems.Cast<ExcludedProgram>()
            .Select(item => item.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        SaveExclusions(_exclusions.Paths.Where(path => !selected.Contains(path)));
    }

    private void SaveExclusions(IEnumerable<string> paths)
    {
        if (_exclusions.TrySave(paths, out var error))
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
