using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EdgeLink.Core;
using EdgeLink.Services;
using Microsoft.Win32;

namespace EdgeLink;

/// <summary>
/// Draws the connected monitors, by pixel count or at their physical sizes, with every link shown as a pair of
/// coloured spans joined by a curve, and lets the user create and reshape links by dragging.
/// </summary>
internal sealed class LayoutCanvas : FrameworkElement {
    private const double Padding = 36;
    private const double Gap = 9; // each monitor is drawn this much smaller, so touching monitors show a gutter
    private const double SpanThickness = 7;
    private const double HandleRadius = 6;
    private const double EdgeReach = 11;

    private static readonly Color[] Palette = [
        Color.FromRgb(0x4C, 0xC2, 0xFF), Color.FromRgb(0xFF, 0x8C, 0x42), Color.FromRgb(0x6C, 0xCB, 0x5F), Color.FromRgb(0xE8, 0x6B, 0xB0),
        Color.FromRgb(0xB4, 0x8C, 0xFF), Color.FromRgb(0xF2, 0xC7, 0x44), Color.FromRgb(0x3F, 0xD6, 0xC6), Color.FromRgb(0xFF, 0x6B, 0x6B)
    ];

    private enum DragMode {
        None,
        PendingSpan,
        MoveSpan,
        Handle,
        NewLink
    }

    private readonly record struct EdgeHit(OnlineMonitor Monitor, Edge Edge, double Fraction);

    private AppController? _controller;
    private readonly Dictionary<Guid, RectD> _layout = []; // where each monitor sits, in pixels or millimetres
    private RectD _union;
    private double _scale;
    private Point _origin;

    private DragMode _mode;
    private LinkEnd? _dragEnd;
    private bool _dragIsStart;
    private double _grabOffset;
    private Point _downPoint;
    private Point _mouse;
    private EdgeHit? _newFrom;
    private EdgeHit? _hoverEdge;

