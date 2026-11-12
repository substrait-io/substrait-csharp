// SPDX-License-Identifier: Apache-2.0

using Substrait.Tools.Visitor;

namespace Substrait.Core.Type;

/// <summary>
/// Immutable, plan-scoped table of type-alias definitions (<c>Plan.type_aliases</c>). Validates
/// every definition and the whole alias dependency graph once, at construction, then resolves any
/// <see cref="ParameterizedType.AliasReference"/> found anywhere in a type tree against it.
/// </summary>
public sealed class TypeAliasTable
{
    private readonly IReadOnlyDictionary<uint, IType> definitions;
    private readonly IReadOnlyDictionary<uint, IType> resolvedDefinitions;

    private TypeAliasTable(IReadOnlyDictionary<uint, IType> definitions, IReadOnlyDictionary<uint, IType> resolvedDefinitions)
    {
        this.definitions = definitions;
        this.resolvedDefinitions = resolvedDefinitions;
    }

    /// <summary>Gets the alias table with no definitions.</summary>
    public static TypeAliasTable Empty { get; } = new(new Dictionary<uint, IType>(), new Dictionary<uint, IType>());

    /// <summary>Gets the number of alias definitions in this table.</summary>
    public int Count => this.definitions.Count;

    /// <summary>
    /// Gets each alias anchor paired with its definition exactly as declared - preserving any nested
    /// alias reference rather than the flattened form <see cref="Resolve(IType)"/> resolves against.
    /// </summary>
    public IReadOnlyDictionary<uint, IType> Definitions => this.definitions;

    /// <summary>
    /// Resolves <paramref name="type"/>, replacing every alias reference found anywhere in it -
    /// nested in a struct field, list element, map key/value, user-defined data-type parameter, or
    /// function parameter/return type - with its definition from this table. Each reference's own
    /// nullability replaces its definition's root nullability; the definition's own type variation
    /// and nested metadata are preserved, and the definition itself is never mutated.
    /// </summary>
    /// <param name="type">The type tree to resolve.</param>
    /// <returns>The resolved type tree.</returns>
    /// <exception cref="ArgumentException">An alias reference in <paramref name="type"/> does not
    /// specify nullability, or names an anchor absent from this table.</exception>
    public IType Resolve(IType type) => Rewrite(type ?? throw new ArgumentNullException(nameof(type)), this.resolvedDefinitions, path: null, definitionAnchor: null);

