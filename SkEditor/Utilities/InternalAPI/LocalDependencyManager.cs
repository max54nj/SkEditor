using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using SkEditor.API;
using SkEditor.Utilities.InternalAPI.Classes;
using FileMode = System.IO.FileMode;
using Repository = NuGet.Protocol.Core.Types.Repository;

namespace SkEditor.Utilities.InternalAPI;

/// <summary>
///     Class to manage NuGet dependencies for addons.
///     Downloads packages from NuGet, caches them in addon folders,
///     and loads them into the addon's AssemblyLoadContext.
/// </summary>
public static class LocalDependencyManager
{
    private static readonly Dictionary<string, (string Version, string Path)> LoadedPackages = new();

    /// <summary>
    ///     Check, download, and load all NuGet dependencies for an addon.
    ///     This method handles the complete lifecycle: download, cache, and load into the addon's context.
    /// </summary>
    /// <param name="addonMeta">The addon metadata containing dependency information.</param>
    /// <returns>True if all dependencies were successfully loaded, false otherwise.</returns>
    public static async Task<bool> LoadNuGetDependencies(AddonMeta addonMeta)
    {
        List<NuGetDependency> nugetDependencies = addonMeta.Addon.GetDependencies()
            .Where(x => x is NuGetDependency)
            .Cast<NuGetDependency>()
            .ToList();

        if (nugetDependencies.Count == 0)
        {
            return true; // No dependencies to load
        }

        SkEditorAPI.Logs.Debug($"Loading {nugetDependencies.Count} NuGet dependencies for addon '{addonMeta.Addon.Name}'");

        string addonFolder = Path.Combine(AppConfig.AppDataFolderPath, "Addons", addonMeta.Addon.Identifier);
        if (!Directory.Exists(addonFolder))
        {
            Directory.CreateDirectory(addonFolder);
        }

        foreach (NuGetDependency dependency in nugetDependencies)
        {
            try
            {
                bool success = await LoadSingleDependency(addonMeta, dependency, addonFolder);
                if (!success && dependency.IsRequired)
                {
                    SkEditorAPI.Logs.Error($"Failed to load required NuGet dependency '{dependency.PackageId}' for addon '{addonMeta.Addon.Name}'");
                    return false;
                }
                else if (!success)
                {
                    SkEditorAPI.Logs.Warning($"Failed to load optional NuGet dependency '{dependency.PackageId}' for addon '{addonMeta.Addon.Name}'");
                }
            }
            catch (Exception ex)
            {
                SkEditorAPI.Logs.Error($"Exception while loading NuGet dependency '{dependency.PackageId}': {ex.Message}");
                SkEditorAPI.Logs.Fatal(ex);

                if (dependency.IsRequired)
                {
                    addonMeta.Errors.Add(LoadingErrors.FailedToLoadDependency(dependency.PackageId, ex.Message));
                    return false;
                }
            }
        }

        SkEditorAPI.Logs.Debug($"Successfully loaded all NuGet dependencies for addon '{addonMeta.Addon.Name}'");
        return true;
    }

    private static async Task<bool> LoadSingleDependency(AddonMeta addonMeta, NuGetDependency dependency, string addonFolder)
    {
        string packageId = dependency.PackageId;
        string nameSpace = dependency.NameSpace;
        string? requestedVersion = dependency.Version;

        SkEditorAPI.Logs.Debug($"  Processing NuGet dependency: {packageId} (namespace: {nameSpace}, version: {requestedVersion ?? "latest"})");

        // Build the expected DLL path
        string dllPath = Path.Combine(addonFolder, $"{nameSpace}.dll");

        // Check if the DLL already exists and has the correct version
        if (File.Exists(dllPath))
        {
            bool versionMatch = await CheckAssemblyVersion(dllPath, requestedVersion);
            if (versionMatch)
            {
                SkEditorAPI.Logs.Debug($"    NuGet package '{packageId}' already exists with correct version, loading from cache");
                return await LoadAssemblyIntoContext(addonMeta, dllPath, packageId, nameSpace);
            }
            else
            {
                SkEditorAPI.Logs.Debug($"    NuGet package '{packageId}' exists but version mismatch, re-downloading");
            }
        }

        // Download the package from NuGet
        NuGetVersion targetVersion;
        try
        {
            targetVersion = await DownloadNuGetPackage(packageId, requestedVersion, addonFolder, nameSpace);
        }
        catch (Exception ex)
        {
            SkEditorAPI.Logs.Error($"    Failed to download NuGet package '{packageId}': {ex.Message}");
            addonMeta.Errors.Add(LoadingErrors.FailedToLoadDependency(packageId, $"Download failed: {ex.Message}"));
            return false;
        }

        // Verify the DLL was extracted successfully
        if (!File.Exists(dllPath))
        {
            string errorMsg = $"DLL file '{nameSpace}.dll' not found after extraction";
            SkEditorAPI.Logs.Error($"    {errorMsg}");
            addonMeta.Errors.Add(LoadingErrors.FailedToLoadDependency(packageId, errorMsg));
            return false;
        }

        // Load the assembly into the addon's context
        SkEditorAPI.Logs.Debug($"    Successfully downloaded '{packageId}' v{targetVersion}, now loading into context");
        return await LoadAssemblyIntoContext(addonMeta, dllPath, packageId, nameSpace);
    }

