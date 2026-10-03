namespace System.Windows.Media.ProGPU.Composition;

/// <summary>Records an owned source shader at its original command-stream position.</summary>
internal interface IWpfOwnedShaderEffectCommandSink
{
    /// <summary>
    /// Success transfers the caller's source ownership through the matching Pop.
    /// Rejection or an exception leaves that ownership with the caller.
    /// </summary>
    bool PushOwnedShaderEffect(
        global::ProGPU.Scene.OwnedShaderEffectSource source,
        WpfReplayRect bounds);
}
