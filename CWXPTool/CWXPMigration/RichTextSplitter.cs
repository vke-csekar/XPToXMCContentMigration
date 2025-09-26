using CWXPMigration.Models;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Sitecore.Globalization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CWXPMigration
{
    public class RichTextSplitter
    {
        public static string AddIdAttributeToAllH2(string html)
        {
            if (string.IsNullOrEmpty(html))
                return html;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var h2Nodes = doc.DocumentNode.SelectNodes("//h2");

            if (h2Nodes != null)
            {            
                int counter = 0;                

                foreach (var h2 in h2Nodes)
                {                    
                    string uniqueId = $"keypoint{counter}";                                                                                
                    h2.SetAttributeValue("id", uniqueId);
                    counter++;
                }
            }

            return doc.DocumentNode.OuterHtml;
        }

        public static async Task<string> RemapInternalSitecoreLinks(
            string html, ISitecoreGraphQLClient graphQLClient, string environment, string accessToken,
            List<PageMapping> allPageMappings)
        {
            if (string.IsNullOrEmpty(html))
                return html;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var regex = new Regex(@"_id=([A-F0-9]{32})", RegexOptions.IgnoreCase);

            var linkNodes = doc.DocumentNode.SelectNodes("//a[@href]");

            if (linkNodes == null)
                return html;

            foreach (var linkNode in linkNodes)
            {
                var href = linkNode.GetAttributeValue("href", "");
                
                if (!href.StartsWith("~/link.aspx", StringComparison.OrdinalIgnoreCase))
                    continue;

                var match = regex.Match(href);
                if (match.Success)
                {
                    string rawId = match.Groups[1].Value;
                    string xpItemId = SitecoreUtility.FormatGuid(rawId);
                    if (!string.IsNullOrEmpty(xpItemId))
                    {
                        var xpLinkedItem = Sitecore.Context.Database.GetItem(xpItemId);
                        
                        if (xpLinkedItem == null)
                            continue;

                        var xmcPath = xpLinkedItem.Paths.FullPath.Replace(Constants.SITECORE_XP_PRFIX, Constants.SITECORE_XMC_PRFIX);

                        if(string.IsNullOrEmpty(xmcPath))
                            continue;

                        string xmcItemId = string.Empty;

                        var xmcItem = await graphQLClient.QuerySingleItemAsync(environment, accessToken, xmcPath);
                        if (xmcItem != null)
                        {
                            xmcItemId = xmcItem.ItemId; 
                        }
                        else
                        {
                            // Try mapping table fallback
                            var matchedMapping = allPageMappings.FirstOrDefault(mapping =>
                                !string.IsNullOrEmpty(mapping.CURRENTURL) &&
                                PageMappingUtility.NormalizePath(mapping.CURRENTURL)
                                    .Equals(PageMappingUtility.NormalizePath(xpLinkedItem.Paths.FullPath),
                                        StringComparison.OrdinalIgnoreCase));

                            if (matchedMapping != null)
                            {
                                xmcItem = await graphQLClient.QuerySingleItemAsync(environment, accessToken, matchedMapping.NEWURLPATH);
                                if (xmcItem != null)
                                    xmcItemId = xmcItem.ItemId;
                            }
                        }
                        
                        if (!string.IsNullOrEmpty(xmcItemId))
                        {
                            var updatedHref = regex.Replace(href, $"_id={xmcItemId.ToUpperInvariant()}");
                            linkNode.SetAttributeValue("href", updatedHref);
                        }
                    }
                }
            }

            return doc.DocumentNode.OuterHtml;
        }

        public static async Task<string> RemapInternalSitecoreLinksV2(
            string html, ISitecoreGraphQLClient graphQLClient, string environment, string accessToken,
            List<MetaMapping> mappings)
        {
            if (string.IsNullOrEmpty(html))
                return html;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var regex = new Regex(@"_id=([A-F0-9]{32})", RegexOptions.IgnoreCase);

            var linkNodes = doc.DocumentNode.SelectNodes("//a[@href]");

            if (linkNodes == null)
                return html;

            foreach (var linkNode in linkNodes)
            {
                var href = linkNode.GetAttributeValue("href", "");

                if (!href.StartsWith("~/link.aspx", StringComparison.OrdinalIgnoreCase))
                    continue;

                var match = regex.Match(href);
                if (match.Success)
                {
                    string rawId = match.Groups[1].Value;
                    string xpItemId = SitecoreUtility.FormatGuid(rawId);
                    if (!string.IsNullOrEmpty(xpItemId))
                    {
                        var xpLinkedItem = Sitecore.Context.Database.GetItem(xpItemId);

                        if (xpLinkedItem == null)
                            continue;

                        var xmcPath = xpLinkedItem.Paths.FullPath.Replace(Constants.SITECORE_XP_PRFIX, Constants.SITECORE_XMC_PRFIX);

                        if (string.IsNullOrEmpty(xmcPath))
                            continue;

                        string xmcItemId = string.Empty;

                        var xmcItem = await graphQLClient.QuerySingleItemAsync(environment, accessToken, xmcPath);
                        if (xmcItem != null)
                        {
                            xmcItemId = xmcItem.ItemId;
                        }
                        else
                        {
                            var mapping = mappings.FirstOrDefault(x => x.CurrentUrl.Equals(xpLinkedItem.Paths.FullPath, StringComparison.OrdinalIgnoreCase));
                            if (mapping == null)
                                continue;
                           
                            if (mapping != null)
                            {
                                xmcItem = await graphQLClient.QuerySingleItemAsync(environment, accessToken, mapping.NewUrlPath);
                                if (xmcItem != null)
                                    xmcItemId = xmcItem.ItemId;
                            }
                        }

                        if (!string.IsNullOrEmpty(xmcItemId))
                        {
                            var updatedHref = regex.Replace(href, $"_id={xmcItemId.ToUpperInvariant()}");
                            linkNode.SetAttributeValue("href", updatedHref);
                        }
                    }
                }
            }

            return doc.DocumentNode.OuterHtml;
        }

        public static async Task<string> RemapInternalSitecoreLinksByTextAsync(
    string xpHtml, string xmcHtml,
    ISitecoreGraphQLClient graphQLClient,
    string environment, string accessToken,
    List<MetaMapping> mappings)
        {
            if (string.IsNullOrEmpty(xpHtml) || string.IsNullOrEmpty(xmcHtml))
                return xmcHtml;

            var xpDoc = new HtmlDocument();
            xpDoc.LoadHtml(xpHtml);

            var xmcDoc = new HtmlDocument();
            xmcDoc.LoadHtml(xmcHtml);

            var regex = new Regex(@"_id=([A-F0-9]{32})", RegexOptions.IgnoreCase);

            // get all XP anchors
            var xpLinks = xpDoc.DocumentNode.SelectNodes("//a[@href]") ?? new HtmlNodeCollection(null);
            // get all XMC anchors
            var xmcLinks = xmcDoc.DocumentNode.SelectNodes("//a[@href]") ?? new HtmlNodeCollection(null);

            foreach (var xpLink in xpLinks)
            {
                var xpHref = xpLink.GetAttributeValue("href", "");
                var xpText = xpLink.InnerText.Trim();

                if (!xpHref.StartsWith("~/link.aspx", StringComparison.OrdinalIgnoreCase))
                    continue;

                var match = regex.Match(xpHref);
                if (!match.Success)
                    continue;

                string rawId = match.Groups[1].Value;
                string xpItemId = SitecoreUtility.FormatGuid(rawId);
                if (string.IsNullOrEmpty(xpItemId))
                    continue;

                // Resolve mapping
                var xpLinkedItem = Sitecore.Context.Database.GetItem(xpItemId);
                if (xpLinkedItem == null)
                    continue;

                var mapping = mappings.FirstOrDefault(x =>
                    x.CurrentUrl.Equals(xpLinkedItem.Paths.FullPath, StringComparison.OrdinalIgnoreCase)
                );
                if (mapping == null)
                    continue;

                var xmcPath = mapping.NewUrlPath;
                if (string.IsNullOrEmpty(xmcPath))
                    continue;

                // get the correct XMC id
                var xmcItem = await graphQLClient.QuerySingleItemAsync(environment, accessToken, xmcPath);
                if (xmcItem == null)
                    continue;

                var xmcItemId = xmcItem.ItemId;
                if (string.IsNullOrEmpty(xmcItemId))
                    continue;

                // ✅ Now find the matching link in XMC by text
                var targetXmcLink = xmcLinks
                    .FirstOrDefault(x => x.InnerText.Trim().Equals(xpText, StringComparison.OrdinalIgnoreCase));

                if (targetXmcLink != null)
                {
                    var oldHref = targetXmcLink.GetAttributeValue("href", "");
                    var newHref = regex.Replace(oldHref, $"_id={xmcItemId.ToUpperInvariant()}");
                    targetXmcLink.SetAttributeValue("href", newHref);
                }
            }

            var finalHtml = xmcDoc.DocumentNode.OuterHtml;
            
            return finalHtml;
        }


        public static string Change_CHWH2_H4(string html)
        {
            if (string.IsNullOrEmpty(html))
                return html;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Only select <h2> with class="chw-h2"
            var h2Nodes = doc.DocumentNode.SelectNodes("//h2[@class='chw-h2']");

            if (h2Nodes != null)
            {
                foreach (var h2 in h2Nodes)
                {
                    // Create a new h4 element
                    var h4 = HtmlNode.CreateNode("<h4></h4>");

                    // Copy all attributes from h2 to h4
                    foreach (var attr in h2.Attributes)
                    {
                        h4.Attributes.Add(attr.Name, attr.Value);
                    }

                    // Preserve the inner HTML
                    h4.InnerHtml = h2.InnerHtml;

                    // Replace h2 with h4 in the DOM
                    h2.ParentNode.ReplaceChild(h4, h2);
                }
            }

            return doc.DocumentNode.OuterHtml;
        }


        public static List<RichTextSection> SplitByH2(string html, string language)
        {
            if (string.IsNullOrWhiteSpace(html))
                return new List<RichTextSection>();

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            //var bodyNodes = doc.DocumentNode.SelectNodes("//body/*") ?? doc.DocumentNode.ChildNodes;
            var allNodes = doc.DocumentNode.SelectNodes("//*") ?? doc.DocumentNode.ChildNodes;

            var sections = new List<RichTextSection>();
            HtmlNode currentHeader = null;
            var currentContentNodes = new List<HtmlNode>();
            var tempNodes = new List<HtmlNode>();
            bool foundFirstH2 = false;

            foreach (var node in allNodes)
            {
                if (IsH2(node))
                {
                    if (!foundFirstH2)
                    {
                        // Add content before first <h2>
                        AddSectionIfReady(sections, null, tempNodes, language);
                        foundFirstH2 = true;
                    }
                    else
                    {
                        // Add previous section
                        AddSectionIfReady(sections, currentHeader, currentContentNodes, language);
                    }

                    currentHeader = node;
                    currentContentNodes = new List<HtmlNode>();
                }
                else
                {
                    if (!foundFirstH2)
                    {
                        //tempNodes.Add(node); // content before first <h2>
                        if (node.ParentNode == (doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode))
                            tempNodes.Add(node); // content before first <h2>
                    }
                    else if (currentHeader != null)
                    {
                        //currentContentNodes.Add(node);
                        if (node.ParentNode == currentHeader.ParentNode)
                            currentContentNodes.Add(node);
                    }
                }
            }

            // Add last section
            AddSectionIfReady(sections, currentHeader, currentContentNodes, language);

            // Handle content when no <h2> is found at all
            if (!foundFirstH2 && tempNodes.Count > 0)
            {
                sections.Add(new RichTextSection
                {
                    Title = null,
                    Language = language,
                    HtmlContent = string.Join("", tempNodes.Select(n => n.OuterHtml))
                });
            }

            return sections;
        }

        private static void AddSectionIfReady(List<RichTextSection> sections, HtmlNode header, List<HtmlNode> content, string language)
        {
            if (content.Count == 0 && header == null)
                return;

            var section = new RichTextSection
            {                
                Title = header != null ? WebUtility.HtmlDecode(header.InnerText.Trim()) : null,
                HtmlContent = string.Join("", content.Select(n => n.OuterHtml)),
                Language = language,
            };
            sections.Add(section);
        }

        private static bool IsH2(HtmlNode node)
        {
            return node != null && node.Name.Equals("h2", System.StringComparison.OrdinalIgnoreCase);
        }

        public static string ConvertMediaToRteHtml(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return string.Empty;

            var doc = new HtmlDocument();
            doc.LoadHtml(rawValue);

            var imageNode = doc.DocumentNode.SelectSingleNode("//image");
            if (imageNode == null)
                return string.Empty;

            // Extract attributes
            string mediaId = imageNode.GetAttributeValue("mediaid", string.Empty).Replace("{", "").Replace("}", "");
            string alt = imageNode.GetAttributeValue("alt", string.Empty);

            // Build Sitecore media URL format
            string mediaUrl = $"-/media/{NormalizeMediaId(mediaId)}.ashx";

            // Construct RTE style HTML
            string rteHtml = $@"<div class=""ck-content""><figure class=""image""><img src=""{mediaUrl}"" alt=""{alt}"" /></figure></div>";

            return rteHtml;
        }

        public static string NormalizeMediaId(string rawMediaId)
        {
            if (string.IsNullOrWhiteSpace(rawMediaId))
                return string.Empty;

            // Parse the GUID safely
            if (Guid.TryParse(rawMediaId.Trim('{', '}'), out Guid guid))
            {
                // "N" format = 32 digits, no hyphens
                return guid.ToString("N").ToUpper();
            }

            return string.Empty;
        }
    }
}
