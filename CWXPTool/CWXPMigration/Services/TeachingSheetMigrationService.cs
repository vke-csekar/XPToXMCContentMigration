using CWXPMigration.Models;
using JSNLog.Infrastructure;
using Newtonsoft.Json.Linq;
using Sitecore.Mvc.Names;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace CWXPMigration.Services
{
    public interface ITeachingSheetMigrationService
    {
        /// <summary>
        /// Processes Rich Text Renderings and creates/upgrades the Side Navigation links in Sitecore.
        /// </summary>
        Task ProcessAsync(
            IEnumerable<RenderingInfo> rteRenderings,            
            PageDataModel sourcePageItem,
            string dataItemId,
            string newUrlPath,
            string environment,
            string accessToken,
            string generalHeaderItemId);
        Task<string> GetOrCreateGeneralHeaderItemAsync(string newUrlPath, string dataItemId, string environment, string accessToken);
    }

    public class TeachingSheetMigrationService : BaseMigrationService, ITeachingSheetMigrationService
    {        
        private string _environment;
        private string _accessToken;

        public TeachingSheetMigrationService(ISitecoreGraphQLClient sitecoreGraphQLClient)
            : base(sitecoreGraphQLClient)
        {            
        }

        /// <inheritdoc />
        public async Task ProcessAsync(
            IEnumerable<RenderingInfo> xpRteRenderings,            
            PageDataModel sourcePageItem,
            string dataItemId,
            string newUrlPath,
            string environment,
            string accessToken,
            string generalHeaderItemId)
        {
            _environment = environment;
            _accessToken = accessToken;

            await CreateInPageBannerDatasources(newUrlPath, dataItemId, sourcePageItem);

            List<RichTextSection> jumpLinkSections = new List<RichTextSection>();

            foreach (var xpRteRendering in xpRteRenderings)
            {
                var xpRteDatasource = SitecoreUtility.GetDatasource(sourcePageItem, xpRteRendering.DatasourceID);

                if (xpRteDatasource != null) {
                    var xpRteField = SitecoreUtility.GetRichTextField(xpRteDatasource);
                    if (xpRteField != null)
                    {
                        var path = $"{newUrlPath}/Data/{xpRteDatasource.Name}";
                        var xmcRteDatasource = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, path);
                        var contents = RichTextSplitter.SplitByH2(xpRteField.Value);
                        if (contents != null && contents.Any())
                        {
                            jumpLinkSections.AddRange(contents);
                            var finalHtml = RichTextSplitter.AddIdAttributeToAllH2(xpRteField.Value);
                            if (xmcRteDatasource == null)
                            {
                                var rteInputItem = SitecoreUtility.GetSitecoreCreateItemInput(xpRteDatasource.Name,
                                    XMC_Template_Constants.RTE, dataItemId, "text", finalHtml);

                                await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(
                                    new List<SitecoreCreateItemInput> { rteInputItem }, _environment, _accessToken);
                            }
                        }
                    }
                }                
            }                        

            if (string.IsNullOrEmpty(generalHeaderItemId)) return;

            var fields = new List<SitecoreFieldInput>();

            fields.Add(new SitecoreFieldInput()
            {
                Name = "cta3Variant",
                Value = "JumpLink",
            });            

            if (jumpLinkSections.Any())
            {
                jumpLinkSections = jumpLinkSections.Where(l => !string.IsNullOrEmpty(l.Title))?.ToList();
                if (jumpLinkSections != null && jumpLinkSections.Any())
                {
                    var linkItemIds = await ProcessJumpLinksAsync(jumpLinkSections, newUrlPath, generalHeaderItemId);

                    if (linkItemIds != null || linkItemIds.Any())
                    {
                        fields.Add(new SitecoreFieldInput()
                        {
                            Name = "cta3",
                            Value = string.Join("|", linkItemIds.Select(x => SitecoreUtility.FormatGuid(x))),
                        });
                    }
                }
            }

            var titleField = await CreatePublicationInfoDatasources(newUrlPath, dataItemId, generalHeaderItemId, sourcePageItem);
            if (titleField != null) {
                fields.Add(titleField);
            }

            await UpdateGeneralHeaderAsync(generalHeaderItemId, fields);
        }

        #region Private Helpers

        /// <summary>
        /// Retrieves or creates the General Header item under the specified path.
        /// </summary>
        public async Task<string> GetOrCreateGeneralHeaderItemAsync(string newUrlPath, string dataItemId, string environment, string accessToken)
        {
            _environment = environment;
            _accessToken = accessToken;

            var path = $"{newUrlPath}/Data/General Header";
            var generalHeaderItem = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, path);

            if (generalHeaderItem != null)
            {
                return generalHeaderItem.ItemId;
            }

            var inputItem = new SitecoreCreateItemInput
            {                                
                Language = "en",
                Parent = dataItemId,
                Name = "General Header",
                TemplateId = XMC_Template_Constants.General_Header
            };

            var createdItems = await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(
                new List<SitecoreCreateItemInput> { inputItem }, _environment, _accessToken);

            return createdItems?.FirstOrDefault()?.ItemId;
        }

        /// <summary>
        /// Processes side navigation content and creates Generic Link items in Sitecore.
        /// </summary>
        private async Task<List<string>> ProcessJumpLinksAsync(List<RichTextSection> sideNavContents, string newUrlPath, string generalHeaderItemId)
        {
            var path = $"{newUrlPath}/Data/General Header";
            var linkItemIds = new List<string>();

            for (int i = 0; i < sideNavContents.Count; i++)
            {
                var linkItemName = PathFormatter.FormatItemName(sideNavContents[i].Title);
                var link = BuildAnchorLink(sideNavContents[i].Title, i);

                var existingLinkItem = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, $"{path}/{linkItemName}");
                if (existingLinkItem != null)
                {
                    var updateItem = new SitecoreUpdateItemInput
                    {
                        ItemId = existingLinkItem.ItemId,
                        Fields = new List<SitecoreFieldInput>
                        {
                            new SitecoreFieldInput
                            {
                                Name = "link",
                                Value = link
                            }
                        }
                    };

                    await this.SitecoreGraphQLClient.UpdateBulkItemsBatchedAsync(
                        new List<SitecoreUpdateItemInput> { updateItem }, _environment, _accessToken);
                    linkItemIds.Add(existingLinkItem.ItemId);
                }
                else
                {
                    var newLinkItemId = await CreateGenericLinkItemAsync(linkItemName, link, generalHeaderItemId);
                    if (!string.IsNullOrEmpty(newLinkItemId))
                    {
                        linkItemIds.Add(newLinkItemId);
                    }
                }
            }

            return linkItemIds;
        }

        /// <summary>
        /// Creates a Generic Link item with a specific link field value.
        /// </summary>
        private async Task<string> CreateGenericLinkItemAsync(string itemName, string link, string generalHeaderItemId)
        {
            var inputItem = new SitecoreCreateItemInput
            {
                Name = itemName,
                TemplateId = XMC_Template_Constants.Generic_Link,
                Parent = generalHeaderItemId,
                Language = "en",
                Fields = new List<SitecoreFieldInput>
                {
                    new SitecoreFieldInput
                    {
                        Name = "link",
                        Value = link
                    }
                }
            };

            var createdItems = await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(
                new List<SitecoreCreateItemInput> { inputItem }, _environment, _accessToken);

            return createdItems?.FirstOrDefault()?.ItemId;
        }       

        /// <summary>
        /// Updates the General Header item's CTA3 field with a pipe-separated list of link item IDs.
        /// </summary>
        private async Task UpdateGeneralHeaderAsync(string itemId, List<SitecoreFieldInput> fields)
        {
            var updateItem = new SitecoreUpdateItemInput
            {
                ItemId = itemId,
                Fields = fields
            };

            await this.SitecoreGraphQLClient.UpdateBulkItemsBatchedAsync(
                new List<SitecoreUpdateItemInput> { updateItem }, _environment, _accessToken);
        }

        /// <summary>
        /// Builds the Sitecore anchor link XML string for a section.
        /// </summary>
        private string BuildAnchorLink(string title, int index)
        {
            string safeTitle = WebUtility.HtmlEncode(title); // encodes &, <, >, ", '
            return $"<link text=\"{safeTitle}\" linktype=\"anchor\" url=\"keypoint{index}\" anchor=\"keypoint{index}\" title=\"\" class=\"\" />";
        }

        private async Task CreateInPageBannerDatasources(string newUrlPath, string dataItemId, PageDataModel sourcePageItem)
        {
            var xpRendering = sourcePageItem.Renderings.FirstOrDefault(x => x.RenderingName.Contains(XP_RenderingName_Constants.Multi_Button_Callout));

            if (xpRendering != null)
            {
                var xpDatasource = SitecoreUtility.GetDatasource(sourcePageItem, xpRendering.DatasourceID);

                if (xpDatasource != null)
                {
                    var path = $"{newUrlPath}/Data/Alert";

                    var xmcDatasource = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, path);
                    if (xmcDatasource == null)
                    {
                        var fields = new List<SitecoreFieldInput>();
                        var heading = SitecoreUtility.GetSitecoreFieldInput(xpDatasource, "Heading", "heading");
                        if (heading != null)
                            fields.Add(heading);
                        var bodyText = SitecoreUtility.GetSitecoreFieldInput(xpDatasource, "Description", "bodyText");
                        if (bodyText != null)
                            fields.Add(bodyText);

                        var inputItem = new SitecoreCreateItemInput
                        {
                            Name = "Alert",
                            TemplateId = XMC_Template_Constants.In_Page_Banner,
                            Parent = dataItemId,
                            Language = "en",
                            Fields = fields
                        };

                        await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(
                            new List<SitecoreCreateItemInput> { inputItem }, _environment, _accessToken);
                    }                    
                }
            }
        }

        private async Task<SitecoreFieldInput> CreatePublicationInfoDatasources(string newUrlPath, string dataItemId, string generalHeaderItemId, 
            PageDataModel sourcePageItem)
        {
            var xpRendering = sourcePageItem.Renderings.FirstOrDefault(x => x.RenderingName.Contains(XP_RenderingName_Constants.Publication_Content));

            if (xpRendering != null)
            {
                var xpDatasource = SitecoreUtility.GetDatasource(sourcePageItem, xpRendering.DatasourceID);

                if (xpDatasource != null)
                {
                    var path = $"{newUrlPath}/Data/{xpDatasource.Name}";

                    var xmcDatasource = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, path);
                    if (xmcDatasource == null)
                    {
                        var fields = new List<SitecoreFieldInput>();
                        var draftNumberField = SitecoreUtility.GetSitecoreFieldInput(xpDatasource, "DraftNumber", "draftNumber");
                        if (draftNumberField != null)
                            fields.Add(draftNumberField);
                        var displayCWApprovalSeal = SitecoreUtility.GetSitecoreFieldInput(xpDatasource, "DisplayCWApprovalSeal", "displayCWApprovalSeal");
                        if (displayCWApprovalSeal != null)
                            fields.Add(displayCWApprovalSeal);
                        var documentDate = SitecoreUtility.GetSitecoreFieldInput(xpDatasource, "DocumentDate", "documentDate");
                        if (documentDate != null)
                            fields.Add(documentDate);
                        var nextReviewDate = SitecoreUtility.GetSitecoreFieldInput(xpDatasource, "NextReviewDate", "nextReviewDate");
                        if (nextReviewDate != null)
                            fields.Add(nextReviewDate);

                        var inputItem = new SitecoreCreateItemInput
                        {
                            Name = xpDatasource.Name,
                            TemplateId = XMC_Template_Constants.Publication_Info,
                            Parent = dataItemId,
                            Language = "en",
                            Fields = fields
                        };

                        var createdItems = await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(
                            new List<SitecoreCreateItemInput> { inputItem }, _environment, _accessToken);                        
                    }                                            

                    var headline = xpDatasource.Fields?.FirstOrDefault(x => x.Name.Equals("Headline"))?.Value ?? string.Empty;
                    var draftNumber = xpDatasource.Fields?.FirstOrDefault(x => x.Name.Equals("DraftNumber"))?.Value ?? string.Empty;
                    var title = $"{headline} ({draftNumber})";

                    return new SitecoreFieldInput
                    {
                        Name = "title",
                        Value = title
                    };
                }
            }
            return null;
        }

        private async Task CreateScriptDatasources(string newUrlPath, string dataItemId, PageDataModel sourcePageItem)
        {
            var xpRenderings = sourcePageItem.Renderings.Where(x => x.RenderingName.Contains(XP_RenderingName_Constants.Script));

            if (xpRenderings != null && xpRenderings.Any())
            {
                var folderPath = $"{newUrlPath}/Data/User Scripts";

                var folderItemId = string.Empty;                

                foreach (var xpRendering in xpRenderings) 
                {                    
                    var xpDataSource = SitecoreUtility.GetDatasource(sourcePageItem, xpRendering.DatasourceID);
                    if (xpDataSource != null)
                    {
                        var path = $"{newUrlPath}/Data/User Scripts/{xpDataSource.Name}";
                        var xmcDatasource = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, path);
                        if (xmcDatasource == null)
                        {
                            var fields = new List<SitecoreFieldInput>();
                            var javaScript = SitecoreUtility.GetSitecoreFieldInput(xpDataSource, "JavaScript", "javaScript");
                            if (javaScript != null)
                                fields.Add(javaScript);
                            var inputItem = new SitecoreCreateItemInput
                            {
                                Language = "en",
                                Parent = folderItemId,
                                Name = xpDataSource.Name,
                                TemplateId = Constants.UserScriptItem,
                                Fields = fields
                            };

                            var createdItems = await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(
                                new List<SitecoreCreateItemInput> { inputItem }, _environment, _accessToken);
                        }
                    }
                }                
            }
        }

        #endregion
    }
}
