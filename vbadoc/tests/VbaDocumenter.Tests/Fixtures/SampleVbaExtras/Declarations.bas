Attribute VB_Name = "Declarations"
Option Explicit
Option Private Module

' Settings shared by the whole project.
Public Const APP_NAME As String = "Order Tool", APP_VERSION = 2
Private Const MAX_ROWS& = 10000
Const DEFAULT_SEP = ","

Public Enum OrderStatus
    osNew = 0
    ' Waiting for approval.
    osHold
    osDone = 2
    [_Last]
End Enum

Private Type OrderRow
    Id As String * 10
    Amount As Currency
    Tags() As String
End Type

#If VBA7 Then
Private Declare PtrSafe Function GetTickCount Lib "kernel32" () As Long
#Else
Private Declare Function GetTickCount Lib "kernel32" () As Long
#End If
Public Declare PtrSafe Sub Sleep Lib "kernel32" Alias "Sleep" (ByVal dwMilliseconds As Long)

Public g_LastError$, g_Count%
Private m_Rows() As OrderRow

Rem Builds a label for an order.
Rem Uses the separator constant.
Public Function BuildLabel$(ByVal id As String, _
                            Optional ByVal status As OrderStatus = osNew, _
                            Optional ByVal sep As String = DEFAULT_SEP)
    BuildLabel = APP_NAME & sep & id & sep & CStr(status)
End Function

Public Static Sub CountCalls()
    Dim calls As Long
    calls = calls + 1: g_Count = calls
End Sub

Public Function Total(ParamArray amounts() As Variant) As Double
    Dim i As Long, sum As Double
    For i = LBound(amounts) To UBound(amounts): sum = sum + amounts(i): Next i
    Total = sum
End Function

Private Sub FillRows(ByRef rows() As OrderRow, ByVal count&)
    ReDim rows(1 To count) As OrderRow
    Dim started As Date: started = #12:30:00 PM#
    rows(1).Id = "A:1": rows(1).Amount = 0 ' a colon inside a string is not a separator
    If count > MAX_ROWS Then GoTo TooMany
    Exit Sub
TooMany:
10  Sleep GetTickCount Mod 10
End Sub
