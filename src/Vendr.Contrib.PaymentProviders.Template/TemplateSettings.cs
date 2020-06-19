using Vendr.Core.Web.PaymentProviders;

namespace Vendr.Contrib.PaymentProviders.Template
{
    public class TemplateSettings
    {
        [PaymentProviderSetting(Name = "Continue URL", Description = "The URL to continue to after this provider has done processing. eg: /continue/")]
        public string ContinueUrl { get; set; }
    }
}
