using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Newtonsoft.Json.Linq;
using SkEditor.API;
using SkEditor.Utilities.Extensions;
using SkEditor.Utilities.InternalAPI.Classes;
using SkEditor.Views;
using SkEditor.Views.Settings;

namespace SkEditor.Utilities.InternalAPI;

/// <summary>
///     This class should not be used directly. It's for internal use only.
///     If you want to manage addons, use <see cref="SkEditorAPI.Addons" /> instead.
/// </summary>
public static class AddonLoader
{
    private static JObject _metaCache = new();
    public static List<AddonMeta> Addons { get; } = [];
    public static HashSet<string> DllNames { get; } = [];

    public static async Task Load()
    {
        string addonsFolder = Path.Combine(AppConfig.AppDataFolderPath, "Addons");
        Directory.CreateDirectory(addonsFolder);

        Addons.Clear();
        LoadMeta();

        SkEditorSelfAddon coreAddon = new();
        Addons.Add(new AddonMeta
        {
            Addon = coreAddon,
            State = IAddons.AddonState.Disabled,
            Errors = []
        });
        await EnableAddon(coreAddon);

#if !AOT
        await LoadAddonsFromFiles();
#endif

        List<AddonMeta> addonsWithErrors = Addons.Where(addon => addon.HasErrors).ToList();
        if (addonsWithErrors.Count > 0)
        {
            await Task.Delay(100);
            _ = Task.Run(() => CheckForAddonsErrors(addonsWithErrors.Count));
        }
    }

    private static async Task CheckForAddonsErrors(int errorCount)
    {
        ContentDialogResult response = await SkEditorAPI.Windows.ShowDialog(
            "Addons with errors", $"Some addons ({errorCount}) have errors. Do you want to see them?",
            Symbol.AlertUrgent, "Cancel");

        if (response == ContentDialogResult.Primary)
        {
            SettingsWindow window = new();
            SettingsWindow.NavigateToPage(typeof(AddonsPage));
            await window.ShowDialogOnMainWindow();
        }
    }

    private static void LoadMeta()
    {
        string metaFile = Path.Combine(AppConfig.AppDataFolderPath, "Addons", "meta.json");
        if (!File.Exists(metaFile))
        {
            File.WriteAllText(metaFile, "{}");
            _metaCache = new JObject();
            return;
        }

        _metaCache = JObject.Parse(File.ReadAllText(metaFile));
    }

    private static async Task LoadAddonsFromFiles()
    {
        string folder = Path.Combine(AppConfig.AppDataFolderPath, "Addons");
        string[] folders = Directory.GetDirectories(folder);
        List<string> dllFiles = folders
            .Select(sub => Path.Combine(sub, Path.GetFileName(sub) + ".dll"))
            .Where(File.Exists)
            .ToList();

        // First, load all addons into metadata without enabling them
        List<AddonMeta> loadedAddons = [];
        foreach (string dllFile in dllFiles)
        {
            string? dir = Path.GetDirectoryName(dllFile);
            if (dir != null)
            {
                AddonMeta? meta = await LoadAddonFromFileWithoutEnabling(dir);
                if (meta != null)
                {
                    loadedAddons.Add(meta);
                }
            }
            else
            {
                SkEditorAPI.Logs.Error($"Failed to load addon from \"{dllFile}\": No directory found.");
            }
        }

        // Validate dependencies for all addons
        foreach (AddonMeta meta in loadedAddons)
        {
            AddonDependencyResolver.ValidateDependencies(meta, Addons);
        }

        // Resolve dependencies and get loading order
        List<AddonMeta>? sortedAddons = AddonDependencyResolver.ResolveDependencies(loadedAddons);

        if (sortedAddons == null)
        {
            SkEditorAPI.Logs.Error("Circular dependency detected. Some addons cannot be loaded.");
            return;
        }

        // Enable addons in dependency order
        foreach (AddonMeta meta in sortedAddons)
        {
            bool shouldBeDisabled = _metaCache.TryGetValue(meta.Addon.Identifier, out JToken? enabledToken) &&
                                    enabledToken?.Value<bool>() == false;

            if (!shouldBeDisabled && !meta.HasCriticalErrors)
            {
                await EnableAddon(meta.Addon);
            }
        }
    }

