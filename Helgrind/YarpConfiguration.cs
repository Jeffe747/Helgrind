using System.Net;

namespace Helgrind
{
    public class YarpConfiguration
    {
        public static Boolean AcceptOnlyCloudflareNetwork => true;

        public static List<IPNetwork> CloudflareNetworks => new List<IPNetwork>()
        {
            IPNetwork.Parse("103.21.244.0/22"),
            IPNetwork.Parse("103.22.200.0/22"),
            IPNetwork.Parse("103.31.4.0/22"),
            IPNetwork.Parse("104.16.0.0/13"),
            IPNetwork.Parse("104.24.0.0/14"),
            IPNetwork.Parse("108.162.192.0/18"),
            IPNetwork.Parse("131.0.72.0/22"),
            IPNetwork.Parse("141.101.64.0/18"),
            IPNetwork.Parse("162.158.0.0/15"),
            IPNetwork.Parse("172.64.0.0/13"),
            IPNetwork.Parse("173.245.48.0/20"),
            IPNetwork.Parse("188.114.96.0/20"),
            IPNetwork.Parse("190.93.240.0/20"),
            IPNetwork.Parse("197.234.240.0/22"),
            IPNetwork.Parse("198.41.128.0/17")
        };
    }
}
