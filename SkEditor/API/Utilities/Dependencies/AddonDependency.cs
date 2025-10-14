namespace SkEditor.API;

/// <summary>
///     Represents a dependency to another addon.
/// </summary>
public class AddonDependency(string addonIdentifier, string? versionRange = null, bool isRequired = true) : IDependency
{
    public string AddonIdentifier { get; } = addonIdentifier;
    public string? VersionRange { get; } = versionRange;
    public bool IsRequired { get; } = isRequired;
}