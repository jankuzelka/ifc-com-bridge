using System.IO;

namespace IfcComBridge.Cli
{
    /// <summary>
    /// Private inputs, their configuration, baselines and generated model files must never live inside
    /// the repository. The repository is recognised by its solution file in any parent folder.
    /// </summary>
    public static class RepositoryGuard
    {
        public const string RepositoryMarker = "IfcComBridge.sln";

        /// <returns>The repository root containing <paramref name="path"/>, or null.</returns>
        public static string FindRepositoryRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            for (var dir = new DirectoryInfo(Path.GetFullPath(path)); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, RepositoryMarker)))
                    return dir.FullName;
            }
            return null;
        }

        public static void AssertOutsideRepository(string path)
        {
            string root = FindRepositoryRoot(path);
            if (root != null)
                throw new UsageException($"Refusing to use '{Path.GetFullPath(path)}': it is inside the repository ({root}). Private data, baselines and generated models must stay outside it.");
        }
    }
}
