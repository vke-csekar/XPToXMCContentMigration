using CWXPMigration;
using CWXPMigration.Models;
using CWXPMigration.Services;
using Microsoft.Ajax.Utilities;
using Newtonsoft.Json;
using Sitecore.Data;
using Sitecore.Data.Fields;
using Sitecore.Layouts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using System.Xml;

namespace CWXPTool.Controllers
{
    public class ContentMigrationController : Controller
    {
        private const string PageTemplateId = "{8F3DE639-B021-42CE-AE90-0E07BECB6B03}";
        private const string SITECORE_XMC_PREFIX = "/sitecore/content/CW/childrens/Home/";
        private const string SITECORE_XP_PRFIX = "/sitecore/content/CHW/Home/";
        private readonly SitecoreGraphQLClient graphQLClient = new SitecoreGraphQLClient();
        private readonly SideNavMigrationService sideNavMigrationService = new SideNavMigrationService();
        private readonly string[] sideNavTemplates = new string[] { "{4D49E913-37B3-4946-9372-7BB0DCA63BC9}" };

        public ActionResult Index()
        {
            return View(new ContentMigrationModel());
        }

        [HttpPost]
        public async Task<ActionResult> Index(string itemPath = null)
        {
            var model = new ContentMigrationModel();
            List<PageDataModel> xpPageDataItems = new List<PageDataModel>();

            if (!string.IsNullOrEmpty(itemPath))
            {
                model.Items = GetPagesAndRelatedDataFromXP(itemPath);

                if (model.Items != null && model.Items.Any())
                {
                    var pageMappingItems = LoadPageMappingsFromJson();
                    if (pageMappingItems != null && pageMappingItems.Any())
                    {
                        await SyncPagesInBatches(model.Items, pageMappingItems);
                    }
                }
            }

            return View(model);
        }

        private List<PageDataModel> GetPagesAndRelatedDataFromXP(string itemPath)
        {
            var database = Sitecore.Context.Database;
            var rootItem = database.GetItem(itemPath);

            List<PageDataModel> xpPageDataItems = new List<PageDataModel>();

            if (rootItem != null && rootItem.Template.BaseTemplates.Any(t => t.ID == ID.Parse(PageTemplateId)))
            {
                var pages = rootItem.Axes
                    .GetDescendants()
                    .Where(descendant => descendant.Template.BaseTemplates.Any(t => t.ID.ToString() == PageTemplateId))
                    .ToList();

                pages.Insert(0, rootItem); // include root item itself

                foreach (var pageItem in pages)
                {
                    var pageData = ExtractPageData(pageItem, database);
                    if (pageData.Renderings.Any() && pageData.DataSources.Any())
                    {
                        xpPageDataItems.Add(pageData);
                    }
                }
                
                System.IO.File.WriteAllText("F:\\Migration\\CWXPMigrationContent.json", JsonConvert.SerializeObject(xpPageDataItems));                
            }
            
            return xpPageDataItems;
        }

        private List<PageMapping> LoadPageMappingsFromJson()
        {
            var pageMappings = JsonConvert.DeserializeObject<List<PageMapping>>(System.IO.File.ReadAllText("F:\\Migration\\CWPageMapping.json"));
            if (pageMappings != null && pageMappings.Any())
            {
                pageMappings.RemoveAll(x => string.IsNullOrEmpty(x.CURRENTURL) || string.IsNullOrEmpty(x.NEWURLPATH));
                pageMappings.ForEach(x =>
                {
                    //x.CURRENTURL = NormalizeUrlToSitecorePath(x.CURRENTURL);
                    //x.NEWURLPATH = RemoveUrlFromSitecorePath(x.NEWURLPATH);
                    
                    // For CURRENTURL (with prefix)
                    x.CURRENTURL = GetSitecorePathFromUrl(x.CURRENTURL, SITECORE_XP_PRFIX);

                    // For NEWURLPATH (no prefix)
                    x.NEWURLPATH = GetSitecorePathFromUrl(x.NEWURLPATH, SITECORE_XMC_PREFIX);
                });                
            }
            return pageMappings;
        }

