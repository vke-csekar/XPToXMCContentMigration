using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using Newtonsoft.Json;

class PageItem
{
    public string CurrentUrl { get; set; } = "";
    public string NewUrlPath { get; set; } = "";
    public string PageTemplate { get; set; } = "";
    public string NetNewCopy { get; set; } = "";
    public string PageTemplateId { get; set; } = "";
    public string CurrentMetaDescription { get; set; } = "";
    public string RecommendedMetaDescription { get; set; } = "";
    public string CurrentMetaTitle { get; set; } = "";
    public string RecommendedMetaTitle { get; set; } = "";
}

class Program
{
    static void Main()
    {
        Main_Content_Migration();
    }
    static void Meta_Migration()
    {
        string excelPath = @"C:\Projects\CW\CWXPTool\Migration Files\MetadataMigrationList.xlsx";
        string outputJsonPath = Path.Combine(Path.GetDirectoryName(excelPath)!, "CWMetadataMapping.json");

        var xpPathList = File.ReadAllLines(@"C:\Projects\CW\CWXPTool\Migration Files\XP_Path_Item_Paths.txt")?.ToList();
        var xmcPathList = File.ReadAllLines(@"C:\Projects\CW\CWXPTool\Migration Files\XMC_Path_Item_Paths.txt")?.ToList();

        if (xpPathList == null || xmcPathList == null)
            return;

        var allItems = new List<PageItem>();        

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
                // Normalization helper
                string Normalize(string path) => path?.Replace(" ", "-")?.ToLowerInvariant() ?? string.Empty;

                if (!csv.Read() || !csv.ReadHeader())
                {
                    Console.WriteLine($"⚠️ No header in sheet: {sheet.Name}");
                    continue;
                }

                while (csv.Read())
                {
                    var item = new PageItem
                    {
                        CurrentUrl = csv.GetField("CURRENT URL")?.Replace("/Home/", "/") ?? "",                        
                        CurrentMetaTitle = csv.GetField("CURRENT TITLE TAG") ?? "",
                        RecommendedMetaTitle = csv.GetField("RECOMMENDED TITLE TAG") ?? "",
                        CurrentMetaDescription = csv.GetField("CURRENT META DESCRIPTION") ?? "",
                        RecommendedMetaDescription = csv.GetField("RECOMMENDED META DESCRIPTION") ?? "",
                    };

                    if(item.CurrentUrl.Contains("/Teaching-Sheet/", StringComparison.OrdinalIgnoreCase))
                    {

                    }

                    var newUrlPath = csv.GetField("NEW URL PATH");
                    if(newUrlPath == null)
                        newUrlPath = csv.GetField("NEW/DESTINATION URL");                    
                        item.NewUrlPath = newUrlPath == null ? "" : newUrlPath.Replace("/Home/", "/");                    

                    if (string.IsNullOrWhiteSpace(item.CurrentUrl) || string.IsNullOrWhiteSpace(item.NewUrlPath))
                        continue;

                    if (!item.CurrentUrl.Contains("childrenswi.org", StringComparison.OrdinalIgnoreCase))
                        item.CurrentUrl = $"https://childrenswi.org/{item.CurrentUrl.TrimStart('/')}";

                    item.CurrentUrl = GetSitecorePathFromUrl(item.CurrentUrl, "/sitecore/content/CHW/Home/");
                    item.NewUrlPath = GetSitecorePathFromUrl(item.NewUrlPath, "/sitecore/content/CW/childrens/Home/");                    

                    var validCurrentUrl = false;
                    var validNewUrl = false;                    

                    var tempCurrentUrl = Normalize(item.CurrentUrl);

                    if (xpPathList != null && xpPathList.Any())
                    {                        
                        foreach (var xpPath in xpPathList)
                        {
                            if (tempCurrentUrl.Equals(xpPath.Replace(" ", "-").ToLowerInvariant()))
                            {
                                item.CurrentUrl = xpPath;
                                validCurrentUrl = true;
                                break;
                            }
                        }
                    }

                    var tempNewUrl = Normalize(item.NewUrlPath);
                    
                    if (xmcPathList != null && xmcPathList.Any())
                    {
                        foreach (var xmcPath in xmcPathList)
                        {
                            if (tempNewUrl.Equals(xmcPath.Replace(" ", "-").ToLowerInvariant()))
                            {
                                item.NewUrlPath = xmcPath;                               
                                validNewUrl = true;
                                break;
                            }
                        }
                    }                    

                    if (validNewUrl && validCurrentUrl)
                        allItems.Add(item);
                }                

                // Providers
                var xpProvidersList = xpPathList?
                    .Where(x => x.Contains("/Physician Directory/", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var xmcProvidersList = xmcPathList?
                    .Where(x => x.Contains("/Providers/", StringComparison.OrdinalIgnoreCase))
                    .ToList();                

                foreach (var xpPath in xpProvidersList ?? Enumerable.Empty<string>())
                {                    
                    var normalizedXp = Normalize(xpPath).Replace("/sitecore/content/chw/home/physician-directory", "");

                    foreach (var xmcPath in xmcProvidersList ?? Enumerable.Empty<string>())
                    {
                        var normalizedXmc = Normalize(xmcPath).Replace("/sitecore/content/cw/childrens/home/providers", "");

                        if (normalizedXp.Equals(normalizedXmc))
                        {
                            var provider = new PageItem
                            {
                                CurrentUrl = xpPath,
                                NewUrlPath = xmcPath                                
                            };

                            allItems.Add(provider);
                            break;
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error reading sheet '{sheet.Name}': {ex.Message}");
            }
        }        

        Console.WriteLine("Total items: " + allItems.Count());

        var uniqueMetaMappings = allItems
                    .GroupBy(x => x.NewUrlPath)
                    .Select(g => g.First())
                    .ToList();

        uniqueMetaMappings = SortPages(uniqueMetaMappings);

        Console.WriteLine("Providers: " + uniqueMetaMappings.Where(x => x.CurrentUrl.Contains("/Physician Directory/", StringComparison.OrdinalIgnoreCase)).Count());
        Console.WriteLine("Teaching Sheet: " + uniqueMetaMappings.Where(x => x.CurrentUrl.Contains("/Publications/Teaching-Sheet/", StringComparison.OrdinalIgnoreCase)).Count());
        Console.WriteLine("Medical Professionals: " + uniqueMetaMappings.Where(x => x.CurrentUrl.Contains("/Medical Professionals/", StringComparison.OrdinalIgnoreCase)).Count());
        Console.WriteLine("Medical Care: " + uniqueMetaMappings.Where(x => x.CurrentUrl.Contains("/Medical Care/", StringComparison.OrdinalIgnoreCase)).Count());
        Console.WriteLine("elearningcenter: " + uniqueMetaMappings.Where(x => x.CurrentUrl.Contains("/elearningcenter/", StringComparison.OrdinalIgnoreCase)).Count());

        File.WriteAllLines(@"C:\\Projects\\CW\\CWXPTool\\Migration Files\\teachingsheetlist.txt", uniqueMetaMappings.Where(x => x.CurrentUrl.Contains("/Publications/Teaching-Sheet/", StringComparison.OrdinalIgnoreCase)).Select(x => x.NewUrlPath).ToArray());

        int batchSize = 500;
        int totalItems = uniqueMetaMappings.Count;
        int batchCount = (int)Math.Ceiling((double)totalItems / batchSize);

        string baseDir = Path.GetDirectoryName(outputJsonPath)!;
        string baseFileName = Path.GetFileNameWithoutExtension(outputJsonPath);
        string ext = Path.GetExtension(outputJsonPath);

        for (int i = 0; i < batchCount; i++)
        {
            var batchItems = uniqueMetaMappings
                .Skip(i * batchSize)
                .Take(batchSize)
                .ToList();

            var json = JsonConvert.SerializeObject(batchItems, Newtonsoft.Json.Formatting.Indented);

            string batchFile = Path.Combine(baseDir, $"{baseFileName}_part{i + 1}{ext}");
            File.WriteAllText(batchFile, json);

            Console.WriteLine($"✅ Saved {batchItems.Count} items to {batchFile}");
            
            Console.ReadLine();
        }               
    }

    static void Main_Content_Migration()
    {        
        string excelPath = @"C:\Projects\CW\CWXPTool\Migration Files\Content Migration Mapping Basic Template.xlsx";
        string outputJsonPath = Path.Combine(Path.GetDirectoryName(excelPath)!, "CWPageMapping.json");

        var allItems = new List<PageItem>();

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
                    var item = new PageItem
                    {
                        CurrentUrl = csv.GetField("CURRENT URL")?.Replace("/Home/", "/") ?? "",
                        NewUrlPath = csv.GetField("NEW URL PATH") ?? "",
                        PageTemplate = csv.GetField("PAGE TEMPLATE") ?? csv.GetField("PAGE TEMPLATE/PAGE TYPE") ?? "",
                        NetNewCopy = csv.GetField("NET NEW COPY (Y/N/Redirect)") ?? ""
                    };

                    // Skip invalid/junk rows
                    if (string.IsNullOrWhiteSpace(item.CurrentUrl) ||
                        !item.CurrentUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(item.PageTemplate) ||
                        string.IsNullOrWhiteSpace(item.NewUrlPath))
                        continue;
                    
                    // Assign PageTemplateId based on rules
                    item.PageTemplateId = MapPageTemplateId(item.PageTemplate, item.CurrentUrl);

                    // Add if at least meaningful                    
                    if (!string.IsNullOrWhiteSpace(item.PageTemplateId) && !string.IsNullOrEmpty(item.NetNewCopy) && item.NetNewCopy.Equals("N", StringComparison.OrdinalIgnoreCase))
                    {
                        allItems.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error reading sheet '{sheet.Name}': {ex.Message}");
            }
        }
        Console.WriteLine("Total items: " + allItems.Count());
        Console.WriteLine("Total items without NewUrlPath: " + allItems.Where(x => string.IsNullOrEmpty(x.NewUrlPath)).Count());
        Console.WriteLine("Total items without PageTemplate: " + allItems.Where(x => string.IsNullOrEmpty(x.PageTemplate)).Count());
        var json = JsonConvert.SerializeObject(allItems, Newtonsoft.Json.Formatting.Indented);
        File.WriteAllText(outputJsonPath, json);
        Console.WriteLine($"✅ Cleaned JSON saved: {outputJsonPath} ({allItems.Count} items)");
    }

    public static List<PageItem> SortPages(List<PageItem> pages)
    {
        // Sort based on itemPath segments
        pages.Sort((a, b) =>
        {
            var pathA = a.CurrentUrl.Split(new string[] { "/" }, StringSplitOptions.RemoveEmptyEntries);
            var pathB = b.CurrentUrl.Split(new string[] { "/" }, StringSplitOptions.RemoveEmptyEntries);

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

    static string GetSitecorePathFromUrl(string url, string prefix = "")
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
                Console.WriteLine(ex.Message);
            }
        }

        string normalizedPath = path.TrimStart('/').Replace("-", " ");

        return string.IsNullOrEmpty(prefix) ? normalizedPath : prefix + normalizedPath;
    }

    static string MapPageTemplateId(string template, string url)
    {
        if (string.IsNullOrWhiteSpace(template)) return "";

        template = template.ToLowerInvariant();
        url = url.ToLowerInvariant();

        if (Regex.IsMatch(template, "conditions|condition|condtions|condtiions|treatment|treatments|treament"))
            return "{4D49E913-37B3-4946-9372-7BB0DCA63BC9}";

        if (template.Contains("specialty") || template.Contains("super specialty"))
            return "{940E3495-FBB0-41F6-91CC-2DDBD6D64D7F}";

        if (template.Contains("general 2 - elearning", StringComparison.OrdinalIgnoreCase))
        {
            return "{3D5701A0-949E-4643-8709-E4D20C8881B1}";
        }

        if (template.Contains("general 2", StringComparison.OrdinalIgnoreCase) || template.Contains("form"))
        {
            if (url.Contains("/elearningcenter/") || url.EndsWith("/contact") || url.EndsWith("/primary-care-access") || url.Contains("/about/") || url.Contains("/foster-care-and-adoption/")
                || url.Contains("/community-partners-professionals/") || url.Contains("/families-and-clients/") || url.Contains("/ways-to-help/"))
            {                
                return "{3D5701A0-949E-4643-8709-E4D20C8881B1}";
            }
            return "{2400C94A-5BB1-4F69-85CC-3AD185DC4BCA}";
        }        

        if (template.Contains("general 1", StringComparison.OrdinalIgnoreCase))
            return "{C8749C06-CA6C-4630-83E0-EA1A9A973907}";

        if (template.Contains("location") || template.Contains("locations"))
        {
            if (url.Contains("/primary-care"))
                return "{6274DC7B-91E7-4243-B5DA-96604F2EBBEA}";

            if (url.Contains("/urgent-care"))
                return "{7A4E0C65-C397-4E65-A941-7CF879C0B727}";

            if (url.Contains("/specialty-clinics"))
                return "{BB35FDA8-7E1F-48DC-A556-FA8FD89F96C2}";

            if (url.Contains("/hospital"))
                return "{CE453EDE-ED09-4928-80B0-143556AA52E8}";

            return "{1B371DE2-704C-4D43-A94B-FC04B95DC6B8}";
        }

        if(template.Contains("teachingsheets")) 
            return "{39EBED3F-5965-4A68-9A4C-45E7D29043C8}";

        return "";
    }

    static string QuoteIfNeeded(string input)
    {
        return input.Contains(",") || input.Contains("\"")
            ? "\"" + input.Replace("\"", "\"\"") + "\""
            : input;
    }
}
