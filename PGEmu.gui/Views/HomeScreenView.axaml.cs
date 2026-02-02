using System;
using System.Linq;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PGEmu.gui.ViewModels;
using Avalonia.Input;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;

namespace PGEmu.gui.Views;

public partial class HomeScreenView : UserControl
{
    private ScrollViewer? _carouselScrollViewer;
    private DispatcherTimer? _snapTimer;
    private bool _internalChange;
    private bool _isDragging;
    private bool _isAnimating;
    private Point _dragStartPoint;
    private const double SlideStep = 360;

    public HomeScreenView()
    {
        InitializeComponent();

        // Helps preview tools not crash when DataContext is missing.
        if (Design.IsDesignMode && DataContext is null)
        {
            DataContext = new HomeScreenViewModel();
        }

        _snapTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _snapTimer.Tick += (_, __) =>
        {
            _snapTimer?.Stop();
            SnapSelectionToCenter();
        };
    }

    public void navigateLogin(object? sender, RoutedEventArgs e)
    {
        if (DataContext is HomeScreenViewModel vm)
        {
            vm.SwitchScreens(vm.loginViewModel);
        }
    }

    public void navigateProfile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is HomeScreenViewModel vm)
        {
            vm.SwitchScreens(vm.ProfileScreenViewModel);
        }
    }

    public void navigateSettings(object? sender, RoutedEventArgs e)
    {
        if (DataContext is HomeScreenViewModel vm)
        {
            vm.SwitchScreens(vm.userSettingsViewModel);
        }
    }

    public void CarouselContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is not ListBoxItem item) return;
        if (DataContext is not HomeScreenViewModel vm) return;
        if (e.Index < 0 || e.Index >= vm.WindowPlatforms.Count) return;
        var wp = vm.WindowPlatforms[e.Index];

        item.Classes.Remove("slot-left");
        item.Classes.Remove("slot-center");
        item.Classes.Remove("slot-right");

        switch (wp.Slot)
        {
            case 0:
                item.Classes.Add("slot-left");
                break;
            case 1:
                item.Classes.Add("slot-center");
                break;
            case 2:
                item.Classes.Add("slot-right");
                break;
        }
    }

    public void CarouselContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        if (e.Container is not ListBoxItem item) return;
        item.Classes.Remove("slot-left");
        item.Classes.Remove("slot-center");
        item.Classes.Remove("slot-right");
    }

    // IMPORTANT: ListBox.SelectionChanged expects Avalonia.Controls.SelectionChangedEventArgs
    public void CarouselSelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (ConsoleCarousel is null) return;

        if (_internalChange) return;
        if (_isAnimating) return;

        // When selection changes (via arrows or click), center it.
        _internalChange = true;
        try
        {
            CenterSelectedItemDeferred();
        }
        finally
        {
            _internalChange = false;
        }
    }

    public void PrevConsole(object? sender, RoutedEventArgs e)
    {
        _ = SlideAndMove(-1);
    }

    public void NextConsole(object? sender, RoutedEventArgs e)
    {
        _ = SlideAndMove(1);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Find the internal ScrollViewer of the ListBox so we can react to user scrolling.
        _carouselScrollViewer = ConsoleCarousel?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_carouselScrollViewer != null)
        {
            _carouselScrollViewer.ScrollChanged += CarouselScrollChanged;
            _carouselScrollViewer.PointerPressed += CarouselPointerPressed;
            _carouselScrollViewer.PointerMoved += CarouselPointerMoved;
            _carouselScrollViewer.PointerReleased += CarouselPointerReleased;
            _carouselScrollViewer.PointerCaptureLost += CarouselPointerCaptureLost;
            _carouselScrollViewer.PointerWheelChanged += CarouselPointerWheelChanged;
        }

        // Initial snap once layout has happened.
        Dispatcher.UIThread.Post(() =>
        {
            if (ConsoleCarousel != null && ConsoleCarousel.ItemCount > 0 && ConsoleCarousel.SelectedIndex < 0)
                ConsoleCarousel.SelectedIndex = 0;

            SnapSelectionToCenter();
        }, DispatcherPriority.Background);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_carouselScrollViewer != null)
        {
            _carouselScrollViewer.ScrollChanged -= CarouselScrollChanged;
            _carouselScrollViewer.PointerPressed -= CarouselPointerPressed;
            _carouselScrollViewer.PointerMoved -= CarouselPointerMoved;
            _carouselScrollViewer.PointerReleased -= CarouselPointerReleased;
            _carouselScrollViewer.PointerCaptureLost -= CarouselPointerCaptureLost;
            _carouselScrollViewer.PointerWheelChanged -= CarouselPointerWheelChanged;
            _carouselScrollViewer = null;
        }

        if (_snapTimer != null)
        {
            _snapTimer.Stop();
        }
    }

    private void CarouselScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (ConsoleCarousel is null) return;

        // Ignore programmatic scroll changes (centering).
        if (_internalChange) return;
        if (_isAnimating) return;

        UpdateSelectionByCenter();

        // Debounce: when scrolling settles, snap to the nearest item.
        _snapTimer?.Stop();
        _snapTimer?.Start();
    }

    private void SnapSelectionToCenter()
    {
        if (ConsoleCarousel is null) return;
        if (ConsoleCarousel.ItemCount <= 0) return;

        var bestIdx = FindIndexClosestToCenter();
        if (bestIdx < 0) return;

        _internalChange = true;
        try
        {
            ConsoleCarousel.SelectedIndex = bestIdx;
            CenterSelectedItemDeferred();
        }
        finally
        {
            _internalChange = false;
        }
    }

    private int FindIndexClosestToCenter()
    {
        if (ConsoleCarousel is null) return -1;
        if (_carouselScrollViewer is null) return -1;

        var viewportCenterX = _carouselScrollViewer.Viewport.Width / 2.0;

        var bestIdx = -1;
        var bestDist = double.MaxValue;

        // Only realized containers are measurable. That’s fine, we only need the visible neighborhood.
        foreach (var item in ConsoleCarousel.GetVisualDescendants().OfType<ListBoxItem>())
        {
            var pt = item.TranslatePoint(new Point(item.Bounds.Width / 2.0, item.Bounds.Height / 2.0), _carouselScrollViewer);
            if (pt is null) continue;

            var dist = Math.Abs(pt.Value.X - viewportCenterX);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIdx = ConsoleCarousel.ItemContainerGenerator.IndexFromContainer(item);
            }
        }

        return bestIdx;
    }

    private void CenterSelectedItemDeferred()
    {
        // Selection and item containers can change before layout is finalized.
        // Post to UI thread so the container is realized and has correct bounds.
        Dispatcher.UIThread.Post(() =>
        {
            CenterSelectedItem();

            // One extra pass after layout settles helps in cases where virtualization delays container sizing.
            Dispatcher.UIThread.Post(CenterSelectedItem, DispatcherPriority.Background);
        }, DispatcherPriority.Background);
    }

    private void CenterSelectedItem()
    {
        if (ConsoleCarousel is null) return;
        if (_carouselScrollViewer is null) return;

        var idx = ConsoleCarousel.SelectedIndex;
        if (idx < 0) return;

        if (ConsoleCarousel.ContainerFromIndex(idx) is not Control container)
            return;

        // Compute container center in ScrollViewer viewport space
        var pt = container.TranslatePoint(new Point(container.Bounds.Width / 2.0, container.Bounds.Height / 2.0), _carouselScrollViewer);
        if (pt is null) return;

        var viewportCenterX = _carouselScrollViewer.Viewport.Width / 2.0;
        var delta = pt.Value.X - viewportCenterX;

        // Adjust horizontal offset so selected item center moves to viewport center.
        var newX = _carouselScrollViewer.Offset.X + delta;
        var maxX = Math.Max(0, _carouselScrollViewer.Extent.Width - _carouselScrollViewer.Viewport.Width);

        if (newX < 0) newX = 0;
        if (newX > maxX) newX = maxX;

        _carouselScrollViewer.Offset = new Vector(newX, _carouselScrollViewer.Offset.Y);
    }
    private void CarouselPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_carouselScrollViewer is null) return;

        // Left-click drag to pan horizontally.
        if (e.GetCurrentPoint(_carouselScrollViewer).Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _dragStartPoint = e.GetPosition(_carouselScrollViewer);

            _snapTimer?.Stop();
            e.Pointer.Capture(_carouselScrollViewer);
            e.Handled = true;
        }
    }

    private void CarouselPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_carouselScrollViewer is null) return;
        if (!_isDragging) return;

        var pos = e.GetPosition(_carouselScrollViewer);
        var dx = pos.X - _dragStartPoint.X;

        if (Math.Abs(dx) >= 80)
        {
            _ = SlideAndMove(dx > 0 ? -1 : 1);

            _dragStartPoint = pos;
        }

        e.Handled = true;
    }

    private void CarouselPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_carouselScrollViewer is null) return;
        if (!_isDragging) return;

        _isDragging = false;
        if (e.Pointer.Captured == _carouselScrollViewer)
            e.Pointer.Capture(null);

        // After drag settles, pick the closest card and center it.
        _snapTimer?.Stop();
        _snapTimer?.Start();

        e.Handled = true;
    }

    private void CarouselPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;

        _snapTimer?.Stop();
        _snapTimer?.Start();
    }

    private void CarouselPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_carouselScrollViewer is null) return;

        // Trackpads often provide horizontal delta already.
        // Mouse wheels often provide vertical delta only, so map Y -> X.
        var dx = e.Delta.X;
        if (Math.Abs(dx) < 0.01)
            dx = e.Delta.Y;

        if (Math.Abs(dx) < 0.01) return;

        _ = SlideAndMove(dx > 0 ? -1 : 1);

        // Debounce snap to center.
        _snapTimer?.Stop();
        _snapTimer?.Start();

        e.Handled = true;
    }

    private void UpdateSelectionByCenter()
    {
        if (ConsoleCarousel is null) return;
        if (_carouselScrollViewer is null) return;
        if (_isAnimating) return;

        var bestIdx = FindIndexClosestToCenter();
        if (bestIdx < 0) return;
        if (ConsoleCarousel.SelectedIndex == bestIdx) return;

        _internalChange = true;
        try
        {
            ConsoleCarousel.SelectedIndex = bestIdx;
        }
        finally
        {
            _internalChange = false;
        }
    }

    private async System.Threading.Tasks.Task AnimateSlide(int direction, bool forward)
    {
        if (ConsoleCarousel is null) return;

        var translate = ConsoleCarousel.RenderTransform as TranslateTransform;
        if (translate is null)
        {
            translate = new TranslateTransform();
            ConsoleCarousel.RenderTransform = translate;
        }

        var from = forward ? (direction > 0 ? SlideStep : -SlideStep) : 0;
        var to = forward ? 0 : (direction > 0 ? -SlideStep : SlideStep);
        translate.X = from;

        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(180),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(TranslateTransform.XProperty, from) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(TranslateTransform.XProperty, to) }
                }
            }
        };

        await animation.RunAsync(translate, System.Threading.CancellationToken.None);
    }

    private async System.Threading.Tasks.Task SlideAndMove(int direction)
    {
        if (ConsoleCarousel is null) return;
        if (DataContext is not HomeScreenViewModel vm) return;
        if (_isAnimating) return;

        _isAnimating = true;
        try
        {
            _internalChange = true;
            try
            {
                vm.MoveWindow(direction);
                CenterSelectedItemDeferred();
            }
            finally
            {
                _internalChange = false;
            }

            await AnimateSlide(direction, forward: false);
        }
        finally
        {
            _isAnimating = false;
        }
    }
}
