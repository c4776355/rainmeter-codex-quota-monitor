; =============================================================================
; CODEX QUOTA MONITOR - DYNAMIC GLACIER 1.4.1
; Glacier is now 380 x 248 px and task-aware. Aurora / Blueprint remain the
; compact 1.2 concepts. Save this file, then refresh the selected skin.
; =============================================================================

[Variables]

RefreshSeconds=300
; Glacier only: remote queries run at this cadence while a Codex task is active.
; Idle mode performs no remote quota queries.
ActiveRefreshSeconds=20
CommandTimeoutMs=12000

; Windows 11 system typeface with an Apple-like neutral appearance.
; Its zero is unslashed. Change both values to any installed typeface.
FontDisplay=Segoe UI Variable Display
FontText=Segoe UI Variable Text
TitleText=CODEX QUOTA

; --- Concept A: Glacier Glass ------------------------------------------------
GlacierBackgroundA=84,201,250,166
GlacierBackgroundB=34,145,229,176
GlacierBackgroundC=99,225,255,160
GlacierAccentA=166,247,255,245
GlacierAccentB=72,190,255,245
GlacierAccentC=132,154,255,225
GlacierInner=202,250,255,70
GlacierGrid=181,239,255,48
GlacierGridFaint=191,243,255,24
GlacierGlow=91,223,255,76
GlacierTrack=13,75,135,118
GlacierTrackBorder=204,248,255,75

; --- Concept B: Aurora Pulse -------------------------------------------------
AuroraBackgroundA=107,221,255,158
AuroraBackgroundB=40,145,235,174
AuroraBackgroundC=109,139,255,164
AuroraAccentA=176,255,250,250
AuroraAccentB=75,203,255,248
AuroraAccentC=164,137,255,238
AuroraInner=198,249,255,65
AuroraGrid=190,246,255,52
AuroraGridFaint=187,230,255,24
AuroraGlowA=86,241,255,88
AuroraGlowB=147,107,255,66
AuroraTrack=20,70,145,110
AuroraTrackBorder=205,248,255,74

; --- Concept C: Blueprint HUD -----------------------------------------------
BlueprintBackgroundA=49,171,237,148
BlueprintBackgroundB=30,111,207,164
BlueprintBackgroundC=87,208,246,148
BlueprintAccentA=154,244,255,248
BlueprintAccentB=61,187,255,245
BlueprintAccentC=113,149,255,232
BlueprintInner=174,241,255,68
BlueprintGrid=154,230,255,54
BlueprintGridFaint=170,235,255,28
BlueprintGlow=66,218,255,62
BlueprintTrack=13,70,126,108
BlueprintTrackBorder=184,241,255,74

; --- Shared text and runtime colors -----------------------------------------
ColorTextPrimary=247,253,255,255
ColorTextSecondary=213,244,255,255
ColorTextMuted=164,221,243,242
ColorTextFaint=128,190,220,220
ColorOnline=105,255,240,255
ColorSync=168,216,255,255
ColorWarningA=255,221,111,255
ColorWarningB=255,158,75,255
ColorCriticalA=255,132,157,255
ColorCriticalB=255,70,112,255
ColorOffline=255,116,145,255
ColorSilent=128,190,220,220
