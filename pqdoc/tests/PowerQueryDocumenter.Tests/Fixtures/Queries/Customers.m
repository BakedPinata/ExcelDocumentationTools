let
    Data = Source,
    Customers = Table.SelectRows(Data, each [Type] = "Customer"),
    Renamed = Table.RenameColumns(Customers, {{"Name", "Customer Name"}})
in
    Renamed
