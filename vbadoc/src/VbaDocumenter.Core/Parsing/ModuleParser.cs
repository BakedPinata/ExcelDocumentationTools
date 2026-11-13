using System.Text.RegularExpressions;
using VbaDocumenter.Core.Extraction;

namespace VbaDocumenter.Core.Parsing;

/// <summary>
/// A deliberately lightweight VBA parser: it recognises declarations and procedure boundaries line by line
/// with regular expressions, rather than implementing the full VBA grammar. Procedure bodies are kept as a
/// list of statements for the reference resolver (stage 3) to interpret.
/// </summary>
/// <remarks>
/// Line numbers refer to the text passed in. The tool passes the exported file's text, so they match the
/// files on disk, including the class header the exporter adds.
/// </remarks>
public static partial class ModuleParser
{
  const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

  // VBA identifiers start with a letter (any script) and continue with letters, digits or underscores.
  const string Id = @"[\p{L}][\p{L}\p{Nd}_]*";
  // Type-declaration characters, e.g. Dim s$ (String). See TypeForSuffix.
  const string Suffix = @"[%&!#@$^]?";

  // The patterns are source-generated at compile time. Interpolating Id and Suffix is fine here because
  // an interpolated string made only of constant strings is itself a constant.
  [GeneratedRegex($@"^(?:(?<vis>Public|Private|Friend|Global)\s+)?(?:(?<static>Static)\s+)?(?<kind>Sub|Function|Property\s+(?:Get|Let|Set))\s+(?<name>{Id})(?<suffix>{Suffix})\s*(?<rest>.*)$", Opts)]
  private static partial Regex ProcedureStart();
  [GeneratedRegex(@"^End\s+(?<kind>Sub|Function|Property)$", Opts)]
  private static partial Regex ProcedureEnd();
  [GeneratedRegex(@"^#If\b", Opts)]
  private static partial Regex DirectiveIf();
  [GeneratedRegex(@"^#Else(?:If)?\b", Opts)]
  private static partial Regex DirectiveElse();
  [GeneratedRegex(@"^#End\s*If\b", Opts)]
  private static partial Regex DirectiveEndIf();
  // Rubberduck's folder annotation, with or without brackets: '@Folder("Parent.Child") or '@Folder "Parent.Child". It
  // can also follow a label ending in a colon, as in a header's ' Folder:<tabs>@Folder("Reports"), which Rubberduck
  // itself doesn't read; anything else before it, such as prose that mentions it, doesn't count.
  [GeneratedRegex(@"^\s*(?:[^:@""]*:\s*)?@Folder\s*(?:\(\s*""(?<path>[^""]*)""\s*\)|""(?<path>[^""]*)"")", Opts)]
  private static partial Regex FolderAnnotation();
  [GeneratedRegex($@"^(?:(?<vis>Public|Private)\s+)?Enum\s+(?<name>{Id})$", Opts)]
  private static partial Regex EnumStart();
  [GeneratedRegex($@"^(?:(?<vis>Public|Private)\s+)?Type\s+(?<name>{Id})$", Opts)]
  private static partial Regex TypeStart();
  [GeneratedRegex(@"^End\s+(?<kind>Enum|Type)$", Opts)]
  private static partial Regex BlockEnd();
  [GeneratedRegex($@"^(?:(?<vis>Public)\s+)?Event\s+(?<name>{Id})\s*(?<rest>.*)$", Opts)]
  private static partial Regex EventDeclaration();
  [GeneratedRegex($@"^(?:(?<vis>Public|Private|Global)\s+)?Declare\s+(?:PtrSafe\s+)?(?<kind>Sub|Function)\s+(?<name>{Id})(?<suffix>{Suffix})\s+Lib\s+""(?<lib>[^""]*)""(?:\s+Alias\s+""(?<alias>[^""]*)"")?\s*(?<rest>.*)$", Opts)]
  private static partial Regex DeclareStatement();
  [GeneratedRegex(@"^(?:(?<vis>Public|Private|Global)\s+)?Const\s+(?<items>.+)$", Opts)]
  private static partial Regex ConstDeclaration();
  [GeneratedRegex(@"^(?<keyword>Dim|Private|Public|Global)\s+(?<items>.+)$", Opts)]
  private static partial Regex VariableDeclaration();
  [GeneratedRegex($@"^Implements\s+(?<name>{Id}(?:\.{Id})?)$", Opts)]
  private static partial Regex ImplementsStatement();
  [GeneratedRegex(@"^Option\s+\w", Opts)]
  private static partial Regex OptionStatement();
  [GeneratedRegex(@"^Def(?:Bool|Byte|Int|Lng|LngLng|LngPtr|Cur|Sng|Dbl|Dec|Date|Str|Obj|Var)\s", Opts)]
  private static partial Regex DefTypeStatement();
  [GeneratedRegex(@"^Attribute\s+(?<name>[\w.]+)\s*=\s*(?<value>.*)$", Opts)]
  private static partial Regex AttributeStatement();
  [GeneratedRegex(@"^(?<keyword>Dim|Static|Const|ReDim(?:\s+Preserve)?)\s+(?<items>.+)$", Opts)]
  private static partial Regex LocalDeclaration();
  [GeneratedRegex($@"^(?:{Id}|\d+):(?!=)\s*", Opts)]
  private static partial Regex LineLabel();
  [GeneratedRegex(@"^\d+\s+", Opts)]
  private static partial Regex LineNumber();
  static readonly HashSet<string> NotLabels = new(StringComparer.OrdinalIgnoreCase)
        { "Else", "Do", "Loop", "Next", "Wend", "Exit", "Resume", "Stop", "End", "Return" };

