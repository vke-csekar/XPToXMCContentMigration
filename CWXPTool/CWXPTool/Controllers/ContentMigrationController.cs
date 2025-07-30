using CWXPMigration;
using CWXPMigration.Models;
using CWXPMigration.Services;
using Microsoft.Ajax.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Sitecore.Data;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.Data.Managers;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Threading.Tasks;
using System.Web.Mvc;
using System.Web.UI.WebControls;

namespace CWXPTool.Controllers
{
    public class ContentMigrationController : Controller
    {
                
        public ISitecoreGraphQLClient SitecoreGraphQLClient { get; set; }
        public ISideNavMigrationService SideNavMigrationService { get; set; }
        public ITeachingSheetMigrationService TeachingSheetMigrationService { get; set; }

        string _environment = string.Empty;
        string _accessToken = string.Empty;

        public ContentMigrationController()
        {
            this.SitecoreGraphQLClient = Sitecore.DependencyInjection.ServiceLocator.ServiceProvider.GetService<ISitecoreGraphQLClient>();
            this.SideNavMigrationService = Sitecore.DependencyInjection.ServiceLocator.ServiceProvider.GetService<ISideNavMigrationService>();
            this.TeachingSheetMigrationService = Sitecore.DependencyInjection.ServiceLocator.ServiceProvider.GetService<ITeachingSheetMigrationService>();
        }

        public ActionResult Index()
        {
            return View(new ContentMigrationModel());
        }

        [HttpPost]
        public ActionResult GetTemplatesUnderPath(string parentPath)
        {
            var templates = new List<TemplateSummaryModel>();

            var parentItem = Sitecore.Context.Database.GetItem(parentPath);
            if (parentItem == null) return Json(templates);

            var usedTemplateIds = parentItem.Axes.GetDescendants()
                .Where(descendant => descendant.Template.BaseTemplates.Any(t => t.ID.ToString() == XP_Page_Template_Constants.XP_BASE_PAGE_TEMPLATEID))
                .Select(i => i.TemplateID)
                .Distinct();

            foreach (var templateId in usedTemplateIds)
            {
                var template = TemplateManager.GetTemplate(templateId, parentItem.Database);
                if (template != null)
                {
                    templates.Add(new TemplateSummaryModel
                    {
                        TemplateId = template.ID.ToString(),
                        TemplateName = template.Name,
                        Fields = template.GetFields()
                        .Where(field => !field.Name.StartsWith("__"))
                        .Select(x => new TemplateFieldModel()
                        {
                            Name = x.Name,
                            Type = x.Type
                        }).ToList()
                    });
                }
            }

            return Json(templates);
        }        



        [HttpPost]
        public async Task<ActionResult> SyncContent(string itemPath, List<TemplateFieldMapping> mappingSelections, 
            string xmcItemPath = "", bool syncComponents = false, bool createPages = false, string environment = "DEV")
        {
            Sitecore.Diagnostics.Log.Info("itemPath: " + itemPath, this);
            Sitecore.Diagnostics.Log.Info("xmcItemPath: " + xmcItemPath, this);
            Sitecore.Diagnostics.Log.Info("environment: " + environment, this);
            Sitecore.Diagnostics.Log.Info("syncComponents: " + syncComponents, this);
            Sitecore.Diagnostics.Log.Info("createPages: " + createPages, this);
            Sitecore.Diagnostics.Log.Info("mappingSelections: " + JsonConvert.SerializeObject(mappingSelections), this);
            List<SyncContentResponse> syncResults = new List<SyncContentResponse>();
            
            var model = new ContentMigrationModel
            {
                ItemPath = itemPath,
                Environment = environment
            };

            _environment = environment;

            List<PageDataModel> xpPageDataItems = new List<PageDataModel>();

            if (!string.IsNullOrEmpty(itemPath))
            {
                model.Items = GetPagesAndRelatedDataFromXP(itemPath);

                if (model.Items != null && model.Items.Any())
                {
                    var pageMappingItems = LoadPageMappingsFromJson();
                    if (pageMappingItems != null && pageMappingItems.Any())
                    {
                        if (!string.IsNullOrEmpty(xmcItemPath) && mappingSelections != null && mappingSelections.Any())
                        {
                            Sitecore.Diagnostics.Log.Info("Processing mapping selections", this);
                            foreach (var item in model.Items)
                            {
                                var mappingSelection = mappingSelections.FirstOrDefault(x => item.TemplateID.Equals(ID.Parse(x.TemplateId)));
                                if(mappingSelection != null)
                                {
                                    Sitecore.Diagnostics.Log.Info($"Mapping found for {item.Page}/{mappingSelection.XMTemplateId}", this);
                                    string itemRelativePath = item.Page.Replace(itemPath, "");
                                    pageMappingItems.Add(new PageMapping()
                                    {
                                        CURRENTURL = item.Page,
                                        NEWURLPATH = $"{xmcItemPath}{itemRelativePath}",
                                        PAGETEMPLATEID = mappingSelection.XMTemplateId
                                    });
                                    Sitecore.Diagnostics.Log.Info($"Mapping CURRENTURL: {item.Page}", this);
                                    Sitecore.Diagnostics.Log.Info($"Mapping NEWURLPATH: {xmcItemPath}{itemRelativePath}", this);
                                    Sitecore.Diagnostics.Log.Info($"Mapping PAGETEMPLATEID: {mappingSelection.XMTemplateId}", this);
                                }                                
                            }
                        }
                        syncResults = await SyncPagesInBatches(model.Items, pageMappingItems, mappingSelections, createPages, syncComponents);
                    }
                }
            }

            return Json(syncResults);
        }


