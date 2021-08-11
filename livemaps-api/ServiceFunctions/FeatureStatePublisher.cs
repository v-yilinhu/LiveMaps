using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Messaging.EventHubs;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ssir.Api.Models;
using Ssir.Api.Services;

namespace AzureMapsStatusPublisher
{
    public static class FeatureStatePublisher
    {       

        [FunctionName("UpdateFeatureState")]
        public static async Task Run([EventHubTrigger("updatestate", Connection = "EventHubCS")] EventData[] events,
                                      [Blob("refdata", Connection = "AzureWebJobsStorage")] BlobContainerClient container,
                                      ILogger log)
        {
            var atlasConfigFile = Environment.GetEnvironmentVariable("AtlasConfigFile") ?? "atlasConfig.json";
            var exceptions = new List<Exception>();
            bool prerequisites = true;
            bool updateRecentData = false;

            var recentDataFile = Environment.GetEnvironmentVariable("RecentDataFile");
            var blobDataService = new BlobDataService();
            var atlasConfig = await blobDataService.ReadBlobData<BuildingConfig[]>(container, atlasConfigFile);

            var mapsServices = new Dictionary<string, MapsService>();
            foreach(var buildingConfig in atlasConfig)
            {
                foreach(var stateSet in buildingConfig.StateSets)
                {
                    string statesetid = stateSet.StateSetId.ToString();
                    if (!mapsServices.ContainsKey(statesetid))
                    {
                        mapsServices.Add(statesetid, new MapsService(buildingConfig.SubscriptionKey, buildingConfig.DatasetId, statesetid));
                    }
                }
            }
           

            if (prerequisites)
            {
                //Create a new container if the container not exists.
                await container.CreateIfNotExistsAsync();
                //Create a Blob client
                var bacmapRef = container.GetBlobClient(recentDataFile);
                //Downloads a blob from the service.
                BlobDownloadResult result = await bacmapRef.DownloadContentAsync();
                string deviceStateData = result.Content.ToString();
                IEnumerable<dynamic> recentData;
                recentData = JsonConvert.DeserializeObject<IEnumerable<dynamic>>(deviceStateData);

                foreach (EventData eventData in events)
                {
                    try
                    {
                        //Convert EventBody Type to string.
                        string messageBody = eventData.EventBody.ToString();
                        //Deserialize the EventBody data to TagObject class.
                        var dataItems = JsonConvert.DeserializeObject<IEnumerable<TagObject>>(messageBody);

                        if (dataItems != null)
                        {
                            foreach (var dataItem in dataItems)
                            {
                                if (dataItem != null)
                                {                                    
                                    foreach (var i in recentData.Where(tag => tag.DeviceId == dataItem.DeviceId))
                                    {
                                        double curVal = double.MinValue;
                                        var cv = i.CurrentValue;
                                        if (cv != null)
                                        {
                                            Double.TryParse(((JValue)cv).Value.ToString(), out curVal);
                                        }
                                        if (dataItem.Value != curVal)
                                        {
                                            i.CurrentValue = dataItem.Value;
                                            updateRecentData = true;
                                            //Create a UpdateTagState operation.
                                            var res = mapsServices[dataItem.DeviceId].UpdateTagState(dataItem.TagName, dataItem.MapFeatureId, dataItem.Value.ToString());
                                        }
                                    }
                                }
                            }
                        }

                        // Replace the line with your processing logic.
                        log.LogInformation($"C# Event Hub trigger function processed a message: {messageBody}");
                    }
                    catch (Exception e)
                    {
                        // We need to keep processing the rest of the batch - capture this exception and continue.
                        // Also, consider capturing details of the message that failed processing so it can be processed again later.
                        exceptions.Add(e);
                    }
                }
                if (updateRecentData)
                {
                    await bacmapRef.UploadAsync(BinaryData.FromObjectAsJson(recentData), overwrite: true);
                }
            }

            // Once processing of the batch is complete, if any messages in the batch failed processing throw an exception so that there is a record of the failure.

            if (exceptions.Count > 1)
            {
                throw new AggregateException(exceptions);
            }
            if (exceptions.Count == 1)
            {
                throw exceptions.Single();
            }
        }
    }
}
