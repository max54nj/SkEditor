using System.Collections.Generic;
using System.Linq;
using SkEditor.API;
using SkEditor.Utilities.InternalAPI.Classes;

namespace SkEditor.Utilities.InternalAPI;

/// <summary>
///     Resolves addon dependencies and determines the correct loading order.
/// </summary>
public static class AddonDependencyResolver
{
    /// <summary>
    ///     Resolve dependencies and return addons in the correct loading order.
    /// </summary>
    /// <param name="addons">The list of addon metadata to resolve.</param>
    /// <returns>A list of addons in dependency order, or null if there are circular dependencies.</returns>
    public static List<AddonMeta>? ResolveDependencies(List<AddonMeta> addons)
    {
        // Build dependency graph
        Dictionary<string, List<string>> graph = new();
        Dictionary<string, AddonMeta> addonMap = new();

        foreach (AddonMeta meta in addons)
        {
            string identifier = meta.Addon.Identifier;
            addonMap[identifier] = meta;
            graph[identifier] = [];

            List<AddonDependency> addonDeps = meta.Addon.GetDependencies()
                .Where(x => x is AddonDependency)
                .Cast<AddonDependency>()
                .ToList();

            foreach (AddonDependency dep in addonDeps)
            {
                graph[identifier].Add(dep.AddonIdentifier);
            }
        }

        // Check for circular dependencies
        HashSet<string> visited = [];
        HashSet<string> recursionStack = [];
        List<string>? circularPath = null;

        foreach (string identifier in graph.Keys)
        {
            if (!visited.Contains(identifier))
            {
                circularPath = DetectCycle(identifier, graph, visited, recursionStack, [identifier]);
                if (circularPath != null)
                {
                    // Mark all addons in the circular dependency as having errors
                    foreach (string id in circularPath)
                    {
                        if (addonMap.TryGetValue(id, out AddonMeta? meta))
                        {
                            meta.Errors.Add(LoadingErrors.CircularDependency(circularPath));
                        }
                    }
                    return null;
                }
            }
        }

        // Perform topological sort
        List<string> sorted = TopologicalSort(graph);

        // Return addons in sorted order
        List<AddonMeta> result = [];
        foreach (string identifier in sorted)
        {
            if (addonMap.TryGetValue(identifier, out AddonMeta? meta))
            {
                result.Add(meta);
            }
        }

        return result;
    }

    /// <summary>
    ///     Validate that all required dependencies are present and have compatible versions.
    /// </summary>
    public static bool ValidateDependencies(AddonMeta addonMeta, List<AddonMeta> allAddons)
    {
        List<AddonDependency> addonDeps = addonMeta.Addon.GetDependencies()
            .Where(x => x is AddonDependency)
            .Cast<AddonDependency>()
            .ToList();

        foreach (AddonDependency dep in addonDeps)
        {
            AddonMeta? dependency = allAddons.FirstOrDefault(a => a.Addon.Identifier == dep.AddonIdentifier);

            if (dependency == null)
            {
                if (dep.IsRequired)
                {
                    addonMeta.Errors.Add(LoadingErrors.MissingAddonDependency(dep.AddonIdentifier));
                    return false;
                }
                continue;
            }

            // Check version compatibility
            if (!string.IsNullOrWhiteSpace(dep.VersionRange))
            {
                string dependencyVersion = dependency.Addon.Version;
                if (!VersionParser.Satisfies(dependencyVersion, dep.VersionRange))
                {
                    if (dep.IsRequired)
                    {
                        addonMeta.Errors.Add(LoadingErrors.IncompatibleAddonVersion(
                            dep.AddonIdentifier, dep.VersionRange, dependencyVersion));
                        return false;
                    }
                }
            }
        }

        return true;
    }

    /// <summary>
    ///     Get all addons that depend on the specified addon.
    /// </summary>
    public static List<AddonMeta> GetDependents(AddonMeta addonMeta, List<AddonMeta> allAddons)
    {
        string targetIdentifier = addonMeta.Addon.Identifier;
        List<AddonMeta> dependents = [];

        foreach (AddonMeta meta in allAddons)
        {
            if (meta.Addon.Identifier == targetIdentifier)
            {
                continue;
            }

            List<AddonDependency> deps = meta.Addon.GetDependencies()
                .Where(x => x is AddonDependency)
                .Cast<AddonDependency>()
                .ToList();

            if (deps.Any(d => d.AddonIdentifier == targetIdentifier && d.IsRequired))
            {
                dependents.Add(meta);
            }
        }

        return dependents;
    }

    private static List<string>? DetectCycle(string node, Dictionary<string, List<string>> graph,
        HashSet<string> visited, HashSet<string> recursionStack, List<string> path)
    {
        visited.Add(node);
        recursionStack.Add(node);

        if (graph.TryGetValue(node, out List<string>? neighbors))
        {
            foreach (string neighbor in neighbors)
            {
                if (!visited.Contains(neighbor))
                {
                    List<string> newPath = new(path) { neighbor };
                    List<string>? cycle = DetectCycle(neighbor, graph, visited, recursionStack, newPath);
                    if (cycle != null)
                    {
                        return cycle;
                    }
                }
                else if (recursionStack.Contains(neighbor))
                {
                    // Found a cycle
                    int startIndex = path.IndexOf(neighbor);
                    return path.Skip(startIndex).ToList();
                }
            }
        }

        recursionStack.Remove(node);
        return null;
    }

    private static List<string> TopologicalSort(Dictionary<string, List<string>> graph)
    {
        Dictionary<string, int> inDegree = graph.Keys.ToDictionary(k => k, _ => 0);

        foreach (List<string> neighbors in graph.Values)
        {
            foreach (string neighbor in neighbors)
            {
                if (inDegree.ContainsKey(neighbor))
                {
                    inDegree[neighbor]++;
                }
            }
        }

        Queue<string> queue = new();
        foreach (KeyValuePair<string, int> kvp in inDegree.Where(kvp => kvp.Value == 0))
        {
            queue.Enqueue(kvp.Key);
        }

        List<string> sorted = [];

        while (queue.Count > 0)
        {
            string node = queue.Dequeue();
            sorted.Add(node);

            if (graph.TryGetValue(node, out List<string>? neighbors))
            {
                foreach (string neighbor in neighbors)
                {
                    if (inDegree.ContainsKey(neighbor))
                    {
                        inDegree[neighbor]--;
                        if (inDegree[neighbor] == 0)
                        {
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }
        }

        return sorted;
    }
}
