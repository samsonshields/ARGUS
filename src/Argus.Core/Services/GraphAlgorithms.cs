using Argus.Core.DTOs;

namespace Argus.Core.Services;

public static class GraphAlgorithms
{
    // Structural metrics use an undirected simple projection: no weight/danger/intent interpretation.
    public static GraphResponse Measure(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges, bool includesProposed)
    {
        var neighbors = nodes.ToDictionary(x => x.Id, _ => new HashSet<Guid>());
        var counts = nodes.ToDictionary(x => x.Id, _ => 0);
        foreach (var edge in edges)
        {
            if (edge.Source == edge.Target || !neighbors.ContainsKey(edge.Source) || !neighbors.ContainsKey(edge.Target)) continue;
            neighbors[edge.Source].Add(edge.Target); neighbors[edge.Target].Add(edge.Source);
            counts[edge.Source]++; counts[edge.Target]++;
        }
        var components = new Dictionary<Guid, int>(); var componentCount = 0;
        var centrality = nodes.ToDictionary(x => x.Id, _ => 0d);
        foreach (var source in nodes.Select(x => x.Id))
        {
            if (!components.ContainsKey(source))
            {
                var queue = new Queue<Guid>(); queue.Enqueue(source); components[source] = componentCount++;
                while (queue.TryDequeue(out var current)) foreach (var next in neighbors[current])
                        if (components.TryAdd(next, components[source])) queue.Enqueue(next);
            }
            var stack = new Stack<Guid>(); var breadth = new Queue<Guid>(); breadth.Enqueue(source);
            var distance = nodes.ToDictionary(x => x.Id, _ => -1); distance[source] = 0;
            var sigma = nodes.ToDictionary(x => x.Id, _ => 0d); sigma[source] = 1;
            var predecessors = nodes.ToDictionary(x => x.Id, _ => new List<Guid>());
            while (breadth.TryDequeue(out var current))
            {
                stack.Push(current);
                foreach (var next in neighbors[current])
                {
                    if (distance[next] < 0) { distance[next] = distance[current] + 1; breadth.Enqueue(next); }
                    if (distance[next] == distance[current] + 1) { sigma[next] += sigma[current]; predecessors[next].Add(current); }
                }
            }
            var dependency = nodes.ToDictionary(x => x.Id, _ => 0d);
            while (stack.TryPop(out var current))
            {
                foreach (var previous in predecessors[current]) dependency[previous] += sigma[previous] / sigma[current] * (1 + dependency[current]);
                if (current != source) centrality[current] += dependency[current];
            }
        }
        return new(nodes.Select(x => x with
        {
            Degree = neighbors[x.Id].Count,
            ConnectionCount = counts[x.Id],
            DegreeCentrality = nodes.Count > 1 ? (double)neighbors[x.Id].Count / (nodes.Count - 1) : 0,
            Betweenness = nodes.Count > 2 ? centrality[x.Id] / ((nodes.Count - 1d) * (nodes.Count - 2d)) : 0,
            Component = components[x.Id]
        }).ToList(), edges, componentCount, includesProposed);
    }

    public static GraphPath ShortestPath(GraphResponse graph, Guid source, Guid target, bool respectDirection)
    {
        var adjacency = graph.Nodes.ToDictionary(x => x.Id, _ => new List<(Guid Node, Guid Edge)>());
        if (!adjacency.ContainsKey(source) || !adjacency.ContainsKey(target)) return new(false, [], []);
        foreach (var edge in graph.Edges)
        {
            adjacency[edge.Source].Add((edge.Target, edge.Id));
            if (!respectDirection || !edge.Directed) adjacency[edge.Target].Add((edge.Source, edge.Id));
        }
        var parents = new Dictionary<Guid, (Guid Node, Guid Edge)>(); var seen = new HashSet<Guid> { source };
        var queue = new Queue<Guid>(); queue.Enqueue(source);
        while (queue.TryDequeue(out var current))
        {
            if (current == target)
            {
                var nodes = new List<Guid> { target }; var edges = new List<Guid>();
                while (current != source) { var parent = parents[current]; edges.Add(parent.Edge); current = parent.Node; nodes.Add(current); }
                nodes.Reverse(); edges.Reverse(); return new(true, nodes, edges);
            }
            foreach (var next in adjacency[current]) if (seen.Add(next.Node)) { parents[next.Node] = (current, next.Edge); queue.Enqueue(next.Node); }
        }
        return new(false, [], []);
    }
}
