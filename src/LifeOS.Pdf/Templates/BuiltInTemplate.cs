namespace LifeOS.Pdf.Templates
{
    /// <summary>The templates that ship with the library.</summary>
    public enum BuiltInTemplate
    {
        /// <summary>Invoice (faktur): seller, customer, line items, discount, tax, bank details.</summary>
        Invoice,

        /// <summary>Receipt (kwitansi) with the amount in words and a signature area. A5 landscape.</summary>
        Receipt,

        /// <summary>Payslip (slip gaji): earnings, deductions, take-home pay.</summary>
        Payslip,

        /// <summary>Monthly finance report: totals, saving rate, spending by category vs budget, transactions.</summary>
        MonthlyReport,

        /// <summary>Certificate (sertifikat) with up to three signers. A4 landscape.</summary>
        Certificate,
    }
}
