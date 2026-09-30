// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace Substrait.Core.Extension.Functions;

/// <summary>
/// An immutable implementation representing a window function implementation.
/// </summary>
public sealed class WindowFunctionImpl : FunctionImpl
{
    private readonly string uri;
    private readonly string name;
    private readonly string description;
    private readonly NullabilityMode nullability;
    private readonly IReadOnlyList<IArgument> args;
    private readonly IReadOnlyDictionary<string, IOption> options;
    private readonly bool? ordered;
    private readonly IVariadicBehavior? variadic;
    private readonly string returnType;
    private readonly DecomposabilityMode decomposable;
    private readonly string intermediate;
    private readonly WindowMode windowType;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowFunctionImpl"/> class.
    /// </summary>
    /// <param name="uri">URI of the window function variant.</param>
    /// <param name="name">Name of the window function variant.</param>
    /// <param name="description">Description of the window function variant.</param>
    /// <param name="nullability">Nullability of the window function variant.</param>
    /// <param name="args">Arguments of the window function variant.</param>
    /// <param name="options">Options of the window function variant.</param>
    /// <param name="ordered">Whether the result of this window function is sensitive to sort order.</param>
    /// <param name="variadic">Whether the window function variant is variadic.</param>
    /// <param name="returnType">The return type of the window function variant.</param>
    /// <param name="decomposable">Decomposability of the window function variant.</param>
    /// <param name="intermediate">Intermediate type of the window function variant.</param>
    /// <param name="windowType">Type of the window function variant.</param>
    public WindowFunctionImpl(string uri, string name, string description, NullabilityMode nullability, IEnumerable<IArgument> args, IEnumerable<KeyValuePair<string, IOption>> options, bool? ordered, IVariadicBehavior? variadic, string returnType, DecomposabilityMode decomposable, string intermediate, WindowMode windowType)
    {
        this.uri = uri;
        this.name = name;
        this.description = description;
        this.nullability = nullability;
        this.args = args.ToImmutableList();
        this.options = options.ToImmutableDictionary();
        this.ordered = ordered;
        this.variadic = variadic;
        this.returnType = returnType;
        this.decomposable = decomposable;
        this.intermediate = intermediate;
        this.windowType = windowType;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowFunctionImpl"/> class.
    /// </summary>
    /// <param name="afi">Aggregate function variant to base the window function variant on.</param>
    /// <param name="decomposable">Decomposability of the window function variant.</param>
    /// <param name="intermediate">Intermediate type of the window function variant.</param>
    /// <param name="windowType">Type of the window function variant.</param>
    public WindowFunctionImpl(AggregateFunctionImpl afi, DecomposabilityMode decomposable, string intermediate, WindowMode windowType)
    {
        this.uri = afi.Uri;
        this.name = afi.Name;
        this.description = afi.Description;
        this.Deprecated = afi.Deprecated;
        this.nullability = afi.Nullability;
        this.args = afi.Args.ToImmutableList();
        this.options = afi.Options.ToImmutableDictionary();
        this.ordered = afi.Ordered;
        this.variadic = afi.Variadic;
        this.returnType = afi.Return;
        this.decomposable = decomposable;
        this.intermediate = intermediate;
        this.windowType = windowType;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowFunctionImpl"/> class.
    /// This is only used for deserialization purposes from YAML files.
    /// </summary>
    public WindowFunctionImpl()
        : this(string.Empty, string.Empty, string.Empty, NullabilityMode.Mirror, ImmutableList<IArgument>.Empty, ImmutableDictionary<string, IOption>.Empty, null, null, string.Empty, DecomposabilityMode.None, string.Empty, WindowMode.Partition)
    {
    }

    /// <summary>
    /// Window type enum.
    /// </summary>
    public enum WindowMode
    {
        /// <summary>
        /// Partition.
        /// </summary>
        Partition,

        /// <summary>
        /// Streaming.
        /// </summary>
        Streaming,
    }

    /// <inheritdoc/>
    public override string Name
    {
        get => this.name;
        init => this.name = value;
    }

    /// <inheritdoc/>
    public override string Uri
    {
        get => this.uri;
        init => this.uri = value;
    }

    /// <inheritdoc/>
    public override string Description
    {
        get => this.description;
        init => this.description = value;
    }

    /// <inheritdoc/>
    public override NullabilityMode Nullability
    {
        get => this.nullability;
        init => this.nullability = value;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<IArgument> Args
    {
        get => this.args;
        init => this.args = value.ToImmutableList();
    }

    /// <inheritdoc/>
    public override IReadOnlyDictionary<string, IOption> Options
    {
        get => this.options;
        init => this.options = value.ToImmutableDictionary();
    }

    /// <inheritdoc/>
    public override bool? Ordered
    {
        get => this.ordered;
        init => this.ordered = value;
    }

    /// <inheritdoc/>
    public override IVariadicBehavior? Variadic
    {
        get => this.variadic;
        init => this.variadic = value;
    }

    /// <inheritdoc/>
    public override string Return
    {
        get => this.returnType;
        init => this.returnType = value;
    }

    /// <summary>
    /// Gets the decomposability of the window function.
    /// </summary>
    public DecomposabilityMode Decomposable
    {
        get => this.decomposable;
        init => this.decomposable = value;
    }

    /// <summary>
    /// Gets the intermediate type of the window function.
    /// </summary>
    public string Intermediate
    {
        get => this.intermediate;
        init => this.intermediate = value;
    }

    /// <summary>
    /// Gets the window type of the window function.
    /// </summary>
    public WindowMode WindowType
    {
        get => this.windowType;
        init => this.windowType = value;
    }

    /// <summary>
    /// Resolves the window function implementation.
    /// </summary>
    /// <param name="uri">The URI on which the window function implementation should be resolved.</param>
    /// <param name="name">The name of the window function implementation.</param>
    /// <param name="description">The description of the window function implementation.</param>
    /// <returns>The window function implementation.</returns>
    public WindowFunctionImpl Resolve(string uri, string name, string description)
    {
        return new WindowFunctionImpl(uri, name, description, this.Nullability, this.Args, this.Options, this.Ordered, this.Variadic, this.Return, this.Decomposable, this.Intermediate, this.WindowType)
        {
            Deprecated = this.Deprecated,
        };
    }
}
