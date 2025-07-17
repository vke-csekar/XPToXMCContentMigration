using CWXPMigration.Models;
using System.Collections.Generic;
using System.Linq;

namespace CWXPMigration
{
    public static class SitecoreQueryBuilder
    {
        public static GraphQLQuery GetParentChildrenQueryByPath(
    string path,
    List<string> fieldNames,
    int first = 50,
    string after = null,
    List<string> excludeTemplateIDs = null,
    List<string> includeTemplateIDs = null)
        {
            var sb = new System.Text.StringBuilder();

            // Start query
            sb.AppendLine("query {");
            sb.AppendLine($"parent: item(where: {{ database: \"master\", path: \"{path}\" }}) {{");
            sb.AppendLine("parent: path");
            sb.AppendLine("itemId: itemId");
            sb.AppendLine("itemName: name");

            // Children block
            var afterClause = !string.IsNullOrWhiteSpace(after) ? $", after: \"{after}\"" : "";

            var excludeTemplateIDsClause = (excludeTemplateIDs != null && excludeTemplateIDs.Count > 0)
                ? $", excludeTemplateIDs: [{string.Join(", ", excludeTemplateIDs.Select(id => $"\"{id}\""))}]"
                : "";

            var includeTemplateIDsClause = (includeTemplateIDs != null && includeTemplateIDs.Count > 0)
                ? $", includeTemplateIDs: [{string.Join(", ", includeTemplateIDs.Select(id => $"\"{id}\""))}]"
                : "";

            sb.AppendLine($"children(first: {first}{afterClause}{excludeTemplateIDsClause}{includeTemplateIDsClause}) {{");
            sb.AppendLine("pageInfo { endCursor hasNextPage }");
            sb.AppendLine("items: edges {");
            sb.AppendLine("item: node {");
            sb.AppendLine("itemId");
            sb.AppendLine("path");
            sb.AppendLine("itemName: name");
            sb.AppendLine("template { templateId }");

            // Field queries
            if (fieldNames != null && fieldNames.Any())
            {
                foreach (var fieldName in fieldNames.Where(f => !string.IsNullOrWhiteSpace(f)))
                {
                    sb.AppendLine($"{fieldName}: field(name: \"{fieldName}\") {{ value }}");
                }
            }

            sb.AppendLine("}"); // Close item: node
            sb.AppendLine("}"); // Close items: edges
            sb.AppendLine("}"); // Close children
            sb.AppendLine("}"); // Close parent

            sb.AppendLine("}"); // Close query

            // Minify query: remove extra whitespace and newlines
            var query = sb.ToString()
                .Replace("\r", "")
                .Replace("\n", " ")
                .Replace("  ", " ")
                .Trim();

            return new GraphQLQuery() { Query = query };
        }
        public static GraphQLQuery GetItemQueryByPath(string path, List<string> fieldNames = null)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            string fieldQueries = string.Empty;
            
            if (fieldNames != null)
            {
                fieldQueries = string.Join(" ", fieldNames
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => $"{f}: field(name: \"{f}\") {{ value }}"));
            }            

            var query = $@"
    query {{
        item(where: {{ database: ""master"", path: ""{path}"" }}) {{
            path: path
            itemId: itemId
            itemName: name
            {fieldQueries}
        }}
    }}";

            return new GraphQLQuery
            {
                Query = query.Replace("\r", "").Replace("\n", " ").Replace("  ", " ").Trim()
            };
        }
    }
}