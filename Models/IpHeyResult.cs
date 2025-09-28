using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GPM_driver.Models
{
    public class IpHeyResult
    {
        public string Status { get; set; } = "";
        public string Browser { get; set; } = "";
        public string Location { get; set; } = "";
        public string Ip { get; set; } = "";
        public string Hardware { get; set; } = "";
        public string Software { get; set; } = "";
    }
}