﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NuGet.Common;
using NuGet.LibraryModel;
using NuGet.ProjectModel;
using NuGet.Shared;

namespace NuGet.Commands
{
    internal class TransitiveNoWarnUtils
    {
        // static should be fine across multiple restore calls as this solely depends on the csproj file of the project.
        private static readonly ConcurrentDictionary<string, WarningPropertiesCollection> _warningPropertiesCache = 
            new ConcurrentDictionary<string, WarningPropertiesCollection>();

        internal static IDictionary<string, WarningPropertiesCollection> CreateTransitiveWarningPropertiesCollection(
            DependencyGraphSpec dgSpec,
            IEnumerable<RestoreTargetGraph> targetGraphs,
            LibraryIdentity parentProject)
        {
            var transitiveWarningPropertiesCollection = new Dictionary<string, WarningPropertiesCollection>();
            var parentProjectSpec = GetNodePackageSpec(dgSpec, parentProject.Name);

            var parentWarningProperties = GetNodeWarningProperties(parentProjectSpec);

            foreach (var targetGraph in targetGraphs)
            {
                if (string.IsNullOrEmpty(targetGraph.RuntimeIdentifier))
                {
                    TraverseTargetGraph(targetGraph, dgSpec, parentProject, parentWarningProperties);
                }
                
            }

            return transitiveWarningPropertiesCollection;
        }

        private static void TraverseTargetGraph(RestoreTargetGraph targetGraph,
            DependencyGraphSpec dgSpec,
            LibraryIdentity parentProject,            
            WarningPropertiesCollection parentWarningPropertiesCollection)
        {
            var paths = new Dictionary<string, IEnumerable<LibraryIdentity>>();
            var dependencyMapping = new Dictionary<string, IEnumerable<LibraryDependency>>();
            var queue = new Queue<Tuple<string, LibraryType ,WarningPropertiesCollection>>();

            //TODO seen should have node id and the path warningproperties
            var seen = new HashSet<string>();

            // All the packages in parent project's closure. 
            // Once we have collected data for all of these, we can exit.
            var parentPackageDependencies = new HashSet<string>(
                targetGraph.Flattened.Where( d => d.Key.Type == LibraryType.Package).Select( d => d.Key.Name));

            // Seed the queue with the original flattened graph
            // Add all dependencies into a dict for a quick transitive lookup
            foreach (var dependency in targetGraph.Flattened.OrderBy(d => d.Key.Name))
            {
                dependencyMapping[dependency.Key.Name] = dependency.Data.Dependencies;
                var queueTuple = Tuple.Create(dependency.Key.Name, dependency.Key.Type ,parentWarningPropertiesCollection);

                // Add the metadata from the parent project here.
                queue.Enqueue(queueTuple);
            }

            // start taking one node from the queue and get all of it's dependencies
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (!seen.Contains(node.Item1))
                {
                    var nodeDependencies = dependencyMapping[node.Item1];
                    var nodeName = node.Item1;
                    var nodeType = node.Item2;
                    var pathWarningProperties = node.Item3;

                    // If the node is a project then we need to extract the warning properties and 
                    // add those to the warning properties of the current path.
                    if (nodeType == LibraryType.Project || nodeType == LibraryType.ExternalProject)
                    {
                        // Get the node PackageSpec
                        var nodeProjectSpec = GetNodePackageSpec(dgSpec, nodeName);

                        // Get the WarningPropertiesCollection from the PackageSpec
                        var nodeWarningProperties = GetNodeWarningProperties(nodeProjectSpec);

                        // Merge the WarningPropertiesCollection to the one in the path
                        var mergedWarningPropertiesCollection = MergeWarningPropertiesCollection(pathWarningProperties, 
                            nodeWarningProperties);

                        // Add all the project's dependencies to the Queue with the merged WarningPropertiesCollection


                    }
                    else if (nodeType == LibraryType.Package)
                    {
                        // Evaluate the package properties for the current path

                        // If the path does not "NoWarn" for this package then remove the path from parentPackageDependencies

                        // If the path has a "NoWarn" for the package then save it to the result
                        
                        // If parentPackageDependencies is empty then exit the graph traversal
                    }

                }
            }
        }

