// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Substrait.Tools;

namespace Substrait.Tests.Tools;

public sealed class ObjectUtilsTests
{
    private static readonly int[] Values = [1, 2, 3];
    private static readonly int[] ReorderedValues = [3, 2, 1];

    [Test]
    public async Task CombineHashCodesIsDeterministicAndOrderSensitive()
    {
        int hash = Values.CombineHashCodes();

        await Assert.That(Values.CombineHashCodes()).IsEqualTo(hash);
        await Assert.That(ReorderedValues.CombineHashCodes()).IsNotEqualTo(hash);
    }
}
