using System;
using System.Linq;
using Avalonia.Threading;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PGEmu.gui.ViewModels;
using Avalonia.Input;
using Avalonia.Media;

namespace PGEmu.gui.Views;

public partial class HomeScreenView : UserControl
{
    private ScrollViewer? _carouselScrollViewer;
    private DispatcherTimer? _snapTimer;
    private bool _internalChange;
    private bool _isDragging;
    private bool _isAnimating;
    private bool _isAnimatingScroll;
    private Point _dragStartPoint;
    private Vector _dragStartOffset;
    private bool _isPanning;
    private bool _hasAppliedInitialTransforms;

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
        item.Classes.Remove("off-left");
        item.Classes.Remove("off-right");

        switch (wp.Slot)
        {
            case -1:
                item.Classes.Add("off-left");
                break;
            case 0:
                item.Classes.Add("slot-left");
                break;
            case 1:
                item.Classes.Add("slot-center");
                break;
            case 2:
                item.Classes.Add("slot-right");
                break;
            case 3:
                item.Classes.Add("off-right");
                break;
        }

        if (_hasAppliedInitialTransforms)
            Dispatcher.UIThread.Post(UpdateCarouselTransforms, DispatcherPriority.Background);
    }

    public void CarouselContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        if (e.Container is not ListBoxItem item) return;
        item.Classes.Remove("slot-left");
        item.Classes.Remove("slot-center");
        item.Classes.Remove("slot-right");
        item.Classes.Remove("off-left");
        item.Classes.Remove("off-right");
    }

    // IMPORTANT: ListBox.SelectionChanged expects Avalonia.Controls.SelectionChangedEventArgs
    public void CarouselSelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (ConsoleCarousel is null) return;

        if (_internalChange) return;
        if (_isAnimating || _isAnimatingScroll) return;

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
            UpdateCarouselTransforms();
            _hasAppliedInitialTransforms = true;
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

        UpdateCarouselTransforms();

        // Ignore programmatic scroll changes (centering).
        if (_internalChange) return;
        if (_isAnimating || _isAnimatingScroll) return;

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
        if (!TryGetCenteredOffsetForIndex(ConsoleCarousel?.SelectedIndex ?? -1, out var newX))
            return;

        AnimateScrollTo(newX);
    }

    private void CenterSelectedItemImmediate()
    {
        if (!TryGetCenteredOffsetForIndex(ConsoleCarousel?.SelectedIndex ?? -1, out var newX))
            return;

        if (_carouselScrollViewer is null) return;
        _carouselScrollViewer.Offset = new Vector(newX, _carouselScrollViewer.Offset.Y);
    }

    private bool TryGetCenteredOffsetForIndex(int index, out double newX)
    {
        newX = 0;
        if (ConsoleCarousel is null) return false;
        if (_carouselScrollViewer is null) return false;
        if (index < 0) return false;

        if (ConsoleCarousel.ContainerFromIndex(index) is not Control container)
            return false;

        var pt = container.TranslatePoint(new Point(container.Bounds.Width / 2.0, container.Bounds.Height / 2.0), _carouselScrollViewer);
        if (pt is null) return false;

        var viewportCenterX = _carouselScrollViewer.Viewport.Width / 2.0;
        var delta = pt.Value.X - viewportCenterX;

        newX = _carouselScrollViewer.Offset.X + delta;
        var maxX = Math.Max(0, _carouselScrollViewer.Extent.Width - _carouselScrollViewer.Viewport.Width);

        if (newX < 0) newX = 0;
        if (newX > maxX) newX = maxX;

        return true;
    }

    private void UpdateCarouselTransforms()
    {
        if (ConsoleCarousel is null) return;
        if (_carouselScrollViewer is null) return;
        if (_carouselScrollViewer.Viewport.Width <= 0.0) return;

        var viewportCenterX = _carouselScrollViewer.Viewport.Width / 2.0;

        foreach (var item in ConsoleCarousel.GetVisualDescendants().OfType<ListBoxItem>())
        {
            var pt = item.TranslatePoint(new Point(item.Bounds.Width / 2.0, item.Bounds.Height / 2.0), _carouselScrollViewer);
            if (pt is null) continue;

            var dist = (pt.Value.X - viewportCenterX) / viewportCenterX;
            dist = Math.Max(-1.2, Math.Min(1.2, dist));
            var abs = Math.Abs(dist);

            var t = Math.Min(1.0, abs);
            var scale = Lerp(1.0, 0.08, t);
            var opacity = Lerp(1.0, 0.25, t);
            var translateY = t * 26.0;
            var translateX = dist * 18.0;
            var skewY = dist * 10.0;

            var group = EnsureTransformGroup(item);
            var scaleTransform = (ScaleTransform)group.Children[0];
            var skewTransform = (SkewTransform)group.Children[1];
            var translateTransform = (TranslateTransform)group.Children[2];

            scaleTransform.ScaleX = scale;
            scaleTransform.ScaleY = scale;
            skewTransform.AngleY = skewY;
            translateTransform.X = translateX;
            translateTransform.Y = translateY;

            item.Opacity = opacity;
            item.ZIndex = (int)(1000 - (abs * 1000.0));
            item.IsHitTestVisible = abs < 0.75;
        }
    }

    private static TransformGroup EnsureTransformGroup(Control item)
    {
        if (item.RenderTransform is TransformGroup existing &&
            existing.Children.Count == 3 &&
            existing.Children[0] is ScaleTransform &&
            existing.Children[1] is SkewTransform &&
            existing.Children[2] is TranslateTransform)
        {
            return existing;
        }

        var group = new TransformGroup
        {
            Children =
            {
                new ScaleTransform(1.0, 1.0),
                new SkewTransform(0.0, 0.0),
                new TranslateTransform(0.0, 0.0)
            }
        };

        item.RenderTransform = group;
        return group;
    }

    private static double Lerp(double from, double to, double t)
    {
        return from + ((to - from) * t);
    }

    private void AnimateScrollTo(double newX)
    {
        _ = AnimateScrollToAsync(newX);
    }

    private async System.Threading.Tasks.Task AnimateScrollToAsync(double newX)
    {
        if (_carouselScrollViewer is null) return;

        var current = _carouselScrollViewer.Offset;

        // Remove tiny-move snap, always animate.

        if (_isAnimatingScroll) return;
        _isAnimatingScroll = true;

        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(320),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward
        };

        var maxX = Math.Max(0, _carouselScrollViewer.Extent.Width - _carouselScrollViewer.Viewport.Width);
        var delta = newX - current.X;
        var dir = Math.Sign(delta);

        // Small overshoot in the travel direction, then settle.
        var overshootX = newX + (dir * 18.0);
        if (overshootX < 0) overshootX = 0;
        if (overshootX > maxX) overshootX = maxX;

        animation.Children.Add(new KeyFrame
        {
            Cue = new Cue(0d),
            Setters =
            {
                new Setter(ScrollViewer.OffsetProperty, current)
            }
        });

        animation.Children.Add(new KeyFrame
        {
            Cue = new Cue(0.85d),
            Setters =
            {
                new Setter(ScrollViewer.OffsetProperty, new Vector(overshootX, current.Y))
            }
        });

        animation.Children.Add(new KeyFrame
        {
            Cue = new Cue(1d),
            Setters =
            {
                new Setter(ScrollViewer.OffsetProperty, new Vector(newX, current.Y))
            }
        });

        try
        {
            await animation.RunAsync(_carouselScrollViewer);
        }
        finally
        {
            _isAnimatingScroll = false;
        }
    }
    private void CarouselPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_carouselScrollViewer is null) return;

        // Left-click drag to pan horizontally.
        if (e.GetCurrentPoint(_carouselScrollViewer).Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _isPanning = true;
            _dragStartPoint = e.GetPosition(_carouselScrollViewer);
            _dragStartOffset = _carouselScrollViewer.Offset;

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

        // Continuous pan
        if (_isPanning)
        {
            var maxX = Math.Max(0, _carouselScrollViewer.Extent.Width - _carouselScrollViewer.Viewport.Width);
            var newX = _dragStartOffset.X - dx;
            if (newX < 0) newX = 0;
            if (newX > maxX) newX = maxX;

            _internalChange = true;
            try
            {
                _carouselScrollViewer.Offset = new Vector(newX, _carouselScrollViewer.Offset.Y);
            }
            finally
            {
                _internalChange = false;
            }

            UpdateCarouselTransforms();
        }

        // If the user drags far enough, treat it like a discrete carousel step.
        if (Math.Abs(dx) >= 140)
        {
            _isPanning = false;
            _ = SlideAndMove(dx > 0 ? -1 : 1);

            // Reset the drag baseline so they can keep dragging without jumps.
            _dragStartPoint = pos;
            _dragStartOffset = _carouselScrollViewer.Offset;
            _isPanning = true;
        }

        e.Handled = true;
    }

    private void CarouselPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_carouselScrollViewer is null) return;
        if (!_isDragging) return;

        _isDragging = false;
        _isPanning = false;
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
        _isPanning = false;

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

        // Tune this to taste. Higher = faster scroll.
        var scrollAmount = -dx * 80.0;

        var maxX = Math.Max(0, _carouselScrollViewer.Extent.Width - _carouselScrollViewer.Viewport.Width);
        var newX = _carouselScrollViewer.Offset.X + scrollAmount;
        if (newX < 0) newX = 0;
        if (newX > maxX) newX = maxX;

        _internalChange = true;
        try
        {
            _carouselScrollViewer.Offset = new Vector(newX, _carouselScrollViewer.Offset.Y);
        }
        finally
        {
            _internalChange = false;
        }

        UpdateCarouselTransforms();
        UpdateSelectionByCenter();

        // Debounce snap to center.
        _snapTimer?.Stop();
        _snapTimer?.Start();

        e.Handled = true;
    }

    private void UpdateSelectionByCenter()
    {
        if (ConsoleCarousel is null) return;
        if (_carouselScrollViewer is null) return;
        if (_isAnimating || _isAnimatingScroll) return;

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

    private async System.Threading.Tasks.Task SlideAndMove(int direction)
    {
        if (ConsoleCarousel is null) return;
        if (DataContext is not HomeScreenViewModel vm) return;
        if (_isAnimating || _isAnimatingScroll) return;

        _isAnimating = true;
        try
        {
            _internalChange = true;
            try
            {
                vm.MoveWindow(direction);
            }
            finally
            {
                _internalChange = false;
            }

            CenterSelectedItemDeferred();
        }
        finally
        {
            _isAnimating = false;
        }
    }
}
