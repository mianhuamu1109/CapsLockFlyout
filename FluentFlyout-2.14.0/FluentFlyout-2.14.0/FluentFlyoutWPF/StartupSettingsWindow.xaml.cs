using FluentFlyoutWPF.CapsFlyout;
using System.Windows;

namespace FluentFlyoutWPF;

public partial class StartupSettingsWindow : Window
{
    private bool _initializing = true;

    public StartupSettingsWindow()
    {
        InitializeComponent();
        StartWithWindowsCheckBox.IsChecked = StartupRegistration.IsEnabled;
        _initializing = false;
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
