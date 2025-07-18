using Sitecore.Data;
using System.Collections.Generic;

namespace CWXPMigration.Models
{
    public class RenderingInfo
    {
        public string RenderingName { get; set; }
        public string DatasourceID { get; set; }
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
        public List<PageDataModel> Items { get; set; }
    }

    public class XPField
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Type { get; set; }
    }

    public class PageDataModel
    {
        public ID ItemID { get; set; }
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