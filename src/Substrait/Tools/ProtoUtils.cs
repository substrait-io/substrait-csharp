// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Substrait.Core.Metadata;

namespace Substrait.Tools;

/// <summary>
/// Utility methods for protobuf values and collections.
/// </summary>
public static class ProtoUtils
{
    internal static ReadOnlyAny SnapshotAny(IMessage message)
    {
        _ = message ?? throw new ArgumentNullException(nameof(message));
        return message is Any any
            ? ReadOnlyAny.FromProto(any)
            : ReadOnlyAny.FromOwnedProto(Any.Pack(message));
    }

    /// <summary>
    /// Pre-allocates additional capacity and adds a range of values.
    /// </summary>
    /// <typeparam name="T">The repeated field element type.</typeparam>
    /// <param name="fields">The repeated field to populate.</param>
    /// <param name="count">The number of values to add.</param>
    /// <param name="values">The values to add.</param>
    public static void AllocateAndAddRange<T>(this RepeatedField<T> fields, int count, IEnumerable<T> values)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegative(count);
#else
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
#endif

        int requiredCapacity = checked(fields.Count + count);
        if (requiredCapacity > fields.Capacity)
        {
            fields.Capacity = requiredCapacity;
        }

        fields.AddRange(values);
    }
}
