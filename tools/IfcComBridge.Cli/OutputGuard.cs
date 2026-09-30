using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IfcComBridge.Cli
{
    /// <summary>
    /// Path checks for everything the harness writes. The library deletes an existing target file
    /// before saving, so a mistyped output path could otherwise destroy a private input model.
    /// </summary>
    public static class OutputGuard
    {
        public static string InputFile(string path, string optionName)
        {
            string full = Path.GetFullPath(path);
            if (!File.Exists(full))
                throw new UsageException($"--{optionName}: file not found: {full}");
            return full;
        }

        public static string OutputFile(string path, bool force, IEnumerable<string> inputs)
        {
            string full = Path.GetFullPath(path);
            if (IsAnyInput(full, inputs))
                throw new UsageException($"Refusing to write over an input file: {full}");
            if (Directory.Exists(full))
                throw new UsageException($"Output path is a directory: {full}");
            if (File.Exists(full) && !force)
                throw new UsageException($"Output file already exists (use --force to overwrite): {full}");

            string directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            return full;
        }

        public static string OutputDirectory(string path, bool force, IEnumerable<string> inputs)
        {
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // Keep private inputs and generated outputs apart: never write into a folder that holds an input.
            if (inputs.Any(input => IsInside(input, full)))
                throw new UsageException($"Output directory contains an input file; choose a separate folder: {full}");
            if (File.Exists(full))
                throw new UsageException($"Output path is a file: {full}");
            if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any() && !force)
                throw new UsageException($"Output directory is not empty (use --force to write into it): {full}");

            Directory.CreateDirectory(full);
            return full;
        }

        private static bool IsAnyInput(string fullPath, IEnumerable<string> inputs) =>
            inputs.Any(input => string.Equals(Path.GetFullPath(input), fullPath, StringComparison.OrdinalIgnoreCase));

        private static bool IsInside(string file, string directory)
        {
            string fileDirectory = Path.GetDirectoryName(Path.GetFullPath(file)) ?? string.Empty;
            string prefix = directory + Path.DirectorySeparatorChar;
            return string.Equals(fileDirectory, directory, StringComparison.OrdinalIgnoreCase)
                || (fileDirectory + Path.DirectorySeparatorChar).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
