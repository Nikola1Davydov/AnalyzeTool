using System.ComponentModel.DataAnnotations;

namespace Build.Options;

/// <summary>
///     The vendor shown by the installer (Programs and Features) and the Autodesk application package.
/// </summary>
/// <seealso href="https://www.autodesk.com/autodesk-university/class/AppBundle-Cross-Distribution-Autodesk-Products-App-Store-and-Forge-2020">AppBundle: Cross-Distribution Autodesk Products, App Store, and Forge</seealso>
[Serializable]
public sealed record VendorOptions
{
    /// <summary>
    ///     The vendor name.
    /// </summary>
    [Required] public string Name { get; init; } = null!;

    /// <summary>
    ///     The vendor website URL.
    /// </summary>
    public string? Url { get; init; }

    /// <summary>
    ///     The vendor email address.
    /// </summary>
    public string? Email { get; init; }
}