    public LayoutCanvas()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
    }

    public AppController? Controller {
        get => _controller;
        set {
            _controller = value;
            InvalidateVisual();
        }
    }

    public Link? SelectedLink { get; private set; }
    public MonitorProfile? SelectedMonitor { get; private set; }

    public event Action? SelectionChanged;

    /// <summary>A short message for the user, e.g. why a link could not be created.</summary>
    public event Action<string>? Message;

    public static Color ColorOf(AppConfig config, Link link) => Palette[Math.Max(0, config.Links.IndexOf(link)) % Palette.Length];

    public void Select(Link? link, MonitorProfile? monitor = null)
    {
        if (ReferenceEquals(SelectedLink, link) && ReferenceEquals(SelectedMonitor, monitor)) return;

        SelectedLink = link;
        SelectedMonitor = link == null ? monitor : null;
        InvalidateVisual();
        SelectionChanged?.Invoke();
    }

    // ---------------------------------------------------------------- geometry

    private bool UpdateView()
    {
        if (_controller == null || _controller.Online.Count == 0) return false;

        var online = _controller.Online;
        _layout.Clear();
        if (_controller.Config.Settings.PhysicalLayout) {
            var arranged = PhysicalLayout.Arrange(online.Select(o => (o.Bounds, PhysicalLayout.Estimate(o.Profile, o.Detected))).ToList());
            for (var i = 0; i < online.Count; i++) _layout[online[i].Profile.Id] = arranged[i];
        }
        else {
            foreach (var o in online) _layout[o.Profile.Id] = new RectD(o.Bounds.X, o.Bounds.Y, o.Bounds.Width, o.Bounds.Height);
        }

        var left = _layout.Values.Min(r => r.X);
        var top = _layout.Values.Min(r => r.Y);
        _union = new RectD(left, top, _layout.Values.Max(r => r.Right) - left, _layout.Values.Max(r => r.Bottom) - top);

        _scale = Math.Min((ActualWidth - 2 * Padding) / _union.Width, (ActualHeight - 2 * Padding) / _union.Height);
        if (!(_scale > 0)) return false;

        _origin = new Point((ActualWidth - _union.Width * _scale) / 2, (ActualHeight - _union.Height * _scale) / 2);
        return true;
    }

    private Rect ViewRect(OnlineMonitor monitor)
    {
        // a monitor can appear between two layout passes; it is drawn from the next one on
        if (!_layout.TryGetValue(monitor.Profile.Id, out var bounds)) return new Rect(-100, -100, 4, 4);

        return new Rect(
            _origin.X + (bounds.X - _union.X) * _scale + Gap,
            _origin.Y + (bounds.Y - _union.Y) * _scale + Gap,
            Math.Max(4, bounds.Width * _scale - 2 * Gap),
            Math.Max(4, bounds.Height * _scale - 2 * Gap));
    }

    private static Point EdgePoint(Rect r, Edge edge, double fraction) => edge switch {
        Edge.Left => new Point(r.Left, r.Top + fraction * r.Height),
        Edge.Right => new Point(r.Right, r.Top + fraction * r.Height),
        Edge.Top => new Point(r.Left + fraction * r.Width, r.Top),
        _ => new Point(r.Left + fraction * r.Width, r.Bottom)
    };

    private static Vector Outward(Edge edge) => edge switch {
        Edge.Left => new Vector(-1, 0),
        Edge.Right => new Vector(1, 0),
        Edge.Top => new Vector(0, -1),
        _ => new Vector(0, 1)
    };

    private static double FractionAt(Rect r, Edge edge, Point p) =>
        Math.Clamp(edge.IsVertical() ? (p.Y - r.Top) / r.Height : (p.X - r.Left) / r.Width, 0, 1);

    private static double DistanceToEdge(Rect r, Edge edge, Point p)
    {
        var nearest = EdgePoint(r, edge, FractionAt(r, edge, p));
        return (p - nearest).Length;
    }

    /// <summary>A point on the centre line of a span, which is drawn just inside the monitor's edge.</summary>
    private static Point SpanPoint(Rect r, Edge edge, double fraction) => EdgePoint(r, edge, fraction) - Outward(edge) * (SpanThickness / 2);

    private static Rect SpanRect(Rect r, LinkEnd end)
    {
        var a = SpanPoint(r, end.Edge, end.Start);
        var b = SpanPoint(r, end.Edge, end.End);
        return end.Edge.IsVertical()
            ? new Rect(a.X - SpanThickness / 2, a.Y, SpanThickness, Math.Max(1, b.Y - a.Y))
            : new Rect(a.X, a.Y - SpanThickness / 2, Math.Max(1, b.X - a.X), SpanThickness);
    }

    /// <summary>Every link end that sits on a connected monitor, with where it is drawn.</summary>
    private IEnumerable<(Link Link, LinkEnd End, OnlineMonitor Monitor, Rect View)> VisibleEnds()
    {
        if (_controller == null) yield break;

        foreach (var link in _controller.Config.Links) {
            foreach (var end in link.Ends) {
                var monitor = _controller.FindOnline(end.MonitorId);
                if (monitor != null) yield return (link, end, monitor, ViewRect(monitor));
            }
        }
    }

    // ---------------------------------------------------------------- hit testing

    private (LinkEnd End, bool IsStart)? HitHandle(Point p)
    {
        if (SelectedLink == null) return null;

        foreach (var (link, end, _, view) in VisibleEnds()) {
            if (!ReferenceEquals(link, SelectedLink)) continue;

            if ((p - SpanPoint(view, end.Edge, end.Start)).Length <= HandleRadius + 3) return (end, true);
            if ((p - SpanPoint(view, end.Edge, end.End)).Length <= HandleRadius + 3) return (end, false);
        }

        return null;
    }

    private (Link Link, LinkEnd End, Rect View)? HitSpan(Point p)
    {
        foreach (var (link, end, _, view) in VisibleEnds()) {
            var rect = SpanRect(view, end);
            rect.Inflate(4, 4);
            if (rect.Contains(p)) return (link, end, view);
        }

        return null;
    }

    private EdgeHit? HitEdge(Point p, double reach = EdgeReach)
    {
        if (_controller == null) return null;

        EdgeHit? best = null;
        var bestDistance = reach;
        foreach (var monitor in _controller.Online) {
            var view = ViewRect(monitor);
            foreach (var edge in Enum.GetValues<Edge>()) {
                var distance = DistanceToEdge(view, edge, p);
                if (distance > bestDistance) continue;

                bestDistance = distance;
                best = new EdgeHit(monitor, edge, FractionAt(view, edge, p));
            }
        }

        return best;
    }

    private OnlineMonitor? HitMonitor(Point p) => _controller?.Online.FirstOrDefault(o => ViewRect(o).Contains(p));

    // ---------------------------------------------------------------- input

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        if (_controller == null || !UpdateView()) return;

        var p = e.GetPosition(this);
        _downPoint = _mouse = p;

        if (HitHandle(p) is { } handle) {
            _mode = DragMode.Handle;
            _dragEnd = handle.End;
            _dragIsStart = handle.IsStart;
        }
        else if (HitSpan(p) is { } span) {
            Select(span.Link);
            _mode = DragMode.PendingSpan;
            _dragEnd = span.End;
            _grabOffset = FractionAt(span.View, span.End.Edge, p) - span.End.Start;
        }
        else if (HitEdge(p) is { } edge) {
            _mode = DragMode.NewLink;
            _newFrom = edge;
        }
        else {
            Select(null, HitMonitor(p)?.Profile);
            return;
        }

        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_controller == null || !UpdateView()) return;

        var p = _mouse = e.GetPosition(this);

        switch (_mode) {
            case DragMode.None:
                UpdateHover(p);
                break;

            case DragMode.PendingSpan:
                if ((p - _downPoint).Length > 3) _mode = DragMode.MoveSpan;
                break;

            case DragMode.MoveSpan:
            case DragMode.Handle:
                DragSpan(p);
                break;

            case DragMode.NewLink:
                InvalidateVisual();
                break;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_mode == DragMode.None) return;

        var p = e.GetPosition(this);
        var mode = _mode;
        var from = _newFrom;
        _mode = DragMode.None;
        _dragEnd = null;
        _newFrom = null;
        ReleaseMouseCapture();

        if (mode == DragMode.NewLink && from is { } start && UpdateView()) {
            if ((p - _downPoint).Length < 4) {
                Select(null, start.Monitor.Profile); // a plain click on an edge selects the monitor
            }
            else if (HitEdge(p, EdgeReach * 2) is { } target && !(target.Monitor == start.Monitor && target.Edge == start.Edge)) {
                CreateLink(start, target);
            }
        }

        InvalidateVisual();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        if (_controller == null || !UpdateView() || HitSpan(e.GetPosition(this)) is not { } span) return;

        Select(span.Link);
        var link = span.Link;

        var reverse = new MenuItem { Header = "Reverse mapping direction", IsCheckable = true, IsChecked = link.Reversed };
        reverse.Click += (_, _) => {
            link.Reversed = !link.Reversed;
            _controller.Commit();
        };
        var delete = new MenuItem { Header = "Delete link", InputGestureText = "Delete" };
        delete.Click += (_, _) => DeleteSelected();

        ContextMenu = new ContextMenu { Items = { reverse, delete }, IsOpen = true };
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Delete && SelectedLink != null) {
            DeleteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) {
            if (_mode != DragMode.None) {
                _mode = DragMode.None;
                _newFrom = null;
                _dragEnd = null;
                ReleaseMouseCapture();
                InvalidateVisual();
            }
            else {
                Select(null);
            }

            e.Handled = true;
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hoverEdge != null) {
            _hoverEdge = null;
            InvalidateVisual();
        }
    }

    private void DeleteSelected()
    {
        if (SelectedLink is not { } link || _controller == null) return;

        Select(null);
        _controller.DeleteLink(link);
    }

    private void UpdateHover(Point p)
    {
        EdgeHit? hover = null;

        if (HitHandle(p) is { } handle) {
            Cursor = handle.End.Edge.IsVertical() ? Cursors.SizeNS : Cursors.SizeWE;
        }
        else if (HitSpan(p) != null) {
            Cursor = Cursors.SizeAll;
        }
        else if ((hover = HitEdge(p)) != null) {
            Cursor = Cursors.Cross;
        }
        else {
            Cursor = Cursors.Arrow;
        }

        if (hover != _hoverEdge) {
            _hoverEdge = hover;
            InvalidateVisual();
        }
    }

    private void DragSpan(Point p)
    {
        if (_controller == null || _dragEnd is not { } end || _controller.FindOnline(end.MonitorId) is not { } monitor) return;

        var view = ViewRect(monitor);
        var length = monitor.Bounds.EdgeLength(end.Edge);
        var occupied = SpanMath.Occupied(_controller.Config, end.MonitorId, end.Edge, except: end);
        var (lo, hi) = SpanMath.FreeIntervalAt((end.Start + end.End) / 2, occupied) ?? (end.Start, end.End);
        var at = FractionAt(view, end.Edge, p);
        var minimum = Math.Min(8.0 / length, hi - lo);

        if (_mode == DragMode.Handle) {
            if (_dragIsStart) end.Start = SpanMath.SnapToPixel(Math.Max(lo, Math.Min(at, end.End - minimum)), length);
            else end.End = SpanMath.SnapToPixel(Math.Min(hi, Math.Max(at, end.Start + minimum)), length);
        }
        else {
            var size = end.End - end.Start;
            var start = SpanMath.SnapToPixel(Math.Max(lo, Math.Min(at - _grabOffset, hi - size)), length);
            end.Start = start;
            end.End = Math.Min(hi, start + size);
        }

        _controller.Commit();
    }

    private void CreateLink(EdgeHit from, EdgeHit to)
    {
        if (_controller == null) return;

        var config = _controller.Config;
        var lengthA = from.Monitor.Bounds.EdgeLength(from.Edge);
        var lengthB = to.Monitor.Bounds.EdgeLength(to.Edge);

        // start with spans of equal pixel length, so the cursor keeps its speed across the link;
        // the user then stretches either side to taste
        var pixels = Math.Min(lengthA, lengthB);
        var a = SpanMath.Place(from.Fraction, (double) pixels / lengthA, SpanMath.Occupied(config, from.Monitor.Profile.Id, from.Edge));
        var b = SpanMath.Place(to.Fraction, (double) pixels / lengthB, SpanMath.Occupied(config, to.Monitor.Profile.Id, to.Edge));
        if (a == null || b == null) {
            Message?.Invoke("Another link already occupies this part of the edge — shrink it or start in a free spot.");
            return;
        }

        var link = new Link {
            A = new LinkEnd {
                MonitorId = from.Monitor.Profile.Id, Edge = from.Edge,
                Start = SpanMath.SnapToPixel(a.Value.Start, lengthA), End = SpanMath.SnapToPixel(a.Value.End, lengthA)
            },
            B = new LinkEnd {
                MonitorId = to.Monitor.Profile.Id, Edge = to.Edge,
                Start = SpanMath.SnapToPixel(b.Value.Start, lengthB), End = SpanMath.SnapToPixel(b.Value.End, lengthB)
            }
        };
        if (link.A.End <= link.A.Start || link.B.End <= link.B.Start) {
            Message?.Invoke("Not enough free space on this edge.");
            return;
        }

        config.Links.Add(link);
        Select(link);
        _controller.Commit();
    }

    // ---------------------------------------------------------------- rendering

    private static bool IsDarkTheme()
    {
        try {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is 0;
        }
        catch (Exception) {
            return false;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        // a transparent fill makes the whole area receive mouse input
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var dark = IsDarkTheme();
        var text = new SolidColorBrush(dark ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1B, 0x1B, 0x1F));
        var subtle = new SolidColorBrush(dark ? Color.FromRgb(0xA6, 0xA6, 0xAE) : Color.FromRgb(0x5F, 0x5F, 0x68));
        var fill = new SolidColorBrush(dark ? Color.FromRgb(0x2E, 0x2E, 0x33) : Color.FromRgb(0xFF, 0xFF, 0xFF));
        var stroke = new Pen(new SolidColorBrush(dark ? Color.FromRgb(0x5C, 0x5C, 0x64) : Color.FromRgb(0xB4, 0xB4, 0xBC)), 1);
        var accent = dark ? Color.FromRgb(0x60, 0xCD, 0xFF) : Color.FromRgb(0x00, 0x5F, 0xB8);
        var selectedStroke = new Pen(new SolidColorBrush(accent), 2);

        if (_controller == null || !UpdateView()) {
            DrawText(dc, "No monitors detected", 14, subtle, new Point(ActualWidth / 2, ActualHeight / 2), bold: false);
            return;
        }

        var config = _controller.Config;

        foreach (var monitor in _controller.Online) {
            var view = ViewRect(monitor);
            var selected = ReferenceEquals(monitor.Profile, SelectedMonitor);
            dc.DrawRoundedRectangle(fill, selected ? selectedStroke : stroke, view, 6, 6);

            dc.PushClip(new RectangleGeometry(view));
            var center = new Point(view.Left + view.Width / 2, view.Top + view.Height / 2);
            DrawText(dc, monitor.Profile.Label, 14, text, center - new Vector(0, 10), bold: true);
            DrawText(dc, $"{monitor.Bounds.Width} × {monitor.Bounds.Height}{(monitor.Detected.IsPrimary ? " · primary" : "")}", 11, subtle, center + new Vector(0, 10), bold: false);
            dc.Pop();
        }

        if (_mode == DragMode.None && _hoverEdge is { } hover) {
            var view = ViewRect(hover.Monitor);
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x90, accent.R, accent.G, accent.B)), 3), EdgePoint(view, hover.Edge, 0), EdgePoint(view, hover.Edge, 1));
        }

        // connectors first, so the spans sit on top of them
        foreach (var link in config.Links) {
            if (_controller.FindOnline(link.A.MonitorId) is not { } a || _controller.FindOnline(link.B.MonitorId) is not { } b) continue;

            var color = ColorOf(config, link);
            var selected = ReferenceEquals(link, SelectedLink);
            var from = EdgePoint(ViewRect(a), link.A.Edge, (link.A.Start + link.A.End) / 2);
            var to = EdgePoint(ViewRect(b), link.B.Edge, (link.B.Start + link.B.End) / 2);
            var reach = Math.Clamp((to - from).Length / 2, 24, 110);

            var geometry = new StreamGeometry();
            using (var g = geometry.Open()) {
                g.BeginFigure(from, false, false);
                g.BezierTo(from + Outward(link.A.Edge) * reach, to + Outward(link.B.Edge) * reach, to, true, true);
            }

            var alpha = (byte) (!link.Enabled ? 0x50 : selected ? 0xFF : 0xA0);
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)), selected ? 2.5 : 1.5);
            if (!link.Enabled) pen.DashStyle = DashStyles.Dash;
            dc.DrawGeometry(null, pen, geometry);
        }

        foreach (var (link, end, _, view) in VisibleEnds()) {
            var color = ColorOf(config, link);
            var usable = link.Enabled && _controller.FindOnline(link.Other(end).MonitorId) != null;
            var brush = new SolidColorBrush(usable ? color : Color.FromArgb(0x70, color.R, color.G, color.B));
            var outline = ReferenceEquals(link, SelectedLink) ? new Pen(text, 1.2) : null;
            dc.DrawRoundedRectangle(brush, outline, SpanRect(view, end), 3, 3);
        }

        if (SelectedLink != null) {
            foreach (var (link, end, monitor, view) in VisibleEnds()) {
                if (!ReferenceEquals(link, SelectedLink)) continue;

                var pen = new Pen(new SolidColorBrush(ColorOf(config, link)), 2);
                dc.DrawEllipse(Brushes.White, pen, SpanPoint(view, end.Edge, end.Start), HandleRadius, HandleRadius);
                dc.DrawEllipse(Brushes.White, pen, SpanPoint(view, end.Edge, end.End), HandleRadius, HandleRadius);

                // the exact range is always in the side panel; on the schematic it only appears
                // for the span under the hand, where it would otherwise cover the monitor's name
                if (ReferenceEquals(end, _dragEnd)) DrawSpanLabel(dc, view, end, monitor.Bounds.EdgeLength(end.Edge), dark);
            }
        }

        if (_mode == DragMode.NewLink && _newFrom is { } start) {
            var pen = new Pen(new SolidColorBrush(accent), 2) { DashStyle = DashStyles.Dash };
            dc.DrawLine(pen, EdgePoint(ViewRect(start.Monitor), start.Edge, start.Fraction), _mouse);

            if (HitEdge(_mouse, EdgeReach * 2) is { } target && !(target.Monitor == start.Monitor && target.Edge == start.Edge)) {
                var view = ViewRect(target.Monitor);
                dc.DrawLine(new Pen(new SolidColorBrush(accent), 4), EdgePoint(view, target.Edge, 0), EdgePoint(view, target.Edge, 1));
            }
        }
    }

    /// <summary>Shows the pixel range of a selected span, just inside the monitor next to it.</summary>
    private void DrawSpanLabel(DrawingContext dc, Rect view, LinkEnd end, int edgeLength, bool dark)
    {
        var from = (int) Math.Round(end.Start * edgeLength);
        var to = (int) Math.Round(end.End * edgeLength);
        var formatted = Format($"{from} – {to} px", 11, dark ? Brushes.White : Brushes.Black, bold: false);

        var mid = EdgePoint(view, end.Edge, (end.Start + end.End) / 2) - Outward(end.Edge) * (SpanThickness + 10 + (end.Edge.IsVertical() ? formatted.Width / 2 : formatted.Height / 2));
        var box = new Rect(mid.X - formatted.Width / 2 - 5, mid.Y - formatted.Height / 2 - 2, formatted.Width + 10, formatted.Height + 4);

        // keep the label inside the monitor it belongs to
        box.X = Math.Max(view.Left + 2, Math.Min(box.X, view.Right - box.Width - 2));
        box.Y = Math.Max(view.Top + 2, Math.Min(box.Y, view.Bottom - box.Height - 2));

        dc.DrawRoundedRectangle(new SolidColorBrush(dark ? Color.FromArgb(0xE0, 0x18, 0x18, 0x1B) : Color.FromArgb(0xE8, 0xF3, 0xF3, 0xF5)), null, box, 4, 4);
        dc.DrawText(formatted, new Point(box.X + 5, box.Y + 2));
    }

    private FormattedText Format(string value, double size, Brush brush, bool bold) => new(
        value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
        size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private void DrawText(DrawingContext dc, string value, double size, Brush brush, Point center, bool bold)
    {
        var formatted = Format(value, size, brush, bold);
        dc.DrawText(formatted, new Point(center.X - formatted.Width / 2, center.Y - formatted.Height / 2));
    }
}
