Attribute VB_Name = "ProgressReporter"
Option Explicit

Private WithEvents m_svc As OrderService
Attribute m_svc.VB_VarHelpID = -1

Public Sub Attach(ByVal svc As OrderService)
    Set m_svc = svc
End Sub

Private Sub m_svc_Progress(ByVal message As String)
    Application.StatusBar = message
    Logger.LogInfo message
End Sub