    private static async Task<AddonMeta?> LoadAddonFromFileWithoutEnabling(string addonFolder)
    {
        string dllFile = Path.Combine(addonFolder, Path.GetFileName(addonFolder) + ".dll");
        if (!File.Exists(dllFile))
        {
            SkEditorAPI.Logs.Error($"Failed to load addon from \"{addonFolder}\": No dll file found.");
            return null;
        }

        AddonLoadContext loadContext = new(Path.GetFullPath(dllFile));
        List<IAddon?> addonInstances;
        try
        {
            await using FileStream stream = File.OpenRead(dllFile);
            addonInstances = loadContext.LoadFromStream(stream)
                .GetTypes()
                .Where(p => typeof(IAddon).IsAssignableFrom(p) && p is { IsClass: true, IsAbstract: false })
                .Select(addonType => (IAddon?)Activator.CreateInstance(addonType))
                .Where(addonInstance => addonInstance != null)
                .ToList();
        }
        catch (Exception e)
        {
            string name = Path.GetFileNameWithoutExtension(dllFile);

            SkEditorAPI.Logs.Error($"Failed to load addon from \"{dllFile}\": {e.Message}\n{e.StackTrace}");

            await SkEditorAPI.Windows.ShowError(
                $"Failed to load addon '{name}'.\n\n" +
                "Check the application logs for detailed error information. " +
                "Visit the Marketplace to see if an update is available for this addon.");
            return null;
        }

        switch (addonInstances.Count)
        {
            case 0:
                SkEditorAPI.Logs.Warning(
                    $"Failed to load addon from \"{dllFile}\": No addon class found. No worries if it's a library.");
                return null;
            case > 1:
                SkEditorAPI.Logs.Warning($"Failed to load addon from \"{dllFile}\": Multiple addon classes found.");
                return null;
        }

        IAddon? addonInstance = addonInstances[0];

        if (addonInstance is null)
        {
            SkEditorAPI.Logs.Warning(
                $"Failed to load addon from \"{dllFile}\": The addon class is null. No worries if it's a library.");
            return null;
        }

        if (addonInstance is SkEditorSelfAddon)
        {
            SkEditorAPI.Logs.Warning(
                $"Failed to load addon from \"{dllFile}\": The SkEditor Core can't be loaded as an addon.");
            return null;
        }

        if (Addons.Any(m => m.Addon.Identifier == addonInstance.Identifier))
        {
            SkEditorAPI.Logs.Warning(
                $"Failed to load addon from \"{dllFile}\": An addon with the identifier \"{addonInstance.Identifier}\" is already loaded.");
            return null;
        }

        AddonMeta addonMeta = new()
        {
            Addon = addonInstance,
            State = IAddons.AddonState.Installed,
            DllFilePath = dllFile,
            Errors = [],
            LoadContext = loadContext
        };

        Addons.Add(addonMeta);
        return addonMeta;
    }

