using System;
using System.IO;
using System.Runtime.InteropServices;
using IfcComBridge.Cli.Synthetic;
using IfcComBridge.Tests.Infrastructure;
using IfcComBridge;
using Xunit;
using static IfcComBridge.Tests.Infrastructure.NativeComClient;

namespace IfcComBridge.Tests.Com
{
    /// <summary>
    /// Native calls into the runtime's COM callable wrapper: late-bound through IDispatch as PHP or
    /// VBScript call it, and early-bound through the dual interface's vtable. The object is created
    /// in-process, so activation by ProgID/CLSID is not covered here: that needs registration or a
    /// registration-free activation context (scripts\com-smoke-test.ps1).
    /// </summary>
    public class ComCallTests
    {
        [Fact]
        public void QueryInterface_AnswersTheRuntimeInterfaceAndIDispatch_ButNotAnUnimplementedInterface()
        {
            using (var runtime = new ComRuntime())
            using (var client = new NativeComClient(runtime))
            {
                Assert.Equal(S_OK, Query(client, ExpectedComContract.InterfaceId));
                Assert.Equal(S_OK, Query(client, "00020400-0000-0000-c000-000000000046")); // IDispatch
                Assert.Equal(E_NOINTERFACE, Query(client, "0000000c-0000-0000-c000-000000000046")); // IStream
            }
        }

        [Fact]
        public void IDispatch_ResolvesTheTenMemberNamesToTheFixedDispIds_CaseInsensitively()
        {
            using (var runtime = new ComRuntime())
            using (var client = new NativeComClient(runtime))
            {
                foreach ((int dispId, string name) in ExpectedComContract.Members)
                {
                    Assert.Equal(S_OK, client.GetIDsOfNames(name, out int actual));
                    Assert.Equal(dispId, actual);
                    Assert.Equal(S_OK, client.GetIDsOfNames(name.ToLowerInvariant(), out actual));
                    Assert.Equal(dispId, actual);
                }
            }
        }

        [Fact]
        public void IDispatch_DoesNotExposeClassInterfaceMembers()
        {
            // ClassInterfaceType.None: there is no class interface, so IDispatch does not reach the System.Object members.
            using (var runtime = new ComRuntime())
            using (var client = new NativeComClient(runtime))
            {
                foreach (string name in new[] { "ToString", "GetHashCode", "GetType", "Equals" })
                    Assert.Equal(DISP_E_UNKNOWNNAME, client.GetIDsOfNames(name, out _));
            }
        }

        [Fact]
        public void IDispatch_Invoke_CallsTheRuntime_WithOptionalModelIndexOmitted()
        {
            using (var temp = new TempDirectory())
            using (var runtime = new ComRuntime())
            using (var client = new NativeComClient(runtime))
            {
                string input = temp.File("building.ifc");
                string output = temp.File("roundtrip.ifc");
                SyntheticModels.WriteBuildingModel(input);

                Assert.Equal(S_OK, client.Invoke(1, new object[0], out object engineOk, out _));
                Assert.Equal(true, engineOk);
                Assert.Equal(S_OK, client.Invoke(2, new object[0], out _, out _));
                Assert.Equal(S_OK, client.Invoke(3, new object[0], out object seconds, out _));
                Assert.IsType<double>(seconds);

                Assert.Equal(S_OK, client.Invoke(6, new object[] { input }, out _, out _));
                Assert.Equal(S_OK, client.Invoke(7, new object[] { output }, out _, out _)); // SaveIfc(file), modelIndex omitted
                Assert.True(new FileInfo(output).Length > 0);

                Assert.Equal(S_OK, client.Invoke(10, new object[0], out _, out _)); // Dispose
            }
        }

        [Fact]
        public void IDispatch_Invoke_ReportsExceptionsAsDispException_WithTheMessage()
        {
            using (var runtime = new ComRuntime())
            using (var client = new NativeComClient(runtime))
            {
                Assert.Equal(DISP_E_EXCEPTION, client.Invoke(5, new object[] { 0 }, out _, out string description)); // GetModelGroupId(0), no models
                Assert.Contains("Index was out of range", description);
            }
        }

        [Fact]
        public void Vtable_CallsTheRuntimeThroughTheDualInterface()
        {
            using (var temp = new TempDirectory())
            using (var runtime = new ComRuntime())
            using (var client = new NativeComClient(runtime))
            {
                string input = temp.File("building.ifc");
                string output = temp.File("roundtrip.ifc");
                SyntheticModels.WriteBuildingModel(input);

                Assert.Equal(S_OK, client.QueryInterface(new Guid(ExpectedComContract.InterfaceId), out IntPtr api));
                try
                {
                    Assert.Equal(S_OK, Slot<BoolResult>(api, 7)(api, out short engineOk)); // DoXBimLibTest
                    Assert.Equal(-1, engineOk); // VARIANT_TRUE
                    Assert.Equal(S_OK, Slot<NoArguments>(api, 8)(api)); // StopwatchStart
                    Assert.Equal(S_OK, Slot<DoubleResult>(api, 9)(api, out double seconds)); // StopwatchStop
                    Assert.True(seconds >= 0);

                    // GetModelGroupId(0) without models: the exception becomes its HRESULT.
                    Assert.Equal(COR_E_ARGUMENTOUTOFRANGE, Slot<IntToString>(api, 11)(api, 0, out _));

                    Assert.Equal(S_OK, Slot<StringArgument>(api, 12)(api, input)); // LoadIfc
                    Assert.Equal(S_OK, Slot<StringIntArguments>(api, 13)(api, output, 0)); // SaveIfc
                    Assert.True(new FileInfo(output).Length > 0);

                    Assert.Equal(S_OK, Slot<NoArguments>(api, 16)(api)); // Dispose
                }
                finally
                {
                    Marshal.Release(api);
                }
            }
        }

        private static int Query(NativeComClient client, string iid)
        {
            int hr = client.QueryInterface(new Guid(iid), out IntPtr pointer);
            if (pointer != IntPtr.Zero)
                Marshal.Release(pointer);
            return hr;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int NoArguments(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int BoolResult(IntPtr self, out short result);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int DoubleResult(IntPtr self, out double result);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int IntToString(IntPtr self, int value, [MarshalAs(UnmanagedType.BStr)] out string result);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int StringArgument(IntPtr self, [MarshalAs(UnmanagedType.BStr)] string value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int StringIntArguments(IntPtr self, [MarshalAs(UnmanagedType.BStr)] string value, int number);
    }
}
