using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using CDriveMigrator.ViewModels;

namespace CDriveMigrator.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();

        // Auto-scroll log
        if (DataContext is MainViewModel vm)
        {
            vm.LogMessages.CollectionChanged += (s, e) =>
            {
                try
                {
                    if (e.Action == NotifyCollectionChangedAction.Add)
                    {
                        var count = LogListBox.Items.Count;
                        if (count > 0)
                        {
                            LogListBox.ScrollIntoView(LogListBox.Items[count - 1]);
                        }
                    }
                }
                catch { }
            };
        }
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        var about = new Window
        {
            Title = "关于 CDriveMigrator",
            Width = 460,
            Height = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E1E2E")),
        };

        var stack = new StackPanel { Margin = new Thickness(32, 24, 32, 24) };

        stack.Children.Add(new TextBlock
        {
            Text = "CDriveMigrator",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 0, 0, 4)
        });

        stack.Children.Add(new TextBlock
        {
            Text = "C盘软件智能迁移工具 v1.0.0",
            FontSize = 13,
            Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#9CA3AF")),
            Margin = new Thickness(0, 0, 0, 20)
        });

        var separator1 = new Border
        {
            Height = 1,
            Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#374151")),
            Margin = new Thickness(0, 0, 0, 16)
        };
        stack.Children.Add(separator1);

        stack.Children.Add(new TextBlock
        {
            Text = "❤ 抖音关注",
            FontSize = 14,
            Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#A78BFA")),
            Margin = new Thickness(0, 0, 0, 6)
        });

        stack.Children.Add(new TextBlock
        {
            Text = "大刀AI很大刀",
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#C084FC")),
            Margin = new Thickness(0, 0, 0, 16)
        });

        var separator2 = new Border
        {
            Height = 1,
            Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#374151")),
            Margin = new Thickness(0, 0, 0, 16)
        };
        stack.Children.Add(separator2);

        stack.Children.Add(new TextBlock
        {
            Text = "本工具完全免费，禁止任何形式的倒卖或收费分发。",
            FontSize = 12,
            Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EF4444")),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });

        stack.Children.Add(new TextBlock
        {
            Text = "如果你是付费获取的，说明你被骗了！",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EF4444")),
            Margin = new Thickness(0, 0, 0, 16)
        });

        stack.Children.Add(new TextBlock
        {
            Text = "功能: 扫描 → 分析 → 迁移 → 自动修复引用 → 彻底卸载",
            FontSize = 11,
            Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6B7280")),
        });

        about.Content = stack;
        about.ShowDialog();
    }
}
