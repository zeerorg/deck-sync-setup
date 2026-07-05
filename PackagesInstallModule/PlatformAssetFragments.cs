using System.Runtime.InteropServices;

/// <summary>
/// Platform-specific asset name fragments used by the release asset install
/// module to identify the correct archive asset for the current host. Each
/// property holds an array of name fragments in descending preference order —
/// the module picks the first release asset whose name contains any fragment
/// from the array.
/// </summary>
/// <param name="WindowsX64">Fragments for Windows on x64 (e.g. <c>["windows-amd64"]</c>).</param>
/// <param name="WindowsArm64">Fragments for Windows on ARM64 (e.g. <c>["windows-arm64", "windows-amd64"]</c>).</param>
/// <param name="LinuxX64">Fragments for Linux on x64 (e.g. <c>["linux-amd64"]</c>).</param>
/// <param name="LinuxArm64">Fragments for Linux on ARM64 (e.g. <c>["linux-arm64", "linux-amd64"]</c>).</param>
/// <param name="MacX64">Fragments for macOS on x64 (e.g. <c>["osx-amd64"]</c> or <c>["darwin-amd64"]</c>).</param>
/// <param name="MacArm64">Fragments for macOS on ARM64 (e.g. <c>["osx-arm64", "osx-amd64"]</c>).</param>
/// <param name="Fallback">Fragments used when the OS or architecture is not recognised by the above (e.g. <c>["rclone"]</c>).</param>
public sealed record PlatformAssetFragments(
    string[] WindowsX64,
    string[] WindowsArm64,
    string[] LinuxX64,
    string[] LinuxArm64,
    string[] MacX64,
    string[] MacArm64,
    string[] Fallback)
{
    /// <summary>Fragments for rclone releases (e.g. <c>rclone-v1.67.0-windows-amd64.zip</c>).</summary>
    public static readonly PlatformAssetFragments Rclone = new(
        WindowsX64:   ["windows-amd64"],
        WindowsArm64: ["windows-arm64", "windows-amd64"],
        LinuxX64:     ["linux-amd64"],
        LinuxArm64:   ["linux-arm64", "linux-amd64"],
        MacX64:       ["osx-amd64"],
        MacArm64:     ["osx-arm64", "osx-amd64"],
        Fallback:     ["rclone"]);

    /// <summary>
    /// Fragments for syncthing releases (e.g. <c>syncthing-windows-amd64-v1.27.0.zip</c>).
    /// Uses <c>darwin-</c> for macOS, matching syncthing's naming convention.
    /// </summary>
    public static readonly PlatformAssetFragments Syncthing = new(
        WindowsX64:   ["windows-amd64"],
        WindowsArm64: ["windows-arm64", "windows-amd64"],
        LinuxX64:     ["linux-amd64"],
        LinuxArm64:   ["linux-arm64", "linux-amd64"],
        MacX64:       ["darwin-amd64"],
        MacArm64:     ["darwin-arm64", "darwin-amd64"],
        Fallback:     ["syncthing"]);

    /// <summary>
    /// Fragments for ludusavi releases (e.g. <c>ludusavi-v0.31.0-win64.zip</c>).
    /// Uses <c>win64</c>/<c>win32</c>/<c>linux</c>/<c>mac</c> naming.
    /// </summary>
    public static readonly PlatformAssetFragments Ludusavi = new(
        WindowsX64:   ["win64", "win32"],
        WindowsArm64: ["win64", "win32"],
        LinuxX64:     ["linux"],
        LinuxArm64:   ["linux"],
        MacX64:       ["mac"],
        MacArm64:     ["mac"],
        Fallback:     ["ludusavi"]);

    /// <summary>
    /// Returns the fragment array for the current OS and architecture.
    /// Falls back to <see cref="WindowsX64"/>, <see cref="LinuxX64"/>, or <see cref="MacX64"/>
    /// for unrecognised architectures within a known OS.
    /// </summary>
    public string[] ForCurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64   => WindowsX64,
                Architecture.Arm64 => WindowsArm64,
                _                  => WindowsX64
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64   => LinuxX64,
                Architecture.Arm64 => LinuxArm64,
                _                  => LinuxX64
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64   => MacX64,
                Architecture.Arm64 => MacArm64,
                _                  => MacX64
            };
        }

        return Fallback;
    }
}
