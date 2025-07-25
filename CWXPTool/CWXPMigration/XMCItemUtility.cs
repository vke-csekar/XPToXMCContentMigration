using CWXPMigration.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CWXPMigration
{
    public static class XMCItemUtility
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
    }
}
