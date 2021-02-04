using System;
using Newtonsoft.Json;

namespace Vendr.Contrib.PaymentProviders.Square.Models
{
    public class SquareWebhookEvent
    {
        public Data data { get; set; }
        public bool IsValid { get; set; }
    }

    public class Data
    {
        [JsonProperty("object")]
        public Object _object { get; set; }
    }

    public class Object
    {
        public Payment payment { get; set; }
    }

    public class Payment
    {
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
        public Amount_Money amount_money { get; set; }
        public Total_Money total_money { get; set; }
        public string status { get; set; }
        public string source_type { get; set; }
        public string order_id { get; set; }
        public string reference_id { get; set; }
        public string customer_id { get; set; }
    }

    public class Amount_Money
    {
        public int amount { get; set; }
        public string currency { get; set; }
    }

    public class Total_Money
    {
        public int amount { get; set; }
        public string currency { get; set; }
    }
}