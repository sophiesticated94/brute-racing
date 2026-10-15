using System.Text.Json;
using Game.Core;
using Godot;

namespace Game.Client;

public partial class Main
{
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

    private void ApplyEvent(RaceEvent e)
    {
        if (e.RaceId != raceId || !seenEvents.Add(e.EventId)) return;
        eventCounts[e.Kind] = eventCounts.GetValueOrDefault(e.Kind) + 1;
        if (seenEvents.Count > 8192) seenEvents.Remove(seenEvents.Min());
        var p = Art.GodotVector(e.Position); var end = Art.GodotVector(e.End);
        if (e.Kind is "explosion" or "destroy" && racers.TryGetValue(localId, out var localVictim) && localVictim.Car.GlobalPosition.DistanceTo(p) < 12) shakeTime = .25;
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
            ordnance.Add(new(e.Target, e.Actor, mine, end, node) { Stats = e.Weapon ?? catalog.Weapons[mine ? "mine" : "rocket"] });
        }
        else if (e.Kind == "emp")
        {
            empFields.Add(new(p, e.Value, e.Weapon?.SlowDuration ?? 3, e.Weapon?.SlowFactor ?? .7f));
            var field = Art.Cylinder(effects, e.Value, e.Value, .04f, p + Vector3.Up * .05f, new("4b7394"), 24);
            var t = field.CreateTween(); t.TweenInterval(e.Weapon?.SlowDuration ?? 3); t.TweenCallback(Callable.From(field.QueueFree));
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
        else if (e.Kind == "hit")
        {
            Art.Burst(effects, p, new("ffdda3"), .35f);
            if (e.Actor == localId && e.Target != localId)
            {
                ui.HitMarker();
                if (!headless)
                {
                    var number = new Label3D { Text = $"{e.Value:0}", Position = p + Vector3.Up, FontSize = 48, PixelSize = .012f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = new("ffdf9b") };
                    effects.AddChild(number); var t = number.CreateTween(); t.TweenProperty(number, "position", number.Position + Vector3.Up * 2, .65); t.TweenCallback(Callable.From(number.QueueFree));
                }
            }
        }
        else if (e.Kind is "respawn" or "teleport")
        {
            if(racers.TryGetValue(e.Actor,out var reset))
            {
                reset.History.Clear();reset.Car.VisualOffset=Vector3.Zero;reset.Car.HardResetPending=true;
                if(e.Actor==localId){pending.Clear();recentInputs.Clear();}
            }
            if(e.Kind=="respawn")Art.Burst(effects,p,new("62dce0"),1.2f);
        }
        else if (e.Kind == "checkpoint" && e.Actor == localId)
        {
            audio.PlayAt("checkpoint", p);
            if (e.Target == 0) ui.Notice(e.Value >= catalog.Track.Laps ? "META!" : $"OKRĄŻENIE {(int)e.Value + 1} / {catalog.Track.Laps}", 2);
        }
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
        if(campaignView is {Complete:true} finalCampaign)
        {
            lines.Clear();lines.Add("KAMPANIA UKOŃCZONA");
            for(int i=0;i<finalCampaign.Ranking.Length;i++){int id=finalCampaign.Ranking[i];var own=finalCampaign.Players.First(p=>p.Id==id);lines.Add($"{i+1:00}   {players.GetValueOrDefault(id)?.Name ?? "Kierowca"}  •  {own.ChampionshipPoints} pkt");}
            lines.Add($"Monety: {profile.Coins} • końcowy build czeka w garażu.");
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
            var aimDirection = new Vector3(-Mathf.Sin(local.Car.TurretYaw), 0, -Mathf.Cos(local.Car.TurretYaw));
            var target = local.Car.GlobalPosition + local.Car.VisualOffset + velocity.LimitLength(30) * .28f + aimDirection * 1.6f;
            var desired = target + new Vector3(0, 28, 20);
            camera.Position = camera.Position.Lerp(desired, 1 - Mathf.Exp(-(float)delta * 5));
            camera.RotationDegrees = new(-54.5f, 0, 0);
            if (shakeTime > 0) { shakeTime -= delta; camera.Position += new Vector3(Mathf.Sin((float)now * 85), Mathf.Cos((float)now * 71), 0) * (float)shakeTime * .55f; }
            UpdateAimLine(local);
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
        if (key.PhysicalKeycode == Key.Escape) { if(garage.IsOpen)garage.Close();else ui.TogglePause(); }
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
}
