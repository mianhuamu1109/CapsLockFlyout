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
            var removeClick = typeof(StartupSettingsWindow).GetMethod("OnRemoveExclusionClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
            void Save(string[] paths) => save.Invoke(window, [paths]);
            void Layout()
            {
                root.Measure(new Size(534, 581));
                root.Arrange(new Rect(0, 0, 534, 581));
                root.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
            }
            Save([]);
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
            foreach (var name in new[] { "StartWithWindowsCheckBox", "ExcludedProgramsList", "AddExclusionButton", "RemoveExclusionButton", "ExclusionStatusText" })
            {
                var element = (FrameworkElement)window.FindName(name);
                var rectangle = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
                Check(rectangle.Top >= 0 && rectangle.Bottom <= root.ActualHeight &&
                    rectangle.Left >= 0 && rectangle.Right <= root.ActualWidth, name + " fits in content");
            }
            removeClick.Invoke(window, [remove, new RoutedEventArgs()]);
            Check(list.Items.Count == 2 && !remove.IsEnabled, "Removing selection updates list and button state");
            Save([]);
            window.Close();
            app.Shutdown();
            Console.WriteLine($"All {_checks} settings checks passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
