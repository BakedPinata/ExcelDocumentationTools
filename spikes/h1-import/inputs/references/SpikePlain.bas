Attribute VB_Name = "SpikePlain"
Option Explicit

' Uses no library but VBA's own, to show what a missing reference does to code that doesn't need it.
Public Sub SpikeNoLibrary()
    MsgBox "This ran. Left(""Library"", 3) = " & Left("Library", 3) & ", Format(3.14159, ""0.00"") = " & Format(3.14159, "0.00"), vbInformation, "H1 spike"
End Sub
