using Game.Core;

namespace Game.Core.Tests;
public class SessionTests
{
    [Fact]
    public void FrozenTurretKeepsDirectionWithinTheWireAngleRange()
    {
        var gate=new GameplayInputGate();gate.Observe(SessionState.Racing,true,true,false,false);
        var command=gate.Apply(new(1,1,0,0,0,0,false,false,false),MathF.Tau*3+1.1f);
        Assert.True(command.IsValid);Assert.Equal(1.1f,command.AimYaw,4);
    }
    [Fact]
    public void ClosingUiClickBetweenTicksStillRequiresRelease()
    {
        var gate=new GameplayInputGate();gate.Observe(SessionState.Racing,false,true,false,false);
        gate.Block();gate.Observe(SessionState.Racing,false,true,true,true);
        var command=new PlayerInputCommand(1,1,0,0,0,1,false,true,true);
        Assert.False(gate.Apply(command,1).Fire);Assert.False(gate.Apply(command,1).Mine);
        gate.Observe(SessionState.Racing,false,true,false,false);gate.Observe(SessionState.Racing,false,true,true,true);
        Assert.True(gate.Apply(command,1).Fire);Assert.True(gate.Apply(command,1).Mine);
    }
    [Fact]
    public void UiBlocksEveryActionAndAimUntilButtonsHaveBeenReleased()
    {
        var gate=new GameplayInputGate();var held=new PlayerInputCommand(1,1,1,1,0,2,true,true,true);
        gate.Observe(SessionState.Racing,true,true,true,true);
        var blocked=gate.Apply(held,.4f);
        Assert.False(gate.CanRead);Assert.Equal(.4f,blocked.AimYaw);Assert.Equal(0,blocked.Throttle);Assert.Equal(1,blocked.Brake);
        Assert.False(blocked.Fire||blocked.Mine||blocked.Handbrake);
        gate.Observe(SessionState.Racing,false,true,true,true);
        Assert.True(gate.CanRead);Assert.False(gate.Apply(held,.4f).Fire);Assert.False(gate.Apply(held,.4f).Mine);
        gate.Observe(SessionState.Racing,false,true,false,false);
        gate.Observe(SessionState.Racing,false,true,true,true);Assert.True(gate.Apply(held,.4f).Fire);
    }
    [Theory]
    [InlineData(SessionState.Menu)] [InlineData(SessionState.Lobby)] [InlineData(SessionState.Countdown)]
    [InlineData(SessionState.Podium)] [InlineData(SessionState.Intermission)]
    [InlineData(SessionState.CampaignComplete)] [InlineData(SessionState.Disconnected)]
    public void NonRacingStatesNeverProduceGameplayInput(SessionState state)
    {var gate=new GameplayInputGate();gate.Observe(state,false,true,false,false);Assert.False(gate.CanRead);}
    [Fact]
    public void FocusLossAndIndependentFireButtonsDoNotLeak()
    {
        var gate=new GameplayInputGate();var input=new PlayerInputCommand(1,1,0,1,0,1,false,true,true);
        gate.Observe(SessionState.Racing,false,false,true,true);
        Assert.False(gate.Apply(input,0).Fire);
        gate.Observe(SessionState.Racing,false,true,false,true);
        gate.Observe(SessionState.Racing,false,true,true,true);
        Assert.True(gate.Apply(input,0).Fire);Assert.False(gate.Apply(input,0).Mine);
    }
    [Fact]
    public void SessionGenerationInvalidatesCallbacksAndTransitionsAreExplicit()
    {
        var session=new SessionLifecycle();long previous=session.Generation;
        Assert.Throws<InvalidOperationException>(()=>session.MoveTo(SessionState.Racing));
        session.MoveTo(SessionState.Lobby);session.MoveTo(SessionState.Countdown);session.MoveTo(SessionState.Racing);
        session.MoveTo(SessionState.Podium);session.MoveTo(SessionState.Intermission);session.MoveTo(SessionState.Countdown);
        session.Reset(SessionState.Disconnected);Assert.False(session.IsCurrent(previous));
        session.MoveTo(SessionState.Menu);session.MoveTo(SessionState.Lobby);
    }
}
