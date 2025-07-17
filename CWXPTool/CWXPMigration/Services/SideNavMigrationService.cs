using CWXPMigration.Models;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.UI.WebControls;

namespace CWXPMigration.Services
{
    public class SideNavMigrationService : MigrationService
    {        
        public SideNavMigrationService() : base() { }

        public async Task ProcessRichTextRenderingsAsync(
            IEnumerable<RenderingInfo> rteRenderings,
            PageDataModel sourcePageItem,
            string dataItemId,
            string newUrlPath,
            string accessToken)
        {
            foreach (var rteRendering in rteRenderings)
            {
                var rteDatasource = GetRichTextDatasource(sourcePageItem, rteRendering.DatasourceID);
                if (rteDatasource == null)
                    continue;

                var rteField = GetRichTextField(rteDatasource);
                if (rteField == null)
                    continue;

                var sideNavContents = RichTextSplitter.SplitByH2(rteField.Value);
                if (sideNavContents == null || !sideNavContents.Any())
                    continue;

                await CreateSideNavContentItemsAsync(sideNavContents, dataItemId, newUrlPath, accessToken);
            }
        }

        public DataSourceDetail GetRichTextDatasource(PageDataModel sourcePageItem, string datasourceId)
        {
            return sourcePageItem.DataSources.FirstOrDefault(x => x.ID.Equals(datasourceId));
        }

        public XPField GetRichTextField(DataSourceDetail datasource)
        {
            return datasource.Fields.FirstOrDefault(f =>
                !string.IsNullOrEmpty(f.Value) &&
                f.Type.Equals("Rich Text", System.StringComparison.OrdinalIgnoreCase));
        }

        public async Task CreateSideNavContentItemsAsync(IEnumerable<RichTextSection> contents, string dataItemId, string newUrlPath, string accessToken)
        {
            var sideNavsWithNoTitle = contents.Where(x => string.IsNullOrEmpty(x.Title));
            var sideNavsWithTitle = contents.Where(x => !string.IsNullOrEmpty(x.Title));

            await CreateUntitledItemsAsync(sideNavsWithNoTitle, dataItemId, newUrlPath, accessToken);

            if (sideNavsWithTitle.Any())
            {
                var sideNavContainerItemId = await EnsureSideNavContainerAsync(dataItemId, newUrlPath, accessToken);
                if (!string.IsNullOrEmpty(sideNavContainerItemId))
                {
                    await CreateTitledItemsAsync(sideNavsWithTitle, sideNavContainerItemId, newUrlPath, accessToken);
                }                
            }
        }

        public async Task CreateUntitledItemsAsync(IEnumerable<RichTextSection> noTitleItems, string parentItemId, string newUrlPath, string accessToken)
        {
            var items = new List<SitecoreCreateItemInput>();
            int counter = 1;

            foreach (var content in noTitleItems)
            {
                var path = $"{newUrlPath}/Data/RTE-{counter}";
                var existingItem = await this.SitecoreGraphQLClient.QuerySingleItemAsync(accessToken, path);
                if (existingItem == null) {
                    items.Add(CreateRteInput($"RTE-{counter}", content.HtmlContent, parentItemId));
                }                
                counter++;
            }

            if(items.Any())
                await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(items, accessToken, 10);
        }

        public async Task CreateTitledItemsAsync(IEnumerable<RichTextSection> titledItems, string sideNavContainerItemId, string newUrlPath, string accessToken)
        {
            foreach (var content in titledItems)
            {                
                var sideNavItemName = PathFormatter.FormatItemName(content.Title);
                var sectionItemId = await EnsureSideNavSectionAsync(content.Title, sideNavItemName, sideNavContainerItemId, newUrlPath, accessToken);

                if (!string.IsNullOrEmpty(sectionItemId) && !string.IsNullOrEmpty(content.HtmlContent))
                {
                    var path = $"{newUrlPath}/Data/Side Nav/{sideNavItemName}/RTE";
                    var existingItem = await this.SitecoreGraphQLClient.QuerySingleItemAsync(accessToken, path);
                    if(existingItem == null)
                    {
                        var inputItem = CreateRteInput("RTE", content.HtmlContent, sectionItemId);                        
                        await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(new List<SitecoreCreateItemInput>() { inputItem }, accessToken);
                    }                    
                }
            }
        }

        public SitecoreCreateItemInput CreateRteInput(string itemName, string content, string parentItemId)
        {
            return new SitecoreCreateItemInput
            {
                Name = itemName,
                TemplateId = "{0EFFE34A-636F-4288-BA3B-0AF056AAD42B}",
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

        public async Task<string> EnsureSideNavContainerAsync(string parentId, string newUrlPath, string accessToken)
        {
            var path = $"{newUrlPath}/Data/Side Nav";
            var existingItem = await this.SitecoreGraphQLClient.QuerySingleItemAsync(accessToken, path);

            if (existingItem != null)
                return existingItem.ItemId;

            var createItemInput = new SitecoreCreateItemInput
            {
                Name = "Side Nav",
                TemplateId = "{A3DC84B7-CDF1-468C-92EF-C33DC4311075}",
                Language = "en",
                Parent = parentId
            };

            var created = await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(new List<SitecoreCreateItemInput> { createItemInput }, accessToken, 10);
            return created.First().ItemId;
        }

        public async Task<string> EnsureSideNavSectionAsync(string title, string sideNavItemName, string parentId, string newUrlPath, string accessToken)
        {            
            var path = $"{newUrlPath}/Data/Side Nav/{sideNavItemName}";

            var existingItem = await this.SitecoreGraphQLClient.QuerySingleItemAsync(accessToken, path);

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
                TemplateId = "{72BB023A-CE3E-4523-B4AA-16E54561D8D4}",
                Language = "en",
                Parent = parentId,
                Fields = fields
            };            

            var created = await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(new List<SitecoreCreateItemInput> { createItemInput }, accessToken, 10);
            return created.First().ItemId;
        }
    }
}