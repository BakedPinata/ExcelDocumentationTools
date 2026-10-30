Attribute VB_Name = "CsvOrderRepository"
Option Explicit
Implements IOrderRepository

Private Function IOrderRepository_LoadAll() As Collection
    Dim lines As Variant, i As Long
    Dim o As Order
    Set IOrderRepository_LoadAll = New Collection
    lines = ReadFile("C:\data\orders.csv")
    Do While i <= UBound(lines)
        Set o = New Order
        o.Parse lines(i)
        IOrderRepository_LoadAll.Add o
        i = i + 1
    Loop
End Function

Private Sub IOrderRepository_Save(ByVal o As Order)
    Logger.LogError "CSV is read-only"
End Sub

Private Function ReadFile(ByVal path As String) As Variant
    ReadFile = Split(CreateObject("Scripting.FileSystemObject").OpenTextFile(path).ReadAll, vbCrLf)
End Function
