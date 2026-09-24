using System.Runtime.InteropServices;

namespace GameMusicShare.Services;

internal enum SetupOutcome { Attached, RestartRequired, Cancelled }
internal sealed record SetupResult(SetupOutcome Outcome, string Message);

// Pure decisions are tested without installing drivers or touching hardware.
internal static class SetupPolicy
{
    public const string TransportVersion = "0.9.8.0";

    public static string? CompatibilityProblem(Version windows, Architecture operatingSystem, Architecture application)
    {
        if (windows.Major < 10 || (windows.Major == 10 && windows.Build < 22000))
            return "MicWeave needs Windows 11. No system settings have been changed.";
        if (operatingSystem is not (Architecture.X64 or Architecture.Arm64))
            return "This processor is not supported by this MicWeave build.";
        if (application != operatingSystem)
            return "This copy of MicWeave does not match your processor. Use the installer to get the correct version; do not install a different processor's driver.";
        return null;
    }

    public static SetupResult InstallerResult(int exitCode) => exitCode switch
    {
        0 => new(SetupOutcome.Attached, "The microphone component is installed. Connecting it now…"),
        3010 or 1641 => new(SetupOutcome.RestartRequired,
            "Save your work and restart Windows, then reopen MicWeave to finish connecting the microphone. You do not need to run setup again."),
        2 => new(SetupOutcome.Cancelled, "Microphone setup was cancelled. You can try again using the setup button."),
        _ => throw new InvalidOperationException($"The microphone component could not finish installing (code {exitCode}). Restart Windows before trying setup again. Do not disable Windows security protections."),
    };

    public static string TransportHash(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "81f426741f7ee2ed991febe24a22daca8400b6ae2f171054e3fb404897e15d39",
        Architecture.Arm64 => "fa5dab657380ed99d7ed953010aae79ad9be478eddfeb2d72c22309fb48bc35e",
        _ => throw new PlatformNotSupportedException("No microphone component is available for this processor."),
    };
}
