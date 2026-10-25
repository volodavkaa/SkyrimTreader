Scriptname STR_RegionalPriceProbe extends Quest

; Ten-minute, one-item experiment. Do not save while this quest is running.
Form IronIngot
Location MarkarthCity
Location WhiterunCity
Location RiftenCity
Int OriginalValue = -1
Int AppliedValue = -1
Float StartedAt

Event OnInit()
    IronIngot = Game.GetFormFromFile(0x0005ACE4, "Skyrim.esm")
    MarkarthCity = Game.GetFormFromFile(0x00018A59, "Skyrim.esm") as Location
    WhiterunCity = Game.GetFormFromFile(0x00018A56, "Skyrim.esm") as Location
    RiftenCity = Game.GetFormFromFile(0x00018A58, "Skyrim.esm") as Location
    If IronIngot == None || MarkarthCity == None || WhiterunCity == None || RiftenCity == None
        Debug.Trace("STR regional probe: required form is missing")
        Stop()
        Return
    EndIf
    OriginalValue = IronIngot.GetGoldValue()
    AppliedValue = OriginalValue
    StartedAt = Utility.GetCurrentRealTime()
    Debug.Notification("STR regional price probe active for ten minutes")
    ApplyCurrentRegion()
    RegisterForSingleUpdate(5.0)
EndEvent

Event OnUpdate()
    If OriginalValue < 0
        Return
    EndIf
    If Utility.GetCurrentRealTime() - StartedAt >= 600.0
        Restore()
        Debug.Notification("STR regional price probe finished")
        Stop()
        Return
    EndIf
    ApplyCurrentRegion()
    If OriginalValue >= 0
        RegisterForSingleUpdate(5.0)
    EndIf
EndEvent

Function ApplyCurrentRegion()
    Int desired = OriginalValue
    Actor player = Game.GetPlayer()
    If player.IsInLocation(MarkarthCity)
        desired = (OriginalValue * 70 + 50) / 100
    ElseIf player.IsInLocation(RiftenCity)
        desired = (OriginalValue * 130 + 50) / 100
    ElseIf player.IsInLocation(WhiterunCity)
        desired = OriginalValue
    EndIf
    If desired < 1
        desired = 1
    EndIf

    Int current = IronIngot.GetGoldValue()
    If current != AppliedValue && current != OriginalValue
        Debug.Trace("STR regional probe: external value change detected; stopping")
        OriginalValue = -1
        Stop()
        Return
    EndIf
    If current != desired
        IronIngot.SetGoldValue(desired)
        Debug.Trace("STR regional probe: iron ingot " + current + " -> " + desired)
    EndIf
    AppliedValue = desired
EndFunction

Function Restore()
    If OriginalValue < 0
        Return
    EndIf
    If IronIngot.GetGoldValue() == AppliedValue
        IronIngot.SetGoldValue(OriginalValue)
    Else
        Debug.Trace("STR regional probe: skipped restore after external value change")
    EndIf
    OriginalValue = -1
EndFunction
