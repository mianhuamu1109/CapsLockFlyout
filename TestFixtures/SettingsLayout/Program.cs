using FluentFlyoutWPF;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        _checks++;
        Console.WriteLine("PASS: " + label);
    }

    [STAThread]
    private static int Main()
    {
        try
        {
            // Real settings UI without App.OnStartup, visible windows or service changes.
            // Test settings are written beside this test executable only.
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var window = new StartupSettingsWindow();
            var root = (FrameworkElement)window.Content;
            ((Grid)root).Background = window.Background;
            var list = (ListBox)window.FindName("ExcludedProgramsList");
            var empty = (TextBlock)window.FindName("EmptyExclusionsText");
            var remove = (Button)window.FindName("RemoveExclusionButton");
            var save = typeof(StartupSettingsWindow).GetMethod("SaveExclusions", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var saveRules = typeof(StartupSettingsWindow).GetMethod("SaveRules", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var assembly = typeof(StartupSettingsWindow).Assembly;
            var ruleType = assembly.GetType("FluentFlyoutWPF.CapsFlyout.ApplicationExclusion")!;
            var emptyRules = Array.CreateInstance(ruleType, 0);
            var photosRules = Array.CreateInstance(ruleType, 1);
            photosRules.SetValue(Activator.CreateInstance(ruleType,
                "照片", "Microsoft.Windows.Photos_8wekyb3d8bbwe", "Microsoft.Windows.Photos_8wekyb3d8bbwe!App"), 0);
            var removeClick = typeof(StartupSettingsWindow).GetMethod("OnRemoveExclusionClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
            void Save(string[] paths) => save.Invoke(window, [paths]);
            void Layout()
            {
                root.Measure(new Size(534, 581));
                root.Arrange(new Rect(0, 0, 534, 581));
                root.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
            }
            saveRules.Invoke(window, [Array.Empty<string>(), emptyRules]);
            Layout();
            Check(empty.Visibility == Visibility.Visible && list.Items.Count == 0, "Empty-list hint is visible");
            Check(!remove.IsEnabled, "Remove is disabled with no selection");
            Save([@"D:\Games\示例游戏\Game.exe", @"D:\Applications\示例编辑器\Editor.exe",
                @"D:\Applications\名称很长的应用程序目录用于检查路径裁切和提示\bin\AnotherEditor.exe"]);
            Layout();
            Check(list.Items.Count == 3 && empty.Visibility == Visibility.Collapsed, "Saved programs populate real WPF list");
            Check(list.ActualHeight >= 100, "List retains useful height below status message");
            list.SelectedIndex = 0;
            Layout();
            Check(remove.IsEnabled, "Selecting an EXE enables remove");
            var data = Path.Combine(AppContext.BaseDirectory, "Data", "ime-exclusions.json");
            Check(File.Exists(data), "Settings saved beside test executable, not in user profile");
            var output = Path.Combine(AppContext.BaseDirectory, "artifacts");
            Directory.CreateDirectory(output);
            saveRules.Invoke(window, [new[] { @"D:\Tools\Editor.exe" }, photosRules]);
            Layout();
            Check(list.Items.Count == 2, "EXE and application rules share one settings list");
            list.SelectedIndex = 1;
            Layout();
            Check(!((Button)window.FindName("ConvertExclusionButton")).IsEnabled, "Identity rule cannot be converted again");
            Check(remove.IsEnabled, "Application rule can be selected for removal");
            foreach (var scale in new[] { 1.0, 1.5, 2.0 })
            {
                var bitmap = new RenderTargetBitmap((int)(534 * scale), (int)(581 * scale),
                    96 * scale, 96 * scale, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bitmap));
                var file = Path.Combine(output, $"settings-{(int)(scale * 100)}.png");
                using var stream = File.Create(file);
                png.Save(stream);
                Console.WriteLine("Layout render: " + file);
            }
            foreach (var name in new[] { "StartWithWindowsCheckBox", "ExcludedProgramsList", "AddExclusionButton", "AddApplicationButton", "ConvertExclusionButton", "RemoveExclusionButton", "ExclusionStatusText" })
            {
                var element = (FrameworkElement)window.FindName(name);
                var rectangle = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
                Check(rectangle.Top >= 0 && rectangle.Bottom <= root.ActualHeight &&
                    rectangle.Left >= 0 && rectangle.Right <= root.ActualWidth, name + " fits in content");
            }
            removeClick.Invoke(window, [remove, new RoutedEventArgs()]);
            Check(list.Items.Count == 1 && !remove.IsEnabled, "Removing app identity preserves the EXE rule");
            Save([]);
            var picker = new InstalledApplicationsWindow();
            var applicationType = assembly.GetType("FluentFlyoutWPF.CapsFlyout.InstalledApplication")!;
            var candidates = Array.CreateInstance(applicationType, 2);
            candidates.SetValue(Activator.CreateInstance(applicationType,
                "照片", "Microsoft.Windows.Photos_8wekyb3d8bbwe", "Microsoft.Windows.Photos_8wekyb3d8bbwe!App", Array.Empty<string>()), 0);
            candidates.SetValue(Activator.CreateInstance(applicationType,
                "截图工具", "Microsoft.ScreenSketch_8wekyb3d8bbwe", "Microsoft.ScreenSketch_8wekyb3d8bbwe!App", Array.Empty<string>()), 1);
            typeof(InstalledApplicationsWindow).GetMethod("SetApplications", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(picker, [candidates]);
            var search = (TextBox)picker.FindName("SearchBox");
            var apps = (ListBox)picker.FindName("ApplicationsList");
            Check(apps.Items.Count == 2, "Installed-app picker displays application names");
            search.Text = "照片";
            Check(apps.Items.Count == 1, "Picker supports Chinese search");
            apps.SelectedIndex = 0;
            Check(((Button)picker.FindName("AcceptButton")).IsEnabled, "Choosing Photos enables Add");
            var pickerRoot = (Grid)picker.Content;
            pickerRoot.Background = picker.Background;
            pickerRoot.Measure(new Size(564, 521));
            pickerRoot.Arrange(new Rect(0, 0, 564, 521));
            pickerRoot.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
            var rendered = new RenderTargetBitmap(846, 782, 144, 144, PixelFormats.Pbgra32);
            rendered.Render(pickerRoot);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rendered));
            using (var stream = File.Create(Path.Combine(output, "installed-apps-150.png"))) encoder.Save(stream);
            search.Text = "not-installed-example";
            Check(apps.Items.Count == 0 && !((Button)picker.FindName("AcceptButton")).IsEnabled,
                "No search match disables Add");
            picker.Close();
            window.Close();
            app.Shutdown();
            Console.WriteLine($"All {_checks} settings checks passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
