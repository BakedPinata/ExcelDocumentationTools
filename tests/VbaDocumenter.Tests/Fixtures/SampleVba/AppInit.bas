Attribute VB_Name = "AppInit"
Option Explicit

Public g_UseCsv As Boolean

Public Sub Startup()
    LoadSettings
    Logger.LogInfo "Application started"
End Sub

Private Sub LoadSettings()
    g_UseCsv = (Sheet1.Range("Z1").Value = "CSV")
    Logger.LogInfo "Settings loaded"
End Sub
