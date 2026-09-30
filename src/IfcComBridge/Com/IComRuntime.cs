using System.Runtime.InteropServices;

namespace IfcComBridge
{
    /// <summary>
    /// The COM interface of IfcComBridge. It is dual: late-bound clients (IDispatch, e.g. PHP) and
    /// early-bound clients (vtable) use the same members. The member order is the vtable order and the
    /// DispIds are fixed in ComIdentity.cs: do not reorder, insert or change members.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Models.</b> An instance holds a list of loaded models, addressed by a zero-based model index.
    /// <see cref="LoadIfc"/> and <see cref="LoadIfcJson"/> replace the whole list; the other members work
    /// on one model of it. Every model is an in-memory IFC4 model: memory use grows with the inputs and
    /// the number of composed models. Changes exist only in memory until <see cref="SaveIfc"/> or
    /// <see cref="SaveWexbim"/> writes them.
    /// </para>
    /// <para>
    /// <b>Paths.</b> File paths are passed to .NET file APIs as they are. A relative path is resolved
    /// against the host process's current directory, not against the library's folder.
    /// </para>
    /// <para>
    /// <b>Errors.</b> Failures are .NET exceptions. COM returns them as a failed HRESULT with the
    /// exception's message; through IDispatch they arrive as DISP_E_EXCEPTION with the message in
    /// EXCEPINFO. The exception types named below are observed behaviour, and clients may depend on them.
    /// </para>
    /// <para>
    /// <b>Lifetime.</b> Call <see cref="Dispose"/> when done. Otherwise the models are released only when
    /// .NET garbage-collects the object after the last COM reference is gone.
    /// </para>
    /// <para>
    /// <b>Threads.</b> The class is registered with ThreadingModel Both but takes no locks: never call one
    /// instance from two threads at once. All instances in a process share the logging setup, the editor
    /// identity and xBIM's global settings (quirk Q9); concurrent use of several instances is not
    /// characterized.
    /// </para>
    /// <para>
    /// Input formats: docs/input-contracts.md. Quirks Q1-Q19: docs/quirks.md.
    /// </para>
    /// </remarks>
    [ComVisible(true)]
    [Guid(ComIdentity.RuntimeInterfaceId)]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface IComRuntime
    {
        /// <summary>Checks that the native geometry engine loads, by creating xBIM's geometry engine.</summary>
        /// <returns>true when the engine loaded.</returns>
        /// <remarks>
        /// Xbim.Geometry.Engine64.dll must be in the same folder as IfcComBridge.dll. When it is missing
        /// or cannot be loaded, the call throws FileLoadException instead of returning false.
        /// </remarks>
        [DispId(RuntimeDispIds.DoXBimLibTest)]
        bool DoXBimLibTest();

        /// <summary>Starts or restarts this instance's stopwatch. It measures time only; no model is involved.</summary>
        [DispId(RuntimeDispIds.StopwatchStart)]
        void StopwatchStart();

        /// <summary>Stops the stopwatch and logs the elapsed time to the console.</summary>
        /// <returns>The seconds since the last <see cref="StopwatchStart"/>, or 0 if it was never called.</returns>
        [DispId(RuntimeDispIds.StopwatchStop)]
        double StopwatchStop();

        /// <summary>
        /// Composes one model per layout group: a copy of the building plus copies of the products the
        /// group places. The composed models replace all loaded models.
        /// </summary>
        /// <param name="fileIfcBuilding">Path of the building IFC4 file. The whole building is copied into every composed model.</param>
        /// <param name="fileIfcProducts">Path of the products IFC4 file. Its IfcBeam and IfcWallStandardCase elements are the products that can be placed.</param>
        /// <param name="productsMapJson">The products map as inline JSON text, a JSON object. Not a file path.</param>
        /// <param name="fileJson">Path of the layout JSON file, an array of groups.</param>
        /// <returns>
        /// The number of composed models, now at indices 0 to count - 1. A group that places no product
        /// yields no model (Q6), so an index is not the position of a group: use <see cref="GetModelGroupId"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The loaded models are disposed first, before the products map is parsed. When the call fails
        /// later, the models composed for the groups before the failing one stay loaded (Q10), and the
        /// exception's stack trace starts in the composer (Q2).
        /// </para>
        /// <para>
        /// Typical exceptions: InvalidDataException (empty path); FileNotFoundException (missing file, or
        /// a file that is not IFC4, Q11); Newtonsoft.Json.JsonReaderException (malformed JSON, or a products
        /// map that is not an object); ArgumentException (duplicate GlobalId among the products, Q12);
        /// NullReferenceException (a required JSON field is missing, Q5); Exception (no products, or an
        /// empty building).
        /// </para>
        /// </remarks>
        [DispId(RuntimeDispIds.LoadIfcJson)]
        int LoadIfcJson(string fileIfcBuilding, string fileIfcProducts, string productsMapJson, string fileJson);

        /// <summary>Returns the group id of a loaded model.</summary>
        /// <param name="modelIndex">Zero-based index of the model.</param>
        /// <returns>
        /// For a composed model, the layout group's group_id as text. After <see cref="LoadIfc"/>, the file
        /// path exactly as it was passed.
        /// </returns>
        /// <remarks>An index outside the loaded models throws ArgumentOutOfRangeException.</remarks>
        [DispId(RuntimeDispIds.GetModelGroupId)]
        string GetModelGroupId(int modelIndex);

        /// <summary>Loads one IFC4 file as the only model, index 0. It replaces all loaded models.</summary>
        /// <param name="ifcFile">Path of the IFC4 file. The path also becomes the model's group id.</param>
        /// <remarks>
        /// The loaded models are disposed first, so a failed load leaves no model loaded. Exceptions:
        /// InvalidDataException (empty path); FileNotFoundException (missing file, or not IFC4, Q11).
        /// </remarks>
        [DispId(RuntimeDispIds.LoadIfc)]
        void LoadIfc(string ifcFile);

        /// <summary>Writes a loaded model to an IFC file.</summary>
        /// <param name="ifcFile">Path of the output file. The tests write IFC-SPF with the .ifc extension.</param>
        /// <param name="modelIndex">Zero-based index of the model. Optional, default 0.</param>
        /// <remarks>
        /// An existing file is deleted before the model is written, so it is lost if writing fails (Q16).
        /// Exceptions: InvalidDataException (empty path); ArgumentOutOfRangeException (no such model);
        /// I/O exceptions from writing.
        /// </remarks>
        [DispId(RuntimeDispIds.SaveIfc)]
        void SaveIfc(string ifcFile, int modelIndex = 0);

        /// <summary>
        /// Generates the tessellated geometry of a loaded model and writes it as a WexBIM file, the binary
        /// format of the xBIM web viewer.
        /// </summary>
        /// <param name="wexbimFile">Path of the output file.</param>
        /// <param name="modelIndex">Zero-based index of the model. Optional, default 0.</param>
        /// <remarks>
        /// <para>
        /// The geometry is regenerated with the native engine on every call; this is the slow step. It is
        /// generated with xBIM's world-coordinate adjustment: a single root placement displacement, such
        /// as a geo-located site, is taken out of the placements and added to the world coordinate system.
        /// </para>
        /// <para>
        /// An existing file is deleted first (Q16). Two runs can order equally populated regions
        /// differently (Q18), so compare WexBIM files by content, not by hash. Exceptions: as
        /// <see cref="SaveIfc"/>, plus FileLoadException when the engine cannot be loaded.
        /// </para>
        /// </remarks>
        [DispId(RuntimeDispIds.SaveWexbim)]
        void SaveWexbim(string wexbimFile, int modelIndex = 0);

        /// <summary>
        /// Applies the 4x4 transformations in a JSON file to the placements of products in a loaded model.
        /// </summary>
        /// <param name="jsonParametersFile">
        /// Path of the transforms JSON file, not JSON text. Products are identified by their STEP entity
        /// label (#id); see docs/input-contracts.md, section 5.
        /// </param>
        /// <param name="modelIndex">Zero-based index of the model. Optional, default 0.</param>
        /// <returns>true when at least one entry was applied.</returns>
        /// <remarks>
        /// <para>
        /// All entries run in one transaction: an exception rolls back every entry of the call. Entries
        /// without an id or a transformation, and ids that are not products with a 3-D local placement, are
        /// skipped. The change stays in memory until the model is saved.
        /// </para>
        /// <para>
        /// Send all 16 matrix values: missing rotation values collapse the directions (Q17). A placement
        /// point or direction that is shared by several placements moves all of them, and a placement
        /// without Axis or RefDirection throws NullReferenceException (Q7). A file without
        /// modified_products throws NullReferenceException (Q5).
        /// </para>
        /// </remarks>
        [DispId(RuntimeDispIds.UpdateModel)]
        bool UpdateModel(string jsonParametersFile, int modelIndex = 0);

        /// <summary>
        /// Releases all loaded models. Calling it again does nothing. Afterwards, calls that need a model
        /// throw ArgumentOutOfRangeException, as they do before anything is loaded.
        /// </summary>
        [DispId(RuntimeDispIds.Dispose)]
        void Dispose();
    }
}
