Attribute VB_Name = "Sheet1"
Attribute VB_Base = "0{00020820-0000-0000-C000-000000000046}"
Option Explicit

Private Sub Worksheet_Change(ByVal Target As Range)
    If Not Intersect(Target, Me.Range("A:A")) Is Nothing Then
        Logger.LogInfo "Order column changed: " & Target.Address
        Main.ProcessOrders
    End If
End Sub

Private Sub btnRun_Click()
    ProcessOrders
End Sub