    public static async Task LoadAddonFromFile(string addonFolder)
    {
        string dllFile = Path.Combine(addonFolder, Path.GetFileName(addonFolder) + ".dll");
        if (!File.Exists(dllFile))
        {
            SkEditorAPI.Logs.Error($"Failed to load addon from \"{addonFolder}\": No dll file found.");
            return;
        }

        AddonLoadContext loadContext = new(Path.GetFullPath(dllFile));
        List<IAddon?> addonInstances;
        try
        {
            await using FileStream stream = File.OpenRead(dllFile);
            addonInstances = loadContext.LoadFromStream(stream)
                .GetTypes()
                .Where(p => typeof(IAddon).IsAssignableFrom(p) && p is { IsClass: true, IsAbstract: false })
                .Select(addonType => (IAddon?)Activator.CreateInstance(addonType))
                .Where(addonInstance => addonInstance != null)
                .ToList();
        }
        catch (Exception e)
        {
            string name = Path.GetFileNameWithoutExtension(dllFile);
            
            SkEditorAPI.Logs.Error($"Failed to load addon from \"{dllFile}\": {e.Message}\n{e.StackTrace}");
            
            await SkEditorAPI.Windows.ShowError(
                $"Failed to load addon '{name}'.\n\n" +
                "Check the application logs for detailed error information. " +
                "Visit the Marketplace to see if an update is available for this addon.");
            return;
        }

        switch (addonInstances.Count)
        {
            case 0:
                SkEditorAPI.Logs.Warning(
                    $"Failed to load addon from \"{dllFile}\": No addon class found. No worries if it's a library.");
                return;
            case > 1:
                SkEditorAPI.Logs.Warning($"Failed to load addon from \"{dllFile}\": Multiple addon classes found.");
                return;
        }
        
        IAddon? addonInstance = addonInstances[0];
        
        if (addonInstance is null)
        {
            SkEditorAPI.Logs.Warning(
                $"Failed to load addon from \"{dllFile}\": The addon class is null. No worries if it's a library.");
            return;
        }

        if (addonInstance is SkEditorSelfAddon)
        {
            SkEditorAPI.Logs.Warning(
                $"Failed to load addon from \"{dllFile}\": The SkEditor Core can't be loaded as an addon.");
            return;
        }

        if (Addons.Any(m => m.Addon.Identifier == addonInstance.Identifier))
        {
            SkEditorAPI.Logs.Warning(
                $"Failed to load addon from \"{dllFile}\": An addon with the identifier \"{addonInstance.Identifier}\" is already loaded.");
            return;
        }

        AddonMeta addonMeta = new()
        {
            Addon = addonInstance,
            State = IAddons.AddonState.Installed,
            DllFilePath = dllFile,
            Errors = [],
            LoadContext = loadContext
        };

        Addons.Add(addonMeta);

        bool shouldBeDisabled = _metaCache.TryGetValue(addonInstance.Identifier, out JToken? enabledToken) &&
                                enabledToken?.Value<bool>() == false;

        if (!shouldBeDisabled)
        {
            await EnableAddon(addonInstance);
        }
    }

    public static async Task LoadAddon(Type addonClass)
    {
        IAddon? addon;

        if (addonClass == typeof(SkEditorSelfAddon))
        {
            addon = new SkEditorSelfAddon();
        }
        else
        {
            addon = (IAddon?)Activator.CreateInstance(addonClass);
        }
        
        if (addon == null)
        {
            SkEditorAPI.Logs.Error($"Failed to load addon \"{addonClass.Name}\": The addon class is null.");
            return;
        }

        Addons.Add(new AddonMeta
        {
            Addon = addon,
            State = IAddons.AddonState.Disabled,
            Errors = []
        });

        await EnableAddon(addon);
    }

    private static bool CanEnable(AddonMeta addonMeta)
    {
        IAddon addon = addonMeta.Addon;
        Version minimalVersion = addon.GetMinimalSkEditorVersion();
        if (SkEditorAPI.Core.GetAppVersion() < minimalVersion)
        {
            SkEditorAPI.Logs.Debug(
                $"Addon \"{addon.Name}\" requires SkEditor version {minimalVersion}, but the current version is {SkEditorAPI.Core.GetAppVersion()}. Disabling it.");
            addonMeta.State = IAddons.AddonState.Disabled;
            addonMeta.Errors.Add(LoadingErrors.OutdatedSkEditor(minimalVersion));
            return false;
        }

        Version? maximalVersion = addon.GetMaximalSkEditorVersion();
        if (maximalVersion == null || SkEditorAPI.Core.GetAppVersion().CompareTo(maximalVersion) <= 0)
        {
            return true;
        }

        SkEditorAPI.Logs.Debug(
            $"Addon \"{addon.Name}\" requires SkEditor version {maximalVersion}, but the current version is {SkEditorAPI.Core.GetAppVersion()}. Disabling it.");
        addonMeta.State = IAddons.AddonState.Disabled;
        addonMeta.Errors.Add(LoadingErrors.OutdatedAddon(maximalVersion));

        return false;
    }

