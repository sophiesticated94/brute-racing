using Game.Core;
using Godot;

namespace Game.Client;

public partial class SkillCanvas : Control
{
    public event Action<string>? Selected;
    private SkillGraph graph=null!;
    private Dictionary<string,int> ranks=[];
    private Vector2 pan=new(22,32);
    private float zoom=.62f;
    private bool dragging;
    private string selected="";
    private int points,level;
    public override void _Ready(){MouseFilter=MouseFilterEnum.Stop;ClipContents=true;CustomMinimumSize=new(780,550);}
    public void Configure(SkillGraph value)=>graph=value;
    public void UpdateBuild(PlayerLoadoutDto build,int available)
    {ranks=build.SkillRanks;level=build.PlayerLevel;points=available;QueueRedraw();}
    public override void _GuiInput(InputEvent e)
    {
        if(e is InputEventMouseButton button)
        {
            if(button.ButtonIndex is MouseButton.Middle or MouseButton.Right){dragging=button.Pressed;AcceptEvent();}
            if(button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                var world=(button.Position-pan)/zoom;zoom=Mathf.Clamp(zoom*(button.ButtonIndex==MouseButton.WheelUp?1.12f:1/1.12f),.35f,1.6f);
                pan=button.Position-world*zoom;QueueRedraw();AcceptEvent();
            }
            if(button.Pressed && button.ButtonIndex==MouseButton.Left && graph is not null)
            {
                var world=(button.Position-pan)/zoom;
                foreach(var node in graph.Nodes.Values)
                    if(new Rect2(new(node.Position.X,node.Position.Y),new(214,78)).HasPoint(world))
                    {selected=node.Id;Selected?.Invoke(selected);QueueRedraw();AcceptEvent();break;}
            }
        }
        if(e is InputEventMouseMotion motion && dragging){pan+=motion.Relative;QueueRedraw();AcceptEvent();}
    }
    public override void _Draw()
    {
        DrawRect(new(Vector2.Zero,Size),new("10222e"));
        if(graph is null)return;
        DrawSetTransform(pan,0,Vector2.One*zoom);
        var font=ThemeDB.FallbackFont;
        foreach(var node in graph.Nodes.Values)
        {
            var at=new Vector2(node.Position.X,node.Position.Y);
            foreach(var prerequisite in node.Prerequisites)
            {
                var parent=graph.Nodes[prerequisite.NodeId];var from=new Vector2(parent.Position.X+107,parent.Position.Y+78);
                bool met=ranks.GetValueOrDefault(parent.Id)>=prerequisite.MinRank;
                DrawLine(from,at+new Vector2(107,0),met?new("e6b55c"):new Color("34505c"),3,true);
            }
        }
        foreach(var node in graph.Nodes.Values)
        {
            int rank=ranks.GetValueOrDefault(node.Id);
            bool exclusive=node.ExclusiveGroup is not null && graph.Nodes.Values.Any(n=>n.Id!=node.Id && n.ExclusiveGroup==node.ExclusiveGroup && ranks.GetValueOrDefault(n.Id)>0);
            bool available=!exclusive && level>=node.RequiredPlayerLevel && node.Prerequisites.All(p=>ranks.GetValueOrDefault(p.NodeId)>=p.MinRank) && rank<node.MaxRank && points>=node.RankCosts[rank];
            Color fill=exclusive?new("512e3d"):rank==node.MaxRank?new("3c6d58"):rank>0?new("755e37"):available?new("29566c"):new("203541");
            var rect=new Rect2(new(node.Position.X,node.Position.Y),new(214,78));
            DrawRect(rect,fill);DrawRect(rect,selected==node.Id?new("ffdf9b"):new Color("4b6770"),false,selected==node.Id?3:1);
            DrawString(font,rect.Position+new Vector2(10,25),node.Name,HorizontalAlignment.Left,194,16,new("f0eddb"));
            DrawString(font,rect.Position+new Vector2(10,56),$"{rank}/{node.MaxRank}   {(rank<node.MaxRank?$"+{node.RankCosts[rank]} SP":"MAKSIMUM")}",HorizontalAlignment.Left,194,14,new("b5cac8"));
            if(node.Prerequisites.Length==0)
                DrawString(font,rect.Position+new Vector2(0,-12),BranchName(node.Branch),HorizontalAlignment.Left,230,19,new("ffbe45"));
        }
        DrawSetTransform(Vector2.Zero);
    }
    public static string BranchName(string branch)=>branch switch{"Drive"=>"NAPĘD","Ballistics"=>"BALISTYKA","Energy"=>"ENERGIA","Defense"=>"OBRONA",_=>"SABOTAŻ"};
}
