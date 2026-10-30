(input as nullable text) as nullable text =>
    let
        Trimmed = Text.Trim(input),
        Cleaned = Text.Clean(Trimmed)
    in
        Cleaned
