using CWXPMigration.Models;
using System.Collections.Generic;
using System.Linq;
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
            RenderingInfo publicationInfoRendering,
            PageDataModel sourcePageItem,
            string dataItemId,
            string newUrlPath,
            string environment,
            string accessToken);
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
            IEnumerable<RenderingInfo> rteRenderings,
            RenderingInfo publicationInfoRendering,
            PageDataModel sourcePageItem,
            string dataItemId,
            string newUrlPath,
            string environment,
            string accessToken)
        {
            _environment = environment;
            _accessToken = accessToken;

            List<RichTextSection> jumpLinkSections = new List<RichTextSection>();

            foreach (var rteRendering in rteRenderings)
            {
                var rteDatasource = XMCItemUtility.GetDatasource(sourcePageItem, rteRendering.DatasourceID);
                if (rteDatasource == null)
                    continue;

                var rteField = XMCItemUtility.GetRichTextField(rteDatasource);
                if (rteField == null)
                    continue;

                var contents = RichTextSplitter.SplitByH2(rteField.Value);
                if (contents == null || !contents.Any())
                    continue;

                jumpLinkSections.AddRange(contents);

                var path = $"{newUrlPath}/Data/{rteDatasource.Name}";
                var rteDatasourceItem = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, path);

                if (rteDatasourceItem != null)
                    return;

                var finalHtml = RichTextSplitter.AddIdAttributeToAllH2(rteField.Value);

                var rteInputItem = XMCItemUtility.GetSitecoreCreateItemInput(rteDatasource.Name,
                    Constants.XMC_RTE_ITEM_TEMPLATEID, dataItemId, "text", finalHtml);
                
                await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(
                    new List<SitecoreCreateItemInput> { rteInputItem }, _environment, _accessToken);
            }

            if (!jumpLinkSections.Any()) return;

            var generalHeaderItemId = await GetOrCreateGeneralHeaderItemAsync(newUrlPath, dataItemId);

            if(string.IsNullOrEmpty(generalHeaderItemId)) return;

            var linkItemIds = await ProcessJumpLinksAsync(jumpLinkSections, newUrlPath, generalHeaderItemId);

            if(linkItemIds == null || !linkItemIds.Any()) return;

            var publicationInfoDatasource = XMCItemUtility.GetDatasource(sourcePageItem, publicationInfoRendering.DatasourceID);

            if(publicationInfoDatasource == null) return;

            await CreatePublicationInfoItemAsync(newUrlPath, dataItemId, publicationInfoDatasource);

            var headline = publicationInfoDatasource.Fields?.FirstOrDefault(x => x.Name.Equals("Name"))?.Value ?? string.Empty;
            var draftNumber = publicationInfoDatasource.Fields?.FirstOrDefault(x => x.Name.Equals("DraftNumber"))?.Value ?? string.Empty;
            var title = $"{headline} ({draftNumber})";
            await UpdateGeneralHeaderWithLinksAsync(generalHeaderItemId, linkItemIds, title);
        }

        #region Private Helpers

        /// <summary>
        /// Retrieves or creates the General Header item under the specified path.
        /// </summary>
        private async Task<string> GetOrCreateGeneralHeaderItemAsync(string newUrlPath, string dataItemId)
        {
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
                TemplateId = Constants.GeneralHeaderTemplateID
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
                TemplateId = Constants.GenericLinkTemplateID,
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
        /// Creates a Publication Info Datasource
        /// </summary>
        private async Task<string> CreatePublicationInfoItemAsync(string newUrlPath, string dataItemId, DataSourceDetail publicationInfo)
        {

            var path = $"{newUrlPath}/Data/General Header/{publicationInfo.Name}";
            
            var publicationInfoItem = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, path);
            if (publicationInfoItem != null)
                return publicationInfoItem.ItemId;

            var fields = new List<SitecoreFieldInput>();
            var draftNumber = XMCItemUtility.GetSitecoreFieldInput(publicationInfo, "DraftNumber", "draftNumber");
            if (draftNumber != null)
                fields.Add(draftNumber);
            var displayCWApprovalSeal = XMCItemUtility.GetSitecoreFieldInput(publicationInfo, "DisplayCWApprovalSeal", "displayCWApprovalSeal");
            if (displayCWApprovalSeal != null)
                fields.Add(displayCWApprovalSeal);
            var documentDate = XMCItemUtility.GetSitecoreFieldInput(publicationInfo, "DocumentDate", "documentDate");
            if (documentDate != null)
                fields.Add(documentDate);
            var nextReviewDate = XMCItemUtility.GetSitecoreFieldInput(publicationInfo, "NextReviewDate", "nextReviewDate");
            if (nextReviewDate != null)
                fields.Add(nextReviewDate);

            var inputItem = new SitecoreCreateItemInput
            {
                Name = publicationInfo.Name,
                TemplateId = Constants.PublicationInfoTemplateID,
                Parent = dataItemId,
                Language = "en",
                Fields = fields
            };

            var createdItems = await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(
                new List<SitecoreCreateItemInput> { inputItem }, _environment, _accessToken);

            return createdItems?.FirstOrDefault()?.ItemId;
        }

        /// <summary>
        /// Updates the General Header item's CTA3 field with a pipe-separated list of link item IDs.
        /// </summary>
        private async Task UpdateGeneralHeaderWithLinksAsync(string generalHeaderItemId, List<string> linkItemIds,
            string title)
        {
            var updateItem = new SitecoreUpdateItemInput
            {
                ItemId = generalHeaderItemId,
                Fields = new List<SitecoreFieldInput>
                {
                    new SitecoreFieldInput
                    {
                        Name = "cta3",
                        Value = string.Join("|", linkItemIds)
                    },
                    new SitecoreFieldInput
                    {
                        Name = "title",
                        Value = title
                    }
                }
            };

            await this.SitecoreGraphQLClient.UpdateBulkItemsBatchedAsync(
                new List<SitecoreUpdateItemInput> { updateItem }, _environment, _accessToken);
        }

        /// <summary>
        /// Builds the Sitecore anchor link XML string for a section.
        /// </summary>
        private string BuildAnchorLink(string title, int index)
        {
            return $"<link text=\"{title}\" linktype=\"anchor\" url=\"keypoint{index}\" anchor=\"keypoint{index}\" title=\"\" class=\"\" />";
        }        

        #endregion
    }
}
