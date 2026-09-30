using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using IfcComBridge.Tests.Infrastructure;
using IfcComBridge;
using Xunit;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace IfcComBridge.Tests.Com
{
    /// <summary>
    /// The type library that RegAsm /tlb or TlbExp would produce, exported in memory and inspected through
    /// ITypeLib/ITypeInfo: this is what early-bound clients compile against and what late-bound clients
    /// see through IDispatch type information. Nothing is saved or registered.
    /// </summary>
    public class ComTypeLibraryTests
    {
        private static readonly Lazy<(ComTypes.ITypeLib Library, IReadOnlyList<string> Events)> Exported =
            new Lazy<(ComTypes.ITypeLib, IReadOnlyList<string>)>(() =>
            {
                var library = TypeLibraryReader.Export(typeof(ComRuntime).Assembly, out IReadOnlyList<string> events);
                return (library, events);
            });

        private static ComTypes.ITypeLib Library => Exported.Value.Library;

        private static ComTypes.ITypeInfo Type(string name) =>
            TypeLibraryReader.Types(Library).Single(t => TypeLibraryReader.Name(t) == name);

        [Fact]
        public void ExportsWithoutWarnings()
        {
            Assert.DoesNotContain(Exported.Value.Events, e => !e.StartsWith(nameof(ExporterEventKind.NOTIF_TYPECONVERTED)));
        }

        [Fact]
        public void Library_HasTheExpectedLibid_Version10_ForWin64_AndOnlyTheInterfaceAndClass()
        {
            ComTypes.TYPELIBATTR attributes = TypeLibraryReader.LibraryAttributes(Library);
            Assert.Equal(new Guid(ExpectedComContract.TypeLibraryId), attributes.guid);
            Assert.Equal((1, 0), (attributes.wMajorVerNum, attributes.wMinorVerNum));
            Assert.Equal(ComTypes.SYSKIND.SYS_WIN64, attributes.syskind);
            Library.GetDocumentation(-1, out string libraryName, out _, out _, out _);
            Assert.Equal(ExpectedComContract.TypeLibraryName, libraryName);

            Assert.Equal(new[] { ExpectedComContract.TypeLibraryClassName, ExpectedComContract.TypeLibraryInterfaceName },
                TypeLibraryReader.Types(Library).Select(TypeLibraryReader.Name).OrderBy(n => n, StringComparer.Ordinal));
        }

        [Fact]
        public void Coclass_IsCreatable_WithTheExpectedClsid_AndTheRuntimeInterfaceAsDefault()
        {
            ComTypes.ITypeInfo coclass = Type("ComRuntime");
            ComTypes.TYPEATTR attributes = TypeLibraryReader.Attributes(coclass);
            Assert.Equal(ComTypes.TYPEKIND.TKIND_COCLASS, attributes.typekind);
            Assert.Equal(new Guid(ExpectedComContract.ClassId), attributes.guid);
            Assert.True(attributes.wTypeFlags.HasFlag(ComTypes.TYPEFLAGS.TYPEFLAG_FCANCREATE));

            // The exporter always lists mscorlib's _Object (the class interface of System.Object) and, since
            // the class implements it, IDisposable. Both are non-default; the default interface, the one
            // late-bound clients get, is IComRuntime (see ComCallTests for what IDispatch exposes).
            Assert.Equal(new[]
            {
                ("_Object", (ComTypes.IMPLTYPEFLAGS)0),
                ("IComRuntime", ComTypes.IMPLTYPEFLAGS.IMPLTYPEFLAG_FDEFAULT),
                ("IDisposable", (ComTypes.IMPLTYPEFLAGS)0),
            }, TypeLibraryReader.ImplementedTypes(coclass).ToArray());
        }

        [Fact]
        public void Interface_IsADualDispinterface_WithTheExpectedIid()
        {
            ComTypes.ITypeInfo dispinterface = Type("IComRuntime");
            ComTypes.TYPEATTR attributes = TypeLibraryReader.Attributes(dispinterface);
            Assert.Equal(ComTypes.TYPEKIND.TKIND_DISPATCH, attributes.typekind);
            Assert.Equal(new Guid(ExpectedComContract.InterfaceId), attributes.guid);
            Assert.True(attributes.wTypeFlags.HasFlag(ComTypes.TYPEFLAGS.TYPEFLAG_FDUAL));
            Assert.True(attributes.wTypeFlags.HasFlag(ComTypes.TYPEFLAGS.TYPEFLAG_FDISPATCHABLE));

            ComTypes.ITypeInfo vtable = TypeLibraryReader.VtableInterfaceOfDual(dispinterface);
            ComTypes.TYPEATTR vtableAttributes = TypeLibraryReader.Attributes(vtable);
            Assert.Equal(ComTypes.TYPEKIND.TKIND_INTERFACE, vtableAttributes.typekind);
            Assert.Equal(new Guid(ExpectedComContract.InterfaceId), vtableAttributes.guid);
            Assert.True(vtableAttributes.wTypeFlags.HasFlag(ComTypes.TYPEFLAGS.TYPEFLAG_FDUAL));
            Assert.True(vtableAttributes.wTypeFlags.HasFlag(ComTypes.TYPEFLAGS.TYPEFLAG_FOLEAUTOMATION));
            Assert.Equal(new[] { ("IDispatch", (ComTypes.IMPLTYPEFLAGS)0) }, TypeLibraryReader.ImplementedTypes(vtable).ToArray());
        }

        [Fact]
        public void VtableInterface_HasTenMethodsAfterIDispatch_WithFixedDispIds_AndAutomationTypes()
        {
            // Offsets on x64: IUnknown (3) and IDispatch (4) occupy slots 0-6, the first own method is slot 7.
            Assert.Equal(new[]
            {
                "1 @56 DoXBimLibTest([out,retval] VT_PTR:VT_BOOL pRetVal) : VT_HRESULT",
                "2 @64 StopwatchStart() : VT_HRESULT",
                "3 @72 StopwatchStop([out,retval] VT_PTR:VT_R8 pRetVal) : VT_HRESULT",
                "4 @80 LoadIfcJson([in] VT_BSTR fileIfcBuilding, [in] VT_BSTR fileIfcProducts, [in] VT_BSTR productsMapJson, [in] VT_BSTR fileJson, [out,retval] VT_PTR:VT_I4 pRetVal) : VT_HRESULT",
                "5 @88 GetModelGroupId([in] VT_I4 modelIndex, [out,retval] VT_PTR:VT_BSTR pRetVal) : VT_HRESULT",
                "6 @96 LoadIfc([in] VT_BSTR ifcFile) : VT_HRESULT",
                "7 @104 SaveIfc([in] VT_BSTR ifcFile, [in,opt=0] VT_I4 modelIndex) : VT_HRESULT",
                "8 @112 SaveWexbim([in] VT_BSTR wexbimFile, [in,opt=0] VT_I4 modelIndex) : VT_HRESULT",
                "9 @120 UpdateModel([in] VT_BSTR jsonParametersFile, [in,opt=0] VT_I4 modelIndex, [out,retval] VT_PTR:VT_BOOL pRetVal) : VT_HRESULT",
                "10 @128 Dispose() : VT_HRESULT",
            }, TypeLibraryReader.Functions(TypeLibraryReader.VtableInterfaceOfDual(Type("IComRuntime"))).ToArray());
        }

        [Fact]
        public void Dispinterface_ExposesTheSameTenMembers_ToLateBoundClients()
        {
            // The dispinterface also lists the inherited IUnknown/IDispatch methods; only the own members
            // (DispIds 1-10) are relevant here. The vtable offset is not part of the late-bound contract.
            string[] members = TypeLibraryReader.Functions(Type("IComRuntime"))
                .Where(f => int.TryParse(f.Split(' ')[0], out int dispId) && dispId >= 1 && dispId <= 10)
                .Select(f => System.Text.RegularExpressions.Regex.Replace(f, " @\\d+", string.Empty))
                .ToArray();
            Assert.Equal(new[]
            {
                "1 DoXBimLibTest() : VT_BOOL",
                "2 StopwatchStart() : VT_VOID",
                "3 StopwatchStop() : VT_R8",
                "4 LoadIfcJson([in] VT_BSTR fileIfcBuilding, [in] VT_BSTR fileIfcProducts, [in] VT_BSTR productsMapJson, [in] VT_BSTR fileJson) : VT_I4",
                "5 GetModelGroupId([in] VT_I4 modelIndex) : VT_BSTR",
                "6 LoadIfc([in] VT_BSTR ifcFile) : VT_VOID",
                "7 SaveIfc([in] VT_BSTR ifcFile, [in,opt=0] VT_I4 modelIndex) : VT_VOID",
                "8 SaveWexbim([in] VT_BSTR wexbimFile, [in,opt=0] VT_I4 modelIndex) : VT_VOID",
                "9 UpdateModel([in] VT_BSTR jsonParametersFile, [in,opt=0] VT_I4 modelIndex) : VT_BOOL",
                "10 Dispose() : VT_VOID",
            }, members);
        }
    }
}