    private static Task<bool> CheckAssemblyVersion(string dllPath, string? requestedVersion)
    {
        try
        {
            // Load assembly metadata to check version without loading into any context
            AssemblyName assemblyName = AssemblyName.GetAssemblyName(dllPath);
            string installedVersion = assemblyName.Version?.ToString() ?? "0.0.0.0";

            if (string.IsNullOrWhiteSpace(requestedVersion))
            {
                // No specific version requested, any version is acceptable
                return Task.FromResult(true);
            }

            // Parse versions for comparison
            if (Version.TryParse(installedVersion, out Version? installed) &&
                Version.TryParse(requestedVersion, out Version? requested))
            {
                // Check if installed version matches or is greater than requested
                return Task.FromResult(installed >= requested);
            }

            // If parsing fails, assume version mismatch to trigger re-download
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            SkEditorAPI.Logs.Warning($"Failed to check assembly version for '{dllPath}': {ex.Message}");
            return Task.FromResult(false); // Trigger re-download on error
        }
    }

    private static async Task<NuGetVersion> DownloadNuGetPackage(string packageId, string? requestedVersion, string addonFolder, string nameSpace)
    {
        SkEditorAPI.Logs.Debug($"    Downloading NuGet package '{packageId}' from nuget.org");

        using SourceCacheContext cache = new();
        SourceRepository repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
        FindPackageByIdResource resource = await repository.GetResourceAsync<FindPackageByIdResource>();

        // Get all available versions
        IEnumerable<NuGetVersion> versionsEnumerable = await resource.GetAllVersionsAsync(
            packageId,
            cache,
            NullLogger.Instance,
            default);

        List<NuGetVersion> versions = versionsEnumerable?.ToList() ?? new List<NuGetVersion>();

        if (versions.Count == 0)
        {
            throw new Exception($"No versions found for package '{packageId}'");
        }

        // Determine which version to download
        NuGetVersion targetVersion;
        if (!string.IsNullOrWhiteSpace(requestedVersion))
        {
            // Try to parse the requested version
            if (!NuGetVersion.TryParse(requestedVersion, out NuGetVersion? parsedVersion) || parsedVersion == null)
            {
                throw new Exception($"Invalid version format: '{requestedVersion}'");
            }

            targetVersion = parsedVersion;

            // Check if the requested version exists
            if (!versions.Contains(targetVersion))
            {
                throw new Exception($"Version '{requestedVersion}' not found for package '{packageId}'");
            }
        }
        else
        {
            // Use the latest stable version
            targetVersion = versions.OrderByDescending(v => v).First();
            SkEditorAPI.Logs.Debug($"    No version specified, using latest: {targetVersion}");
        }

        // Download the package
        using MemoryStream packageStream = new();
        await resource.CopyNupkgToStreamAsync(
            packageId,
            targetVersion,
            packageStream,
            cache,
            NullLogger.Instance,
            default);

        // Extract the DLL from the .nupkg file
        await ExtractDllFromNupkg(packageStream, addonFolder, nameSpace, packageId);

        return targetVersion;
    }

