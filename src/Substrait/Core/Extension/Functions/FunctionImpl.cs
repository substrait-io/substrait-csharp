// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Text;

namespace Substrait.Core.Extension.Functions;

/// <summary>
/// Definition of a function implementation.
/// </summary>
public abstract class FunctionImpl
{
    private readonly Lazy<IReadOnlyList<IArgument>> requiredArguments;
    private readonly Lazy<string> key;
    private readonly Lazy<FunctionImplAnchor> anchor;

    /// <summary>
    /// Initializes a new instance of the <see cref="FunctionImpl"/> class.
    /// </summary>
    public FunctionImpl()
    {
        this.requiredArguments = new Lazy<IReadOnlyList<IArgument>>(() =>
        {
            return this.Args.Where(arg => arg.Required).ToImmutableList();
        });
        this.key = new Lazy<string>(() =>
        {
            return ConstructKey(this.Name, this.Args);
        });
        this.anchor = new Lazy<FunctionImplAnchor>(() =>
        {
            return new FunctionImplAnchor(this.Uri, this.Key);
        });
    }

    /// <summary>
    /// Nullability enum.
    /// </summary>
    public enum NullabilityMode
    {
        /// <summary>
        /// Mirror.
        /// </summary>
        Mirror,

        /// <summary>
        /// Declared output.
        /// </summary>
        Declared_output,

        /// <summary>
        /// Discrete.
        /// </summary>
        Discrete,
    }

    /// <summary>
    /// Decomposability enum.
    /// </summary>
    public enum DecomposabilityMode
    {
        /// <summary>
        /// None.
        /// </summary>
        None,

        /// <summary>
        /// One.
        /// </summary>
        One,

        /// <summary>
        /// Many.
        /// </summary>
        Many,
    }

    /// <summary>
    /// Gets name of the function.
    /// Note. We can't use null detection here since we initially construct this with a parent name.
    /// </summary>
    public virtual string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets name of the function.
    /// Note. We can't use null detection here since we initially construct this without a uri, then
    /// resolve later.
    /// </summary>
    public virtual string Uri { get; init; } = string.Empty;

    /// <summary>
    /// Gets variadic behavior of the function.
    /// </summary>
    public abstract IVariadicBehavior? Variadic { get; init; }

    /// <summary>
    /// Gets description of the function.
    /// </summary>
    public virtual string Description { get; init; } = string.Empty;

    /// <summary>Gets the deprecation information, when this implementation is deprecated.</summary>
    public Deprecation? Deprecated { get; init; }

    /// <summary>Describes a deprecated function implementation.</summary>
    public sealed class Deprecation
    {
        /// <summary>Gets the specification version that deprecated the implementation.</summary>
        public string Since { get; init; } = string.Empty;

        /// <summary>Gets the reason for deprecation.</summary>
        public string Reason { get; init; } = string.Empty;
    }

    /// <summary>
    /// Gets function arguments.
    /// </summary>
    public abstract IReadOnlyList<IArgument> Args { get; init; }

    /// <summary>
    /// Gets function options.
    /// </summary>
    public abstract IReadOnlyDictionary<string, IOption> Options { get; init; }

    /// <summary>
    /// Gets required arguments.
    /// </summary>
    /// <returns>The required arguments.</returns>
    public IReadOnlyList<IArgument> RequiredArguments { get => this.requiredArguments.Value; }

    /// <summary>
    /// Gets nullability of the function.
    /// </summary>
    public virtual NullabilityMode Nullability { get; init; } = NullabilityMode.Mirror;

    /// <summary>
    /// Gets whether the result of this function is sensitive to sort order.
    /// </summary>
    public abstract bool? Ordered { get; init; }

    /// <summary>
    /// Gets the key of the function.
    /// </summary>
    /// <returns>The key of the function.</returns>
    public string Key { get => this.key.Value; }

    /// <summary>
    /// Gets the anchor of the function.
    /// </summary>
    /// <returns>An anchor of the function.</returns>
    public FunctionImplAnchor Anchor { get => this.anchor.Value; }

    /// <summary>
    /// Gets the return type of the function.
    /// </summary>
    public abstract string Return { get; init; }

    /// <summary>
    /// Constructs a key from name and input arguments.
    /// </summary>
    /// <param name="name">Name of the function.</param>
    /// <param name="arguments">Function arguments.</param>
    /// <returns>The key constructed from name and input types.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the types cannot be converted to string.</exception>
    public static string ConstructKey(string name, IReadOnlyList<IArgument> arguments)
    {
        try
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.Append(name).Append(':');
            foreach (var argument in arguments)
            {
                stringBuilder.Append(argument.ToTypeString()).Append('_');
            }

            if (arguments.Count > 0)
            {
                --stringBuilder.Length; // Remove the last character
            }

            return stringBuilder.ToString();
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"Failure converting types of function {name}.", ex);
        }
    }

    /// <summary>
    /// String representation of the function.
    /// </summary>
    /// <returns>The string representation of the function.</returns>
    public override string ToString()
    {
        return this.Key;
    }

    /// <summary>
    /// Gets the range of number of parameters for the function.
    /// </summary>
    /// <returns>A tuple of minimum and maximum number of parameters.</returns>
    public Tuple<int, int> GetRange()
    {
        int max, min;
        if (this.Variadic != null)
        {
            max = this.Variadic.Max != null ? this.Args.Count - 1 + (int)this.Variadic.Max : int.MaxValue;
            min = this.Args.Count - 1 + this.Variadic.Min;
        }
        else
        {
            max = this.Args.Count;
            min = this.RequiredArguments.Count;
        }

        return new Tuple<int, int>(min, max);
    }
}