  [GeneratedRegex($@"^(?<optional>Optional\s+)?(?:(?<by>ByVal|ByRef)\s+)?(?<paramArray>ParamArray\s+)?(?<name>{Id})(?<suffix>{Suffix})\s*(?<array>\(\s*\))?\s*(?:As\s+(?<type>[^=]+?))?\s*(?:=\s*(?<default>.+))?$", Opts)]
  private static partial Regex ParameterItem();
  [GeneratedRegex($@"^(?<withEvents>WithEvents\s+)?(?<name>{Id})(?<suffix>{Suffix})\s*(?<array>\(.*\))?\s*(?:As\s+(?<new>New\s+)?(?<type>.+))?$", Opts)]
  private static partial Regex VariableItem();
  [GeneratedRegex($@"^(?<name>{Id})(?<suffix>{Suffix})\s*(?:As\s+(?<type>[^=]+?))?\s*=\s*(?<value>.+)$", Opts)]
  private static partial Regex ConstItem();
  [GeneratedRegex($@"^(?:(?<name>{Id})|\[(?<bracketed>[^\]]+)\])\s*(?:=\s*(?<value>.+))?$", Opts)]
  private static partial Regex EnumItem();

  public static ModuleModel Parse(string name, VbaModuleKind kind, string text)
  {
    var lines = LogicalLineReader.Read(text);
    var state = new ParserState();

    // Files exported by the VBA editor start with a header (VERSION 1.0 CLASS ... END, or a form's
    // Begin ... End designer block). Skip it; it ends where the Attribute VB_Name line starts.
    int first = 0;
    if (lines.Count > 0 && lines[0].Code.StartsWith("VERSION ", StringComparison.OrdinalIgnoreCase))
      while (first < lines.Count && !lines[first].Code.StartsWith("Attribute VB_Name", StringComparison.OrdinalIgnoreCase)) first++;

    for (int i = first; i < lines.Count; i++)
      state.Handle(lines[i]);

    int lineCount = lines.Count == 0 ? 0 : lines[^1].LastLine;
    state.Finish(lineCount);

    return new ModuleModel
    {
      Name = name,
      Kind = kind,
      Members = state.Members,
      Implements = state.Implements,
      Attributes = state.Attributes,
      Options = state.Options,
      Folder = state.Folder,
      LineCount = lineCount,
      SourceLines = LogicalLineReader.PhysicalLines(text),
      Warnings = state.Warnings,
    };
  }

  /// <summary>The parser's position in the module: at module level, inside a procedure, or inside an Enum/Type block.</summary>
  sealed class ParserState
  {
    public List<Member> Members { get; } = [];
    public List<string> Implements { get; } = [];
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Options { get; } = [];
    public List<string> Warnings { get; } = [];
    public string? Folder { get; private set; }

    Member? _procedure;          // the procedure we're inside, if any
    readonly Dictionary<Member, BlockTracker> _blocks = []; // each procedure's blocks, followed as its lines are read

