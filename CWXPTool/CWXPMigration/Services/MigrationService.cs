using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace CWXPMigration.Services
{
    public class MigrationService
    {
        public SitecoreGraphQLClient SitecoreGraphQLClient { get; set; }
        public MigrationService() { 
            this.SitecoreGraphQLClient = new SitecoreGraphQLClient();
        }
    }
}