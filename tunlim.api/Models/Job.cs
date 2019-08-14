using System;
using System.Runtime.Serialization;

using Newtonsoft.Json;

namespace tunlim.api.Models
{
    [DataContract]
    internal class Job
    {
        [DataMember]
        [JsonProperty("id")]
        public string ID { get; set; }

        [DataMember]
        [JsonProperty("data")]
        public string data { get; set; }

        [DataMember]
        [JsonProperty("executiontime")]
        public DateTime ExecutionTime { get; set; }
    }
}
