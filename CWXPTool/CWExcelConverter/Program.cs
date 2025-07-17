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
}

class Program
{
    static void Main()
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
                        CurrentUrl = csv.GetField("CURRENT URL") ?? "",
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

    static string MapPageTemplateId(string template, string url)
    {
        if (string.IsNullOrWhiteSpace(template)) return "";

        template = template.ToLowerInvariant();
        url = url.ToLowerInvariant();

        if (Regex.IsMatch(template, "conditions|condition|condtions|condtiions|treatment|treatments|treament"))
            return "{4D49E913-37B3-4946-9372-7BB0DCA63BC9}";

        if (template.Contains("specialty") || template.Contains("super specialty"))
            return "{940E3495-FBB0-41F6-91CC-2DDBD6D64D7F}";

        if (template.Contains("general 2", StringComparison.OrdinalIgnoreCase))
            return "{D2B65608-E58C-4E89-9A58-25FD1474C762}";

        if (template.Contains("general 1", StringComparison.OrdinalIgnoreCase))
            return "{B25FC994-9D77-45D2-A643-9911C243CA32}";

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

        return "";
    }

    static string QuoteIfNeeded(string input)
    {
        return input.Contains(",") || input.Contains("\"")
            ? "\"" + input.Replace("\"", "\"\"") + "\""
            : input;
    }
}
