namespace CWXPMigration
{
    public class XMC_Template_Constants
    {
        //Component Datasource Templates
        public const string RTE = "{0EFFE34A-636F-4288-BA3B-0AF056AAD42B}";
        public const string In_Page_Banner = "{8BF22068-7CA9-4768-AB54-65872A7A79DD}";
        public const string Data = "{1C82E550-EBCD-4E5D-8ABD-D50D0809541E}";
        public const string Text_Media = "{0EE2E1D9-DDD5-471A-9BE3-0F39AD0FC4E2}";
        public const string General_Header = "{58DE8B66-C714-4BB3-80C1-A0FD015A056D}";
        public const string Generic_Link = "{B7E9466D-8242-4EFC-A137-C21C9E181ECB}";
        public const string Publication_Info = "{34702083-3088-4312-8546-1A90D8AE9FD6}";
        public const string SideNav = "{A3DC84B7-CDF1-468C-92EF-C33DC4311075}";
        public const string SideNavSection = "{72BB023A-CE3E-4523-B4AA-16E54561D8D4}";
    }

    public static class XMC_Page_Template_Constants
    {
        // Individual constants
        public const string Condition_Treatment = "{4D49E913-37B3-4946-9372-7BB0DCA63BC9}";
        public const string Teaching_Sheets = "{39EBED3F-5965-4A68-9A4C-45E7D29043C8}";
        public const string General2 = "{2400C94A-5BB1-4F69-85CC-3AD185DC4BCA}";

        public const string PrimaryCare = "{6274DC7B-91E7-4243-B5DA-96604F2EBBEA}";
        public const string UrgentCare = "{7A4E0C65-C397-4E65-A941-7CF879C0B727}";
        public const string SpecialtyCare = "{BB35FDA8-7E1F-48DC-A556-FA8FD89F96C2}";
        public const string Hospital = "{CE453EDE-ED09-4928-80B0-143556AA52E8}";
        public const string LocationPage = "{1B371DE2-704C-4D43-A94B-FC04B95DC6B8}";

        // Grouped arrays from constants
        public static readonly string[] Side_Nav_Templates = new[]
        {
            Condition_Treatment
        };

        public static readonly string[] Location_Pages = new[]
        {
            PrimaryCare,
            UrgentCare,
            SpecialtyCare,
            Hospital,
            LocationPage
        };
    }


    public class XP_Page_Template_Constants
    {
        public const string XP_BASE_PAGE_TEMPLATEID = "{8F3DE639-B021-42CE-AE90-0E07BECB6B03}";
    }

    public static class XP_RenderingName_Constants
    {
        public const string RichText = "RichText";
        public const string PageHeadline = "PageHeadline";
        public const string Headline = "Headline";
        public const string RichText_Plain = "RichText Plain";
        public const string Publication_Footer = "Publication Footer";
        public const string Video_Main_Body = "Video Main Body";
        public const string Multi_Button_Callout = "Multi Button Callout";

        public static readonly string[] XP_RENDERING_NAMES =
        {
            PageHeadline,
            Headline,
            RichText,
            RichText_Plain,
            Publication_Footer,
            Video_Main_Body,
            Multi_Button_Callout
        };
    }


    public static class Constants
    {
        public const string AuthUrl = "https://auth.sitecorecloud.io/oauth/token";
        public const string Audience = "https://api.sitecorecloud.io";

        public const string SITECORE_XMC_PREFIX = "/sitecore/content/CW/childrens/Home/";

        public const string OFFICE_HOURS_FOLDER_TEMPLATEID = "{87409A28-AD55-4E80-B814-DAE11AF579B0}";
        public const string PHONE_HOURS_FOLDER_TEMPLATEID = "{BE526692-1363-4CA1-905B-4BDD7E72244E}";

        public const string SITECORE_XP_PRFIX = "/sitecore/content/CHW/Home/";
        public const string XP_MIGRATION_LOG_JSON_PATH = "F:\\Migration\\CWXPMigrationContent.json";
    }
}