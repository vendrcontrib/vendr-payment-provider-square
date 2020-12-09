using Vendr.Core.Web.PaymentProviders;

namespace Vendr.Contrib.PaymentProviders.Square
{
    public class SquareSettings
    {
        [PaymentProviderSetting(Name = "Continue URL", Description = "The URL to continue to after this provider has done processing. eg: /continue/")]
        public string ContinueUrl { get; set; }

        [PaymentProviderSetting(Name = "Location Id", Description = "The ID of the business location to associate the checkout with. The default is LTN1NC5CCYASX")]
        public string LocationId { get; set; }

        [PaymentProviderSetting(Name = "Sandbox Access Token", Description = "Enter the access token for the Sandbox environment")]
        public string SandboxAccessToken { get; set; }

        [PaymentProviderSetting(Name = "Sandbox Webhook Signing Secret", Description = "Enter the webhook signing secret for the Sandbox environment")]
        public string SandboxWebhookSigningSecret { get; set; }

        [PaymentProviderSetting(Name = "Live Access Token", Description = "Enter the access token for the Live environment")]
        public string LiveAccessToken { get; set; }

        [PaymentProviderSetting(Name = "Live Webhook Signing Secret", Description = "Enter the webhook signing secret for the Live environment")]
        public string LiveWebhookSigningSecret { get; set; }

        [PaymentProviderSetting(Name = "Sandbox Mode", Description = "Set whether to process payments in Sandbox mode")]
        public bool SandboxMode { get; set; }
    }
}
