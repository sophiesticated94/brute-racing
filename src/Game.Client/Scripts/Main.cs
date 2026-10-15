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
    }
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
            foreach (var file in new[] { "cars/rook.json", "weapons/weapons.json", "tracks/cinder.json" })
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

    private void BuildWorld()
    {
        var environment = new WorldEnvironment { Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new("a5b9ba"),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new("b9d3d6"), AmbientLightEnergy = .65f,
            TonemapMode = Godot.Environment.ToneMapper.Linear
        } }; AddChild(environment);
        var sun = new DirectionalLight3D { RotationDegrees = new(-55, -32, 0), LightColor = new("fff3e3"), LightEnergy = .8f, ShadowEnabled = true, DirectionalShadowMaxDistance = 135 };
        AddChild(sun);
        track = new RaceTrack(); AddChild(track); track.Build(catalog.Track);
        raceRoot = new Node3D { Name = "Race" }; AddChild(raceRoot);
        effects = new Node3D { Name = "Effects" }; AddChild(effects);
        camera = new Camera3D { Current = true, Fov = 47, Far = 400, Position = new(55, 66, 71) }; AddChild(camera); camera.LookAt(new(0, 0, 3));
    }

    private PlayerLoadoutDto SelectedLoadout() => new("rook", ui.WeaponId, 1, new());
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
    private EnetTransport CreateTransport()
    {
        var net = new EnetTransport();
        net.Connected += id =>
        {
            if (!isHost && id == 1)
            {
                localId = net.LocalId;
                SendControl(1, new("hello", ContentHash: contentHash, Name: SelectedName(), Color: (int)ui.Paint.Selected, Loadout: SelectedLoadout()));
            }
            else if (isHost && phase != RacePhase.Lobby) net.Disconnect(id);
        };
        net.Disconnected += id =>
        {
            if (!isHost && id == 1)
            {
                Leave("Gospodarz zakończył połączenie.");
                if (arguments.ContainsKey("exit-on-disconnect")) GetTree().Quit();
                return;
            }
            acceptedHello.Remove(id);
            if (isHost && players.TryGetValue(id, out var p))
            {
                if (phase == RacePhase.Lobby) players.Remove(id);
                else
                {
                    players[id] = p with { Bot = true, Ready = true, Name = p.Name + " · AI" };
                    if (racers.TryGetValue(id, out var racer)) racer.Info = players[id];
                    SendControl(0, new("roster", Players: players.Values.ToArray()));
                    GD.Print($"PLAYER_AI_TAKEOVER id={id}");
                }
                if (phase == RacePhase.Lobby) PublishLobby();
            }
        };
        net.Received += Receive; return net;
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
    private void Refill(Racer r)
    {
        r.Ammo = catalog.Weapons[r.Info.Loadout.WeaponId].AmmoCapacity;
        r.Mines = catalog.Weapons["mine"].AmmoCapacity; r.Energy = 100; r.Heat = 0;
        r.Overheated = false; r.PrimaryCooldown = r.MineCooldown = 0;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (catalog is null) return;
        now += delta; transport?.Poll(); FlushDelayed();
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
                    pending.Add(new(command, local.Car.Grounded, track.SurfaceGrip(local.Car.GlobalPosition), track.Height(a)));
                    if (pending.Count > 180) pending.RemoveAt(0);
                }
                local.Car.Driving = phase == RacePhase.Racing && local.Car.Health > 0;
                local.Car.SurfaceGrip = track.SurfaceGrip(local.Car.GlobalPosition);
                local.Car.TurretYaw = Mathf.RotateToward(local.Car.TurretYaw, command.AimYaw, (float)delta * 4.5f);
            }
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

    private PlayerInputCommand ReadInput(Racer r, uint sequence)
    {
        float steer = (KeyDown(Key.D, Key.Right) ? 1 : 0) - (KeyDown(Key.A, Key.Left) ? 1 : 0);
        float throttle = (KeyDown(Key.W, Key.Up) ? 1 : 0) - (KeyDown(Key.S, Key.Down) ? 1 : 0);
        float brake = 0;
        bool fire = Input.IsMouseButtonPressed(MouseButton.Left), mine = Input.IsMouseButtonPressed(MouseButton.Right), handbrake = Input.IsPhysicalKeyPressed(Key.Space);
        var pads = Input.GetConnectedJoypads();
        if (pads.Count > 0)
        {
            int pad = pads[0]; float stick = Input.GetJoyAxis(pad, JoyAxis.LeftX);
            if (Mathf.Abs(stick) > .15f) steer = stick;
            float gas = Mathf.Max(0, Input.GetJoyAxis(pad, JoyAxis.TriggerRight));
            float reverse = Mathf.Max(0, Input.GetJoyAxis(pad, JoyAxis.TriggerLeft));
            if (gas > .1f || reverse > .1f) throttle = gas - reverse;
            var aim = new Vector2(Input.GetJoyAxis(pad, JoyAxis.RightX), Input.GetJoyAxis(pad, JoyAxis.RightY));
            if (aim.Length() > .25f) desiredAim = Mathf.Atan2(-aim.X, -aim.Y);
            fire |= Input.IsJoyButtonPressed(pad, JoyButton.RightShoulder); mine |= Input.IsJoyButtonPressed(pad, JoyButton.LeftShoulder); handbrake |= Input.IsJoyButtonPressed(pad, JoyButton.A);
        }
        if (!headless && (pads.Count == 0 || Input.GetLastMouseVelocity().Length() > 1))
        {
            var mouse = GetViewport().GetMousePosition(); var origin = camera.ProjectRayOrigin(mouse); var direction = camera.ProjectRayNormal(mouse);
            var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(origin, origin + direction * 500, 1));
            Vector3 target;
            if (hit.Count > 0) target = (Vector3)hit["position"];
            else target = origin + direction * ((r.Car.GlobalPosition.Y - origin.Y) / Mathf.Min(-.001f, direction.Y));
            var aim = target - r.Car.GlobalPosition; desiredAim = Mathf.Atan2(-aim.X, -aim.Z);
        }
        return new(sequence, tick, steer, throttle, brake, desiredAim, handbrake, fire, mine);
    }
    private static bool KeyDown(Key a, Key b) => Input.IsPhysicalKeyPressed(a) || Input.IsPhysicalKeyPressed(b);
    private PlayerInputCommand BotInput(Racer r, uint sequence)
    {
        var car = r.Car; int nearest = track.NearestPath(car.GlobalPosition);
        int nextGate = isHost ? r.Progress.NextCheckpoint : r.NetworkState?.NextCheckpoint ?? 1;
        var gate = track.GatePoint(nextGate);
        var target = track.Path[(nearest + 6) % track.Path.Length];
        // A missed gate must be crossed rather than orbiting forever with an invalid lap.
        int gateIndex = nextGate * track.Path.Length / catalog.Track.Checkpoints;
        int distanceToGate = (gateIndex - nearest + track.Path.Length) % track.Path.Length;
        if (distanceToGate < 9) target = gate + track.GateDirection(nextGate) * 2;
        bool missedGate = distanceToGate > track.Path.Length - 16 && distanceToGate < track.Path.Length - 3;
        if (missedGate) target = gate - track.GateDirection(nextGate) * 5;
        var offset = target - car.GlobalPosition; offset.Y = 0;
        var forward = -car.GlobalBasis.Z; forward.Y = 0; forward = forward.Normalized();
        var desired = offset.Normalized(); float angle = Mathf.Atan2(forward.Cross(desired).Y, forward.Dot(desired));
        float steer = Mathf.Clamp(-angle * 1.7f, -1, 1);
        float speed = new Vector2(car.LinearVelocity.X, car.LinearVelocity.Z).Length();
        float targetSpeed = Mathf.Lerp(22, 11, Mathf.Clamp(Mathf.Abs(angle) / .8f, 0, 1));
        if (missedGate) targetSpeed = 7;
        // Separation steers away from cars immediately ahead without changing checkpoint logic.
        foreach (var other in racers.Values)
        {
            if (other == r || other.Car.Health <= 0) continue;
            var relative = other.Car.GlobalPosition - car.GlobalPosition;
            if (relative.LengthSquared() < 30 && relative.Dot(forward) > 1)
                steer += Mathf.Sign(relative.Dot(car.GlobalBasis.X)) * -.25f;
        }
        var enemy = racers.Values.Where(o => o != r && o.Car.Health > 0 && o.Respawn <= 0)
            .OrderBy(o => o.Car.GlobalPosition.DistanceSquaredTo(car.GlobalPosition)).FirstOrDefault();
        Vector3 aim = enemy is not null ? enemy.Car.GlobalPosition - car.GlobalPosition : forward;
        float yaw = Mathf.Atan2(-aim.X, -aim.Z);
        bool fire = !noWeapons && enemy is not null && aim.Length() < 35 && r.Invulnerable <= 0 && (tick + (uint)Math.Abs(r.Info.Id % 10)) % 90 < 42;
        bool mine = !noWeapons && speed > 8 && tick % (uint)(240 + Math.Abs(r.Info.Id % 7) * 19) == 0;
        return new(sequence, tick, Mathf.Clamp(steer, -1, 1), speed < targetSpeed ? 1 : 0, speed > targetSpeed + 1 ? .35f : 0, yaw, false, fire, mine);
    }

    private void UpdateRacer(Racer r, float dt)
    {
        var car = r.Car;
        r.PrimaryCooldown = Mathf.Max(0, r.PrimaryCooldown - dt); r.MineCooldown = Mathf.Max(0, r.MineCooldown - dt);
        r.Invulnerable = Mathf.Max(0, r.Invulnerable - dt); car.Invulnerability = r.Invulnerable;
        car.CollisionMask = r.Invulnerable > 0 || r.Respawn > 0 ? 1u : 3u;
        car.CollisionLayer = r.Respawn > 0 ? 0u : r.Invulnerable > 0 ? 4u : 2u;
        if (r.Respawn > 0)
        {
            r.Respawn -= dt; car.Driving = false;
            if (r.Respawn <= 0)
            {
                var p = track.GatePoint(r.Progress.LastCheckpoint) + track.GateDirection(r.Progress.LastCheckpoint) * 2 + Vector3.Up;
                car.ResetAt(p, track.YawAt(r.Progress.LastCheckpoint)); car.Health = car.Stats.MaxHealth; r.Invulnerable = 2; Refill(r);
                Emit("respawn", r.Info.Id, 0, p, Vector3.Zero);
            }
            return;
        }
        car.Command = r.Gate.Read(now); car.Driving = phase == RacePhase.Racing && !r.Progress.Finished;
        car.SurfaceGrip = track.SurfaceGrip(car.GlobalPosition);
        car.TurretYaw = Mathf.RotateToward(car.TurretYaw, car.Command.AimYaw, dt * 4.5f);
        r.Energy = Mathf.Min(100, r.Energy + 18 * dt); r.Heat = Mathf.Max(0, r.Heat - 23 * dt);
        if (r.Heat < 30) r.Overheated = false;
        if (!car.Driving) { r.PreviousPosition = car.GlobalPosition; return; }
        CheckGateCrossing(r);
        if (car.GlobalPosition.Y < -5 || car.GlobalPosition.Length() > 150) { Destroy(r); return; }
        if (car.LinearVelocity.Length() < 1.4f && (r.Info.Bot || autoPlay)) r.Unstuck += dt; else r.Unstuck = 0;
        if (r.Unstuck > 4)
        {
            r.Unstuck = 0; car.ResetAt(track.GatePoint(r.Progress.LastCheckpoint) + track.GateDirection(r.Progress.LastCheckpoint) * 2 + Vector3.Up, track.YawAt(r.Progress.LastCheckpoint));
        }
        if (noWeapons) return;
        if (car.Command.Fire && r.PrimaryCooldown <= 0 && !r.Overheated) Fire(r);
        if (car.Command.Mine && r.MineCooldown <= 0 && r.Mines > 0)
        {
            var def = catalog.Weapons["mine"]; r.Mines--; r.MineCooldown = 1 / def.FireRate;
            var position = car.GlobalPosition + car.GlobalBasis.Z * 2.5f;
            var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(position + Vector3.Up * 2, position - Vector3.Up * 5, 1));
            position.Y = hit.Count > 0 ? ((Vector3)hit["position"]).Y + .15f : position.Y - .5f;
            Emit("mine", r.Info.Id, ++projectileSequence, position, Vector3.Zero);
        }
    }
    private void Fire(Racer r)
    {
        var def = catalog.Weapons[r.Info.Loadout.WeaponId];
        if (def.AmmoCapacity > 0 && r.Ammo <= 0 || r.Energy < def.EnergyConsumption) return;
        if (def.AmmoCapacity > 0) r.Ammo--;
        r.Energy -= def.EnergyConsumption; r.Heat = Mathf.Min(100, r.Heat + def.HeatGeneration);
        if (r.Heat >= 95) r.Overheated = true;
        r.PrimaryCooldown = 1 / def.FireRate; shots++;
        var direction = new Vector3(-Mathf.Sin(r.Car.TurretYaw), 0, -Mathf.Cos(r.Car.TurretYaw));
        var origin = r.Car.GlobalPosition + Vector3.Up * .92f + direction * 1.5f;
        if (def.Id == "rocket") { Emit("rocket", r.Info.Id, ++projectileSequence, origin, direction * def.ProjectileSpeed); return; }
        var end = origin + direction * def.Range;
        var query = PhysicsRayQueryParameters3D.Create(origin, end, 3); query.Exclude = new Godot.Collections.Array<Rid> { r.Car.GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0)
        {
            end = (Vector3)hit["position"];
            if (hit["collider"].AsGodotObject() is ArcadeCar victim && racers.TryGetValue(victim.PlayerId, out var target))
                Damage(target, def.Damage, direction * def.KnockbackForce, r.Info.Id);
        }
        Emit(def.Id, r.Info.Id, 0, origin, end);
    }
    private void UpdateOrdnance(float dt)
    {
        foreach (var projectile in ordnance.ToArray())
        {
            if (!GodotObject.IsInstanceValid(projectile.Node)) { ordnance.Remove(projectile); continue; }
            projectile.Age += dt;
            if (!headless && !projectile.Mine && (int)(projectile.Age * 12) > projectile.SmokeFrame)
            {
                projectile.SmokeFrame = (int)(projectile.Age * 12);
                var puff = Art.Box(effects, new(.25f, .25f, .25f), projectile.Node.GlobalPosition, new("a9ad9d"));
                var fade = puff.CreateTween(); fade.SetParallel();
                fade.TweenProperty(puff, "position", puff.Position + Vector3.Up, .55);
                fade.TweenProperty(puff, "scale", Vector3.Zero, .55); fade.Chain().TweenCallback(Callable.From(puff.QueueFree));
            }
            if (!isHost)
            {
                if (!projectile.Mine) projectile.Node.Position += projectile.Velocity * dt;
                if (projectile.Age > 30) RemoveOrdnance(projectile);
                continue;
            }
            bool detonate = false;
            var position = projectile.Node.GlobalPosition;
            if (projectile.Mine)
            {
                if (projectile.Age >= .5f)
                    detonate = racers.Values.Any(r => r.Car.Health > 0 && r.Respawn <= 0 && (r.Info.Id != projectile.Owner || projectile.Age > 1.5f) && r.Car.GlobalPosition.DistanceTo(position) < catalog.Weapons["mine"].Range);
                if (projectile.Age > 25) { Emit("expire", projectile.Owner, projectile.Id, position, Vector3.Zero); continue; }
            }
            else
            {
                var next = position + projectile.Velocity * dt;
                var query = PhysicsRayQueryParameters3D.Create(position, next, 3);
                if (racers.TryGetValue(projectile.Owner, out var owner)) query.Exclude = new Godot.Collections.Array<Rid> { owner.Car.GetRid() };
                var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
                if (hit.Count > 0) { position = (Vector3)hit["position"]; detonate = true; }
                else projectile.Node.GlobalPosition = next;
                if (projectile.Age > 2) detonate = true;
            }
            if (detonate)
            {
                var def = catalog.Weapons[projectile.Mine ? "mine" : "rocket"];
                foreach (var r in racers.Values)
                {
                    var offset = r.Car.GlobalPosition - position; float distance = offset.Length();
                    if (distance > def.SplashRadius || r.Respawn > 0) continue;
                    float falloff = 1 - distance / def.SplashRadius;
                    Damage(r, def.Damage * falloff, (offset.Normalized() + Vector3.Up * .55f).Normalized() * def.KnockbackForce * falloff, projectile.Owner);
                }
                Emit("explosion", projectile.Owner, projectile.Id, position, Vector3.Zero, def.SplashRadius);
            }
        }
    }
    private void RemoveOrdnance(Ordnance p) { ordnance.Remove(p); p.Node.QueueFree(); }
    private void Damage(Racer r, float amount, Vector3 impulse, int attacker)
    {
        if (r.Respawn > 0 || r.Invulnerable > 0 || r.Progress.Finished || phase != RacePhase.Racing) return;
        r.Car.Health = Mathf.Max(0, r.Car.Health - amount * (1 - r.Car.Stats.Armor));
        r.Car.ApplyCentralImpulse(impulse * (1 - r.Car.Stats.KnockbackResistance));
        Emit("hit", attacker, r.Info.Id, r.Car.GlobalPosition + Vector3.Up * .5f, Vector3.Zero, amount);
        if (r.Car.Health <= 0) Destroy(r);
    }
    private void Destroy(Racer r)
    {
        if (r.Respawn > 0) return;
        destructions++; r.Car.Health = 0; r.Respawn = 2.5f; r.Car.Driving = false;
        Emit("destroy", r.Info.Id, 0, r.Car.GlobalPosition, Vector3.Zero);
    }
    private void CheckGateCrossing(Racer r)
    {
        var car = r.Car; int gate = r.Progress.NextCheckpoint;
        var position = car.GlobalPosition; var previous = r.PreviousPosition; r.PreviousPosition = position;
        var center = track.GatePoint(gate); var forward = track.GateDirection(gate);
        float before = (previous - center).Dot(forward), after = (position - center).Dot(forward);
        // The plane test counts a real forward crossing, rather than an early contact
        // by the front bumper. Teleports never count as traversing a checkpoint.
        if (!CheckpointGeometry.CrossesForward(Art.Numerics(previous), Art.Numerics(position), Art.Numerics(center), Art.Numerics(forward), catalog.Track.Width / 2 + .5f, 5)) return;
        if (!r.Progress.Pass(gate, (after - before) * 60, raceElapsed)) return;
        checkpointPasses++;
        Emit("checkpoint", r.Info.Id, gate, car.GlobalPosition, Vector3.Zero, r.Progress.CompletedLaps);
        if (r.Progress.Finished) clock.NotifyFinish();
    }

    private void Emit(string kind, int actor, int target, Vector3 position, Vector3 end, float value = 0)
    {
        var e = new RaceEvent(raceId, ++eventSequence, kind, actor, target, Art.Numerics(position), Art.Numerics(end), value);
        ApplyEvent(e); Send(0, PacketCodec.EncodeJson(PacketKind.Event, e), true, 0);
    }
    private void ApplyEvent(RaceEvent e)
    {
        if (e.RaceId != raceId || !seenEvents.Add(e.EventId)) return;
        eventCounts[e.Kind] = eventCounts.GetValueOrDefault(e.Kind) + 1;
        if (seenEvents.Count > 8192) seenEvents.Remove(seenEvents.Min());
        var p = Art.GodotVector(e.Position); var end = Art.GodotVector(e.End);
        if (e.Kind is "rocket" or "mine")
        {
            var node = new Node3D { Position = p }; effects.AddChild(node);
            bool mine = e.Kind == "mine";
            if (mine)
            {
                Art.Cylinder(node, .45f, .5f, .2f, Vector3.Zero, new("273f45"));
                Art.Cylinder(node, .14f, .14f, .23f, Vector3.Zero, new("ed7457"));
            }
            else
            {
                Art.Box(node, new(.26f, .26f, .85f), Vector3.Zero, new("e3e4cf"));
                Art.Box(node, new(.2f, .2f, .25f), new(0, 0, .5f), new("ffbe45"), true);
                node.LookAt(p + end.Normalized()); audio.PlayAt("rocket", p);
            }
            ordnance.Add(new(e.Target, e.Actor, mine, end, node));
        }
        else if (e.Kind is "explosion" or "expire")
        {
            var projectile = ordnance.FirstOrDefault(x => x.Id == e.Target);
            if (projectile is not null) RemoveOrdnance(projectile);
            if (e.Kind == "explosion") { Art.Burst(effects, p, new("ffad49"), e.Value * .45f); audio.PlayAt("explosion", p); }
        }
        else if (e.Kind is "machine_gun" or "laser")
        {
            Art.Beam(effects, p, end, e.Kind == "laser" ? new("60edf1") : new("ffe1a0"), e.Kind == "laser" ? .13f : .055f);
            audio.PlayAt(e.Kind, p);
        }
        else if (e.Kind == "destroy") { Art.Burst(effects, p, new("ff8746"), 3); audio.PlayAt("explosion", p); }
        else if (e.Kind == "hit") Art.Burst(effects, p, new("ffdda3"), .35f);
        else if (e.Kind == "respawn") Art.Burst(effects, p, new("62dce0"), 1.2f);
        else if (e.Kind == "checkpoint" && e.Actor == localId)
        {
            audio.PlayAt("checkpoint", p);
            if (e.Target == 0) ui.Notice(e.Value >= catalog.Track.Laps ? "META!" : $"OKRĄŻENIE {(int)e.Value + 1} / {catalog.Track.Laps}", 2);
        }
    }

    private void Receive(int sender, byte[] packet)
    {
        try
        {
            if (!isHost && sender != 1) return;
            if (!isHost) lastServerPacket = now;
            switch ((PacketKind)packet[0])
            {
                case PacketKind.Control: HandleControl(sender, PacketCodec.DecodeJson<SessionMessage>(packet, PacketKind.Control)); break;
                case PacketKind.Input:
                    if (!isHost || phase is RacePhase.Lobby or RacePhase.Podium || !racers.TryGetValue(sender, out var r) || r.Info.Bot) return;
                    var inputs = PacketCodec.DecodeInputs(packet);
                    // Redundancy recovers the latest state, never executes multiple physics steps or shots per packet.
                    foreach (var input in inputs) r.Gate.Accept(input, now);
                    break;
                case PacketKind.Snapshot:
                    if (!isHost && phase != RacePhase.Lobby) ApplySnapshot(PacketCodec.DecodeSnapshot(packet));
                    break;
                case PacketKind.Event:
                    if (!isHost && phase != RacePhase.Lobby) ApplyEvent(PacketCodec.DecodeJson<RaceEvent>(packet, PacketKind.Event));
                    break;
            }
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException or JsonException or ArgumentException or NullReferenceException)
        {
            GD.Print($"REJECT_PACKET peer={sender} reason={e.GetType().Name}");
            if (isHost) transport?.Disconnect(sender);
        }
    }
    private void HandleControl(int sender, SessionMessage m)
    {
        if (isHost)
        {
            if (m.Kind == "hello")
            {
                if (phase != RacePhase.Lobby || acceptedHello.ContainsKey(sender)) { transport?.Disconnect(sender); return; }
                if (m.Protocol != 1 || m.ContentHash != contentHash || !new BuildValidator(catalog).IsValid(m.Loadout) || m.Color is < 0 or > 7 || m.Name is null || m.Name.Length > 20)
                {
                    SendControl(sender, new("error", Message: "Niezgodna wersja gry, dane lub build.")); transport?.Disconnect(sender); return;
                }
                // A joining human takes a bot's slot if the lobby is full.
                if (players.Count >= 8)
                {
                    var bot = players.Values.FirstOrDefault(p => p.Bot);
                    if (bot is null) { transport?.Disconnect(sender); return; }
                    players.Remove(bot.Id);
                }
                acceptedHello[sender] = now;
                players[sender] = new(sender, CleanName(m.Name), m.Color, autoStart, false, m.Loadout!); PublishLobby();
                GD.Print($"PLAYER_JOINED id={sender} count={players.Count}");
            }
            else if (phase == RacePhase.Lobby && players.TryGetValue(sender, out var player))
            {
                if (m.Kind == "ready") { players[sender] = player with { Ready = !player.Ready }; PublishLobby(); }
                if (m.Kind == "loadout" && m.Color is >= 0 and < 8 && m.Name is not null && m.Name.Length <= 20 && new BuildValidator(catalog).IsValid(m.Loadout))
                {
                    players[sender] = player with { Name = CleanName(m.Name), Color = m.Color, Loadout = m.Loadout!, Ready = false }; PublishLobby();
                }
            }
        }
        else if (sender == 1)
        {
            if (m.Kind == "error") { Leave(m.Message); return; }
            if (m.Kind is "lobby" or "start" or "roster")
            {
                if (m.Players is null || m.Players.Length > 8 || m.Players.Any(p => !new BuildValidator(catalog).IsValid(p.Loadout) || p.Color is < 0 or > 7)) throw new InvalidDataException("Roster.");
                players.Clear(); foreach (var p in m.Players) players.Add(p.Id, p);
                if (m.Kind == "start") { raceId = m.RaceId; InitializeRace(); }
                else if (m.Kind == "lobby") { phase = RacePhase.Lobby; ui.ShowLobby(players.Values.ToArray(), false, localId); }
                else foreach (var p in players.Values) if (racers.TryGetValue(p.Id, out var r)) r.Info = p;
            }
            if (m.Kind == "results" && m.RaceId == raceId && m.Ranking is not null) { finalRanking = m.Ranking; phase = RacePhase.Podium; ShowPodium(); }
        }
    }
    private static string CleanName(string name)
    {
        var clean = new string(name.Where(c => !char.IsControl(c)).Take(20).ToArray()).Trim();
        return clean.Length == 0 ? "Kierowca" : clean;
    }
    private void SendControl(int target, SessionMessage message) => Send(target, PacketCodec.EncodeJson(PacketKind.Control, message), true, 0);
    private void Send(int target, byte[] packet, bool reliable, int channel)
    {
        if (transport is null) return;
        if (!reliable && artificialLoss > 0 && random.NextDouble() < artificialLoss) return;
        if (artificialLatency > 0) delayed.Add((now + artificialLatency, target, packet, reliable, channel));
        else transport.Send(target, packet, reliable, channel);
    }
    private void FlushDelayed()
    {
        for (int i = 0; i < delayed.Count;)
        {
            var p = delayed[i];
            if (p.At > now) { i++; continue; }
            transport?.Send(p.Target, p.Packet, p.Reliable, p.Channel); delayed.RemoveAt(i);
        }
    }
    private WorldSnapshot CreateSnapshot() => new(raceId, tick, phase, raceElapsed, raceRemaining, racers.Values.Select(r =>
        new VehicleState(r.Info.Id, Art.Numerics(r.Car.GlobalPosition), Art.Numerics(r.Car.Quaternion), Art.Numerics(r.Car.LinearVelocity), Art.Numerics(r.Car.AngularVelocity),
            r.Car.TurretYaw, r.Car.Health, r.Energy, r.Heat, r.Ammo, r.Mines, r.Progress.CompletedLaps, r.Progress.NextCheckpoint, r.Progress.PassedGates,
            r.Respawn, r.Invulnerable, r.Car.LastSimulatedSequence, r.Progress.FinishTime ?? -1)).ToArray());
    private void ApplySnapshot(WorldSnapshot s)
    {
        if (s.RaceId != raceId || s.Tick <= lastSnapshotTick) return;
        lastSnapshotTick = s.Tick; receivedSnapshots++; phase = s.Phase; raceElapsed = s.Elapsed; raceRemaining = s.Remaining;
        foreach (var state in s.Vehicles)
        {
            if (!racers.TryGetValue(state.Id, out var r)) continue;
            r.NetworkState = state; r.Car.Health = state.Health; r.Energy = state.Energy; r.Heat = state.Heat;
            r.Ammo = state.Ammo; r.Mines = state.Mines; r.Respawn = state.Respawn; r.Invulnerable = state.Invulnerability;
            r.Car.Invulnerability = state.Invulnerability;
            r.Car.CollisionLayer = state.Id != localId || state.Respawn > 0 ? 0u : state.Invulnerability > 0 ? 4u : 2u;
            r.Car.CollisionMask = state.Id == localId ? 1u : 0u;
            if (state.Id == localId)
            {
                pending.RemoveAll(p => p.Command.Sequence <= state.AcknowledgedSequence);
                var rotation = Art.GodotQuaternion(state.Rotation);
                var predicted = new PredictionState(state.Position, state.Velocity, new Basis(rotation).GetEuler().Y, state.AngularVelocity.Y);
                if (s.Phase == RacePhase.Racing && state.Health > 0)
                    foreach (var command in pending) predicted = MovementPrediction.Step(predicted, command, r.Car.Stats);
                else pending.Clear();
                var yawDelta = Mathf.Wrap(predicted.Yaw - new Basis(rotation).GetEuler().Y, -Mathf.Pi, Mathf.Pi);
                rotation = new Quaternion(Vector3.Up, yawDelta) * rotation;
                float correction = r.Car.GlobalPosition.DistanceTo(Art.GodotVector(predicted.Position));
                correctionTotal += correction; correctionCount++; correctionMax = Mathf.Max(correctionMax, correction);
                if (correction > 4) hardCorrections++;
                r.Car.Reconcile(Art.GodotVector(predicted.Position), rotation, Art.GodotVector(predicted.Velocity), Art.GodotVector(state.AngularVelocity));
            }
            else
            {
                r.History.Add((s.Tick / 60.0, state)); if (r.History.Count > 12) r.History.RemoveAt(0);
            }
        }
        if (phase == RacePhase.Podium && finalRanking.Length > 0) ShowPodium();
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
    private void ShowPodium()
    {
        var lines = new List<string>();
        for (int i = 0; i < finalRanking.Length; i++)
        {
            if (!racers.TryGetValue(finalRanking[i], out var r)) continue;
            double finish = isHost ? r.Progress.FinishTime ?? -1 : r.NetworkState?.FinishTime ?? -1;
            lines.Add($"{i + 1:00}   {r.Info.Name}\n       {(finish >= 0 ? GameUi.TimeText(finish) : "NIE UKOŃCZYŁ")}");
            r.Car.Driving = false;
        }
        ui.ShowResults(string.Join("\n", lines), isHost);
    }

    public override void _Process(double delta)
    {
        if (camera is null) return;
        frameCount++; frameTotal += delta; frameWorst = Math.Max(frameWorst, delta);
        ulong frameUsec = Time.GetTicksUsec();
        if (lastFrameUsec > 0)
        {
            double wallDt = (frameUsec - lastFrameUsec) / 1000000.0;
            wallFrameTotal += wallDt; wallWorstFrame = Math.Max(wallWorstFrame, wallDt);
        }
        lastFrameUsec = frameUsec;
        if (Input.IsPhysicalKeyPressed(Key.F12) && !screenshotSaved) SaveScreenshot();
        if (!isHost && phase != RacePhase.Lobby)
        {
            foreach (var r in racers.Values.Where(r => r.Info.Id != localId && r.History.Count > 0))
            {
                double renderTime = lastSnapshotTick / 60.0 + Math.Clamp(now - lastServerPacket, 0, .06) - .1;
                var a = r.History[0]; var b = r.History[^1];
                for (int i = 0; i < r.History.Count - 1; i++)
                    if (r.History[i].Time <= renderTime && r.History[i + 1].Time >= renderTime) { a = r.History[i]; b = r.History[i + 1]; break; }
                float t = b.Time > a.Time ? Mathf.Clamp((float)((renderTime - a.Time) / (b.Time - a.Time)), 0, 1) : 1;
                var position = Art.GodotVector(a.State.Position).Lerp(Art.GodotVector(b.State.Position), t);
                var rotation = Art.GodotQuaternion(a.State.Rotation).Slerp(Art.GodotQuaternion(b.State.Rotation), t);
                r.Car.GlobalTransform = new(new Basis(rotation), position);
                r.Car.TurretYaw = Mathf.LerpAngle(a.State.TurretYaw, b.State.TurretYaw, t);
            }
        }
        if (inSession && phase != RacePhase.Lobby && racers.Count > 0)
        {
            var local = racers.GetValueOrDefault(localId) ?? racers.Values.First();
            var velocity = local.Car.LinearVelocity;
            var target = local.Car.GlobalPosition + velocity * .28f;
            var desired = target + new Vector3(0, 28, 20);
            camera.Position = camera.Position.Lerp(desired, 1 - Mathf.Exp(-(float)delta * 5));
            camera.RotationDegrees = new(-54.5f, 0, 0);
            var rank = Ranking(); int place = Array.IndexOf(rank, local.Info.Id) + 1;
            string board = string.Join("\n", rank.Select((id, i) => $"{i + 1:00}   {racers[id].Info.Name}{(id == localId ? "  ◂" : "")}"));
            int lap = isHost ? local.Progress.CompletedLaps : local.NetworkState?.Lap ?? 0;
            ui.UpdateRace(place, rank.Length, lap, catalog.Track.Laps, raceElapsed, raceRemaining, phase, local.Car.Health, local.Car.Stats.MaxHealth,
                local.Energy, local.Heat, local.Info.Loadout.WeaponId == "laser" ? -1 : local.Ammo, local.Mines, velocity.Length(), board, local.Respawn, local.Invulnerable);
            audio.UpdateEngine(velocity.Length(), phase == RacePhase.Racing && local.Car.Health > 0);
            ui.UpdateMap(racers.Values.Select(r => (r.Car.GlobalPosition, Art.Colors[r.Info.Color], r.Info.Id == localId)).ToArray());
        }
        else
        {
            float angle = (float)now * .035f;
            camera.Position = new(Mathf.Sin(angle + .65f) * 80, 66, Mathf.Cos(angle + .65f) * 80);
            camera.LookAt(new(0, 0, 3)); audio?.UpdateEngine(0, false);
        }
        if (arguments.ContainsKey("screenshot") && !headless && now > 5 && !screenshotSaved) SaveScreenshot();
    }
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.PhysicalKeycode == Key.Escape) ui.TogglePause();
        if (key.PhysicalKeycode == Key.M) { audio.Muted = !audio.Muted; ui.Notice(audio.Muted ? "Dźwięk wyłączony" : "Dźwięk włączony", 2); }
    }
    private void SaveScreenshot()
    {
        screenshotSaved = true;
        RenderingServer.FramePostDraw += Capture;
        void Capture()
        {
            RenderingServer.FramePostDraw -= Capture;
            string output = arguments.GetValueOrDefault("screenshot", "user://screenshot.png");
            GetViewport().GetTexture().GetImage().SavePng(output); GD.Print("SCREENSHOT " + output);
        }
    }
    private async void MapPort(int port)
    {
        string status;
        try
        {
            status = await Task.Run(() =>
            {
                using var upnp = new Upnp(); var found = upnp.Discover(1500, 2);
                if (found != (int)Upnp.UpnpResult.Success) return "UPnP niedostępne. Ustaw przekierowanie UDP ręcznie.";
                var mapped = upnp.AddPortMapping(port, port, "Mini Car Mayhem", "UDP", 3600);
                return mapped == (int)Upnp.UpnpResult.Success ? $"UPnP: {upnp.QueryExternalAddress()}:{port}" : "UPnP nie otworzył portu. Ustaw przekierowanie UDP ręcznie.";
            });
        }
        catch { status = "UPnP niedostępne. Połączenia LAN nadal działają."; }
        if (IsInsideTree() && inSession && isHost) ui.Notice(status, 10);
    }
    private void ClearRace()
    {
        foreach (var child in raceRoot.GetChildren()) { raceRoot.RemoveChild(child); child.QueueFree(); }
        foreach (var child in effects.GetChildren()) { effects.RemoveChild(child); child.QueueFree(); }
        racers.Clear(); ordnance.Clear(); pending.Clear(); recentInputs.Clear();
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
