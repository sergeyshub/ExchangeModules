namespace DataModels
{
    public class PaymentInstructions
    {
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public string BeneficiaryName { get; set; }
        public string BeneficiaryCountry { get; set; }
        public string BeneficiaryZip { get; set; }
        public string BeneficiaryCity { get; set; }
        public string BeneficiaryState { get; set; }
        public string BeneficiaryStreet1 { get; set; }
        public string BeneficiaryStreet2 { get; set; }
        public string BankName { get; set; }
        public string BankCountry { get; set; }
        public string BankZip { get; set; }
        public string BankCity { get; set; }
        public string BankState { get; set; }
        public string BankStreet1 { get; set; }
        public string BankStreet2 { get; set; }
        public string SwiftCode { get; set; }
        public string RoutingNumber { get; set; }
        public string AccountNumber { get; set; }
        public string Reference { get; set; }
    }
}