    BlockTracker BlocksOf(Member procedure) => _blocks.TryGetValue(procedure, out var tracker) ? tracker : _blocks[procedure] = new BlockTracker(procedure);
    Member? _block;              // the Enum or Type we're inside, if any
    int? _commentBlockStart;    // first line of the run of comment lines directly above the current line
    bool _sawProcedure;          // whether the declarations have ended

    public void Handle(LogicalLine line)
    {
      if (line.IsBlank) { _commentBlockStart = null; return; }
      if (line.IsCommentOnly)
      {
        // A folder annotation counts only in the declarations, before the first procedure, as in Rubberduck.
        if (Folder == null && !_sawProcedure && FolderAnnotation().Match(line.Comment!) is { Success: true } folder)
          Folder = folder.Groups["path"].Value.Trim();
        _commentBlockStart ??= line.FirstLine;
        return;
      }

      var code = line.Code;
      if (code.StartsWith('#')) { HandleDirective(code, line); return; }

      var attribute = AttributeStatement().Match(code);
      if (attribute.Success)
      {
        // Module attributes (VB_Name, VB_PredeclaredId, ...) are kept. Member attributes such as
        // "Attribute Foo.VB_Description" can appear inside procedures and are skipped.
        if (!attribute.Groups["name"].Value.Contains('.'))
          Attributes[attribute.Groups["name"].Value] = attribute.Groups["value"].Value.Trim().Trim('"');
        return;
      }

      if (_procedure != null) HandleProcedureLine(line);
      else if (_block != null) HandleBlockLine(line);
      else HandleModuleLine(line);

      _commentBlockStart = null;
    }

    public void Finish(int lastLine)
    {
      if (_procedure != null)
      {
        Warnings.Add($"Line {_procedure.DeclarationLine}: {KindText(_procedure.Kind)} {_procedure.Name} has no End line.");
        _procedure.EndLine = lastLine;
      }
      if (_block != null)
      {
        Warnings.Add($"Line {_block.DeclarationLine}: {_block.Kind} {_block.Name} has no End {_block.Kind}.");
        CloseBlock(lastLine);
      }
    }

    // ---- Module level ----

    void HandleModuleLine(LogicalLine line)
    {
      var code = line.Code;
      int start = _commentBlockStart ?? line.FirstLine;
      Match m;

      if (OptionStatement().IsMatch(code)) { Options.Add(code); return; }
      if (DefTypeStatement().IsMatch(code)) return;

      if ((m = ProcedureStart().Match(code)).Success) { StartProcedure(m, line, start); return; }

      if ((m = EnumStart().Match(code)).Success) { StartBlock(m, MemberKind.Enum, line, start); return; }
      if ((m = TypeStart().Match(code)).Success) { StartBlock(m, MemberKind.Type, line, start); return; }

      if ((m = EventDeclaration().Match(code)).Success)
      {
        var signature = ParseSignature(m.Groups["rest"].Value, line);
        Add(new Member
        {
          Name = m.Groups["name"].Value,
          Kind = MemberKind.Event,
          Visibility = Visibility.Public,
          Parameters = signature.Parameters,
          DeclarationLine = line.FirstLine,
          StartLine = start,
          EndLine = line.LastLine,
        });
        return;
      }

      if ((m = DeclareStatement().Match(code)).Success)
      {
        var signature = ParseSignature(m.Groups["rest"].Value, line);
        bool isFunction = m.Groups["kind"].Value.Equals("Function", StringComparison.OrdinalIgnoreCase);
        Add(new Member
        {
          Name = m.Groups["name"].Value,
          Kind = MemberKind.Declare,
          Visibility = ParseVisibility(m.Groups["vis"].Value, Visibility.Public),
          IsFunctionDeclare = isFunction,
          Type = signature.ReturnType ?? TypeForSuffix(m.Groups["suffix"].Value),
          IsArray = signature.ReturnsArray,
          Library = m.Groups["lib"].Value,
          Alias = m.Groups["alias"].Success ? m.Groups["alias"].Value : null,
          Parameters = signature.Parameters,
          DeclarationLine = line.FirstLine,
          StartLine = start,
          EndLine = line.LastLine,
        });
        return;
      }

      if ((m = ConstDeclaration().Match(code)).Success)
      {
        // Module-level constants are Private unless declared Public.
        var visibility = ParseVisibility(m.Groups["vis"].Value, Visibility.Private);
        foreach (var item in SplitTopLevel(m.Groups["items"].Value))
        {
          var c = ConstItem().Match(item);
          if (!c.Success) { Warnings.Add($"Line {line.FirstLine}: can't read constant '{item}'."); continue; }
          Add(new Member
          {
            Name = c.Groups["name"].Value,
            Kind = MemberKind.Constant,
            Visibility = visibility,
            Type = TypeOrSuffix(c.Groups["type"].Value, c.Groups["suffix"].Value),
            Value = c.Groups["value"].Value.Trim(),
            DeclarationLine = line.FirstLine,
            StartLine = start,
            EndLine = line.LastLine,
          });
        }
        return;
      }

      if ((m = ImplementsStatement().Match(code)).Success) { Implements.Add(m.Groups["name"].Value); return; }

      if ((m = VariableDeclaration().Match(code)).Success)
      {
        // "Dim" at module level is Private; "Global" is Public.
        var visibility = ParseVisibility(m.Groups["keyword"].Value, Visibility.Private);
        foreach (var item in SplitTopLevel(m.Groups["items"].Value))
        {
          var v = VariableItem().Match(item);
          if (!v.Success) { Warnings.Add($"Line {line.FirstLine}: can't read variable '{item}'."); continue; }
          Add(new Member
          {
            Name = v.Groups["name"].Value,
            Kind = MemberKind.Variable,
            Visibility = visibility,
            Type = TypeOrSuffix(v.Groups["type"].Value, v.Groups["suffix"].Value),
            IsArray = v.Groups["array"].Success,
            IsWithEvents = v.Groups["withEvents"].Success,
            IsAutoNew = v.Groups["new"].Success,
            DeclarationLine = line.FirstLine,
            StartLine = start,
            EndLine = line.LastLine,
          });
        }
        return;
      }

      Warnings.Add($"Line {line.FirstLine}: unrecognised module-level line '{code}'.");
    }

