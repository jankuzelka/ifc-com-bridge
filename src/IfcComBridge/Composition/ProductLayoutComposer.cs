using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using IfcComBridge.Geometry;
using IfcComBridge.Infrastructure;
using IfcComBridge.IO;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Xbim.Common;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.IO;

namespace IfcComBridge.Composition
{
    /// <summary>
    /// LoadIfcJson: composes one model per layout group from the building model and the products model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For each group, a new in-memory IFC4 model receives a full copy of the building
    /// (CreateCopyOfBuilding), one product copy per placed layout set (ProductCopier), and the relations
    /// of those copies (ProductRelationsCopier).
    /// </para>
    /// <para>
    /// LoadIfcJson is an iterator. Nothing is read until enumeration starts, and each group is composed
    /// when the enumerator reaches it. The building and products models stay open until the enumeration
    /// ends or is disposed. The caller owns every model it receives.
    /// </para>
    /// </remarks>
    internal static class ProductLayoutComposer
    {
        internal enum OptimizationTarget
        {
            Speed,
            Size
        }

        private static readonly Microsoft.Extensions.Logging.ILogger _logger = LoggingSetup.CreateLogger(nameof(ProductLayoutComposer));

        // Process-wide (Q9). Nothing in the library sets it, so it is always Size (Q15): after composition,
        // Cartesian points with identical coordinates are merged (BrepPointOptimizer). Speed skips that step.
        internal static OptimizationTarget CurrentOptimization { get; set; } = OptimizationTarget.Size;

