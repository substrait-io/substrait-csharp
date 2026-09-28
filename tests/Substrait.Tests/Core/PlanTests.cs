// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Substrait.Core.Plan;
using Substrait.Core.Relation;
using Substrait.Core.Type;

namespace Substrait.Tests.Core;

[TestClass]
public sealed class PlanTests
{
    [TestMethod]
    public void CurrentVersionMatchesSubstraitPackages()
    {
        System.Version? expected = typeof(Substrait.Protobuf.Plan).Assembly.GetName().Version;
        Assert.IsNotNull(expected);

        Assert.AreEqual((uint)expected.Major, CurrentVersion.MajorNumber);
        Assert.AreEqual((uint)expected.Minor, CurrentVersion.MinorNumber);
        Assert.AreEqual((uint)expected.Build, CurrentVersion.PatchNumber);
        Assert.AreEqual((uint)expected.Major, Substrait.Core.Plan.Version.Current.MajorNumber);
        Assert.AreEqual((uint)expected.Minor, Substrait.Core.Plan.Version.Current.MinorNumber);
        Assert.AreEqual((uint)expected.Build, Substrait.Core.Plan.Version.Current.PatchNumber);
        string expectedHash = typeof(Substrait.Protobuf.Plan).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "SubstraitGitHash")?.Value ?? string.Empty;
        Assert.AreEqual(expectedHash, CurrentVersion.GitHash);
        Assert.AreEqual(expectedHash, Substrait.Core.Plan.Version.Current.GitHash);
    }

    [TestMethod]
    public void EquivalentPlansHaveEqualRootsAndVersions()
    {
        Plan first = CreatePlan();
        Plan equivalent = CreatePlan();

        Assert.AreEqual(first, equivalent);
        Assert.AreEqual(first.GetHashCode(), equivalent.GetHashCode());
    }

    private static Plan CreatePlan()
    {
        NamedStruct schema = new(["value"], TypeFactory.REQUIRED.Struct([TypeFactory.REQUIRED.I64]));
        NamedTableRead read = new(schema, ["orders"], filter: null);
        Plan.Root root = new(read, ["value"]);
        return new Plan([root], Substrait.Core.Plan.Version.Current);
    }
}
