// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using Substrait.Core.Extension.Types;
using Substrait.Core.Type;
using Substrait.Tools.Visitor;
using Assembly = System.Reflection.Assembly;

namespace Substrait.Tests.Core.Type;

/// <summary>
/// Tests for type visitors.
/// </summary>
public class TypeVisitorTest
{
    private readonly TypeFactory typeFactory;
    private readonly PrimitiveTypeFactory primitiveTypeFactory;
    private readonly TypeTopDownDispatcher<StringBuilderContext, VoidOutput> topDownDispatcher;
    private readonly TypeBottomUpDispatcher<StringBuilderContext, VoidOutput> bottomUpDispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="TypeVisitorTest"/> class.
    /// </summary>
    public TypeVisitorTest()
    {
        this.typeFactory = TypeFactory.REQUIRED;
        this.primitiveTypeFactory = PrimitiveTypeFactory.REQUIRED;
        this.topDownDispatcher = new TypeTopDownDispatcher<StringBuilderContext, VoidOutput>(new TypePrinter());
        this.bottomUpDispatcher = new TypeBottomUpDispatcher<StringBuilderContext, VoidOutput>(new TypePrinter());
    }

    /// <summary>
    /// Verifies that all sealed classes that implement IType have a corresponding Visit method in TypeVisitor.
    /// </summary>
    [Test]
    public async Task TestTypeVisitorContainsAllSealedClasses()
    {
        // Get all types that implement IType
        var itypeTypes = Assembly.GetAssembly(typeof(IType))!
            .GetTypes()
            .Where(t => typeof(IType).IsAssignableFrom(t) && t.IsClass && t.IsSealed && t.Namespace == "Substrait.Core.Type")
            .ToList();

        // Get all methods in TypeVisitor
        var typeVisitorMethods = typeof(TypeVisitor<,>).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "Visit" && m.GetParameters().Length == 2)
            .Select(m => m.GetParameters()[0].ParameterType)
            .ToList();

        // Check if all IType types have corresponding Visit methods in TypeVisitor
        foreach (var type in itypeTypes)
        {
            await Assert.That(typeVisitorMethods.Contains(type)).IsTrue().Because($"TypeVisitor does not contain a Visit method for {type.Name}");
        }
    }

    /// <summary>
    /// Tests the type visitor with a struct containing all possible types.
    /// </summary>
    [Test]
    public async Task TestTypeVisitor()
    {
        var fields = ImmutableList.Create<IType>(
            this.primitiveTypeFactory.BOOL,
            this.primitiveTypeFactory.I8,
            this.primitiveTypeFactory.I16,
            this.primitiveTypeFactory.I32,
            this.primitiveTypeFactory.I64,
            this.primitiveTypeFactory.FP32,
            this.primitiveTypeFactory.FP64,
            this.primitiveTypeFactory.STR,
            this.primitiveTypeFactory.BINARY,
            this.primitiveTypeFactory.DATE,
            this.primitiveTypeFactory.UUID,
            this.primitiveTypeFactory.TIME,
            this.primitiveTypeFactory.INTERVAL_DAY,
            this.primitiveTypeFactory.INTERVAL_YEAR,
            this.typeFactory.PrecisionTime(9),
            this.typeFactory.IntervalDay(12),
            this.typeFactory.IntervalCompound(3),
            this.typeFactory.PrecisionTimestamp(9),
            this.typeFactory.PrecisionTimestampTZ(6),
            this.typeFactory.FixedChar(1),
            this.typeFactory.VarChar(50),
            this.typeFactory.FixedBinary(10),
            this.typeFactory.Decimal(10, 2));

        var structType = this.typeFactory.Struct(fields);

        string topDownExpected = "Struct|Bool|I8|I16|I32|I64|FP32|FP64|Str|Binary|Date|Uuid|Time|IntervalDay|IntervalYear|Time|IntervalDay|IntervalCompound|PrecisionTimestamp|PrecisionTimestampTZ|FixedChar|VarChar|FixedBinary|Decimal|";
        string bottomUpExpected = "Bool|I8|I16|I32|I64|FP32|FP64|Str|Binary|Date|Uuid|Time|IntervalDay|IntervalYear|Time|IntervalDay|IntervalCompound|PrecisionTimestamp|PrecisionTimestampTZ|FixedChar|VarChar|FixedBinary|Decimal|Struct|";

        await this.CheckTopDownTraversal(structType, topDownExpected);
        await this.CheckBottomUpTraversal(structType, bottomUpExpected);
    }

    [Test]
    public async Task NewTypeVisitorOverloadsRemainOptional()
    {
        foreach (var type in new[] { typeof(PrimitiveType.Uuid), typeof(ParameterizedType.IntervalCompound) })
        {
            var method = typeof(TypeVisitor<object, object>).GetMethod("Visit", [type, typeof(object)])!;
            await Assert.That(method.IsVirtual).IsTrue();
            await Assert.That(method.IsAbstract).IsFalse();
        }
    }

    /// <summary>
    /// Tests the type visitor with an unsupported type.
    /// </summary>
    [Test]
    public async Task TestTypeVisitorWithUnsupportedType()
    {
        var unsupportedType = new UnsupportedType();
        var structType = this.typeFactory.Struct(ImmutableList.Create<IType>(unsupportedType));
        await Assert.That(() => this.topDownDispatcher.Dispatch(structType, new StringBuilderContext())).ThrowsExactly<NotSupportedException>();
        await Assert.That(() => this.bottomUpDispatcher.Dispatch(structType, new StringBuilderContext())).ThrowsExactly<NotSupportedException>();
    }

    private async Task CheckTopDownTraversal(IType type, string expected)
    {
        StringBuilderContext context = new StringBuilderContext();
        this.topDownDispatcher.Dispatch(type, context);
        await Assert.That(context.ToString()).IsEqualTo(expected);
    }

    private async Task CheckBottomUpTraversal(IType type, string expected)
    {
        StringBuilderContext context = new StringBuilderContext();
        this.bottomUpDispatcher.Dispatch(type, context);
        await Assert.That(context.ToString()).IsEqualTo(expected);
    }

    private sealed class StringBuilderContext : NoOpContext<IType, VoidOutput>
    {
        private readonly StringBuilder builder = new StringBuilder();

        public void Append(string value) => this.builder.Append(value).Append('|');

        public override string ToString() => this.builder.ToString();
    }

    private sealed class TypePrinter : DefaultTypeVisitor<StringBuilderContext, VoidOutput>
    {
        public override VoidOutput Visit(IType other, StringBuilderContext context)
        {
            throw new NotSupportedException($"Unable to print type {other.GetType().Name}");
        }

        protected override VoidOutput DefaultVisit(IType type, StringBuilderContext context)
        {
            context.Append(type.GetType().Name);
            return VoidOutput.Instance;
        }
    }

    private sealed class UnsupportedType : IType
    {
        public IType.NullableType Nullable => IType.NullableType.Unspecified;

        public IEnumerable<IType> InputNodes { get => Array.Empty<IType>(); }

        public ITypeVariation? TypeVariation => null;

        public string ShortTypeName => throw new NotImplementedException();

        public string TypeName => throw new NotImplementedException();

        public TOutput Accept<TContext, TOutput>(TypeVisitor<TContext, TOutput> visitor, TContext context)
        {
            return visitor.Visit(this, context);
        }

        public bool NodeEquals(IType other, ITypeComparison comparison)
        {
            throw new NotImplementedException();
        }

        public string ToTypeString()
        {
            throw new NotImplementedException();
        }
    }
}
