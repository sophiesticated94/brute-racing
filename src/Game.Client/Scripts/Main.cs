using System.Text.Json;
using Game.Core;
using Godot;

namespace Game.Client;

public partial class Main : Node3D
{
    private sealed class Racer(LobbyPlayer info, ArcadeCar car, RaceProgress progress)
    {
        public LobbyPlayer Info = info;
        public ArcadeCar Car = car;
        public RaceProgress Progress = progress;
        public InputGate Gate = new();
        public ComputedLoadoutStats Build = null!;
        public float SlowRemaining, SlowFactor = 1;
        public float Energy = 100, Heat, PrimaryCooldown, MineCooldown, Respawn, Invulnerable = 2;
        public int Ammo, Mines;
        public bool Overheated;
        public float Unstuck;
        public Vector3 PreviousPosition = car.Position;
        public VehicleState? NetworkState;
        public readonly List<(double Time, VehicleState State)> History = [];
    }
    private sealed class Ordnance(int id, int owner, bool mine, Vector3 velocity, Node3D node)
    {
        public int Id = id, Owner = owner;
        public bool Mine = mine;
        public Vector3 Velocity = velocity;
        public Node3D Node = node;
        public float Age;
        public int SmokeFrame;
        public WeaponDefinition Stats = null!;
    }
    private sealed class EmpField(Vector3 position, float radius, float duration, float factor)
    { public Vector3 Position = position; public float Radius = radius, Remaining = duration, Factor = factor; }
    private readonly List<EmpField> empFields = [];
    private GameCatalog catalog = null!;
    private string contentHash = "";
    private RaceTrack track = null!;
    private GameUi ui = null!;
    private GameAudio audio = null!;
    private Camera3D camera = null!;
    private Node3D raceRoot = null!;
    private Node3D effects = null!;
    private EnetTransport? transport;
    private readonly Dictionary<int, LobbyPlayer> players = [];
    private readonly Dictionary<int, Racer> racers = [];
    private readonly List<Ordnance> ordnance = [];
    private readonly List<PredictedInput> pending = [];
    private readonly Queue<PlayerInputCommand> recentInputs = [];
    private readonly Dictionary<int, double> acceptedHello = [];
    private readonly HashSet<long> seenEvents = [];
    private RaceClock clock = new();
    private RacePhase phase = RacePhase.Lobby;
    private uint raceId, tick, localSequence, lastSnapshotTick;
    private long eventSequence;
    private int projectileSequence;
    private int localId = 1;
    private bool inSession, isHost, dedicated, autoPlay, autoStart, smoke, resultsWritten, headless;
    private double now, connectionStart, lastServerPacket, autoStartAt = 2;
    private double raceElapsed;
    private float raceRemaining;
    private float desiredAim;
    private int[] finalRanking = [];
    private readonly Dictionary<string, string> arguments = [];
    private double maxSeconds = 240;
    private string resultPath = "";
    private bool noWeapons;
    private int expectedPlayers = 1;
    private Random random = new(340);
    private uint receivedSnapshots;
    private int shots, destructions, checkpointPasses;
    private double frameTotal, frameWorst;
    private uint frameCount;
    private double artificialLatency;
    private float artificialLoss;
    private readonly List<(double At, int Target, byte[] Packet, bool Reliable, int Channel)> delayed = [];
    private bool screenshotSaved;
    private readonly Dictionary<string, int> eventCounts = [];
    private int completedRaces;
    private bool roundRecorded;
    private readonly List<object> roundReports = [];
    private int requiredRounds = 1;
    private double wallFrameTotal;
    private ulong lastFrameUsec;
    private double wallWorstFrame;
    private double correctionTotal;
    private float correctionMax;
    private int correctionCount, hardCorrections;
    private readonly NetworkDiagnostics diagnostics = new();
    private double nextPing;
    private double shakeTime;
    private MeshInstance3D? aimLine;
    private double nextAimLine;

