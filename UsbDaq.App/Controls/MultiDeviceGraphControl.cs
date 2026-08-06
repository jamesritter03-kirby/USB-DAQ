using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using UsbDaq.App.ViewModels;

namespace UsbDaq.App.Controls;

public sealed class MultiDeviceGraphControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<DeviceChannelViewModel>?> SeriesProperty =
        AvaloniaProperty.Register<MultiDeviceGraphControl, IReadOnlyList<DeviceChannelViewModel>?>(nameof(Series));

    public static readonly StyledProperty<double> MinValueProperty =
        AvaloniaProperty.Register<MultiDeviceGraphControl, double>(nameof(MinValue), 0);

    public static readonly StyledProperty<double> MaxValueProperty =
        AvaloniaProperty.Register<MultiDeviceGraphControl, double>(nameof(MaxValue), 30000);

    public static readonly StyledProperty<int> SampleIntervalMsProperty =
        AvaloniaProperty.Register<MultiDeviceGraphControl, int>(nameof(SampleIntervalMs), 250);

    public static readonly StyledProperty<bool> ShowLegendProperty =
        AvaloniaProperty.Register<MultiDeviceGraphControl, bool>(nameof(ShowLegend), true);

    public static readonly StyledProperty<bool> ShowCursorsProperty =
        AvaloniaProperty.Register<MultiDeviceGraphControl, bool>(nameof(ShowCursors), true);

    public static readonly StyledProperty<double> StartFractionProperty =
        AvaloniaProperty.Register<MultiDeviceGraphControl, double>(nameof(StartFraction), 0d);

    public static readonly StyledProperty<double> WindowFractionProperty =
        AvaloniaProperty.Register<MultiDeviceGraphControl, double>(nameof(WindowFraction), 1d);

    public static readonly StyledProperty<string> GraphModeProperty =
        AvaloniaProperty.Register<MultiDeviceGraphControl, string>(nameof(GraphMode), "Sliding Window");

    public static readonly StyledProperty<bool> GraphAutoFollowProperty =
        AvaloniaProperty.Register<MultiDeviceGraphControl, bool>(nameof(GraphAutoFollow), true);

    public static readonly StyledProperty<bool> ShowPointMarkersProperty =
        AvaloniaProperty.Register<MultiDeviceGraphControl, bool>(nameof(ShowPointMarkers), false);

    public static readonly StyledProperty<bool> IsAcquiringProperty =
        AvaloniaProperty.Register<MultiDeviceGraphControl, bool>(nameof(IsAcquiring), false);

    private static readonly Cursor SizeCursor = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor PanCursor = new(StandardCursorType.SizeAll);
    private const int MinVisibleSamples = 2;

    private bool _isPanning;
    private bool _isDraggingCursor;
    private bool _dragCursorA;
    private Point _lastPointer;
    private double _cursorA;
    private double _cursorB = 1;
    private Rect _lastPlotRect;
    private double _activeViewStart;
    private double _activeViewSpan = 1;
    private int _activeTotalSpan = 1;
    private bool _isStripMode;
    private string _cursorStats = "Cursor stats: n/a";

    static MultiDeviceGraphControl()
    {
        AffectsRender<MultiDeviceGraphControl>(
            SeriesProperty,
            MinValueProperty,
            MaxValueProperty,
            SampleIntervalMsProperty,
            ShowLegendProperty,
            ShowCursorsProperty,
            StartFractionProperty,
            WindowFractionProperty,
            GraphModeProperty,
            GraphAutoFollowProperty,
            ShowPointMarkersProperty,
            IsAcquiringProperty);
    }

    public MultiDeviceGraphControl()
    {
        Focusable = true;
    }

    public IReadOnlyList<DeviceChannelViewModel>? Series
    {
        get => GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public double MinValue
    {
        get => GetValue(MinValueProperty);
        set => SetValue(MinValueProperty, value);
    }

    public double MaxValue
    {
        get => GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    public int SampleIntervalMs
    {
        get => GetValue(SampleIntervalMsProperty);
        set => SetValue(SampleIntervalMsProperty, Math.Max(1, value));
    }

    public bool ShowLegend
    {
        get => GetValue(ShowLegendProperty);
        set => SetValue(ShowLegendProperty, value);
    }

    public bool ShowCursors
    {
        get => GetValue(ShowCursorsProperty);
        set => SetValue(ShowCursorsProperty, value);
    }

    public double StartFraction
    {
        get => GetValue(StartFractionProperty);
        set => SetValue(StartFractionProperty, Math.Clamp(value, 0d, 1d));
    }

    public double WindowFraction
    {
        get => GetValue(WindowFractionProperty);
        set => SetValue(WindowFractionProperty, Math.Clamp(value, 0.02d, 1d));
    }

    public string GraphMode
    {
        get => GetValue(GraphModeProperty);
        set => SetValue(GraphModeProperty, value);
    }

    public bool GraphAutoFollow
    {
        get => GetValue(GraphAutoFollowProperty);
        set => SetValue(GraphAutoFollowProperty, value);
    }

    public bool ShowPointMarkers
    {
        get => GetValue(ShowPointMarkersProperty);
        set => SetValue(ShowPointMarkersProperty, value);
    }

    public bool IsAcquiring
    {
        get => GetValue(IsAcquiringProperty);
        set => SetValue(IsAcquiringProperty, value);
    }

    public void ResetView()
    {
        StartFraction = 0;
        WindowFraction = 1;
        InvalidateVisual();
    }

    public void ZoomIn()
    {
        ApplyZoom(0.86, 0.5);
    }

    public void ZoomOut()
    {
        ApplyZoom(1.17, 0.5);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        if (bounds.Width < 120 || bounds.Height < 80)
        {
            return;
        }

        // Ensure the full control surface is hit-testable for pointer wheel/drag interactions.
        context.DrawRectangle(Brushes.Transparent, null, bounds);

        var left = 50d;
        var top = 12d;
        var right = ShowLegend ? 210d : 12d;
        var bottom = 44d;
        var plot = new Rect(left, top, Math.Max(1, bounds.Width - left - right), Math.Max(1, bounds.Height - top - bottom));
        _lastPlotRect = plot;

        var axisBrush = new SolidColorBrush(Color.Parse("#8EA4B7"));
        var gridPen = new Pen(new SolidColorBrush(Color.Parse("#566D80"), 0.30), 1);
        var axisPen = new Pen(axisBrush, 1);

        for (var i = 0; i <= 5; i++)
        {
            var y = plot.Top + (plot.Height * i / 5d);
            context.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }

        context.DrawLine(axisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        context.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));

        var channels = (Series ?? Array.Empty<DeviceChannelViewModel>())
            .Where(c => c.IsConnected && c.IsVisible && c.Samples.Count > 1)
            .ToList();

        if (channels.Count == 0)
        {
            _cursorStats = "Cursor stats: no visible connected channels";
            DrawFooterText(context, axisBrush, plot, false);
            return;
        }

        var ySpan = Math.Max(1e-6, MaxValue - MinValue);
        var maxCount = channels.Max(c => c.Samples.Count);
        var totalSpan = Math.Max(1, maxCount - 1);

        var isStripMode = IsStripChartMode();
        var viewSpan = ComputeViewSpan(totalSpan);
        var maxStart = Math.Max(0, totalSpan - viewSpan);
        var viewStart = isStripMode ? maxStart : StartFraction * maxStart;
        if (!isStripMode && GraphAutoFollow && IsAcquiring)
        {
            viewStart = maxStart;
        }

        var viewEnd = isStripMode ? totalSpan : viewStart + viewSpan;
        _activeTotalSpan = totalSpan;
        _activeViewStart = viewStart;
        _activeViewSpan = viewSpan;
        _isStripMode = isStripMode;

        foreach (var channel in channels)
        {
            var pen = new Pen(new SolidColorBrush(channel.TraceColor), 2);
            Point? prev = null;
            var prevSlot = -1;

            var spanSamples = Math.Max(2, (int)Math.Round(viewSpan));
            var markerStep = Math.Max(1, spanSamples / 120);

            for (var i = 0; i < channel.Samples.Count; i++)
            {
                if (i < viewStart || i > viewEnd)
                {
                    continue;
                }

                var sample = channel.Samples[i];
                double x;
                var slot = 0;
                if (isStripMode)
                {
                    slot = i % spanSamples;
                    x = plot.Left + (slot / Math.Max(1d, spanSamples - 1d)) * plot.Width;
                }
                else
                {
                    x = plot.Left + ((i - viewStart) / Math.Max(1, viewSpan)) * plot.Width;
                }

                var normalized = Math.Clamp((sample.PressurePsig - MinValue) / ySpan, 0, 1);
                var y = plot.Bottom - normalized * plot.Height;
                var point = new Point(x, y);

                if (prev is { } p)
                {
                    if (!isStripMode || slot > prevSlot)
                    {
                        context.DrawLine(pen, p, point);
                    }
                }

                if (ShowPointMarkers && (i % markerStep == 0 || i == channel.Samples.Count - 1))
                {
                    context.DrawEllipse(new SolidColorBrush(channel.TraceColor), null, point, 2.1, 2.1);
                }

                prev = point;
                prevSlot = slot;
            }
        }

        if (ShowLegend)
        {
            DrawLegend(context, channels, bounds, axisBrush);
        }

        DrawCursors(context, channels, plot, totalSpan, viewStart, viewSpan, axisBrush);
        DrawFooterText(context, axisBrush, plot, isStripMode);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var pointer = e.GetPosition(this);
        if (!_lastPlotRect.Contains(pointer))
        {
            return;
        }

        var rel = Math.Clamp((pointer.X - _lastPlotRect.Left) / Math.Max(1, _lastPlotRect.Width), 0, 1);
        ApplyZoom(e.Delta.Y > 0 ? 0.86 : 1.17, rel);
        e.Handled = true;
    }

    private void ApplyZoom(double zoomFactor, double anchorRelative)
    {
        var totalSpan = Math.Max(1, GetTotalSpan());
        var oldViewSpan = ComputeViewSpan(totalSpan);
        var minVisible = Math.Min(MinVisibleSamples, totalSpan);
        var newViewSpan = Math.Clamp(oldViewSpan * zoomFactor, minVisible, totalSpan);

        if (IsStripChartMode())
        {
            WindowFraction = Math.Clamp(newViewSpan / totalSpan, 0.02, 1);
            InvalidateVisual();
            return;
        }

        var oldMaxStart = Math.Max(0, totalSpan - oldViewSpan);
        var oldViewStart = StartFraction * oldMaxStart;

        var rel = Math.Clamp(anchorRelative, 0, 1);
        var anchorSample = oldViewStart + rel * oldViewSpan;

        var newMaxStart = Math.Max(0, totalSpan - newViewSpan);
        var newViewStart = Math.Clamp(anchorSample - rel * newViewSpan, 0, newMaxStart);

        WindowFraction = Math.Clamp(newViewSpan / totalSpan, 0.02, 1);
        StartFraction = newMaxStart <= 0 ? 0 : newViewStart / newMaxStart;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var point = e.GetPosition(this);
        _lastPointer = point;

        if (!_lastPlotRect.Contains(point))
        {
            return;
        }

        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed || props.IsMiddleButtonPressed)
        {
            _isPanning = true;
            e.Pointer.Capture(this);
            return;
        }

        if (!props.IsLeftButtonPressed)
        {
            return;
        }

        if (_isStripMode)
        {
            return;
        }

        var nearA = false;
        var nearB = false;
        if (ShowCursors)
        {
            var xA = CursorToX(_cursorA);
            var xB = CursorToX(_cursorB);
            nearA = Math.Abs(point.X - xA) <= 16;
            nearB = Math.Abs(point.X - xB) <= 16;
        }

        if (ShowCursors && (nearA || nearB || e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
        {
            _dragCursorA = nearA || (!nearB && e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            _isDraggingCursor = true;
            SetCursorFromX(point.X, _dragCursorA);
        }
        else if (!_isStripMode)
        {
            _isPanning = true;
            Cursor = PanCursor;
        }

        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);

        if (_isPanning)
        {
            if (_isStripMode)
            {
                _lastPointer = point;
                return;
            }

            var totalSpan = Math.Max(1, GetTotalSpan());
            var viewSpan = ComputeViewSpan(totalSpan);
            var maxStart = Math.Max(0, totalSpan - viewSpan);
            var viewStart = StartFraction * maxStart;

            var dx = point.X - _lastPointer.X;
            var shift = -dx / Math.Max(1, _lastPlotRect.Width) * viewSpan;
            viewStart = Math.Clamp(viewStart + shift, 0, maxStart);
            StartFraction = maxStart <= 0 ? 0 : viewStart / maxStart;
            InvalidateVisual();
        }
        else if (_isDraggingCursor)
        {
            SetCursorFromX(point.X, _dragCursorA);
        }
        else
        {
            if (_isStripMode)
            {
                Cursor = ArrowCursor;
                _lastPointer = point;
                return;
            }

            if (ShowCursors)
            {
                var xA = CursorToX(_cursorA);
                var xB = CursorToX(_cursorB);
                Cursor = (Math.Abs(point.X - xA) <= 14 || Math.Abs(point.X - xB) <= 14)
                    ? SizeCursor
                    : (_lastPlotRect.Contains(point) && !_isStripMode ? PanCursor : ArrowCursor);
            }
            else
            {
                Cursor = _lastPlotRect.Contains(point) && !_isStripMode ? PanCursor : ArrowCursor;
            }
        }

        _lastPointer = point;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isPanning = false;
        _isDraggingCursor = false;
        Cursor = ArrowCursor;
        e.Pointer.Capture(null);
    }

    private void DrawLegend(DrawingContext context, IReadOnlyList<DeviceChannelViewModel> channels, Rect bounds, IBrush textBrush)
    {
        var x = bounds.Right - 196;
        var y = 14d;
        foreach (var ch in channels.Take(10))
        {
            context.DrawRectangle(new SolidColorBrush(ch.TraceColor), null, new Rect(x, y + 5, 13, 13));
            var label = new FormattedText(
                $"{ch.DisplayName}  {ch.CurrentPressureText}",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                12,
                textBrush);
            context.DrawText(label, new Point(x + 20, y));
            y += 18;
        }
    }

    private void DrawCursors(
        DrawingContext context,
        IReadOnlyList<DeviceChannelViewModel> channels,
        Rect plot,
        int totalSpan,
        double viewStart,
        double viewSpan,
        IBrush textBrush)
    {
        if (!ShowCursors)
        {
            _cursorStats = "Cursors disabled";
            return;
        }

        if (_isStripMode)
        {
            _cursorStats = "Cursors are available in Sliding Window mode";
            return;
        }

        _cursorA = Math.Clamp(_cursorA, 0, totalSpan);
        _cursorB = Math.Clamp(_cursorB, 0, totalSpan);

        var xA = plot.Left + ((_cursorA - viewStart) / Math.Max(1, viewSpan)) * plot.Width;
        var xB = plot.Left + ((_cursorB - viewStart) / Math.Max(1, viewSpan)) * plot.Width;

        var penA = new Pen(new SolidColorBrush(Color.Parse("#E2E8F0"), 0.9), 2);
        var penB = new Pen(new SolidColorBrush(Color.Parse("#F59E0B"), 0.9), 2);

        context.DrawLine(penA, new Point(xA, plot.Top), new Point(xA, plot.Bottom));
        context.DrawLine(penB, new Point(xB, plot.Top), new Point(xB, plot.Bottom));

        context.DrawRectangle(new SolidColorBrush(Color.Parse("#E2E8F0")), null, new Rect(xA - 5, plot.Top, 10, 8));
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#F59E0B")), null, new Rect(xB - 5, plot.Top, 10, 8));

        var primary = channels[0];
        var idxA = Math.Clamp((int)Math.Round(_cursorA), 0, primary.Samples.Count - 1);
        var idxB = Math.Clamp((int)Math.Round(_cursorB), 0, primary.Samples.Count - 1);
        var pA = primary.Samples[idxA].PressurePsig;
        var pB = primary.Samples[idxB].PressurePsig;
        var dtSamples = Math.Abs(idxB - idxA);
        var dt = dtSamples * Math.Max(1, SampleIntervalMs) / 1000d;
        var dp = pB - pA;
        var slope = dt <= 0 ? 0 : dp / dt;

        _cursorStats =
            $"A:{idxA}  B:{idxB}   Delta t: {dt:F3}s   Delta P: {dp:F2} psig   dP/dt: {slope:F2} psig/s";

        var text = new FormattedText(
            _cursorStats,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            12,
            textBrush);
        context.DrawText(text, new Point(plot.Left + 6, plot.Bottom + 8));
    }

    private void DrawFooterText(DrawingContext context, IBrush textBrush, Rect plot, bool isStripMode)
    {
        var modeHint = isStripMode ? "Mode: Strip Chart (auto-wrap)" : "Mode: Sliding Window";
        var interactionHint = isStripMode
            ? "Mouse wheel=zoom visible span | Switch to Sliding Window for cursors/pan"
            : "Mouse wheel=zoom | Left-drag=pan | Left-drag near cursor handle to move cursor | Shift+Left sets Cursor A";
        var hint = new FormattedText(
            $"{modeHint} | {interactionHint}",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            11,
            textBrush);
        context.DrawText(hint, new Point(plot.Left + 6, Math.Max(0, plot.Top - 12)));
    }

    private int GetTotalSpan()
    {
        var channels = Series;
        if (channels is null || channels.Count == 0)
        {
            return 1;
        }

        var max = channels.Where(c => c.IsVisible && c.Samples.Count > 1).Select(c => c.Samples.Count).DefaultIfEmpty(2).Max();
        return Math.Max(1, max - 1);
    }

    private void SetCursorFromX(double x, bool cursorA)
    {
        var rel = Math.Clamp((x - _lastPlotRect.Left) / Math.Max(1, _lastPlotRect.Width), 0, 1);
        var sample = Math.Clamp(_activeViewStart + rel * _activeViewSpan, 0, _activeTotalSpan);

        if (cursorA)
        {
            _cursorA = sample;
        }
        else
        {
            _cursorB = sample;
        }

        InvalidateVisual();
    }

    private double CursorToX(double cursor)
    {
        var rel = (cursor - _activeViewStart) / Math.Max(1, _activeViewSpan);
        return _lastPlotRect.Left + Math.Clamp(rel, 0, 1) * _lastPlotRect.Width;
    }

    private bool IsStripChartMode()
    {
        return GraphMode.Contains("Strip", StringComparison.OrdinalIgnoreCase);
    }

    private double ComputeViewSpan(int totalSpan)
    {
        var clampedTotal = Math.Max(1, totalSpan);
        var minVisible = Math.Min(MinVisibleSamples, clampedTotal);
        return Math.Clamp(clampedTotal * WindowFraction, minVisible, clampedTotal);
    }
}
