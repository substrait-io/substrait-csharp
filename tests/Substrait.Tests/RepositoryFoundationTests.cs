// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;

namespace Substrait.Tests;

public sealed class RepositoryFoundationTests
{
    [Test]
    public async Task LibraryUsesExpectedAssemblyName()
    {
        string assemblyPath = Path.Combine(AppContext.BaseDirectory, "Substrait.Net.dll");
        AssemblyName assemblyName = AssemblyName.GetAssemblyName(assemblyPath);

        await Assert.That(assemblyName.Name).IsEqualTo("Substrait.Net");
    }
}
