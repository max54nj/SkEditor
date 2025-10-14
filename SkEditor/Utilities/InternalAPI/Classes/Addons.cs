﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SkEditor.Utilities.InternalAPI;

namespace SkEditor.API;

public class Addons : IAddons
{
    public IAddons.AddonState GetAddonState(IAddon addon)
    {
        return AddonLoader.GetAddonState(addon);
    }

    public async Task<bool> EnableAddon(IAddon addon)
    {
        IAddons.AddonState state = GetAddonState(addon);
        if (state == IAddons.AddonState.Enabled)
        {
            return true;
        }

        if (addon is SkEditorSelfAddon)
        {
            SkEditorAPI.Logs.AddonError("Cannot enable the self addon of SkEditor.", true);
            return false;
        }

        return await AddonLoader.EnableAddon(addon);
    }

    public async Task DisableAddon(IAddon addon)
    {
        if (GetAddonState(addon) == IAddons.AddonState.Disabled)
        {
            return;
        }

        if (addon is SkEditorSelfAddon)
        {
            SkEditorAPI.Logs.AddonError("Cannot disable the self addon of SkEditor.", true);
            return;
        }

        await AddonLoader.DisableAddon(addon);
    }

    public IAddon? GetAddon(string addonIdentifier)
    {
        return AddonLoader.Addons.FirstOrDefault(a => a.Addon.Identifier == addonIdentifier)?.Addon;
    }

    public IEnumerable<IAddon> GetAddons(IAddons.AddonState state = IAddons.AddonState.Installed)
    {
        return state == IAddons.AddonState.Installed
            ? AddonLoader.Addons.Select(a => a.Addon)
            : AddonLoader.Addons.Where(a => GetAddonState(a.Addon) == state).Select(a => a.Addon);
    }

    public SkEditorSelfAddon GetSelfAddon()
    {
        return AddonLoader.GetCoreAddon();
    }

    public bool IsAddonAvailable(string addonIdentifier)
    {
        IAddon? addon = GetAddon(addonIdentifier);
        return addon != null && GetAddonState(addon) == IAddons.AddonState.Enabled;
    }

    public bool IsAddonAvailable(string addonIdentifier, string versionRange)
    {
        IAddon? addon = GetAddon(addonIdentifier);
        if (addon == null || GetAddonState(addon) != IAddons.AddonState.Enabled)
        {
            return false;
        }

        return VersionParser.Satisfies(addon.Version, versionRange);
    }
}