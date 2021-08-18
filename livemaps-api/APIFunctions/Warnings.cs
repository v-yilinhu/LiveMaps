using System;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Ssir.Api
{
    public static class Warnings
    {
        /*
         Faults data rendered on the sidebar available by the Warnings Function.
         */
        [FunctionName("Warnings")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "faults/{region}/{campus}/{building}")] HttpRequest req,
            [Blob("shared", Connection = "AzureWebJobsStorage")] BlobContainerClient container,
            string region,
            string campus,
            string building,
            ILogger log)
        {
            string fileName;
            try
            {
                if (!string.IsNullOrEmpty(building))
                {
                    fileName = $"{region}_{campus}_{building}_warnings.json".ToLower();
                }
                else
                {
                    return new NotFoundObjectResult("Data not found!");
                }
                //Create a Blob client.
                var deviceStateRef = container.GetBlobClient(fileName);
                //Downloads a blob from the service.
                BlobDownloadResult result = await deviceStateRef.DownloadContentAsync();
                string deviceStateData = result.Content.ToString();
                return new OkObjectResult(deviceStateData);
            }
            catch(Exception ex)
            {
                log.LogError(ex.Message);
                return new NotFoundObjectResult("Data not found!");
            }
        }
    }
}
