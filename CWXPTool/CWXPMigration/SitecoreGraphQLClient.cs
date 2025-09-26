using CWXPMigration.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace CWXPMigration
{
    public interface ISitecoreGraphQLClient
    {
        Task<List<CreatedItem>> CreateBulkItemsBatchedAsync(List<SitecoreCreateItemInput> items, string environment, string accessToken, int batchSize = 50);

        Task<bool> UpdateBulkItemsBatchedAsync(
            List<SitecoreUpdateItemInput> items,
            string environment,
            string accessToken,
            int batchSize = 50);

        Task<SitecoreItem> QuerySingleItemAsync(string environment, string accessToken, string path);

        Task<QueryItemsResult<T>> QueryItemsAsync<T>(
            string environment,
            string accessToken,
            string parentPath,
            List<string> fieldNames,
            Func<JObject, T> itemMapper,
            List<string> excludeTemplateIDs = null,
            List<string> includeTemplateIDs = null);

        Task<XMCBlogPage> QueryBlogSingleItemAsync(string environment, string accessToken, string path);
    }

    public class SitecoreGraphQLClient : ISitecoreGraphQLClient
    {        
        private readonly HttpClient _httpClient;        

        public SitecoreGraphQLClient()
        {            
            _httpClient = new HttpClient();            
        }        

        public async Task<List<CreatedItem>> CreateBulkItemsBatchedAsync(
        List<SitecoreCreateItemInput> items, string environment, string accessToken, int batchSize = 50)
        {
            var createdItems = new List<CreatedItem>();

            for (int i = 0; i < items.Count; i += batchSize)
            {
                var batch = items.Skip(i).Take(batchSize).ToList();

                var mutation = SitecoreMutationBuilder.CreateBulkItems(batch);
                var jsonBody = JsonConvert.SerializeObject(mutation);

                if (!string.IsNullOrEmpty(environment)) Sitecore.Diagnostics.Log.Info(jsonBody, this);
                var request = new HttpRequestMessage(HttpMethod.Post, getEndpoint(environment));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);

                var content = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Create failed: {response.StatusCode} - {content}");

                var data = JObject.Parse(content)?["data"];
                if (data == null || data.Type != JTokenType.Object) continue;

                foreach (var prop in data.Children<JProperty>())
                {
                    var item = prop.Value["item"];
                    if (item != null)
                    {
                        createdItems.Add(new CreatedItem
                        {
                            ItemId = item.Value<string>("itemId"),
                            Name = item.Value<string>("name"),
                            Path = item.Value<string>("path"),
                            Language = item["language"]?.Value<string>("name"),
                            Fields = item["fields"]?["nodes"]
                                ?.Select(f => new FieldValue
                                {
                                    Name = f.Value<string>("name"),
                                    Value = f["value"]
                                }).ToList()
                        });
                    }
                }
            }

            return createdItems;
        }

        public async Task<bool> UpdateBulkItemsBatchedAsync(
            List<SitecoreUpdateItemInput> items,
            string environment,
            string accessToken,
            int batchSize = 50)
        {
            var updatedItems = new List<SitecoreUpdatedItem>();

            try
            {
                for (int i = 0; i < items.Count; i += batchSize)
                {
                    var batch = items.Skip(i).Take(batchSize).ToList();
                    var mutation = SitecoreMutationBuilder.UpdateBulkItems(batch);
                    var jsonBody = JsonConvert.SerializeObject(mutation);

                    var request = new HttpRequestMessage(HttpMethod.Post, getEndpoint(environment));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                    var response = await _httpClient.SendAsync(request);
                    var responseContent = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        if (!string.IsNullOrEmpty(environment)) Sitecore.Diagnostics.Log.Error($"Failed to update items: {response.StatusCode} - {responseContent}", this);
                        return false;
                    }

                    var json = JObject.Parse(responseContent);

                    if (json["errors"] != null)
                    {
                        if (!string.IsNullOrEmpty(environment)) Sitecore.Diagnostics.Log.Error("GraphQL Errors: " + json["errors"], this);
                    }

                    var data = json["data"];
                    if (data != null)
                    {
                        foreach (var prop in data.Children<JProperty>())
                        {
                            var item = prop.Value["item"];
                            if (item != null)
                            {
                                var updatedItem = new SitecoreUpdatedItem
                                {
                                    ItemId = item.Value<string>("itemId"),
                                    Name = item.Value<string>("name"),
                                    Path = item.Value<string>("path"),
                                    Language = item["language"]?.Value<string>("name")
                                };
                                updatedItems.Add(updatedItem);

                                if (!string.IsNullOrEmpty(environment)) Sitecore.Diagnostics.Log.Info($"[GraphQLApiClient] [UPDATE] Item Updated: {updatedItem.Path}", this);
                            }
                        }
                    }



                    if (!string.IsNullOrEmpty(environment)) Sitecore.Diagnostics.Log.Info($"[GraphQLApiClient] [UPDATE] Items Updated: {updatedItems.Count}", this);
                }

                return true;
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(environment)) Sitecore.Diagnostics.Log.Error("Error performing bulk update: " + ex.Message, ex, this);
                return false;
            }
        }


        public async Task<SitecoreItem> QuerySingleItemAsync(string environment, string accessToken, string path)
        {
            var query = SitecoreQueryBuilder.GetItemQueryByPath(path);

            var jsonContent = new StringContent(
                Newtonsoft.Json.JsonConvert.SerializeObject(query),
                Encoding.UTF8,
                "application/json");

            try
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                var response = await _httpClient.PostAsync(getEndpoint(environment), jsonContent);

                if (!response.IsSuccessStatusCode)
                {
                    var errMsg = await response.Content.ReadAsStringAsync();
                    throw new Exception($"GraphQL query failed: {response.StatusCode} - {errMsg}");
                }

                var responseString = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(responseString);

                if (json["errors"] != null)
                {
                    Console.WriteLine("GraphQL Errors: " + json["errors"]);
                    return null;
                }

                var item = json["data"]?["item"];
                if (item == null)
                    return null;

                return new SitecoreItem
                {
                    ItemId = item.Value<string>("itemId"),
                    Path = item.Value<string>("path"),
                    Name = item.Value<string>("itemName"),
                    TemplateId = item["template"]?.Value<string>("templateId")
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error querying single item: " + ex.Message);
                return null;
            }
        }


        public async Task<XMCBlogPage> QueryBlogSingleItemAsync(string environment, string accessToken, string path)
        {
            var query = SitecoreQueryBuilder.GetItemQueryByPath(path, new List<string>() { "content" });            

            var jsonContent = new StringContent(
                Newtonsoft.Json.JsonConvert.SerializeObject(query),
                Encoding.UTF8,
                "application/json");

            try
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                var response = await _httpClient.PostAsync(getEndpoint(environment), jsonContent);

                if (!response.IsSuccessStatusCode)
                {
                    var errMsg = await response.Content.ReadAsStringAsync();
                    throw new Exception($"GraphQL query failed: {response.StatusCode} - {errMsg}");
                }

                var responseString = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(responseString);                

                if (json["errors"] != null)
                {
                    Console.WriteLine("GraphQL Errors: " + json["errors"]);
                    return null;
                }

                var item = json["data"]?["item"];
                if (item == null)
                    return null;

                return new XMCBlogPage
                {
                    ItemId = item.Value<string>("itemId"),
                    Path = item.Value<string>("path"),
                    ItemName = item.Value<string>("itemName"),
                    Content = item["content"]?.Value<string>("value")
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error querying single item: " + ex.Message);
                return null;
            }
        }

        public async Task<QueryItemsResult<T>> QueryItemsAsync<T>(
            string environment,
            string accessToken,            
            string parentPath,
            List<string> fieldNames,
            Func<JObject, T> itemMapper,
            List<string> excludeTemplateIDs = null,
            List<string> includeTemplateIDs = null)
        {
            var allItems = new List<T>();
            bool hasNextPage = true;
            string endCursor = null;

            string itemId = string.Empty;
            string path = string.Empty;
            string itemName = string.Empty;

            var lastPageInfo = new PageInfo { EndCursor = string.Empty, HasNextPage = false };

            try
            {
                while (hasNextPage)
                {
                    var query = SitecoreQueryBuilder.GetParentChildrenQueryByPath(
                        parentPath,
                        fieldNames,
                        50,
                        endCursor,
                        excludeTemplateIDs,
                        includeTemplateIDs
                    );                    

                    var jsonContent = new StringContent(
                        Newtonsoft.Json.JsonConvert.SerializeObject(query),
                        Encoding.UTF8,
                        "application/json"
                    );

                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    var response = await _httpClient.PostAsync(getEndpoint(environment), jsonContent);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errMsg = await response.Content.ReadAsStringAsync();
                        throw new Exception($"GraphQL query failed: {response.StatusCode} - {errMsg}");
                    }

                    var responseString = await response.Content.ReadAsStringAsync();
                    var json = JObject.Parse(responseString);                    

                    if (json["errors"] != null)
                    {
                        Console.WriteLine("GraphQL Errors: " + json["errors"]);
                        // Continue or break depending on your strategy
                    }

                    var parentData = json["data"]?["parent"];
                    if (parentData != null)
                    {
                        itemId = parentData.Value<string>("itemId");
                        path = parentData.Value<string>("parent");
                        itemName = parentData.Value<string>("itemName");

                        var children = parentData["children"];
                        var items = children?["items"];

                        if (items != null)
                        {
                            foreach (var edge in items)
                            {
                                var itemNode = edge["item"] as JObject;
                                if (itemNode != null)
                                {
                                    var mappedItem = itemMapper(itemNode);
                                    if (mappedItem != null)
                                    {
                                        allItems.Add(mappedItem);
                                    }
                                }
                            }
                        }

                        var pageInfo = children?["pageInfo"];
                        hasNextPage = pageInfo?.Value<bool>("hasNextPage") ?? false;
                        endCursor = pageInfo?.Value<string>("endCursor");
                        lastPageInfo = new PageInfo
                        {
                            EndCursor = endCursor,
                            HasNextPage = hasNextPage
                        };
                    }
                    else
                    {
                        hasNextPage = false; // Stop loop if no parent data
                    }
                }

                return new QueryItemsResult<T>
                {
                    ItemId = itemId,
                    Path = path,
                    ItemName = itemName,
                    Items = allItems,
                    PageInfo = lastPageInfo
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error querying items: " + ex.Message);
                return null;
            }
        }

        private string getEndpoint(string environment)
        {
            if (string.IsNullOrEmpty(environment))
                return "https://xmc-childrensho3e59-cw60b4-prod2126.sitecorecloud.io/sitecore/api/authoring/graphql/v1";
            string authoringUrl = Sitecore.Configuration.Settings.GetSetting($"CW.{environment}.AuthoringUrl")?.TrimEnd('/');
            return $"{authoringUrl}/sitecore/api/authoring/graphql/v1";
        }
    }
}