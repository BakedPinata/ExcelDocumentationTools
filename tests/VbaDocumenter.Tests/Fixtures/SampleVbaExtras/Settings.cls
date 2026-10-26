VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
END
Attribute VB_Name = "Settings"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

Private m_Target As Object
Private m_Values As New Collection

' The worksheet the settings are written to.
' Nothing until a target is set.
Public Property Get Target() As Object
Attribute Target.VB_Description = "The worksheet the settings are written to."
    Set Target = m_Target
End Property

Public Property Set Target(ByVal value As Object)
    Set m_Target = value
End Property

Public Property Get Item(ByVal key As String) As Variant
Attribute Item.VB_UserMemId = 0
    Item = m_Values(key)
End Property

Public Property Let Item(ByVal key As String, ByVal value As Variant)
    m_Values.Add value, key
End Property

Friend Sub Reset()
    Set m_Values = New Collection
End Sub

Private Sub Class_Initialize()
    Reset
End Sub
