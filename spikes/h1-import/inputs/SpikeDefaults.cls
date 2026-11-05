VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
END
Attribute VB_Name = "SpikeDefaults"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Option Explicit

' A predeclared class, used without New, which only works if its attributes came through.
Public Property Get Greeting() As String
Attribute Greeting.VB_Description = "What SpikeHello says first."
    Greeting = "The H1 spike's modules compiled and ran."
End Property
