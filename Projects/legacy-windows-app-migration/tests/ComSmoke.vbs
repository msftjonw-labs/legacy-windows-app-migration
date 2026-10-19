Option Explicit
Dim engine, actual
Set engine = CreateObject("LegacyOrders.PricingEngine")
actual = engine.CalculateLineTotal("1250.00", 2, "10")
If actual <> "2250.00" Then
    WScript.Echo "FAIL: expected 2250.00, got " & actual
    WScript.Quit 1
End If
WScript.Echo "PASS: native Windows Script Host activated x86 COM pricing through IDispatch."
Set engine = Nothing
