// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using NuGet.Packaging;
using NuGet.Versioning;

namespace NuGet.Repositories
{
    public class LocalPackageInfo
    {
        private readonly Lazy<NuspecReader> _nuspec;
        private readonly Lazy<IReadOnlyList<string>> _files;

        public LocalPackageInfo(
            string packageId,
            NuGetVersion version,
            string path,
            string manifestPath,
            string zipPath,
            Lazy<NuspecReader> nuspec,
            Lazy<IReadOnlyList<string>> files)
        {
            Id = packageId;
            Version = version;
            ExpandedPath = path;
            ManifestPath = manifestPath;
            ZipPath = zipPath;
            _nuspec = nuspec;
            _files = files;
        }

        public string Id { get; }

        public NuGetVersion Version { get; }

        public string ExpandedPath { get; set; }

        public string ManifestPath { get; }

        public string ZipPath { get; }

        /// <summary>
        /// Caches the nuspec reader.
        /// If the nuspec does not exist this will throw a friendly exception.
        /// </summary>
        public NuspecReader Nuspec => _nuspec.Value;

        /// <summary>
        /// Package files with OPC files filtered out.
        /// Cached to avoid reading the same files multiple times.
        /// </summary>
        public IReadOnlyList<string> Files => _files.Value;

        public override string ToString()
        {
            return Id + " " + Version + " (" + (ManifestPath ?? ZipPath) + ")";
        }
    }
}
