Attribute VB_Name = "Utils"
Option Explicit

Public Function FormatMoney(ByVal amount As Double) As String
    FormatMoney = Format(amount, "$#,##0.00")
End Function

Private Sub UnusedHelper()
    Logger.LogInfo "never called"
End Sub
