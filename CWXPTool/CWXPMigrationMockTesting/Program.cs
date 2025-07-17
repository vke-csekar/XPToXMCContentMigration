using CWXPMigration;
using CWXPMigration.Services;
using System.Threading.Tasks;

namespace CWXPMigrationMockTesting
{
    public class Program
    {
        static void Main(string[] args)
        {
            // Call the async method and wait synchronously
            RunAsync().GetAwaiter().GetResult();
        }

        static async Task RunAsync()
        {
            SideNavMigrationService sideNavMigrationService = new SideNavMigrationService();
            var authResponse = await AuthHelper.GetAuthTokenAsync();
            string rteValue = "<p><strong><img src=\"~/media/1CE624C19C2D41209C177D496E3A84FB.ashx\" alt=\"menstrcy\" style=\"vertical-align: middle;\" /></strong></p>\r\n<h2 class=\"chw-h2\">What is ovulation?</h2>\r\n<p>When a young woman reaches puberty, she begins to ovulate - a process in which a mature egg cell (also called an ovum), ready for fertilization by a sperm cell, is released from one of the ovaries (two female reproductive organs located in the pelvis). If the egg is fertilized by a sperm cell as it travels down the fallopian tube, then pregnancy occurs and it becomes attached to the lining of the uterus until the placenta (an organ, shaped like a flat cake, that only grows during pregnancy and provides a metabolic interchange between the fetus and mother) develops. If the egg does not become fertilized as it travels down the fallopian tube on its way to the uterus, the endometrium (lining of the uterus) is shed and passes through the vagina (the passageway through which fluid passes out of the body during menstrual periods; also called the birth canal), a process called <strong>menstruation.</strong></p>\r\n<p>As the average menstrual cycle lasts 28 days (starting with the first day of one period and ending with the first day of the next menstrual period), most women ovulate on day 14. At this time, some women experience minor discomfort in their lower abdomen, spotting, and/or bleeding, while others do not experience any symptoms at all.</p>\r\n<p>A woman is generally most fertile (able to become pregnant) a few days before, during, and after ovulation.</p>\r\n<h2 class=\"chw-h2\">What is menstruation?</h2>\r\n<p>Menstruation is one part of a woman's menstrual cycle which includes the shedding of the endometrium (lining of the uterus) that occurs throughout a woman's reproductive life. With each monthly (on average) menstrual cycle, the endometrium prepares itself to nourish a fetus, as increased levels of estrogen and progesterone help to thicken its walls. If fertilization does not occur, the endometrium, coupled with blood and mucus from the vagina and cervix (the lower, narrow part of the uterus located between the bladder and the rectum) make up the menstrual flow (also called menses) that leaves the body through the vagina.</p>\r\n<h2 class=\"chw-h2\">When does menstruation begin?</h2>\r\n<p>On average, menarche (a young woman's first menstrual period) occurs between the ages of 12 and 14 years old - generally two years after her breast budding, and, in most cases, not long after the onset of pubic hair and underarm hair. Stress, various types of strenuous exercise, and diet can affect the onset of menstruation and the regularity of the menstrual cycle.</p>\r\n<p>The American College of Obstetricians and Gynecologists (ACOG) recommends that a young woman consult her physician if she has not started to menstruate by the age of 16, and/or if she has not begun to develop breast buds, pubic hair, and/or underarm hair by the age of 13 or 14.</p>\r\n<h2 class=\"chw-h2\">How long is a menstrual cycle?</h2>\r\n<p>For menstruating women, an average menstrual cycle lasts 28 days - starting with the first day of the last period (which, on average, lasts 6 days, with some women having a very light flow and others having a very heavy flow) and ending with the first day of the next menstrual period. However, the length of women's cycles varies, particularly for the first one to two years after menarche (a young woman's first menstrual period). Women may have cycles as short as 23 days, or as long as 35 days. However, anything that deviates from this range is considered abnormal and may require medical attention.</p>";
            var sideNavContents = RichTextSplitter.SplitByH2(rteValue);
            await sideNavMigrationService.CreateSideNavContentItemsAsync(sideNavContents,
                "{F2E50DF5-F302-437E-8718-E724A42923AA}", "/sitecore/content/CW/childrens/Home/Find Care/Adolescent Health Medicine/Menstrual Cycle An Overview", authResponse.AccessToken);
        }
    }
}
