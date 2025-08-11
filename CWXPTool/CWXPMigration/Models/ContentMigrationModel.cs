using Sitecore.Data;
using System;
using System.Collections.Generic;

namespace CWXPMigration.Models
{
    public class TemplateFieldMapping
    {
        public string TemplateId { get; set; }            // XP Template ID
        public string XMTemplateId { get; set; }          // XM Cloud Template ID
        public List<FieldMapping> FieldMappings { get; set; } = new List<FieldMapping>();
    }
    public class FieldMapping
    {
        public string XPField { get; set; }         // Field name in XP
        public string XMField { get; set; }         // Field name in XM Cloud
        public string XPType { get; set; }           // Optional: type like "Rich Text", "Single-Line Text"
    }
    public class TemplateFieldModel
    {
        public string Name { get; set; }
        public string Type { get; set; }
    }
    public class TemplateSummaryModel
    {
        public string TemplateId { get; set; }
        public string TemplateName { get; set; }
        public List<TemplateFieldModel> Fields { get; set; }
    }
    public class SyncContentResponse
    {
        public bool Success { get; set; } = true;
        public string SyncedItemPath { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public string Message { get; set; }
    }
    public class RenderingInfo
    {
        public string RenderingName { get; set; }
        public string DatasourceID { get; set; }
        public string RenderingId { get; set; }
        public string Placeholder { get; set; }        
        public string Parameters { get; set; }
        public string DeviceId { get; set; }
    }

    public class RenderingDetail
    {
        public string ID { get; set; }
        public string DisplayName { get; set; }
        public string Path { get; set; }
    }

    public class DataSourceDetail
    {
        public string ID { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public List<XPField> Fields { get; set; } = new List<XPField>();
    }

    public class ContentMigrationModel
    {
        public string ItemPath { get; set; }
        public string XMCItemPath { get; set; }
        public string Environment { get; set; }
        public bool Reimport { get; set; }
        public List<PageDataModel> Items { get; set; }
    }

    public class XPField
    {
        public XPField(Sitecore.Data.Fields.Field field)
        {
            Field = field;
        }
        public string Name { get; set; }
        public string Value { get; set; }
        public string Type { get; set; }
        public Sitecore.Data.Fields.Field Field { get; set; }
    }

    public class PageDataModel
    {
        public ID ItemID { get; set; }
        public ID TemplateID { get; set; }
        public string Page { get; set; }
        public List<XPField> Fields { get; set; } = new List<XPField>();
        public List<RenderingInfo> Renderings { get; set; } = new List<RenderingInfo>();
        public List<DataSourceDetail> DataSources { get; set; } = new List<DataSourceDetail>();
    }

    public class PageMapping
    {
        public string CURRENTURL { get; set; }
        public string NEWURLPATH { get; set; }
        public string PAGETEMPLATE { get; set; }
        public string PAGETEMPLATEID { get; set; }
        public string NetNewCopy { get; set; }
        public string RedirectEntry { get; set; }
    }

    public class AuthResponse
    {
        [Newtonsoft.Json.JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [Newtonsoft.Json.JsonProperty("token_type")]
        public string TokenType { get; set; }

        [Newtonsoft.Json.JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }
    }

    public class SitecoreFieldInput
    {
        public string Name { get; set; }
        public object Value { get; set; }
    }

    public class SitecoreCreateItemInput
    {
        public string Name { get; set; }
        public string TemplateId { get; set; }
        public string Parent { get; set; }
        public string Language { get; set; }
        public List<SitecoreFieldInput> Fields { get; set; }
    }

    public class SitecoreUpdateItemInput
    {
        public string ItemId { get; set; }
        public string Language { get; set; }
        public List<SitecoreFieldInput> Fields { get; set; }
    }

    public class SitecoreItem
    {
        public string ItemId { get; set; }
        public string Path { get; set; }
        public string Name { get; set; }
    }

    public class XPLookUpItem : SitecoreItemBase
    {
        public List<SitecoreFieldInput> Fields { get; set; }
    }

    public class XPBlogResourceItem : SitecoreItemBase
    {
        public string Description { get; set; }
    }

    public class GraphQLQuery
    {
        [Newtonsoft.Json.JsonProperty("query")]
        public string Query { get; set; }
    }

    public class SitecoreItemInput
    {
        public string Name { get; set; }
        public string TemplateId { get; set; }
        public string ParentPath { get; set; }
        public Dictionary<string, object> Fields { get; set; }
    }

    public class CreatedItem
    {
        public string ItemId { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string Language { get; set; }
        public List<FieldValue> Fields { get; set; }
    }

    public class FieldValue
    {
        public string Name { get; set; }
        public object Value { get; set; }
    }

    public class SitecoreUpdatedItem
    {
        public string ItemId { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string Language { get; set; }
    }

    public class RichTextSection
    {
        public string Title { get; set; }
        public string HtmlContent { get; set; }
        public string Language { get; set; }
    }

    public class SitecoreItemBase
    {
        public string ItemId { get; set; }
        public string Path { get; set; }
        public string ItemName { get; set; }
    }

    public class PageInfo
    {
        public string EndCursor { get; set; }
        public bool HasNextPage { get; set; }
    }

    public class QueryItemsResult<T> : SitecoreItemBase
    {
        public List<T> Items { get; set; }
        public PageInfo PageInfo { get; set; }
    }

    public class SitecoreSideNavSection : SitecoreItemBase
    {
        public FieldValue Heading { get; set; }
        public FieldValue SideNavHeading { get; set; }
        public FieldValue SubHeading { get;  set; }
    }
}