using PowerQueryDocumenter.Model;

namespace PowerQueryDocumenter.Analysis;

/// <summary>
/// Finds circular dependencies (FR-ANA-5) using Tarjan's strongly connected components algorithm.
/// The implementation is iterative, so very deep dependency chains cannot overflow the stack.
/// </summary>
public static class CycleDetector
{
  /// <summary>
  /// Returns one <see cref="DependencyCycle"/> per strongly connected group of two or more queries.
  /// Self-loops are not reported (self-references are ignored by the graph).
  /// </summary>
  /// <param name="nodes">Every node name. Order determines the order of the result.</param>
  /// <param name="successors">Direct successors (downstream queries) of a node.</param>
  public static IReadOnlyList<DependencyCycle> FindCycles(
    IReadOnlyList<string> nodes,
    Func<string, IEnumerable<string>> successors)
  {
    ArgumentNullException.ThrowIfNull(nodes);
    ArgumentNullException.ThrowIfNull(successors);

    var comparer = DependencyGraph.NameComparer;
    var indexOf = new Dictionary<string, int>(comparer);
    for (var i = 0; i < nodes.Count; i++)
    {
      indexOf.TryAdd(nodes[i], i);
    }

    var adjacency = new int[nodes.Count][];
    for (var i = 0; i < nodes.Count; i++)
    {
      adjacency[i] = successors(nodes[i])
        .Where(indexOf.ContainsKey)
        .Select(s => indexOf[s])
        .Where(s => s != i)
        .Distinct()
        .ToArray();
    }

    var components = FindStronglyConnectedComponents(adjacency);

    return components
      .Where(c => c.Count > 1)
      .Select(c => BuildCycle(c, nodes, adjacency))
      .OrderBy(c => c.Members[0], comparer)
      .ToArray();
  }

  private static List<List<int>> FindStronglyConnectedComponents(int[][] adjacency)
  {
    var count = adjacency.Length;
    var index = new int[count];
    var lowLink = new int[count];
    var onStack = new bool[count];
    Array.Fill(index, -1);

    var componentStack = new Stack<int>();
    var callStack = new Stack<(int Node, int NextEdge)>();
    var components = new List<List<int>>();
    var counter = 0;

    for (var start = 0; start < count; start++)
    {
      if (index[start] != -1)
      {
        continue;
      }

      Visit(start);

      while (callStack.Count > 0)
      {
        var (node, nextEdge) = callStack.Pop();

        if (nextEdge < adjacency[node].Length)
        {
          // Come back to this node for its next edge after handling the current one.
          callStack.Push((node, nextEdge + 1));
          var successor = adjacency[node][nextEdge];

          if (index[successor] == -1)
          {
            Visit(successor);
          }
          else if (onStack[successor])
          {
            lowLink[node] = Math.Min(lowLink[node], index[successor]);
          }

          continue;
        }

        // All edges of this node are done: propagate its low-link to the caller.
        if (callStack.Count > 0)
        {
          var parent = callStack.Peek().Node;
          lowLink[parent] = Math.Min(lowLink[parent], lowLink[node]);
        }

        if (lowLink[node] == index[node])
        {
          var component = new List<int>();
          int member;
          do
          {
            member = componentStack.Pop();
            onStack[member] = false;
            component.Add(member);
          }
          while (member != node);

          components.Add(component);
        }
      }
    }

    return components;

    void Visit(int node)
    {
      index[node] = counter;
      lowLink[node] = counter;
      counter++;
      componentStack.Push(node);
      onStack[node] = true;
      callStack.Push((node, 0));
    }
  }

  private static DependencyCycle BuildCycle(List<int> component, IReadOnlyList<string> nodes, int[][] adjacency)
  {
    var comparer = DependencyGraph.NameComparer;
    var members = component.Select(i => nodes[i]).Order(comparer).ToArray();
    var memberSet = component.ToHashSet();

    // Start from the alphabetically first member and find the shortest path back to it (BFS),
    // staying inside the component. Successors are visited in name order so the path is stable.
    var start = component.OrderBy(i => nodes[i], comparer).First();
    var previous = new Dictionary<int, int>();
    var queue = new Queue<int>();
    queue.Enqueue(start);
    var visited = new HashSet<int> { start };
    var last = -1;

    while (queue.Count > 0 && last == -1)
    {
      var current = queue.Dequeue();
      foreach (var next in adjacency[current].Where(memberSet.Contains).OrderBy(i => nodes[i], comparer))
      {
        if (next == start)
        {
          last = current;
          break;
        }

        if (visited.Add(next))
        {
          previous[next] = current;
          queue.Enqueue(next);
        }
      }
    }

    // A strongly connected component with more than one member always contains a cycle through
    // any of its members, so 'last' is always found.
    var path = new List<int> { start };
    for (var step = last; step != start; step = previous[step])
    {
      path.Insert(1, step);
    }

    path.Add(start);
    return new DependencyCycle(members, path.Select(i => nodes[i]).ToArray());
  }
}