    private static IType Rewrite(IType root, IReadOnlyDictionary<uint, IType> resolvedDefinitions, RewritePath? path, uint? definitionAnchor)
    {
        var stack = new Stack<(IType Current, List<IType> CollectedTypes, List<IType>? NestedTypes, RewritePath? Path)>();
        var rootTypes = new List<IType>(1);
        stack.Push((root, rootTypes, null, path));

        while (stack.Count > 0)
        {
            var (current, collectedTypes, nestedTypes, currentPath) = stack.Pop();
            switch (current)
            {
                case ParameterizedType.AliasReference reference:
                    if (reference.Nullable is not (IType.NullableType.Required or IType.NullableType.Nullable))
                    {
                        throw new ArgumentException($"Alias reference to anchor {reference.Anchor}{FormatSuffix(currentPath)} must specify nullability.");
                    }

                    if (!resolvedDefinitions.TryGetValue(reference.Anchor, out IType? target))
                    {
                        throw new ArgumentException($"Alias reference to anchor {reference.Anchor}{FormatSuffix(currentPath)} has no definition in this table.");
                    }

                    collectedTypes.Add(TypeFactory.Of(reference.Nullable).ResolveTypeWithNullability(target));
                    break;

                case ParameterizedType.Struct @struct:
                    if (nestedTypes is not null)
                    {
                        collectedTypes.Add(TypeFactory.Of(@struct.Nullable).Struct(nestedTypes, @struct.TypeVariation));
                    }
                    else
                    {
                        var childTypes = new List<IType>(@struct.Fields.Count);
                        stack.Push((current, collectedTypes, childTypes, currentPath));
                        for (int index = @struct.Fields.Count - 1; index >= 0; --index)
                        {
                            stack.Push((@struct.Fields[index], childTypes, null, new RewritePath(currentPath, $"struct field {index}")));
                        }
                    }

                    break;

                case ParameterizedType.List list:
                    if (nestedTypes is not null)
                    {
                        collectedTypes.Add(TypeFactory.Of(list.Nullable).List(nestedTypes[0], list.TypeVariation));
                    }
                    else
                    {
                        var childTypes = new List<IType>(1);
                        stack.Push((current, collectedTypes, childTypes, currentPath));
                        stack.Push((list.ElementType, childTypes, null, new RewritePath(currentPath, "list element")));
                    }

                    break;

                case ParameterizedType.Map map:
                    if (nestedTypes is not null)
                    {
                        collectedTypes.Add(TypeFactory.Of(map.Nullable).Map(nestedTypes[0], nestedTypes[1], map.TypeVariation));
                    }
                    else
                    {
                        var childTypes = new List<IType>(2);
                        stack.Push((current, collectedTypes, childTypes, currentPath));
                        stack.Push((map.ValueType, childTypes, null, new RewritePath(currentPath, "map value")));
                        stack.Push((map.KeyType, childTypes, null, new RewritePath(currentPath, "map key")));
                    }

                    break;

                case ParameterizedType.Func func:
                    if (nestedTypes is not null)
                    {
                        int returnIndex = nestedTypes.Count - 1;
                        collectedTypes.Add(TypeFactory.Of(func.Nullable).Func(nestedTypes.Take(returnIndex), nestedTypes[returnIndex]));
                    }
                    else
                    {
                        var childTypes = new List<IType>(func.Parameters.Count + 1);
                        stack.Push((current, collectedTypes, childTypes, currentPath));
                        stack.Push((func.ReturnType, childTypes, null, new RewritePath(currentPath, "function return type")));
                        for (int index = func.Parameters.Count - 1; index >= 0; --index)
                        {
                            stack.Push((func.Parameters[index], childTypes, null, new RewritePath(currentPath, $"function parameter {index}")));
                        }
                    }

                    break;

                case UserDefinedType userDefined:
                    if (nestedTypes is not null)
                    {
                        int childIndex = -1;
                        collectedTypes.Add(TypeFactory.Of(userDefined.Nullable).UserDefined(
                            userDefined.Anchor,
                            userDefined.Parameters.Select(parameter => parameter is TypeParameter.DataType
                                ? new TypeParameter.DataType(nestedTypes[++childIndex])
                                : parameter),
                            userDefined.TypeVariation,
                            userDefined.Declaration));
                    }
                    else
                    {
                        if (definitionAnchor is not null)
                        {
                            for (int index = 0; index < userDefined.Parameters.Count; ++index)
                            {
                                if (userDefined.Parameters[index] is TypeParameter.Null)
                                {
                                    throw new ArgumentException($"Alias anchor {definitionAnchor}: a definition must specify every type parameter, but user-defined type {userDefined.Anchor.Namespace}#{userDefined.Anchor.Key} parameter {index} is unspecified{FormatSuffix(currentPath)}.");
                                }
                            }
                        }

                        var childTypes = new List<IType>();
                        stack.Push((current, collectedTypes, childTypes, currentPath));
                        for (int index = userDefined.Parameters.Count - 1; index >= 0; --index)
                        {
                            if (userDefined.Parameters[index] is TypeParameter.DataType dataType)
                            {
                                stack.Push((dataType.Value, childTypes, null, new RewritePath(currentPath, $"user-defined type {userDefined.Anchor.Namespace}#{userDefined.Anchor.Key} parameter {index}")));
                            }
                        }
                    }

                    break;

                case UnboundType when definitionAnchor is not null:
                    throw new ArgumentException($"Alias anchor {definitionAnchor}: a definition must be a concrete type, but contains the unbound placeholder{FormatSuffix(currentPath)}.");

                default:
                    collectedTypes.Add(current);
                    break;
            }
        }

        return rootTypes[0];
    }

    private static string FormatSuffix(RewritePath? path)
    {
        if (path is null)
        {
            return string.Empty;
        }

        var segments = new Stack<string>();
        for (RewritePath? current = path; current is not null; current = current.Parent)
        {
            segments.Push(current.Description);
        }

        return $" at {string.Join(" -> ", segments)}";
    }

    private sealed class RewritePath(RewritePath? parent, string description)
    {
        public RewritePath? Parent { get; } = parent;

        public string Description { get; } = description;
    }

