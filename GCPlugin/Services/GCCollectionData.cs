using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GCPlugin.Services
{
    public sealed class GCCollectionData
    {
        public required string Id { get; init; }

        public DateTime Timestamp { get; init; }
        public required GCReason Reason { get; init; }
        public required int Generation { get; init; }
        public required GCType Type { get; init; }

        
        public double StartTime { get; set; }
        public double EndTime { get; set; }
        public double Duration => EndTime == 0 ? -1 : EndTime - StartTime;
    }
}