        private string GetSitecorePathFromUrl(string url, string prefix = "")
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            string path = url;
            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var uri = new Uri(url);
                    path = uri.AbsolutePath;
                }
                catch (UriFormatException ex)
                {
                    Sitecore.Diagnostics.Log.Error(ex.Message, ex, typeof(ContentMigrationController));
                }
            }
            
            string normalizedPath = path.TrimStart('/').Replace("-", " ");
            return string.IsNullOrEmpty(prefix) ? normalizedPath : prefix + normalizedPath;
        }


        private PageDataModel ExtractPageData(Sitecore.Data.Items.Item pageItem, Database database)
        {
            var pageModel = new PageDataModel { Page = pageItem.Paths.FullPath };

            foreach (Field field in pageItem.Fields)
            {
                if (!field.Name.StartsWith("__") && !pageModel.Fields.Any(x => x.Name.Equals(field.Name)))
                {
                    var xpField = new XPField();
                    xpField.Name = field.Name;
                    xpField.Value = field.Value;
                    xpField.Type = field.Type;
                    pageModel.Fields.Add(xpField);
                }
            }

            var mergedLayoutXml = GetMergedLayoutXml(pageItem);
            var uniqueDataSourceIds = new HashSet<string>();

            if (!string.IsNullOrEmpty(mergedLayoutXml))
            {
                var layoutDefinition = LayoutDefinition.Parse(mergedLayoutXml);
                foreach (DeviceDefinition device in layoutDefinition.Devices)
                {
                    foreach (RenderingDefinition rendering in device.Renderings)
                    {
                        var renderingId = GetRenderingIdFromXml(rendering);
                        var dataSourceId = GetDataSourceIdFromXml(rendering);

                        if (string.IsNullOrEmpty(renderingId) || string.IsNullOrEmpty(dataSourceId))
                            continue;

                        var renderingItem = database.GetItem(renderingId);
                        if (renderingItem != null &&
                            (renderingItem.Name.Contains("PageHeadline") ||
                             renderingItem.Name.Contains("RichText") ||
                             renderingItem.Name.Contains("RichText Plain")))
                        {
                            uniqueDataSourceIds.Add(dataSourceId);

                            pageModel.Renderings.Add(new RenderingInfo
                            {
                                RenderingName = renderingItem.Name ?? "(unknown)",
                                DatasourceID = dataSourceId ?? "(none)"
                            });
                        }
                    }
                }
            }

            foreach (var dataSourceId in uniqueDataSourceIds)
            {
                var dataSourceItem = database.GetItem(dataSourceId);
                if (dataSourceItem != null && !pageModel.DataSources.Any(d => d.ID == dataSourceId))
                {
                    var dataSourceFields = new List<XPField>();
                    foreach (Field field in dataSourceItem.Fields)
                    {
                        if (!field.Name.StartsWith("__"))
                        {
                            var xpField = new XPField();
                            xpField.Name = field.Name;
                            xpField.Value = field.Value;
                            xpField.Type = field.Type;
                            dataSourceFields.Add(xpField);
                        }
                    }

                    pageModel.DataSources.Add(new DataSourceDetail
                    {
                        ID = dataSourceId,
                        Name = dataSourceItem.Name,
                        Path = dataSourceItem.Paths.FullPath,
                        Fields = dataSourceFields
                    });
                }
            }

            return pageModel;
        }

        private async Task<bool> SyncPagesInBatches(List<PageDataModel> pageDataItems, List<PageMapping> pageMappingItems)
        {
            const int batchSize = 5;
            int total = pageDataItems.Count;
            int batches = (int)Math.Ceiling(total / (double)batchSize);

            var authResponse = await AuthHelper.GetAuthTokenAsync();

            if (authResponse != null && !string.IsNullOrEmpty(authResponse.AccessToken))
            {
                pageMappingItems = SortPages(pageMappingItems);
                for (int i = 0; i < batches; i++)
                {
                    var batch = pageDataItems.Skip(i * batchSize).Take(batchSize).ToList();

                    System.Diagnostics.Debug.WriteLine($"Processing batch {i + 1} with {batch.Count} items");

                    foreach (var sourcePageItem in batch)
                    {
                        var matchedMapping = pageMappingItems
                            .FirstOrDefault(mapping =>
                                !string.IsNullOrEmpty(mapping.CURRENTURL) &&
                                mapping.CURRENTURL.Equals(sourcePageItem.Page, StringComparison.OrdinalIgnoreCase));

                        if (matchedMapping != null)
                        {
                            string xmcTemplateId = matchedMapping.PAGETEMPLATEID;

                            if (!string.IsNullOrEmpty(matchedMapping.NEWURLPATH))
                            {
                                string sourcePagePath = sourcePageItem.Page;
                                string targetItemId = string.Empty;
                                var targetItem = await graphQLClient.QuerySingleItemAsync(authResponse.AccessToken, matchedMapping.NEWURLPATH);
                                if (targetItem != null)
                                    targetItemId = targetItem.ItemId;
                                if (targetItem == null)
                                {
                                    await EnsureSitecorePathExistsAsync(matchedMapping.NEWURLPATH, authResponse.AccessToken, graphQLClient, pageMappingItems);
                                    targetItem = await graphQLClient.QuerySingleItemAsync(authResponse.AccessToken, matchedMapping.NEWURLPATH);
                                }
                                if (targetItem != null)
                                {
                                    string dataItemId = string.Empty;
                                    string sideNavItemId = string.Empty;
                                    await SyncPageFields(sourcePageItem, targetItem, authResponse.AccessToken);
                                    var dataItem = await graphQLClient.QuerySingleItemAsync(authResponse.AccessToken, matchedMapping.NEWURLPATH + "/Data");
                                    if (dataItem == null)
                                        dataItemId = await CreateItem(authResponse.AccessToken, targetItemId, "Data", "{1C82E550-EBCD-4E5D-8ABD-D50D0809541E}");
                                    else
                                        dataItemId = dataItem.ItemId;
                                    if (!string.IsNullOrEmpty(dataItemId))
                                    {
                                        var rteRenderings = sourcePageItem.Renderings.Where(r => r.RenderingName.Contains("RichText"));
                                        var pageHeadlineRenderings = sourcePageItem.Renderings.Where(r => r.RenderingName.Contains("PageHeadline"));
                                        var rtePlainRenderings = sourcePageItem.Renderings.Where(r => r.RenderingName.Contains("RichText Plain"));

                                        //Page Specific Data Migration
                                        if (sideNavTemplates.Contains(xmcTemplateId))
                                        {
                                            await sideNavMigrationService.ProcessRichTextRenderingsAsync(rteRenderings, sourcePageItem, dataItemId, matchedMapping.NEWURLPATH, authResponse.AccessToken);
                                        }
                                        else
                                        {
                                            await CreateRTEDatasources(sourcePageItem,
                                            dataItemId, graphQLClient, authResponse.AccessToken);
                                        }

                                        //Rich Text is already processed.                                                                                        
                                        await CreatePageHeadlineDatasources(sourcePageItem, pageHeadlineRenderings,
                                            dataItemId, matchedMapping, graphQLClient, authResponse.AccessToken);
                                    }
                                }
                            }
                        }
                    }

                }
            }
            else
                Sitecore.Diagnostics.Log.Warn($"Auth response is null", this);
            return true;
        }

        private async Task CreateRTEDatasources(
            PageDataModel sourcePageItem,            
            string localDataItemId,            
            SitecoreGraphQLClient graphQLClient,
            string accessToken)
        {
            if (sourcePageItem.DataSources != null && sourcePageItem.DataSources.Any())
            {
                List<SitecoreCreateItemInput> items = new List<SitecoreCreateItemInput>();
                int itemCounter = 1;
                var uniqueDatasources = sourcePageItem.DataSources.DistinctBy(x => x.ID);
                foreach (var datasource in uniqueDatasources)
                {
                    if (datasource.Fields.Any(x => x.Type.Equals("Rich Text")))
                    {
                        var rteFields = datasource.Fields.Where(x => x.Type.Equals("Rich Text"));
                        foreach (var rteField in rteFields)
                        {
                            var itemName = rteFields.Count() > 1 ? $"{datasource.Name}-{itemCounter}" : datasource.Name;
                            var inputItem = GetSitecoreCreateItemInput(itemName,
                                        "{0EFFE34A-636F-4288-BA3B-0AF056AAD42B}", localDataItemId, "text", rteField.Value);
                            items.Add(inputItem);
                        }
                    }
                }
                if (items.Any())
                    await graphQLClient.CreateBulkItemsBatchedAsync(items, accessToken, 10);
            }
        }

        private async Task CreatePageHeadlineDatasources(
            PageDataModel sourcePageItem,
            IEnumerable<RenderingInfo> pageHeadlineRenderings,
            string localDataItemId,
            PageMapping matchedMapping, 
            SitecoreGraphQLClient graphQLClient, 
            string accessToken)
        {
            if (pageHeadlineRenderings != null && pageHeadlineRenderings.Any())
            {
                var pageHeadlineRenderingsDatasourceIds = pageHeadlineRenderings.Select(x => x.DatasourceID).ToList();
                if (pageHeadlineRenderingsDatasourceIds != null && pageHeadlineRenderingsDatasourceIds.Any())
                {
                    var datasources = sourcePageItem.DataSources
                        .Where(x => pageHeadlineRenderingsDatasourceIds.Any(id => id.Equals(x.ID, StringComparison.OrdinalIgnoreCase)))
                        .DistinctBy(x => x.ID).ToList();

                    //var datasources = sourcePageItem.DataSources.Where(x => pageHeadlineRenderingsDatasourceIds.Contains(x.ID))?.DistinctBy(x => x.ID);
                    if (datasources != null && datasources.Any())
                    {
                        List<SitecoreCreateItemInput> items = new List<SitecoreCreateItemInput>();
                        foreach (var datasource in datasources)
                        {
                            if (datasource.Fields.Any(x => x.Name.Equals("Headline")))
                            {
                                var headlineField = datasource.Fields.FirstOrDefault(x => x.Name.Equals("Headline"));
                                if (headlineField != null && !string.IsNullOrEmpty(headlineField.Value))
                                {
                                    var path = $"{matchedMapping.NEWURLPATH}/Data/{datasource.Name}";
                                    var existingItem = await graphQLClient.QuerySingleItemAsync(accessToken, path);
                                    if (existingItem == null)
                                    {
                                        var inputItem = GetSitecoreCreateItemInput(datasource.Name,
                                    "{0EFFE34A-636F-4288-BA3B-0AF056AAD42B}", localDataItemId, "text", headlineField.Value);
                                        items.Add(inputItem);
                                    }
                                }
                            }
                        }
                        if (items.Any())
                            await graphQLClient.CreateBulkItemsBatchedAsync(items, accessToken, 10);
                    }
                }
            }
        }
        private string GetRenderingIdFromXml(RenderingDefinition rendering)
        {
            try
            {
                var xml = rendering.ToXml();
                var doc = new XmlDocument();
                doc.LoadXml(xml);
                return doc.DocumentElement?.Attributes["s:id"]?.Value;
            }
            catch
            {
                return null;
            }
        }

        private string GetDataSourceIdFromXml(RenderingDefinition rendering)
        {
            try
            {
                var xml = rendering.ToXml();
                var doc = new XmlDocument();
                doc.LoadXml(xml);
                return doc.DocumentElement?.Attributes["s:ds"]?.Value;
            }
            catch
            {
                return null;
            }
        }

        private string GetMergedLayoutXml(Sitecore.Data.Items.Item item)
        {
            var sharedLayout = item.Fields[Sitecore.FieldIDs.LayoutField]?.Value;
            var finalLayout = item.Fields[Sitecore.FieldIDs.FinalLayoutField]?.Value;

            if (string.IsNullOrWhiteSpace(finalLayout)) return sharedLayout;
            if (string.IsNullOrWhiteSpace(sharedLayout)) return finalLayout;

            var mergedLayout = new LayoutDefinition();

            var sharedDef = LayoutDefinition.Parse(sharedLayout);
            foreach (DeviceDefinition device in sharedDef.Devices)
                mergedLayout.Devices.Add(device);

            var finalDef = LayoutDefinition.Parse(finalLayout);
            foreach (DeviceDefinition device in finalDef.Devices)
            {
                var existing = mergedLayout.GetDevice(device.ID);
                if (existing != null)
                    existing.Renderings = device.Renderings;
                else
                    mergedLayout.Devices.Add(device);
            }

            return mergedLayout.ToXml();
        }        

        private SitecoreCreateItemInput GetSitecoreCreateItemInput(string itemName, string templateId, string parentId, string fieldName, string fieldValue)
        {
            var inputItem = new SitecoreCreateItemInput();
            inputItem.Name = itemName;
            inputItem.TemplateId = templateId;
            inputItem.Language = "en";
            inputItem.Parent = parentId;
            var fields = new List<SitecoreFieldInput>();
            if (!string.IsNullOrEmpty(fieldName))
            {
                fields.Add(new SitecoreFieldInput()
                {
                    Name = fieldName,
                    Value = fieldValue
                });
                inputItem.Fields = fields;
            }
            return inputItem;
        }

        private async Task<string> CreateItem(string accessToken, string targetItemId, string itemName, string templateId, List<SitecoreFieldInput> fields = null)
        {
            string createdItemId = string.Empty;
            var createDataItemInput = new SitecoreCreateItemInput();
            createDataItemInput.Name = itemName;
            createDataItemInput.TemplateId = templateId;
            createDataItemInput.Language = "en";
            createDataItemInput.Parent = targetItemId;
            if (fields != null)
            {
                createDataItemInput.Fields = fields;
            }
            var createdItem = await graphQLClient.CreateBulkItemsBatchedAsync(new List<SitecoreCreateItemInput>() { createDataItemInput }, accessToken, 10);
            if (createdItem != null)
                createdItemId = createdItem.FirstOrDefault().ItemId;
            return createdItemId;
        }

        public static List<PageMapping> SortPages(List<PageMapping> pages)
        {            
            // Sort based on itemPath segments
            pages.Sort((a, b) =>
            {
                var pathA = a.NEWURLPATH.Split(new string[] { "/" }, StringSplitOptions.RemoveEmptyEntries);
                var pathB = b.NEWURLPATH.Split(new string[] { "/" }, StringSplitOptions.RemoveEmptyEntries);

                int minLength = Math.Min(pathA.Length, pathB.Length);
                for (int i = 0; i < minLength; i++)
                {
                    int comparison = string.Compare(pathA[i], pathB[i], StringComparison.OrdinalIgnoreCase);
                    if (comparison != 0)
                        return comparison;
                }
                return pathA.Length.CompareTo(pathB.Length); // shorter path comes first
            });

            return pages;
        }

        private async Task<bool> SyncPageFields(PageDataModel sourcePageItem, SitecoreItem targetItem, string accessToken)
        {
            var fields = new List<SitecoreFieldInput>() { };
            var pageMetaTitle = GetSitecoreFieldInput(sourcePageItem, "PageMetaTitle", "pageMetaTitle");
            if (pageMetaTitle != null)
                fields.Add(pageMetaTitle);
            var metaDescription = GetSitecoreFieldInput(sourcePageItem, "MetaDescription", "metaDescription");
            if (metaDescription != null)
                fields.Add(metaDescription);
            var displayDescription = GetSitecoreFieldInput(sourcePageItem, "DisplayDescription", "Description");
            if (displayDescription != null)
                fields.Add(displayDescription);
            var title = GetSitecoreFieldInput(sourcePageItem, "Title", "Title");
            if (title != null)
                fields.Add(title);
            var metaKeywords = GetSitecoreFieldInput(sourcePageItem, "MetaKeywords", "metaKeywords");
            if (metaKeywords != null)
                fields.Add(metaKeywords);
            if (sourcePageItem.Fields.Any(x => !x.Name.Equals("DisplayDescription") && x.Type.Equals("Rich Text")))
            {
                var richTextField = sourcePageItem.Fields.FirstOrDefault(x => !x.Name.Equals("DisplayDescription") && x.Type.Equals("Rich Text"));
                if (richTextField != null)
                {
                    fields.Add(new SitecoreFieldInput()
                    {
                        Name = "content",
                        Value = richTextField.Value,
                    });
                }
            }
            if (fields.Any())
            {
                var updateItemInput = new SitecoreUpdateItemInput()
                {
                    ItemId = targetItem.ItemId,
                    Fields = fields,
                    Language = "en"
                };
                return await graphQLClient.UpdateBulkItemsBatchedAsync(new List<SitecoreUpdateItemInput>() { updateItemInput }, accessToken, 10);
            }

            return false;
        }

        public async Task EnsureSitecorePathExistsAsync(
    string targetPath,
    string accessToken,
    SitecoreGraphQLClient graphQLClient,
    List<PageMapping> pageMappingItems)
        {
            // Find the nearest existing parent
            int totalSegments = PathFormatter.GetUrlSegmentsCount(targetPath);
            string existingParentId = string.Empty;
            string existingParentPath = string.Empty;

            for (int i = totalSegments - 1; i >= 0; i--)
            {
                var parentPath = PathFormatter.GetParentPath(targetPath, i);

                if (!string.IsNullOrEmpty(parentPath))
                {
                    var parentItem = await graphQLClient.QuerySingleItemAsync(accessToken, parentPath);

                    if (parentItem != null)
                    {
                        existingParentId = parentItem.ItemId;
                        existingParentPath = parentPath;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(existingParentId))
            {
                Sitecore.Diagnostics.Log.Warn($"No existing parent found for path {targetPath}. Cannot proceed with creation.", this);
            }

            // Collect segments to create from existingParentPath -> targetPath
            var segmentsToCreate = new List<string>();

            for (int k = PathFormatter.GetUrlSegmentsCount(existingParentPath) + 1; k <= totalSegments; k++)
            {
                var segmentPath = PathFormatter.GetParentPath(targetPath, k);

                if (!string.IsNullOrEmpty(segmentPath))
                {
                    Sitecore.Diagnostics.Log.Info(segmentPath, typeof(ContentMigrationController));
                    segmentsToCreate.Add(segmentPath);
                }
            }

            string parentItemId = existingParentId;            

            foreach (var segmentPath in segmentsToCreate)
            {
                var segmentMapping = pageMappingItems.FirstOrDefault(x =>
                    !string.IsNullOrEmpty(x.PAGETEMPLATEID) &&
                    x.NEWURLPATH.Equals(segmentPath, StringComparison.OrdinalIgnoreCase));

                // Check if segment already exists
                var existingSegmentItem = await graphQLClient.QuerySingleItemAsync(accessToken, segmentPath);

                if (segmentMapping != null && existingSegmentItem == null)
                {
                    // Create the missing item
                    var createItemInput = new SitecoreCreateItemInput
                    {
                        Name = PathFormatter.GetLastPathSegment(segmentPath),
                        TemplateId = segmentMapping.PAGETEMPLATEID,
                        Language = "en",
                        Parent = parentItemId
                    };

                    var createdItems = await graphQLClient.CreateBulkItemsBatchedAsync(
                        new List<SitecoreCreateItemInput> { createItemInput },
                        accessToken,
                        10
                    );

                    // Use the created item's ID as the parent for the next iteration
                    parentItemId = createdItems.First().ItemId;
                }
                else
                {
                    // If it already exists, use its ID as parent
                    parentItemId = existingSegmentItem.ItemId;
                }
            }
        }

        private SitecoreFieldInput GetSitecoreFieldInput(PageDataModel sourcePageItem, string xpFieldName, string xmcFieldName)
        {
            var fieldValue = sourcePageItem.Fields.FirstOrDefault(x => x.Name == xpFieldName && !string.IsNullOrEmpty(x.Value))?.Value ?? string.Empty;
            if (!string.IsNullOrEmpty(fieldValue))
            {
                return new SitecoreFieldInput()
                {
                    Name = xmcFieldName,
                    Value = sourcePageItem.Fields.FirstOrDefault(x => x.Name == xpFieldName && !string.IsNullOrEmpty(x.Value))?.Value ?? string.Empty,
                };
            }
            return null;
        }
    }
}
