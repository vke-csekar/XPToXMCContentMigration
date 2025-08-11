using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using CWXPMigration;
using CWXPMigration.Models;
using Newtonsoft.Json;
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Web;

public class XMCPages
{
    public List<string>? Blogs { get; set; }
}
public class RedirectRule
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public bool IsRegex { get; set; }
    public int StatusCode { get; set; }
    public string RedirectEntry { get; set; } = string.Empty;
    public string PageType { get; set; } = string.Empty;
}
class Program
{
    const string _blogIdentifier = "/at-every-turn/stories/";
    const string _locationIdentifier = "/location-directory/locations/";
    const string _medicalCareIdentifier = "/medical-care/";
    const string _physicianIdentifier = "/physician-directory";
    const string _medicalProfessionalsIdentifier = "/medical-professionals/";
    const string _patientsAndFamiliesIdentifier = "/patients-and-families/";
    const string _aboutIdentifier = "/about/";
    const string _cwCommunityIdentifier = "/childrens-and-the-community/";
    const string _waysToHelpIdentifier = "/ways-to-help/";
    const string _careersIdentifier = "/careers/"; 
    const string _digitalCareIdentifier = "/digital-care/";
    static readonly SitecoreGraphQLClient sitecoreGraphQLClient = new SitecoreGraphQLClient();
    static void Main()
    {
        RunAsync().GetAwaiter().GetResult();
    }
    static async Task RunAsync()
    {
        var regexRedirectRules = new List<RedirectRule>();
        var rawMappings = GetMappingItemsFromXL();
        if (rawMappings?.Any() != true) return;

        // Normalize current and new URL paths
        foreach (var mapping in rawMappings)
        {
            mapping.CURRENTURL = GetAbsolutePathFromUrl(mapping.CURRENTURL);
            mapping.NEWURLPATH = GetAbsolutePathFromUrl(mapping.NEWURLPATH);
        }

        // Sort all mappings
        var sortedMappings = PageMappingUtility.SortPages(rawMappings);
        if (sortedMappings?.Any() != true) return;

        sortedMappings = sortedMappings.DistinctBy(m => m.CURRENTURL).ToList();
        if (sortedMappings?.Any() != true) return;        

        //Home redirect
        //redirectRules.Add(GetRedirectRule(
        //        source: @"/home/",
        //        target: "/",
        //        isRegex: false,
        //        statusCode: 301));

        //Teaching-Sheet Redirects
        var redirectTeachingSheetEntryEn = GetRedirectRule(
                source: @"^/Publications/Teaching-Sheet/(.*)/(\d+-.*)$",
                target: "/teaching-sheet/$1/$2",
                isRegex: true,
                statusCode: 301);
        regexRedirectRules.Add(redirectTeachingSheetEntryEn);

        //Teaching-Sheet Spanish Redirects
        var redirectTeachingSheetEntryEs = GetRedirectRule(
            source: @"^/es-es/Publications/Teaching-Sheet/(.*)/(\d+-.*)$",
            target: "/es-es/teaching-sheet/$1/$2",
            isRegex: true,
            statusCode: 301);
        regexRedirectRules.Add(redirectTeachingSheetEntryEs);

        regexRedirectRules.Add(GetRedirectRule(
                            source: @"^/physician-directory",
                            target: "/providers",
                            isRegex: true,
                            statusCode: 301));

        //Provider redirects
        regexRedirectRules.Add(GetRedirectRule(
                            source: @"^/physician-directory/([a-zA-Z0-9-]+)/([^/]+)$",
                            target: "/providers/$1/$2",
                            isRegex: true,
                            statusCode: 301));


        var blogRedirectRules = new List<RedirectRule>();

        // Filter XP blog pages
        var xpBlogMappings = sortedMappings
            .Where(m => m.CURRENTURL.StartsWith(_blogIdentifier, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (xpBlogMappings.Any())
        {
            // Get all blog page paths from XMC
            var xmcBlogFullPaths = GetXMCBlogPageList();
            var xmcRelativePaths = xmcBlogFullPaths
                .Select(p => p.Replace($"{Constants.SITECORE_XMC_ROOT_PATH}/Home", ""))
                .ToList();

            // Match XP blog slugs with XMC blog slugs
            foreach (var xpMapping in xpBlogMappings)
            {
                var currentSlug = GetLastSegment(xpMapping.CURRENTURL);

                var matchingNewUrls = xmcRelativePaths
                    .Where(newUrl => string.Equals(GetLastSegment(newUrl), currentSlug, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                xpMapping.NEWURLPATH = matchingNewUrls.FirstOrDefault();

                if (!string.IsNullOrEmpty(xpMapping.NEWURLPATH) && !string.Equals(xpMapping.CURRENTURL, xpMapping.NEWURLPATH, StringComparison.OrdinalIgnoreCase))
                {
                    blogRedirectRules.Add(GetRedirectRule(
                        source: $"{xpMapping.CURRENTURL}",
                        target: $"{xpMapping.NEWURLPATH}",
                        isRegex: false,
                        statusCode: 301));
                }
            }
        }

        var regexRedirects = regexRedirectRules.Where(rule => rule.IsRegex).Select(x => x.RedirectEntry);
        var regexRedirectRawValue = string.Join("&", regexRedirects);

        var blogRedirects = blogRedirectRules.Select(x => x.RedirectEntry);
        var blogRedirectsRawValue = string.Join("&", blogRedirects);

        var xpLocationsRedirects = GetRedirectRules(sortedMappings, _locationIdentifier);        
        var xpLocationsRedirectRawValue = string.Join("&", xpLocationsRedirects.Select(x => x.RedirectEntry));

        var xpMedicalCareRedirects = GetRedirectRules(sortedMappings, _medicalCareIdentifier);        
        var xpMedicalCareRedirectRawValue = string.Join("&", xpMedicalCareRedirects.Select(x => x.RedirectEntry));

        var xpMedicalProfessionalsRedirects = GetRedirectRules(sortedMappings, _medicalProfessionalsIdentifier);        
        var xpMedicalProfessionalsRedirectRawValue = string.Join("&", xpMedicalProfessionalsRedirects.Select(x => x.RedirectEntry));

        var xpPatientsAndFamiliesRedirects = GetRedirectRules(sortedMappings, _patientsAndFamiliesIdentifier);        
        var xpPatientsAndFamiliesRedirectRawValue = string.Join("&", xpPatientsAndFamiliesRedirects.Select(x => x.RedirectEntry));

        var xpAboutRedirects = GetRedirectRules(sortedMappings, _aboutIdentifier);        
        var xpAboutRedirectRawValue = string.Join("&", xpAboutRedirects.Select(x => x.RedirectEntry));

        var xpCommunityRedirects = GetRedirectRules(sortedMappings, _cwCommunityIdentifier);
        var xpCommunityRedirectRawValue = string.Join("&", xpCommunityRedirects.Select(x => x.RedirectEntry));

        var xpWaysToHelpRedirects = GetRedirectRules(sortedMappings, _waysToHelpIdentifier);
        var xpWaysToHelpRedirectRawValue = string.Join("&", xpWaysToHelpRedirects.Select(x => x.RedirectEntry));

        var xpCareersRedirects = GetRedirectRules(sortedMappings, _careersIdentifier);
        var xpCareersRedirectRawValue = string.Join("&", xpCareersRedirects.Select(x => x.RedirectEntry));

        var xpDigitalCareRedirects = GetRedirectRules(sortedMappings, _digitalCareIdentifier);
        var xpDigitalCareRedirectRawValue = string.Join("&", xpDigitalCareRedirects.Select(x => x.RedirectEntry));

    }

    static List<RedirectRule> GetRedirectRules(List<PageMapping> sortedMappings, string identifier)
    {
        List<RedirectRule> redirectRules = new List<RedirectRule>();
        var xpMedicalCareMappings = sortedMappings
            .Where(m => m.CURRENTURL.StartsWith(identifier, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (xpMedicalCareMappings.Any())
        {
            foreach (var xpMapping in xpMedicalCareMappings)
            {
                if (!string.IsNullOrEmpty(xpMapping.NEWURLPATH) && !string.Equals(xpMapping.CURRENTURL, xpMapping.NEWURLPATH, StringComparison.OrdinalIgnoreCase))
                {
                    xpMapping.NEWURLPATH = xpMapping.NEWURLPATH.Replace(" ", "-");
                    if (!xpMapping.NEWURLPATH.StartsWith("/"))
                        xpMapping.NEWURLPATH = $"/{xpMapping.NEWURLPATH}";
                    redirectRules.Add(GetRedirectRule(
                        source: $"{xpMapping.CURRENTURL}",
                        target: $"{xpMapping.NEWURLPATH}",
                        isRegex: false,
                        statusCode: 301));
                }
            }
        }
        
        return redirectRules;
    }

    static string GetRedirectEntry(string currentPath, string newUrlPath)
    {
        return $"{HttpUtility.UrlEncode(currentPath)}={HttpUtility.UrlEncode(newUrlPath)}";
    }

    static string GetLastSegment(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        var segments = url.TrimEnd('/').Split('/');
        return segments.Last();
    }

    static async Task<QueryItemsResult<SitecoreItemBase>> QueryItemsAsync(string accessToken, string path)
    {
        return await sitecoreGraphQLClient.QueryItemsAsync<SitecoreItemBase>(string.Empty, accessToken, path,
            new List<string>(),
            jObj => new SitecoreItemBase
            {
                ItemId = jObj["itemId"]?.ToString(),
                Path = jObj["path"]?.ToString(),
                ItemName = jObj["itemName"]?.ToString()
            });
    }

    static string QuoteIfNeeded(string input)
    {
        return input.Contains(",") || input.Contains("\"")
            ? "\"" + input.Replace("\"", "\"\"") + "\""
            : input;
    }

    static IEnumerable<string> GetXMCBlogPageList()
    {
        string xmcPagePath = @"C:\Projects\CW\CWXPTool\Migration Files\XMCPages.json";

        var fileContents = File.ReadAllText(xmcPagePath);

        if (!string.IsNullOrEmpty(fileContents))
        {
            var obj = JsonConvert.DeserializeObject<XMCPages>(fileContents);

            if (obj != null)
            {
                return obj.Blogs ?? Enumerable.Empty<string>();
            }
        }

        return Enumerable.Empty<string>();
    }

    static List<PageMapping> GetMappingItemsFromXL()
    {
        string excelPath = @"C:\Projects\CW\CWXPTool\Migration Files\Content Migration Mapping Basic Template.xlsx";

        var mappingItems = new List<PageMapping>();

        using var workbook = new XLWorkbook(excelPath);

        foreach (var sheet in workbook.Worksheets)
        {
            var usedRange = sheet.RangeUsed();
            if (usedRange == null || usedRange.RowCount() < 2)
            {
                Console.WriteLine($"⏭️ Skipping empty or invalid sheet: {sheet.Name}");
                continue;
            }

            using var writer = new StringWriter();
            foreach (var row in usedRange.Rows())
            {
                var values = row.Cells().Select(c => QuoteIfNeeded(c.GetString().Trim()));
                writer.WriteLine(string.Join(",", values));
            }

            var csvData = new StringReader(writer.ToString());

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                TrimOptions = TrimOptions.Trim,
                IgnoreBlankLines = true,
                BadDataFound = null,
                MissingFieldFound = null,
                HeaderValidated = null
            };

            using var csv = new CsvReader(csvData, config);

            try
            {
                if (!csv.Read() || !csv.ReadHeader())
                {
                    Console.WriteLine($"⚠️ No header in sheet: {sheet.Name}");
                    continue;
                }

                while (csv.Read())
                {
                    var item = new PageMapping
                    {
                        CURRENTURL = csv.GetField("CURRENT URL")?.Replace("/Home/", "/") ?? "",
                        NEWURLPATH = csv.GetField("NEW URL PATH") ?? "",
                    };

                    if (string.IsNullOrWhiteSpace(item.CURRENTURL) ||
                        !item.CURRENTURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Skip invalid/junk rows
                    if (item.CURRENTURL.Contains(_physicianIdentifier))
                        item.NEWURLPATH = item.CURRENTURL;

                    // Skip invalid/junk rows
                    if (string.IsNullOrWhiteSpace(item.NEWURLPATH))
                        continue;

                    mappingItems.Add(item);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error reading sheet '{sheet.Name}': {ex.Message}");
            }
        }
        return mappingItems;
    }


    static string GetAbsolutePathFromUrl(string url)
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
                path.Replace("/Home/", "/");
            }
            catch (UriFormatException ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        return path;
    }

    private static RedirectRule GetRedirectRule(string source, string target, bool isRegex, int statusCode)
    {
        target = !isRegex ? UnescapeDataString(target).ToLower() : target;
        return new RedirectRule()
        {
            Source = source,
            Target = target,
            IsRegex = isRegex,
            StatusCode = statusCode,
            RedirectEntry = GetRedirectEntry(source, target)            
        };
    }    

    private static string UnescapeDataString(string target)
    {
        var segments = target.Split(new char[] { '/' });
        if (segments.Length > 0)
        {
            segments = segments.Select(s => s.Trim()).ToArray();
            return string.Join("/", segments);
        }
        return target;
    }
}
