using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using IfcComBridge;
using Xunit;

namespace IfcComBridge.Tests.Com
{
    /// <summary>
    /// The COM contract as the CLR sees it (reflection and the interop services RegAsm uses). Nothing is
    /// registered. See ComTypeLibraryTests for the exported type library and ComCallTests for native calls.
    /// </summary>
    public class ComContractTests
    {
        private static readonly Assembly Library = typeof(ComRuntime).Assembly;

        [Fact]
        public void Assembly_IsComInvisibleByDefault_WithTheExpectedTypeLibraryIdentity()
        {
            Assert.False(Library.GetCustomAttribute<ComVisibleAttribute>().Value);
            Assert.Equal(ExpectedComContract.TypeLibraryId, Library.GetCustomAttribute<GuidAttribute>().Value);
            Assert.Equal(new Guid(ExpectedComContract.TypeLibraryId), Marshal.GetTypeLibGuidForAssembly(Library));
            Marshal.GetTypeLibVersionForAssembly(Library, out int major, out int minor);
            Assert.Equal((1, 0), (major, minor));
        }

        [Fact]
        public void OnlyTheRuntimeInterfaceAndClassAreComVisible()
        {
            string[] comVisible = Library.GetExportedTypes().Where(Marshal.IsTypeVisibleFromCom)
                .Select(t => t.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { ExpectedComContract.ClassName, ExpectedComContract.InterfaceName }, comVisible);
        }

        [Fact]
        public void ExactlyOneCreatableClass_WithTheExpectedClsidAndExplicitProgId()
        {
            var registration = new RegistrationServices();
            Type[] registrable = registration.GetRegistrableTypesInAssembly(Library);
            Assert.Equal(new[] { typeof(ComRuntime) }, registrable);

            Type runtime = typeof(ComRuntime);
            Assert.Equal(new Guid(ExpectedComContract.ClassId), Marshal.GenerateGuidForType(runtime));
            Assert.Equal(ExpectedComContract.ProgId, runtime.GetCustomAttribute<ProgIdAttribute>().Value);
            Assert.Equal(ExpectedComContract.ProgId, registration.GetProgIdForType(runtime));
            Assert.NotNull(runtime.GetConstructor(Type.EmptyTypes));
        }

        [Fact]
        public void RuntimeClass_HasNoClassInterface_AndTheRuntimeInterfaceAsDefault()
        {
            Type runtime = typeof(ComRuntime);
            Assert.Equal(ClassInterfaceType.None, runtime.GetCustomAttribute<ClassInterfaceAttribute>().Value);
            Assert.Equal(typeof(IComRuntime), runtime.GetCustomAttribute<ComDefaultInterfaceAttribute>().Value);
            Assert.Contains(typeof(IComRuntime), runtime.GetInterfaces());
        }

        [Fact]
        public void RuntimeInterface_IsDual_WithTenMembersInVtableOrder_AndFixedDispIds()
        {
            Type api = typeof(IComRuntime);
            Assert.Equal(new Guid(ExpectedComContract.InterfaceId), Marshal.GenerateGuidForType(api));
            Assert.Equal(ComInterfaceType.InterfaceIsDual, api.GetCustomAttribute<InterfaceTypeAttribute>().Value);

            var actual = api.GetMethods()
                .Select(m => (Slot: Marshal.GetComSlotForMethodInfo(m), DispId: m.GetCustomAttribute<DispIdAttribute>()?.Value, m.Name))
                .OrderBy(m => m.Slot)
                .ToArray();
            var expected = ExpectedComContract.Members
                .Select((m, i) => (Slot: 7 + i, DispId: (int?)m.DispId, m.Name))
                .ToArray();
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void RuntimeInterface_HasTheExpectedSignatures_WithOptionalModelIndex()
        {
            // Names, parameter order and types of the ten members. modelIndex is optional (default 0) on
            // SaveIfc, SaveWexbim and UpdateModel, so late-bound calls such as SaveIfc(file) work without it.
            string[] signatures = typeof(IComRuntime).GetMethods()
                .OrderBy(Marshal.GetComSlotForMethodInfo)
                .Select(Describe)
                .ToArray();
            Assert.Equal(new[]
            {
                "Boolean DoXBimLibTest()",
                "Void StopwatchStart()",
                "Double StopwatchStop()",
                "Int32 LoadIfcJson(String fileIfcBuilding, String fileIfcProducts, String productsMapJson, String fileJson)",
                "String GetModelGroupId(Int32 modelIndex)",
                "Void LoadIfc(String ifcFile)",
                "Void SaveIfc(String ifcFile, Int32 modelIndex = 0)",
                "Void SaveWexbim(String wexbimFile, Int32 modelIndex = 0)",
                "Boolean UpdateModel(String jsonParametersFile, Int32 modelIndex = 0)",
                "Void Dispose()",
            }, signatures);
        }

        private static string Describe(MethodInfo method) =>
            $"{method.ReturnType.Name} {method.Name}(" +
            string.Join(", ", method.GetParameters().Select(p =>
                $"{p.ParameterType.Name} {p.Name}" + (p.IsOptional ? $" = {p.DefaultValue}" : string.Empty))) +
            ")";
    }
}
