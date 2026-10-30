let
    Data = Source,
    /* Customers is mentioned in this block comment
       and must not become a dependency. */
    Cleaned = Table.TransformColumns(Data, {{"Region", fnCleanText, type text}}),
    Joined = Table.NestedJoin(Cleaned, {"Region"}, #"Sales Region Map", {"Region"}, "Map", JoinKind.LeftOuter),
    Note = "See the Customers query for details" // A string literal is not a dependency either.
in
    Joined
