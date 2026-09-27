namespace G7Bridge.Core;

public static class GameInputErrors
{
    public static bool IsDisconnected(int result)=>(uint)result is 0x838A0001 or 0x838A0008 or 0x8007048F;

    public static string RecoveryHint(int result,bool runtimeOperation=false)=>(uint)result switch {
        0x838A0002=>"The selected controller was not found in this Windows session. Turn it on and reopen G7 Bridge.",
        0x838A0001 or 0x838A0008 or 0x8007048F=>"The physical controller disappeared or changed mode.",
        0x80004002 or 0x8007007E or 0x8007007F when runtimeOperation=>"The required GameInput runtime or API is unavailable. Reinstall G7 Bridge and reopen the app.",
        0x80004002=>"Windows does not offer the requested interface for this device. Save diagnostics.",
        0x80070490=>"Windows does not offer the requested additional device channel or report. Save diagnostics.",
        0x80070034=>"The additional channel cannot be uniquely matched to the selected controller. Save diagnostics.",
        0x80070005=>"Windows denied access to this channel. Save diagnostics with this error code.",
        0x838A0007 or 0x838A000C=>"Windows does not offer this input or output feature for the selected device. Save diagnostics.",
        _=>"Save diagnostics including this error code to identify the failed Windows operation."
    };
}
