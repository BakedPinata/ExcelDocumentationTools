let
    Source = SpikeBase,
    Doubled = Table.AddColumn(Source, "Doubled", each fnSpikeDouble([Amount]), type number)
in
    Doubled
