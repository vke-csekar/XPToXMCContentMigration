using CWXPMigration.Models;
using Sitecore.Data;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.Globalization;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CWXPMigration
{
    public static class SitecoreUtility
    {
        public static SitecoreCreateItemInput GetSitecoreCreateItemInput(string itemName, string templateId, string parentId, string fieldName, string fieldValue)
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

        public static DataSourceDetail GetDatasource(PageDataModel sourcePageItem, string datasourceId)
        {
            return sourcePageItem.DataSources.FirstOrDefault(x => x.ID.Equals(datasourceId));
        }

        public static XPField GetRichTextField(DataSourceDetail datasource)
        {
            return datasource.Fields.FirstOrDefault(f =>
                !string.IsNullOrEmpty(f.Value) &&
                f.Type.Equals("Rich Text", System.StringComparison.OrdinalIgnoreCase));
        }

        public static XPField GetRichTextField(Item datasource)
        {            
            var field = datasource.Fields.FirstOrDefault(f =>
                !string.IsNullOrEmpty(f.Value) &&
                f.Type.Equals("Rich Text", System.StringComparison.OrdinalIgnoreCase));
            if(field != null)
            {
                var xpField = new XPField(field);
                xpField.Name = field.Name;
                xpField.Value = field.Value;
                xpField.Type = field.Type;
                return xpField;
            }
            return null;
        }

        public static SitecoreFieldInput GetSitecoreFieldInput(DataSourceDetail datasource, string xpFieldName, string xmcFieldName)
        {
            var fieldValue = datasource.Fields.FirstOrDefault(x => x.Name == xpFieldName && !string.IsNullOrEmpty(x.Value))?.Value ?? string.Empty;
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

        public static string FormatGuid(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || itemId.Length != 32 || !System.Text.RegularExpressions.Regex.IsMatch(itemId, @"^[0-9A-Fa-f]{32}$"))
            {
                throw new ArgumentException("Invalid 32-character hex string.");
            }

            var guid = string.Format("{0}-{1}-{2}-{3}-{4}",
                itemId.Substring(0, 8),
                itemId.Substring(8, 4),
                itemId.Substring(12, 4),
                itemId.Substring(16, 4),
                itemId.Substring(20));

            return $"{{{guid.ToUpperInvariant()}}}";
        }

        public static SitecoreFieldInput GetSitecoreFieldInput(Item sourcePageItem, string xpFieldName, string xmcFieldName)
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

        public static PageDataModel RemoveRenderingDatasources(PageDataModel sourcePageItem, IEnumerable<RenderingInfo> renderings, string renderingName)
        {
            if (renderings == null || !renderings.Any()) return sourcePageItem;

            var filteredRenderings = renderings.Where(rendering => rendering.RenderingName.Contains(renderingName));

            if (filteredRenderings == null || !filteredRenderings.Any()) return sourcePageItem;

            sourcePageItem.DataSources.RemoveAll(x => filteredRenderings.Any(y => x.ID.Equals(y.DatasourceID, StringComparison.OrdinalIgnoreCase)));

            return sourcePageItem;
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


        #region XP To XMC Mappings
        /// <summary>
        /// Retrieves parent and child pages from Sitecore XP for the specified path.
        /// For each page, collects common page fields, predefined renderings, and their datasources.
        /// Consolidates the data into a structured object and saves it as a JSON file (Just logging/troubleshooting).
        /// </summary>
        /// <param name="itemPath">The Sitecore path of the parent item to start data retrieval from.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public static List<PageDataModel> GetPagesAndRelatedDataFromXP(List<Item> pages, Database database)
        {
            List<PageDataModel> xpPageDataItems = new List<PageDataModel>();

            foreach (var pageItem in pages)
            {
                Sitecore.Diagnostics.Log.Info($"{pageItem.ID}|{pageItem.Language.Name}", typeof(SitecoreUtility));
                var pageData = SitecoreUtility.ExtractPageData(pageItem, database);
                xpPageDataItems.Add(pageData);
            }

            return xpPageDataItems;
        }

        public static PageDataModel ExtractPageData(Sitecore.Data.Items.Item pageItem, Database database)
        {
            var pageModel = new PageDataModel { Page = pageItem.Paths.FullPath };
            pageModel.ItemID = pageItem.ID;
            pageModel.TemplateID = pageItem.TemplateID;
            foreach (Field field in pageItem.Fields)
            {
                if (!field.Name.StartsWith("__") && !pageModel.Fields.Any(x => x.Name.Equals(field.Name)))
                {
                    var xpField = new XPField(field);
                    xpField.Name = field.Name;
                    xpField.Value = field.Value;
                    xpField.Type = field.Type;
                    Sitecore.Diagnostics.Log.Info($"Field:{xpField.Name}|Value:{xpField.Value}", typeof(SitecoreUtility));
                    pageModel.Fields.Add(xpField);
                }
            }

            var uniqueDataSourceIds = new HashSet<string>();

            var renderingInfos = SitecoreUtility.GetRenderingsForCurrentDevice(pageItem);

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
                            var xpField = new XPField(field);
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
        #endregion

        public static Item GetItemByLanguage(string itemPathOrID, string language, Database database)
        {
            Item item = Sitecore.Data.ID.IsID(itemPathOrID)
                ? database.GetItem(ID.Parse(itemPathOrID))
                : database.GetItem(itemPathOrID);

            if (item == null)
            {
                Sitecore.Diagnostics.Log.Warn($"Item not found: {itemPathOrID}", typeof(SitecoreUtility));
                return null;
            }

            if (language.Equals("en", StringComparison.OrdinalIgnoreCase))
                return item;

            Language lang = Language.Parse(language);

            Item langItem = database.GetItem(item.ID, lang);

            if (langItem == null || langItem.Versions == null && langItem.Versions.Count <= 0)
            {
                Sitecore.Diagnostics.Log.Info($"No version for language: {lang.Name}", typeof(SitecoreUtility));
                return langItem;
            }

            return langItem;            
        }

    }
}
