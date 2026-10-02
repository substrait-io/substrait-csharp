// SPDX-License-Identifier: Apache-2.0

using Substrait.MetadataGenerator;
using Substrait.Protobuf;

if (args.Length != 1 || args[0] is not ("--write" or "--check"))
{
    Console.Error.WriteLine("Usage: Substrait.MetadataGenerator --write|--check");
    return 2;
}

DirectoryInfo? root = new(AppContext.BaseDirectory);
while (root is not null && !File.Exists(Path.Combine(root.FullName, "Substrait.sln")))
{
    root = root.Parent;
}

if (root is null)
{
    Console.Error.WriteLine("Cannot locate Substrait.sln above the generator executable.");
    return 2;
}

IReadOnlyDictionary<string, string> files = FacadeGenerator.Generate(
    [RelCommon.Descriptor, AdvancedExtension.Descriptor]);
string output = Path.Combine(root.FullName, "src", "Substrait", "Core", "Metadata", "Generated");
return GeneratedFiles.Synchronize(files, output, args[0] == "--write", Console.Out, Console.Error);
