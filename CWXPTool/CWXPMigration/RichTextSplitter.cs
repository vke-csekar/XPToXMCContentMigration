using CWXPMigration.Models;
using HtmlAgilityPack;
using Sitecore.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;

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

        public static List<RichTextSection> SplitByH2(string html, string language)
        {
            if (string.IsNullOrWhiteSpace(html))
                return new List<RichTextSection>();

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var bodyNodes = doc.DocumentNode.SelectNodes("//body/*") ?? doc.DocumentNode.ChildNodes;

            var sections = new List<RichTextSection>();
            HtmlNode currentHeader = null;
            var currentContentNodes = new List<HtmlNode>();
            var tempNodes = new List<HtmlNode>();
            bool foundFirstH2 = false;

            foreach (var node in bodyNodes)
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
                        tempNodes.Add(node); // content before first <h2>
                    }
                    else if (currentHeader != null)
                    {
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
    }
}
