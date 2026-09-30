using Xbim.Common.Metadata;
using Xbim.Ifc4.Interfaces;

namespace IfcComBridge.Composition
{
    /// <summary>Property filter for InsertCopy: copies everything except owner histories.</summary>
    /// <remarks>
    /// Copied IfcRoot entities reference no owner history, which IFC4 allows (the attribute is optional).
    /// ProductRelationsCopier then deletes the owner histories nothing references, so the composed output
    /// carries none (tested).
    /// </remarks>
    internal static class CopyFilters
    {
        internal static object SemanticFilterFull(ExpressMetaProperty aProperty, object aParentObject)
        {
            if (aProperty.PropertyInfo.Name == nameof(IIfcProduct.OwnerHistory))
                return null;

            return aProperty.PropertyInfo.GetValue(aParentObject, null);
        }
    }
}
