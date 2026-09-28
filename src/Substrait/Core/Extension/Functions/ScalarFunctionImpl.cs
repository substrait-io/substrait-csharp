// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;

namespace Substrait.Core.Extension.Functions;

/// <summary>
/// An immutable implementation representing a scalar function implementation.
/// </summary>
public sealed class ScalarFunctionImpl : FunctionImpl
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

    /// <summary>
    /// Initializes a new instance of the <see cref="ScalarFunctionImpl"/> class.
    /// </summary>
    /// <param name="uri">URI of the scalar function variant.</param>
    /// <param name="name">Name of the scalar function variant.</param>
    /// <param name="description">Description of the scalar function variant.</param>
    /// <param name="nullability">Nullability of the scalar function variant.</param>
    /// <param name="args">Arguments of the scalar function variant.</param>
    /// <param name="options">Options of the scalar function variant.</param>
    /// <param name="ordered">Whether the scalar function variant is ordered.</param>
    /// <param name="variadic">Whether the scalar function variant is variadic.</param>
    /// <param name="returnType">The return type of the scalar function variant.</param>
    public ScalarFunctionImpl(string uri, string name, string description, NullabilityMode nullability, IEnumerable<IArgument> args, IEnumerable<KeyValuePair<string, IOption>> options, bool? ordered, IVariadicBehavior? variadic, string returnType)
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
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScalarFunctionImpl"/> class.
    /// This is only used for deserialization purposes from YAML files.
    /// </summary>
    public ScalarFunctionImpl()
        : this(string.Empty, string.Empty, string.Empty, NullabilityMode.Mirror, ImmutableList<IArgument>.Empty, ImmutableDictionary<string, IOption>.Empty, null, null, string.Empty)
    {
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
    /// Resolves the scalar function implementation.
    /// </summary>
    /// <param name="uri">The URI on which the scalar function implementation should be resolved.</param>
    /// <param name="name">The name of the scalar function implementation.</param>
    /// <param name="description">The description of the scalar function implementation.</param>
    /// <returns>The scalar function implementation.</returns>
    public ScalarFunctionImpl Resolve(string uri, string name, string description)
    {
        return new ScalarFunctionImpl(uri, name, description, this.Nullability, this.Args, this.Options, this.Ordered, this.Variadic, this.Return)
        {
            Deprecated = this.Deprecated,
        };
    }
}
