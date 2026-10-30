Attribute VB_Name = "Logger"
Option Explicit

Public Sub LogInfo(ByVal msg As String)
    WriteLine "INFO", msg
End Sub

Public Sub LogError(ByVal msg As String)
    WriteLine "ERROR", msg
End Sub

Private Sub WriteLine(ByVal level As String, ByVal msg As String)
    Debug.Print Format(Now, "hh:nn:ss") & " [" & level & "] " & msg ' don't log: LogInfo
End Sub
