using CWXPMigration.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CWXPMigration
{
    public class PageMappingUtility
    {
        public static List<PageMapping> SortPages(List<PageMapping> pages)
        {
            // Sort based on itemPath segments
            pages.Sort((a, b) =>
            {
                var pathA = a.NEWURLPATH.Split(new string[] { "/" }, StringSplitOptions.RemoveEmptyEntries);
                var pathB = b.NEWURLPATH.Split(new string[] { "/" }, StringSplitOptions.RemoveEmptyEntries);

                int minLength = Math.Min(pathA.Length, pathB.Length);
                for (int i = 0; i < minLength; i++)
                {
                    int comparison = string.Compare(pathA[i], pathB[i], StringComparison.OrdinalIgnoreCase);
                    if (comparison != 0)
                        return comparison;
                }
                return pathA.Length.CompareTo(pathB.Length); // shorter path comes first
            });

            return pages;
        }

        public static List<PageMapping> LoadPageMappingsFromJson()
        {
            var pageMappings = JsonConvert.DeserializeObject<List<PageMapping>>(System.IO.File.ReadAllText("F:\\Migration\\CWPageMapping.json"));
            if (pageMappings != null && pageMappings.Any())
            {
                pageMappings.RemoveAll(x => string.IsNullOrEmpty(x.CURRENTURL) || string.IsNullOrEmpty(x.NEWURLPATH));
                pageMappings.ForEach(x =>
                {

                    // Handles CURRENTURL (with prefix).
                    // The CURRENTURL value from Excel contains a full URL; this extracts only the corresponding Sitecore content path.
                    // Cleans the URL by removing any encoded or unwanted special characters.
                    x.CURRENTURL = GetSitecorePathFromUrl(x.CURRENTURL, x.PAGETEMPLATEID, Constants.SITECORE_XP_PRFIX);                    
                    // Handles NEWURLPATH (with prefix).
                    // The NEWURLPATH value from Excel contains a partial path of Sitecore item; this appends XMC root paths.
                    // Cleans the URL by removing any encoded or unwanted special characters.
                    x.NEWURLPATH = GetSitecorePathFromUrl(x.NEWURLPATH, x.PAGETEMPLATEID, Constants.SITECORE_XMC_ROOT_PATH + "/Home/");                    
                });
            }
            return pageMappings;
        }

        public static string GetSitecorePathFromUrl(string url, string pageTemplateId, string prefix = "")
        {
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            string path = url;
            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var uri = new Uri(url);
                    path = Uri.UnescapeDataString(uri.AbsolutePath);
                }
                catch (UriFormatException ex)
                {
                    Sitecore.Diagnostics.Log.Error(ex.Message, ex, typeof(PageMappingUtility));
                }
            }

            string normalizedPath = pageTemplateId.Equals(XMC_Page_Template_Constants.Teaching_Sheets) ? path.TrimStart('/') : path.TrimStart('/').Replace("-", " ");
            return string.IsNullOrEmpty(prefix) ? normalizedPath : prefix + normalizedPath;
        }
    }
}