        private static WarningPropertiesCollection GetNodeWarningProperties(PackageSpec nodeProjectSpec)
        {
            return _warningPropertiesCache.GetOrAdd(nodeProjectSpec.RestoreMetadata.ProjectPath, 
                (s) => new WarningPropertiesCollection()
                {
                    ProjectWideWarningProperties = nodeProjectSpec.RestoreMetadata?.ProjectWideWarningProperties,
                    PackageSpecificWarningProperties = PackageSpecificWarningProperties.CreatePackageSpecificWarningProperties(nodeProjectSpec),
                    ProjectFrameworks = nodeProjectSpec.TargetFrameworks.Select(f => f.FrameworkName).AsList().AsReadOnly()
                });
        }

        private static PackageSpec GetNodePackageSpec(DependencyGraphSpec dgSpec, string nodeId)
        {
            return dgSpec
                .Projects
                .Where(p => string.Equals(p.RestoreMetadata.ProjectName, nodeId, StringComparison.OrdinalIgnoreCase))
                .First();
        }

        private static WarningPropertiesCollection MergeWarningPropertiesCollection(WarningPropertiesCollection first, 
            WarningPropertiesCollection second)
        {
            WarningPropertiesCollection result = null;

            if (TryMergeNullObjects(first, second, out object merged))
            {
                result = merged as WarningPropertiesCollection;
            }
            else
            {
                // Merge Project Wide Warning Properties
                var mergedProjectWideWarningProperties = MergeProjectWideWarningProperties(
                    first.ProjectWideWarningProperties, 
                    second.ProjectWideWarningProperties);

                // Merge Package Specific Warning Properties
                var mergedPackageSpecificWarnings = MergePackageSpecificWarningProperties(first.PackageSpecificWarningProperties,
                    second.PackageSpecificWarningProperties);
            }

            return result;
        }

        private static WarningProperties MergeProjectWideWarningProperties(WarningProperties first, 
            WarningProperties second)
        {
            WarningProperties result = null;

            if (TryMergeNullObjects(first, second, out object merged))
            {
                result = merged as WarningProperties;
            }
            else
            {
                // Merge WarningsAsErrors Sets.
                var mergedWarningsAsErrors = new HashSet<NuGetLogCode>();
                mergedWarningsAsErrors.UnionWith(first.WarningsAsErrors);
                mergedWarningsAsErrors.UnionWith(second.WarningsAsErrors);

                // Merge NoWarn Sets.
                var mergedNoWarn = new HashSet<NuGetLogCode>();
                mergedNoWarn.UnionWith(first.NoWarn);
                mergedNoWarn.UnionWith(second.NoWarn);

                // Merge AllWarningsAsErrors. If one project treats all warnigs as errors then the chain will too.
                var mergedAllWarningsAsErrors = first.AllWarningsAsErrors || second.AllWarningsAsErrors;

                result = new WarningProperties(mergedWarningsAsErrors, 
                    mergedNoWarn, 
                    mergedAllWarningsAsErrors);
            }

            return result;
        }

        private static PackageSpecificWarningProperties MergePackageSpecificWarningProperties(PackageSpecificWarningProperties first,
            PackageSpecificWarningProperties second)
        {
            PackageSpecificWarningProperties result = null;

            if (TryMergeNullObjects(first, second, out object merged))
            {
                result = merged as PackageSpecificWarningProperties;
            }
            else
            {
                result = new PackageSpecificWarningProperties();
                foreach (var code in first.Properties.Keys)
                {
                    foreach (var libraryId in first.Properties[code].Keys)
                    {
                        result.AddRange(code, libraryId, first.Properties[code][libraryId]);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Try to merge 2 objects if one or both of them are null.
        /// </summary>
        /// <param name="first">First Object to be merged.</param>
        /// <param name="second">First Object to be merged.</param>
        /// <param name="merged">Out Merged Object.</param>
        /// <returns>Returns true if atleast one of the objects was Null. 
        /// If none of them is null then the returns false, indicating that the merge failed.</returns>
        private static bool TryMergeNullObjects(object first, object second, out object merged)
        {
            merged = null;
            var result = false;

            if (first == null && second == null)
            {
                merged = null;
                result = true;
            }
            else if (first == null)
            {
                merged = second;
                result = true;
            }
            else if (second == null)
            {
                merged = first;
                result = true;
            }

            return result;
        }
    }
}