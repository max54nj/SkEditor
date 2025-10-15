using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace SkEditor.Utilities.InternalAPI;

public class AddonLoadContext(string pluginPath) : AssemblyLoadContext(true)
{
    private readonly AssemblyDependencyResolver _resolver = new(pluginPath);
    
    // Static registry to track all loaded addon assemblies and their contexts
    private static readonly Dictionary<string, AddonLoadContext> LoadedContexts = new();
    private static readonly Dictionary<string, Assembly> LoadedAssemblies = new();
    private static readonly object LockObject = new();
    
    public string PluginPath { get; } = pluginPath;
    public Assembly? MainAssembly { get; private set; }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // First, check if this assembly is already loaded in our registry
        lock (LockObject)
        {
            if (LoadedAssemblies.TryGetValue(assemblyName.FullName, out Assembly? cachedAssembly))
            {
                return cachedAssembly;
            }
        }

        // Try to resolve from our own addon directory first
        string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            Assembly assembly = LoadFromAssemblyPath(assemblyPath);
            RegisterAssembly(assemblyName.FullName, assembly);
            return assembly;
        }

        // Try to resolve from other loaded addon contexts
        lock (LockObject)
        {
            foreach (var context in LoadedContexts.Values)
            {
                if (context == this) continue; // Skip ourselves
                
                // Check if the addon directory contains this assembly
                string? addonDir = Path.GetDirectoryName(context.PluginPath);
                if (addonDir != null)
                {
                    string possiblePath = Path.Combine(addonDir, assemblyName.Name + ".dll");
                    if (File.Exists(possiblePath))
                    {
                        try
                        {
                            // Load the assembly from the other addon's context
                            Assembly assembly = context.LoadFromAssemblyPath(possiblePath);
                            RegisterAssembly(assemblyName.FullName, assembly);
                            return assembly;
                        }
                        catch
                        {
                            // Continue searching
                        }
                    }
                }
            }
        }

        // Fall back to default resolution (shared framework assemblies, etc.)
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        // Try to find in other addon directories
        lock (LockObject)
        {
            foreach (var context in LoadedContexts.Values)
            {
                if (context == this) continue;
                
                string? addonDir = Path.GetDirectoryName(context.PluginPath);
                if (addonDir != null)
                {
                    string possiblePath = Path.Combine(addonDir, unmanagedDllName);
                    if (File.Exists(possiblePath))
                    {
                        try
                        {
                            return LoadUnmanagedDllFromPath(possiblePath);
                        }
                        catch
                        {
                            // Continue searching
                        }
                    }
                }
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Register this load context so other addons can resolve assemblies from it.
    /// </summary>
    public void Register()
    {
        lock (LockObject)
        {
            string key = Path.GetFileNameWithoutExtension(PluginPath);
            if (!LoadedContexts.ContainsKey(key))
            {
                LoadedContexts[key] = this;
            }
        }
    }

    /// <summary>
    /// Unregister this load context when the addon is unloaded.
    /// </summary>
    public void Unregister()
    {
        lock (LockObject)
        {
            string key = Path.GetFileNameWithoutExtension(PluginPath);
            LoadedContexts.Remove(key);
            
            // Remove all assemblies loaded by this context
            var assembliesToRemove = LoadedAssemblies
                .Where(kvp => kvp.Value.Equals(MainAssembly) || IsAssemblyLoadedByThisContext(kvp.Value))
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var assemblyName in assembliesToRemove)
            {
                LoadedAssemblies.Remove(assemblyName);
            }
        }
    }
    
    private bool IsAssemblyLoadedByThisContext(Assembly assembly)
    {
        try
        {
            // Check if this assembly was loaded by this context by comparing locations
            string? assemblyLocation = assembly.Location;
            string? thisContextDir = Path.GetDirectoryName(PluginPath);
            
            if (assemblyLocation != null && thisContextDir != null)
            {
                return assemblyLocation.StartsWith(thisContextDir, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // If we can't determine, assume it's not from this context
        }
        
        return false;
    }

    private static void RegisterAssembly(string fullName, Assembly assembly)
    {
        lock (LockObject)
        {
            if (!LoadedAssemblies.ContainsKey(fullName))
            {
                LoadedAssemblies[fullName] = assembly;
            }
        }
    }

    /// <summary>
    /// Load an assembly from a stream and register it as the main assembly.
    /// </summary>
    public new Assembly LoadFromStream(Stream assembly)
    {
        MainAssembly = base.LoadFromStream(assembly);
        if (MainAssembly != null)
        {
            RegisterAssembly(MainAssembly.FullName ?? "", MainAssembly);
        }
        return MainAssembly;
    }

    /// <summary>
    /// Clear all registered contexts (for testing or full reload).
    /// </summary>
    public static void ClearRegistry()
    {
        lock (LockObject)
        {
            LoadedContexts.Clear();
            LoadedAssemblies.Clear();
        }
    }
}