    void StartBlock(Match m, MemberKind kind, LogicalLine line, int start)
    {
      _block = new Member
      {
        Name = m.Groups["name"].Value,
        Kind = kind,
        Visibility = ParseVisibility(m.Groups["vis"].Value, Visibility.Public),
        DeclarationLine = line.FirstLine,
        StartLine = start,
      };
      Members.Add(_block);
      Track(_block);
    }

    static MemberKind ProcedureKind(Match m) => m.Groups["kind"].Value.ToLowerInvariant() switch
    {
      "sub" => MemberKind.Sub,
      "function" => MemberKind.Function,
      var k when k.EndsWith("get") => MemberKind.PropertyGet,
      var k when k.EndsWith("let") => MemberKind.PropertyLet,
      _ => MemberKind.PropertySet,
    };

    void StartProcedure(Match m, LogicalLine line, int start)
    {
      var kind = ProcedureKind(m);
      var signature = ParseSignature(m.Groups["rest"].Value, line);
      var type = signature.ReturnType ?? TypeForSuffix(m.Groups["suffix"].Value);
      _sawProcedure = true;

      // Another complete version of a procedure, in another branch of #If: its body adds to the first version's.
      var earlier = EarlierVersion(m.Groups["name"].Value, kind);
      if (earlier != null)
      {
        Fold(earlier, type, signature.Parameters, line.LastLine);
        _procedure = earlier;
        return;
      }

      _procedure = new Member
      {
        Name = m.Groups["name"].Value,
        Kind = kind,
        Visibility = ParseVisibility(m.Groups["vis"].Value, Visibility.Public),
        IsStatic = m.Groups["static"].Success,
        Type = type,
        IsArray = signature.ReturnsArray,
        Parameters = signature.Parameters,
        DeclarationLine = line.FirstLine,
        StartLine = start,
      };
      Members.Add(_procedure);
      Track(_procedure);
    }

    // ---- Inside a procedure ----

