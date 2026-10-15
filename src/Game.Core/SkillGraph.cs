namespace Game.Core;

public sealed record SkillPosition(float X, float Y);
public sealed record SkillRequirement(string NodeId, int MinRank);
public sealed record SkillRankEffect(StatModifier[] Modifiers, MechanicsFlags Mechanics = MechanicsFlags.None);
public sealed record SkillNodeDefinition(string Id, string Name, string Description, string Branch,
    SkillPosition Position, int MaxRank, int[] RankCosts, SkillRequirement[] Prerequisites,
    string? ExclusiveGroup, int RequiredPlayerLevel, SkillRankEffect[] RankEffects);

public sealed class SkillGraph
{
    public Dictionary<string, SkillNodeDefinition> Nodes { get; }
    public string[] TopologicalOrder { get; }
    public SkillGraph(IEnumerable<SkillNodeDefinition> definitions)
    {
        Nodes = new(StringComparer.Ordinal);
        foreach (var node in definitions)
        {
            if (node is null || string.IsNullOrWhiteSpace(node.Id) || string.IsNullOrWhiteSpace(node.Name) ||
                node.MaxRank is < 1 or > 5 || node.RankCosts is null || node.RankEffects is null ||
                node.RankCosts.Length != node.MaxRank || node.RankEffects.Length != node.MaxRank || node.RankCosts.Any(c => c is < 1 or > 10) ||
                node.Prerequisites is null || node.Position is null || !float.IsFinite(node.Position.X) || !float.IsFinite(node.Position.Y) ||
                node.RequiredPlayerLevel is < 1 or > 6 || !new[] {"Drive","Ballistics","Energy","Defense","Sabotage"}.Contains(node.Branch) || !Nodes.TryAdd(node.Id, node))
                throw new InvalidDataException("Nieprawidłowa definicja węzła DAG.");
            foreach (var effect in node.RankEffects)
            {
                if (effect?.Modifiers is null || ((int)effect.Mechanics & ~63)!=0) throw new InvalidDataException("Brak efektów rangi.");
                foreach (var modifier in effect.Modifiers)
                {
                    if(modifier is null || modifier.Stat is null)throw new InvalidDataException("Nieprawidłowy modyfikator.");
                    var parts = modifier.Stat.Split('.',2);
                    Type? target = parts[0] switch { "car" => typeof(CarDefinition), "primary" or "mines" => typeof(WeaponDefinition), "mechanics" => typeof(MechanicsStats), _ => null };
                    if (parts.Length != 2 || target?.GetProperty(parts[1]) is null || !StatLimits.IsKnown(parts[1]) || !float.IsFinite(modifier.Value) ||
                        !Enum.IsDefined(modifier.Operation) || modifier.Operation == ModifierOperation.Multiply && modifier.Value <= 0)
                        throw new InvalidDataException("Nieprawidłowy modyfikator DAG.");
                }
            }
        }
        foreach (var node in Nodes.Values)
            if (node.Prerequisites.Select(p => p.NodeId).Distinct(StringComparer.Ordinal).Count() != node.Prerequisites.Length ||
                node.Prerequisites.Any(p => !Nodes.TryGetValue(p.NodeId, out var parent) || p.MinRank < 1 || p.MinRank > parent.MaxRank))
                throw new InvalidDataException("Nieprawidłowe zależności DAG.");
        var pending = Nodes.ToDictionary(x => x.Key, x => x.Value.Prerequisites.Length, StringComparer.Ordinal);
        var roots = new SortedSet<string>(pending.Where(x => x.Value == 0).Select(x => x.Key), StringComparer.Ordinal);
        var sorted = new List<string>();
        while (roots.Count > 0)
        {
            string id = roots.Min!; roots.Remove(id); sorted.Add(id);
            foreach (var child in Nodes.Values.Where(n => n.Prerequisites.Any(p => p.NodeId == id)))
                if (--pending[child.Id] == 0) roots.Add(child.Id);
        }
        if (sorted.Count != Nodes.Count) throw new InvalidDataException("Cykl w grafie umiejętności.");
        TopologicalOrder = sorted.ToArray();
    }
    public int Cost(IReadOnlyDictionary<string,int> ranks) => ranks.Sum(r => Nodes[r.Key].RankCosts.Take(r.Value).Sum());
    public IEnumerable<(string Source,StatModifier Modifier)> Modifiers(IReadOnlyDictionary<string,int> ranks) =>
        ranks.Where(r => r.Value > 0).OrderBy(r => r.Key,StringComparer.Ordinal)
            .SelectMany(r => Nodes[r.Key].RankEffects[r.Value-1].Modifiers.Select(m => (r.Key,m)));
    public MechanicsFlags Mechanics(IReadOnlyDictionary<string,int> ranks) => ranks.Where(r => r.Value > 0)
        .Aggregate(MechanicsFlags.None,(flags,r) => flags | Nodes[r.Key].RankEffects[r.Value-1].Mechanics);
}