    public override void _Ready()
    {
        foreach (var raw in OS.GetCmdlineUserArgs())
        {
            var parts = raw.TrimStart('-').Split('=', 2); arguments[parts[0]] = parts.Length == 2 ? parts[1] : "true";
        }
        headless = DisplayServer.GetName() == "headless";
        dedicated = arguments.ContainsKey("dedicated"); smoke = arguments.ContainsKey("smoke");
        autoPlay = smoke || arguments.ContainsKey("autoplay"); autoStart = smoke || arguments.ContainsKey("autostart");
        noWeapons = arguments.ContainsKey("no-weapons");
        if (arguments.TryGetValue("results", out var path)) resultPath = path;
        if (arguments.TryGetValue("timeout", out var timeout)) maxSeconds = double.Parse(timeout, System.Globalization.CultureInfo.InvariantCulture);
        if (arguments.TryGetValue("wait-players", out var wait)) expectedPlayers = int.Parse(wait);
        if (arguments.TryGetValue("rounds", out var rounds)) requiredRounds = Math.Max(1, int.Parse(rounds));
        if (arguments.TryGetValue("latency", out var latency)) artificialLatency = double.Parse(latency) / 1000;
        if (arguments.TryGetValue("loss", out var loss)) artificialLoss = float.Parse(loss, System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            var docs = new Dictionary<string, string>();
            foreach (var file in GameCatalog.Files)
                docs[file] = Godot.FileAccess.GetFileAsString("res://data/" + file);
            catalog = GameCatalog.Load(file => docs[file]);
            contentHash = PacketCodec.ContentHash(docs.OrderBy(p => p.Key).Select(p => p.Value));
            BuildWorld();
            ui = new GameUi(); AddChild(ui); audio = new GameAudio(); AddChild(audio);
            ui.ConfigureMap(track.Path);
            ui.SoloRequested += StartSolo; ui.HostRequested += StartHosting; ui.JoinRequested += JoinHost;
            ui.ReadyRequested += ToggleReady; ui.StartRequested += TryStartRace; ui.LeaveRequested += () => Leave("Sesja zakończona.");
            ui.ReplayRequested += () => { if (isHost) StartRace(); }; ui.LoadoutChanged += ChangeLoadout;
            if (arguments.TryGetValue("port", out var port)) ui.Port.Value = int.Parse(port);
            if (arguments.TryGetValue("bots", out var bots)) ui.Bots.Value = Math.Clamp(int.Parse(bots), 0, 7);
            if (arguments.TryGetValue("weapon", out var weapon)) ui.Weapon.Select(weapon == "laser" ? 1 : weapon == "rocket" ? 2 : 0);
            if (arguments.TryGetValue("name", out var name)) ui.PlayerName.Text = name;
            if (arguments.TryGetValue("join", out var address)) { ui.Address.Text = address; JoinHost(); }
            else if (arguments.ContainsKey("host")) StartHosting();
            else if (smoke || arguments.ContainsKey("solo")) StartSolo();
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }

    private PlayerLoadoutDto SelectedLoadout() => new("rook", ui.WeaponId, arguments.TryGetValue("weapon-level", out var level) ? Math.Clamp(int.Parse(level), 1, 5) : 1, new(), arguments.TryGetValue("mine-level", out var mine) ? Math.Clamp(int.Parse(mine), 1, 5) : 1);
    private string SelectedName() => string.IsNullOrWhiteSpace(ui.PlayerName.Text) ? "Kierowca" : ui.PlayerName.Text.Trim();
    private void StartSolo()
    {
        LeaveInternal(); inSession = isHost = true; localId = 1;
        players[1] = new(1, SelectedName(), (int)ui.Paint.Selected, true, false, SelectedLoadout());
        AddBots((int)ui.Bots.Value); StartRace();
    }
    private void StartHosting()
    {
        try
        {
            LeaveInternal(); transport = CreateTransport(); transport.Host((int)ui.Port.Value, 8);
            inSession = isHost = true; localId = dedicated ? 0 : 1;
            if (!dedicated) players[1] = new(1, SelectedName(), (int)ui.Paint.Selected, autoStart, false, SelectedLoadout());
            AddBots((int)ui.Bots.Value); connectionStart = now; autoStartAt = now + 2;
            PublishLobby(); ui.Notice($"Host UDP :{ui.Port.Value:0}  •  dołącz po adresie IP", 8);
            if (ui.Internet.ButtonPressed) MapPort((int)ui.Port.Value);
            GD.Print($"HOST_READY port={(int)ui.Port.Value}");
        }
        catch (Exception e) { Leave(e.Message); }
    }
    private void JoinHost()
    {
        try
        {
            LeaveInternal(); transport = CreateTransport(); transport.Join(ui.Address.Text.Trim(), (int)ui.Port.Value);
            inSession = true; isHost = false; localId = transport.LocalId; connectionStart = lastServerPacket = now;
            ui.Notice("Łączenie z gospodarzem…", 10);
        }
        catch (Exception e) { Leave(e.Message); }
    }
    private void AddBots(int count)
    {
        for (int i = 0; i < count && players.Count < 8; i++)
        {
            int id = -i - 1;
            players[id] = new(id, new[] { "BOLT", "NOVA", "RUST", "ECHO", "FLINT", "APEX", "DUNE" }[i], (i + 1) % 8, true, true,
                new("rook", new[] { "machine_gun", "rocket", "laser" }[i % 3], 1, new()));
        }
    }
    private void ChangeLoadout()
    {
        if (!inSession || phase != RacePhase.Lobby) return;
        if (isHost && players.TryGetValue(localId, out var p))
        {
            players[localId] = p with { Name = SelectedName(), Color = (int)ui.Paint.Selected, Loadout = SelectedLoadout(), Ready = false }; PublishLobby();
        }
        else SendControl(1, new("loadout", Name: SelectedName(), Color: (int)ui.Paint.Selected, Loadout: SelectedLoadout()));
    }
    private void ToggleReady()
    {
        if (!players.TryGetValue(localId, out var p)) return;
        if (isHost) { players[localId] = p with { Ready = !p.Ready }; PublishLobby(); }
        else SendControl(1, new("ready"));
    }
    private void PublishLobby()
    {
        if (!inSession) return;
        var all = players.Values.ToArray(); ui.ShowLobby(all, isHost, localId); SendControl(0, new("lobby", Players: all));
    }
    private void TryStartRace()
    {
        if (!isHost || players.Count < 2 || players.Values.Any(p => !p.Ready)) return;
        StartRace();
    }
    private void StartRace()
    {
        if (!isHost) return;
        raceId++;
        SendControl(0, new("start", Players: players.Values.ToArray(), RaceId: raceId));
        InitializeRace();
    }
    private void InitializeRace()
    {
        ClearRace(); phase = RacePhase.Countdown; clock = new(); clock.Start();
        localSequence = 0; lastSnapshotTick = 0; tick = 0; resultsWritten = false; receivedSnapshots = 0;
        recentInputs.Clear(); pending.Clear(); seenEvents.Clear(); eventCounts.Clear(); finalRanking = []; roundRecorded = false;
        shots = destructions = checkpointPasses = 0;
        int slot = 0;
        foreach (var p in players.Values)
        {
            var car = new ArcadeCar { Name = "Car_" + p.Id, PlayerId = p.Id };
            raceRoot.AddChild(car); car.Build(VehicleStatsCalculator.Calculate(catalog, p.Loadout), Art.Colors[p.Color % 8], isHost || p.Id == localId);
            // Remote interpolated bodies are presentation proxies. Only the host resolves
            // car-to-car collisions; frozen proxies must not block the local predictor.
            if (!isHost) { car.CollisionLayer = p.Id == localId ? 2u : 0u; car.CollisionMask = p.Id == localId ? 1u : 0u; }
            car.ResetAt(track.Spawn(slot++), track.YawAt(0)); car.TurretYaw = desiredAim = track.YawAt(0);
            var racer = new Racer(p, car, new(catalog.Track.Checkpoints, catalog.Track.Laps));
            racer.Build = VehicleStatsCalculator.CalculateBuild(catalog, p.Loadout);
            Refill(racer); racers.Add(p.Id, racer);
        }
        ui.ShowRace(); ui.Notice("Miedziany Kanion  /  rozgrzej opony", 2.5);
        if (!isHost && arguments.ContainsKey("probe-authority"))
        {
            var fake = new VehicleState(1, new(9999, 9999, 9999), System.Numerics.Quaternion.Identity,
                System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero, 0, 0, 0, 0, 0, 0, 99, 0, 9999, 0, 0, 0, 0);
            Send(1, PacketCodec.EncodeSnapshot(new(raceId, uint.MaxValue, RacePhase.Podium, 0, 0, [fake])), true, 0);
            Send(1, PacketCodec.EncodeJson(PacketKind.Event, new RaceEvent(raceId, long.MaxValue, "destroy", 1, 0, new(9999, 0, 0), new(), 0)), true, 0);
            SendControl(1, new("results", Ranking: [localId], RaceId: raceId));
            GD.Print("AUTHORITY_PROBE_SENT");
        }
        GD.Print($"RACE_START players={racers.Count} host={isHost}");
    }
    public override void _PhysicsProcess(double delta)
    {
        if (catalog is null) return;
        now += delta; transport?.Poll(); FlushDelayed();
        if (inSession && !isHost && transport is not null && now >= nextPing)
        { nextPing = now + 1; SendControl(1, new("ping", SentTime: now)); }
        if (smoke && now > maxSeconds && !resultsWritten)
        {
            GD.PushError("SMOKE_TIMEOUT"); WriteResults(false); GetTree().Quit(2); return;
        }
        if (inSession && !isHost && now - lastServerPacket > 12) { Leave("Brak odpowiedzi gospodarza (12 s)."); return; }
        if (isHost && phase == RacePhase.Lobby && autoStart && now >= autoStartAt && players.Values.Count(p => !p.Bot) >= expectedPlayers)
        {
            foreach (var id in players.Keys.ToArray()) players[id] = players[id] with { Ready = true };
            TryStartRace();
        }
        if (!inSession || phase == RacePhase.Lobby) return;
        tick++;
        if (racers.TryGetValue(localId, out var local))
        {
            var command = autoPlay ? BotInput(local, ++localSequence) : ReadInput(local, ++localSequence);
            if (ui.BlockingInput) command = command.Neutral;
            if (isHost) local.Gate.Accept(command, now);
            else
            {
                local.Car.Command = command;
                recentInputs.Enqueue(command); while (recentInputs.Count > 3) recentInputs.Dequeue();
                Send(1, PacketCodec.EncodeInputs(recentInputs.ToArray()), false, 1);
                if (phase == RacePhase.Racing && local.Car.Health > 0)
                {
                    float a = Mathf.Atan2(local.Car.GlobalPosition.X / catalog.Track.RadiusX, local.Car.GlobalPosition.Z / catalog.Track.RadiusZ);
                    pending.Add(new(command, local.Car.Grounded, track.SurfaceGrip(local.Car.GlobalPosition), track.Height(a), local.Car.MovementScale));
                    if (pending.Count > 180) pending.RemoveAt(0);
                }
                local.Car.Driving = phase == RacePhase.Racing && local.Car.Health > 0;
                local.Car.SurfaceGrip = track.SurfaceGrip(local.Car.GlobalPosition);
                local.Car.TurretYaw = Mathf.RotateToward(local.Car.TurretYaw, command.AimYaw, (float)delta * local.Car.Stats.TurretTraverseSpeed);
            }
        }
        foreach (var field in empFields.ToArray())
        {
            field.Remaining -= (float)delta;
            if (field.Remaining <= 0) { empFields.Remove(field); continue; }
            if (isHost) foreach (var victim in racers.Values)
                if (victim.Car.GlobalPosition.DistanceTo(field.Position) <= field.Radius) { victim.SlowRemaining = .2f; victim.SlowFactor = field.Factor; }
        }
        if (isHost)
        {
            foreach (var r in racers.Values)
            {
                if (r.Info.Bot) r.Gate.Accept(BotInput(r, r.Gate.LastSequence + 1), now);
                UpdateRacer(r, (float)delta);
            }
            UpdateOrdnance((float)delta);
            clock.Update(delta, racers.Count > 0 && racers.Values.All(r => r.Progress.Finished));
            raceElapsed = clock.Elapsed; raceRemaining = (float)clock.Remaining;
            var previous = phase; phase = clock.Phase;
            if (previous != RacePhase.Podium && phase == RacePhase.Podium) FinishRace();
            if (tick % 2 == 0 && transport is not null) Send(0, PacketCodec.EncodeSnapshot(CreateSnapshot()), false, 2);
        }
        else UpdateOrdnance((float)delta);
        if (phase == RacePhase.Podium && smoke && finalRanking.Length > 0 && !roundRecorded)
        {
            roundRecorded = true; completedRaces++;
            roundReports.Add(new { raceId, ranking = finalRanking, events = new Dictionary<string, int>(eventCounts) });
            if (completedRaces < requiredRounds)
            {
                if (isHost) GetTree().CreateTimer(1).Timeout += StartRace;
            }
            else
            {
                WriteResults(true);
                // Allow reliable results and final snapshot to leave the host before closing ENet.
                GetTree().CreateTimer(1).Timeout += () => GetTree().Quit();
            }
        }
    }

    private int[] Ranking()
    {
        if (finalRanking.Length > 0) return finalRanking;
        return RaceRanking.Rank(racers.Values.Select(r =>
        {
            int gate = isHost ? r.Progress.NextCheckpoint : r.NetworkState?.NextCheckpoint ?? 1;
            int passed = isHost ? r.Progress.PassedGates : r.NetworkState?.PassedGates ?? 0;
            double? finished = isHost ? r.Progress.FinishTime : r.NetworkState is { FinishTime: >= 0 } s ? s.FinishTime : null;
            return new Standing(r.Info.Id, passed, r.Car.GlobalPosition.DistanceTo(track.GatePoint(gate)), finished);
        }));
    }
    private void FinishRace()
    {
        finalRanking = Ranking(); Send(0, PacketCodec.EncodeSnapshot(CreateSnapshot()), false, 2);
        // Final state is reliable, so the podium does not depend on the last unreliable snapshot.
        Send(0, PacketCodec.EncodeSnapshot(CreateSnapshot()), true, 0);
        SendControl(0, new("results", Ranking: finalRanking, RaceId: raceId)); ShowPodium();
        GD.Print("RACE_FINISH ranking=" + string.Join(',', finalRanking));
    }
    private void ClearRace()
    {
        foreach (var child in raceRoot.GetChildren()) { raceRoot.RemoveChild(child); child.QueueFree(); }
        foreach (var child in effects.GetChildren()) { effects.RemoveChild(child); child.QueueFree(); }
        racers.Clear(); ordnance.Clear(); empFields.Clear(); pending.Clear(); recentInputs.Clear();
    }
    private void LeaveInternal()
    {
        transport?.Dispose(); transport = null; inSession = false; phase = RacePhase.Lobby;
        players.Clear(); acceptedHello.Clear(); delayed.Clear(); finalRanking = []; if (raceRoot is not null) ClearRace();
    }
    private void Leave(string message) { LeaveInternal(); ui.ShowMenu(); ui.Notice(message, 7); GD.Print(message); }
    private void WriteResults(bool success)
    {
        resultsWritten = true;
        var snapshot = CreateSnapshot();
        var report = new
        {
            success, host = isHost, localId, tick, phase = phase.ToString(), elapsed = raceElapsed,
            ranking = Ranking(), receivedSnapshots, shots, destructions, checkpointPasses,
            simulatedFps = frameTotal > 0 ? frameCount / frameTotal : 0,
            averageFps = wallFrameTotal > 0 ? (frameCount - 1) / wallFrameTotal : 0, worstFrameMs = wallWorstFrame * 1000,
            rounds = roundReports, events = eventCounts,
            rttMs = diagnostics.RttMs, jitterMs = diagnostics.JitterMs, maxJitterMs = diagnostics.MaxJitterMs, snapshotAgeMs = diagnostics.SnapshotAgeMs(now), pingSamples = diagnostics.PingSamples,
            meanCorrection = correctionCount > 0 ? correctionTotal / correctionCount : 0, maxCorrection = correctionMax, hardCorrections,
            vehicles = racers.Values.Select(r => new { id = r.Info.Id, laps = isHost ? r.Progress.CompletedLaps : r.NetworkState?.Lap ?? 0,
                passed = isHost ? r.Progress.PassedGates : r.NetworkState?.PassedGates ?? 0, position = r.Car.GlobalPosition.ToString(),
                finish = isHost ? r.Progress.FinishTime ?? -1 : r.NetworkState?.FinishTime ?? -1 }).ToArray()
        };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        if (resultPath.Length > 0) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath))!); System.IO.File.WriteAllText(resultPath, json); }
        GD.Print("SMOKE_RESULT " + JsonSerializer.Serialize(report));
    }
    public override void _ExitTree() { transport?.Dispose(); }
}
