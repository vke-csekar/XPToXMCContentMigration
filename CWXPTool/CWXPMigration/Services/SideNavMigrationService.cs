using CWXPMigration.Models;
using Microsoft.Extensions.DependencyInjection;
using Sitecore.StringExtensions;
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
            string accessToken,
            List<PageMapping> allPageMappings);                        
    }
    public class SideNavMigrationService : BaseMigrationService, ISideNavMigrationService
    {                
        public SideNavMigrationService(ISitecoreGraphQLClient sitecoreGraphQLClient) : base(sitecoreGraphQLClient) { 
            
        }

        string _environment = string.Empty;
        string _accessToken = string.Empty;
        List<PageMapping> _allPageMappings;

        public async Task ProcessAsync(
            IEnumerable<RenderingInfo> rteRenderings,
            PageDataModel sourcePageItem,
            string dataItemId,
            string newUrlPath,
            string environment,
            string accessToken, 
            List<PageMapping> allPageMappings)
        {
            _environment = environment;
            _accessToken = accessToken;
            _allPageMappings = allPageMappings;

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

            sideNavContents.AddRange(AddAccordionItemToSideNavs(sourcePageItem));

            if (sideNavContents.Any())
                await CreateSideNavContentItemsAsync(sideNavContents, dataItemId, newUrlPath);
        }

        private async Task CreateSideNavContentItemsAsync(IEnumerable<RichTextSection> contents, string dataItemId, string newUrlPath)
        {
            var sideNavsWithNoTitle = contents.Where(x => string.IsNullOrEmpty(x.Title));
            var sideNavsWithTitle = contents.Where(x => !string.IsNullOrEmpty(x.Title));            

            await CreateUntitledItemsAsync(sideNavsWithNoTitle, dataItemId, newUrlPath);

            if (sideNavsWithTitle.Any())
            {
                var sideNavContainerItem = await EnsureSideNavContainerAsync(dataItemId, newUrlPath);
                if (sideNavContainerItem != null && !string.IsNullOrEmpty(sideNavContainerItem.ItemId))
                {                    
                    await CreateTitledItemsAsync(sideNavsWithTitle, sideNavContainerItem, newUrlPath);
                }                
            }
        }

        private List<RichTextSection> AddAccordionItemToSideNavs(PageDataModel sourcePageItem)
        {
            List<RichTextSection> accordionNavItems = new List<RichTextSection>();

            var xpRenderings = sourcePageItem.Renderings.Where(x => x.RenderingName.Contains(XP_RenderingName_Constants.Accordion))?.ToList();

            if (xpRenderings?.Any() == true)
            {
                foreach (var rendering in xpRenderings)
                {
                    var xpDatasourceItem = Sitecore.Context.Database.GetItem(rendering.DatasourceID);

                    if (xpDatasourceItem != null && xpDatasourceItem.HasChildren)
                    {
                        var xpAccordionItems = xpDatasourceItem.GetChildren().ToList();

                        foreach (var xpAccordionItem in xpAccordionItems)
                        {
                            var title = xpAccordionItem.Fields.FirstOrDefault(x => x.Name == "Title" && !string.IsNullOrEmpty(x.Value))?.Value ?? string.Empty;
                            var content = xpAccordionItem.Fields.FirstOrDefault(x => x.Name == "Content" && !string.IsNullOrEmpty(x.Value))?.Value ?? string.Empty;
                            accordionNavItems.Add(new RichTextSection()
                            {
                                Title = title,
                                HtmlContent = content,
                                Language = "en"
                            });
                        }
                    }

                }
            }

            return accordionNavItems;
        }

        private async Task CreateUntitledItemsAsync(IEnumerable<RichTextSection> noTitleItems, string parentItemId, string newUrlPath)
        {
            var items = new List<SitecoreCreateItemInput>();
            var updateItems = new List<SitecoreUpdateItemInput>();
            int counter = 1;

            foreach (var content in noTitleItems)
            {
                var path = $"{newUrlPath}/Data/RTE-{counter}";
                var existingItem = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, path);
                if (existingItem == null) {
                    items.Add(await CreateRteInput($"RTE-{counter}", content.HtmlContent, parentItemId));
                }
                else
                {
                    updateItems.Add(await UpdateRteInput(existingItem.ItemId, content.HtmlContent));
                }
                counter++;
            }

            if(items.Any())
                await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(items, _environment, _accessToken, 10);

            if (updateItems.Any())
                await this.SitecoreGraphQLClient.UpdateBulkItemsBatchedAsync(updateItems, _environment, _accessToken, 10);
        }

        private async Task CreateTitledItemsAsync(IEnumerable<RichTextSection> titledItems, SitecoreItemBase sideNavContainerItem, string newUrlPath)
        {
            foreach (var content in titledItems)
            {                
                var sideNavItemName = PathFormatter.FormatItemName(content.Title);
                var sectionItemId = await EnsureSideNavSectionAsync(content.Title, sideNavItemName, sideNavContainerItem, newUrlPath);

                if (!string.IsNullOrEmpty(sectionItemId) && !string.IsNullOrEmpty(content.HtmlContent))
                {
                    var path = $"{newUrlPath}/Data/{sideNavContainerItem.ItemName}/{sideNavItemName}/RTE";
                    var existingItem = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, path);
                    if(existingItem == null)
                    {                        
                        var inputItem = await CreateRteInput("RTE", content.HtmlContent, sectionItemId);                        
                        await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(new List<SitecoreCreateItemInput>() { inputItem }, _environment, _accessToken);
                    }
                    else
                    {
                        var updateItem = await UpdateRteInput(existingItem.ItemId, content.HtmlContent);
                        await this.SitecoreGraphQLClient.UpdateBulkItemsBatchedAsync(new List<SitecoreUpdateItemInput>() { updateItem }, _environment, _accessToken, 10);
                    }
                }
            }
        }

        private async Task<SitecoreCreateItemInput> CreateRteInput(string itemName, string content, string parentItemId)
        {
            var finalHtml = await RichTextSplitter.RemapInternalSitecoreLinks(content, this.SitecoreGraphQLClient, _environment, _accessToken, _allPageMappings);

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
                        Value = finalHtml
                    }
                }
            };
        }

        private async Task<SitecoreUpdateItemInput> UpdateRteInput(string itemId, string content)
        {
            var finalHtml = await RichTextSplitter.RemapInternalSitecoreLinks(content, this.SitecoreGraphQLClient, _environment, _accessToken, _allPageMappings);

            return new SitecoreUpdateItemInput
            {
                Language = "en",
                ItemId = itemId,
                Fields = new List<SitecoreFieldInput>
                {
                    new SitecoreFieldInput
                    {
                        Name = "text",
                        Value = finalHtml
                    }
                }
            };
        }

        private async Task<SitecoreItemBase> GetExistingSideNavContainer(string newUrlPath)
        {            
            
            var sideNavContainerResult = await this.SitecoreGraphQLClient.QueryItemsAsync<SitecoreItemBase>(
                _environment,
                _accessToken,
                $"{newUrlPath}/Data",
                new List<string>(), // No extra fields for now
                jObj => new SitecoreItemBase
                {
                    ItemId = jObj["itemId"]?.ToString(),
                    Path = jObj["path"]?.ToString(),
                    ItemName = jObj["itemName"]?.ToString()
                },
                includeTemplateIDs: new List<string>() { "a3dc84b7cdf1468c92efc33dc4311075" }
            );
            
            if(sideNavContainerResult != null && sideNavContainerResult.Items != null)
                return sideNavContainerResult.Items.FirstOrDefault();
            
            return null;
        }

        private async Task<SitecoreItemBase> EnsureSideNavContainerAsync(string parentId, string newUrlPath)
        {
            var existingItem = await GetExistingSideNavContainer(newUrlPath);

            if (existingItem != null)
                return existingItem;

            var createItemInput = new SitecoreCreateItemInput
            {
                Name = "Side Nav",
                TemplateId = XMC_Template_Constants.SideNav,
                Language = "en",
                Parent = parentId
            };

            var created = await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(new List<SitecoreCreateItemInput> { createItemInput }, _environment, _accessToken, 10);
            
            if(created != null)
            {
                var createdItem = created.FirstOrDefault();
                return new SitecoreItemBase()
                {
                    ItemId = createdItem.ItemId,
                    ItemName = createdItem.Name,
                    Path = createdItem.Path
                };
            }

            return null;
        }

        private async Task<string> EnsureSideNavSectionAsync(string title, string sideNavItemName, SitecoreItemBase sideNavContainerItem, string newUrlPath)
        {            
            var path = $"{newUrlPath}/Data/{sideNavContainerItem.ItemName}/{sideNavItemName}";

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
                Parent = sideNavContainerItem.ItemId,
                Fields = fields
            };            

            var created = await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(new List<SitecoreCreateItemInput> { createItemInput }, _environment, _accessToken, 10);
            return created.First().ItemId;
        }
    }
}