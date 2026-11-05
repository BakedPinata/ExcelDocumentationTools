Attribute VB_Name = "SpikeTools"
Option Explicit

' Written into the workbook by the H1 spike as source only, with no compiled code, so Excel compiles it on opening.

' Usable from a cell: =SpikeDouble(21) gives 42.
Public Function SpikeDouble(ByVal x As Double) As Double
    SpikeDouble = x * 2
End Function

' Uses a class (SpikeCounter) and a predeclared class (SpikeDefaults), so all three must compile together.
Public Sub SpikeHello()
    Dim counter As New SpikeCounter
    counter.Add 2
    counter.Add 3
    MsgBox SpikeDefaults.Greeting & vbCrLf & vbCrLf & _
        "SpikeDouble(21) = " & SpikeDouble(21) & vbCrLf & _
        "The counter holds " & counter.Total & " (expected 5)." & vbCrLf & _
        "Accented text: café, Zürich.", vbInformation, "H1 spike"
End Sub
