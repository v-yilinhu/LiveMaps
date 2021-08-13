using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Ssir.Api.Models;
using Ssir.Api.Models.Atlas;
using Ssir.Api.Services;

namespace Ssir.Api
{
    public static class PostState
    {
        [FunctionName("PostState")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "state/{region}/{campus}/{building}/{floor}/{room}")] HttpRequest req,
            [Blob("shared", Connection = "AzureWebJobsStorage")] BlobContainerClient container,
            string region,
            string campus,
            string building,
            string floor,
            string room,
            ILogger log)
        {
            bool prerequisites = true;
            var errors = new StringBuilder();
            //Get AtlasConfigFile from environment variable.
            var atlasConfigFile = Environment.GetEnvironmentVariable("AtlasConfigFile") ?? "atlasConfig.json";

            if (string.IsNullOrEmpty(room))
            {
                prerequisites = false;
                errors.Append("Required query parameter {room} was not defined");
            }
            
            var blobDataService = new BlobDataService();
            //Create a ReadBlobData operation and return the data of the BuildingConfig.
            var atlasConfig = await blobDataService.ReadBlobData<BuildingConfig[]>(container, atlasConfigFile);
            var buildingConfig = atlasConfig.FirstOrDefault(b => b.BuildingId.ToLower() == $"{region}/{campus}/{building}".ToLower());

            if (buildingConfig == null)
            {
                prerequisites = false;
                errors.Append($"Atlas config for {building} was not found");
            }

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            

            var atlasFeaturesFileName = $"{region}_{campus}_{building}_featuremap.json".ToLower();
            //Create a Blob client.
            var featureMapref = container.GetBlobClient(atlasFeaturesFileName);
           
            List<Feature> features;
            

            if (prerequisites)
            {                
                var rdi = new Dictionary<string, Dictionary<string, RoomDataItem>>();
                try
                {
                    features = await blobDataService.ReadBlobData<List<Feature>>(container, atlasFeaturesFileName);

                    var jo = JsonDocument.Parse(requestBody);
                    string stateSet = null;
                    string value = null;
                    if (jo != null)
                    {
                        var jp = jo.RootElement.EnumerateObject().First();
                        if (jp.Name != null)
                        {
                            stateSet = jp.Name;
                            value = jp.Value.ToString();
                        }
                    }

                    var stateSetCfg = buildingConfig.StateSets.FirstOrDefault(ss => ss.StateSetName.ToLower() == stateSet.ToLower());

                    if(stateSetCfg != null)
                    {
                        var feature = features.FirstOrDefault(f => f.Properties.Name.ToLower().Replace("-","") == room.ToLower().Replace("-",""));
                        if (feature != null)
                        {
                            var mapsService = new MapsService(buildingConfig.SubscriptionKey, buildingConfig.DatasetId, stateSetCfg.StateSetId.ToString());
                            await mapsService.UpdateTagState(stateSetCfg.StateSetName, feature.Id, value);
                        }
                    }

                }
                catch (Exception ex)
                {
                    log.LogError(ex.Message);
                }

                return new OkObjectResult(JsonSerializer.Serialize(rdi));
            }
            else
            {
                log.LogError(errors.ToString());
                return new NotFoundResult();
            }            
        }
    }
}
