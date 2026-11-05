VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
END
Attribute VB_Name = "SpikeCounter"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

Private m_total As Long

Public Sub Add(ByVal amount As Long)
    m_total = m_total + amount
End Sub

Public Property Get Total() As Long
    Total = m_total
End Property
