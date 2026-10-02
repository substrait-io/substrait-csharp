// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace Substrait.MetadataGenerator;

internal static class GeneratedFiles
{
    internal static int Synchronize(
        IReadOnlyDictionary<string, string> expected,
        string directory,
        bool write,
        TextWriter output,
        TextWriter error)
    {
        string[] unexpected = Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .Where(path => !expected.ContainsKey(Path.GetRelativePath(directory, path)))
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];
        if (unexpected.Length != 0)
        {
            foreach (string path in unexpected)
            {
                error.WriteLine($"Unexpected generated-directory file: {path}. Remove or relocate it explicitly.");
            }

            return 1;
        }

        bool matches = true;
        foreach (var (name, source) in expected.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            string path = Path.Combine(directory, name);
            byte[] bytes = Encoding.UTF8.GetBytes(source);
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            {
                continue;
            }

            if (write)
            {
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(path, bytes);
                output.WriteLine($"Generated {name}");
            }
            else
            {
                error.WriteLine($"Missing or stale facade: {path}. Run the metadata generator with --write.");
                matches = false;
            }
        }

        if (matches)
        {
            output.WriteLine($"Verified {expected.Count} metadata facades.");
        }

        return matches ? 0 : 1;
    }
}
