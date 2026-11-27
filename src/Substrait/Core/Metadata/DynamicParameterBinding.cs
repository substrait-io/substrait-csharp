// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Expression;

namespace Substrait.Core.Metadata;

/// <summary>
/// The runtime value bound to a dynamic parameter.
/// </summary>
public sealed class DynamicParameterBinding : IEquatable<DynamicParameterBinding>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicParameterBinding"/> class.
    /// </summary>
    /// <param name="parameterAnchor">The anchor of the bound dynamic parameter. 0 is a valid anchor.</param>
    /// <param name="value">The bound value.</param>
    public DynamicParameterBinding(uint parameterAnchor, Literal value)
    {
        this.ParameterAnchor = parameterAnchor;
        this.Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets the anchor of the bound dynamic parameter.
    /// </summary>
    public uint ParameterAnchor { get; }

    /// <summary>
    /// Gets the bound value.
    /// </summary>
    public Literal Value { get; }

    /// <inheritdoc/>
    public bool Equals(DynamicParameterBinding? other) => other is not null && this.ParameterAnchor == other.ParameterAnchor && this.Value.Equals(other.Value);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => this.Equals(obj as DynamicParameterBinding);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(nameof(DynamicParameterBinding), this.ParameterAnchor, this.Value);
}
