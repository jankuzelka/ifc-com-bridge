using Xbim.Common;
using Xbim.Ifc;

namespace IfcComBridge.Infrastructure
{
    /// <summary>
    /// The editor identity xBIM requires for opening and creating models: neutral values of the public
    /// project.
    /// </summary>
    /// <remarks>
    /// xBIM's IfcStore builds from it the owner histories it attaches to IfcRoot entities created or
    /// modified through the store. The characterized operations write it into no output: composed copies
    /// carry no owner history (CopyFilters), and loading, saving and UpdateModel change no IfcRoot entity.
    /// This was checked on the synthetic regression outputs, and the tests assert that composed models have
    /// no owner history. Uncharacterized paths may carry it, e.g. the bare storey ProductRelationsCopier
    /// creates when the building has none.
    /// </remarks>
    internal static class EditorIdentity
    {
        /// <summary>The credentials every model is opened or created with (Q9: shared by all instances).</summary>
        internal static XbimEditorCredentials Shared { get; } = Create();

        internal static XbimEditorCredentials Create()
        {
            return new XbimEditorCredentials
                {
                    ApplicationDevelopersName = "IfcComBridge contributors",
                    ApplicationFullName = "IfcComBridge",
                    ApplicationIdentifier = "IfcComBridge",
                    ApplicationVersion = "1.0",

                    EditorsFamilyName = "IfcComBridge",
                    EditorsGivenName = "IfcComBridge",
                    EditorsOrganisationName = "IfcComBridge contributors"
            };
        }
    }
}