    void HandleProcedureLine(LogicalLine line)
    {
      var proc = _procedure!;
      var code = line.Code;

      var end = ProcedureEnd().Match(code);
      if (end.Success)
      {
        var expected = proc.IsProperty ? "Property" : proc.Kind.ToString();
        if (!end.Groups["kind"].Value.Equals(expected, StringComparison.OrdinalIgnoreCase))
          Warnings.Add($"Line {line.FirstLine}: '{code}' closes {KindText(proc.Kind)} {proc.Name}.");
        proc.EndLine = line.LastLine;
        _procedure = null;
        return;
      }

      // A procedure can't contain another one, so another header means the End line is missing, unless it's another
      // version of this procedure's header under #If.
      var header = ProcedureStart().Match(code);
      if (header.Success && IsAnotherHeader(proc, header))
      {
        var signature = ParseSignature(header.Groups["rest"].Value, line);
        Fold(proc, signature.ReturnType ?? TypeForSuffix(header.Groups["suffix"].Value), signature.Parameters, line.LastLine);
        return;
      }
      if (header.Success)
      {
        Warnings.Add($"Line {proc.DeclarationLine}: {KindText(proc.Kind)} {proc.Name} has no End line.");
        proc.EndLine = line.FirstLine - 1;
        _procedure = null;
        HandleModuleLine(line);
        return;
      }

      // A label ("ErrHandler:") or line number ("10 ") at the start of a line isn't a statement.
      // Keywords such as "Else:" look like labels but aren't.
      var label = LineLabel().Match(code);
      if (label.Success && !NotLabels.Contains(label.Value.TrimEnd().TrimEnd(':')))
      {
        BlocksOf(proc).Label(label.Value.TrimEnd().TrimEnd(':'), line.FirstLine);
        code = code[label.Length..];
      }
      var lineNumber = LineNumber().Match(code);
      if (lineNumber.Success)
      {
        BlocksOf(proc).Label(lineNumber.Value.Trim(), line.FirstLine);
        code = code[lineNumber.Length..];
      }

      foreach (var (statement, block, opens) in BlocksOf(proc).Statements(code, line.FirstLine))
      {
        proc.BodyList.Add(new Statement(statement, line.FirstLine) { ContinuedLines = line.LastLine - line.FirstLine, Block = block, Opens = opens });
        var declaration = LocalDeclaration().Match(statement);
        if (declaration.Success) AddLocals(proc, declaration, line.FirstLine);
      }
    }

    void AddLocals(Member proc, Match declaration, int lineNumber)
    {
      var keyword = declaration.Groups["keyword"].Value;
      bool isConst = keyword.Equals("Const", StringComparison.OrdinalIgnoreCase);
      bool isReDim = keyword.StartsWith("ReDim", StringComparison.OrdinalIgnoreCase);

      foreach (var item in SplitTopLevel(declaration.Groups["items"].Value))
      {
        if (isConst)
        {
          var c = ConstItem().Match(item);
          if (c.Success)
            proc.LocalsList.Add(new LocalVariable(c.Groups["name"].Value, TypeOrSuffix(c.Groups["type"].Value, c.Groups["suffix"].Value), false, true, lineNumber));
          continue;
        }

        var v = VariableItem().Match(item);
        if (!v.Success) continue;
        var name = v.Groups["name"].Value;

        // ReDim normally resizes an existing array (a local or a parameter); it only declares one if the name is new.
        if (isReDim && (!v.Groups["type"].Success || IsDeclaredIn(proc, name))) continue;

        proc.LocalsList.Add(new LocalVariable(name, TypeOrSuffix(v.Groups["type"].Value, v.Groups["suffix"].Value),
            v.Groups["array"].Success, false, lineNumber));
      }
    }

