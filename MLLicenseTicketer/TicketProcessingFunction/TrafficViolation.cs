using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TicketProcessingFunction
{
    public class TrafficViolation
    {
        public string? Vehicle { get; set; }

        public string? LicensePlate { get; set; }

        public string? Date { get; set; }

        public string? Address { get; set; }

        public string? ViolationType { get; set; }

        public int? ViolationAmount { get; set; }

        public string? Contact { get; set; }

        public string? PreferredLanguage { get; set; }
    }
}
