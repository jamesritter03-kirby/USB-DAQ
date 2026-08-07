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

    public static readonly StyledProperty<bool> ShowAlarmLinesProperty =
        AvaloniaProperty.Register<MultiDeviceGraphControl, bool>(nameof(ShowAlarmLines), true);

    public static readonly StyledProperty<double> LowAlarmValueProperty =
        AvaloniaProperty.Register<MultiDeviceGraphControl, double>(nameof(LowAlarmValue), 0);

    public static readonly StyledProperty<double> HighAlarmValueProperty =
        AvaloniaProperty.Register<MultiDeviceGraphControl, double>(nameof(HighAlarmValue), 30000);

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

    public static readonly StyledProperty<bool> StackedPlotsProperty =
        AvaloniaProperty.Register<MultiDeviceGraphControl, bool>(nameof(StackedPlots), false);

    public static readonly StyledProperty<bool> CursorSnapToDataProperty =
        AvaloniaProperty.Register<MultiDeviceGraphControl, bool>(nameof(CursorSnapToData), false);

    private static readonly Cursor SizeCursor = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor PanCursor = new(StandardCursorType.SizeAll);
    private const int MinVisibleSamples = 2;
    private const double MarginLeft = 68;
    private const double MarginTop = 14;
    private const double MarginRight = 12;
    private const double MarginBottom = 52;

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
    private string _cursorStats = string.Empty;

    static MultiDeviceGraphControl()
    {
        AffectsRender<MultiDeviceGraphControl>(
            SeriesProperty,
            MinValueProperty,
            MaxValueProperty,
            SampleIntervalMsProperty,
            ShowLegendProperty,
            ShowCursorsProperty,
            ShowAlarmLinesProperty,
            LowAlarmValueProperty,
            HighAlarmValueProperty,
            StartFractionProperty,
            WindowFractionProperty,
            GraphModeProperty,
            GraphAutoFollowProperty,
            ShowPointMarkersProperty,
            IsAcquiringProperty,
            StackedPlotsProperty);
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

    public bool ShowAlarmLines
    {
        get => GetValue(ShowAlarmLinesProperty);
        set => SetValue(ShowAlarmLinesProperty, value);
    }

    public double LowAlarmValue
    {
        get => GetValue(LowAlarmValueProperty);
        set => SetValue(LowAlarmValueProperty, value);
    }

    public double HighAlarmValue
    {
        get => GetValue(HighAlarmValueProperty);
        set => SetValue(HighAlarmValueProperty, value);
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

    public bool StackedPlots
    {
        get => GetValue(StackedPlotsProperty);
        set => SetValue(StackedPlotsProperty, value);
    }

    public bool CursorSnapToData
    {
        get => GetValue(CursorSnapToDataProperty);
        set => SetValue(CursorSnapToDataProperty, value);
    }

    public void ResetView()
    {
        StartFraction = 0;
        WindowFraction = 1;
        InvalidateVisual();
    }

    public void ZoomIn() => ApplyZoom(0.86, 0.5);
    public void ZoomOut() => ApplyZoom(1.17, 0.5);

    public void ZoomToSeconds(double seconds, int sampleIntervalMs)
    {
        var totalSpan = GetTotalSpan();
        if (totalSpan <= 1) return;

        var samplesInSpan = seconds * 1000.0 / Math.Max(1, sampleIntervalMs);
        WindowFraction = Math.Clamp(samplesInSpan / totalSpan, 0.02, 1.0);
        if (GraphAutoFollow) StartFraction = 1.0;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        if (bounds.Width < 120 || bounds.Height < 80) return;

        context.DrawRectangle(Brushes.Transparent, null, bounds);

        var plot = new Rect(
            MarginLeft,
            MarginTop,
            Math.Max(1, bounds.Width - MarginLeft - MarginRight),
            Math.Max(1, bounds.Height - MarginTop - MarginBottom));
        _lastPlotRect = plot;

        var axisBrush = new SolidColorBrush(Color.Parse("#8EA4B7"));
        var gridPen = new Pen(new SolidColorBrush(Color.Parse("#566D80"), 0.25), 1);
        var axisPen = new Pen(axisBrush, 1.5);

        var allSeries = Series ?? Array.Empty<DeviceChannelViewModel>();
        var channels = allSeries.Where(c => c.IsConnected && c.IsVisible && c.Samples.Count > 1).ToList();

        // Compute view parameters early so X-axis labels use the same view window
        var maxCount = channels.Count > 0 ? channels.Max(c => c.Samples.Count) : 0;
        var totalSpan = Math.Max(1, maxCount - 1);
        var isStripMode = IsStripChartMode();
        var viewSpan = ComputeViewSpan(totalSpan);
        var maxStart = Math.Max(0, totalSpan - viewSpan);
        var viewStart = isStripMode ? maxStart : StartFraction * maxStart;
        if (!isStripMode && GraphAutoFollow && IsAcquiring) viewStart = maxStart;
        var viewEnd = isStripMode ? totalSpan : viewStart + viewSpan;
        _activeTotalSpan = totalSpan;
        _activeViewStart = viewStart;
        _activeViewSpan = viewSpan;
        _isStripMode = isStripMode;

        var ySpan = Math.Max(1e-6, MaxValue - MinValue);

        // Y-axis grid lines and labels
        var yTickInterval = NiceTickInterval(ySpan, 6);
        var firstYTick = Math.Ceiling(MinValue / yTickInterval) * yTickInterval;
        for (var tick = firstYTick; tick <= MaxValue + yTickInterval * 0.01; tick += yTickInterval)
        {
            var norm = Math.Clamp((tick - MinValue) / ySpan, 0, 1);
            var y = plot.Bottom - norm * plot.Height;
            context.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            context.DrawLine(axisPen, new Point(plot.Left - 4, y), new Point(plot.Left, y));

            var labelStr = FormatYLabel(tick);
            var ft = new FormattedText(labelStr, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10.5, axisBrush);
            context.DrawText(ft, new Point(plot.Left - 6 - ft.Width, y - ft.Height * 0.5));
        }

        // X-axis time labels
        var startSec = viewStart * SampleIntervalMs / 1000.0;
        var endSec = viewEnd * SampleIntervalMs / 1000.0;
        var timeSec = endSec - startSec;
        if (timeSec > 0.001)
        {
            var xTickInterval = NiceTickInterval(timeSec, 6);
            var firstXTick = Math.Ceiling(startSec / xTickInterval) * xTickInterval;
            for (var t = firstXTick; t <= endSec + xTickInterval * 0.01; t += xTickInterval)
            {
                var xRel = (t - startSec) / timeSec;
                var x = plot.Left + xRel * plot.Width;
                if (x < plot.Left - 1 || x > plot.Right + 1) continue;

                context.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                context.DrawLine(axisPen, new Point(x, plot.Bottom), new Point(x, plot.Bottom + 4));

                var timeLabel = FormatElapsedTime(t);
                var ft = new FormattedText(timeLabel, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, axisBrush);
                context.DrawText(ft, new Point(x - ft.Width * 0.5, plot.Bottom + 7));
            }
        }

        // Axes
        context.DrawLine(axisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        context.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));

        // Empty state
        if (channels.Count == 0)
        {
            var hint = new FormattedText("Add channels and connect to begin",
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 13, new SolidColorBrush(Color.Parse("#566D80")));
            context.DrawText(hint, new Point(
                plot.Left + (plot.Width - hint.Width) * 0.5,
                plot.Top + (plot.Height - hint.Height) * 0.5));
            DrawModeHint(context, axisBrush, plot, isStripMode);
            return;
        }

        // Stacked layout: each channel in its own horizontal strip
        if (StackedPlots)
        {
            RenderStacked(context, channels, plot, viewStart, viewSpan, ySpan, axisBrush, gridPen, axisPen);
            DrawCursors(context, channels, plot, totalSpan, viewStart, viewSpan, axisBrush);
            DrawModeHint(context, axisBrush, plot, isStripMode);
            return;
        }

        // Draw channel traces
        var spanSamples = Math.Max(2, (int)Math.Round(viewSpan));
        var markerStep = Math.Max(1, spanSamples / 120);

        foreach (var channel in channels)
        {
            var pen = new Pen(new SolidColorBrush(channel.TraceColor), 2);
            Point? prev = null;
            var prevSlot = -1;

            for (var i = 0; i < channel.Samples.Count; i++)
            {
                if (i < viewStart || i > viewEnd) continue;

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

                if (prev is { } p && (!isStripMode || slot > prevSlot))
                    context.DrawLine(pen, p, point);

                if (ShowPointMarkers && (i % markerStep == 0 || i == channel.Samples.Count - 1))
                    context.DrawEllipse(new SolidColorBrush(channel.TraceColor), null, point, 2.5, 2.5);

                prev = point;
                prevSlot = slot;
            }
        }

        // Alarm threshold lines
        if (ShowAlarmLines)
        {
            var dashStyle = DashStyle.Dash;
            var lowNorm = Math.Clamp((LowAlarmValue - MinValue) / ySpan, 0, 1);
            var highNorm = Math.Clamp((HighAlarmValue - MinValue) / ySpan, 0, 1);

            var lowY = plot.Bottom - lowNorm * plot.Height;
            if (lowY > plot.Top && lowY < plot.Bottom)
            {
                var lowPen = new Pen(new SolidColorBrush(Color.Parse("#F59E0B")), 1.5, dashStyle);
                context.DrawLine(lowPen, new Point(plot.Left, lowY), new Point(plot.Right, lowY));
                var lbl = new FormattedText($"Lo {LowAlarmValue:F0}", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10,
                    new SolidColorBrush(Color.Parse("#F59E0B")));
                context.DrawText(lbl, new Point(plot.Right - lbl.Width - 4, lowY - lbl.Height - 1));
            }

            var highY = plot.Bottom - highNorm * plot.Height;
            if (highY > plot.Top && highY < plot.Bottom)
            {
                var highPen = new Pen(new SolidColorBrush(Color.Parse("#EF4444")), 1.5, dashStyle);
                context.DrawLine(highPen, new Point(plot.Left, highY), new Point(plot.Right, highY));
                var lbl = new FormattedText($"Hi {HighAlarmValue:F0}", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10,
                    new SolidColorBrush(Color.Parse("#EF4444")));
                context.DrawText(lbl, new Point(plot.Right - lbl.Width - 4, highY + 2));
            }
        }

        // Legend overlay (top-left inside plot)
        if (ShowLegend)
        {
            var legendPad = 8d;
            var rowH = 18d;
            var legendW = 210d;
            var legendH = channels.Count * rowH + legendPad;
            var lx = plot.Left + 8;
            var ly = plot.Top + 8;

            context.DrawRectangle(new SolidColorBrush(Color.Parse("#B3101828")), null,
                new Rect(lx - legendPad * 0.5, ly - legendPad * 0.5, legendW, legendH), 5, 5);

            var rowY = ly;
            foreach (var ch in channels.Take(10))
            {
                context.DrawRectangle(new SolidColorBrush(ch.TraceColor), null,
                    new Rect(lx, rowY + 3, 12, 12));
                var lbl = new FormattedText(
                    $"{ch.DisplayName}  {ch.CurrentPressureText}",
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 11.5, axisBrush);
                context.DrawText(lbl, new Point(lx + 18, rowY + 1));
                rowY += rowH;
            }
        }

        DrawCursors(context, channels, plot, totalSpan, viewStart, viewSpan, axisBrush);
        DrawModeHint(context, axisBrush, plot, isStripMode);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var pointer = e.GetPosition(this);
        if (!_lastPlotRect.Contains(pointer)) return;

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

        if (!_lastPlotRect.Contains(point)) return;

        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed || props.IsMiddleButtonPressed)
        {
            _isPanning = true;
            e.Pointer.Capture(this);
            return;
        }

        if (!props.IsLeftButtonPressed) return;
        if (_isStripMode) return;

        var nearA = false;
        var nearB = false;
        if (ShowCursors)
        {
            nearA = Math.Abs(point.X - CursorToX(_cursorA)) <= 16;
            nearB = Math.Abs(point.X - CursorToX(_cursorB)) <= 16;
        }

        if (ShowCursors && (nearA || nearB || e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
        {
            _dragCursorA = nearA || (!nearB && e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            _isDraggingCursor = true;
            SetCursorFromX(point.X, _dragCursorA);
        }
        else
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

        if (_isPanning && !_isStripMode)
        {
            var totalSpan = Math.Max(1, GetTotalSpan());
            var viewSpan = ComputeViewSpan(totalSpan);
            var maxStart = Math.Max(0, totalSpan - viewSpan);
            var viewStart = StartFraction * maxStart;
            var shift = -(point.X - _lastPointer.X) / Math.Max(1, _lastPlotRect.Width) * viewSpan;
            viewStart = Math.Clamp(viewStart + shift, 0, maxStart);
            StartFraction = maxStart <= 0 ? 0 : viewStart / maxStart;
            InvalidateVisual();
        }
        else if (_isDraggingCursor)
        {
            SetCursorFromX(point.X, _dragCursorA);
        }
        else if (!_isStripMode && ShowCursors)
        {
            var nearA = Math.Abs(point.X - CursorToX(_cursorA)) <= 14;
            var nearB = Math.Abs(point.X - CursorToX(_cursorB)) <= 14;
            Cursor = (nearA || nearB) ? SizeCursor
                : _lastPlotRect.Contains(point) ? PanCursor
                : ArrowCursor;
        }
        else
        {
            Cursor = (!_isStripMode && _lastPlotRect.Contains(point)) ? PanCursor : ArrowCursor;
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

    private void RenderStacked(
        DrawingContext context,
        IReadOnlyList<DeviceChannelViewModel> channels,
        Rect plot,
        double viewStart, double viewSpan, double ySpan,
        IBrush axisBrush, Pen gridPen, Pen axisPen)
    {
        var count = channels.Count;
        var rawH = plot.Height / count;
        var spanSamples = Math.Max(2, (int)Math.Round(viewSpan));
        var markerStep = Math.Max(1, spanSamples / 120);
        var dashStyle = DashStyle.Dash;

        // Shared X-axis labels at the bottom of the full plot
        var startSec = viewStart * SampleIntervalMs / 1000.0;
        var endSec = (viewStart + viewSpan) * SampleIntervalMs / 1000.0;
        var timeSec = endSec - startSec;
        if (timeSec > 0.001)
        {
            var xi = NiceTickInterval(timeSec, 6);
            for (var t = Math.Ceiling(startSec / xi) * xi; t <= endSec + xi * 0.01; t += xi)
            {
                var x = plot.Left + (t - startSec) / timeSec * plot.Width;
                if (x < plot.Left - 1 || x > plot.Right + 1) continue;
                context.DrawLine(axisPen, new Point(x, plot.Bottom), new Point(x, plot.Bottom + 4));
                var ft = new FormattedText(FormatElapsedTime(t), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, axisBrush);
                context.DrawText(ft, new Point(x - ft.Width * 0.5, plot.Bottom + 7));
            }
        }

        // Border axes
        context.DrawLine(axisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        context.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));

        for (var ci = 0; ci < count; ci++)
        {
            var channel = channels[ci];
            var stripTop = plot.Top + ci * rawH;
            var strip = new Rect(plot.Left, stripTop, plot.Width, rawH);

            // Strip separator
            if (ci > 0)
                context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#566D80"), 0.4), 1),
                    new Point(strip.Left, strip.Top), new Point(strip.Right, strip.Top));

            // Y-axis: 3 ticks per strip (0%, 50%, 100% of range)
            for (var ti = 0; ti <= 2; ti++)
            {
                var tickVal = MinValue + (MaxValue - MinValue) * ti * 0.5;
                var norm = Math.Clamp((tickVal - MinValue) / ySpan, 0, 1);
                var y = strip.Bottom - norm * strip.Height;
                context.DrawLine(gridPen, new Point(strip.Left, y), new Point(strip.Right, y));
                context.DrawLine(axisPen, new Point(strip.Left - 4, y), new Point(strip.Left, y));
                var lbl = FormatYLabel(tickVal);
                var ft = new FormattedText(lbl, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9, axisBrush);
                context.DrawText(ft, new Point(strip.Left - 5 - ft.Width, y - ft.Height * 0.5));
            }

            // Vertical X grid lines within strip
            if (timeSec > 0.001)
            {
                var xi = NiceTickInterval(timeSec, 6);
                for (var t = Math.Ceiling(startSec / xi) * xi; t <= endSec + xi * 0.01; t += xi)
                {
                    var x = strip.Left + (t - startSec) / timeSec * strip.Width;
                    if (x < strip.Left - 1 || x > strip.Right + 1) continue;
                    context.DrawLine(gridPen, new Point(x, strip.Top), new Point(x, strip.Bottom));
                }
            }

            // Channel trace
            var pen = new Pen(new SolidColorBrush(channel.TraceColor), 2);
            Point? prev = null;
            var prevSlot = -1;
            for (var i = 0; i < channel.Samples.Count; i++)
            {
                if (i < viewStart || i > viewStart + viewSpan) continue;
                var sample = channel.Samples[i];
                double x;
                var slot = 0;
                if (_isStripMode)
                {
                    slot = i % spanSamples;
                    x = strip.Left + (slot / Math.Max(1d, spanSamples - 1d)) * strip.Width;
                }
                else
                {
                    x = strip.Left + ((i - viewStart) / Math.Max(1, viewSpan)) * strip.Width;
                }
                var normalized = Math.Clamp((sample.PressurePsig - MinValue) / ySpan, 0, 1);
                var y = strip.Bottom - normalized * strip.Height;
                var pt = new Point(x, y);
                if (prev is { } p && (!_isStripMode || slot > prevSlot))
                    context.DrawLine(pen, p, pt);
                if (ShowPointMarkers && i % markerStep == 0)
                    context.DrawEllipse(new SolidColorBrush(channel.TraceColor), null, pt, 2.5, 2.5);
                prev = pt;
                prevSlot = slot;
            }

            // Alarm lines within strip
            if (ShowAlarmLines)
            {
                var loY = strip.Bottom - Math.Clamp((LowAlarmValue - MinValue) / ySpan, 0, 1) * strip.Height;
                var hiY = strip.Bottom - Math.Clamp((HighAlarmValue - MinValue) / ySpan, 0, 1) * strip.Height;
                if (loY > strip.Top && loY < strip.Bottom)
                    context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#F59E0B")), 1, dashStyle),
                        new Point(strip.Left, loY), new Point(strip.Right, loY));
                if (hiY > strip.Top && hiY < strip.Bottom)
                    context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#EF4444")), 1, dashStyle),
                        new Point(strip.Left, hiY), new Point(strip.Right, hiY));
            }

            // Channel label overlay (top-left of strip)
            context.DrawRectangle(new SolidColorBrush(Color.Parse("#B3101828")), null,
                new Rect(strip.Left + 4, strip.Top + 3, 210, 17), 3, 3);
            context.DrawRectangle(new SolidColorBrush(channel.TraceColor), null,
                new Rect(strip.Left + 8, strip.Top + 6, 10, 10));
            var lblFt = new FormattedText($"{channel.DisplayName}  {channel.CurrentPressureText}",
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 11, axisBrush);
            context.DrawText(lblFt, new Point(strip.Left + 22, strip.Top + 3));
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
        if (!ShowCursors || _isStripMode) return;

        _cursorA = Math.Clamp(_cursorA, 0, totalSpan);
        _cursorB = Math.Clamp(_cursorB, 0, totalSpan);

        var xA = plot.Left + ((_cursorA - viewStart) / Math.Max(1, viewSpan)) * plot.Width;
        var xB = plot.Left + ((_cursorB - viewStart) / Math.Max(1, viewSpan)) * plot.Width;

        // Cursor lines
        context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#E2E8F0"), 0.85), 1.5),
            new Point(xA, plot.Top), new Point(xA, plot.Bottom));
        context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#F59E0B"), 0.85), 1.5),
            new Point(xB, plot.Top), new Point(xB, plot.Bottom));

        // Cursor drag handles
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#E2E8F0")), null,
            new Rect(xA - 5, plot.Top, 10, 7));
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#F59E0B")), null,
            new Rect(xB - 5, plot.Top, 10, 7));

        var idxA = (int)Math.Round(_cursorA);
        var idxB = (int)Math.Round(_cursorB);
        var dt = Math.Abs(idxB - idxA) * Math.Max(1, SampleIntervalMs) / 1000d;
        var tA = idxA * SampleIntervalMs / 1000.0;
        var tB = idxB * SampleIntervalMs / 1000.0;

        // Multi-channel stats overlay box (bottom-left of plot)
        var lineH = 16.0;
        var boxRows = 1 + channels.Count; // header row + one row per channel
        var boxH = boxRows * lineH + 10;
        var boxW = Math.Min(plot.Width - 8, 440.0);
        var bx = plot.Left + 4;
        var by = plot.Bottom - boxH - 4;

        context.DrawRectangle(new SolidColorBrush(Color.Parse("#CC0D1B2A")), null,
            new Rect(bx, by, boxW, boxH), 4, 4);

        // Header: Δt info
        var headerText = $"A: {FormatElapsedTime(tA)}   B: {FormatElapsedTime(tB)}   \u0394t: {dt:F2}s";
        var headerFt = new FormattedText(headerText, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI", FontStyle.Normal, FontWeight.SemiBold),
            11, textBrush);
        context.DrawText(headerFt, new Point(bx + 6, by + 4));

        // Per-channel rows
        for (var ci = 0; ci < channels.Count; ci++)
        {
            var ch = channels[ci];
            var cIdxA = Math.Clamp(idxA, 0, ch.Samples.Count - 1);
            var cIdxB = Math.Clamp(idxB, 0, ch.Samples.Count - 1);
            if (ch.Samples.Count == 0) continue;
            var pA = ch.Samples[cIdxA].PressurePsig;
            var pB = ch.Samples[cIdxB].PressurePsig;
            var dp = pB - pA;
            var slope = dt > 0 ? dp / dt : 0;
            var rowY = by + 4 + lineH * (ci + 1);

            // Color swatch
            context.DrawRectangle(new SolidColorBrush(ch.TraceColor), null,
                new Rect(bx + 6, rowY + 3, 9, 9));

            var rowText = $"{ch.DisplayName,-14}  A: {pA,9:F1}   B: {pB,9:F1}   \u0394P: {dp:+0.0;-0.0} psig   dP/dt: {slope:+0.0;-0.0} psig/s";
            var rowFt = new FormattedText(rowText, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"),
                10.5, new SolidColorBrush(ch.TraceColor));
            context.DrawText(rowFt, new Point(bx + 20, rowY));
        }
    }

    private static void DrawModeHint(DrawingContext context, IBrush textBrush, Rect plot, bool isStripMode)
    {
        var hint = isStripMode
            ? "Strip Chart  |  scroll wheel = zoom"
            : "Sliding Window  |  scroll = zoom  \u2022  right-drag = pan  \u2022  drag cursor handles  \u2022  Shift+click = cursor A";
        var ft = new FormattedText(hint, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10.5, textBrush);
        context.DrawText(ft, new Point(plot.Left + 4, Math.Max(0, plot.Top - 13)));
    }

    private int GetTotalSpan()
    {
        var channels = Series;
        if (channels is null || channels.Count == 0) return 1;
        var max = channels.Where(c => c.IsVisible && c.Samples.Count > 1)
            .Select(c => c.Samples.Count).DefaultIfEmpty(2).Max();
        return Math.Max(1, max - 1);
    }

    private void SetCursorFromX(double x, bool cursorA)
    {
        var rel = Math.Clamp((x - _lastPlotRect.Left) / Math.Max(1, _lastPlotRect.Width), 0, 1);
        var sample = Math.Clamp(_activeViewStart + rel * _activeViewSpan, 0, _activeTotalSpan);
        if (CursorSnapToData) sample = Math.Round(sample);
        if (cursorA) _cursorA = sample; else _cursorB = sample;
        InvalidateVisual();
    }

    private double CursorToX(double cursor)
    {
        var rel = (cursor - _activeViewStart) / Math.Max(1, _activeViewSpan);
        return _lastPlotRect.Left + Math.Clamp(rel, 0, 1) * _lastPlotRect.Width;
    }

    private bool IsStripChartMode() =>
        GraphMode.Contains("Strip", StringComparison.OrdinalIgnoreCase);

    private double ComputeViewSpan(int totalSpan)
    {
        var clampedTotal = Math.Max(1, totalSpan);
        var minVisible = Math.Min(MinVisibleSamples, clampedTotal);
        return Math.Clamp(clampedTotal * WindowFraction, minVisible, clampedTotal);
    }

    private static double NiceTickInterval(double range, int targetCount = 6)
    {
        if (range <= 0) return 1;
        var raw = range / targetCount;
        var mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var norm = raw / mag;
        double nice = norm < 1.5 ? 1 : norm < 3.5 ? 2 : norm < 7.5 ? 5 : 10;
        return nice * mag;
    }

    private static string FormatYLabel(double value) =>
        Math.Abs(value) >= 10000 ? $"{value / 1000:F0}k"
        : Math.Abs(value) >= 1000 ? $"{value:F0}"
        : $"{value:F1}";

    private static string FormatElapsedTime(double seconds)
    {
        if (seconds < 60) return $"{seconds:F0}s";
        var m = (int)(seconds / 60);
        var s = (int)(seconds % 60);
        return $"{m}:{s:D2}";
    }
}
