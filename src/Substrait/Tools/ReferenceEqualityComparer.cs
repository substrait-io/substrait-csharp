// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

#if !NET5_0_OR_GREATER
using System.Runtime.CompilerServices;

namespace Substrait.Tools;

internal sealed class ReferenceEqualityComparer : IEqualityComparer<object>
{
    internal static readonly ReferenceEqualityComparer Instance = new();

    public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

    public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
}
#endif
