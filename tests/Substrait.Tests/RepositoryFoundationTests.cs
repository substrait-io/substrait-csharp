// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Substrait.Tests;

[TestClass]
public sealed class RepositoryFoundationTests
{
    [TestMethod]
    public void LibraryUsesExpectedAssemblyName()
    {
        string assemblyPath = Path.Combine(AppContext.BaseDirectory, "Substrait.Net.dll");
        AssemblyName assemblyName = AssemblyName.GetAssemblyName(assemblyPath);

        Assert.AreEqual("Substrait.Net", assemblyName.Name);
    }
}
