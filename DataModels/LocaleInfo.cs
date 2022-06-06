using DataAccess;

namespace DataModels
{
    public class LocaleInfo
    {
        public string CountryCode { get; set; }
        public string LanguageCode { get; set; }
        public string LocaleString { get; set; }
        public TimeZone TimeZone { get; set; }
    }
}
