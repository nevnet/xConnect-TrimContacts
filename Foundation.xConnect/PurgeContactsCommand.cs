using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Sitecore;
using Sitecore.Configuration;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.DependencyInjection;
using Sitecore.Diagnostics;
using Sitecore.Tasks;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;

namespace Foundation.xConnect
{
    /// <summary>
    /// A Sitecore command that calls the new Data Tools API to register a 'purge contacts' task.
    /// https://doc.sitecore.com/xp/en/developers/102/sitecore-experience-platform/web-api-for-xconnect-data-tools.html
    /// Contacts (and their interactions) that have not visited the site within the CutoffDate period will be removed from xConnect.
    /// The Cortext Processing role is responsible for executing the task.
    /// The API must be called by providing credentials for a Sitecore user in the "Sitecore XConnect Data Admin" role.
    /// </summary>
    public class PurgeContactsCommand
    {
        private const string clientId = "SitecorePassword";
        private const string tokenPath = "/connect/token";
        private const string taskPath = "/sitecore/api/datatools/purge/tasks/contacts";
        private const string cutoffParamName = "CutoffDays";
        private readonly HttpClient httpClient;
        private readonly string tokenUrl;
        private readonly string taskUrl;
        private readonly string clientSecret;
        private readonly string username;
        private readonly string password;

        public PurgeContactsCommand()
        {
            httpClient = ServiceLocator.ServiceProvider.GetService<HttpClient>();
            tokenUrl = Settings.GetSetting("FederatedAuthentication.IdentityServer.Authority") + tokenPath;
            taskUrl = Settings.GetSetting("Foundation.xConnect.PurgeCommand.HostPrefix", Globals.ServerUrl) + taskPath;
            username = Settings.GetSetting("Foundation.xConnect.PurgeCommand.Username");
            password = Settings.GetSetting("Foundation.xConnect.PurgeCommand.Password");
            clientSecret = Settings.GetSetting("Foundation.xConnect.PurgeCommand.ClientSecret");

            Assert.IsNotNullOrEmpty(username, "Configuration value for Foundation.xConnect.PurgeCommand.Username must be provided.");
            Assert.IsNotNullOrEmpty(password, "Configuration value for Foundation.xConnect.PurgeCommand.Password must be provided.");
            Assert.IsNotNullOrEmpty(clientSecret, "Configuration value for Foundation.xConnect.PurgeCommand.ClientSecret must be provided");
        }

        [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Sitecore")]
        public async void Execute(Item[] items, CommandItem command, ScheduleItem schedule)
        {
            // Whole method needs to be wrapped in try block to prevent process from crashing
            try
            {
                string token = null;
                var parameters = (NameValueListField)command.InnerItem.Fields["Parameters"];
                var cutoffDays = 180;

                if (parameters.NameValues.AllKeys.Contains(cutoffParamName))
                {
                    Assert.IsTrue(int.TryParse(parameters.NameValues[cutoffParamName], out cutoffDays), $"{cutoffParamName} is not a valid number.");
                    Assert.IsTrue(cutoffDays > 0, $"{cutoffParamName} must be greater than zero.");
                }

                // Get token from Identity Server for user that has permission to execute Data Tools API
                {
                    var body = new Dictionary<string, string> {
                        { "grant_type", "password" },
                        { "username", username },
                        { "password", password },
                        { "client_id", clientId },
                        { "client_secret", clientSecret }
                    };

                    using (var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl))
                    {
                        request.Headers.Add("cache-control", "no-cache");
                        request.Content = new FormUrlEncodedContent(body);
                        using (var response = await httpClient.SendAsync(request))
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                Log.Error($"Contact purge command failed with error: {await response.Content.ReadAsStringAsync()}", this);
                                return;
                            }

                            using (var stream = await response.Content.ReadAsStreamAsync())
                            using (var sr = new StreamReader(stream))
                            using (var jr = new JsonTextReader(sr))
                            {
                                var serializer = new JsonSerializer();
                                dynamic r = serializer.Deserialize(jr);
                                Assert.IsNotNullOrEmpty((string)r.access_token, "Response does not contain a valid access token.");

                                token = r.access_token;
                                Log.Info($"Contact purge command successfully obtained API token for user {username}.", this);
                            }
                        }
                    }
                }

                // Register purge task with xConnect
                // Note: CutoffDays must be >= 180 unless this has been overridden in the Cortex Processing configuration files.
                {
                    var body = new Dictionary<string, string> {
                        { cutoffParamName, cutoffDays.ToString() }
                    };

                    using (var request = new HttpRequestMessage(HttpMethod.Post, taskUrl))
                    {
                        request.Headers.Add("Authorization", $"Bearer {token}");
                        request.Content = new FormUrlEncodedContent(body);
                        using (var response = await httpClient.SendAsync(request))
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                Log.Error($"Contact purge task registration failed with error: {await response.Content.ReadAsStringAsync()}", this);
                                return;
                            }

                            using (var stream = await response.Content.ReadAsStreamAsync())
                            using (var sr = new StreamReader(stream))
                            using (var jr = new JsonTextReader(sr))
                            {
                                var serializer = new JsonSerializer();
                                dynamic r = serializer.Deserialize(jr);
                                Assert.IsNotNullOrEmpty((string)r.TaskId, "Response does not contain a valid task ID.");

                                Log.Info($"Contact purge task {r.TaskId} registered with xConnect. Removing data older than {cutoffDays} days.", this);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Unhandled exception detected in PurgeContactsCommand.", ex, this);
            }
        }
    }
}
