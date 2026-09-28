// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;

namespace Substrait.Core.Plan;

/// <summary>
/// Provides information about the packaged Substrait specification version.
/// This can be used to populate the Version field in a plan if it is created
/// by this library.
/// </summary>
public static class CurrentVersion
{
    private static readonly System.Version ProtobufVersion = typeof(Protobuf.Plan).Assembly.GetName().Version
        ?? throw new InvalidOperationException("The Substrait protobuf assembly does not declare a version.");

    /// <summary>
    /// The current major version number information for Substrait.
    /// </summary>
    public static readonly uint MajorNumber = checked((uint)ProtobufVersion.Major);

    /// <summary>
    /// The current minor version number information for Substrait.
    /// </summary>
    public static readonly uint MinorNumber = checked((uint)ProtobufVersion.Minor);

    /// <summary>
    /// The current patch version number information Substrait.
    /// </summary>
    public static readonly uint PatchNumber = checked((uint)ProtobufVersion.Build);

    /// <summary>
    /// The specification commit recorded by the package, or empty for packages without source metadata.
    /// </summary>
    public static readonly string GitHash = GetSpecificationGitHash();

    /// <summary>
    /// The producer information for the Substrait plan (if any).
    /// </summary>
    public static readonly string Producer = string.Empty;

    private static string GetSpecificationGitHash()
    {
        AssemblyMetadataAttribute? metadata = typeof(Protobuf.Plan).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "SubstraitGitHash");
        if (metadata is null)
        {
            return string.Empty;
        }

        string? hash = metadata.Value;
        if (hash is null || hash.Length != 40 || hash.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new InvalidOperationException("The protobuf package's SubstraitGitHash is not a valid specification commit.");
        }

        return hash;
    }
}
