using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using EdgeLink.Core;
using EdgeLink.Services;

namespace EdgeLink;

public partial class MainWindow : Window {
    private readonly AppController _controller;
    private readonly DispatcherTimer _messageTimer = new() { Interval = TimeSpan.FromSeconds(7) };
    private readonly List<Action> _valueRefreshers = [];
    private object? _panelSubject;
    private TextBox? _labelBox;
    private bool _syncing;

    internal MainWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();

        LayoutView.Controller = controller;
        LayoutView.SelectionChanged += RefreshPanel;
        LayoutView.Message += ShowMessage;
        controller.Changed += OnControllerChanged;

        _messageTimer.Tick += (_, _) => {
            _messageTimer.Stop();
            StatusText.Text = DefaultStatus();
        };
        StatusText.Text = DefaultStatus();

        _syncing = true;
        try {
            AutostartBox.SelectedIndex = (int) Autostart.Get();
        }
        catch (Exception e) {
            Log.Write("Reading the autostart state failed", e);
        }

        _syncing = false;

        OnControllerChanged();
    }

    protected override void OnClosed(EventArgs e)
    {
        _controller.Changed -= OnControllerChanged;
        base.OnClosed(e);
    }

    private string DefaultStatus()
    {
        var text = "Przeciągnij od krawędzi jednego monitora do krawędzi drugiego, aby utworzyć połączenie. "
                   + "Przesuwaj je i zmieniaj jego długość uchwytami; zmiany działają od razu.";
        text += _controller.HotkeyAvailable
            ? $" Pauza: {AppController.HotkeyText}."
            : $" Skrót pauzy {AppController.HotkeyText} jest zajęty przez inny program.";
        if (!AppController.IsElevated) {
            text += " EdgeLink działa bez uprawnień administratora, więc nie zadziała, gdy aktywne jest okno uruchomione jako administrator.";
        }

        return text;
    }

    private void ShowMessage(string message)
    {
        StatusText.Text = message;
        _messageTimer.Stop();
        _messageTimer.Start();
    }

    private void OnControllerChanged()
    {
        _syncing = true;
        EnabledBox.IsChecked = _controller.Config.Settings.Enabled;
        BlockBox.IsChecked = _controller.Config.Settings.BlockUnlinkedEdges;
        PhysicalBox.IsChecked = _controller.Config.Settings.PhysicalLayout;
        _syncing = false;

        // the selection may refer to something that no longer exists
        if (LayoutView.SelectedLink is { } link && !_controller.Config.Links.Contains(link)) LayoutView.Select(null);
        if (LayoutView.SelectedMonitor is { } monitor && !_controller.Config.Monitors.Contains(monitor)) LayoutView.Select(null);

        LayoutView.InvalidateVisual();
        RefreshPanel();
    }

    // ---------------------------------------------------------------- toolbar

    private void EnabledBox_Click(object sender, RoutedEventArgs e)
    {
        if (!_syncing) _controller.SetEnabled(EnabledBox.IsChecked == true);
    }

    private void BlockBox_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;

        _controller.Config.Settings.BlockUnlinkedEdges = BlockBox.IsChecked == true;
        _controller.Commit();
    }

    private void PhysicalBox_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;

        _controller.Config.Settings.PhysicalLayout = PhysicalBox.IsChecked == true;
        _controller.Commit();
    }

    private void Identify_Click(object sender, RoutedEventArgs e) => IdentifyOverlay.ShowAll(_controller);

    private void Refresh_Click(object sender, RoutedEventArgs e) => _controller.RefreshMonitors();

    private void AutostartBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || AutostartBox.SelectedIndex < 0) return;

        bool changed;
        try {
            changed = Autostart.Set((AutostartMode) AutostartBox.SelectedIndex);
        }
        catch (Exception exception) {
            Log.Write("Changing autostart failed", exception);
            changed = false;
        }

        if (changed) return;

        _syncing = true;
        try {
            AutostartBox.SelectedIndex = (int) Autostart.Get();
        }
        catch (Exception) {
            AutostartBox.SelectedIndex = 0;
        }

        _syncing = false;
        ShowMessage("Nie udało się zmienić ustawienia autostartu.");
    }

    // ---------------------------------------------------------------- side panel

    private void RefreshPanel()
    {
        var subject = (object?) LayoutView.SelectedLink ?? LayoutView.SelectedMonitor;

        // while the same thing stays selected only the values are refreshed, so that the text box
        // being typed in, or a control being clicked, is not replaced under the user's hands
        if (subject != null && ReferenceEquals(subject, _panelSubject)) {
            if (subject is Link) {
                foreach (var refresh in _valueRefreshers) refresh();
                return;
            }

            if (Keyboard.FocusedElement is TextBox focused && SidePanel.IsAncestorOf(focused)) return;
        }

        _panelSubject = subject;
        _valueRefreshers.Clear();
        _labelBox = null;
        SidePanel.Children.Clear();

        switch (subject) {
            case Link link:
                BuildLinkPanel(link);
                break;
            case MonitorProfile monitor:
                BuildMonitorPanel(monitor);
                break;
            default:
                BuildOverview();
                break;
        }
    }

    private void BuildOverview()
    {
        var config = _controller.Config;

        Add(Heading("Połączenia"));
        if (config.Links.Count == 0) {
            Add(Note("Nie ma jeszcze żadnych połączeń. Złap krawędź monitora na schemacie i przeciągnij do krawędzi innego monitora."));
        }
        else {
            foreach (var link in config.Links) Add(LinkRow(link));
        }

        var offline = _controller.OfflineProfiles.ToList();
        if (offline.Count > 0) {
            Add(SubHeading("Odłączone monitory"));
            Add(Note("Ich połączenia są zachowane i zaczną działać, gdy monitor wróci."));

            foreach (var profile in offline) {
                var count = config.Links.Count(l => l.A.MonitorId == profile.Id || l.B.MonitorId == profile.Id);
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

                var forget = new Button { Content = "Zapomnij", ToolTip = "Usuwa ten monitor razem z jego połączeniami" };
                forget.Click += (_, _) => _controller.RemoveProfile(profile);
                DockPanel.SetDock(forget, Dock.Right);
                row.Children.Add(forget);

                row.Children.Add(new TextBlock {
                    Text = $"{profile.Label} · {profile.Fingerprint.Width} × {profile.Fingerprint.Height} · połączeń: {count}",
                    VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
                });
                Add(row);
            }
        }

        Add(SubHeading("Jak to działa"));
        Add(Note("Połączenie łączy fragment krawędzi jednego monitora z fragmentem krawędzi drugiego. Fragmenty mogą mieć różną długość — pozycja kursora jest przeliczana proporcjonalnie, więc mały odcinek może prowadzić na całą krawędź."));
        Add(Note("Monitory nie muszą się stykać w ustawieniach Windows. Kliknij monitor, aby zmienić jego nazwę albo poprawić rozpoznanie."));
    }

    private void BuildMonitorPanel(MonitorProfile profile)
    {
        var config = _controller.Config;
        var online = _controller.FindOnline(profile.Id);
        var fingerprint = profile.Fingerprint;

        Add(Heading("Monitor"));

        Add(Caption("Nazwa w EdgeLink"));
        _labelBox = new TextBox { Text = profile.Label, Margin = new Thickness(0, 0, 0, 12) };
        _labelBox.TextChanged += (_, _) => {
            profile.Label = _labelBox.Text;
            _controller.Commit();
        };
        Add(_labelBox);

        var identity = fingerprint.HasEdid
            ? fingerprint.Serial.Length > 0 ? "pewna (numer seryjny z EDID)" : "dobra (model z EDID i port)"
            : "słaba — wyświetlacz wirtualny bez EDID, rozpoznawany po rozdzielczości i położeniu";
        Add(Note($"Nazwa w Windows: {(fingerprint.FriendlyName.Length > 0 ? fingerprint.FriendlyName : "(brak)")}\n"
                 + $"Rozdzielczość: {fingerprint.Width} × {fingerprint.Height} px\n"
                 + $"Położenie w Windows: {fingerprint.X}, {fingerprint.Y}\n"
                 + $"Stan: {(online != null ? "podłączony" : "odłączony")}\n"
                 + $"Identyfikacja: {identity}"));

        AddSizeEditor(profile);

        var others = config.Monitors.Where(m => !ReferenceEquals(m, profile)).ToList();
        if (others.Count > 0) {
            Add(SubHeading("To nie ten monitor?"));
            Add(Note("Jeśli EdgeLink pomylił dwa wyświetlacze (np. dwa tablety zgłaszające się tak samo), zamień je miejscami. Nazwy i połączenia zostają, zmienia się tylko to, który fizyczny ekran jest którym."));

            foreach (var other in others) {
                var swap = new Button {
                    Content = $"Zamień z: {other.Label}{(_controller.FindOnline(other.Id) == null ? " (odłączony)" : "")}",
                    HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 6)
                };
                swap.Click += (_, _) => {
                    _controller.SwapIdentity(profile, other);
                    IdentifyOverlay.ShowAll(_controller);
                };
                Add(swap);
            }
        }

        var links = config.Links.Where(l => l.A.MonitorId == profile.Id || l.B.MonitorId == profile.Id).ToList();
        Add(SubHeading("Połączenia tego monitora"));
        if (links.Count == 0) Add(Note("Brak. Przeciągnij od jego krawędzi do krawędzi innego monitora."));
        foreach (var link in links) Add(LinkRow(link));
    }

    /// <summary>Lets the user type the screen diagonal in, for monitors that misreport their size.</summary>
    private void AddSizeEditor(MonitorProfile profile)
    {
        Add(SubHeading("Rozmiar fizyczny"));

        var box = new TextBox { Width = 72, HorizontalContentAlignment = HorizontalAlignment.Right };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new TextBlock { Text = "Przekątna", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        row.Children.Add(box);
        row.Children.Add(new TextBlock { Text = "cali", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75, Margin = new Thickness(8, 0, 0, 0) });
        Add(row);

        var info = Note("");
        Add(info);

        void Show()
        {
            box.Text = profile.DiagonalInches?.ToString("0.#") ?? "";

            if (_controller.FindOnline(profile.Id) is not { } online) {
                info.Text = "Monitor odłączony — rozmiar zostanie ustalony, gdy wróci.";
                return;
            }

            var size = PhysicalLayout.Estimate(profile, online.Detected);
            var source = size.Source switch {
                PhysicalSizeSource.Manual => "wpisana przekątna",
                PhysicalSizeSource.Edid => "zgłoszony przez monitor (EDID)",
                PhysicalSizeSource.EdidCorrected => "EDID z poprawką — monitor zgłasza proporcje niezgodne z rozdzielczością",
                _ => $"szacunek ze skalowania Windows ({online.Detected.Dpi * 100 / 96}%)"
            };
            info.Text = $"{size.WidthMm / 10:0.#} × {size.HeightMm / 10:0.#} cm ({size.DiagonalInches:0.#}″) — {source}."
                        + size.Source switch {
                            PhysicalSizeSource.Manual => " Wyczyść pole, aby wrócić do odczytu automatycznego.",
                            PhysicalSizeSource.Edid => " Wpisz przekątną, aby go nadpisać.",
                            _ => " Wpisz przekątną, jeśli na schemacie wygląda nie tak."
                        };
        }

        void Apply()
        {
            var text = box.Text.Trim().Replace("\"", "").Replace("″", "").Replace(',', '.');
            if (text.Length == 0) {
                profile.DiagonalInches = null;
            }
            else if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var inches)
                     && inches is >= PhysicalLayout.MinDiagonalInches and <= PhysicalLayout.MaxDiagonalInches) {
                profile.DiagonalInches = Math.Round(inches, 1);
            }
            else {
                ShowMessage($"Przekątna musi być liczbą od {PhysicalLayout.MinDiagonalInches:0} do {PhysicalLayout.MaxDiagonalInches:0} cali.");
            }

            _controller.Commit();
            Show();
        }

        box.LostKeyboardFocus += (_, _) => {
            // skip the commit when nothing changed, so merely clicking away does not rewrite the config
            if (box.Text.Trim() != (profile.DiagonalInches?.ToString("0.#") ?? "")) Apply();
        };
        box.KeyDown += (_, e) => {
            if (e.Key != Key.Enter) return;

            Apply();
            box.SelectAll();
            e.Handled = true;
        };

        Show();
    }

    private void BuildLinkPanel(Link link)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        header.Children.Add(Dot(link));
        header.Children.Add(new TextBlock { Text = "Połączenie", FontSize = 16, FontWeight = FontWeights.SemiBold });
        Add(header);

        AddEndEditor(link.A);
        AddEndEditor(link.B);

        var ratio = Note("");
        ratio.Margin = new Thickness(0, 14, 0, 8);
        Add(ratio);
        _valueRefreshers.Add(() => {
            var a = SpanPixels(link.A);
            var b = SpanPixels(link.B);
            ratio.Text = a > 0 && b > 0
                ? $"{a} px ⇄ {b} px — 1 px po pierwszej stronie to {(double) b / a:0.##} px po drugiej."
                : "Połączenie zacznie działać, gdy oba monitory będą podłączone.";
        });

        var reversed = new CheckBox {
            Content = "Odwróć kierunek mapowania",
            ToolTip = "Początek pierwszego fragmentu prowadzi na koniec drugiego"
        };
        reversed.Click += (_, _) => {
            link.Reversed = reversed.IsChecked == true;
            _controller.Commit();
        };
        Add(reversed);

        var enabled = new CheckBox { Content = "Aktywne" };
        enabled.Click += (_, _) => {
            link.Enabled = enabled.IsChecked == true;
            _controller.Commit();
        };
        Add(enabled);

        _valueRefreshers.Add(() => {
            reversed.IsChecked = link.Reversed;
            enabled.IsChecked = link.Enabled;
        });

        var delete = new Button { Content = "Usuń połączenie", Margin = new Thickness(0, 16, 0, 0) };
        delete.Click += (_, _) => {
            LayoutView.Select(null);
            _controller.DeleteLink(link);
        };
        Add(delete);

        foreach (var refresh in _valueRefreshers) refresh();
    }

    private int SpanPixels(LinkEnd end) =>
        _controller.FindOnline(end.MonitorId) is { } online
            ? (int) Math.Round((end.End - end.Start) * online.Bounds.EdgeLength(end.Edge))
            : 0;

    private void AddEndEditor(LinkEnd end)
    {
        var profile = _controller.Config.FindMonitor(end.MonitorId);
        Add(SubHeading($"{profile?.Label ?? "?"} — {EdgeName(end.Edge)}"));

        if (_controller.FindOnline(end.MonitorId) == null) {
            Add(Note("Monitor odłączony. Zakres będzie można zmienić, gdy wróci."));
            return;
        }

        int Length() => _controller.FindOnline(end.MonitorId)?.Bounds.EdgeLength(end.Edge) ?? 1;

        (double Lo, double Hi) Free()
        {
            var occupied = SpanMath.Occupied(_controller.Config, end.MonitorId, end.Edge, except: end);
            return SpanMath.FreeIntervalAt((end.Start + end.End) / 2, occupied) ?? (end.Start, end.End);
        }

        var startBox = new TextBox { Width = 72, HorizontalContentAlignment = HorizontalAlignment.Right };
        var endBox = new TextBox { Width = 72, HorizontalContentAlignment = HorizontalAlignment.Right };
        var total = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75, Margin = new Thickness(8, 0, 0, 0) };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new TextBlock { Text = "od", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        row.Children.Add(startBox);
        row.Children.Add(new TextBlock { Text = "do", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) });
        row.Children.Add(endBox);
        row.Children.Add(total);
        Add(row);

        void ShowValues(bool force)
        {
            var length = Length();
            if (force || !startBox.IsKeyboardFocused) startBox.Text = Math.Round(end.Start * length).ToString("0");
            if (force || !endBox.IsKeyboardFocused) endBox.Text = Math.Round(end.End * length).ToString("0");
            total.Text = $"z {length} px";
        }

        void Apply()
        {
            var length = Length();
            var (lo, hi) = Free();
            var pixel = 1.0 / length;

            if (int.TryParse(startBox.Text.Trim(), out var startPixels)) {
                end.Start = SpanMath.SnapToPixel(Math.Max(lo, Math.Min(startPixels * pixel, end.End - pixel)), length);
            }

            if (int.TryParse(endBox.Text.Trim(), out var endPixels)) {
                end.End = SpanMath.SnapToPixel(Math.Min(hi, Math.Max(endPixels * pixel, end.Start + pixel)), length);
            }

            _controller.Commit();
            ShowValues(force: true);
        }

        foreach (var box in new[] { startBox, endBox }) {
            box.LostKeyboardFocus += (_, _) => Apply();
            box.KeyDown += (_, e) => {
                if (e.Key != Key.Enter) return;

                Apply();
                box.SelectAll();
                e.Handled = true;
            };
        }

        var stretch = new Button { Content = "Rozciągnij na całe wolne miejsce" };
        stretch.Click += (_, _) => {
            (end.Start, end.End) = Free();
            _controller.Commit();
        };
        Add(stretch);

        _valueRefreshers.Add(() => ShowValues(force: false));
    }

    // ---------------------------------------------------------------- small building blocks

    private void Add(UIElement element) => SidePanel.Children.Add(element);

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };

    private static TextBlock SubHeading(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 6), TextWrapping = TextWrapping.Wrap };

    private static TextBlock Caption(string text) => new() { Text = text, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 4) };

    private static TextBlock Note(string text) => new() { Text = text, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };

    private Ellipse Dot(Link link) => new() {
        Width = 12, Height = 12, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
        Fill = new SolidColorBrush(LayoutCanvas.ColorOf(_controller.Config, link))
    };

    private Button LinkRow(Link link)
    {
        var config = _controller.Config;
        var waiting = _controller.FindOnline(link.A.MonitorId) == null || _controller.FindOnline(link.B.MonitorId) == null;

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(Dot(link));
        content.Children.Add(new TextBlock {
            Text = $"{config.FindMonitor(link.A.MonitorId)?.Label} {EdgeArrow(link.A.Edge)}  ⇄  {config.FindMonitor(link.B.MonitorId)?.Label} {EdgeArrow(link.B.Edge)}"
                   + (waiting ? "  (czeka na monitor)" : !link.Enabled ? "  (wyłączone)" : ""),
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var row = new Button {
            Content = content, Margin = new Thickness(0, 0, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Opacity = waiting || !link.Enabled ? 0.7 : 1
        };
        row.Click += (_, _) => LayoutView.Select(link);
        return row;
    }

    private static string EdgeName(Edge edge) => edge switch {
        Edge.Left => "lewa krawędź",
        Edge.Right => "prawa krawędź",
        Edge.Top => "górna krawędź",
        _ => "dolna krawędź"
    };

    private static string EdgeArrow(Edge edge) => edge switch {
        Edge.Left => "◀",
        Edge.Right => "▶",
        Edge.Top => "▲",
        _ => "▼"
    };
}
