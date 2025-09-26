using CWXPMigration.Models;
using JSNLog.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sitecore.Data;
using Sitecore.Globalization;
using Sitecore.Mvc.Names;
using Sitecore.StringExtensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace CWXPMigration.Services
{
    public interface IELearningCenterMigrationService
    {
        /// <summary>
        /// Processes Rich Text Renderings and creates/upgrades the Side Navigation links in Sitecore.
        /// </summary>
        Task ProcessAsync(
            string language,
            IEnumerable<RenderingInfo> rteRenderings,
            PageDataModel sourcePageItem,
            List<PageMapping> allPageMappings,
            string dataItemId,
            string newUrlPath,
            string environment,
            string accessToken);        
    }

    public class ELearningCenterMigrationService : BaseMigrationService, IELearningCenterMigrationService
    {
        private string _environment;
        private string _accessToken;
        private List<PageMapping> _allPageMappings;

        public ELearningCenterMigrationService(ISitecoreGraphQLClient sitecoreGraphQLClient)
            : base(sitecoreGraphQLClient)
        {
        }

        /// <inheritdoc />
        public async Task ProcessAsync(
            string language,
            IEnumerable<RenderingInfo> xpRteRenderings,
            PageDataModel sourcePageItem,
            List<PageMapping> allPageMappings,
            string dataItemId,
            string newUrlPath,
            string environment,
            string accessToken)
        {
            _environment = environment;
            _accessToken = accessToken;
            _allPageMappings = allPageMappings;

            int counter = 0;

            foreach (var xpRteRendering in xpRteRenderings)
            {
                var xpRteDatasource = SitecoreUtility.GetDatasource(sourcePageItem, xpRteRendering.DatasourceID);

                if (xpRteDatasource != null)
                {                    
                    var xpRteField = SitecoreUtility.GetRichTextField(xpRteDatasource);
                    if (xpRteField != null)
                    {
                        var datasourceName = counter == 0 ? "RTE" : xpRteDatasource.Name;
                        
                        var path = $"{newUrlPath}/Data/{datasourceName}";
                        var xmcRteDatasource = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, path);
                        
                        var linksRemappedHtml = await RichTextSplitter.RemapInternalSitecoreLinks(xpRteField.Value, this.SitecoreGraphQLClient, _environment, _accessToken, _allPageMappings);

                        if (string.IsNullOrEmpty(linksRemappedHtml))
                            continue;

                        var finalHtml = RichTextSplitter.Change_CHWH2_H4(linksRemappedHtml);

                        if (string.IsNullOrEmpty(finalHtml))
                            continue;

                        if (xmcRteDatasource == null)
                        {
                            var rteInputItem = SitecoreUtility.GetSitecoreCreateItemInput(datasourceName,
                                XMC_Template_Constants.RTE, dataItemId, "text", finalHtml);

                            await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(
                                new List<SitecoreCreateItemInput> { rteInputItem }, _environment, _accessToken);
                        }
                        else
                        {
                            var updateItem = new SitecoreUpdateItemInput
                            {
                                ItemId = xmcRteDatasource.ItemId,
                                Fields = new List<SitecoreFieldInput>() { new SitecoreFieldInput(){
                                        Name = "text",
                                        Value = finalHtml,
                                    } },
                                Language = language,
                            };

                            await this.SitecoreGraphQLClient.UpdateBulkItemsBatchedAsync(
                                new List<SitecoreUpdateItemInput> { updateItem }, _environment, _accessToken);
                        }
                        counter++;
                    }
                }
            }

            //await CreateInPageBannerDatasources(language, newUrlPath, dataItemId, sourcePageItem);            

            await CreateScriptDatasources(newUrlPath, dataItemId, sourcePageItem);
        }

        #region Private Helpers

        private async Task CreateInPageBannerDatasources(string language, string newUrlPath, string dataItemId, PageDataModel sourcePageItem)
        {
            Sitecore.Diagnostics.Log.Info("CreateInPageBannerDatasources", this);
            var xpRenderings = sourcePageItem.Renderings.Where(x => x.RenderingName.Contains(XP_RenderingName_Constants.SidebarContentCallout));

            if(xpRenderings != null && xpRenderings.Any())
            {
                Sitecore.Diagnostics.Log.Info("CreateInPageBannerDatasources found", this);
                foreach (var xpRendering in xpRenderings)
                {
                    var xpDatasource = SitecoreUtility.GetDatasource(sourcePageItem, xpRendering.DatasourceID);

                    if (xpDatasource != null)
                    {
                        Sitecore.Diagnostics.Log.Info("xpDatasource found", this);
                        var rootPath = $"/sitecore/content/CW/childrens/Data/In Page Banners/E-Learning Center";

                        var xmcRootDatasource = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, rootPath);

                        if(xmcRootDatasource != null)
                        {
                            Sitecore.Diagnostics.Log.Info("xmcRootDatasource found", this);
                            var fields = new List<SitecoreFieldInput>();
                            var heading = SitecoreUtility.GetSitecoreFieldInput(xpDatasource, "Title", "heading");
                            if (heading != null)
                                fields.Add(heading);
                            var bodyText = SitecoreUtility.GetSitecoreFieldInput(xpDatasource, "Content", "bodyText");
                            if (bodyText != null)
                            {
                                var linksRemappedHtml = await RichTextSplitter.RemapInternalSitecoreLinks(bodyText.Value?.ToString(), this.SitecoreGraphQLClient, _environment, _accessToken, _allPageMappings);

                                if (!string.IsNullOrEmpty(linksRemappedHtml))
                                {
                                    bodyText.Value = linksRemappedHtml;
                                    fields.Add(bodyText);
                                }                                    
                            }

                            var linkText = xpDatasource.Fields.FirstOrDefault(x => x.Name == "LinkText" && !string.IsNullOrEmpty(x.Value))?.Value ?? string.Empty;
                            var link = xpDatasource.Fields.FirstOrDefault(x => x.Name == "Link" && !string.IsNullOrEmpty(x.Value))?.Value ?? string.Empty;

                            var finalLink = SitecoreUtility.UpdateLinkText(link, linkText);

                            fields.Add(new SitecoreFieldInput()
                            {
                                Name = "ctaButton",
                                Value = finalLink
                            });

                            var xmcDatasource = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, $"{rootPath}/{xpDatasource.Name}");

                            if(xmcDatasource == null)
                            {
                                Sitecore.Diagnostics.Log.Info("xmcDatasource not found", this);
                                var inputItem = new SitecoreCreateItemInput
                                {
                                    Name = xpDatasource.Name,
                                    TemplateId = XMC_Template_Constants.In_Page_Banner,
                                    Parent = xmcRootDatasource.ItemId,
                                    Language = "en",
                                    Fields = fields
                                };

                                await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(
                                    new List<SitecoreCreateItemInput> { inputItem }, _environment, _accessToken);
                            }   
                            else
                            {
                                Sitecore.Diagnostics.Log.Info("xmcDatasource found", this);
                                var updateItem = new SitecoreUpdateItemInput
                                {
                                    ItemId = xmcDatasource.ItemId,
                                    Language = "en",
                                    Fields = fields
                                };
                                await this.SitecoreGraphQLClient.UpdateBulkItemsBatchedAsync(
                                    new List<SitecoreUpdateItemInput> { updateItem }, _environment, _accessToken);
                            }
                        }                                                                    
                    }
                }
            }
        }        

        private async Task CreateScriptDatasources(string newUrlPath, string dataItemId, PageDataModel sourcePageItem)
        {            
            var xpRenderings = sourcePageItem.Renderings.Where(x => x.RenderingName.Contains(XP_RenderingName_Constants.Script));

            if (xpRenderings != null && xpRenderings.Any())
            {                
                var sharedPath = "/sitecore/content/CW/childrens/Data/User Scripts/E-Learning Center";                  

                var xmcRootDatasource = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, sharedPath);

                if(xmcRootDatasource != null)
                {                    
                    foreach (var xpRendering in xpRenderings)
                    {                        
                        var xpDataSource = SitecoreUtility.GetDatasource(sourcePageItem, xpRendering.DatasourceID);
                        if (xpDataSource != null)
                        {                            

                            var localScriptsRootItemId = await CreateLocalScriptsFolder(newUrlPath, dataItemId);

                            var path = $"{newUrlPath}/Data/Scripts/{xpDataSource.Name}";
                            var parentId = localScriptsRootItemId;

                            if(xpDataSource.Path.Contains("/shared/"))
                            {
                                path = $"{sharedPath}/{xpDataSource.Name}";
                                parentId = xmcRootDatasource.ItemId;
                            }

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
                                    Parent = parentId,
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
        }

        private async Task<string> CreateLocalScriptsFolder(string newUrlPath, string dataItemId)
        {
            var itemId = string.Empty;            
            var rootFolder = await this.SitecoreGraphQLClient.QuerySingleItemAsync(_environment, _accessToken, $"{newUrlPath}/Data/Scripts");
            if (rootFolder == null)
            {
                var scriptsInputItem = new SitecoreCreateItemInput
                {
                    Language = "en",
                    Parent = dataItemId,
                    Name = "Scripts",
                    TemplateId = XMC_Template_Constants.UserScriptsFoldlder
                };

                var createdItems = await this.SitecoreGraphQLClient.CreateBulkItemsBatchedAsync(
                    new List<SitecoreCreateItemInput> { scriptsInputItem }, _environment, _accessToken);
                if(createdItems != null && createdItems.Any(x => !string.IsNullOrEmpty(x.ItemId)))
                    itemId = createdItems.FirstOrDefault().ItemId;
            }
            else
                itemId = rootFolder.ItemId;
            return itemId;
        }

        #endregion
    }
}
