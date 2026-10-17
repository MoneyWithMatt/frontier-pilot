using System.Globalization;

namespace FrontierAirportBuilder.Core;

public static class DollarDisplay
{
    public static string Format(decimal amount) => "$" + amount.ToString(amount == decimal.Truncate(amount) ? "N0" : "N2", CultureInfo.InvariantCulture);
}

public sealed record PilotWage(string FlightId, string EmployerId, string EmployerName, long AirborneTicks, decimal HourlyRate)
{
    public decimal Amount => decimal.Round(AirborneTicks / (decimal)TimeSpan.TicksPerHour * HourlyRate, 2, MidpointRounding.AwayFromZero);
    public PilotWage EnsureValid()
    {
        EconomyValidation.Id(FlightId,nameof(FlightId)); EconomyValidation.Id(EmployerId,nameof(EmployerId));
        EconomyValidation.Text(EmployerName,nameof(EmployerName),160);
        if(AirborneTicks<0 || HourlyRate<=0)throw new InvalidDataException("Invalid pilot wage evidence.");
        return this;
    }
    public string Summary => $"{EmployerName} — Pilot wages · {TimeSpan.FromTicks(AirborneTicks).TotalMinutes:0.##} min @ {DollarDisplay.Format(HourlyRate)}/hr · {DollarDisplay.Format(Amount)}";
}

internal static class PilotWagePayments
{
    public static FinancialTransaction? Create(EconomyWorld world, EconomyJob job, TransportRequirement requirement, VerifiedDeliveryEvidence evidence)
    {
        var employer=world.Businesses.Single(x=>x.Id==job.EmployerBusinessId);
        var wage=evidence.Wage;
        if(employer.PilotHourlyRate is null && wage is null)
            return new(EconomyIds.Derive("payment-",job.Id),FinancialTransactionKind.JobPayment,job.Id,evidence.Id,
                requirement.FundingBusinessId,job.PlayerId,requirement.PaymentAmount,requirement.CurrencyCode,evidence.VerifiedUtc);
        if(wage is null || wage.EnsureValid().EmployerId!=job.EmployerBusinessId || wage.HourlyRate!=employer.PilotHourlyRate)
            throw new InvalidOperationException("Employed delivery requires verified airborne wage evidence.");
        var prior=world.FinancialLedger.SingleOrDefault(x=>x.Kind==FinancialTransactionKind.PilotWage && x.Wage?.FlightId==wage.FlightId);
        if(prior is not null)
        {
            if(prior.Wage!=wage||prior.ToEntityId!=job.PlayerId||prior.CurrencyCode!=requirement.CurrencyCode)
                throw new InvalidDataException("Shared flight wage evidence differs from its payment.");
            return null;
        }
        return new(EconomyIds.Derive("wage-",wage.FlightId),FinancialTransactionKind.PilotWage,job.Id,evidence.Id,
            wage.EmployerId,job.PlayerId,wage.Amount,requirement.CurrencyCode,evidence.VerifiedUtc){Wage=wage};
    }
}
