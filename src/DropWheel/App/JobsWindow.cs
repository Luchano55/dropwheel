using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using DropWheel.Jobs;
using DropWheel.Shell;

namespace DropWheel.App;

/// <summary>Мини-окно со списком задач и прогрессом (раздел 8 ТЗ). Открывается у трея.</summary>
internal sealed class JobsWindow : Window
{
    private readonly ObservableCollection<JobRow> _rows = [];

    public JobsWindow(JobQueue queue)
    {
        Title = "DropWheel — задачи";
        Width = 420;
        Height = 360;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStyle = WindowStyle.ToolWindow;

        foreach (var job in queue.Jobs.Reverse()) _rows.Add(new JobRow(job));

        var list = new ItemsControl { ItemsSource = _rows, ItemTemplate = RowTemplate(), Margin = new Thickness(8) };
        var empty = new TextBlock
        {
            Text = "Задач пока нет. Нажмите правой кнопкой на файл → «Конвертировать…».",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
            Margin = new Thickness(12),
        };
        var panel = new Grid();
        panel.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        panel.Children.Add(empty);
        empty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _rows.CollectionChanged += (_, _) => empty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Content = panel;

        Deactivated += (_, _) => Hide();
    }

    public void Refresh(Job job)
    {
        var row = _rows.FirstOrDefault(r => r.Job == job);
        if (row is null) _rows.Insert(0, new JobRow(job));
        else row.Update();
    }

    public void ShowNearTray()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 8;
        Top = area.Bottom - Height - 8;
        Show();
        Activate();
    }

    private static DataTemplate RowTemplate()
    {
        var root = new FrameworkElementFactory(typeof(StackPanel));
        root.SetValue(MarginProperty, new Thickness(0, 0, 0, 10));

        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(JobRow.Title)));
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        root.AppendChild(name);

        var bar = new FrameworkElementFactory(typeof(ProgressBar));
        bar.SetValue(HeightProperty, 6.0);
        bar.SetValue(RangeBase.MaximumProperty, 1.0);
        bar.SetBinding(RangeBase.ValueProperty, new Binding(nameof(JobRow.Progress)));
        bar.SetBinding(ProgressBar.IsIndeterminateProperty, new Binding(nameof(JobRow.Indeterminate)));
        bar.SetBinding(VisibilityProperty, new Binding(nameof(JobRow.BarVisibility)));
        bar.SetValue(MarginProperty, new Thickness(0, 3, 0, 3));
        root.AppendChild(bar);

        var line = new FrameworkElementFactory(typeof(DockPanel));
        var status = new FrameworkElementFactory(typeof(TextBlock));
        status.SetBinding(TextBlock.TextProperty, new Binding(nameof(JobRow.Status)));
        status.SetValue(TextBlock.ForegroundProperty, Brushes.DimGray);
        status.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);

        var action = new FrameworkElementFactory(typeof(Button));
        action.SetValue(DockPanel.DockProperty, Dock.Right);
        action.SetValue(PaddingProperty, new Thickness(8, 0, 8, 0));
        action.SetBinding(ContentControl.ContentProperty, new Binding(nameof(JobRow.ActionText)));
        action.SetBinding(VisibilityProperty, new Binding(nameof(JobRow.ActionVisibility)));
        action.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent,
            new RoutedEventHandler((s, _) => ((JobRow)((FrameworkElement)s).DataContext).DoAction()));
        line.AppendChild(action);
        line.AppendChild(status);
        root.AppendChild(line);

        return new DataTemplate { VisualTree = root };
    }

    private sealed class JobRow(Job job) : INotifyPropertyChanged
    {
        public Job Job { get; } = job;
        public string Title => $"{Path.GetFileName(Job.Input)} → {Job.Operation.Label}";
        public double Progress => Job.Progress;
        // У ImageMagick нет прогресса — «бегущая» полоса.
        public bool Indeterminate => Job.State == JobState.Running && Job.Operation.Engine == Conversions.EngineKind.ImageMagick;
        public Visibility BarVisibility => Job.State is JobState.Queued or JobState.Running ? Visibility.Visible : Visibility.Collapsed;

        public string Status => Job.State switch
        {
            JobState.Queued => "В очереди",
            JobState.Running => Job.Progress > 0 ? $"{Job.Progress:P0}" : "Выполняется…",
            JobState.Done => "Готово: " + Path.GetFileName(Job.OutputPath),
            JobState.Failed => "Ошибка: " + Job.Error,
            _ => "Отменено",
        };

        public string ActionText => Job.State == JobState.Done ? "Показать" : "Отмена";
        public Visibility ActionVisibility => Job.State is JobState.Failed or JobState.Canceled ? Visibility.Collapsed : Visibility.Visible;

        public void DoAction()
        {
            if (Job.State == JobState.Done && Job.OutputPath is not null) Notifier.RevealInExplorer(Job.OutputPath);
            else Job.Cancel();
        }

        public void Update() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
