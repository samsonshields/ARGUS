using System.Diagnostics;
using Argus.Core.DTOs;
using Argus.Core.Entities;
using Argus.Core.Enums;
using Argus.Core.Services;

namespace Argus.UnitTests;

public sealed class GraphTests
{
    private static Guid Id(int number) => Guid.Parse($"00000000-0000-0000-0000-{number:D12}");
    private static GraphNode Node(int number) => new(Id(number), $"Fictional {number}", EntityType.PERSON, 0, 0, 0, 0, 0);
    private static GraphEdge Edge(int number, int first, int second, bool directed = false) => new(Id(1000 + number), Id(first), Id(second),
        RelationshipType.MENTIONED_WITH, ReviewStatus.PROPOSED, "MENTIONED_WITH", directed, 0, 1);
    [Fact]
    public void EmptyAndDisconnectedGraphsHaveExplicitMetricsAndNoInventedPaths()
    {
        Assert.Equal(0, GraphAlgorithms.Measure([], [], false).ComponentCount);
        var graph = GraphAlgorithms.Measure([Node(1), Node(2), Node(3)], [Edge(1, 1, 2)], true);
        Assert.Equal(2, graph.ComponentCount);
        Assert.Equal(0, graph.Nodes.Single(x => x.Id == Id(3)).Degree);
        Assert.False(GraphAlgorithms.ShortestPath(graph, Id(1), Id(3), false).Found);
        Assert.Equal(new[] { Id(1) }, GraphAlgorithms.ShortestPath(graph, Id(1), Id(1), false).NodeIds);
    }
    [Fact]
    public void ChainCentralitiesAndDirectionsMatchKnownValues()
    {
        var graph = GraphAlgorithms.Measure([Node(1), Node(2), Node(3)], [Edge(1, 1, 2, true), Edge(2, 2, 3, true)], false);
        var middle = graph.Nodes.Single(x => x.Id == Id(2));
        Assert.Equal(2, middle.Degree); Assert.Equal(1, middle.DegreeCentrality); Assert.Equal(1, middle.Betweenness);
        Assert.Equal(0, graph.Nodes.Single(x => x.Id == Id(1)).Betweenness);
        Assert.True(GraphAlgorithms.ShortestPath(graph, Id(3), Id(1), false).Found);
        Assert.False(GraphAlgorithms.ShortestPath(graph, Id(3), Id(1), true).Found);
    }
    [Fact]
    public void ParallelEdgesDoNotInflateUniqueNeighborCentrality()
    {
        var graph = GraphAlgorithms.Measure([Node(1), Node(2)], [Edge(1, 1, 2), Edge(2, 1, 2)], true);
        Assert.All(graph.Nodes, node => { Assert.Equal(1, node.Degree); Assert.Equal(2, node.ConnectionCount); Assert.Equal(1, node.DegreeCentrality); });
    }
    [Fact]
    public void HundredNodesAndThreeHundredEdgesAreMeasuredWithinLocalBudget()
    {
        var nodes = Enumerable.Range(0, 100).Select(Node).ToList();
        var edges = new List<GraphEdge>();
        for (var i = 0; i < 100; i++) for (var offset = 1; offset <= 3; offset++) edges.Add(Edge(i * 3 + offset, i, (i + offset) % 100));
        var clock = Stopwatch.StartNew(); var graph = GraphAlgorithms.Measure(nodes, edges, true); clock.Stop();
        Assert.Equal(1, graph.ComponentCount); Assert.Equal(300, graph.Edges.Count);
        Assert.All(graph.Nodes, node => Assert.Equal(6, node.Degree));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"100/300 graph metrics took {clock.Elapsed}.");
    }
    [Fact]
    public void SubstantiveReviewRequiresReasonEvidenceAndExplicitConfirmation()
    {
        var relationship = new Relationship(Id(1), Id(2), Id(3));
        Assert.Throws<System.ComponentModel.DataAnnotations.ValidationException>(() => relationship.Approve(new() { Type = RelationshipType.MEMBER_OF }));
        relationship.Approve(new() { Type = RelationshipType.MEMBER_OF, Notes = "Explicit training statement reviewed", EvidenceIds = [Id(4)], ConfirmEvidenceReviewed = true });
        Assert.Equal(ReviewStatus.APPROVED, relationship.Status);
        Assert.Throws<InvalidOperationException>(() => relationship.Reject());
        relationship.RedirectEntity(Id(2), Id(3));
        Assert.NotNull(relationship.RetiredReason);
    }
}
