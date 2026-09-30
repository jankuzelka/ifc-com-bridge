using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace IfcComBridge.Cli.Commands
{
    [Serializable]
    public sealed class EngineProbeResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string[] GeometryAssemblies { get; set; } = new string[0];
    }

    /// <summary>
    /// Loads a library DLL the way an in-process COM host does, without registering it: into a fresh
    /// AppDomain whose application base is an empty folder and which has no configuration file (so no
    /// binding redirects), via <see cref="Assembly.LoadFrom(string)"/> from the DLL's own location, which
    /// is what /codebase activation does. This validates a deployment folder, e.g. "library DLL plus
    /// Xbim.Geometry.Engine64.dll", in isolation from the harness's own bin folder.
    /// </summary>
    /// <remarks>
    /// This type must not reference library types statically: it is loaded into the probe domain, and
    /// any static reference would bind the harness's copy of the library instead of the one under test.
    /// </remarks>
    public sealed class IsolatedEngineProbe : MarshalByRefObject
    {
        public const string DefaultRuntimeTypeName = "IfcComBridge.ComRuntime";

        public static int RunInIsolatedDomain(string libraryPath, string runtimeTypeName)
        {
            EngineProbeResult result = ProbeInIsolatedDomain(libraryPath, runtimeTypeName);
            Console.WriteLine($"engine-check (isolated): {libraryPath}");
            Console.WriteLine(result.Success ? "engine-check: DoXBimLibTest() returned True" : "engine-check: FAILED");
            if (result.Error != null)
                Console.WriteLine("  error: " + result.Error);
            foreach (string line in result.GeometryAssemblies.OrderBy(s => s))
                Console.WriteLine("  loaded: " + line);
            return result.Success ? Program.ExitOk : Program.ExitCheckFailed;
        }

        public static EngineProbeResult ProbeInIsolatedDomain(string libraryPath, string runtimeTypeName)
        {
            string appBase = Path.Combine(Path.GetTempPath(), "IfcComBridge.Cli", "isolated-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(appBase);
            AppDomain domain = AppDomain.CreateDomain("IfcComBridge.EngineProbe", null, new AppDomainSetup { ApplicationBase = appBase });
            try
            {
                var probe = (IsolatedEngineProbe)domain.CreateInstanceFromAndUnwrap(
                    typeof(IsolatedEngineProbe).Assembly.Location, typeof(IsolatedEngineProbe).FullName);
                return probe.Probe(libraryPath, runtimeTypeName);
            }
            finally
            {
                AppDomain.Unload(domain);
                TryDeleteDirectory(appBase);
            }
        }

        /// <summary>Runs inside the probe domain.</summary>
        public EngineProbeResult Probe(string libraryPath, string runtimeTypeName)
        {
            var result = new EngineProbeResult();
            try
            {
                Assembly library = Assembly.LoadFrom(libraryPath);
                // Costura attaches its resolver in the module initializer. COM activation triggers it by
                // touching the class; reflection alone does not, so run it explicitly first.
                RuntimeHelpers.RunModuleConstructor(library.ManifestModule.ModuleHandle);
                Type runtimeType = library.GetType(runtimeTypeName, throwOnError: true);
                object runtime = Activator.CreateInstance(runtimeType);
                try
                {
                    result.Success = (bool)runtimeType.GetMethod("DoXBimLibTest").Invoke(runtime, null);
                }
                finally
                {
                    (runtime as IDisposable)?.Dispose();
                }
            }
            catch (Exception ex)
            {
                Exception inner = Program.Unwrap(ex);
                result.Error = $"{inner.GetType().FullName}: {inner.Message}";
            }

            result.GeometryAssemblies = DescribeGeometryAssemblies(AppDomain.CurrentDomain);
            return result;
        }

        internal static string[] DescribeGeometryAssemblies(AppDomain domain) =>
            domain.GetAssemblies()
                  .Where(a => a.GetName().Name.StartsWith("Xbim.Geometry", StringComparison.OrdinalIgnoreCase))
                  .Select(a => $"{a.GetName().Name} from '{SafeLocation(a)}'")
                  .ToArray();

        private static string SafeLocation(Assembly assembly)
        {
            try
            {
                // Empty for assemblies loaded from bytes (Costura-embedded).
                return string.IsNullOrEmpty(assembly.Location) ? "<memory>" : assembly.Location;
            }
            catch (NotSupportedException)
            {
                return "<dynamic>";
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try { Directory.Delete(path, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
