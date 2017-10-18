// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NuGet.Common;
using NuGet.Packaging;

namespace NuGet.Protocol
{
    /// <summary>
    /// Allow .nuspec files on disk to be cached across v3 folder readers.
    /// Allow the list of files in a package to be cached across all projects.
    /// </summary>
    /// <remarks>Nuspecs that do not exist are returned as null. It is expected that the caller has already verified
    /// the that folder and paths are valid.</remarks>
    public class LocalPackageFileCache
    {
        // Expanded path -> NuspecReader
        private readonly ConcurrentDictionary<string, Lazy<NuspecReader>> _nuspecCache
            = new ConcurrentDictionary<string, Lazy<NuspecReader>>(PathUtility.GetStringComparerBasedOnOS());

        // Expanded path -> Package file list
        private readonly ConcurrentDictionary<string, Lazy<IReadOnlyList<string>>> _filesCache
            = new ConcurrentDictionary<string, Lazy<IReadOnlyList<string>>>(PathUtility.GetStringComparerBasedOnOS());

        public LocalPackageFileCache()
        {
        }

        /// <summary>
        /// Read a nuspec file from disk. The nuspec is expected to exist.
        /// </summary>
        public Lazy<NuspecReader> GetOrAddNuspec(string manifestPath, string expandedPath)
        {
            return _nuspecCache.GetOrAdd(expandedPath,
                e => new Lazy<NuspecReader>(() => GetNuspec(manifestPath, expandedPath)));
        }

        /// <summary>
        /// Read a the package files from disk.
        /// </summary>
        public Lazy<IReadOnlyList<string>> GetOrAddFiles(string expandedPath)
        {
            return _filesCache.GetOrAdd(expandedPath,
                e => new Lazy<IReadOnlyList<string>>(() => GetFiles(expandedPath)));
        }

        /// <summary>
        /// Read files from a package folder.
        /// </summary>
        private static IReadOnlyList<string> GetFiles(string expandedPath)
        {
            using (var packageReader = new PackageFolderReader(expandedPath))
            {
                // Get package files, excluding directory entries and OPC files
                // This is sorted before it is written out
                return packageReader.GetFiles()
                    .Where(file => IsAllowedLibraryFile(file))
                    .ToList();
            }
        }

        /// <summary>
        /// True if the file should be added to the lock file library
        /// Fale if it is an OPC file or empty directory
        /// </summary>
        private static bool IsAllowedLibraryFile(string path)
        {
            switch (path)
            {
                case "_rels/.rels":
                case "[Content_Types].xml":
                    return false;
            }

            if (path.EndsWith("/", StringComparison.Ordinal)
                || path.EndsWith(".psmdcp", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Search for a nuspec using the given path, or by the expanded folder path.
        /// The manifest path here is a shortcut to use the already constructed well
        /// known location, if this doesn't exist the folder reader will find the nuspec
        /// if it exists.
        /// </summary>
        private static NuspecReader GetNuspec(string manifestPath, string expandedPath)
        {
            NuspecReader nuspec = null;

            // Verify that the nuspec has the correct name before opening it
            if (File.Exists(manifestPath))
            {
                nuspec = new NuspecReader(File.OpenRead(manifestPath));
            }
            else
            {
                // Scan the folder for the nuspec
                var folderReader = new PackageFolderReader(expandedPath);

                // This will throw if the nuspec is not found
                nuspec = new NuspecReader(folderReader.GetNuspec());
            }

            return nuspec;
        }
    }
}
