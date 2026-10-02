// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Substrait.Tools;
using static Substrait.Tools.StringBuilderUtils;

namespace Substrait.Tests.Tools;

public sealed class StringBuilderUtilsTests
{
    private static readonly char[] Space = [' '];
    private static readonly char[] HelloSuffix = ['l', 'e', 'o'];

    [Test]
    public async Task AppendTypeNameFormatsBuiltInNestedAndGenericTypes()
    {
        var builder = new StringBuilder();
        await Assert.That(builder.AppendTypeName(typeof(string)).ToString()).IsEqualTo("string");

        builder.Clear();
        await Assert.That(builder.AppendTypeName(typeof(NestedType)).ToString()).IsEqualTo("StringBuilderUtilsTests.NestedType");

        builder.Clear();
        await Assert.That(builder.AppendTypeName(typeof(NestedType), qualifyDeclaringTypes: false).ToString()).IsEqualTo("NestedType");

        builder.Clear();
        await Assert.That(builder.AppendTypeName(typeof(List<Dictionary<int, string>>), qualifyDeclaringTypes: false).ToString()).IsEqualTo("List<Dictionary<int,string>>");
    }

    [Test]
    public async Task TrimEndRemovesOnlyMatchingSuffixCharacters()
    {
        await Assert.That(new StringBuilder("abcde  ").TrimEnd(Space).ToString()).IsEqualTo("abcde");
        await Assert.That(new StringBuilder("abcdehello").TrimEnd(HelloSuffix).ToString()).IsEqualTo("abcdeh");
        await Assert.That(new StringBuilder("abcde").TrimEnd(Space).ToString()).IsEqualTo("abcde");
    }

    [Test]
    public async Task EndsWithRecognizesSuffixes()
    {
        var builder = new StringBuilder("abcde");

        await Assert.That(builder.EndsWith("e")).IsTrue();
        await Assert.That(builder.EndsWith("de")).IsTrue();
        await Assert.That(builder.EndsWith("cde")).IsTrue();
        await Assert.That(builder.EndsWith("bcde")).IsTrue();
        await Assert.That(builder.EndsWith("abcde")).IsTrue();
        await Assert.That(builder.EndsWith(" abcde")).IsFalse();
        await Assert.That(builder.EndsWith(" ")).IsFalse();
    }

    [Test]
    public async Task IndentAppendsCharactersForEachLevel()
    {
        var builder = new StringBuilder();
        var indent = new IndentChar('!', 2);

        await Assert.That(builder.Indent(indent, 0).ToString()).IsEqualTo(string.Empty);
        await Assert.That(builder.Indent(indent, 2).ToString()).IsEqualTo("!!!!");
    }

    private sealed class NestedType
    {
    }
}
