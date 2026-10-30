let
    // "Q1.Summary" below is a text value and Orders here is in a comment: neither is a dependency.
    Report = #"Sales Report",
    Label = "Q1.Summary",
    Margin = Table.AddColumn(Report, "Margin", each [Amount] * 0.2, type number)
in
    Margin