    static bool IsDeclaredIn(Member proc, string name) =>
        proc.LocalsList.Any(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        || proc.Parameters.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    // ---- Inside an Enum or Type block ----

    void HandleBlockLine(LogicalLine line)
    {
      var block = _block!;
      var code = line.Code;
      var end = BlockEnd().Match(code);
      if (end.Success)
      {
        CloseBlock(line.LastLine);
        return;
      }

      if (block.Kind == MemberKind.Enum)
      {
        var e = EnumItem().Match(code);
        if (!e.Success) { Warnings.Add($"Line {line.FirstLine}: can't read enum value '{code}'."); return; }
        Members.Add(new Member
        {
          Name = e.Groups["name"].Success ? e.Groups["name"].Value : e.Groups["bracketed"].Value,
          Kind = MemberKind.EnumMember,
          Visibility = block.Visibility,
          Type = block.Name,
          Value = e.Groups["value"].Success ? e.Groups["value"].Value.Trim() : null,
          ParentName = block.Name,
          DeclarationLine = line.FirstLine,
          StartLine = _commentBlockStart ?? line.FirstLine,
          EndLine = line.LastLine,
        });
      }
      else
      {
        var f = VariableItem().Match(code);
        if (!f.Success || f.Groups["withEvents"].Success) { Warnings.Add($"Line {line.FirstLine}: can't read Type field '{code}'."); return; }
        block.FieldsList.Add(new TypeField(f.Groups["name"].Value, TypeOrSuffix(f.Groups["type"].Value, f.Groups["suffix"].Value), f.Groups["array"].Success, line.FirstLine));
      }
    }

    void CloseBlock(int endLine)
    {
      _block!.EndLine = endLine;
      _block = null;
    }

    void Add(Member member)
    {
      var earlier = EarlierVersion(member.Name, member.Kind);
      if (earlier != null)
      {
        Fold(earlier, member.Type, member.Parameters, member.EndLine);
        return;
      }
      Members.Add(member);
      Track(member);
    }

    // ---- Conditional compilation ----
    //
    // Every branch of #If … #ElseIf … #Else … #End If is read, so the page documents every version of the code. VBA
    // compiles one branch and won't accept a name declared twice, so a name declared again with the same kind (and so
    // the same Get, Let or Set), when either declaration is under #If, is the same member: the later declarations are
    // folded into the first, which then spans from the #If above it to the #End If (or the End line) after the last.
    // Enums and Types aren't folded. #Const lines are skipped.

    sealed class IfBlock(int line)
    {
      public int Line { get; } = line;
      public int Branch { get; set; }
    }

    readonly List<IfBlock> _ifs = [];                                     // the #If blocks open now, innermost last
    readonly Dictionary<Member, (IfBlock Block, int Branch)> _under = []; // where each member under #If was declared
    readonly List<(Member Member, IfBlock Block)> _toEndIf = [];          // folded members to stretch to a block's #End If

    void HandleDirective(string code, LogicalLine line)
    {
      if (DirectiveIf().IsMatch(code))
      {
        _ifs.Add(new IfBlock(line.FirstLine));
        if (_procedure != null) BlocksOf(_procedure).DirectiveIf();
      }
      else if (DirectiveElse().IsMatch(code))
      {
        if (_ifs.Count > 0) _ifs[^1].Branch++;
        if (_procedure != null) BlocksOf(_procedure).DirectiveElse();
      }
      else if (DirectiveEndIf().IsMatch(code) && _ifs.Count > 0)
      {
        if (_procedure != null) BlocksOf(_procedure).DirectiveEndIf();
        var block = _ifs[^1];
        _ifs.RemoveAt(_ifs.Count - 1);
        foreach (var entry in _toEndIf.Where(e => e.Block == block).ToList())
        {
          // A procedure still open ends at its End line, after this.
          if (entry.Member != _procedure) entry.Member.EndLine = Math.Max(entry.Member.EndLine, line.LastLine);
          _toEndIf.Remove(entry);
        }
      }
    }

    void Track(Member member)
    {
      if (_ifs.Count > 0) _under[member] = (_ifs[^1], _ifs[^1].Branch);
    }

    /// <summary>The earlier member a declaration is another version of, or null.</summary>
    Member? EarlierVersion(string name, MemberKind kind)
    {
      if (kind is MemberKind.Enum or MemberKind.Type or MemberKind.EnumMember) return null;
      return Members.FirstOrDefault(m => m.Kind == kind && m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
          && (_ifs.Count > 0 || _under.ContainsKey(m)));
    }

    /// <summary>Folds another version into an earlier member: its type and parameters, and its lines.</summary>
    void Fold(Member earlier, string? type, IReadOnlyList<Parameter> parameters, int endLine)
    {
      earlier.AddVersion(type, parameters);
      if (_under.TryGetValue(earlier, out var first)) earlier.StartNoLaterThan(first.Block.Line);
      earlier.EndLine = Math.Max(earlier.EndLine, endLine);
      if (_ifs.Count > 0) _toEndIf.Add((earlier, _ifs[^1]));
    }

    /// <summary>
    /// Whether a procedure header inside an open procedure is another version of its header: the same name and kind, in
    /// a later branch of the #If its header is in, before any of the body. Early and late binding are often written so.
    /// </summary>
    bool IsAnotherHeader(Member proc, Match header) =>
        proc.Body.Count == 0
        && header.Groups["name"].Value.Equals(proc.Name, StringComparison.OrdinalIgnoreCase)
        && ProcedureKind(header) == proc.Kind
        && _under.TryGetValue(proc, out var at) && _ifs.Contains(at.Block) && at.Block.Branch != at.Branch;

    // ---- Signatures ----

    sealed record Signature(List<Parameter> Parameters, string? ReturnType, bool ReturnsArray);

    /// <summary>Parses "(parameters) As Type" after a procedure, Event or Declare name.</summary>
    Signature ParseSignature(string rest, LogicalLine line)
    {
      rest = rest.Trim();
      var parameters = new List<Parameter>();
      if (rest.StartsWith('('))
      {
        int close = MatchingParenthesis(rest, 0);
        if (close < 0)
        {
          Warnings.Add($"Line {line.FirstLine}: unbalanced parentheses in '{line.Code}'.");
          return new Signature(parameters, null, false);
        }
        foreach (var item in SplitTopLevel(rest[1..close]))
        {
          var p = ParameterItem().Match(item);
          if (!p.Success) { Warnings.Add($"Line {line.FirstLine}: can't read parameter '{item}'."); continue; }
          parameters.Add(new Parameter(
              p.Groups["name"].Value,
              TypeOrSuffix(p.Groups["type"].Value, p.Groups["suffix"].Value),
              IsOptional: p.Groups["optional"].Success,
              IsByVal: p.Groups["by"].Value.Equals("ByVal", StringComparison.OrdinalIgnoreCase),
              IsParamArray: p.Groups["paramArray"].Success,
              IsArray: p.Groups["array"].Success || p.Groups["paramArray"].Success,
              DefaultValue: p.Groups["default"].Success ? p.Groups["default"].Value.Trim() : null));
        }
        rest = rest[(close + 1)..].Trim();
      }

      string? returnType = null;
      bool returnsArray = false;
      if (rest.StartsWith("As ", StringComparison.OrdinalIgnoreCase))
      {
        returnType = rest[3..].Trim();
        if (returnType.EndsWith("()")) { returnsArray = true; returnType = returnType[..^2].TrimEnd(); }
      }
      else if (rest.Length > 0)
      {
        Warnings.Add($"Line {line.FirstLine}: unexpected '{rest}' in '{line.Code}'.");
      }
      return new Signature(parameters, returnType, returnsArray);
    }
  }

