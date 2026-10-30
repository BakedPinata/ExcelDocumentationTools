let
    Result = if List.Count(Table.ColumnNames(#"Cycle B")) > 0 then #"Cycle B" else #table({}, {})
in
    Result