    /// <summary>Builds a <see cref="TypeAliasTable"/>, validating definitions and the dependency
    /// graph as a whole.</summary>
    public sealed class Builder
    {
        private readonly Dictionary<uint, IType> definitions = [];

        /// <summary>
        /// Adds an alias definition.
        /// </summary>
        /// <param name="anchor">The plan-local alias anchor. 0 is a valid anchor.</param>
        /// <param name="definition">The concrete type the anchor aliases.</param>
        /// <returns>This builder.</returns>
        /// <exception cref="ArgumentException"><paramref name="definition"/> is itself an alias
        /// reference, is the unbound placeholder, or <paramref name="anchor"/> is already defined.</exception>
        public Builder Add(uint anchor, IType definition)
        {
            _ = definition ?? throw new ArgumentNullException(nameof(definition));
            if (definition is ParameterizedType.AliasReference)
            {
                throw new ArgumentException($"Alias anchor {anchor}: a definition cannot directly be another alias reference.", nameof(definition));
            }

            if (definition is UnboundType)
            {
                throw new ArgumentException($"Alias anchor {anchor}: a definition must be a concrete type, not the unbound placeholder.", nameof(definition));
            }

            try
            {
                this.definitions.Add(anchor, definition);
            }
            catch (ArgumentException) when (this.definitions.ContainsKey(anchor))
            {
                throw new ArgumentException($"Duplicate alias anchor {anchor}.", nameof(anchor));
            }

            return this;
        }

        /// <summary>
        /// Builds the table, validating that every alias reference nested in a definition resolves
        /// to another definition in this same table, and that the alias dependency graph has no
        /// cycle (self or indirect).
        /// </summary>
        /// <returns>The validated, immutable alias table.</returns>
        /// <exception cref="ArgumentException">A definition references a missing alias anchor,
        /// contains the unbound placeholder or an unspecified user-defined type parameter at any
        /// depth, or the alias dependency graph contains a cycle.</exception>
        public TypeAliasTable Build()
        {
            if (this.definitions.Count == 0)
            {
                return Empty;
            }

            var dependencies = new Dictionary<uint, List<uint>>(this.definitions.Count);
            foreach (KeyValuePair<uint, IType> entry in this.definitions)
            {
                uint anchor = entry.Key;
                var deps = new List<uint>();
                foreach (var nested in new TopDownTraversal<IType>().Traverse(entry.Value).OfType<ParameterizedType.AliasReference>())
                {
                    if (!this.definitions.ContainsKey(nested.Anchor))
                    {
                        throw new ArgumentException($"Alias anchor {anchor} references missing alias anchor {nested.Anchor}.");
                    }

                    deps.Add(nested.Anchor);
                }

                dependencies[anchor] = deps;
            }

            IReadOnlyList<uint> order = GetDependencyOrder(dependencies);

            var resolved = new Dictionary<uint, IType>(this.definitions.Count);
            foreach (uint anchor in order)
            {
                resolved[anchor] = Rewrite(this.definitions[anchor], resolved, path: null, definitionAnchor: anchor);
            }

            return new TypeAliasTable(new Dictionary<uint, IType>(this.definitions), resolved);
        }

        private static IReadOnlyList<uint> GetDependencyOrder(IReadOnlyDictionary<uint, List<uint>> dependencies)
        {
            var remaining = dependencies.ToDictionary(pair => pair.Key, pair => pair.Value.Count);
            var dependents = dependencies.Keys.ToDictionary(anchor => anchor, _ => new List<uint>());
            foreach (KeyValuePair<uint, List<uint>> entry in dependencies)
            {
                foreach (uint dependency in entry.Value)
                {
                    dependents[dependency].Add(entry.Key);
                }
            }

            var ready = new Queue<uint>(remaining.Where(pair => pair.Value == 0).Select(pair => pair.Key));
            var order = new List<uint>(dependencies.Count);
            while (ready.Count > 0)
            {
                uint anchor = ready.Dequeue();
                order.Add(anchor);
                foreach (uint dependent in dependents[anchor])
                {
                    if (--remaining[dependent] == 0)
                    {
                        ready.Enqueue(dependent);
                    }
                }
            }

            if (order.Count != dependencies.Count)
            {
                uint cyclic = remaining.First(pair => pair.Value > 0).Key;
                throw new ArgumentException($"Alias anchor {cyclic} has a circular alias dependency (self or indirect cycle).");
            }

            return order;
        }
    }
}