  // ---- Helpers ----

  static Visibility ParseVisibility(string keyword, Visibility defaultVisibility) => keyword.ToLowerInvariant() switch
  {
    "public" or "global" => Visibility.Public,
    "private" or "dim" => Visibility.Private,
    "friend" => Visibility.Friend,
    _ => defaultVisibility,
  };

  static string KindText(MemberKind kind) => kind switch
  {
    MemberKind.PropertyGet => "Property Get",
    MemberKind.PropertyLet => "Property Let",
    MemberKind.PropertySet => "Property Set",
    _ => kind.ToString(),
  };

  static string? TypeOrSuffix(string asType, string suffix) =>
      asType.Trim().Length > 0 ? asType.Trim() : TypeForSuffix(suffix);

  /// <summary>Maps a type-declaration character to its type: Dim s$ is a String, n% an Integer, and so on.</summary>
  public static string? TypeForSuffix(string suffix) => suffix switch
  {
    "%" => "Integer",
    "&" => "Long",
    "^" => "LongLong",
    "!" => "Single",
    "#" => "Double",
    "@" => "Currency",
    "$" => "String",
    _ => null,
  };

  /// <summary>Splits at commas that are not inside parentheses or literals.</summary>
  public static List<string> SplitTopLevel(string text)
  {
    var parts = new List<string>();
    int depth = 0, start = 0;
    for (int i = 0; i < text.Length; i++)
    {
      int literalEnd = LogicalLineReader.LiteralEnd(text, i);
      if (literalEnd > i) { i = literalEnd - 1; continue; }

      if (text[i] == '(') depth++;
      else if (text[i] == ')') depth--;
      else if (text[i] == ',' && depth == 0) { parts.Add(text[start..i]); start = i + 1; }
    }
    parts.Add(text[start..]);
    return parts.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
  }

  /// <summary>Index of the ")" matching the "(" at <paramref name="open"/>, or -1.</summary>
  static int MatchingParenthesis(string text, int open)
  {
    int depth = 0;
    for (int i = open; i < text.Length; i++)
    {
      int literalEnd = LogicalLineReader.LiteralEnd(text, i);
      if (literalEnd > i) { i = literalEnd - 1; continue; }

      if (text[i] == '(') depth++;
      else if (text[i] == ')' && --depth == 0) return i;
    }
    return -1;
  }
}
