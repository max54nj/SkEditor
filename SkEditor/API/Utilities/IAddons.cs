using System.Collections.Generic;
using System.Threading.Tasks;

namespace SkEditor.API;

/// <summary>
///     Interface for Addons.
/// </summary>
public interface IAddons
{
    /// <summary>
    ///     Represents the state of an addon.
    /// </summary>
    public enum AddonState
    {
        /// <summary>
        ///     The addon is installed. This regroups both enabled and disabled addons.
        /// </summary>
        Installed,

        /// <summary>
        ///     The addon is enabled.
        /// </summary>
        Enabled,

        /// <summary>
        ///     The addon is disabled.
        /// </summary>
        Disabled
    }

    /// <summary>
    ///     Get the <see cref="AddonState" /> of an addon.
    /// </summary>
    /// <param name="addon">The addon to get the state of.</param>
    /// <returns>The state of the addon.</returns>
    public AddonState GetAddonState(IAddon addon);

    /// <summary>
    ///     Enables the specified addon.
    /// </summary>
    /// <param name="addon">The addon to enable.</param>
    /// <returns>True if the addon was enabled successfully, false otherwise.</returns>
    public Task<bool> EnableAddon(IAddon addon);

    /// <summary>
    ///     Disables the specified addon.
    /// </summary>
    /// <param name="addon">The addon to disable.</param>
    public Task DisableAddon(IAddon addon);

    /// <summary>
    ///     Retrieves the addon with the specified identifier.
    /// </summary>
    /// <param name="addonIdentifier">The identifier of the addon to retrieve.</param>
    /// <returns>The addon with the specified identifier, or null if no such addon exists.</returns>
    public IAddon? GetAddon(string addonIdentifier);

    /// <summary>
    ///     Retrieves all addons with the specified state.
    /// </summary>
    /// <param name="state">The state of the addons to retrieve. Defaults to AddonState.Installed.</param>
    /// <returns>An enumerable of addons with the specified state.</returns>
    public IEnumerable<IAddon> GetAddons(AddonState state = AddonState.Installed);

    /// <summary>
    ///     Get the self addon of SkEditor. This is the addon that represents SkEditor itself.
    /// </summary>
    /// <returns>The self addon of SkEditor.</returns>
    public SkEditorSelfAddon GetSelfAddon();

    /// <summary>
    ///     Check if an addon with the specified identifier is available (installed and enabled).
    /// </summary>
    /// <param name="addonIdentifier">The identifier of the addon to check.</param>
    /// <returns>True if the addon is available, false otherwise.</returns>
    public bool IsAddonAvailable(string addonIdentifier);

    /// <summary>
    ///     Check if an addon with the specified identifier is available and matches the version range.
    /// </summary>
    /// <param name="addonIdentifier">The identifier of the addon to check.</param>
    /// <param name="versionRange">The version range to check (npm-style: ^1.2.3, >=1.0.0, etc.).</param>
    /// <returns>True if the addon is available and matches the version range, false otherwise.</returns>
    public bool IsAddonAvailable(string addonIdentifier, string versionRange);
}