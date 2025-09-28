using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GPM_driver.Models
{
    public class IpFighterResult
    {
        public string Isp { get; set; }
        public string Blacklist { get; set; }
        public string Proxy { get; set; }
        public string WebRTC { get; set; }
        public string Score { get; set; }
        public string City { get; set; }
        public string Country { get; set; }
        public string Hostname { get; set; }
        public string DNS { get; set; }
        public string BlacklistDetails { get; set; }
        public string BlacklistServers { get; set; }
    }
}