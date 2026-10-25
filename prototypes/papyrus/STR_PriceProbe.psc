Scriptname STR_PriceProbe extends Quest

; Standalone price experiment. Attach to a test quest before running in Skyrim.
; It changes the global base value of one iron ingot and must be restored afterward.
Int OriginalValue = -1

Event OnInit()
    ApplyPercent(70)
    RegisterForSingleUpdate(120.0)
    Debug.Notification("STR price probe active for two minutes")
EndEvent

Event OnUpdate()
    Restore()
    Debug.Notification("STR price probe restored original value")
    Stop()
EndEvent

Function ApplyPercent(Int percent)
    Form ironIngot = Game.GetFormFromFile(0x0005ACE4, "Skyrim.esm")
    If ironIngot == None
        Debug.Trace("STR_PriceProbe: iron ingot not found")
        Return
    EndIf
    If percent < 50 || percent > 200
        Debug.Trace("STR_PriceProbe: percent outside 50..200")
        Return
    EndIf
    If OriginalValue < 0
        OriginalValue = ironIngot.GetGoldValue()
    EndIf
    Int newValue = (OriginalValue * percent + 50) / 100
    If newValue < 1
        newValue = 1
    EndIf
    ironIngot.SetGoldValue(newValue)
    Debug.Trace("STR_PriceProbe: iron ingot value changed to " + newValue)
EndFunction

Function Restore()
    If OriginalValue < 0
        Return
    EndIf
    Form ironIngot = Game.GetFormFromFile(0x0005ACE4, "Skyrim.esm")
    If ironIngot != None
        ironIngot.SetGoldValue(OriginalValue)
    EndIf
    OriginalValue = -1
EndFunction