    /// <summary>
    ///     Preload an addon. This will make the desired addon ready to be enabled,
    ///     by checking if it can actually be enabled, and its dependencies.
    /// </summary>
    /// <param name="addonMeta">The addon to preload.</param>
    /// <returns>True if the addon can be enabled, false otherwise.</returns>
    public static async Task<bool> PreLoad(AddonMeta addonMeta)
    {
        if (!CanEnable(addonMeta))
        {
            return false;
        }

        // Check that all required addon dependencies are enabled
        if (!AreAddonDependenciesEnabled(addonMeta))
        {
            return false;
        }

        if (!await LocalDependencyManager.CheckAddonDependencies(addonMeta))
        {
            return false;
        }

        if (!addonMeta.NeedsRestart)
        {
            return true;
        }

        _ = Task.Run(async () =>
        {
            await SkEditorAPI.Windows.ShowMessage("Addon needs restart",
                $"The addon \"{addonMeta.Addon.Name}\" needs a restart to be enabled correctly.");
        });
        addonMeta.State = IAddons.AddonState.Disabled;
        return false;
    }

    private static bool AreAddonDependenciesEnabled(AddonMeta addonMeta)
    {
        List<AddonDependency> addonDeps = addonMeta.Addon.GetDependencies()
            .Where(x => x is AddonDependency)
            .Cast<AddonDependency>()
            .ToList();

        foreach (AddonDependency dep in addonDeps)
        {
            if (!dep.IsRequired)
            {
                continue; // Optional dependencies don't need to be enabled
            }

            AddonMeta? dependency = Addons.FirstOrDefault(a => a.Addon.Identifier == dep.AddonIdentifier);

            if (dependency == null)
            {
                SkEditorAPI.Logs.Error($"Addon \"{addonMeta.Addon.Name}\" requires addon \"{dep.AddonIdentifier}\" which is not installed.");
                addonMeta.Errors.Add(LoadingErrors.MissingAddonDependency(dep.AddonIdentifier));
                return false;
            }

            if (dependency.State != IAddons.AddonState.Enabled)
            {
                SkEditorAPI.Logs.Error($"Addon \"{addonMeta.Addon.Name}\" requires addon \"{dep.AddonIdentifier}\" to be enabled, but it is not.");
                addonMeta.Errors.Add(LoadingErrors.DependencyNotEnabled(dep.AddonIdentifier));
                return false;
            }

            // Verify version compatibility
            if (!string.IsNullOrWhiteSpace(dep.VersionRange))
            {
                string dependencyVersion = dependency.Addon.Version;
                if (!VersionParser.Satisfies(dependencyVersion, dep.VersionRange))
                {
                    SkEditorAPI.Logs.Error($"Addon \"{addonMeta.Addon.Name}\" requires addon \"{dep.AddonIdentifier}\" version {dep.VersionRange}, but version {dependencyVersion} is installed.");
                    addonMeta.Errors.Add(LoadingErrors.IncompatibleAddonVersion(
                        dep.AddonIdentifier, dep.VersionRange, dependencyVersion));
                    return false;
                }
            }
        }

        return true;
    }

    public static async Task<bool> EnableAddon(IAddon addon)
    {
        AddonMeta meta = Addons.First(m => m.Addon == addon);
        if (meta.State == IAddons.AddonState.Enabled)
        {
            return true;
        }

        if (!await PreLoad(meta))
        {
            return false;
        }

        try
        {
            AddonSettingsManager.LoadSettings(addon);

            // ReSharper disable once MethodHasAsyncOverload
            addon.OnEnable();
            await addon.OnEnableAsync();

            meta.State = IAddons.AddonState.Enabled;
            await SaveMeta();

            _ = Dispatcher.UIThread.InvokeAsync(() => SkEditorAPI.Windows.GetMainWindow()?.ReloadUiOfAddons());
            return true;
        }
        catch (Exception e)
        {
            SkEditorAPI.Logs.Error($"Failed to enable addon \"{addon.Name}\": {e.Message}");
            SkEditorAPI.Logs.Fatal(e);
            meta.Errors.Add(LoadingErrors.LoadingException(e));
            meta.State = IAddons.AddonState.Disabled;
            await SaveMeta();
            Registries.Unload(addon);

            _ = Task.Run(() => SkEditorAPI.Windows.GetMainWindow()?.ReloadUiOfAddons());
            return false;
        }
    }

