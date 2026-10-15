using Game.Core;
using Godot;
namespace Game.Client;
public partial class Main
{
    private readonly SessionLifecycle lifecycle=new();
    private readonly GameplayInputGate gameplayInput=new();
    private bool shuttingDown;
    private void SyncSessionState()
    {
        if(!inSession)return;
        SessionState state=phase switch{
            RacePhase.Countdown=>SessionState.Countdown,RacePhase.Racing=>SessionState.Racing,
            RacePhase.Podium=>campaignView?.Complete==true?SessionState.CampaignComplete:SessionState.Podium,
            _=>campaignView?.Complete==true?SessionState.CampaignComplete:campaignView?.CompletedRounds>0?SessionState.Intermission:SessionState.Lobby};
        if(lifecycle.State is SessionState.Menu or SessionState.Disconnected)lifecycle.MoveTo(SessionState.Lobby);
        lifecycle.MoveTo(state);
    }
    private void ScheduleSession(double delay,Action action,bool physics=false)
    {
        long generation=lifecycle.Generation;uint round=raceId;
        GetTree().CreateTimer(delay,false,physics).Timeout+=()=>{if(!shuttingDown&&lifecycle.IsCurrent(generation)&&raceId==round)action();};
    }
    private PlayerInputCommand LocalCommand(Racer racer,uint sequence)
    {
        bool fire=Input.IsMouseButtonPressed(MouseButton.Left),mine=Input.IsMouseButtonPressed(MouseButton.Right);
        foreach(int pad in Input.GetConnectedJoypads())
        {fire|=Input.IsJoyButtonPressed(pad,JoyButton.RightShoulder);mine|=Input.IsJoyButtonPressed(pad,JoyButton.LeftShoulder);}
        bool focused=headless||GetWindow().HasFocus();
        gameplayInput.Observe(lifecycle.State,ui.BlockingInput||garage.IsBlockingInput,focused,fire,mine);
        var command=gameplayInput.CanRead?(autoPlay?BotInput(racer,sequence):ReadInput(racer,sequence)):
            new PlayerInputCommand(sequence,tick,0,0,1,racer.Car.TurretYaw,false,false,false);
        return gameplayInput.Apply(command,racer.Car.TurretYaw);
    }
}
