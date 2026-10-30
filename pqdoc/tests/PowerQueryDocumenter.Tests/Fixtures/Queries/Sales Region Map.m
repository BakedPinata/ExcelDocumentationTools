let
    // "Source" here is this query's own step, not the query called Source.
    Source = Excel.CurrentWorkbook(){[Name = "RegionMap"]}[Content],
    #"Changed Type" = Table.TransformColumnTypes(Source, {{"Region", type text}, {"Manager", type text}})
in
    #"Changed Type"
