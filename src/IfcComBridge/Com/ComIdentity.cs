using System.Runtime.InteropServices;

// The COM identity of the library. Every COM identifier (type library, interface, class, ProgID and
// DispIds) is defined in this file and nowhere else. These values are the public COM contract of
// IfcComBridge: never change or reuse them. A breaking change to the interface needs a new interface
// with a new IID.

// COM visibility is opt-in: only IComRuntime and ComRuntime are marked [ComVisible(true)].
[assembly: ComVisible(false)]
[assembly: Guid(IfcComBridge.ComIdentity.TypeLibraryId)]
[assembly: TypeLibVersion(1, 0)]

namespace IfcComBridge
{
    /// <summary>COM identifiers of the IfcComBridge type library, runtime interface and runtime class.</summary>
    internal static class ComIdentity
    {
        /// <summary>LIBID of the type library.</summary>
        public const string TypeLibraryId = "b147cbdc-5fe3-42e6-93cf-469b331f8c5c";

        /// <summary>IID of the dual runtime interface (IComRuntime).</summary>
        public const string RuntimeInterfaceId = "caffdf75-6c90-4661-a477-ce89de1ae2e3";

        /// <summary>CLSID of the runtime class (ComRuntime), the only creatable COM class.</summary>
        public const string RuntimeClassId = "393ae64d-08f9-4652-a40c-ff133845e01d";

        /// <summary>ProgID of the runtime class.</summary>
        public const string RuntimeProgId = "IfcComBridge.Runtime";
    }

    /// <summary>DispIds of the runtime interface members, fixed for late-bound (IDispatch) clients.</summary>
    internal static class RuntimeDispIds
    {
        public const int DoXBimLibTest = 1;
        public const int StopwatchStart = 2;
        public const int StopwatchStop = 3;
        public const int LoadIfcJson = 4;
        public const int GetModelGroupId = 5;
        public const int LoadIfc = 6;
        public const int SaveIfc = 7;
        public const int SaveWexbim = 8;
        public const int UpdateModel = 9;
        public const int Dispose = 10;
    }
}
