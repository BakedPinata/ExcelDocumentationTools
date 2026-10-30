let
    Grouped = Table.Group(Orders, {"Region"}, {{"Total", each List.Sum([Amount]), type number}})
in
    Grouped
