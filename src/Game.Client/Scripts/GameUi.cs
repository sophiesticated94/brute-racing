using Game.Core;
using Godot;

namespace Game.Client;

public partial class GameUi : CanvasLayer
{
    public event Action? SoloRequested, HostRequested, JoinRequested, ReadyRequested, StartRequested, LeaveRequested, ReplayRequested;
    public event Action? LoadoutChanged;
    public LineEdit Address { get; private set; } = null!;
    public SpinBox Port { get; private set; } = null!;
    public LineEdit PlayerName { get; private set; } = null!;
    public OptionButton Weapon { get; private set; } = null!;
    public OptionButton Paint { get; private set; } = null!;
    public SpinBox Bots { get; private set; } = null!;
    public CheckBox Internet { get; private set; } = null!;
    private Control root = null!, menu = null!, lobby = null!, hud = null!, podium = null!, pause = null!;
    private Label lobbyList = null!, status = null!, raceInfo = null!, leaderboard = null!, resources = null!, countdown = null!, result = null!, respawn = null!;
    private ProgressBar health = null!, energy = null!;
    private Button start = null!, replay = null!, ready = null!;
    private Label notice = null!;
    private double noticeTime;
    private RaceMap map = null!;
    private AimReticle reticle = null!;
    public void HitMarker() => reticle.Hit();
    private static readonly Color Ink = new("e8eddf"), Muted = new("8da8af"), Accent = new("ffbe45");
    public string WeaponId => new[] { "machine_gun", "laser", "rocket" }[(int)Weapon.Selected];
    public override void _Ready()
    {
        root = new Control(); AddChild(root); root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); root.MouseFilter = Control.MouseFilterEnum.Ignore;
        var topBar = new ColorRect { Color = new Color(.035f, .075f, .105f, .94f), AnchorRight = 1, OffsetBottom = 64, MouseFilter = Control.MouseFilterEnum.Ignore }; root.AddChild(topBar);
        var bottomBar = new ColorRect { Color = new Color(.035f, .075f, .105f, .94f), AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1, OffsetTop = -38, MouseFilter = Control.MouseFilterEnum.Ignore }; root.AddChild(bottomBar);
        var brand = new Label { Text = "MCM  /  MOTORSPORT DIVISION", Position = new(38, 24) }; Font(brand, 18, Accent); root.AddChild(brand);
        var build = new Label { Text = "VOL. 01   •   CINDER CIRCUIT", AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -340, OffsetTop = 26, OffsetRight = -32, OffsetBottom = 60, HorizontalAlignment = HorizontalAlignment.Right };
        Font(build, 14, Muted); root.AddChild(build);
        menu = Card(new(38, 87), new(445, 694));
        var m = Stack(menu);
        AddText(m, "MINI CAR\nMAYHEM", 57, Ink);
        AddText(m, "MAŁE AUTA. DUŻY CHAOS.", 16, Accent);
        AddText(m, "Miedziany Kanion  /  3 okrążenia\nWyścigi bojowe • 1–8 kierowców", 16, Muted);
        m.AddChild(new HSeparator());
        PlayerName = new LineEdit { Text = "Kierowca", PlaceholderText = "Nazwa kierowcy", MaxLength = 20 }; m.AddChild(PlayerName);
        Weapon = new OptionButton(); foreach (string w in new[] { "Karabin · szybki i celny", "Laser · energia i przegrzanie", "Rakiety · obrażenia obszarowe" }) Weapon.AddItem(w); m.AddChild(Weapon);
        Paint = new OptionButton(); foreach (string color in new[] { "Bursztyn", "Turkus", "Koral", "Lawenda", "Limonka", "Kość słoniowa", "Pomarańcz", "Kobalt" }) Paint.AddItem(color); m.AddChild(Paint);
        Bots = new SpinBox { MinValue = 0, MaxValue = 7, Value = 3, Prefix = "Boty: " }; m.AddChild(Bots);
        AddButton(m, "SZYBKI WYŚCIG", () => SoloRequested?.Invoke(), true);
        var connection = new HBoxContainer(); m.AddChild(connection);
        Address = new LineEdit { Text = "127.0.0.1", PlaceholderText = "Adres IP hosta", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; connection.AddChild(Address);
        Port = new SpinBox { MinValue = 1024, MaxValue = 65535, Value = 28747, CustomMinimumSize = new(120, 0) }; connection.AddChild(Port);
        Internet = new CheckBox { Text = "Host internetowy · spróbuj UPnP" }; m.AddChild(Internet);
        var network = new HBoxContainer(); m.AddChild(network);
        AddButton(network, "UTWÓRZ POKÓJ", () => HostRequested?.Invoke());
        AddButton(network, "DOŁĄCZ", () => JoinRequested?.Invoke());
        Weapon.ItemSelected += _ => LoadoutChanged?.Invoke(); Paint.ItemSelected += _ => LoadoutChanged?.Invoke();
        PlayerName.TextChanged += _ => LoadoutChanged?.Invoke();
        lobby = Card(new(38, 87), new(445, 610)); var l = Stack(lobby);
        AddText(l, "PADDOCK", 40, Ink); AddText(l, "Miedziany Kanion  /  3 okrążenia", 16, Accent);
        AddText(l, "ROOK  /  wybierz uzbrojenie i lakier", 15, Muted);
        var lobbyWeapon = new OptionButton(); foreach (string w in new[] { "Karabin", "Laser", "Rakiety" }) lobbyWeapon.AddItem(w); l.AddChild(lobbyWeapon);
        var lobbyPaint = new OptionButton(); foreach (string c in new[] { "Bursztyn", "Turkus", "Koral", "Lawenda", "Limonka", "Kość słoniowa", "Pomarańcz", "Kobalt" }) lobbyPaint.AddItem(c); l.AddChild(lobbyPaint);
        lobbyWeapon.ItemSelected += index => { Weapon.Select((int)index); LoadoutChanged?.Invoke(); };
        lobbyPaint.ItemSelected += index => { Paint.Select((int)index); LoadoutChanged?.Invoke(); };
        lobby.VisibilityChanged += () => { lobbyWeapon.Select(Weapon.Selected); lobbyPaint.Select(Paint.Selected); };
        lobbyList = AddText(l, "", 17, Ink); lobbyList.CustomMinimumSize = new(0, 205);
        ready = AddButton(l, "GOTOWY / NIEGOTOWY", () => ReadyRequested?.Invoke(), true);
        start = AddButton(l, "START WYŚCIGU", () => StartRequested?.Invoke());
        AddButton(l, "WYJDŹ Z POKOJU", () => LeaveRequested?.Invoke()); lobby.Visible = false;
        hud = new Control(); root.AddChild(hud); hud.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); hud.MouseFilter = Control.MouseFilterEnum.Ignore;
        var raceCard = Card(new(32, 83), new(278, 133), hud); var raceStack = Stack(raceCard);
        raceInfo = AddText(raceStack, "", 27, Ink);
        var standingPanel = new PanelContainer { AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -300, OffsetRight = -32, OffsetTop = 83, OffsetBottom = 285, MouseFilter = Control.MouseFilterEnum.Ignore };
        standingPanel.AddThemeStyleboxOverride("panel", PanelStyle()); hud.AddChild(standingPanel);
        leaderboard = new Label(); Font(leaderboard, 19, Ink); standingPanel.AddChild(leaderboard);
        var dash = new PanelContainer { AnchorTop = 1, AnchorBottom = 1, OffsetLeft = 32, OffsetRight = 510, OffsetTop = -172, OffsetBottom = -38 };
        dash.AddThemeStyleboxOverride("panel", PanelStyle()); hud.AddChild(dash); var d = Stack(dash);
        resources = AddText(d, "", 19, Ink);
        health = Bar(d, new("ed7867")); energy = Bar(d, new("51c9d2"));
        countdown = new Label { AnchorLeft = .5f, AnchorRight = .5f, AnchorTop = .3f, AnchorBottom = .3f, OffsetLeft = -300, OffsetRight = 300, OffsetTop = 0, OffsetBottom = 130, HorizontalAlignment = HorizontalAlignment.Center };
        Font(countdown, 86, Accent); hud.AddChild(countdown);
        respawn = new Label { AnchorLeft = .5f, AnchorRight = .5f, AnchorTop = .52f, AnchorBottom = .52f, OffsetLeft = -350, OffsetRight = 350, OffsetBottom = 80, HorizontalAlignment = HorizontalAlignment.Center };
        Font(respawn, 23, Ink); hud.AddChild(respawn); hud.Visible = false;
        var mapPanel = new PanelContainer { AnchorLeft = 1, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1, OffsetLeft = -288, OffsetRight = -32, OffsetTop = -224, OffsetBottom = -48, MouseFilter = Control.MouseFilterEnum.Ignore };
        mapPanel.AddThemeStyleboxOverride("panel", PanelStyle()); hud.AddChild(mapPanel);
        map = new RaceMap { CustomMinimumSize = new(224, 140), MouseFilter = Control.MouseFilterEnum.Ignore }; mapPanel.AddChild(map);
        reticle = new AimReticle { MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 5 }; hud.AddChild(reticle);
        podium = Card(new(38, 87), new(510, 610)); var p = Stack(podium);
        AddText(p, "META.", 64, Accent); AddText(p, "KLASYFIKACJA KOŃCOWA", 17, Muted);
        result = AddText(p, "", 23, Ink); result.CustomMinimumSize = new(0, 325);
        replay = AddButton(p, "JESZCZE JEDEN WYŚCIG", () => ReplayRequested?.Invoke(), true);
        AddButton(p, "WRÓĆ DO MENU", () => LeaveRequested?.Invoke()); podium.Visible = false;
        pause = Card(new(500, 270), new(370, 190)); var pauseStack = Stack(pause);
        AddText(pauseStack, "PIT STOP", 32, Ink);
        AddButton(pauseStack, "WRÓĆ DO JAZDY", () => pause.Visible = false, true);
        AddButton(pauseStack, "OPUŚĆ WYŚCIG", () => LeaveRequested?.Invoke()); pause.Visible = false;
        status = new Label { AnchorLeft = 0, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1, OffsetLeft = 38, OffsetRight = -38, OffsetTop = -32, OffsetBottom = -8 };
        Font(status, 14, Muted); root.AddChild(status); status.Text = "WASD / STRZAŁKI  jazda     SPACJA  drift     MYSZ  celowanie     LPM  ogień     PPM  miny     ESC  menu     M  dźwięk";
        notice = new Label { AnchorLeft = .5f, AnchorRight = .5f, OffsetLeft = -450, OffsetRight = 450, OffsetTop = 72, OffsetBottom = 110, HorizontalAlignment = HorizontalAlignment.Center };
        Font(notice, 18, Accent); root.AddChild(notice);
    }
    public bool BlockingInput => pause.Visible;
    public void ConfigureMap(Vector3[] path) => map.SetTrack(path);
    public void UpdateMap((Vector3 Position, Color Color, bool Local)[] values) => map.UpdateCars(values);
    public void TogglePause() { if (hud.Visible) pause.Visible = !pause.Visible; }
    public void ShowMenu() { menu.Visible = true; lobby.Visible = hud.Visible = podium.Visible = pause.Visible = false; }
    public void ShowLobby(LobbyPlayer[] players, bool isHost, int local)
    {
        menu.Visible = false; lobby.Visible = true; hud.Visible = podium.Visible = pause.Visible = false;
        lobbyList.Text = string.Join("\n", players.Select((p, i) => $"{i + 1:00}  {p.Name}{(p.Id == local ? " ◂" : "")}   /   {(p.Bot ? "AI" : p.Ready ? "GOTOWY" : "…")}"));
        start.Visible = isHost; start.Disabled = players.Length < 2 || players.Any(p => !p.Ready);
        ready.Text = players.FirstOrDefault(p => p.Id == local)?.Ready == true ? "GOTOWY ✓  /  ZMIEŃ" : "ZGŁOŚ GOTOWOŚĆ";
    }
    public void ShowRace() { menu.Visible = lobby.Visible = podium.Visible = pause.Visible = false; hud.Visible = true; }
    public void ShowResults(string text, bool isHost) { menu.Visible = lobby.Visible = hud.Visible = pause.Visible = false; podium.Visible = true; result.Text = text; replay.Visible = isHost; }
    public void UpdateRace(int position, int count, int lap, int laps, double elapsed, float remaining, RacePhase phase,
        float hp, float maxHp, float e, float heat, int ammo, int mines, float speed, string board, float respawnTime, float invulnerability)
    {
        raceInfo.Text = $"{position:00} / {count:00}     OKR. {Math.Min(lap + 1, laps)}/{laps}\n{TimeText(elapsed)}  •  {(int)(speed * 3.6f)} km/h";
        leaderboard.Text = board;
        resources.Text = $"{(ammo < 0 ? "ENERGIA" : $"AMUNICJA {ammo:000}")}    MINY {mines:00}    CIEPŁO {(int)heat}%";
        health.Value = hp / maxHp * 100; energy.Value = e;
        countdown.Text = phase == RacePhase.Countdown ? Math.Max(1, (int)Math.Ceiling(remaining)).ToString() : elapsed < .7 ? "GAZ!" : "";
        respawn.Text = respawnTime > 0 ? $"WRACASZ NA TOR ZA {respawnTime:0.0} s" : invulnerability > 0 ? "OSŁONA PO RESPAWNIE" : remaining > 0 && phase == RacePhase.Racing ? $"META ZAMYKA SIĘ ZA {Math.Ceiling(remaining)} s" : "";
    }
    public void Notice(string text, double seconds = 5) { notice.Text = text; noticeTime = seconds; }
    public override void _Process(double delta) { if ((noticeTime -= delta) <= 0) notice.Text = ""; }
    public static string TimeText(double seconds) => $"{(int)seconds / 60:00}:{seconds % 60:00.00}";
    private Control Card(Vector2 position, Vector2 size, Control? parent = null)
    {
        var panel = new PanelContainer { Position = position, CustomMinimumSize = size, Size = size };
        panel.AddThemeStyleboxOverride("panel", PanelStyle()); (parent ?? root).AddChild(panel); return panel;
    }
    private static StyleBoxFlat PanelStyle() => new()
    {
        BgColor = new Color(.035f, .075f, .105f, .96f), BorderColor = new Color("34505c"),
        BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
        CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14,
        ContentMarginLeft = 23, ContentMarginRight = 23, ContentMarginTop = 20, ContentMarginBottom = 20
    };
    private static VBoxContainer Stack(Node parent) { var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 11); parent.AddChild(v); return v; }
    private static Label AddText(Node parent, string text, int size, Color color) { var label = new Label { Text = text }; Font(label, size, color); parent.AddChild(label); return label; }
    private static void Font(Control c, int size, Color color) { c.AddThemeFontSizeOverride("font_size", size); c.AddThemeColorOverride("font_color", color); }
    private static Button AddButton(Node parent, string text, Action action, bool accent = false)
    {
        var button = new Button { Text = text, CustomMinimumSize = new(0, 43), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        Font(button, 16, accent ? new("122532") : Ink);
        var style = new StyleBoxFlat { BgColor = accent ? Accent : new("223c49"), CornerRadiusTopLeft = 7, CornerRadiusTopRight = 7, CornerRadiusBottomLeft = 7, CornerRadiusBottomRight = 7, ContentMarginLeft = 12, ContentMarginRight = 12 };
        button.AddThemeStyleboxOverride("normal", style); var hover = (StyleBoxFlat)style.Duplicate(); hover.BgColor = hover.BgColor.Lightened(.14f); button.AddThemeStyleboxOverride("hover", hover);
        parent.AddChild(button); button.Pressed += action; return button;
    }
    private static ProgressBar Bar(Node parent, Color color)
    {
        var bar = new ProgressBar { CustomMinimumSize = new(0, 10), ShowPercentage = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new("213c48") }); bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = color }); parent.AddChild(bar); return bar;
    }
}
