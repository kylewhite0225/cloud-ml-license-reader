using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TicketProcessingFunction
{
    public class TrafficViolation
    {
        public string? Plate { get; set; }

        public string? Make { get; set; }

        public string? Model { get; set; }

        public string? Color { get; set; }

        public string? Name { get; set; }

        public string? Contact { get; set; }

        public string? PreferredLanguage { get; set; }

        public string? ViolationLocation { get; set; }

        public string? ViolationType { get; set; }

        public int? TicketAmount { get; set; }

        public string? Date { get; set; }
    }
}
