using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace PhotoFunctionApp
{
    public static class GenerateSmallPhoto
    {
        [FunctionName("GenerateSmallPhoto")]
        public static async void Run([QueueTrigger("journeynotes", Connection = "queueConnection")]string myQueueItem, ILogger log, ExecutionContext context)
        {
            log.LogInformation($"Process small image: {myQueueItem}");
            
        }
    }
}
