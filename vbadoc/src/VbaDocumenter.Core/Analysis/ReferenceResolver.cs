using VbaDocumenter.Core.Parsing;

namespace VbaDocumenter.Core.Analysis;

/// <summary>Counts references between nodes. Several references between the same two nodes become one edge with a count.</summary>
public sealed class EdgeCollector
{
  readonly Dictionary<(string From, string To, EdgeKind Kind), int> _counts = [];

  /// <summary>
  /// Adds a reference from a member of <paramref name="fromModule"/>. References to the member's own module node
  /// (e.g. "Me.Range" in a sheet) are dropped: they say nothing about dependencies.
  /// </summary>
  public void Add(string from, ModuleModel fromModule, string to, EdgeKind kind)
  {
    if (to.Equals(fromModule.Name, StringComparison.Ordinal)) return;
    _counts[(from, to, kind)] = _counts.GetValueOrDefault((from, to, kind)) + 1;
  }

  public IEnumerable<Edge> Edges => _counts.Select(c => new Edge(c.Key.From, c.Key.To, c.Key.Kind, c.Value));
}

/// <summary>
/// Finds what each statement refers to, using declared types to follow chains such as "repo.LoadAll" or
/// "Me.Target.Name". Based on the resolver in the VbaSequenceProbe spike.
/// </summary>
/// <remarks>
/// A plain name is looked up in this order: the procedure's locals and parameters, the module's own members,
/// module names, public members of standard modules (and public Enum values), and Enum names. Names that
/// match nothing in the project (Range, Debug, MsgBox, ...) belong to Excel or VBA and are ignored.
/// </remarks>
public sealed class ReferenceResolver(ProjectIndex index, EdgeCollector edges, List<LateBoundReference> lateBound)
{
  /// <summary>Where a statement or expression lives, and which node its references come from.</summary>
  sealed record Scope(ModuleModel Module, Member? Procedure, string FromId, int Line);

  /// <summary>A name in a chain, with whether it was followed by "(...)".</summary>
  sealed record Segment(string Name, bool HasArguments);

  /// <summary>Built-in functions that return Object: member access on their result is late-bound.</summary>
  static readonly HashSet<string> ObjectFunctions = new(StringComparer.OrdinalIgnoreCase) { "CreateObject", "GetObject" };

