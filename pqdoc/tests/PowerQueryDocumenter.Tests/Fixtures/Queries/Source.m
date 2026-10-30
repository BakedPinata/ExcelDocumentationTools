// A query whose name is also a common step name and a word the old analyser treated as built-in.
let
    Raw = Csv.Document(File.Contents("C:\Data\sales.csv"), [Delimiter = ",", Encoding = 65001, QuoteStyle = QuoteStyle.Csv]),
    Promoted = Table.PromoteHeaders(Raw, [PromoteAllScalars = true]),
    Typed = Table.TransformColumnTypes(Promoted, {{"Amount", type number}, {"Date", type date}})
in
    Typed
