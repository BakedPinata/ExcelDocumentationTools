let
    Merged = Table.NestedJoin(Orders, {"CustomerId"}, Customers, {"Id"}, "Customer", JoinKind.Inner),
    WithQuarter = Table.Combine({Merged, Q1.Summary})
in
    WithQuarter