    public static async Task DisableAddon(IAddon addon)
    {
        AddonMeta meta = Addons.First(m => m.Addon == addon);
        if (meta.State == IAddons.AddonState.Disabled)
        {
            return;
        }

        // Check if any enabled addons depend on this addon
        List<AddonMeta> dependents = AddonDependencyResolver.GetDependents(meta, Addons)
            .Where(d => d.State == IAddons.AddonState.Enabled)
            .ToList();

        if (dependents.Count > 0)
        {
            string dependentNames = string.Join(", ", dependents.Select(d => d.Addon.Name));
            ContentDialogResult result = await SkEditorAPI.Windows.ShowDialog(
                "Disable dependent addons?",
                $"The following addons depend on '{addon.Name}':\n\n{dependentNames}\n\n" +
                "Disabling this addon will also disable these addons. Do you want to continue?",
                Symbol.AlertUrgent,
                "Cancel",
                "Disable Anyway",
                false);

            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            // Disable all dependent addons first
            foreach (AddonMeta dependent in dependents)
            {
                try
                {
                    dependent.Addon.OnDisable();
                    dependent.State = IAddons.AddonState.Disabled;
                    Registries.Unload(dependent.Addon);
                }
                catch (Exception e)
                {
                    SkEditorAPI.Logs.Error($"Failed to disable dependent addon \"{dependent.Addon.Name}\": {e.Message}");
                    dependent.State = IAddons.AddonState.Disabled;
                }
            }
        }

        try
        {
            addon.OnDisable();
            meta.State = IAddons.AddonState.Disabled;
        }
        catch (Exception e)
        {
            SkEditorAPI.Logs.Error($"Failed to disable addon \"{addon.Name}\": {e.Message}");
            meta.State = IAddons.AddonState.Disabled;
        }

        await SaveMeta();
        Registries.Unload(addon);
        SkEditorAPI.Windows.GetMainWindow()?.ReloadUiOfAddons();
    }

    public static bool IsAddonEnabled(IAddon addon)
    {
        return Addons.First(m => m.Addon == addon).State == IAddons.AddonState.Enabled;
    }

    public static async Task DeleteAddon(IAddon addon)
    {
        if (addon is SkEditorSelfAddon)
        {
            SkEditorAPI.Logs.Error("You can't delete the SkEditor Core.", true);
            return;
        }

        AddonMeta addonMeta = Addons.First(m => m.Addon == addon);
        if (addonMeta.State == IAddons.AddonState.Enabled)
        {
            try
            {
                addon.OnDisable();
            }
            catch (Exception e)
            {
                SkEditorAPI.Logs.Error($"Failed to disable addon \"{addon.Name}\": {e.Message}");
                Registries.Unload(addon);
            }
        }

        addonMeta.LoadContext?.Unload();

        string addonFile = Path.Combine(AppConfig.AppDataFolderPath, "Addons", addonMeta.Addon.Identifier,
            addonMeta.Addon.Identifier + ".dll");
        if (File.Exists(addonFile))
        {
            File.Delete(addonFile);
        }

        Addons.Remove(addonMeta);
        await SaveMeta();
        Registries.Unload(addon);
        SkEditorAPI.Windows.GetMainWindow()?.ReloadUiOfAddons();
    }

    public static IAddon? GetAddonByNamespace(string? addonNamespace)
    {
        return Addons.FirstOrDefault(addon => addon.Addon.GetType().Namespace == addonNamespace)?.Addon;
    }

    public static SkEditorSelfAddon GetCoreAddon()
    {
        return (SkEditorSelfAddon)Addons.First(addon => addon.Addon is SkEditorSelfAddon).Addon;
    }

    public static async Task SaveMeta()
    {
        string metaFile = Path.Combine(AppConfig.AppDataFolderPath, "Addons", "meta.json");
        _metaCache = new JObject();
        foreach (AddonMeta addonMeta in Addons)
        {
            _metaCache[addonMeta.Addon.Identifier] = addonMeta.State == IAddons.AddonState.Enabled;
        }

        await File.WriteAllTextAsync(metaFile, _metaCache.ToString());
    }

    public static IAddons.AddonState GetAddonState(IAddon addon)
    {
        return Addons.First(m => m.Addon == addon).State;
    }

    public static void HandleAddonMethod(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            SkEditorAPI.Logs.Error($"Failed to execute addon method: {e.Message}", true);
            SkEditorAPI.Logs.Fatal(e);
        }
    }
}