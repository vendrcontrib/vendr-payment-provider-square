using System;
using Newtonsoft.Json;

namespace Vendr.Contrib.PaymentProviders.Square.Models
{
    public class SquareWebhookEvent
    {
        public string merchant_id { get; set; }
        public string type { get; set; }
        public string event_id { get; set; }
        public string created_at { get; set; }
        public Data data { get; set; }
        public bool IsValid { get; set; }
    }

    public class Data
    {
        public string type { get; set; }
        public string id { get; set; }
        public Object _object { get; set; }
    }

    public class Object
    {
        public Payment payment { get; set; }
    }

    public class Payment
    {
        public string id { get; set; }
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
        public Amount_Money amount_money { get; set; }
        public Total_Money total_money { get; set; }
        public string status { get; set; }
        public string source_type { get; set; }
        public Card_Details card_details { get; set; }
        public string location_id { get; set; }
        public string order_id { get; set; }
        public string receipt_number { get; set; }
        public string receipt_url { get; set; }
        public string reference_id { get; set; }
        public string note { get; set; }
        public string customer_id { get; set; }
        public int version { get; set; }
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

    public class Card_Details
    {
        public string auth_result_code { get; set; }
        public string avs_status { get; set; }
        public Card card { get; set; }
        public string cvv_status { get; set; }
        public string entry_method { get; set; }
        public string statement_description { get; set; }
        public string status { get; set; }
    }

    public class Card
    {
        public string bin { get; set; }
        public string card_brand { get; set; }
        public string card_type { get; set; }
        public int exp_month { get; set; }
        public int exp_year { get; set; }
        public string fingerprint { get; set; }
        public string last_4 { get; set; }
    }
}