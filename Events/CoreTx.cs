using System;
using System.Collections.Generic;
using DataAccess;

namespace Events
{
    public class CoreTx
    {
        public const byte TYPE_PAYMENT = 1;
        public const byte TYPE_EXCHANGE = 2;

        public string CoreName;
        public Asset Asset;
        public byte Type;
        public decimal Amount;
        public decimal Fee;
        public string ExternalId;
        public DateTime TimeExecuted;
    }
}
