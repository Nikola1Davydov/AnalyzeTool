using System.ComponentModel.DataAnnotations;

namespace Build.Options;

/// <summary>
///     The product the installer and the bundle ship. Every project made from the template gets its own.
/// </summary>
[Serializable]
public sealed record ProductOptions
{
    /// <summary>
    ///     The product name: the .msi and .bundle file names, the name in Programs and Features and in the Autodesk store.
    /// </summary>
    [Required] public string Name { get; init; } = null!;

    /// <summary>
    ///     The UpgradeCode of the .msi: the same for every version of the product, so a newer one replaces the older.
    /// </summary>
    [Required] public string UpgradeCode { get; init; } = null!;
}
