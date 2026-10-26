Attribute VB_Name = "Order"
Option Explicit

Private m_Id As String
Private m_Status As String
Private m_Amount As Double

Private Sub Class_Initialize()
    m_Status = "NEW"
End Sub

Public Property Get Id() As String
    Id = m_Id
End Property

Public Property Get Status() As String
    Status = m_Status
End Property

Public Property Let Status(ByVal value As String)
    m_Status = value
End Property

Public Sub Load(ByVal row As Range)
    m_Id = row.Cells(1, 1).Value: m_Amount = row.Cells(1, 3).Value
End Sub

Public Sub Parse(ByVal line As String)
    Dim parts() As String
    parts = Split(line, ",")
    m_Id = parts(0)
End Sub

Public Function IsValid() As Boolean
    IsValid = (m_Id <> "" And m_Amount > 0)
End Function

Public Function Describe() As String
    Describe = m_Id & " " & Utils.FormatMoney(m_Amount)
End Function
