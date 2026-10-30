Attribute VB_Name = "SheetOrderRepository"
Option Explicit
Implements IOrderRepository

Private Function IOrderRepository_LoadAll() As Collection
    Dim result As New Collection, r As Long
    Dim o As Order
    For r = 2 To Sheet1.Cells(Sheet1.Rows.Count, 1).End(xlUp).Row
        Set o = New Order
        o.Load Sheet1.Rows(r)
        result.Add o
    Next r
    Set IOrderRepository_LoadAll = result
End Function

Private Sub IOrderRepository_Save(ByVal o As Order)
    Logger.LogInfo "Saved order " & o.Id & " to sheet"
End Sub
