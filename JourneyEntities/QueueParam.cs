using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace JourneyEntities
{
    public class QueueParam
    {
        public string Id { get; set; }

        public string PictureUri { get; set; }

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this);
        }

        public static QueueParam FromJson(string json)
        {
            return JsonConvert.DeserializeObject<QueueParam>(json);
        }
    }
}
