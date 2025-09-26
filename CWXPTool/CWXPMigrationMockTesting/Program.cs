using CWXPMigration;
using CWXPMigration.Models;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CWXPMigrationMockTesting
{
    public class Program
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        static void Main(string[] args)
        {
            var rte = @"<h1>(Terapia debajo de la lengua)</h1>
<h2 class=""chw-h2"">&iquest;Qu&eacute; es la inmunoterapia sublingual?</h2>
<p>La terapia sublingual es un medicamento que se administra debajo de la lengua . El medicamento puede reducir los s&iacute;ntomas de alergia mediante el fortalecimiento de las defensas<br />
de su cuerpo.</p>
<h2 class=""chw-h2"">&iquest;C&oacute;mo funcionan las tabletas de inmunoterapia sublingual?</h2>
<p>Las tabletas tardan unos meses en empezar a hacer efecto. Las tabletas pueden disminuir los signos de alergias como:</p>
<ul>
    <li>Picaz&oacute;n en los ojos, nariz</li>
    <li>Lagrimeo en los ojos</li>
    <li>Tos</li>
    <li>Estornudos</li>
    <li>Congesti&oacute;n nasal</li>
    <li>Escurrimiento nasal.</li>
</ul>
<p>Es posible que todav&iacute;a presente algunos s&iacute;ntomas de alergia incluso despu&eacute;s de que las tabletas comiencen a funcionar.</p>
<h2 class=""chw-h2"">&iquest;A qui&eacute;n se le deben dar las tabletas de inmunoterapia sublingual?</h2>
<p>A cualquier persona que:</p>
<ul>
    <li>Tenga alergias al pasto, los &aacute;caros del polvo o la ambros&iacute;a.</li>
    <li>Se someta a an&aacute;lisis de piel o de sangre que muestren que tiene estas alergias.</li>
    <li>Haya intentado evitar el polen o los &aacute;caros del polvo sin &eacute;xito.</li>
    <li>No haya sentido alivio con los medicamentos para la alergia.</li>
    <li>Tenga demasiados efectos secundarios del medicamento para la alergia.</li>
    <li>No pueda ir a la cl&iacute;nica para recibir vacunas contra la alergia.</li>
</ul>
<h2 class=""chw-h2"">&iquest;C&oacute;mo empezamos a tomar las tabletas?</h2>
<p>
La <strong>primera tableta se administra en la cl&iacute;nica por seguridad. Tendr&aacute; que quedarse durante&nbsp;</strong>30 minutos despu&eacute;s de que le den el medicamento.</p>";
            List<RichTextSection> jumpLinkSections = new List<RichTextSection>();
            var contents = RichTextSplitter.SplitByH2(rte, "en");
            if (contents != null && contents.Any())
            {
                jumpLinkSections.AddRange(contents);
                var finalHtml = RichTextSplitter.AddIdAttributeToAllH2(rte);                
            }            
        }

        static async Task RunAsync()
        {
            var accessToken = await GetAuthTokenAsync();
            var specialties = await QueryItemsAsync<SitecoreItemBase>(accessToken.AccessToken, XMC_Datasource_Constants.Specialties, // e.g., "/sitecore/content/CW/childrens/Specialties"
    new List<string>(), // No extra fields for now
    jObj => new SitecoreItemBase
    {
        ItemId = jObj["itemId"]?.ToString(),
        Path = jObj["path"]?.ToString(),
        ItemName = jObj["itemName"]?.ToString()
    }
);
            var its = specialties.Items;
        }

        public async static Task<QueryItemsResult<T>> QueryItemsAsync<T>(            
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

                    var response = await _httpClient.PostAsync("https://xmc-childrensho3e59-cw60b4-prod2126.sitecorecloud.io/sitecore/api/authoring/graphql/v1", jsonContent);

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

        public static async Task<AuthResponse> GetAuthTokenAsync()
        {            

            var formData = new Dictionary<string, string>
        {
            { "grant_type", "client_credentials" },
            { "client_id", "9sDljVEboLPxzMbQpB9euHHtPgNPy4fr" },
            { "client_secret", "Nz6HL3keTQDresoTbqJgyPMv0ljQzgbtkIofuaAllCKjNiXl20m1ogimWtrFgZll" },
            { "audience", Constants.Audience }
        };

            var request = new HttpRequestMessage(HttpMethod.Post, Constants.AuthUrl)
            {
                Content = new FormUrlEncodedContent(formData)
            };

            try
            {
                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    string message = "An error occurred.";
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        message = "Unauthorized request. Check your client credentials.";
                    else if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                        message = "Bad request. Invalid parameters.";

                    try
                    {
                        var errorObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseContent);
                        if (errorObj != null && errorObj.ContainsKey("message"))
                            message = errorObj["message"].ToString();
                    }
                    catch (Exception ex)
                    {                        
                        throw ex;
                    }
                }
                
                return JsonConvert.DeserializeObject<AuthResponse>(responseContent);
            }
            catch (Exception ex)
            {
                throw new Exception("Internal Server Error: " + ex.Message);
            }
        }
    }
}
