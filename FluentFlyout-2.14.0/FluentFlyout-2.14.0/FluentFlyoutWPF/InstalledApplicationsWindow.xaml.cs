using FluentFlyoutWPF.CapsFlyout;
using System.Windows;
using System.Windows.Controls;

namespace FluentFlyoutWPF;

public partial class InstalledApplicationsWindow : Window
{
    private IReadOnlyList<InstalledApplication> _applications = [];
    private bool _closed;
    internal IReadOnlyList<ApplicationExclusion> SelectedApplications { get; private set; } = [];
    public InstalledApplicationsWindow()
    {
        InitializeComponent();
        Closed += (_, _) => _closed = true;
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var applications = await InstalledApplications.LoadAsync().WaitAsync(TimeSpan.FromSeconds(20));
            if (_closed) return;
            SetApplications(applications);
        }
        catch (Exception error)
        {
            if (!_closed) LoadStatusText.Text = "读取失败，请关闭后重试：" + error.Message;
        }
    }
    internal void SetApplications(IReadOnlyList<InstalledApplication> applications)
    {
        _applications = applications;
        Filter();
    }
    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (ApplicationsList is not null) Filter();
    }
    private void Filter()
    {
        var query = SearchBox.Text.Trim();
        var filtered = _applications.Where(app =>
            app.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            app.AppId.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        ApplicationsList.ItemsSource = filtered;
        LoadStatusText.Text = $"显示 {filtered.Length} 个应用。可按住 Ctrl 多选；普通程序请返回选择 EXE。";
    }
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AcceptButton is not null) AcceptButton.IsEnabled = ApplicationsList.SelectedItems.Count > 0;
    }
    private void OnAcceptClick(object sender, RoutedEventArgs e)
    {
        SelectedApplications = ApplicationsList.SelectedItems.Cast<InstalledApplication>().Select(app => app.ToRule()).ToArray();
        if (SelectedApplications.Count > 0) DialogResult = true;
    }
}
