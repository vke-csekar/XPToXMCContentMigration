using CWXPMigration.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace CWXPMigration
{
    public static class AuthHelper
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public static async Task<AuthResponse> GetAuthTokenAsync(string environment)
        {
            string client_id = string.Empty;
            string client_secret = string.Empty;
            if (!string.IsNullOrEmpty(environment)) {
                client_id = Sitecore.Configuration.Settings.GetSetting($"CW.{environment}.ClientId");
                client_secret = Sitecore.Configuration.Settings.GetSetting($"CW.{environment}.ClientSecret");
            }
            else
            {
                client_id = "9sDljVEboLPxzMbQpB9euHHtPgNPy4fr";
                client_secret = "Nz6HL3keTQDresoTbqJgyPMv0ljQzgbtkIofuaAllCKjNiXl20m1ogimWtrFgZll";
            }
            
            if(!string.IsNullOrEmpty(environment)) Sitecore.Diagnostics.Log.Info($"Starting authentication at authUrl: {Constants.AuthUrl}", typeof(AuthHelper));

            var formData = new Dictionary<string, string>
        {
            { "grant_type", "client_credentials" },
            { "client_id", client_id },
            { "client_secret", client_secret },
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
                        if (!string.IsNullOrEmpty(environment)) Sitecore.Diagnostics.Log.Error(ex.Message, ex, typeof(AuthHelper));
                        throw new Exception(message);
                    }
                }

                if (!string.IsNullOrEmpty(environment)) Sitecore.Diagnostics.Log.Info($"Authentication successful at authUrl: {Constants.AuthUrl}", typeof(AuthHelper));
                return JsonConvert.DeserializeObject<AuthResponse>(responseContent);
            }
            catch (Exception ex)
            {
                throw new Exception("Internal Server Error: " + ex.Message);
            }
        }
    }
}