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
using Sitecore.Install.Framework;
using Sitecore.Resources;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
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
        List<PageMapping> _allPageMappings = new List<PageMapping>();
        List<XPSpecialtyItem> _xpSpecialtyLookUps = new List<XPSpecialtyItem>();
        QueryItemsResult<SitecoreItemBase> _xmcSpecialtyLookUpsResult = new QueryItemsResult<SitecoreItemBase>();


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
        public async Task<ActionResult> SyncContent(
            string itemPath,
            string xmcItemPath = "",
            string language = "en",
            string workflowState = "Draft",
            bool createPages = false,
            bool syncComponents = false,
            bool syncGlobalDatasources = false,
            string datasourceType = "",
            List<TemplateFieldMapping> mappingSelections = null,
            string environment = "DEV")
        {
            List<SyncContentResponse> syncResults = new List<SyncContentResponse>();

            var model = new ContentMigrationModel
            {
                ItemPath = itemPath,
                Environment = environment
            };

            _environment = environment;

            var authResponse = await AuthHelper.GetAuthTokenAsync(_environment);

            if (authResponse != null && !string.IsNullOrEmpty(authResponse.AccessToken))
            {
                _accessToken = authResponse.AccessToken;

                var database = Sitecore.Context.Database;
                var rootItem = SitecoreUtility.IsGuidInput(itemPath) ? database.GetItem(ID.Parse(itemPath)) : database.GetItem(itemPath);
                if (rootItem != null)
                {
                    Sitecore.Diagnostics.Log.Info($"rootItem: {rootItem.ID.ToString()}", this);
                    var items = rootItem.Axes
                    .GetDescendants()
                            .Where(descendant => syncGlobalDatasources ? true : descendant.Template.BaseTemplates.Any(t => t.ID.ToString() == XP_Page_Template_Constants.XP_BASE_PAGE_TEMPLATEID))
                            .ToList();
                    items.Insert(0, rootItem); // include root item itself
                    var pageMappingItems = PageMappingUtility.LoadPageMappingsFromJson();
                    pageMappingItems = PageMappingUtility.SortPages(pageMappingItems);

                    if (syncGlobalDatasources)
                    {
                        Sitecore.Diagnostics.Log.Info($"syncGlobalDatasources: {syncGlobalDatasources}", this);
                        await SyncBlogData();
                        return Json(syncResults);
                    }

                    model.Items = SitecoreUtility.GetPagesAndRelatedDataFromXP(items, database);
                    if (model.Items != null && model.Items.Any())
                    {
                        if (pageMappingItems != null && pageMappingItems.Any())
                        {
                            if (!string.IsNullOrEmpty(xmcItemPath) && mappingSelections != null && mappingSelections.Any())
                            {
                                Sitecore.Diagnostics.Log.Info("Processing mapping selections", this);
                                foreach (var item in model.Items)
                                {
                                    var mappingSelection = mappingSelections.FirstOrDefault(x => item.TemplateID.Equals(ID.Parse(x.TemplateId)));
                                    if (mappingSelection != null)
                                    {
                                        Sitecore.Diagnostics.Log.Info($"Mapping found for {item.Page}/{mappingSelection.XMTemplateId}", this);
                                        string itemRelativePath = item.Page.Replace(itemPath, "");
                                        pageMappingItems.Add(new PageMapping()
                                        {
                                            CURRENTURL = item.Page,
                                            NEWURLPATH = $"{xmcItemPath}{itemRelativePath}",
                                            PAGETEMPLATEID = mappingSelection.XMTemplateId
                                        });
                                    }
                                }
                            }
                            syncResults = await SyncPagesInBatches(model.Items, pageMappingItems, mappingSelections, createPages, syncComponents);
                        }
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

            return Json(syncResults);
        }

        private async Task<List<SyncContentResponse>> SyncPagesInBatches(List<PageDataModel> pageDataItems,
            List<PageMapping> pageMappingItems, List<TemplateFieldMapping> mappingSelections, bool createPages = false, bool syncComponents = false)
        {
            const int batchSize = 5;
            int total = pageDataItems.Count;
            int batches = (int)Math.Ceiling(total / (double)batchSize);

            var database = Sitecore.Context.Database;
            var syncResults = new List<SyncContentResponse>();

            _xpSpecialtyLookUps = GetSpecialtyLookUpsFromXP();
            _xmcSpecialtyLookUpsResult = await GetSpecialtyLookUpsFromXMC();

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
                        _allPageMappings = pageMappingItems;
                        var matchedMapping = _allPageMappings
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
                                    await EnsureSitecorePathExistsAsync(matchedMapping.NEWURLPATH);
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

                                            var generalHeaderItemId = string.Empty;

                                            if (XMC_Page_Template_Constants.General_Header_Templates.Contains(xmcTemplateId))
                                            {
                                                generalHeaderItemId = await TeachingSheetMigrationService.GetOrCreateGeneralHeaderItemAsync(matchedMapping.NEWURLPATH, dataItemId, _environment, _accessToken);
                                            }

                                            //Page Specific Data Migration
                                            if (XMC_Page_Template_Constants.Side_Nav_Templates.Contains(xmcTemplateId))
                                            {
                                                await SideNavMigrationService.ProcessAsync(rteRenderings, sourcePageItem, dataItemId, matchedMapping.NEWURLPATH, _environment, _accessToken);
                                                sourcePageItem = SitecoreUtility.RemoveRenderingDatasources(sourcePageItem, sourcePageItem.Renderings, XP_RenderingName_Constants.RichText);
                                            }

                                            if (xmcTemplateId.Equals(XMC_Page_Template_Constants.Teaching_Sheets))
                                            {
                                                await TeachingSheetMigrationService.ProcessAsync(rteRenderings, sourcePageItem, dataItemId, matchedMapping.NEWURLPATH, _environment, _accessToken, generalHeaderItemId);
                                                sourcePageItem = SitecoreUtility.RemoveRenderingDatasources(sourcePageItem, sourcePageItem.Renderings, XP_RenderingName_Constants.RichText);
                                                sourcePageItem = SitecoreUtility.RemoveRenderingDatasources(sourcePageItem, sourcePageItem.Renderings, XP_RenderingName_Constants.Multi_Button_Callout);
                                            }

                                            if (XMC_Page_Template_Constants.Location_Pages.Contains(xmcTemplateId))
                                            {
                                                var pageItem = database.GetItem(sourcePageItem.ItemID);

                                                if (pageItem.HasChildren)
                                                {
                                                    var localDatasourceItems = pageItem.Axes.GetDescendants()
                                                    .Where(descendant =>
                                                        descendant.TemplateID.ToString().Equals(Constants.OFFICE_HOURS_Details_FOLDER_TEMPLATEID, StringComparison.OrdinalIgnoreCase) ||
                                                        descendant.Parent?.TemplateID.ToString().Equals(Constants.OFFICE_HOURS_Details_FOLDER_TEMPLATEID, StringComparison.OrdinalIgnoreCase) == true)?.ToArray();

                                                    var officeHoursRootItemId = string.Empty;
                                                    var officeHoursRootFolder = await SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, matchedMapping.NEWURLPATH + $"/Data/Office Hours Folder");
                                                    if (officeHoursRootFolder == null)
                                                    {
                                                        officeHoursRootItemId = await CreateItem(dataItemId, "Office Hours Folder", XMC_Template_Constants.OfficeHoursFolder);
                                                    }

                                                    //Office Hours
                                                    await CreateOfficeHoursDatasources(localDatasourceItems, matchedMapping.NEWURLPATH, officeHoursRootItemId, "Office Hours", Constants.OFFICE_HOURS_Details_FOLDER_TEMPLATEID);
                                                    //Phone Hours
                                                    await CreateOfficeHoursDatasources(localDatasourceItems, matchedMapping.NEWURLPATH, officeHoursRootItemId, "Phone Hours", Constants.OFFICE_HOURS_Details_FOLDER_TEMPLATEID);
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
            return syncResults;
        }

        private async Task SyncBlogData()
        {
            var xpBlogResources = GetBlogTagResourcesFromXP();
            foreach (var xpBlogResource in xpBlogResources)
            {
                Sitecore.Diagnostics.Log.Info($"xpBlogResource.ItemName: {xpBlogResource.ItemName}", this);
                var tagItem = await SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, $"{XMC_Datasource_Constants.StoryTags}/{xpBlogResource.ItemName}");
                if (tagItem != null)
                {
                    Sitecore.Diagnostics.Log.Info($"tagItem.Name: {tagItem.Name}", this);
                    var resourcesItem = await SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, $"{XMC_Datasource_Constants.StoryTags}/{xpBlogResource.ItemName}/Resources");
                    if (resourcesItem == null)
                    {
                        var resourcesItemId = await CreateItem(tagItem.ItemId, "Resources", XMC_Template_Constants.TextLinkList);
                        if (!string.IsNullOrEmpty(resourcesItemId))
                        {
                            Sitecore.Diagnostics.Log.Info($"resourcesItemId: {resourcesItemId}", this);
                            var linksItem = await SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, $"{XMC_Datasource_Constants.StoryTags}/{xpBlogResource.ItemName}/Resources/Links");
                            if (linksItem == null)
                            {
                                var fields = new List<SitecoreFieldInput>()
                                        {
                                            new SitecoreFieldInput()
                                            {
                                                Name = "textArea",
                                                Value = xpBlogResource.Description
                                            }
                                        };
                                var linksItemId = await CreateItem(resourcesItemId, "Links", XMC_Template_Constants.LinkGroup, fields);
                                if (!string.IsNullOrEmpty(linksItemId))
                                {
                                    Sitecore.Diagnostics.Log.Info($"linksItemId: {linksItemId}", this);
                                    var resourcesUpdate = new SitecoreUpdateItemInput()
                                    {
                                        ItemId = resourcesItemId,
                                        Fields = new List<SitecoreFieldInput>()
                                                {
                                                    new SitecoreFieldInput()
                                                    {
                                                        Name = "heading",
                                                        Value = "Children's Wisconsin Resources"
                                                    },
                                                    new SitecoreFieldInput()
                                                    {
                                                        Name = "column1",
                                                        Value = SitecoreUtility.FormatGuid(linksItemId)
                                                    }

                                                },
                                        Language = "en"
                                    };
                                    var tagUpdate = new SitecoreUpdateItemInput()
                                    {
                                        ItemId = tagItem.ItemId,
                                        Fields = new List<SitecoreFieldInput>()
                                                {
                                                    new SitecoreFieldInput()
                                                    {
                                                        Name = "resources",
                                                        Value = SitecoreUtility.FormatGuid(resourcesItemId)
                                                    }
                                                },
                                        Language = "en"
                                    };
                                    await SitecoreGraphQLClient.UpdateBulkItemsBatchedAsync(new List<SitecoreUpdateItemInput>() { resourcesUpdate }, _environment, _accessToken, 10);
                                    await SitecoreGraphQLClient.UpdateBulkItemsBatchedAsync(new List<SitecoreUpdateItemInput>() { tagUpdate }, _environment, _accessToken, 10);
                                }
                            }
                        }
                    }
                }
            }
        }

        private async Task CreateOfficeHoursDatasources(Item[] items, string newUrlPath, string dataItemId, string folderName, string templateId)
        {
            var officeHoursPath = $"/Data/Office Hours Folder/{folderName}";
            var officeHoursFolderItem = items.FirstOrDefault(x => x.TemplateID == ID.Parse(templateId));
            if (officeHoursFolderItem != null)
            {
                if (!officeHoursFolderItem.HasChildren)
                    return;
                var createdItemId = string.Empty;
                var xmcOfficeHoursFolder = await SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, newUrlPath + officeHoursPath);
                if (xmcOfficeHoursFolder == null)
                {
                    createdItemId = await CreateItem(dataItemId, folderName, officeHoursFolderItem.TemplateID.ToString());
                }
                else
                    createdItemId = xmcOfficeHoursFolder.ItemId;
                if (!string.IsNullOrEmpty(createdItemId))
                {
                    var officeHoursItems = items.Where(x => x.Parent.TemplateID == ID.Parse(templateId));
                    if (officeHoursItems != null && officeHoursItems.Any())
                    {
                        var dayListIds = new List<string>();
                        foreach (var item in officeHoursItems)
                        {
                            var fields = new List<SitecoreFieldInput>();
                            var officeHourType = SitecoreUtility.GetSitecoreFieldInput(item, "Office Hour Type", "officeHourType");
                            if (officeHourType != null)
                                fields.Add(officeHourType);
                            var open = SitecoreUtility.GetSitecoreFieldInput(item, "Open", "open");
                            if (open != null)
                                fields.Add(open);
                            var close = SitecoreUtility.GetSitecoreFieldInput(item, "Close", "close");
                            if (close != null)
                                fields.Add(close);
                            var xmcOfficeHours = await SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, newUrlPath + $"{officeHoursPath}/{item.Name}");
                            if (xmcOfficeHours == null)
                            {
                                var itemCreated = await CreateItem(createdItemId, item.Name, item.TemplateID.ToString(), fields);
                                if (!string.IsNullOrEmpty(itemCreated))
                                {
                                    if (!open?.Value?.ToString()?.Equals("x") == true && !close?.Value?.ToString()?.Equals("x") == true)
                                        dayListIds.Add(itemCreated);
                                }
                            }
                            else
                            {
                                if (!open?.Value?.ToString()?.Equals("x") == true && !close?.Value?.ToString()?.Equals("x") == true)
                                    dayListIds.Add(xmcOfficeHours.ItemId);
                            }
                        }
                        if (dayListIds.Any())
                        {
                            var fields = new List<SitecoreFieldInput>();
                            fields.Add(new SitecoreFieldInput()
                            {
                                Name = "availableHours",
                                Value = string.Join("|", dayListIds.Select(x => SitecoreUtility.FormatGuid(x))),
                            });
                            fields.Add(new SitecoreFieldInput()
                            {
                                Name = "heading",
                                Value = folderName,
                            });
                            var updateItemInput = new SitecoreUpdateItemInput()
                            {
                                ItemId = createdItemId,
                                Fields = fields,
                                Language = "en"
                            };
                            await SitecoreGraphQLClient.UpdateBulkItemsBatchedAsync(new List<SitecoreUpdateItemInput>() { updateItemInput }, _environment, _accessToken, 10);
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
                                var inputItem = SitecoreUtility.GetSitecoreCreateItemInput(itemName,
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
                var title = SitecoreUtility.GetSitecoreFieldInput(datasource, "Title", "title");
                if (title != null)
                    fields.Add(title);

                var richTextAbove = datasource.Fields.FirstOrDefault(x => x.Name == "RichTextAbove" && !string.IsNullOrEmpty(x.Value))?.Value ?? string.Empty;
                var richTextBelow = datasource.Fields.FirstOrDefault(x => x.Name == "RichTextBelow" && !string.IsNullOrEmpty(x.Value))?.Value ?? string.Empty;


                var richText = $"{richTextAbove}";

                if (!string.IsNullOrEmpty(richText) && !string.IsNullOrEmpty(richTextBelow))
                    richText += $"<br/><br/>{richTextBelow}";
                else
                    richText += richTextBelow;

                if (string.IsNullOrEmpty(richText))
                    richText = title.Value?.ToString() ?? string.Empty;

                if (!string.IsNullOrEmpty(richText))
                {
                    var field = new SitecoreFieldInput()
                    {
                        Name = "description",
                        Value = richText,
                    };
                    fields.Add(field);
                }

                var videoID = SitecoreUtility.GetSitecoreFieldInput(datasource, "VideoId", "videoID");
                if (videoID != null)
                    fields.Add(videoID);
                var image = SitecoreUtility.GetSitecoreFieldInput(datasource, "Thumbnail", "image");
                if (image != null)
                    fields.Add(image);
                var path = $"{pageMapping.NEWURLPATH}/Data/{datasource.Name}";
                var existingItem = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, path);
                if (existingItem == null)
                    await CreateItem(localDataItemId, datasource.Name, XMC_Template_Constants.Text_Media, fields);
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
                                        var inputItem = SitecoreUtility.GetSitecoreCreateItemInput(datasource.Name,
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

        private async Task<bool> SyncPageFields(PageDataModel sourcePageItem, PageMapping pageMapping, SitecoreItem targetItem,
            List<RenderingInfo> headlineRenderings, List<TemplateFieldMapping> mappingSelections)
        {
            Sitecore.Diagnostics.Log.Info($"Page context fields syncing started for {sourcePageItem.Page}|{pageMapping.NEWURLPATH}", this);

            var fields = new List<SitecoreFieldInput>() { };

            var pageMetaTitle = await GetSitecoreFieldInput(sourcePageItem, "PageMetaTitle", "pageMetaTitle");
            if (pageMetaTitle != null)
                fields.Add(pageMetaTitle);

            var metaDescription = await GetSitecoreFieldInput(sourcePageItem, "MetaDescription", "metaDescription");
            if (metaDescription != null)
                fields.Add(metaDescription);

            var metaKeywords = await GetSitecoreFieldInput(sourcePageItem, "MetaKeywords", "metaKeywords");
            if (metaKeywords != null)
                fields.Add(metaKeywords);


            var title = await GetSitecoreFieldInput(sourcePageItem, "DisplayTitle", "Title");
            var pageHeadLine = GetPageHeadline(sourcePageItem, headlineRenderings);
            if (pageMapping.PAGETEMPLATEID.Equals(XMC_Page_Template_Constants.Condition_Treatment))
                title.Value = pageHeadLine;
            if (title != null)
                fields.Add(title);

            var displayDescription = await GetSitecoreFieldInput(sourcePageItem, "DisplayDescription", "Description");
            if (displayDescription != null)
                fields.Add(displayDescription);

            var displayContent = await GetSitecoreFieldInput(sourcePageItem, "DisplayContent", "content");
            if (displayContent != null)
                fields.Add(displayContent);

            if (mappingSelections != null && mappingSelections.Any())
            {
                var mappingSelection = mappingSelections.FirstOrDefault(x => sourcePageItem.TemplateID.Equals(ID.Parse(x.TemplateId)));
                if (mappingSelection != null && mappingSelection.FieldMappings != null && mappingSelection.FieldMappings.Any())
                {
                    Sitecore.Diagnostics.Log.Info($"Page context mapping selections found: {JsonConvert.SerializeObject(mappingSelection)}", this);
                    foreach (var fieldMapping in mappingSelection.FieldMappings)
                    {
                        if (!string.IsNullOrEmpty(fieldMapping.XMField) && !string.IsNullOrEmpty(fieldMapping.XPField))
                        {
                            Sitecore.Diagnostics.Log.Info($"Page context mapping field processing: {fieldMapping.XMField}/{fieldMapping.XPField}", this);
                            if (fields.Any(f => f.Name.Equals(fieldMapping.XMField, StringComparison.OrdinalIgnoreCase)))
                                fields.RemoveAll(f => f.Name.Equals(fieldMapping.XMField, StringComparison.OrdinalIgnoreCase));
                            var field = await GetSitecoreFieldInput(sourcePageItem, fieldMapping.XPField, fieldMapping.XMField,
                                _xpSpecialtyLookUps, _xmcSpecialtyLookUpsResult);
                            if (field != null)
                                fields.Add(field);
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

        private async Task EnsureSitecorePathExistsAsync(string targetPath)
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
                var segmentMapping = _allPageMappings.FirstOrDefault(x =>
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

        private async Task<SitecoreFieldInput> GetSitecoreFieldInput(PageDataModel sourcePageItem, string xpFieldName, string xmcFieldName,
            List<XPSpecialtyItem> xpSpecialtyLookUps = null, QueryItemsResult<SitecoreItemBase> xmcSpecialtyLookUpsResult = null)
        {
            var field = sourcePageItem.Fields.FirstOrDefault(x => x.Name == xpFieldName && !string.IsNullOrEmpty(x.Value));

            if (field == null)
                return null;

            string fieldValue = field.Value;
            string fieldType = field.Type?.ToLowerInvariant();

            if (string.IsNullOrEmpty(fieldValue))
                return null;

            SitecoreFieldInput fieldInput = null;

            switch (fieldType)
            {
                case "treelist":
                case "treelistex":
                    {
                        MultilistField treelistField = field.Field;
                        if (treelistField != null)
                        {
                            Item[] selectedItems = treelistField.GetItems();
                            switch (xpFieldName)
                            {
                                case "Specialties":
                                    {
                                        var xpPageSpecialties = selectedItems?.Select(x => x.Name)?.ToList();
                                        var currentPageSpecialties = await GetCurrentPageSpecialties(xpPageSpecialties, xmcSpecialtyLookUpsResult, xpSpecialtyLookUps);
                                        if (currentPageSpecialties != null)
                                        {
                                            fieldInput = new SitecoreFieldInput()
                                            {
                                                Name = xmcFieldName,
                                                Value = string.Join("|", currentPageSpecialties.Select(x => SitecoreUtility.FormatGuid(x)))
                                            };
                                        }
                                    }
                                    break;
                                case "Locations":
                                    {
                                        var xpPageLocations = selectedItems?.Select(x => x.Paths.FullPath).Where(path => !string.IsNullOrEmpty(path))?.ToList();
                                        if (xpPageLocations != null && xpPageLocations.Any())
                                        {
                                            var matchingLocations = _allPageMappings.Where(mapping => !string.IsNullOrEmpty(mapping.CURRENTURL) &&
                                            xpPageLocations.Any(loc => string.Equals(loc, mapping.CURRENTURL, StringComparison.OrdinalIgnoreCase)));
                                            if (matchingLocations != null && matchingLocations.Any())
                                            {
                                                List<string> locationItemIds = new List<string>();
                                                foreach (var matchingLocation in matchingLocations)
                                                {
                                                    var locationItem = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, matchingLocation.NEWURLPATH);
                                                    if (locationItem != null)
                                                        locationItemIds.Add(SitecoreUtility.FormatGuid(locationItem.ItemId));
                                                }
                                                if (locationItemIds.Any())
                                                {
                                                    fieldInput = new SitecoreFieldInput()
                                                    {
                                                        Name = xmcFieldName,
                                                        Value = string.Join("|", locationItemIds)
                                                    };
                                                }
                                            }
                                        }
                                    }
                                    break;
                            }
                        }
                    }
                    break;
                default:
                    {
                        fieldInput = new SitecoreFieldInput()
                        {
                            Name = xmcFieldName,
                            Value = fieldValue,
                        };
                    }
                    break;
            }

            return fieldInput;
        }

        private async Task<List<string>> GetCurrentPageSpecialties(List<string> xpPageSpecialties,
            QueryItemsResult<SitecoreItemBase> xmcSpecialtyLookUpsResult,
            List<XPSpecialtyItem> xpSpecialtyLookUps)
        {
            if (xpPageSpecialties != null && xpPageSpecialties.Any())
            {
                if (xpSpecialtyLookUps != null && xpSpecialtyLookUps.Any())
                {
                    if (xmcSpecialtyLookUpsResult != null && xmcSpecialtyLookUpsResult.Items != null && xmcSpecialtyLookUpsResult.Items.Any())
                    {
                        var xmcSpecialtyLookUps = xmcSpecialtyLookUpsResult.Items;
                        foreach (var item in xpSpecialtyLookUps)
                        {
                            if (!xmcSpecialtyLookUps.Any(s => s.ItemName.Equals(item.ItemName, StringComparison.OrdinalIgnoreCase)))
                            {
                                var itemId = await CreateItem(xmcSpecialtyLookUpsResult.ItemId, item.ItemName, XMC_Template_Constants.Specialty, item.Fields);
                                xmcSpecialtyLookUps.Add(new SitecoreItemBase()
                                {
                                    ItemId = itemId,
                                    ItemName = item.ItemName,
                                    Path = $"{XMC_Datasource_Constants.Specialties}/{item.ItemName}",
                                });
                            }
                        }
                        return xmcSpecialtyLookUps.Where(x => xpPageSpecialties.Contains(x.ItemName, StringComparer.OrdinalIgnoreCase))?.Select(x => x.ItemId)?.ToList();
                    }
                }
            }
            return null;
        }

        private async Task<QueryItemsResult<SitecoreItemBase>> GetSpecialtyLookUpsFromXMC()
        {
            var specialties = await this.SitecoreGraphQLClient.QueryItemsAsync<SitecoreItemBase>(
                _environment,
                _accessToken,
                XMC_Datasource_Constants.Specialties, // e.g., "/sitecore/content/CW/childrens/Specialties"
                new List<string>(), // No extra fields for now
                jObj => new SitecoreItemBase
                {
                    ItemId = jObj["itemId"]?.ToString(),
                    Path = jObj["path"]?.ToString(),
                    ItemName = jObj["itemName"]?.ToString()
                }
            );
            return specialties;
        }

        private List<XPBlogResourceItem> GetBlogTagResourcesFromXP()
        {
            Sitecore.Diagnostics.Log.Info("GetBlogTagResourcesFromXP", this);
            var xpStoryTags = new List<XPBlogResourceItem>();

            var storyTagsRoot = Sitecore.Context.Database.GetItem(XP_Datasource_Constants.StoryTags);

            Sitecore.Diagnostics.Log.Info($"storyTagsRoot: {storyTagsRoot?.ID?.ToString()}", this);

            var storyTags = storyTagsRoot.GetChildren().Where(i => i.HasChildren);

            foreach (var storyTag in storyTags)
            {
                Sitecore.Diagnostics.Log.Info($"storyTag: {storyTag?.ID?.ToString()}", this);
                var resourcesRootItem = storyTag.GetChildren()
                    .FirstOrDefault(i => i.Name.Equals("Resources", StringComparison.OrdinalIgnoreCase));

                if (resourcesRootItem == null || !resourcesRootItem.HasChildren)
                    continue;

                var resources = resourcesRootItem.GetChildren().Where(i => !string.IsNullOrEmpty(i.Fields["Title"]?.Value));

                if (resources == null || !resources.Any())
                    return xpStoryTags;

                var resourceLinks = new List<string>();

                foreach (var resource in resources)
                {
                    Sitecore.Diagnostics.Log.Info($"resource: {resource?.ID?.ToString()}", this);
                    var title = resource.Fields["Title"].Value;
                    resourceLinks.Add($@"<li><a href=""/"">{title}</a></li>");
                }

                if (resourceLinks == null || !resourceLinks.Any())
                    continue;

                xpStoryTags.Add(new XPBlogResourceItem()
                {
                    Path = storyTag.Paths.FullPath,
                    ItemName = storyTag.Name,
                    ItemId = storyTag.ID.ToString(),
                    Description = $@"<ul class=""blog-related-resources"">{string.Join("", resourceLinks)}</ul>"
                });
            }
            Sitecore.Diagnostics.Log.Info($"xpStoryTags: {(xpStoryTags != null ? JsonConvert.SerializeObject(xpStoryTags) : "")}", this);
            return xpStoryTags;
        }


        private List<XPSpecialtyItem> GetSpecialtyLookUpsFromXP()
        {
            var specialtyRootItem = Sitecore.Context.Database.GetItem(XP_Datasource_Constants.Specialties);
            if (specialtyRootItem != null)
            {
                var specialtyItems = specialtyRootItem.Axes.GetDescendants();
                if (specialtyItems != null)
                {
                    return specialtyItems.Select(i =>
                    new XPSpecialtyItem()
                    {
                        ItemId = i.ID.ToString(),
                        ItemName = i.Name,
                        Path = i.Paths.FullPath,
                        Fields = new List<SitecoreFieldInput>()
                        {
                            new SitecoreFieldInput()
                            {
                                Name = "specialtyCode",
                                Value = i.Fields["Specialty Code"].Value,
                            },
                            new SitecoreFieldInput()
                            {
                                Name = "specialtyLabelText",
                                Value = i.Fields["Specialty Name"].Value,
                            }
                        }
                    }).ToList();
                }
            }
            return null;
        }
    }
}