        internal static IEnumerable<ComposedModel> LoadIfcJson(string aBuildingIfcFile, string aProductsIfcFile, JObject aProductsMap, string aJsonFile, string aJsonItemNotFoundLogger = null)
        {
            IEnumerable<JToken> jsonValues = ModelFiles.LoadJson(aJsonFile, "JSON data");

            using (IfcStore buildingModel = ModelFiles.LoadIfcModel(aBuildingIfcFile, "IFC building"))
            {
                using (IfcStore productModel = ModelFiles.LoadIfcModel(aProductsIfcFile, "IFC products"))
                {
                    // Only IfcBeam (with its subtypes) and IfcWallStandardCase can be placed. The log calls
                    // all of them beams.
                    _logger.LogInformation($"Gathering IFC beams...");
                    var products = Array.Empty<IIfcProduct>()
                        .Concat(productModel.Instances.OfType<IIfcBeam>())
                        .Concat(productModel.Instances.OfType<IIfcWallStandardCase>());

                    _logger.LogInformation($"Found {products.Count()} beams...");

                    if (products.Count() < 1)
                        throw new Exception("No beams in the products");

                    _logger.LogInformation($"Creating guid -> beam map...");
                    Dictionary<string, IIfcProduct> guid2productDict = new Dictionary<string, IIfcProduct>();
                    Dictionary<string, IIfcProduct> name2productDict = new Dictionary<string, IIfcProduct>();
                    foreach (var product in products)
                    {
                        // Q12: a duplicate GlobalId throws ArgumentException. Duplicate names are allowed: the
                        // last candidate with a name wins the name lookup.
                        guid2productDict.Add(product.GlobalId, product);
                        if (!string.IsNullOrWhiteSpace(product.Name))
                            name2productDict[product.Name] = product;
                    }

                    _logger.LogInformation($"Extracting elements...");

                    foreach (var group in jsonValues)
                    {
                        // Each group gets its own in-memory model, in the building's schema (IFC4, checked on load).
                        IfcStore targetModel = IfcStore.Create(EditorIdentity.Shared, buildingModel.SchemaVersion, XbimStoreType.InMemoryModel);

                        try
                        {
                            // Source product -> its copies in this model. ProductRelationsCopier rebuilds the
                            // relations of the copies from it.
                            Dictionary<IPersistEntity, List<IPersistEntity>> src2targetProductDict = new Dictionary<IPersistEntity, List<IPersistEntity>>();

                            // An instance-handle map records which source entities already have a copy in the
                            // target; InsertCopy reuses a mapped copy instead of copying the entity again. Styled
                            // items have their own map; the product maps are per length scale (see below).
                            _logger.LogInformation($"Creating instance map...");
                            var stylesMap = new XbimInstanceHandleMap(productModel, targetModel);
                            var scale2ProductMapDict = new Dictionary<int, XbimInstanceHandleMap>();

                            _logger.LogInformation($"Creating copy of building...");
                            CreateCopyOfBuilding(targetModel, buildingModel);

                            _logger.LogInformation($"Merging products...");

                            foreach (var item in group["sets"])
                            {
                                var set_id = item["set_id"].ToString();
                                var id = item["id"].ToString();
                                // Layout names can be HTML-encoded (e.g. "&amp;"); the products-map keys are plain text.
                                var name = WebUtility.HtmlDecode(item["name"].ToString());

                                double lengthScaleRatio = 1;
                                // Q1: a single-object entry keeps this offset of 1 along the product's local X. Only
                                // catalogue selection replaces it, with its clip offset.
                                double centerOffset = 1;
                                IIfcProduct product = null;

                                _logger.LogInformation($"Processing set_id \"{set_id}\" - id \"{id}\" - name \"{name}\"...");

                                if (!aProductsMap.TryGetValue(name, out JToken productParams))
                                {
                                    //Take slow search with substring
                                    // Q8: the first key in document order that the name starts with wins, even when a
                                    // longer key also matches.
                                    foreach (var test in aProductsMap)
                                        if (name.StartsWith(test.Key))
                                        {
                                            productParams = test.Value;
                                            break;
                                        }

                                    if (productParams==null)
                                    {
                                        _logger.LogInformation($"Requested JSON item not found");

                                        if (!string.IsNullOrWhiteSpace(aJsonItemNotFoundLogger))
                                            File.AppendAllText(aJsonItemNotFoundLogger, $"Set_id \"{set_id}\" - id \"{id}\" - name \"{name}\": Requested JSON item not found\n");
                                        continue;
                                    }
                                }

                                // An array is a catalogue of variants: choose one by the set's dimensions, together
                                // with the length scale and the clip offset for the copy.
                                if (productParams is JArray productParamsArray)
                                    productParams = ProductSelector.FindMostSuitableObjectToDimensions(productParamsArray, item, out lengthScaleRatio, out centerOffset);

                                if (productParams == null)
                                {
									_logger.LogInformation($"IFC object with suitable length not found");

                                    if (!string.IsNullOrWhiteSpace(aJsonItemNotFoundLogger))
                                        File.AppendAllText(aJsonItemNotFoundLogger, $"Set_id \"{set_id}\" - id \"{id}\" - name \"{name}\": IFC object with suitable length not found\n");

                                    continue;
                                }

								// Find the source product: by ifc_guid, otherwise by ifc_name.
								string guid = productParams["ifc_guid"].ToString();
								if (!guid2productDict.ContainsKey(guid))
                                {
                                    //try to find by name
                                    string ifcName = productParams["ifc_name"].ToString();
                                    if (name2productDict.ContainsKey(ifcName))
                                    {
										_logger.LogInformation($"Requested IFC product found by name...");
										product = name2productDict[ifcName];
                                    }
                                    else
                                    {
                                        _logger.LogInformation($"Requested IFC item not found");

                                        if (!string.IsNullOrWhiteSpace(aJsonItemNotFoundLogger))
                                            File.AppendAllText(aJsonItemNotFoundLogger, $"Set_id \"{set_id}\" - id \"{id}\" - name \"{name}\": Requested IFC item not found, ifc_guid: \"{guid}\", ifc_name: \"{ifcName}\"\n");

                                        continue;
                                    }
                                }
                                else
                                {
                                    _logger.LogInformation($"Requested IFC product found by guid...");
                                    product = guid2productDict[guid];
                                }

                                _logger.LogInformation($"Creating copy...");

                                using (var txn_0129347821 = targetModel.BeginTransaction("Target product insertion"))
                                {
                                    // One product map per length scale, keyed by the scale in thousandths (truncated).
                                    // Copies at the same scale share the copied geometry items. A new scale gets its
                                    // own items, which CopyProduct stretches unless the scale is 1. CopyProduct removes
                                    // the product, placement and representation entries before each copy, so those
                                    // are always new.
                                    int roundedScale = (int)(lengthScaleRatio * 1000);
                                    if (!scale2ProductMapDict.TryGetValue(roundedScale, out XbimInstanceHandleMap productMap))
                                    {
                                        productMap = new XbimInstanceHandleMap(productModel, targetModel);
                                        scale2ProductMapDict.Add(roundedScale, productMap);
                                    }

                                    IIfcProduct productCpy = null;
                                    // CopyProduct edits the source product's placement and vertices in place, inside
                                    // this transaction on the products model, so that InsertCopy copies the new values.
                                    // The transaction is never committed: disposing it rolls the edits back, and the
                                    // next set starts from the original products model.
                                    using (var txn_4679320920 = productModel.BeginTransaction("Source product fetch"))
                                    {
                                        productCpy = ProductCopier.CopyProduct(targetModel, product, productMap, stylesMap, item as JObject, productParams as JObject, lengthScaleRatio, centerOffset);
                                        productCpy.Name += $" [{name}]";
                                        productCpy.Description = $"JSON [ID: {id}, SET_ID: {set_id}]";

                                        txn_4679320920.Dispose(); // won't change: rolls back the edits to the source
                                    }

                                    txn_0129347821.Commit();

									if (!src2targetProductDict.TryGetValue(product, out List<IPersistEntity> targetProductList))
									{
                                        targetProductList = new List<IPersistEntity>();
                                        src2targetProductDict.Add(product, targetProductList);
									}

                                    targetProductList.Add(productCpy);
                                }
                                }

                            // Q6: a group without any placed product yields no model, so the models of later
                            // groups move up one index.
                            if (src2targetProductDict.Count() < 1)
                            {
                                targetModel.Dispose();
                                continue;
                            }

                            if (CurrentOptimization == OptimizationTarget.Size)
                            {
                                _logger.LogInformation($"Optimizing Brep points...");
                                BrepPointOptimizer.OptimizeBrepPoints(targetModel);
                            }

                            _logger.LogInformation($"Creating relations...");
                            ProductRelationsCopier.CreateProductsRelations(src2targetProductDict, productModel, targetModel);
                        }
                        catch (Exception ex)
                        {
                            // Q2: "throw ex" restarts the stack trace here. Q10: the models of earlier groups
                            // were already yielded, so the caller still holds them.
                            targetModel.Dispose();
                            throw ex;
                        }

                        // group_id is read only here: only groups that yield a model need one.
                        yield return new ComposedModel(targetModel, group["group_id"].ToString());
                    }
                }
            }
        }

        // Copies every instance of the building, not only its products: the project, spatial structure,
        // units, contexts and everything else come along. One map for the whole copy keeps shared entities
        // shared. Arguments: includeInverses true, keepLabels false (the copy gets new entity labels).
        private static void CreateCopyOfBuilding(IfcStore aTargetModel, IfcStore aBuildingModel)
        {
            using (ITransaction txn_9023849023 = aTargetModel.BeginTransaction("Insert copy of building and storeys"))
            {
                _logger.LogInformation($"Gathering IFC instances...");
                var instances = Array.Empty<IIfcProduct>().Concat(aBuildingModel.Instances);

                _logger.LogInformation($"Found {instances.Count()} instances...");

                if (instances.Count() < 1)
                    throw new Exception("No IFC instances");

                _logger.LogInformation($"Copying...");

                var map = new XbimInstanceHandleMap(aBuildingModel, aTargetModel);
                foreach (var item in instances)
                    aTargetModel.InsertCopy(item, map, CopyFilters.SemanticFilterFull, true, false);

                txn_9023849023.Commit();
            }
        }
    }
}
