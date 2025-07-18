using CWXPMigration.Models;
using System.Collections.Generic;
using System.Linq;

namespace CWXPMigration
{    
    public static class SitecoreMutationBuilder
    {
        public static GraphQLQuery CreateBulkItems(List<SitecoreCreateItemInput> inputs)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("mutation {");

            for (int i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i];
                var cleanedFields = input.Fields?
                    .Where(f => !string.IsNullOrWhiteSpace(f.Name) && f.Value != null)
                    .ToList() ?? new List<SitecoreFieldInput>();

                var fieldsBlock = "";
                if (cleanedFields.Any())
                {
                    var fieldEntries = cleanedFields.Select(field =>
                    {
                        var safeValue = field.Value.ToString()
                            .Replace("\\", "\\\\")
                            .Replace("\"", "\\\"")
                            .Replace("\n", "\\n");

                        return $"{{ name: \"{field.Name}\", value: \"{safeValue}\" }}";
                    });

                    fieldsBlock = $"fields: [{string.Join(", ", fieldEntries)}],";
                }

                var languageLine = !string.IsNullOrWhiteSpace(input.Language)
                    ? $"language: \"{input.Language}\","
                    : "";

                sb.AppendLine($@"
                item{i}: createItem(
                    input: {{
                        name: ""{input.Name}"",
                        templateId: ""{input.TemplateId}"",
                        parent: ""{input.Parent}"",
                        {languageLine}
                        {fieldsBlock}
                    }}
                ) {{
                    item {{
                        itemId
                        name
                        path
                        language {{
                            name
                        }}
                        fields(ownFields: true, excludeStandardFields: true) {{
                            nodes {{
                                name
                                value
                            }}
                        }}
                    }}
                }}");
            }

            sb.AppendLine("}");
            var query = sb.ToString().Replace("\r", "").Replace("\n", " ").Replace("  ", " ").Trim();
            return new GraphQLQuery() { Query = query };
        }


        public static GraphQLQuery UpdateBulkItems(List<SitecoreUpdateItemInput> inputs)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("mutation {");

            for (int i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i];
                var cleanedFields = input.Fields?
                    .Where(f => !string.IsNullOrWhiteSpace(f.Name) && f.Value != null)
                    .ToList() ?? new List<SitecoreFieldInput>();

                var fieldsBlock = "";
                if (cleanedFields.Any())
                {
                    var fieldEntries = cleanedFields.Select(field =>
                    {
                        var safeValue = field.Value.ToString()
                            .Replace("\\", "\\\\")
                            .Replace("\"", "\\\"")
                            .Replace("\n", "\\n");

                        return $"{{ name: \"{field.Name}\", value: \"{safeValue}\" }}";
                    });

                    fieldsBlock = $"fields: [{string.Join(", ", fieldEntries)}],";
                }

                var languageLine = !string.IsNullOrWhiteSpace(input.Language)
                    ? $"language: \"{input.Language}\","
                    : "";

                sb.AppendLine($@"
                item{i}: updateItem(
                    input: {{
                        itemId: ""{input.ItemId}"",
                        {languageLine}
                        {fieldsBlock}
                    }}
                ) {{
                    item {{
                        itemId
                        name
                        path
                        language {{
                            name
                        }}
                        fields(ownFields: true, excludeStandardFields: true) {{
                            nodes {{
                                name
                                value
                            }}
                        }}
                    }}
                }}");
            }

            sb.AppendLine("}");
            var query = sb.ToString().Replace("\r", "").Replace("\n", " ").Replace("  ", " ").Trim();
            return new GraphQLQuery() { Query = query };
        }
    }
}