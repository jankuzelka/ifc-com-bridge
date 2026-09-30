namespace IfcComBridge.Tests.Com
{
    /// <summary>
    /// The public COM contract, pinned independently of the library's ComIdentity.cs so
    /// that any change to it fails the tests. Changing a value here is a breaking change for COM clients.
    /// </summary>
    internal static class ExpectedComContract
    {
        public const string TypeLibraryId = "b147cbdc-5fe3-42e6-93cf-469b331f8c5c";
        public const string InterfaceId = "caffdf75-6c90-4661-a477-ce89de1ae2e3";
        public const string ClassId = "393ae64d-08f9-4652-a40c-ff133845e01d";
        public const string ProgId = "IfcComBridge.Runtime";

        public const string InterfaceName = "IfcComBridge.IComRuntime";
        public const string ClassName = "IfcComBridge.ComRuntime";

        /// <summary>Type library name (the assembly name) and the names early-bound clients see in it.</summary>
        public const string TypeLibraryName = "IfcComBridge";
        public const string TypeLibraryInterfaceName = "IComRuntime";
        public const string TypeLibraryClassName = "ComRuntime";

        /// <summary>The members in vtable order (first slot after IDispatch = 7) with their fixed DispIds.</summary>
        public static readonly (int DispId, string Name)[] Members =
        {
            (1, "DoXBimLibTest"),
            (2, "StopwatchStart"),
            (3, "StopwatchStop"),
            (4, "LoadIfcJson"),
            (5, "GetModelGroupId"),
            (6, "LoadIfc"),
            (7, "SaveIfc"),
            (8, "SaveWexbim"),
            (9, "UpdateModel"),
            (10, "Dispose"),
        };
    }
}