  /// <summary>Keywords that can appear where a name could. "Me" is handled separately.</summary>
  static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "And", "ByRef", "ByVal", "Call", "Case", "Const", "Dim", "Do", "Each", "Else", "ElseIf", "Empty", "End", "Eqv",
        "Erase", "Error", "Exit", "False", "For", "Function", "Get", "If", "Imp", "In", "Is", "Let", "Like", "Loop", "Mod",
        "Next", "Not", "Nothing", "Null", "On", "Optional", "Or", "Preserve", "Property", "ReDim", "Select", "Set",
        "Static", "Step", "Stop", "Sub", "Then", "To", "True", "TypeOf", "Until", "Wend", "While", "With", "Xor",
    };

  public void ResolveBody(ModuleModel module, Member procedure)
  {
    var withTypes = new Stack<ResolvedType>();
    foreach (var statement in procedure.Body)
    {
      var scope = new Scope(module, procedure, index.IdOf(procedure), statement.Line);
      var tokens = StatementTokenizer.Tokenize(statement.Text);

      if (tokens.Count == 2 && tokens[0].IsKeyword("End") && tokens[1].IsKeyword("With"))
      {
        if (withTypes.Count > 0) withTypes.Pop();
        continue;
      }

      var chains = ResolveTokens(tokens, scope, withTypes);
      if (tokens.Count >= 2 && tokens[0].IsKeyword("With"))
        withTypes.Push(WithBlockType(tokens, chains, scope));
    }
  }

  /// <summary>Resolves an expression outside a procedure body: a constant's value or a parameter's default.</summary>
  public void ResolveExpression(string expression, ModuleModel module, Member member)
  {
    var scope = new Scope(module, null, index.IdOf(member), member.DeclarationLine);
    ResolveTokens(StatementTokenizer.Tokenize(expression), scope, new Stack<ResolvedType>());
  }

  /// <summary>Resolves every chain in a statement and returns each one's position and resulting type.</summary>
  List<(int Start, int End, ResolvedType Type)> ResolveTokens(List<Token> t, Scope scope, Stack<ResolvedType> withTypes)
  {
    var chains = new List<(int, int, ResolvedType)>();
    for (int i = 0; i < t.Count; i++)
    {
      var token = t[i];
      bool isMemberName = i > 0 && t[i - 1].Kind is TokenKind.Dot or TokenKind.Bang; // part of a chain already handled

      if (token.Kind == TokenKind.Identifier && !isMemberName)
      {
        if (token.IsKeyword("New"))
        {
          var typeName = ReadTypeName(t, i + 1, out int last);
          if (typeName != null && index.TypeTargetId(typeName, scope.Module) is { } target)
            edges.Add(scope.FromId, scope.Module, target, EdgeKind.Uses);
          i = last;
        }
        else if (token.IsKeyword("As"))
        {
          // "Dim x As Order": declared types come from the parser's Locals, so skip the type name here.
          int next = i + 1 < t.Count && t[i + 1].IsKeyword("New") ? i + 2 : i + 1;
          ReadTypeName(t, next, out i);
        }
        else if (token.IsKeyword("RaiseEvent") && i + 1 < t.Count && t[i + 1].Kind == TokenKind.Identifier)
        {
          var name = t[++i].Text;
          var declaration = scope.Module.Members.FirstOrDefault(m => m.Kind == MemberKind.Event && Same(m.Name, name));
          if (declaration != null) edges.Add(scope.FromId, scope.Module, index.IdOf(declaration), EdgeKind.Events);
        }
        else if (token.IsKeyword("GoTo") || token.IsKeyword("GoSub") || token.IsKeyword("Resume"))
        {
          if (i + 1 < t.Count && t[i + 1].Kind == TokenKind.Identifier) i++; // a label, not a reference
        }
        else if (i + 1 < t.Count && t[i + 1].Kind == TokenKind.ColonEquals)
        {
          // A named argument ("repo:=Nothing"): the name is a parameter of the called procedure.
        }
        else if (!Keywords.Contains(token.Text))
        {
          var (end, type) = ResolveChain(t, i, withDot: false, scope, withTypes);
          chains.Add((i, end, type));
        }
      }
      else if (token.Kind == TokenKind.Dot && i + 1 < t.Count && t[i + 1].Kind == TokenKind.Identifier && !ContinuesChain(t, i))
      {
        // A leading dot refers to the object of the enclosing With block.
        var (end, type) = ResolveChain(t, i, withDot: true, scope, withTypes);
        chains.Add((i, end, type));
      }
    }
    return chains;
  }

  /// <summary>
  /// Resolves a chain such as "repo.LoadAll", "Logger.LogInfo", "Sheet1.Cells(1, 1).Value" or ".Summarise".
  /// Arguments in parentheses are skipped here; the caller's loop resolves them separately.
  /// </summary>
  (int End, ResolvedType Type) ResolveChain(List<Token> t, int start, bool withDot, Scope scope, Stack<ResolvedType> withTypes)
  {
    var segments = new List<Segment>();
    int j = withDot ? start + 1 : start;
    while (true)
    {
      var name = t[j++].Text;
      bool hasArguments = j < t.Count && t[j].Kind == TokenKind.OpenParen;
      if (hasArguments) j = MatchingClose(t, j) + 1;
      segments.Add(new Segment(name, hasArguments));
      if (j + 1 < t.Count && t[j].Kind == TokenKind.Dot && t[j + 1].Kind == TokenKind.Identifier) { j++; continue; }
      break;
    }

    var access = AccessAt(t, start, j);
    ResolvedType current;
    int next;
    if (withDot)
    {
      current = withTypes.Count > 0 ? withTypes.Peek() : ResolvedType.Unknown;
      next = 0;
    }
    else
    {
      current = ResolveFirstName(segments[0], scope, segments.Count == 1 ? access : Access.Read, isAlone: segments.Count == 1);
      next = 1;
    }

    for (; next < segments.Count; next++)
    {
      var segment = segments[next];
      var segmentAccess = next == segments.Count - 1 ? access : Access.Read;
      switch (current.Kind)
      {
        case ResolvedTypeKind.LateBound:
          lateBound.Add(new LateBoundReference(scope.FromId, Describe(segments, next, withDot), scope.Line));
          return (j, ResolvedType.Unknown);

        case ResolvedTypeKind.Module:
          var target = current.Module!;
          var member = index.FindMember(target, segment.Name, segmentAccess, includePrivate: target == scope.Module);
          if (member == null)
          {
            // Not one of our members (e.g. Sheet1.Range): count it as a use of the module itself.
            edges.Add(scope.FromId, scope.Module, target.Name, EdgeKind.Uses);
            return (j, ResolvedType.Unknown);
          }
          edges.Add(scope.FromId, scope.Module, index.IdOf(member), EdgeKind.Uses);
          current = TypeOf(member, segmentAccess);
          break;

        case ResolvedTypeKind.UserType:
          var field = current.Declaration!.Fields.FirstOrDefault(f => Same(f.Name, segment.Name));
          current = field == null ? ResolvedType.Unknown : index.TypeFromName(field.Type, index.OwnerOf(current.Declaration));
          break;

        case ResolvedTypeKind.EnumQualifier:
          var enumDeclaration = current.Declaration!;
          var value = index.OwnerOf(enumDeclaration).Members.FirstOrDefault(m =>
              m.Kind == MemberKind.EnumMember && Same(m.ParentName!, enumDeclaration.Name) && Same(m.Name, segment.Name));
          if (value != null) edges.Add(scope.FromId, scope.Module, index.IdOf(value), EdgeKind.Uses);
          return (j, ResolvedType.Unknown);

        default:
          return (j, ResolvedType.Unknown); // Excel or VBA object: nothing to follow
      }
    }
    return (j, current);
  }

  /// <summary>Resolves the first name of a chain, adding an edge if it is a project member.</summary>
  ResolvedType ResolveFirstName(Segment segment, Scope scope, Access access, bool isAlone)
  {
    var name = segment.Name;
    if (Same(name, "Me")) return new ResolvedType(ResolvedTypeKind.Module, scope.Module);

    var procedure = scope.Procedure;
    if (procedure != null)
    {
      // Inside a Function or Property Get, its own name is the return value, unless it is called
      // with arguments, which is recursion (e.g. Factorial(n - 1)).
      bool isOwnName = Same(name, procedure.Name) && procedure.Kind is MemberKind.Function or MemberKind.PropertyGet;
      bool isRecursiveCall = isOwnName && segment.HasArguments && procedure.Parameters.Count > 0;
      if (isOwnName && !isRecursiveCall) return index.TypeFromName(procedure.Type, scope.Module);

      if (!isOwnName)
      {
        var local = procedure.Locals.FirstOrDefault(l => Same(l.Name, name));
        if (local != null) return local.IsConstant ? ResolvedType.Unknown : index.TypeFromName(local.Type, scope.Module);
        var parameter = procedure.Parameters.FirstOrDefault(p => Same(p.Name, name));
        if (parameter != null) return index.TypeFromName(parameter.Type, scope.Module);
      }
    }

    var own = index.FindMember(scope.Module, name, access, includePrivate: true);
    if (own != null)
    {
      edges.Add(scope.FromId, scope.Module, index.IdOf(own), EdgeKind.Uses);
      return TypeOf(own, access);
    }

    if (ObjectFunctions.Contains(name)) return ResolvedType.LateBound;

    var module = index.FindModule(name);
    if (module != null)
    {
      if (isAlone) edges.Add(scope.FromId, scope.Module, module.Name, EdgeKind.Uses); // e.g. passing Sheet1 as an argument
      return new ResolvedType(ResolvedTypeKind.Module, module);
    }

    var global = index.FindGlobal(name);
    if (global != null)
    {
      edges.Add(scope.FromId, scope.Module, index.IdOf(global), EdgeKind.Uses);
      return TypeOf(global, access);
    }

    var enumDeclaration = index.FindTypeDeclaration(name, scope.Module, MemberKind.Enum);
    if (enumDeclaration != null) return new ResolvedType(ResolvedTypeKind.EnumQualifier, Declaration: enumDeclaration);

    return ResolvedType.Unknown;
  }

  /// <summary>The type of the value a member gives when read. Assigning to a member gives nothing to follow.</summary>
  ResolvedType TypeOf(Member member, Access access)
  {
    if (access != Access.Read) return ResolvedType.Unknown;
    bool hasValue = member.Kind is MemberKind.Variable or MemberKind.Function or MemberKind.PropertyGet
                    || (member.Kind == MemberKind.Declare && member.IsFunctionDeclare);
    return hasValue ? index.TypeFromName(member.Type, index.OwnerOf(member)) : ResolvedType.Unknown;
  }

  /// <summary>The object a With block refers to: "With svc", "With Me.Target" or "With New Order".</summary>
  ResolvedType WithBlockType(List<Token> t, List<(int Start, int End, ResolvedType Type)> chains, Scope scope)
  {
    if (t[1].IsKeyword("New"))
    {
      var typeName = ReadTypeName(t, 2, out _);
      return typeName == null ? ResolvedType.Unknown : index.TypeFromName(typeName, scope.Module);
    }
    // Only a With expression that is one complete chain has a type we can follow.
    return chains.Where(c => c.Start == 1 && c.End == t.Count).Select(c => c.Type).FirstOrDefault() ?? ResolvedType.Unknown;
  }

  /// <summary>
  /// A chain at the start of a statement followed by "=" is being assigned to: Property Let, or Property Set
  /// after "Set". Everywhere else (including comparisons such as "If o.Status = x") it is being read.
  /// </summary>
  static Access AccessAt(List<Token> t, int start, int end)
  {
    bool startsStatement = start == 0 || (start == 1 && (t[0].IsKeyword("Set") || t[0].IsKeyword("Let")));
    if (!startsStatement || end >= t.Count || t[end].Kind != TokenKind.Equals) return Access.Read;
    return t[0].IsKeyword("Set") ? Access.Set : Access.Let;
  }

  /// <summary>
  /// True if the dot at <paramref name="dot"/> continues a chain ("obj.Member", "f(x).Member"). A dot after a
  /// keyword or an operator starts a With-block access instead: "With .Child", "If .Count > 0", "x + .Total".
  /// </summary>
  static bool ContinuesChain(List<Token> t, int dot)
  {
    if (dot == 0) return false;
    var previous = t[dot - 1];
    return previous.Kind == TokenKind.CloseParen
        || (previous.Kind == TokenKind.Identifier && !Keywords.Contains(previous.Text));
  }

  /// <summary>Reads a possibly qualified type name ("Order", "Excel.Range") starting at <paramref name="i"/>.</summary>
  static string? ReadTypeName(List<Token> t, int i, out int last)
  {
    last = i - 1;
    if (i >= t.Count || t[i].Kind != TokenKind.Identifier) return null;
    var name = t[i].Text;
    last = i;
    while (last + 2 < t.Count && t[last + 1].Kind == TokenKind.Dot && t[last + 2].Kind == TokenKind.Identifier)
    {
      name += "." + t[last + 2].Text;
      last += 2;
    }
    return name;
  }

  static int MatchingClose(List<Token> t, int open)
  {
    int depth = 0;
    for (int i = open; i < t.Count; i++)
    {
      if (t[i].Kind == TokenKind.OpenParen) depth++;
      else if (t[i].Kind == TokenKind.CloseParen && --depth == 0) return i;
    }
    return t.Count - 1;
  }

  /// <summary>Text for a late-bound reference, e.g. "legacy.ExportAll" or "CreateObject(…).OpenTextFile".</summary>
  static string Describe(List<Segment> segments, int upTo, bool withDot)
  {
    var parts = segments.Take(upTo + 1).Select((s, i) => s.HasArguments && i < upTo ? s.Name + "(…)" : s.Name);
    return (withDot ? "." : "") + string.Join(".", parts);
  }

  static bool Same(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);
}