        /// <summary>
        /// Retrieves parent and child pages from Sitecore XP for the specified path.
        /// For each page, collects common page fields, predefined renderings, and their datasources.
        /// Consolidates the data into a structured object and saves it as a JSON file (Just logging/troubleshooting).
        /// </summary>
        /// <param name="itemPath">The Sitecore path of the parent item to start data retrieval from.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private List<PageDataModel> GetPagesAndRelatedDataFromXP(string itemPath)
        {
            var database = Sitecore.Context.Database;
            var rootItem = IsGuidInput(itemPath) ? database.GetItem(ID.Parse(itemPath)) : database.GetItem(itemPath);

            List<PageDataModel> xpPageDataItems = new List<PageDataModel>();

            //if (rootItem != null && rootItem.Template.BaseTemplates.Any(t => t.ID == ID.Parse(XP_Page_Template_Constants.XP_BASE_PAGE_TEMPLATEID)))
            if(rootItem != null)
            {
                var pages = rootItem.Axes
                    .GetDescendants()
                    .Where(descendant => descendant.Template.BaseTemplates.Any(t => t.ID.ToString() == XP_Page_Template_Constants.XP_BASE_PAGE_TEMPLATEID))
                    .ToList();

                pages.Insert(0, rootItem); // include root item itself

                foreach (var pageItem in pages)
                {
                    var pageData = ExtractPageData(pageItem, database);
                    Sitecore.Diagnostics.Log.Info("XP Page ItemID: " + pageData.ItemID, this);
                    Sitecore.Diagnostics.Log.Info("XP Page: " + pageData.Page, this);
                    xpPageDataItems.Add(pageData);
                }

                System.IO.File.WriteAllText(Constants.XP_MIGRATION_LOG_JSON_PATH, JsonConvert.SerializeObject(xpPageDataItems));
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

                    // Handles CURRENTURL (with prefix).
                    // The CURRENTURL value from Excel contains a full URL; this extracts only the corresponding Sitecore content path.
                    // Cleans the URL by removing any encoded or unwanted special characters.
                    x.CURRENTURL = GetSitecorePathFromUrl(x.CURRENTURL, x.PAGETEMPLATEID, Constants.SITECORE_XP_PRFIX);
                    Sitecore.Diagnostics.Log.Info("CURRENTURL: " + x.CURRENTURL, this);
                    // Handles NEWURLPATH (with prefix).
                    // The NEWURLPATH value from Excel contains a partial path of Sitecore item; this appends XMC root paths.
                    // Cleans the URL by removing any encoded or unwanted special characters.
                    x.NEWURLPATH = GetSitecorePathFromUrl(x.NEWURLPATH, x.PAGETEMPLATEID, Constants.SITECORE_XMC_ROOT_PATH + "/Home/");
                    Sitecore.Diagnostics.Log.Info("NEWURLPATH: " + x.NEWURLPATH, this);
                });
            }
            return pageMappings;
        }

        private string GetSitecorePathFromUrl(string url, string pageTemplateId, string prefix = "")
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
                    path = Uri.UnescapeDataString(uri.AbsolutePath);
                }
                catch (UriFormatException ex)
                {
                    Sitecore.Diagnostics.Log.Error(ex.Message, ex, typeof(ContentMigrationController));
                }
            }            

            string normalizedPath = pageTemplateId.Equals(XMC_Page_Template_Constants.Teaching_Sheets) ? path.TrimStart('/') : path.TrimStart('/').Replace("-", " ");
            return string.IsNullOrEmpty(prefix) ? normalizedPath : prefix + normalizedPath;
        }


        private PageDataModel ExtractPageData(Sitecore.Data.Items.Item pageItem, Database database)
        {
            var pageModel = new PageDataModel { Page = pageItem.Paths.FullPath };
            pageModel.ItemID = pageItem.ID;
            pageModel.TemplateID = pageItem.TemplateID;
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

            var uniqueDataSourceIds = new HashSet<string>();

            var renderingInfos = GetRenderingsForCurrentDevice(pageItem);

            if (renderingInfos != null && renderingInfos.Any())
            {
                foreach (var renderingInfo in renderingInfos)
                {
                    if (renderingInfo != null && XP_RenderingName_Constants.XP_RENDERING_NAMES.Contains(renderingInfo.RenderingName))
                    {
                        uniqueDataSourceIds.Add(renderingInfo.DatasourceID);
                        pageModel.Renderings.Add(renderingInfo);
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

        private async Task<List<SyncContentResponse>> SyncPagesInBatches(List<PageDataModel> pageDataItems, 
            List<PageMapping> pageMappingItems, List<TemplateFieldMapping> mappingSelections, bool createPages = false, bool syncComponents = false)
        {
            const int batchSize = 5;
            int total = pageDataItems.Count;
            int batches = (int)Math.Ceiling(total / (double)batchSize);

            var database = Sitecore.Context.Database;
            var syncResults = new List<SyncContentResponse>();

            var authResponse = await AuthHelper.GetAuthTokenAsync(_environment);

            if (authResponse != null && !string.IsNullOrEmpty(authResponse.AccessToken))
            {
                _accessToken = authResponse.AccessToken;
                pageMappingItems = SortPages(pageMappingItems);
                for (int i = 0; i < batches; i++)
                {
                    var batch = pageDataItems.Skip(i * batchSize).Take(batchSize).ToList();

                    System.Diagnostics.Debug.WriteLine($"Processing batch {i + 1} with {batch.Count} items");

                    foreach (var batchItem in batch)
                    {
                        Sitecore.Diagnostics.Log.Info($"Syncing started for {batchItem.Page}", this);
                        var response = new SyncContentResponse
                        {
                            Success = false,
                            SyncedItemPath = batchItem.Page,
                            Errors = new List<string>()
                        };

                        try
                        {
                            var sourcePageItem = batchItem;
                            var matchedMapping = pageMappingItems
                                .FirstOrDefault(mapping =>
                                    !string.IsNullOrEmpty(mapping.CURRENTURL) &&
                                    mapping.CURRENTURL.Equals(sourcePageItem.Page, StringComparison.OrdinalIgnoreCase));

                            if (matchedMapping != null)
                            {
                                response.SyncedItemPath = matchedMapping.NEWURLPATH;

                                string xmcTemplateId = matchedMapping.PAGETEMPLATEID;
                                if (!string.IsNullOrEmpty(matchedMapping.NEWURLPATH))
                                {
                                    string sourcePagePath = sourcePageItem.Page;
                                    string targetItemId = string.Empty;
                                    var targetItem = await SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, matchedMapping.NEWURLPATH);                                    
                                    if (targetItem == null && createPages)
                                    {
                                        Sitecore.Diagnostics.Log.Info($"Page not exists on XMC, creating page hierarchy", this);
                                        await EnsureSitecorePathExistsAsync(matchedMapping.NEWURLPATH, pageMappingItems);
                                        targetItem = await SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, matchedMapping.NEWURLPATH);
                                    }
                                    if (targetItem != null)
                                    {
                                        targetItemId = targetItem.ItemId;
                                        Sitecore.Diagnostics.Log.Info($"Page exists on XMC {targetItemId}", this);
                                        string dataItemId = string.Empty;
                                        string sideNavItemId = string.Empty;

                                        var headlineRenderings = new List<RenderingInfo>();
                                        var headlineRenderings1 = sourcePageItem.Renderings.Where(r => r.RenderingName.Contains(XP_RenderingName_Constants.Headline)).ToList();
                                        if (headlineRenderings1 != null && headlineRenderings1.Any())
                                            headlineRenderings.AddRange(headlineRenderings1);
                                        var headlineRenderings2 = sourcePageItem.Renderings.Where(r => r.RenderingName.Contains(XP_RenderingName_Constants.PageHeadline));
                                        if (headlineRenderings2 != null && headlineRenderings2.Any())
                                            headlineRenderings.AddRange(headlineRenderings2);

                                        await SyncPageFields(sourcePageItem, matchedMapping, targetItem, headlineRenderings, mappingSelections);

                                        if (syncComponents)
                                        {
                                            Sitecore.Diagnostics.Log.Info($"Page components sync enabled", this);
                                            var dataItem = await SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, matchedMapping.NEWURLPATH + "/Data");
                                            if (dataItem == null)
                                                dataItemId = await CreateItem(targetItemId, "Data", XMC_Template_Constants.Data);
                                            else
                                                dataItemId = dataItem.ItemId;
                                            if (!string.IsNullOrEmpty(dataItemId))
                                            {
                                                var rteRenderings = sourcePageItem.Renderings.Where(r => r.RenderingName.Contains(XP_RenderingName_Constants.RichText));

                                                var rtePlainRenderings = sourcePageItem.Renderings.Where(r => r.RenderingName.Contains(XP_RenderingName_Constants.RichText_Plain));

                                                //Page Specific Data Migration
                                                if (XMC_Page_Template_Constants.Side_Nav_Templates.Contains(xmcTemplateId))
                                                {
                                                    await SideNavMigrationService.ProcessAsync(rteRenderings, sourcePageItem, dataItemId, matchedMapping.NEWURLPATH, _environment, _accessToken);
                                                    sourcePageItem = RemoveRenderingDatasources(sourcePageItem, sourcePageItem.Renderings, XP_RenderingName_Constants.RichText);
                                                }

                                                if (xmcTemplateId.Equals(XMC_Page_Template_Constants.Teaching_Sheets))
                                                {
                                                    await TeachingSheetMigrationService.ProcessAsync(rteRenderings, sourcePageItem, dataItemId, matchedMapping.NEWURLPATH, _environment, _accessToken);
                                                    sourcePageItem = RemoveRenderingDatasources(sourcePageItem, sourcePageItem.Renderings, XP_RenderingName_Constants.RichText);
                                                    sourcePageItem = RemoveRenderingDatasources(sourcePageItem, sourcePageItem.Renderings, XP_RenderingName_Constants.Multi_Button_Callout);
                                                }

                                                if (XMC_Page_Template_Constants.Location_Pages.Contains(matchedMapping.PAGETEMPLATEID))
                                                {
                                                    var pageItem = database.GetItem(sourcePageItem.ItemID);

                                                    if (pageItem.HasChildren)
                                                    {
                                                        var localDatasourceItems = pageItem.Axes.GetDescendants()
                                                        .Where(descendant =>
                                                            descendant.TemplateID.ToString().Equals(Constants.OFFICE_HOURS_FOLDER_TEMPLATEID, StringComparison.OrdinalIgnoreCase) ||
                                                            descendant.Parent?.TemplateID.ToString().Equals(Constants.OFFICE_HOURS_FOLDER_TEMPLATEID, StringComparison.OrdinalIgnoreCase) == true ||
                                                            descendant.TemplateID.ToString().Equals(Constants.PHONE_HOURS_FOLDER_TEMPLATEID, StringComparison.OrdinalIgnoreCase) ||
                                                            descendant.Parent?.TemplateID.ToString().Equals(Constants.PHONE_HOURS_FOLDER_TEMPLATEID, StringComparison.OrdinalIgnoreCase) == true
                                                        ).ToArray();

                                                        //Office Hours
                                                        await CreateOfficeHoursDatasources(localDatasourceItems, matchedMapping.NEWURLPATH, dataItemId, Constants.OFFICE_HOURS_FOLDER_TEMPLATEID);
                                                        //Phone Hours
                                                        await CreateOfficeHoursDatasources(localDatasourceItems, matchedMapping.NEWURLPATH, dataItemId, Constants.PHONE_HOURS_FOLDER_TEMPLATEID);
                                                    }
                                                }

                                                var videMainBodyRenderings = sourcePageItem.Renderings.Where(r => r.RenderingName.Contains(XP_RenderingName_Constants.Video_Main_Body)).ToList();
                                                if (videMainBodyRenderings != null && videMainBodyRenderings.Any())
                                                {
                                                    videMainBodyRenderings.RemoveAll(x => string.IsNullOrEmpty(x.DatasourceID));
                                                    if (videMainBodyRenderings != null && videMainBodyRenderings.Any())
                                                    {
                                                        var datasourceIds = videMainBodyRenderings.Select(x => x.DatasourceID).ToList();
                                                        await CreateTextMediaDatasources(datasourceIds, sourcePageItem, matchedMapping, dataItemId);
                                                        sourcePageItem.DataSources.RemoveAll(x => datasourceIds.Any(id => id.Equals(x.ID, StringComparison.OrdinalIgnoreCase)));
                                                    }
                                                }

                                                await CreatePageHeadlineDatasources(sourcePageItem, headlineRenderings,
                                                    dataItemId, matchedMapping);

                                                await CreateRTEDatasources(sourcePageItem, matchedMapping,
                                                    dataItemId);
                                            }
                                        }

                                        response.Success = true;
                                        response.Message = $"Page synced successfully: {matchedMapping.NEWURLPATH}";
                                    }
                                    else
                                        response.Errors.Add("Target item not found or could not be created.");
                                }
                                else
                                    response.Errors.Add("NEWURLPATH is empty.");
                            }
                            else
                            {
                                response.Errors.Add("Mapping not found for this page.");
                                Sitecore.Diagnostics.Log.Info($"Mapping not found for this page {batchItem.Page}", this);
                            }
                        }
                        catch (Exception ex)
                        {
                            Sitecore.Diagnostics.Log.Error(ex.Message, ex, this);
                            response.Errors.Add($"Exception: {ex.Message}");
                        }
                        syncResults.Add(response);
                    }

                }
            }
            else
            {
                Sitecore.Diagnostics.Log.Warn($"Auth response is null", this);
                syncResults.Add(new SyncContentResponse
                {
                    Success = false,
                    SyncedItemPath = "N/A",
                    Errors = new List<string> { "Auth token is missing or invalid." },
                    Message = "Unable to start sync due to authentication failure."
                });
            }
            return syncResults;
        }

        private async Task CreateOfficeHoursDatasources(Item[] items, string newUrlPath, string dataItemId, string templateId)
        {
            var officeHoursFolderItem = items.FirstOrDefault(x => x.TemplateID == ID.Parse(templateId));
            if (officeHoursFolderItem != null)
            {
                var createdItemId = string.Empty;
                var xmcOfficeHoursFolder = await SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, newUrlPath + $"/Data/{officeHoursFolderItem.Name}");
                if (xmcOfficeHoursFolder == null)
                {
                    createdItemId = await CreateItem(dataItemId, officeHoursFolderItem.Name, officeHoursFolderItem.TemplateID.ToString());
                }
                else
                    createdItemId = xmcOfficeHoursFolder.ItemId;
                if (!officeHoursFolderItem.HasChildren)
                    return;
                if (!string.IsNullOrEmpty(createdItemId))
                {
                    var officeHoursItems = items.Where(x => x.Parent.TemplateID == ID.Parse(templateId));
                    if (officeHoursItems != null && officeHoursItems.Any())
                    {
                        if (!string.IsNullOrEmpty(createdItemId))
                        {
                            foreach (var item in officeHoursItems)
                            {
                                var xmcOfficeHours = await SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, newUrlPath + $"/Data/{item.Name}");
                                if (xmcOfficeHours != null)
                                    continue;
                                var fields = new List<SitecoreFieldInput>();
                                var officeHourType = GetSitecoreFieldInput(item, "Office Hour Type", "officeHourType");
                                if (officeHourType != null)
                                    fields.Add(officeHourType);
                                var open = GetSitecoreFieldInput(item, "Open", "open");
                                if (open != null)
                                    fields.Add(open);
                                var close = GetSitecoreFieldInput(item, "Close", "close");
                                if (close != null)
                                    fields.Add(close);
                                var path = $"{newUrlPath}/Data/{officeHoursFolderItem.Name}/{item.Name}";
                                var existingItem = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, path);
                                if (existingItem == null)
                                {
                                    await CreateItem(createdItemId, item.Name, item.TemplateID.ToString(), fields);
                                }
                            }
                        }
                    }
                }
            }
        }

        private async Task CreateRTEDatasources(
            PageDataModel sourcePageItem,
            PageMapping pageMapping,
            string localDataItemId)
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
                            var path = $"{pageMapping.NEWURLPATH}/Data/{itemName}";
                            var existingItem = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, path);
                            if (existingItem == null)
                            {
                                var inputItem = XMCItemUtility.GetSitecoreCreateItemInput(itemName,
                                        XMC_Template_Constants.RTE, localDataItemId, "text", rteField.Value);
                                items.Add(inputItem);
                            }
                        }
                    }
                }
                if (items.Any())
                    await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(items, _environment, _accessToken, 10);
            }
        }

        private async Task CreateTextMediaDatasources(
            List<string> datasourceIds,
            PageDataModel sourcePageItem,
            PageMapping pageMapping,
            string localDataItemId)
        {
            var datasources = sourcePageItem.DataSources
                    .Where(x => datasourceIds.Any(id => id.Equals(x.ID, StringComparison.OrdinalIgnoreCase)))
                    .DistinctBy(x => x.ID).ToList();
            List<SitecoreCreateItemInput> items = new List<SitecoreCreateItemInput>();            
            foreach (var datasource in datasources)
            {
                var fields = new List<SitecoreFieldInput>();
                var title = XMCItemUtility.GetSitecoreFieldInput(datasource, "Title", "title");
                if (title != null)
                    fields.Add(title);
                
                var richTextAbove = datasource.Fields.FirstOrDefault(x => x.Name == "RichTextAbove" && !string.IsNullOrEmpty(x.Value))?.Value ?? string.Empty;
                var richTextBelow = datasource.Fields.FirstOrDefault(x => x.Name == "RichTextBelow" && !string.IsNullOrEmpty(x.Value))?.Value ?? string.Empty;

                if (!string.IsNullOrEmpty(richTextBelow))
                {
                    var field = new SitecoreFieldInput()
                    {
                        Name = "description",
                        Value = richTextBelow,
                    };
                    fields.Add(field);
                }                

                var videoID = XMCItemUtility.GetSitecoreFieldInput(datasource, "VideoId", "videoID");
                if (videoID != null)
                    fields.Add(videoID);
                var image = XMCItemUtility.GetSitecoreFieldInput(datasource, "Thumbnail", "image");
                if (image != null)
                    fields.Add(image);
                var path = $"{pageMapping.NEWURLPATH}/Data/{datasource.Name}";
                var existingItem = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, path);
                if (existingItem == null)
                    await CreateItem(localDataItemId, datasource.Name, XMC_Template_Constants.Text_Media, fields);

                if(!string.IsNullOrEmpty(richTextAbove))
                {
                    var field = new SitecoreFieldInput()
                    {
                        Name = "text",
                        Value = richTextAbove,
                    };
                    string itemName = PathFormatter.FormatItemName($"{datasource.Name} top");
                    path = $"{pageMapping.NEWURLPATH}/Data/{itemName}";
                    existingItem = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, path);
                    if (existingItem == null)
                        await CreateItem(localDataItemId, itemName, XMC_Template_Constants.RTE, new List<SitecoreFieldInput>() { field });
                }
            }
        }

        private string GetPageHeadline(
            PageDataModel sourcePageItem,
            List<RenderingInfo> headlineRenderings)
        {
            if (headlineRenderings != null && headlineRenderings.Any())
            {
                var pageHeadlineRenderingsDatasourceIds = headlineRenderings.Select(x => x.DatasourceID).ToList();
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
                                    return headlineField.Value;
                                }
                            }
                        }
                    }
                }
            }
            return string.Empty;
        }

        private async Task CreatePageHeadlineDatasources(
            PageDataModel sourcePageItem,
            IEnumerable<RenderingInfo> pageHeadlineRenderings,
            string localDataItemId,
            PageMapping matchedMapping)
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
                        int counter = 0;
                        foreach (var datasource in datasources)
                        {
                            if (datasource.Fields.Any(x => x.Name.Equals("Headline")))
                            {
                                var headlineField = datasource.Fields.FirstOrDefault(x => x.Name.Equals("Headline"));
                                if (headlineField != null && !string.IsNullOrEmpty(headlineField.Value))
                                {
                                    //Always first page headline will be syncted page title field.
                                    //Rest page headline datasources created as RTE in XMC Cloud
                                    if (counter == 0)
                                    {
                                        if (matchedMapping.PAGETEMPLATEID.Equals(XMC_Page_Template_Constants.Condition_Treatment))
                                            continue;
                                        if (matchedMapping.PAGETEMPLATEID.Equals(XMC_Page_Template_Constants.General2))
                                        {
                                            await UpdateGeneralHeaderTitle(matchedMapping, headlineField.Value);
                                            continue;
                                        }
                                    }
                                    var path = $"{matchedMapping.NEWURLPATH}/Data/{datasource.Name}";
                                    var existingItem = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, path);
                                    if (existingItem == null)
                                    {
                                        var inputItem = XMCItemUtility.GetSitecoreCreateItemInput(datasource.Name,
                                    XMC_Template_Constants.RTE, localDataItemId, "text", headlineField.Value);
                                        items.Add(inputItem);
                                    }
                                }
                            }
                            counter++;
                        }
                        if (items.Any())
                            await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(items, _environment, _accessToken, 10);
                    }
                }
            }
        }

        public static List<RenderingInfo> GetRenderingsForCurrentDevice(Item item)
        {
            var results = new List<RenderingInfo>();

            var renderings = item.Visualization.GetRenderings(Sitecore.Context.Device, false);
            if (renderings == null)
                return results;

            foreach (var renderingReference in renderings)
            {
                if (renderingReference.RenderingItem == null)
                    continue; // Skip broken rendering

                results.Add(new RenderingInfo
                {
                    RenderingName = renderingReference.RenderingItem.Name,
                    RenderingId = renderingReference.RenderingID.ToString(),
                    Placeholder = renderingReference.Placeholder,
                    DatasourceID = renderingReference.Settings?.DataSource,
                    Parameters = renderingReference.Settings?.Parameters,
                    DeviceId = Sitecore.Context.Device.ID.ToString()
                });
            }

            return results;
        }

        private async Task UpdateGeneralHeaderTitle(PageMapping matchedMapping, string value)
        {
            var generalHeaderpath = $"{matchedMapping.NEWURLPATH}/Data/General Header";
            var generalHeaderItem = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, generalHeaderpath);
            if (generalHeaderItem != null)
            {
                var updateItemInput = new SitecoreUpdateItemInput()
                {
                    ItemId = generalHeaderItem.ItemId,
                    Fields = new List<SitecoreFieldInput>()
                    {
                        new SitecoreFieldInput()
                        {
                            Name = "title",
                            Value = value
                        }
                    },
                    Language = "en"
                };
                await SitecoreGraphQLClient.UpdateBulkItemsBatchedAsync(new List<SitecoreUpdateItemInput>() { updateItemInput }, _environment, _accessToken, 10);
            }
        }


        private async Task<string> CreateItem(string targetItemId, string itemName, string templateId, List<SitecoreFieldInput> fields = null)
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
            var createdItem = await SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(new List<SitecoreCreateItemInput>() { createDataItemInput }, _environment, _accessToken, 10);
            if (createdItem != null)
                createdItemId = createdItem.FirstOrDefault().ItemId;
            return createdItemId;
        }

        private static List<PageMapping> SortPages(List<PageMapping> pages)
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

        private async Task<bool> SyncPageFields(PageDataModel sourcePageItem, PageMapping pageMapping, SitecoreItem targetItem,
            List<RenderingInfo> headlineRenderings, List<TemplateFieldMapping> mappingSelections)
        {
            Sitecore.Diagnostics.Log.Info($"Page context fields syncing started for {sourcePageItem.Page}|{pageMapping.NEWURLPATH}", this);

            var fields = new List<SitecoreFieldInput>() { };
            
            var pageMetaTitle = GetSitecoreFieldInput(sourcePageItem, "PageMetaTitle", "pageMetaTitle");
            if (pageMetaTitle != null)
                fields.Add(pageMetaTitle);
            
            var metaDescription = GetSitecoreFieldInput(sourcePageItem, "MetaDescription", "metaDescription");
            if (metaDescription != null)
                fields.Add(metaDescription);
            
            var metaKeywords = GetSitecoreFieldInput(sourcePageItem, "MetaKeywords", "metaKeywords");
            if (metaKeywords != null)
                fields.Add(metaKeywords);


            var title = GetSitecoreFieldInput(sourcePageItem, "DisplayTitle", "Title");            
            var pageHeadLine = GetPageHeadline(sourcePageItem, headlineRenderings);
            if (pageMapping.PAGETEMPLATEID.Equals(XMC_Page_Template_Constants.Condition_Treatment))
                title.Value = pageHeadLine;
            if (title != null)
                fields.Add(title);

            var displayDescription = GetSitecoreFieldInput(sourcePageItem, "DisplayDescription", "Description");
            if (displayDescription != null)
                fields.Add(displayDescription);
            
            var displayContent = GetSitecoreFieldInput(sourcePageItem, "DisplayContent", "content");
            if (displayContent != null)
                fields.Add(displayContent);

            if(mappingSelections != null && mappingSelections.Any())
            {                
                var mappingSelection = mappingSelections.FirstOrDefault(x => sourcePageItem.TemplateID.Equals(ID.Parse(x.TemplateId)));
                if(mappingSelection != null && mappingSelection.FieldMappings != null && mappingSelection.FieldMappings.Any())
                {
                    Sitecore.Diagnostics.Log.Info($"Page context mapping selections found: {JsonConvert.SerializeObject(mappingSelection)}", this);
                    foreach ( var fieldMapping in mappingSelection.FieldMappings)
                    {
                        if (!string.IsNullOrEmpty(fieldMapping.XMField) && !string.IsNullOrEmpty(fieldMapping.XPField))
                        {
                            Sitecore.Diagnostics.Log.Info($"Page context mapping field processing: {fieldMapping.XMField}/{fieldMapping.XPField}", this);
                            if(!fields.Any(field => field.Name.Equals(fieldMapping.XMField, StringComparison.OrdinalIgnoreCase)))
                            {
                                var field = GetSitecoreFieldInput(sourcePageItem, fieldMapping.XPField, fieldMapping.XMField);
                                if (field != null)
                                    fields.Add(field);
                            }                            
                        }                        
                    }
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
                var result = await SitecoreGraphQLClient.UpdateBulkItemsBatchedAsync(new List<SitecoreUpdateItemInput>() { updateItemInput }, _environment, _accessToken, 10);                
                Sitecore.Diagnostics.Log.Info($"Page context fields syncing completed for {sourcePageItem.Page}|{pageMapping.NEWURLPATH}", this);
                return result;
            }
            

            return false;
        }

        private List<SitecoreFieldInput> GetLocationPageFields(PageDataModel sourcePageItem)
        {
            var fields = new List<SitecoreFieldInput>() { };

            var mobileVisiblePhoneNumber = GetSitecoreFieldInput(sourcePageItem, "Mobile Visible Phone Number", "mobileVisiblePhoneNumber");
            if (mobileVisiblePhoneNumber != null)
                fields.Add(mobileVisiblePhoneNumber);
            var locationID = GetSitecoreFieldInput(sourcePageItem, "Location ID", "locationID");
            if (locationID != null)
                fields.Add(locationID);
            var locationName = GetSitecoreFieldInput(sourcePageItem, "Location Name", "locationName");
            if (locationName != null)
                fields.Add(locationName);
            var address1 = GetSitecoreFieldInput(sourcePageItem, "Address 1", "address1");
            if (address1 != null)
                fields.Add(address1);
            var address2 = GetSitecoreFieldInput(sourcePageItem, "Address 2", "address2");
            if (address2 != null)
                fields.Add(address2);
            var alternateCity = GetSitecoreFieldInput(sourcePageItem, "Alternate City", "alternateCity");
            if (alternateCity != null)
                fields.Add(alternateCity);
            var city = GetSitecoreFieldInput(sourcePageItem, "City", "city");
            if (city != null)
                fields.Add(city);
            //var state = GetSitecoreFieldInput(sourcePageItem, "State", "state");
            //if (state != null)
            //    fields.Add(state);
            var zip = GetSitecoreFieldInput(sourcePageItem, "Zip", "zip");
            if (zip != null)
                fields.Add(zip);
            //var website = GetSitecoreFieldInput(sourcePageItem, "Website", "website");
            //if (website != null)
            //    fields.Add(website);
            var emailAddress = GetSitecoreFieldInput(sourcePageItem, "Email Address", "emailAddress");
            if (emailAddress != null)
                fields.Add(emailAddress);
            //var acceptingNewPatients = GetSitecoreFieldInput(sourcePageItem, "Accepting New Patients", "acceptingNewPatients");
            //if (acceptingNewPatients != null)
            //    fields.Add(acceptingNewPatients);
            var officeHours = GetSitecoreFieldInput(sourcePageItem, "Office Hours", "officeHours");
            if (officeHours != null)
                fields.Add(officeHours);
            //var department = GetSitecoreFieldInput(sourcePageItem, "Department", "department");
            //if (department != null)
            //    fields.Add(department);
            //var addressMapURL = GetSitecoreFieldInput(sourcePageItem, "Address Map URL", "addressMapURL");
            //if (addressMapURL != null)
            //    fields.Add(addressMapURL);
            var showInLocationListings = GetSitecoreFieldInput(sourcePageItem, "Show in Location Listings", "showInLocationListings");
            if (showInLocationListings != null)
                fields.Add(showInLocationListings);
            var displayPhysiciansAtThisLocation = GetSitecoreFieldInput(sourcePageItem, "Display Physicians At This Location", "displayPhysiciansAtThisLocation");
            if (displayPhysiciansAtThisLocation != null)
                fields.Add(displayPhysiciansAtThisLocation);
            var displayOpenScheduling = GetSitecoreFieldInput(sourcePageItem, "Display Open Scheduling", "displayOpenScheduling");
            if (displayOpenScheduling != null)
                fields.Add(displayOpenScheduling);
            var openScheduleIntro = GetSitecoreFieldInput(sourcePageItem, "Open Schedule Intro", "openScheduleIntro");
            if (openScheduleIntro != null)
                fields.Add(openScheduleIntro);
            var epicDeptID = GetSitecoreFieldInput(sourcePageItem, "Epic Dept ID", "epicDeptID");
            if (epicDeptID != null)
                fields.Add(epicDeptID);
            var npiNumber = GetSitecoreFieldInput(sourcePageItem, "NPI Number", "npiNumber");
            if (npiNumber != null)
                fields.Add(npiNumber);
            var ratingAndReviewDataId = GetSitecoreFieldInput(sourcePageItem, "RatingAndReview Data Id", "ratingAndReviewDataId");
            if (ratingAndReviewDataId != null)
                fields.Add(ratingAndReviewDataId);
            return fields;
        }

        private async Task EnsureSitecorePathExistsAsync(
    string targetPath,
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
                    var parentItem = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, parentPath);

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
                var existingSegmentItem = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, segmentPath);

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

                    var createdItems = await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(
                        new List<SitecoreCreateItemInput> { createItemInput },
                        _environment,
                        _accessToken,
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
                    Value = fieldValue,
                };
            }
            return null;
        }

        private SitecoreFieldInput GetSitecoreFieldInput(Item sourcePageItem, string xpFieldName, string xmcFieldName)
        {
            var fieldValue = sourcePageItem.Fields.FirstOrDefault(x => x.Name == xpFieldName && !string.IsNullOrEmpty(x.Value))?.Value ?? string.Empty;
            if (!string.IsNullOrEmpty(fieldValue))
            {
                return new SitecoreFieldInput()
                {
                    Name = xmcFieldName,
                    Value = fieldValue,
                };
            }
            return null;
        }

        private static PageDataModel RemoveRenderingDatasources(PageDataModel sourcePageItem, IEnumerable<RenderingInfo> renderings, string renderingName)
        {
            if (renderings == null || !renderings.Any()) return sourcePageItem;
            
            var filteredRenderings = renderings.Where(rendering => rendering.RenderingName.Contains(renderingName));

            if (filteredRenderings == null || !filteredRenderings.Any()) return sourcePageItem;            

            sourcePageItem.DataSources.RemoveAll(x => filteredRenderings.Any(y => x.ID.Equals(y.DatasourceID, StringComparison.OrdinalIgnoreCase)));            

            return sourcePageItem;
        }
        private static bool IsGuidInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;

            return Guid.TryParse(input, out _);
        }
    }
}
