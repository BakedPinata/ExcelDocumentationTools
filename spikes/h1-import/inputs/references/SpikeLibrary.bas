Attribute VB_Name = "SpikeLibrary"
Option Explicit

' Scripting.Dictionary is early bound, so this compiles only with the Microsoft Scripting Runtime reference.
Public Sub SpikeReferences()
    Dim items As New Scripting.Dictionary
    items.Add "Apples", 1
    items.Add "Pears", 2
    MsgBox "The reference came through: the dictionary holds " & items.Count & " items (expected 2)." & vbCrLf & _
        "Left(""Library"", 3) = " & Left("Library", 3), vbInformation, "H1 spike"
End Sub
