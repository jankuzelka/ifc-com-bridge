using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using IfcComBridge.Composition;
using IfcComBridge.Infrastructure;
using IfcComBridge.IO;
using IfcComBridge.Placement;

namespace IfcComBridge
{
    /// <summary>
    /// The only creatable COM class (ProgID IfcComBridge.Runtime). COM clients see IComRuntime only:
    /// there is no class interface. The members are documented on <see cref="IComRuntime"/>.
    /// </summary>
    /// <remarks>
    /// The class keeps the list of loaded models and delegates the work: composition to
    /// ProductLayoutComposer, reading and writing files to ModelFiles, transformations to PlacementUpdater.
    /// </remarks>
    [ComVisible(true)]
    [Guid(ComIdentity.RuntimeClassId)]
    [ProgId(ComIdentity.RuntimeProgId)]
    [ClassInterface(ClassInterfaceType.None)]
    [ComDefaultInterface(typeof(IComRuntime))]
    public class ComRuntime : IComRuntime, IDisposable
    {
        private bool _disposed;
        private Microsoft.Extensions.Logging.ILogger _logger = null;
        private Stopwatch _stopwatch = null;

        // The loaded models. A model index of the COM interface is a position in this list, so an index
        // outside it throws ArgumentOutOfRangeException (HRESULT 0x80131502 for COM clients).
        private List<ComposedModel> _models = new List<ComposedModel>();

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes the loaded models. The class has no finalizer: without Dispose, the models are released
        /// only when the garbage collector reclaims them.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                // Exceptions are swallowed, so Dispose never fails for the caller. If one model fails to
                // dispose, the list is not cleared and the models stay referenced until collection.
                try
                {
                    if (disposing)
                    {
                        _models.ForEach(model => model.Dispose());
                        _models.Clear();
                    }
                }
                catch
                {
                }
            }

            _disposed = true;
        }

        /// <summary>Creates a runtime with no models loaded.</summary>
        public ComRuntime()
        {
            // The first instance in a process configures the process-wide logging (Q9). This is also the
            // first use of the embedded dependencies, so a conflicting binding redirect in the host's
            // configuration fails here (docs/known-issues.md, KI-1).
            _logger = LoggingSetup.CreateLogger<ComRuntime>();
        }

        /// <inheritdoc />
        public bool DoXBimLibTest()
        {
            // Creating the engine loads the native Xbim.Geometry.Engine64.dll from the library's folder. If
            // it cannot be loaded, the constructor throws, so this never returns false in practice.
            var x = new Xbim.Geometry.Engine.Interop.XbimGeometryEngine();
            return (x != null);
        }

        /// <inheritdoc />
        public void StopwatchStart()
        {
            _stopwatch = Stopwatch.StartNew();
        }

        /// <inheritdoc />
        public double StopwatchStop()
        {
            if (_stopwatch != null)
            {
                _stopwatch.Stop();
                _logger.LogInformation($"Processing completed in {_stopwatch.ElapsedMilliseconds / 1e3:N}s");
                return _stopwatch.Elapsed.TotalSeconds;
            }
            return 0.0;
        }

        /// <inheritdoc />
        public int LoadIfcJson(string fileIfcBuilding, string fileIfcProducts, string productsMapJson, string fileJson)
        {
            // The old models are released before any input is read, so they are gone even if this call fails.
            _models.ForEach(model => model.Dispose());
            _models.Clear();
            // COM clients pass the products map as JSON text; the other three arguments are file paths.
            JObject productsMap = JObject.Parse(productsMapJson);
            // The composer runs with its default optimization, OptimizationTarget.Size (Q15).
            // AddRange pulls the lazy composer one group at a time. When a group throws, the models of the
            // groups before it are already in the list and stay loaded (Q10).
            _models.AddRange(ProductLayoutComposer.LoadIfcJson(fileIfcBuilding, fileIfcProducts, productsMap, fileJson));
            return _models.Count;
        }

        /// <inheritdoc />
        public void LoadIfc(string ifcFile)
        {
            _models.ForEach(model => model.Dispose());
            _models.Clear();
            // A single loaded model has no layout group: the file path serves as its group id.
            _models.Add(new ComposedModel(ModelFiles.LoadIfcModel(ifcFile, "ifc"), ifcFile));
        }

        /// <inheritdoc />
        public void SaveIfc(string ifcFile, int modelIndex = 0)
        {
            ModelFiles.SaveIfcModel(ifcFile, _models[modelIndex].IfcStore);
        }

        /// <inheritdoc />
        public void SaveWexbim(string wexbimFile, int modelIndex = 0)
		{
            ModelFiles.SaveIfcWexbim(wexbimFile, _models[modelIndex].IfcStore);
        }

        /// <inheritdoc />
        /// <remarks>
        /// jsonParameters is a file path. COM clients see the interface's name for it, jsonParametersFile.
        /// </remarks>
        public bool UpdateModel(string jsonParameters, int modelIndex = 0)
        {
            return PlacementUpdater.UpdateIfcPosition(jsonParameters, _models[modelIndex].IfcStore);
        }

        /// <inheritdoc />
		public string GetModelGroupId(int modelIndex)
		{
            return _models[modelIndex].GroupId;
		}
	}
}
