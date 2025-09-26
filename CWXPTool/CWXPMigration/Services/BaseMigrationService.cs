using CWXPMigration.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CWXPMigration.Services
{
    public class BaseMigrationService
    {
        public ISitecoreGraphQLClient SitecoreGraphQLClient { get; set; }       
        public BaseMigrationService(ISitecoreGraphQLClient sitecoreGraphQLClient)
        {
            this.SitecoreGraphQLClient = sitecoreGraphQLClient;
        }             
    }
}
