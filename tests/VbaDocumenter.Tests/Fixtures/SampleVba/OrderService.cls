Attribute VB_Name = "OrderService"
Option Explicit

Public Event Progress(ByVal message As String)

Private m_repo As IOrderRepository
Private m_count As Long

Public Sub Init(ByVal repo As IOrderRepository)
    Set m_repo = repo
End Sub

Public Sub Process(ByVal o As Order)
    If o.IsValid Then
        m_repo.Save o
        m_count = m_count + 1
        RaiseEvent Progress("Processed " & o.Describe)
    ElseIf o.Status = "DRAFT" Then
        o.Status = "HOLD"
    Else
        Logger.LogError "Invalid order " & o.Id
    End If
End Sub

Public Sub Summarise()
    Logger.LogInfo m_count & " orders processed"
    RaiseEvent Progress("Done")
End Sub
