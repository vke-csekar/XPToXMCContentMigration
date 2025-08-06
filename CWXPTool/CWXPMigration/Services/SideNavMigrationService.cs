using CWXPMigration.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.UI.WebControls;

namespace CWXPMigration.Services
{
    public interface ISideNavMigrationService
    {
        Task ProcessAsync(
            IEnumerable<RenderingInfo> rteRenderings,
            PageDataModel sourcePageItem,
            string dataItemId,
            string newUrlPath,
            string environment,
            string accessToken);                        
    }
    public class SideNavMigrationService : BaseMigrationService, ISideNavMigrationService
    {                
        public SideNavMigrationService(ISitecoreGraphQLClient sitecoreGraphQLClient) : base(sitecoreGraphQLClient) { 
            
        }

        string _environment = string.Empty;
        string _accessToken = string.Empty;

        public async Task ProcessAsync(
            IEnumerable<RenderingInfo> rteRenderings,
            PageDataModel sourcePageItem,
            string dataItemId,
            string newUrlPath,
            string environment,
            string accessToken)
        {
            _environment = environment;
            _accessToken = accessToken;

            List<RichTextSection> sideNavContents = new List<RichTextSection>();

            foreach (var rteRendering in rteRenderings)
            {
                var rteDatasource = SitecoreUtility.GetDatasource(sourcePageItem, rteRendering.DatasourceID);
                if (rteDatasource == null)
                    continue;

                var rteField = SitecoreUtility.GetRichTextField(rteDatasource);
                if (rteField == null)
                    continue;

                var contents = RichTextSplitter.SplitByH2(rteField.Value, "en");
                if (contents == null || !contents.Any())
                    continue;

                sideNavContents.AddRange(contents);                
            }

            if(sideNavContents.Any())
                await CreateSideNavContentItemsAsync(sideNavContents, dataItemId, newUrlPath);
        }

        private async Task CreateSideNavContentItemsAsync(IEnumerable<RichTextSection> contents, string dataItemId, string newUrlPath)
        {
            var sideNavsWithNoTitle = contents.Where(x => string.IsNullOrEmpty(x.Title));
            var sideNavsWithTitle = contents.Where(x => !string.IsNullOrEmpty(x.Title));

            await CreateUntitledItemsAsync(sideNavsWithNoTitle, dataItemId, newUrlPath);

            if (sideNavsWithTitle.Any())
            {
                var sideNavContainerItemId = await EnsureSideNavContainerAsync(dataItemId, newUrlPath);
                if (!string.IsNullOrEmpty(sideNavContainerItemId))
                {
                    await CreateTitledItemsAsync(sideNavsWithTitle, sideNavContainerItemId, newUrlPath);
                }                
            }
        }

        private async Task CreateUntitledItemsAsync(IEnumerable<RichTextSection> noTitleItems, string parentItemId, string newUrlPath)
        {
            var items = new List<SitecoreCreateItemInput>();
            int counter = 1;

            foreach (var content in noTitleItems)
            {
                var path = $"{newUrlPath}/Data/RTE-{counter}";
                var existingItem = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, path);
                if (existingItem == null) {
                    items.Add(CreateRteInput($"RTE-{counter}", content.HtmlContent, parentItemId));
                }                
                counter++;
            }

            if(items.Any())
                await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(items, _environment, _accessToken, 10);
        }

        private async Task CreateTitledItemsAsync(IEnumerable<RichTextSection> titledItems, string sideNavContainerItemId, string newUrlPath)
        {
            foreach (var content in titledItems)
            {                
                var sideNavItemName = PathFormatter.FormatItemName(content.Title);
                var sectionItemId = await EnsureSideNavSectionAsync(content.Title, sideNavItemName, sideNavContainerItemId, newUrlPath);

                if (!string.IsNullOrEmpty(sectionItemId) && !string.IsNullOrEmpty(content.HtmlContent))
                {
                    var path = $"{newUrlPath}/Data/Side Nav/{sideNavItemName}/RTE";
                    var existingItem = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, path);
                    if(existingItem == null)
                    {
                        var inputItem = CreateRteInput("RTE", content.HtmlContent, sectionItemId);                        
                        await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(new List<SitecoreCreateItemInput>() { inputItem }, _environment, _accessToken);
                    }                    
                }
            }
        }

        private SitecoreCreateItemInput CreateRteInput(string itemName, string content, string parentItemId)
        {
            return new SitecoreCreateItemInput
            {
                Name = itemName,
                TemplateId = XMC_Template_Constants.RTE,
                Language = "en",
                Parent = parentItemId,
                Fields = new List<SitecoreFieldInput>
                {
                    new SitecoreFieldInput
                    {
                        Name = "text",
                        Value = content
                    }
                }
            };
        }

        private async Task<string> EnsureSideNavContainerAsync(string parentId, string newUrlPath)
        {
            var path = $"{newUrlPath}/Data/Side Nav";
            var existingItem = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, path);

            if (existingItem != null)
                return existingItem.ItemId;

            var createItemInput = new SitecoreCreateItemInput
            {
                Name = "Side Nav",
                TemplateId = XMC_Template_Constants.SideNav,
                Language = "en",
                Parent = parentId
            };

            var created = await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(new List<SitecoreCreateItemInput> { createItemInput }, _environment, _accessToken, 10);
            return created.First().ItemId;
        }

        private async Task<string> EnsureSideNavSectionAsync(string title, string sideNavItemName, string parentId, string newUrlPath)
        {            
            var path = $"{newUrlPath}/Data/Side Nav/{sideNavItemName}";

            var existingItem = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, path);

            if (existingItem != null)
                return existingItem.ItemId;

            var fields = new List<SitecoreFieldInput>
            {
                new SitecoreFieldInput { Name = "heading", Value = title },
                new SitecoreFieldInput { Name = "sideNavHeading", Value = title }
            };

            var createItemInput = new SitecoreCreateItemInput
            {
                Name = sideNavItemName,
                TemplateId = XMC_Template_Constants.SideNavSection,
                Language = "en",
                Parent = parentId,
                Fields = fields
            };            

            var created = await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(new List<SitecoreCreateItemInput> { createItemInput }, _environment, _accessToken, 10);
            return created.First().ItemId;
        }
    }
}