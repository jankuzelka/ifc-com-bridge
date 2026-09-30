using System.Collections.Generic;
using System.Linq;
using IfcComBridge.Infrastructure;
using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.ProductExtension;

namespace IfcComBridge.Composition
{
    /// <summary>
    /// Recreates the relations of the copied products in the target model (containment, types, properties,
    /// classifications, materials, layers) and tidies units, owner histories and project contexts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A relation's related objects are forward references, so copying a relation as it is would drag all
    /// of its source objects into the target, placed or not. Each relation of the products model is
    /// therefore emptied first, inside a transaction on the products model that is never committed. The
    /// copy keeps the relating side (type, property set, classification, material, layer) and then receives
    /// the product copies. Disposing the transaction restores the products model.
    /// </para>
    /// <para>
    /// All relations of one target model share relMap, so a type, property set or material that several
    /// relations use is copied once.
    /// </para>
    /// </remarks>
    internal static class ProductRelationsCopier
    {
        private static readonly Microsoft.Extensions.Logging.ILogger _logger = LoggingSetup.CreateLogger(nameof(ProductRelationsCopier));

        internal static void CreateProductsRelations(Dictionary<IPersistEntity, List<IPersistEntity>> aSrc2targetProductDict, IfcStore aProductModel, IfcStore aTargetModel)
        {
            using (var txn_1048234823 = aTargetModel.BeginTransaction("Create product relations"))
            {
                using (var txn_175435733 = aProductModel.BeginTransaction("Reset related objects"))
                {
                    var relMap = new XbimInstanceHandleMap(aProductModel, aTargetModel);

                    // All copies go into the target's first IfcRelContainedInSpatialStructure, whichever structure
                    // it relates to. Without one, a new relation to the first storey is created, and a bare storey
                    // too if the building has none.
                    _logger.LogInformation($"Creating relations - contained in spatial structure...");
                    {
                        var ciss = aTargetModel.Instances.OfType<IIfcRelContainedInSpatialStructure>().FirstOrDefault();

                        if (ciss == null)
                        {
                            var buildingStorey = aTargetModel.Instances.OfType<IIfcBuildingStorey>().FirstOrDefault();

                            if (buildingStorey == null)
                                buildingStorey = aTargetModel.Instances.New<IfcBuildingStorey>();

                            ciss = aTargetModel.Instances.New<IfcRelContainedInSpatialStructure>();
                            ciss.RelatingStructure = buildingStorey;
                        }

                        foreach (var products in aSrc2targetProductDict.Values)
                            ciss.RelatedElements.AddRange(products.OfType<IIfcProduct>());
                    }

                    _logger.LogInformation($"Creating relations - defined by type...");
                    {
                        var dbtList = aProductModel.Instances.OfType<IIfcRelDefinesByType>();

                        foreach (var dbt in dbtList)
                        {
                            IIfcRelDefinesByType dbtCpy = null;
                            var dbtROList = new List<IIfcObject>();
                            dbtROList.AddRange(dbt.RelatedObjects);
                            dbt.RelatedObjects.Clear();

                            foreach (var obj in dbtROList)
                                if (aSrc2targetProductDict.ContainsKey(obj))
                                {
                                    if (dbtCpy == null)
                                        dbtCpy = aTargetModel.InsertCopy(dbt, relMap, CopyFilters.SemanticFilterFull, false, false);

                                    dbtCpy.RelatedObjects.AddRange(aSrc2targetProductDict[obj].OfType<IIfcObject>());
                                }
                        }
                    }

                    _logger.LogInformation($"Creating relations - defined by properties...");
                    {
                        var dbpList = aProductModel.Instances.OfType<IIfcRelDefinesByProperties>();

                        foreach (var dbp in dbpList)
                        {
                            IIfcRelDefinesByProperties dbpCpy = null;
                            var dbpROList = new List<IIfcObjectDefinition>();
                            dbpROList.AddRange(dbp.RelatedObjects);
                            dbp.RelatedObjects.Clear();

                            foreach (var obj in dbpROList)
                                if (aSrc2targetProductDict.ContainsKey(obj))
                                {
                                    if (dbpCpy == null)
                                        dbpCpy = aTargetModel.InsertCopy(dbp, relMap, CopyFilters.SemanticFilterFull, false, false);

                                    dbpCpy.RelatedObjects.AddRange(aSrc2targetProductDict[obj].OfType<IIfcObjectDefinition>());
                                }
                        }
                    }

                    _logger.LogInformation($"Creating relations - associated classification...");
                    {
                        var acList = aProductModel.Instances.OfType<IIfcRelAssociatesClassification>();

                        foreach (var ac in acList)
                        {
                            IIfcRelAssociatesClassification acCpy = null;
                            var acROList = new List<IIfcDefinitionSelect>();
                            acROList.AddRange(ac.RelatedObjects);
                            ac.RelatedObjects.Clear();

                            foreach (var obj in acROList)
                                if (aSrc2targetProductDict.ContainsKey(obj))
                                {
                                    if (acCpy == null)
                                        acCpy = aTargetModel.InsertCopy(ac, relMap, CopyFilters.SemanticFilterFull, false, false);

                                    acCpy.RelatedObjects.AddRange(aSrc2targetProductDict[obj].OfType<IIfcDefinitionSelect>());
                                }
                        }
                    }

                    _logger.LogInformation($"Creating relations - associated materials...");
                    {
                        var amList = aProductModel.Instances.OfType<IIfcRelAssociatesMaterial>();

                        foreach (var am in amList)
                        {
                            IIfcRelAssociatesMaterial amCpy = null;
                            var amROList = new List<IIfcDefinitionSelect>();
                            amROList.AddRange(am.RelatedObjects);
                            am.RelatedObjects.Clear();

                            HashSet<IIfcMaterial> relatingMaterialSet = new HashSet<IIfcMaterial>();

                            if (am.RelatingMaterial is IIfcMaterialLayerSetUsage mlsu)
                            {
                                if (mlsu.ForLayerSet != null)
                                    foreach (var ml in mlsu.ForLayerSet.MaterialLayers)
                                        if (ml.Material != null)
                                            relatingMaterialSet.Add(ml.Material);
                            }
                            else if (am.RelatingMaterial != null)
                                relatingMaterialSet.Add(am.RelatingMaterial as IIfcMaterial);

                            foreach (var obj in amROList)
                            {
                                if (aSrc2targetProductDict.ContainsKey(obj))
                                {
                                    if (amCpy == null)
                                        amCpy = aTargetModel.InsertCopy(am, relMap, CopyFilters.SemanticFilterFull, false, false);

                                    amCpy.RelatedObjects.AddRange(aSrc2targetProductDict[obj].OfType<IIfcDefinitionSelect>());
                                }

                                // Material definition representations and material properties reference the material,
                                // the inverse direction, so the relation's copy does not bring them. They are copied for
                                // a single IfcMaterial and for the layers of an IfcMaterialLayerSetUsage; other material
                                // selects get the relation only. relMap makes the repeats for later objects no-ops.
                                if ((amCpy != null) && (relatingMaterialSet.Count() > 0))
                                {
                                    var mdrList = aProductModel.Instances.OfType<IIfcMaterialDefinitionRepresentation>().Where(imdr => relatingMaterialSet.Contains(imdr.RepresentedMaterial));
                                    foreach (var mdr in mdrList)
                                        aTargetModel.InsertCopy(mdr, relMap, CopyFilters.SemanticFilterFull, false, false);

                                    var mpList = aProductModel.Instances.OfType<IIfcMaterialProperties>().Where(imp => relatingMaterialSet.Contains(imp.Material));
                                    foreach (var mp in mpList)
                                        aTargetModel.InsertCopy(mp, relMap, CopyFilters.SemanticFilterFull, false, false);
                                }
                            }
                        }
                    }

                    // A product is on a layer when one of its representations is assigned to it; assignments of
                    // single representation items are not recognized. The layer is copied once and receives the
                    // representations of all copies of those products.
                    _logger.LogInformation($"Creating relations - presentation layer assignment...");
                    {
                        var plaList = aProductModel.Instances.OfType<IIfcPresentationLayerAssignment>();

                        foreach (var pla in plaList)
                        {
                            HashSet<IPersistEntity> srcPlaSet = new HashSet<IPersistEntity>();
                            var plaAIList = new List<IIfcLayeredItem>();
                            plaAIList.AddRange(pla.AssignedItems);
                            pla.AssignedItems.Clear();

                            foreach (var plai in plaAIList)
                            {
                                var productList = aProductModel.Instances.OfType<IIfcProduct>().Where(ib => ib.Representation?.Representations.Contains(plai) == true);
                                foreach (var product in productList)
                                    if (aSrc2targetProductDict.ContainsKey(product))
                                        srcPlaSet.Add(product);
                            }

                            if (srcPlaSet.Count() > 0)
                            {
                                IIfcPresentationLayerAssignment plaCpy = null;

                                foreach (var srcProduct in srcPlaSet)
                                    foreach (IIfcProduct targetProduct in aSrc2targetProductDict[srcProduct])
                                    {
                                        if ((targetProduct.Representation == null) || (targetProduct.Representation.Representations.Count() < 1))
                                            continue;

                                        if (plaCpy == null)
                                            plaCpy = aTargetModel.InsertCopy(pla, relMap, CopyFilters.SemanticFilterFull, false, false);

                                        plaCpy.AssignedItems.AddRange(targetProduct.Representation.Representations);
                                    }
                            }
                        }
                    }

                    // Q3: the condition deletes a unit assignment unless some context uses a different one. With
                    // the building's single IfcProject, that is the building's unit assignment, the one in use. It
                    // reads like an inverted "used by no context"; whether that is intended is not established.
                    // The project-properties step below assigns the products model's units anyway, so the output
                    // uses the products model's units (tested). SI units that no remaining assignment lists are
                    // deleted as well; other unit types are kept.
                    _logger.LogInformation($"Creating relations - unit assignment...");
                    {
                        var unitAssignmentList = aTargetModel.Instances.OfType<IIfcUnitAssignment>().ToList();

                        foreach (var item in unitAssignmentList)
                            if (!aTargetModel.Instances.OfType<IIfcContext>().Any(c => c.UnitsInContext != item))
                                aTargetModel.Delete(item);

                        var unitList = aTargetModel.Instances.OfType<IIfcSIUnit>().ToList();

                        foreach (var item in unitList)
                            if (!aTargetModel.Instances.OfType<IIfcUnitAssignment>().Any(ua => ua.Units.Contains(item)))
                                aTargetModel.Delete(item);
                    }

                    // Copies carry no owner history (CopyFilters), so this removes the ones copied with the
                    // building that nothing references any more.
                    _logger.LogInformation($"Creating relations - owner history...");
                    {
                        var ownerHistoryList = aTargetModel.Instances.OfType<IIfcOwnerHistory>().ToList();

                        foreach (var item in ownerHistoryList)
                            if (!aTargetModel.Instances.OfType<IIfcRoot>().Any(r => r.OwnerHistory == item))
                                aTargetModel.Delete(item);
                    }

                    // TODO: should be set in building.ifc, otherwise generate complete IFC (with building, storeys, IFCRELAGGREGATES !!!) without building.ifc
                    // The target project gets a copy of the products project's unit assignment (Q3), and copies of
                    // its representation contexts next to the building's (Q19). The product copies keep using the
                    // context copies that their per-scale maps made, which the project does not list, so a
                    // composed model holds several equal contexts.
                    _logger.LogInformation($"Creating relations - project properties...");
                    {
                        var sourceProject = aProductModel.Instances.OfType<IIfcProject>().FirstOrDefault();
                        var targetProject = aTargetModel.Instances.OfType<IIfcProject>().FirstOrDefault();

                        if ((sourceProject != null) && (targetProject != null))
                        {
                            var units = sourceProject.UnitsInContext;
                            if (units != null)
                                targetProject.UnitsInContext = aTargetModel.InsertCopy(units, relMap, CopyFilters.SemanticFilterFull, false, false);

                            var repreContextList = sourceProject.RepresentationContexts;
                            if (repreContextList != null)
                                foreach (var repreContext in repreContextList)
                                    targetProject.RepresentationContexts.Add(aTargetModel.InsertCopy(repreContext, relMap, CopyFilters.SemanticFilterFull, false, false));
                        }
                    }

                    txn_175435733.Dispose(); // won't change: restores the emptied relations of the products model
                }

                txn_1048234823.Commit();
            }
        }
    }
}
