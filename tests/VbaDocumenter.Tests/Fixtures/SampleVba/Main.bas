Attribute VB_Name = "Main"
Option Explicit

Public Sub ProcessOrders()
    Dim svc As OrderService
    Dim repo As IOrderRepository
    Dim reporter As ProgressReporter
    Dim o As Order
    Dim legacy As Object

    On Error GoTo ErrHandler
    Set svc = New OrderService
    If g_UseCsv Then
        Set repo = New CsvOrderRepository
    Else
        Set repo = New SheetOrderRepository
    End If
    svc.Init repo
    Set reporter = New ProgressReporter: reporter.Attach svc

    For Each o In repo.LoadAll
        Select Case o.Status
            Case "NEW"
                svc.Process o
            Case "HOLD"
                Logger.LogInfo "Skipped held order " & o.Id
            Case Else
                ' nothing
        End Select
    Next o

    With svc
        .Summarise
    End With

    Set legacy = CreateObject("Legacy.Exporter")
    legacy.ExportAll repo:=Nothing
    Debug.Print Factorial(5)
    Exit Sub
ErrHandler:
    Logger.LogError "ProcessOrders failed: " & Err.Description
End Sub

Public Function Factorial(ByVal n As Long) As Long
    If n <= 1 Then Factorial = 1 Else Factorial = n * Factorial(n - 1)
End Function
