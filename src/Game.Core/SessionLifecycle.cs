namespace Game.Core;

public enum SessionState { Menu,Lobby,Countdown,Racing,Podium,Intermission,CampaignComplete,Disconnected }

public sealed class SessionLifecycle
{
    public SessionState State {get;private set;}=SessionState.Menu;
    public long Generation {get;private set;}
    public void Reset(SessionState destination=SessionState.Menu){Generation++;State=destination;}
    public bool IsCurrent(long generation)=>generation==Generation;
    public void MoveTo(SessionState next)
    {
        if(next==State)return;
        bool legal=next is SessionState.Menu or SessionState.Disconnected || (State,next) switch
        {
            (SessionState.Menu or SessionState.Disconnected,SessionState.Lobby)=>true,
            (SessionState.Lobby or SessionState.Intermission,SessionState.Countdown)=>true,
            (SessionState.Countdown,SessionState.Racing or SessionState.Podium)=>true,
            (SessionState.Racing,SessionState.Podium or SessionState.CampaignComplete)=>true,
            (SessionState.Podium,SessionState.Intermission or SessionState.Countdown or SessionState.CampaignComplete)=>true,
            (SessionState.Lobby,SessionState.Intermission or SessionState.CampaignComplete)=>true,
            (SessionState.CampaignComplete,SessionState.Intermission or SessionState.Lobby)=>true,
            _=>false
        };
        if(!legal)throw new InvalidOperationException($"Niedozwolone przejście sesji: {State} → {next}.");
        State=next;
    }
}

/// <summary>Applied before the input source is queried and again before a command enters simulation.</summary>
public sealed class GameplayInputGate
{
    private bool fireLatched,mineLatched;
    public bool CanRead {get;private set;}
    public void Observe(SessionState state,bool uiBlocked,bool focused,bool fireHeld,bool mineHeld)
    {
        bool previouslyAllowed=CanRead;
        CanRead=state==SessionState.Racing && !uiBlocked && focused;
        if(!CanRead||!previouslyAllowed){fireLatched|=fireHeld;mineLatched|=mineHeld;}
        if(!fireHeld)fireLatched=false;if(!mineHeld)mineLatched=false;
    }
    public PlayerInputCommand Apply(PlayerInputCommand command,float currentYaw)=>!CanRead
        ? command with{Steering=0,Throttle=0,Brake=1,Handbrake=false,Fire=false,Mine=false,AimYaw=currentYaw}
        : command with{Fire=command.Fire&&!fireLatched,Mine=command.Mine&&!mineLatched};
    public void Block()=>CanRead=false;
    public void Reset(){CanRead=false;fireLatched=mineLatched=false;}
}
