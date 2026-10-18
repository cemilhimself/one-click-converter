using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO;
using WpfButton = System.Windows.Controls.Button;

namespace OneClickConverter;

public partial class WheelOverlayWindow : Window
{
    private readonly MainWindow _mainWindow;
    private IReadOnlyList<FormatChoice> _targets = Array.Empty<FormatChoice>();
    private readonly List<WpfButton> _buttons = [];
    private int _highlightedIndex = -1;

    public bool IsDragging { get; private set; }

    public WheelOverlayWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
    }

    public void SetOptions(IReadOnlyList<string> paths, IReadOnlyList<FormatChoice> targets)
    {
        _targets = targets;
        _highlightedIndex = -1;
        IsDragging = false;
        SelectedFormatText.Text = "FORMAT";
        OutputLocationText.Text = _mainWindow.OutputFolder;
        OutputLocationText.ToolTip = _mainWindow.OutputFolder;
        foreach (var button in _buttons) WheelCanvas.Children.Remove(button);
        _buttons.Clear();

        const double center = 240;
        const double radius = 156;
        for (var index = 0; index < _targets.Count; index++)
        {
            var angle = -Math.PI / 2 + Math.Tau * index / _targets.Count;
            var button = new WpfButton
            {
                Width = 102,
                Height = 54,
                Content = new StackPanel
                {
                    IsHitTestVisible = false,
                    Children =
                    {
                        new TextBlock { Text = _targets[index].Name, FontSize = 11, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis },
                        new TextBlock { Text = $".{_targets[index].Extension.ToUpperInvariant()}", FontSize = 9, Opacity = 0.78, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 2, 0, 0) }
                    }
                },
                Tag = index,
                Background = Brushes.White,
                Foreground = (Brush)Application.Current.Resources["Ink"],
                BorderBrush = (Brush)Application.Current.Resources["Line"],
                BorderThickness = new Thickness(1),
                Padding = new Thickness(5, 3, 5, 3),
                ToolTip = $"Convert to {_targets[index].Name}"
            };
            Canvas.SetLeft(button, center + radius * Math.Cos(angle) - button.Width / 2);
            Canvas.SetTop(button, center + radius * Math.Sin(angle) - button.Height / 2);
            WheelCanvas.Children.Add(button);
            _buttons.Add(button);
        }
    }

    private void Wheel_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || _targets.Count == 0)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var paths = e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];
        if (paths.Length == 0 || !paths.All(File.Exists))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        IsDragging = true;
        e.Effects = DragDropEffects.Copy;
        var point = e.GetPosition(WheelCanvas);
        UpdateHighlight(point);
        e.Handled = true;
    }

    private async void Wheel_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || _highlightedIndex < 0 || _highlightedIndex >= _targets.Count)
        {
            IsDragging = false;
            _mainWindow.CloseWheel();
            e.Handled = true;
            return;
        }

        var format = _targets[_highlightedIndex].Extension;
        var compatible = paths.Where(path => File.Exists(path) && ConversionService.IsSupportedInput(path)).ToArray();
        if (compatible.Length == 0 || !GetTargetsForAll(compatible).Any(target => target.Extension == format))
        {
            IsDragging = false;
            _mainWindow.CloseWheel();
            e.Handled = true;
            return;
        }

        IsDragging = false;
        _mainWindow.CloseWheel();
        await _mainWindow.ConvertFromWheelAsync(compatible, format);
        e.Handled = true;
    }

    private static IReadOnlyList<FormatChoice> GetTargetsForAll(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return Array.Empty<FormatChoice>();
        return paths.Select(ConversionService.GetTargets)
            .Aggregate((left, right) => left.Where(item => right.Any(other => other.Extension == item.Extension)).ToArray());
    }

    private void UpdateHighlight(System.Windows.Point point)
    {
        var dx = point.X - 240;
        var dy = point.Y - 240;
        if (Math.Sqrt(dx * dx + dy * dy) < 78)
        {
            _highlightedIndex = -1;
            SelectedFormatText.Text = "FORMAT";
            PaintButtons();
            return;
        }

        var angle = Math.Atan2(dy, dx) + Math.PI / 2;
        if (angle < 0) angle += Math.Tau;
        _highlightedIndex = (int)Math.Floor((angle / Math.Tau * _targets.Count) + 0.5) % _targets.Count;
        SelectedFormatText.Text = _targets[_highlightedIndex].Extension.ToUpperInvariant();
        PaintButtons();
    }

    private void PaintButtons()
    {
        for (var index = 0; index < _buttons.Count; index++)
        {
            var selected = index == _highlightedIndex;
            _buttons[index].Background = selected ? (Brush)Application.Current.Resources["Accent"] : Brushes.White;
            _buttons[index].Foreground = selected ? Brushes.White : (Brush)Application.Current.Resources["Ink"];
            _buttons[index].BorderBrush = selected ? (Brush)Application.Current.Resources["Accent"] : (Brush)Application.Current.Resources["Line"];
            _buttons[index].RenderTransform = selected ? new ScaleTransform(1.08, 1.08) : Transform.Identity;
        }
    }
}