    private static async Task ExtractDllFromNupkg(MemoryStream packageStream, string addonFolder, string nameSpace, string packageId)
    {
        // Reset stream position
        packageStream.Position = 0;

        // Save temporarily to extract
        string tempNupkgPath = Path.Combine(addonFolder, $"{packageId}_{Guid.NewGuid()}.nupkg");

        try
        {
            await File.WriteAllBytesAsync(tempNupkgPath, packageStream.ToArray());

            // Open and extract the package
            await using FileStream fileStream = new(tempNupkgPath, FileMode.Open, FileAccess.Read);
            using PackageArchiveReader reader = new(fileStream);

            // Find the DLL file in the package
            // NuGet packages typically have DLLs in lib/{framework}/ folders
            List<string> files = reader.GetFiles().ToList();
            string? targetDll = FindTargetDll(files, nameSpace);

            if (targetDll == null)
            {
                throw new Exception($"Could not find DLL '{nameSpace}.dll' in package '{packageId}'");
            }

            SkEditorAPI.Logs.Debug($"    Found DLL in package: {targetDll}");

            // Extract the DLL to the addon folder
            string outputPath = Path.Combine(addonFolder, $"{nameSpace}.dll");
            await using Stream dllStream = reader.GetStream(targetDll);
            await using FileStream outputStream = new(outputPath, FileMode.Create);
            await dllStream.CopyToAsync(outputStream);

            SkEditorAPI.Logs.Debug($"    Extracted DLL to: {outputPath}");
        }
        finally
        {
            // Clean up temporary file
            if (File.Exists(tempNupkgPath))
            {
                try
                {
                    File.Delete(tempNupkgPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    private static string? FindTargetDll(List<string> files, string nameSpace)
    {
        // Look for the DLL in various common locations within the NuGet package
        // Priority order:
        // 1. lib/net8.0/{nameSpace}.dll
        // 2. lib/net7.0/{nameSpace}.dll
        // 3. lib/net6.0/{nameSpace}.dll
        // 4. lib/netstandard2.1/{nameSpace}.dll
        // 5. lib/netstandard2.0/{nameSpace}.dll
        // 6. Any lib folder with the correct DLL name

        string[] preferredPaths = new[]
        {
            $"lib/net8.0/{nameSpace}.dll",
            $"lib/net7.0/{nameSpace}.dll",
            $"lib/net6.0/{nameSpace}.dll",
            $"lib/netstandard2.1/{nameSpace}.dll",
            $"lib/netstandard2.0/{nameSpace}.dll"
        };

        // Check preferred paths first
        foreach (string preferredPath in preferredPaths)
        {
            string? match = files.FirstOrDefault(f =>
                f.Replace('\\', '/').Equals(preferredPath, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }
        }

        // Fall back to any DLL with the correct name in a lib folder
        return files.FirstOrDefault(f =>
            f.EndsWith($"{nameSpace}.dll", StringComparison.OrdinalIgnoreCase) &&
            f.Contains("lib/", StringComparison.OrdinalIgnoreCase));
    }

    private static Task<bool> LoadAssemblyIntoContext(AddonMeta addonMeta, string dllPath, string packageId, string nameSpace)
    {
        try
        {
            // Check if this package is already loaded globally
            string cacheKey = $"{packageId}:{nameSpace}";
            if (LoadedPackages.TryGetValue(cacheKey, out var cached))
            {
                // Check if it's from a different location (version conflict)
                if (cached.Path != dllPath)
                {
                    SkEditorAPI.Logs.Warning(
                        $"NuGet package '{packageId}' is already loaded from a different location. " +
                        $"This may cause version conflicts. Existing: {cached.Path}, New: {dllPath}");
                }
                else
                {
                    SkEditorAPI.Logs.Debug($"    NuGet package '{packageId}' already loaded in context");
                }
                return Task.FromResult(true);
            }

            // Load the assembly into the addon's load context
            Assembly assembly = addonMeta.LoadContext.LoadFromAssemblyPath(dllPath);

            // Track the loaded package
            LoadedPackages[cacheKey] = (assembly.GetName().Version?.ToString() ?? "unknown", dllPath);

            SkEditorAPI.Logs.Debug($"    Successfully loaded assembly '{nameSpace}' v{assembly.GetName().Version} into addon context");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            SkEditorAPI.Logs.Error($"    Failed to load assembly from '{dllPath}': {ex.Message}");
            addonMeta.Errors.Add(LoadingErrors.FailedToLoadDependency(packageId, $"Failed to load assembly: {ex.Message}"));
            return Task.FromResult(false);
        }
    }

    /// <summary>
    ///     Check if there are version conflicts across multiple addons.
    ///     Returns a list of warning messages if conflicts are detected.
    /// </summary>
    public static List<string> CheckForVersionConflicts(List<AddonMeta> allAddons)
    {
        List<string> warnings = new();

        // Group NuGet dependencies by package ID
        Dictionary<string, List<(string AddonName, string? Version)>> packageUsage = new();

        foreach (AddonMeta addon in allAddons)
        {
            if (addon.State != IAddons.AddonState.Enabled)
            {
                continue;
            }

            List<NuGetDependency> deps = addon.Addon.GetDependencies()
                .Where(x => x is NuGetDependency)
                .Cast<NuGetDependency>()
                .ToList();

            foreach (NuGetDependency dep in deps)
            {
                if (!packageUsage.ContainsKey(dep.PackageId))
                {
                    packageUsage[dep.PackageId] = new();
                }

                packageUsage[dep.PackageId].Add((addon.Addon.Name, dep.Version));
            }
        }

        // Check for conflicts
        foreach (var (packageId, usages) in packageUsage)
        {
            if (usages.Count <= 1)
            {
                continue;
            }

            // Check if different versions are requested
            var distinctVersions = usages
                .Where(u => !string.IsNullOrWhiteSpace(u.Version))
                .Select(u => u.Version)
                .Distinct()
                .ToList();

            if (distinctVersions.Count > 1)
            {
                string addonList = string.Join(", ", usages.Select(u => $"{u.AddonName} ({u.Version ?? "latest"})"));
                string warning = $"Version conflict detected for NuGet package '{packageId}'. Used by: {addonList}";
                warnings.Add(warning);
                SkEditorAPI.Logs.Warning(warning);
            }
        }

        return warnings;
    }

    /// <summary>
    ///     Clear the loaded packages cache. Useful for testing or cleanup.
    /// </summary>
    public static void ClearCache()
    {
        LoadedPackages.Clear();
    }
}
