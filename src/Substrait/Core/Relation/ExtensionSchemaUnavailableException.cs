// SPDX-License-Identifier: Apache-2.0

namespace Substrait.Core.Relation;

/// <summary>A custom relation's schema was requested without a known schema contract.</summary>
public sealed class ExtensionSchemaUnavailableException : InvalidOperationException
{
    /// <summary>Initializes an exception for an unresolved extension schema.</summary>
    public ExtensionSchemaUnavailableException()
        : base("The extension schema is unavailable. Supply an unmapped schema or an extension schema resolver.")
    {
    }

    /// <summary>Initializes an exception with a diagnostic message.</summary>
    /// <param name="message">The diagnostic message.</param>
    public ExtensionSchemaUnavailableException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes an exception with a diagnostic message and cause.</summary>
    /// <param name="message">The diagnostic message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public ExtensionSchemaUnavailableException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    internal ExtensionSchemaUnavailableException(ExtensionRelationKind kind, string? typeUrl)
        : base($"The {kind} extension schema is unavailable (detail type URL: '{typeUrl ?? "<absent>"}'). Supply an unmapped schema or an extension schema resolver.")
    {
    }
}
