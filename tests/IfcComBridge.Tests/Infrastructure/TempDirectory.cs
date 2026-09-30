using System;
using System.IO;

namespace IfcComBridge.Tests.Infrastructure
{
    /// <summary>A unique folder under %TEMP% that is deleted on dispose. Tests never write into the repository.</summary>
    public sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "IfcComBridge.Tests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public string Directory(string name)
        {
            string dir = System.IO.Path.Combine(Path, name);
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
