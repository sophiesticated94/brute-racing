using Game.Core;
using Godot;
namespace Game.Client;

public partial class Main
{
    private int? inputSmokePad;
    private bool? inputSmokeFocus;
    private bool inputSmokeDone;
    private double inputSmokeDeadline;
    private int staleSmokeCallbacks;
    private Godot.Collections.Array<int> LocalPads()=>inputSmokePad is{} id?new(){id}:Input.GetConnectedJoypads();
    private void RunInputSmoke()
    {
        if(inputSmokeDone)
        {
            if(now<inputSmokeDeadline)return;
            if(staleSmokeCallbacks!=0)throw new InvalidOperationException("Stary timer zmienił nową sesję.");
            GD.Print("PASS: INPUT_SESSION_SMOKE mouse/pad, garage/dialog/pause/focus, release latch, 12 sessions and stale timers.");GetTree().Quit();return;
        }
        inputSmokeDone=true;Input.UseAccumulatedInput=false;inputSmokeFocus=true;autoPlay=false;phase=RacePhase.Racing;SyncSessionState();
        var racer=racers[localId];racer.Car.TurretYaw=1.1f;
        static void Check(bool valid,string detail){if(!valid)throw new InvalidOperationException("INPUT_SMOKE: "+detail);}
        void Buttons(bool down,bool pad)
        {
            if(pad)
            {Input.ParseInputEvent(new InputEventJoypadButton{Device=0,ButtonIndex=JoyButton.RightShoulder,Pressed=down});Input.ParseInputEvent(new InputEventJoypadButton{Device=0,ButtonIndex=JoyButton.LeftShoulder,Pressed=down});}
            else
            {Input.ParseInputEvent(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=down});Input.ParseInputEvent(new InputEventMouseButton{ButtonIndex=MouseButton.Right,Pressed=down});}
        }
        uint sequence=1;
        foreach(bool pad in new[]{false,true})
        {
            inputSmokePad=pad?0:null;gameplayInput.Reset();
            foreach(var key in new[]{Key.W,Key.D,Key.Space})Input.ParseInputEvent(new InputEventKey{PhysicalKeycode=key,Pressed=!pad});
            foreach(var axis in new[]{JoyAxis.LeftX,JoyAxis.TriggerRight,JoyAxis.RightX})Input.ParseInputEvent(new InputEventJoypadMotion{Device=0,Axis=axis,AxisValue=1});
            Buttons(false,pad);LocalCommand(racer,sequence++);Buttons(true,pad);
            Input.FlushBufferedEvents();var active=LocalCommand(racer,sequence++);
            Check(active.Fire&&active.Mine&&active.Throttle>0&&active.Steering>0,"native input source "+pad+$" fire={active.Fire} mine={active.Mine} throttle={active.Throttle} steering={active.Steering}");
            var dialog=garage.GetChildren().OfType<ConfirmationDialog>().Single();
            var panels=new (Action Open,Action Close,string Name)[]{
                (()=>ui.TogglePause(),()=>ui.TogglePause(),"pause"),
                (()=>OpenGarage(),()=>garage.Close(),"garage"),
                (()=>dialog.PopupCentered(new(400,200)),()=>dialog.Hide(),"dialog"),
                (()=>{inputSmokeFocus=false;gameplayInput.Block();},()=>inputSmokeFocus=true,"focus")};
            foreach(var panel in panels)
            {
                float previousAim=desiredAim;panel.Open();var blocked=LocalCommand(racer,sequence++);
                Check(blocked.Throttle==0&&blocked.Steering==0&&blocked.Brake==1&&!blocked.Handbrake&&!blocked.Fire&&!blocked.Mine&&blocked.AimYaw==racer.Car.TurretYaw&&desiredAim==previousAim,panel.Name+" blocked "+pad);
                panel.Close();var held=LocalCommand(racer,sequence++);Check(!held.Fire&&!held.Mine,panel.Name+" held "+pad);
                Buttons(false,pad);LocalCommand(racer,sequence++);Buttons(true,pad);
                var released=LocalCommand(racer,sequence++);Check(released.Fire&&released.Mine,panel.Name+" release "+pad);
            }
            // Open and close between physics ticks: the closing click must still be latched.
            Buttons(false,pad);LocalCommand(racer,sequence++);OpenGarage();Buttons(true,pad);garage.Close();
            var fast=LocalCommand(racer,sequence++);Check(!fast.Fire&&!fast.Mine,"between ticks "+pad);
            Buttons(false,pad);
        }
        foreach(var key in new[]{Key.W,Key.D,Key.Space})Input.ParseInputEvent(new InputEventKey{PhysicalKeycode=key,Pressed=false});
        inputSmokePad=null;inputSmokeFocus=null;
        for(int cycle=0;cycle<12;cycle++)
        {
            ScheduleSession(.05,()=>staleSmokeCallbacks++);
            seenEvents.Add(999);recentInputs.Enqueue(new(1,1,0,0,0,0,false,false,false));effects.AddChild(new Node3D());
            LeaveInternal();Check(racers.Count==0&&pending.Count==0&&seenEvents.Count==0&&recentInputs.Count==0&&ordnance.Count==0&&effects.GetChildCount()==0&&!garage.IsBlockingInput,"cleanup");
            StartSolo();
        }
        phase=RacePhase.Racing;SyncSessionState();gameMode=GameMode.Campaign;EnsureCampaign();
        Guid previousCampaign=campaign!.Id;
        for(int round=0;round<campaign.Length;round++)
        {Guid id=Guid.NewGuid();Check(CommitCampaign(id,state=>state.RecordRace(id,state.Players.Keys.ToArray(),state.Players.Keys.ToDictionary(k=>k,k=>true),new Dictionary<int,CombatScore>())),"campaign completion");}
        SynchronizeCampaign();phase=RacePhase.Podium;SyncSessionState();ReplayRequested();SyncSessionState();
        Check(campaign.Id!=previousCampaign&&campaign.CompletedRounds==0&&campaign.Players.Values.All(p=>p.Xp==0&&p.OwnedLevels.Values.All(l=>l==1)),"new campaign reset");
        inputSmokeDeadline=now+.2;
    }
}
