// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Substrait.Core.Plan;
using Substrait.Core.Relation;
using Substrait.Core.Type;

namespace Substrait.Tests.Core;

public sealed class PlanTests
{
    [Test]
    public async Task CurrentVersionMatchesSubstraitPackages()
    {
        System.Version? expected = typeof(Substrait.Protobuf.Plan).Assembly.GetName().Version;
        await Assert.That(expected).IsNotNull();

        await Assert.That(CurrentVersion.MajorNumber).IsEqualTo((uint)expected.Major);
        await Assert.That(CurrentVersion.MinorNumber).IsEqualTo((uint)expected.Minor);
        await Assert.That(CurrentVersion.PatchNumber).IsEqualTo((uint)expected.Build);
        await Assert.That(Substrait.Core.Plan.Version.Current.MajorNumber).IsEqualTo((uint)expected.Major);
        await Assert.That(Substrait.Core.Plan.Version.Current.MinorNumber).IsEqualTo((uint)expected.Minor);
        await Assert.That(Substrait.Core.Plan.Version.Current.PatchNumber).IsEqualTo((uint)expected.Build);
        string expectedHash = typeof(Substrait.Protobuf.Plan).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "SubstraitGitHash")?.Value ?? string.Empty;
        await Assert.That(CurrentVersion.GitHash).IsEqualTo(expectedHash);
        await Assert.That(Substrait.Core.Plan.Version.Current.GitHash).IsEqualTo(expectedHash);
    }

    [Test]
    public async Task EquivalentPlansHaveEqualRootsAndVersions()
    {
        Plan first = CreatePlan();
        Plan equivalent = CreatePlan();

        await Assert.That(equivalent).IsEqualTo(first);
        await Assert.That(equivalent.GetHashCode()).IsEqualTo(first.GetHashCode());
    }

    private static Plan CreatePlan()
    {
        NamedStruct schema = new(["value"], TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64]));
        NamedTableRead read = new(schema, ["orders"], filter: null);
        Plan.Root root = new(read, ["value"]);
        return new Plan([root], Substrait.Core.Plan.Version.Current);
    }
}
