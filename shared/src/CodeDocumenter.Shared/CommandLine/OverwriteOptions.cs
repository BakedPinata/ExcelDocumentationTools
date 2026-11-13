using System.CommandLine;
using System.CommandLine.Parsing;
using CodeDocumenter.Shared.Files;

namespace CodeDocumenter.Shared.CommandLine;

/// <summary>
/// <c>--on-exists</c> and <c>--force</c> (<c>-y</c>), the same in every tool: what to do when an output file already
/// exists. The default asks when the session is interactive and fails when it isn't (see <see cref="OverwriteResolver"/>).
/// </summary>
public sealed class OverwriteOptions
{
  /// <summary>Creates both options.</summary>
  /// <param name="existingOutput">What may already exist, for the help text, such as "an output file" or "the page".</param>
  /// <param name="existingOutputs">The same, for overwriting, such as "existing output files" or "an existing page".</param>
  public OverwriteOptions(string existingOutput, string existingOutputs)
  {
    OnExists = new Option<OnExistsBehaviour?>("--on-exists")
    {
      Description = $"What to do when {existingOutput} already exists: prompt, overwrite, skip or fail. "
        + "[default: prompt when interactive, otherwise fail]",
      HelpName = "behaviour",
      CustomParser = Parse,
    };

    Force = new Option<bool>("--force", "-y")
    {
      Description = $"Overwrite {existingOutputs} without asking. Shorthand for --on-exists overwrite.",
    };
  }

  /// <summary><c>--on-exists</c>.</summary>
  public Option<OnExistsBehaviour?> OnExists { get; }

  /// <summary><c>--force</c> / <c>-y</c>.</summary>
  public Option<bool> Force { get; }

  /// <summary>Reports <c>--force</c> with any <c>--on-exists</c> but overwrite. Call it from the command's validator.</summary>
  public void Validate(CommandResult result)
  {
    ArgumentNullException.ThrowIfNull(result);

    // Inside a command validator, GetValue throws for options whose own parser reports an error, so --on-exists is
    // read from its raw token.
    var onExistsResult = result.GetResult(OnExists);
    if (result.GetValue(Force)
      && onExistsResult is { Implicit: false, Tokens.Count: > 0 }
      && TryParse(onExistsResult.Tokens[^1].Value, out var onExists)
      && onExists != OnExistsBehaviour.Overwrite)
    {
      result.AddError($"--force (-y) means --on-exists overwrite, which conflicts with --on-exists {onExists.ToString().ToLowerInvariant()}. "
        + "Use only one of them.");
    }
  }

  /// <summary>The behaviour asked for: overwrite with <c>--force</c>, <c>--on-exists</c> if given, otherwise prompt.</summary>
  public OnExistsBehaviour Bind(ParseResult parseResult)
  {
    ArgumentNullException.ThrowIfNull(parseResult);
    return parseResult.GetValue(Force)
      ? OnExistsBehaviour.Overwrite
      : parseResult.GetValue(OnExists) ?? OnExistsBehaviour.Prompt;
  }

  private static bool TryParse(string value, out OnExistsBehaviour behaviour) =>
    Enum.TryParse(value, ignoreCase: true, out behaviour)
    && Enum.IsDefined(behaviour)
    && !int.TryParse(value, out _);

  private static OnExistsBehaviour? Parse(ArgumentResult result)
  {
    var value = result.Tokens.Count > 0 ? result.Tokens[^1].Value : string.Empty;
    if (TryParse(value, out var behaviour))
    {
      return behaviour;
    }

    result.AddError($"Invalid value '{value}' for --on-exists. Valid values are: prompt, overwrite, skip, fail.");
    return null;
  }
}
