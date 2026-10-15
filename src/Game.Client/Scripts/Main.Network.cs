using System.Text.Json;
using Game.Core;
using Godot;

namespace Game.Client;

public partial class Main
{
    private EnetTransport CreateTransport()
    {
        var net = new EnetTransport();
        net.Connected += id =>
        {
            if (!isHost && id == 1)
            {
                localId = net.LocalId;
                SendControl(1, new("hello", ContentHash: contentHash, Name: SelectedName(), Color: (int)ui.Paint.Selected, Loadout: SelectedLoadout(),MatchId:quickOffer?.Id??Guid.Empty,TicketId:queueTicket?.Id??Guid.Empty));
            }
            else if (isHost && phase != RacePhase.Lobby) net.Disconnect(id);
        };
        net.Disconnected += id =>
        {
            if (!isHost && id == 1)
            {
                if(queueTicket is not null&&phase==RacePhase.Lobby){RetryQuick();return;}
                Leave("Gospodarz zakończył połączenie.");
                if (arguments.ContainsKey("exit-on-disconnect")) GetTree().Quit();
                return;
            }
            acceptedHello.Remove(id);quickPeers.Remove(id);
            if (isHost && players.TryGetValue(id, out var p))
            {
                if (phase == RacePhase.Lobby && campaign?.CompletedRounds is not >0) players.Remove(id);
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
            if (m.Kind == "ping" && players.ContainsKey(sender)) { SendControl(sender, new("pong", SentTime: m.SentTime)); return; }
            if (m.Kind == "hello")
            {
                if (phase != RacePhase.Lobby || campaign?.CompletedRounds>0 || acceptedHello.ContainsKey(sender)) { transport?.Disconnect(sender); return; }
                if (m.Protocol != 3 || m.ContentHash != contentHash || !new BuildValidator(catalog).IsValid(m.Loadout) || m.Color is < 0 or > 7 || m.Name is null || m.Name.Length > 20)
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
                if(!ValidateQuickHello(sender,m)){transport?.Disconnect(sender);return;}
                acceptedHello[sender] = now; autoStartAt = Math.Max(autoStartAt,now+.4);
                players[sender] = new(sender, CleanName(m.Name), m.Color, autoStart, false, ValidateBuild(m.Loadout,m.Color)!); PublishLobby();
                GD.Print($"PLAYER_JOINED id={sender} count={players.Count}");
            }
            else if (phase == RacePhase.Lobby && players.TryGetValue(sender, out var player))
            {
                if (m.Kind == "campaign_action")HandleCampaignAction(sender,m.Message,m.ItemId);
                if (m.Kind == "ready") { players[sender] = player with { Ready = !player.Ready }; PublishLobby(); }
                if (m.Kind == "loadout" && gameMode!=GameMode.Quick) HandleBuildRequest(sender,m);
            }
        }
        else if (sender == 1)
        {
            if (m.Kind == "pong") { diagnostics.ObserveRtt(now - m.SentTime); return; }
            if (m.Kind == "build_error") { ui.Notice(m.Message,3); if(garage.IsOpen)garage.Notify(m.Message); return; }
            if(m.Kind=="reward" && m.Reward is not null){AcceptReward(m.Reward);return;}
            if (m.Kind == "error") { Leave(m.Message); return; }
            if (m.Kind is "lobby" or "start" or "roster")
            {
                UpdateCampaignMessage(m);
                if (m.Players is null || m.Players.Length > 8 || m.Players.Any(p => !new BuildValidator(catalog).IsValid(p.Loadout) || p.Color is < 0 or > 7)) throw new InvalidDataException("Roster.");
                if (m.BalanceRules is not null) { if (!m.BalanceRules.IsValid) throw new InvalidDataException("Balance rules."); balanceRules = m.BalanceRules; }
                players.Clear(); foreach (var p in m.Players) players.Add(p.Id, p);
                if (m.Kind == "start") { raceId = m.RaceId; InitializeRace(); }
                else if (m.Kind == "lobby") { phase = RacePhase.Lobby; ui.ShowLobby(players.Values.ToArray(), false, localId); ui.ShowBalance(balanceRules,false); ProbeBuilds(); }
                else foreach (var p in players.Values) if (racers.TryGetValue(p.Id, out var r)) r.Info = p;
                RefreshGarage();
            }
            if (m.Kind == "results" && m.RaceId == raceId && m.Ranking is not null) { UpdateCampaignMessage(m); finalRanking = m.Ranking; phase = RacePhase.Podium; ShowPodium(); }
        }
    }
    private static string CleanName(string name)
    {
        var clean = new string(name.Where(c => !char.IsControl(c)).Take(20).ToArray()).Trim();
        return clean.Length == 0 ? "Kierowca" : clean;
    }
    private void SendControl(int target, SessionMessage message)
    {
        if(message.Kind is "lobby" or "start" or "results" or "roster")
            message=message with{Mode=gameMode,Campaign=campaign?.View(),SessionId=raceSessionId};
        Send(target,PacketCodec.EncodeJson(PacketKind.Control,message),true,0);
    }
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
            r.Respawn, r.Invulnerable, r.Car.LastSimulatedSequence, r.Progress.FinishTime ?? -1, r.SlowRemaining, r.SlowFactor)).ToArray());
    private void ApplySnapshot(WorldSnapshot s)
    {
        if (s.RaceId != raceId || s.Tick <= lastSnapshotTick) return;
        diagnostics.ObserveSnapshot(now);
        lastSnapshotTick = s.Tick; receivedSnapshots++; phase = s.Phase; raceElapsed = s.Elapsed; raceRemaining = s.Remaining;
        foreach (var state in s.Vehicles)
        {
            if (!racers.TryGetValue(state.Id, out var r)) continue;
            bool respawned = r.NetworkState is { Health: <= 0 } && state.Health > 0;
            bool teleported = r.Car.GlobalPosition.DistanceTo(Art.GodotVector(state.Position)) > MovementTuning.TeleportDistance;
            if (respawned || teleported) { r.History.Clear(); r.Car.HardResetPending = true; if (state.Id == localId) pending.Clear(); }
            r.NetworkState = state; r.Car.Health = state.Health; r.Energy = state.Energy; r.Heat = state.Heat;
            if (state.Id == localId)
            {
                if (!r.Overheated && state.Heat >= r.Build.Primary.OverheatThreshold) { r.Overheated = true; ui.Notice("LASER PRZEGRZANY", 2); }
                else if (r.Overheated && state.Heat < r.Build.Primary.OverheatResume) { r.Overheated = false; ui.Notice("LASER GOTOWY", 2); }
            }
            r.Ammo = state.Ammo; r.Mines = state.Mines; r.Respawn = state.Respawn; r.Invulnerable = state.Invulnerability;
            r.Car.Invulnerability = state.Invulnerability; r.Car.MovementScale = state.SlowRemaining > 0 ? state.SlowFactor : 1;
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
                if (correction >= MovementTuning.TeleportDistance) hardCorrections++;
                r.Car.Reconcile(Art.GodotVector(predicted.Position), rotation, Art.GodotVector(predicted.Velocity), Art.GodotVector(state.AngularVelocity));
            }
            else
            {
                r.History.Add((s.Tick / 60.0, state)); if (r.History.Count > 12) r.History.RemoveAt(0);
            }
        }
        if (phase == RacePhase.Podium && finalRanking.Length > 0) ShowPodium();
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
        if (IsInsideTree() && inSession && isHost) { ui.ConnectionStatus(status);ui.Notice(status, 10); }
    }
}
