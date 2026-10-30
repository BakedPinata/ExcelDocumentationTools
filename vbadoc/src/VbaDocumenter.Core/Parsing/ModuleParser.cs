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
      LineCount = lineCount,
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

    Member? _procedure;          // the procedure we're inside, if any
    Member? _block;              // the Enum or Type we're inside, if any
    int? _commentBlockStart;    // first line of the run of comment lines directly above the current line

    public void Handle(LogicalLine line)
    {
      if (line.IsBlank) { _commentBlockStart = null; return; }
      if (line.IsCommentOnly) { _commentBlockStart ??= line.FirstLine; return; }

      var code = line.Code;
      if (code.StartsWith('#')) return; // #If / #Else / #Const: conditional compilation. Both branches are parsed.

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
    }

    void StartProcedure(Match m, LogicalLine line, int start)
    {
      var kind = m.Groups["kind"].Value.ToLowerInvariant() switch
      {
        "sub" => MemberKind.Sub,
        "function" => MemberKind.Function,
        var k when k.EndsWith("get") => MemberKind.PropertyGet,
        var k when k.EndsWith("let") => MemberKind.PropertyLet,
        _ => MemberKind.PropertySet,
      };
      var signature = ParseSignature(m.Groups["rest"].Value, line);
      _procedure = new Member
      {
        Name = m.Groups["name"].Value,
        Kind = kind,
        Visibility = ParseVisibility(m.Groups["vis"].Value, Visibility.Public),
        IsStatic = m.Groups["static"].Success,
        Type = signature.ReturnType ?? TypeForSuffix(m.Groups["suffix"].Value),
        IsArray = signature.ReturnsArray,
        Parameters = signature.Parameters,
        DeclarationLine = line.FirstLine,
        StartLine = start,
      };
      Members.Add(_procedure);
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

      // A procedure can't contain another one, so this means the End line is missing.
      if (ProcedureStart().IsMatch(code))
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
        code = code[label.Length..];
      code = LineNumber().Replace(code, "", 1);

      foreach (var statement in LogicalLineReader.SplitStatements(code))
      {
        proc.BodyList.Add(new Statement(statement, line.FirstLine));
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
        block.FieldsList.Add(new TypeField(f.Groups["name"].Value, TypeOrSuffix(f.Groups["type"].Value, f.Groups["suffix"].Value), f.Groups["array"].Success));
      }
    }

    void CloseBlock(int endLine)
    {
      _block!.EndLine = endLine;
      _block = null;
    }

    void Add(Member member) => Members.Add(member);

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
